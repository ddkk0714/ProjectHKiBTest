using System;
using System.Collections.Generic;
using UnityEngine;

// ────────────────────────────────────────────────────────────────
// 루트파인딩 시스템의 중앙 모듈 — 공용 상태의 단일 소유자.
//
// 시스템 전체 구조:
//   MapGraph          (Logic)  : 지도 원본 데이터(JSON) 보관·조회 — 읽기 전용
//   MapPathFinder     (Logic)  : 경로 탐색 알고리즘(BFS/Dijkstra) — 무상태 정적 클래스
//   DifficultyCalculator(Logic): 기획서 난이도 공식 — 무상태 정적 클래스
//   RouteModule       (Module) : ★ 공용 상태·기획 규칙의 단일 소유자  ← 이 클래스
//   RouteProgressState(Module) : 맵 진행 상태(방문/클리어/단서) — RouteModule이 소유
//   WaveCombatBridge  (Manager): 맵 전투 실행 창구 (2026-07-14 이전 — 원래는 연결 전투) — 시작/종료 이벤트만 발행
//   RouteSpawnManager (Manager): 침대 임시 스폰 포인트
//   MapViewer         (MapView): 지도 UI — 모듈의 상태를 읽어 그리기만 한다
//
// 소유하는 공용 상태:
//   1) Progress      : 방문한 맵, 클리어한 연결, 단서, 이벤트 플래그
//   2) 장착 장비      : 난이도 계산·통과 가능 판정의 기준 (기획서: "출발 전 입력한 장비만 참조")
//   3) 선택 경로·이동 : 선택된 경로, 현재 노드 인덱스, 이동 중 여부
//   4) 경로 탐색 옵션 : 단서 없는 맵 회피 여부(avoidNoClueNodes) — 장비와 마찬가지로 출발 전에만 변경 가능
//
// 강제하는 기획 규칙:
//   - 출발(StartTravel) 후에는 장비·경로 변경 불가, 지도 열람 불가(CanOpenMap)
//   - 통과 불가 구간(IsBlocked)이 포함된 경로는 선택 불가 — AlternativePath를 대신 선택해야 함
// ────────────────────────────────────────────────────────────────
// IEventSaveProvider(Save/New_Save, RouteFinding 폴더 밖) 구현체 — RouteProgressState가 아니라
// 이 클래스가 구현한다. RouteProgressState.SetEventFlag(mapGuid, eventKey)(게임플레이용, 스토리 이벤트
// 발생 기록)와 IEventSaveProvider.SetEventFlag(id, value)(세이브용, 이름은 같지만 완전히 다른 의미)가
// 한 클래스에 같이 있으면 오버로드는 되지만("string,string" vs "string,bool") 헷갈리기 쉬워서, 이미
// "공용 상태의 단일 소유자"인 이 클래스가 대신 얇게 위임한다.
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 전투/퀘스트/UI 등 RouteFinding 밖의 다른 시스템은 이 클래스만 참조하면
// 루트파인딩 상태를 읽고 조작할 수 있다. 씬에 미리 배치할 필요 없이 RouteModule.Instance로 항상
// 접근 가능(첫 접근 시 자동 생성). 아래는 실제 구현부(이 파일 본문)에 있는 멤버를 용도별로 정리한 것 —
// 자세한 동작/제약은 각 멤버 바로 위 주석 참고.
//
// ▸ 장착 장비 (전투 상성/난이도 계산 기준)
//   RouteModule.Instance.EquippedGears            : 현재 장착 장비 목록(읽기 전용)
//   RouteModule.Instance.IsGearEquipped(gear)      : 특정 장비 장착 여부
//   RouteModule.Instance.ToggleGear(gear)          : 장비 착탈(이동 중엔 거부, bool 반환)
//   ※ 이동 중(IsTraveling)에는 변경 불가 — 실패 시 false 반환 + Debug.LogWarning
//
// ▸ 이동 상태 조회 (전투/연출 시스템이 "지금 뭘 해야 하는지" 판단할 때 사용)
//   RouteModule.Instance.IsTraveling               : 이동 중 여부
//   RouteModule.Instance.CanOpenMap                 : 지도 열람 가능 여부(=이동 중 아님)
//   RouteModule.Instance.CurrentNode                : 이동 중 현재 위치한 노드(이동 중 아니면 null)
//   RouteModule.Instance.CurrentLocation             : 이동 여부 무관하게 항상 유효한 "현재 위치"
//   RouteModule.Instance.GetCurrentTargetNode()      : 다음 전투 대상 맵(=다음에 도달해야 할 노드)
//   RouteModule.Instance.SelectedRoute               : 현재 선택된 경로(PathResult)
//
// ▸ 이동 진행 이벤트 구독 (Note/UI/연출 시스템이 이동 흐름에 맞춰 반응할 때)
//   OnTravelStarted            : 출발 시
//   OnNodeArrived(MapNodeData) : 전투 통과 후 새 노드 도달 시
//   OnTravelEnded(bool)        : 이동 종료 시(true=목적지 도달, false=중단)
//   OnRouteSelected(PathResult): 경로가 선택(커밋)될 때
//
// ▸ 전투 시스템 연동 (핵심 훅 — 웨이브 전투 실제 구현 시 사용)
//   전투 시작/종료 "사실"은 WaveCombatBridge(Manager/WaveCombatBridge.cs)가 이벤트로 알리고,
//   그 결과로 맵을 영구 클리어 처리하거나 다음 노드로 진행하는 것은 이 클래스가 내부에서 자동 처리한다
//   (HandleCombatCompleted/HandleCombatFailed, private). 전투 시스템은 WaveCombatBridge의 API만
//   신경 쓰면 되고, RouteModule을 직접 호출할 필요는 없다 — 상세는 WaveCombatBridge.cs 상단 참고.
//
// ▸ 사망 처리 연동
//   RouteModule.Instance.RevertToLastSave(lastSaved) : 미세이브 맵 진도 손실(마지막 세이브로 복귀)
//   직접 호출하지 말고 Manager/DeathHandler.cs의 HandleDeath()를 통해 호출할 것(스폰 복귀와 함께
//   묶어서 처리해야 함) — 상세는 DeathHandler.cs 상단 참고.
//
// ▸ 스토리 이벤트 → 단서 공개 연동 (퀘스트/대화/전투 시스템이 호출)
//   RouteModule.Instance.Progress.SetEventFlag(mapGuid, eventKey) : 특정 맵에서 이벤트 발생 기록
//   → 해당 이벤트 키를 requiredEventKey로 참조하는 ClueData가 있으면 자동으로 단서 획득 재검사됨.
//   상세는 RouteProgressState.cs 상단 참고.
//
// ▸ 사용 예시 — 전투 승리 시 특정 적을 죽였다는 이벤트를 기록해 단서를 공개하고 싶을 때:
//     RouteModule.Instance.Progress.SetEventFlag(currentMapGuid, "kill_boss01");
//
// ▸ 세이브 연동은 RouteModule이 IEventSaveProvider를 구현하는 방식으로 이미 SaveModule과
//   자동 연결되어 있다 — 이 인터페이스(EventFlags/SetEventFlag/Passages/SetPassage/ResetForLoad)는
//   세이브 시스템 내부용이고, 다른 게임플레이 모듈이 직접 호출할 일은 없다.
// ════════════════════════════════════════════════════════════════
public class RouteModule : MonoBehaviour, IEventSaveProvider
{
    private static RouteModule _instance;
    private static bool _isQuitting; // 종료 중에는 다른 오브젝트의 OnDestroy가 Instance를 건드려도 재생성하지 않는다.

    // 씬에 미리 배치해도 되고, 배치하지 않았다면 첫 접근 시 자동 생성된다.
    public static RouteModule Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying && !_isQuitting)
            {
                _instance = FindObjectOfType<RouteModule>();
                if (_instance == null)
                    _instance = new GameObject(nameof(RouteModule)).AddComponent<RouteModule>();
            }
            return _instance;
        }
    }

    // 플레이 모드 종료(에디터) / 앱 종료 시 호출된다 — OnDestroy들보다 먼저 실행되는 것이 보장되므로,
    // 이후 다른 오브젝트의 OnDestroy에서 Instance에 접근해도 새 GameObject를 만들지 않도록 막는다
    // (안 막으면 CodexModule.OnDestroy 같은 곳에서 이미 파괴된 RouteModule을 새로 스폰해버려
    // "Some objects were not cleaned up when closing the scene" 경고가 뜬다).
    private void OnApplicationQuit() => _isQuitting = true;

    // ─── 진행 상태 (방문 / 클리어 / 단서 / 이벤트) ────────────────
    private RouteProgressState _progress;

    // MapGraph가 JSON을 로드한 뒤(Awake) 첫 접근 시점에 생성된다.
    // MapGraph가 아직 없으면 생성하지 않는다 — 빈 그래프 기준의 잘못된 상태가
    // 캐시로 굳는 것을 막기 위함. (씬에 MapGraph를 배치해야 한다)
    public RouteProgressState Progress
    {
        get
        {
            if (_progress == null)
            {
                if (MapGraph.Instance == null)
                {
                    Debug.LogError("[RouteModule] MapGraph가 씬에 없습니다 — 진행 상태를 생성할 수 없습니다.");
                    return null;
                }
                _progress = new RouteProgressState(MapGraph.Instance);
            }
            return _progress;
        }
    }

    // ─── 장착 장비 ───────────────────────────────────────────────
    private readonly List<EmotionColor> _equippedGears = new();
    private EmotionColor[] _gearArrayCache; // 장비 변경 시 무효화되는 스냅샷

    public IReadOnlyList<EmotionColor> EquippedGears => _equippedGears;
    public bool IsGearEquipped(EmotionColor gear) => _equippedGears.Contains(gear);

    // 계산 로직(DifficultyCalculator / IsPassableWith / MapPathFinder)에 넘기는 배열.
    // 장비 미입력이면 null → "상성 미적용, 적 기본 수치만 표시" 모드로 동작한다 (기획서 규칙).
    public EmotionColor[] EquippedGearArray =>
        _equippedGears.Count == 0 ? null : _gearArrayCache ??= _equippedGears.ToArray();

    // 장비 착탈. 이동 중에는 변경할 수 없다 (기획서: 출발 후 장비 변경 불가).
    // 반환값: 실제로 변경되었는지 여부 (UI가 갱신 필요 여부를 판단하는 데 사용)
    public bool ToggleGear(EmotionColor gear)
    {
        if (_isTraveling)
        {
            Debug.LogWarning("[RouteModule] 이동 중에는 장비를 변경할 수 없습니다.");
            return false;
        }

        if (!_equippedGears.Remove(gear))
            _equippedGears.Add(gear);
        _gearArrayCache = null;
        return true;
    }

    // 세이브 로드 전용 — ToggleGear()의 이동 중 잠금 없이 곧장 대입한다(로드는 이동 중이 아닐 때만
    // 일어나는 것이 전제이기도 하고, 설령 그렇더라도 로드가 우선이다).
    public void ImportEquippedGears(List<EmotionColor> gears)
    {
        _equippedGears.Clear();
        if (gears != null) _equippedGears.AddRange(gears);
        _gearArrayCache = null;
    }

    // ─── 경로 탐색 옵션 ───────────────────────────────────────────
    // 단서 없는 맵을 경유하는 것을 최대한 피할지 여부 (기본값: 허용).
    // 장비와 마찬가지로 출발 전에만 바꿀 수 있다 — 이동 중 탐색 조건이 바뀌면 안 되므로.
    private bool _avoidNoClueNodes;

    public bool AvoidNoClueNodes => _avoidNoClueNodes;

    // 반환값: 실제로 변경되었는지 여부 (UI가 갱신 필요 여부를 판단하는 데 사용)
    public bool SetAvoidNoClueNodes(bool value)
    {
        if (_isTraveling)
        {
            Debug.LogWarning("[RouteModule] 이동 중에는 경로 탐색 옵션을 변경할 수 없습니다.");
            return false;
        }
        if (_avoidNoClueNodes == value) return false;
        _avoidNoClueNodes = value;
        return true;
    }

    // ─── 선택 경로·이동 진행 ─────────────────────────────────────
    private PathResult _selectedRoute;
    private int _currentNodeIndex;
    private bool _isTraveling;

    public PathResult SelectedRoute => _selectedRoute;
    public bool IsTraveling => _isTraveling;

    // 이동 중에는 지도를 열 수 없다 (기획서: 지도는 시작지점에서만 확인 가능)
    public bool CanOpenMap => !_isTraveling;

    public MapNodeData CurrentNode =>
        _isTraveling && _selectedRoute != null && _selectedRoute.IsValid
            ? _selectedRoute.Nodes[_currentNodeIndex]
            : null;

    // 이동 중이 아닐 때도 유지되는 "현재 위치" — Note의 다중 목적지 계획(4단계)이 다음 구간의
    // 시작점을 잡는 데 사용한다(NoteSystem_기획서.md "현재 위치 → 다음 목적지" 참고). 이동 중엔
    // 매 노드 도달마다 갱신되고, 이동 종료(도달/중단) 후에는 마지막으로 도달한 노드에 고정된다.
    private MapNodeData _currentLocation;
    public MapNodeData CurrentLocation => _currentLocation ??= MapGraph.Instance?.StartNode;

    // 현재 위치를 직접 대입 전용 — 세이브 로드(SaveModule.LoadEvents) 외에 사망 복귀
    // (DeathHandler.HandleDeath)도 이 메서드로 침대/집 노드를 반영한다. guid가 비어있거나
    // 그래프에서 못 찾으면 null로 되돌린다 — CurrentLocation 프로퍼티가 null일 때 자동으로
    // 시작 노드(집)로 폴백하므로 별도 기본값 처리가 불필요하다.
    public void ImportCurrentLocation(string guid) =>
        _currentLocation = !string.IsNullOrEmpty(guid) ? MapGraph.Instance?.GetNode(guid) : null;

    // 이동 진행 알림 — 확장 시스템(Note, UI 등)이 구독한다.
    public event Action OnTravelStarted;
    public event Action<MapNodeData> OnNodeArrived; // 맵 전투 통과 후 새 노드 도달

    // 이동 종료 알림 — completed=true면 목적지 정상 도달, false면 AbortTravel()로 인한 중단.
    // (2026-07-14 — Note의 자동 순차 실행이 성공/중단을 구분해야 해서 bool 인자를 추가했다.
    //  이전엔 Action(무인자)이었고 구독자가 없었으므로 시그니처 변경에 따른 별도 마이그레이션은 불필요.)
    public event Action<bool> OnTravelEnded;

    // 경로 선택 변경 알림 (2026-07-14 신설) — NoteModule의 "경로 연동 자동 편입"(규칙 1)이 구독한다.
    public event Action<PathResult> OnRouteSelected;

    // 출발 전 경로 선택 (지도 화면에서 호출).
    // 통과 불가 맵을 포함한 경로(IsBlocked)는 선택할 수 없다 — AlternativePath를 대신 선택해야 한다.
    public bool SelectRoute(PathResult route)
    {
        if (_isTraveling)
        {
            Debug.LogWarning("[RouteModule] 이동 중에는 경로를 변경할 수 없습니다.");
            return false;
        }
        if (route != null && route.IsBlocked)
        {
            Debug.LogWarning("[RouteModule] 현재 장비로 통과할 수 없는 구간이 포함된 경로는 선택할 수 없습니다.");
            return false;
        }
        _selectedRoute = route;
        Debug.Log($"[RouteModule] 경로 선택 완료 — {route?.Nodes?.Count ?? 0}개 노드");
        OnRouteSelected?.Invoke(route);
        return true;
    }

    // 세이브 로드 전용 — 마지막으로 선택했던 경로(Nodes 순서, SaveSlotData.selectedRouteNodeGuids)를
    // 복원한다. SelectRoute()와 달리 유효성 검사·이벤트 발행을 하지 않고 조용히 대입만 한다 —
    // 로드 시점의 경로는 저장 시점에 이미 SelectRoute()를 통과했던(=유효했던) 것이 보장되고,
    // OnRouteSelected를 또 발행하면 NoteModule의 RouteLinked 재계산이 같은 시점에 복원되는
    // NoteModule.ImportFrom과 순서 다툼을 일으킬 수 있다. Connections/TotalDifficulty 등 파생
    // 필드는 채우지 않는다 — 노트 좌측 그래프(NoteRouteGraphView)는 Nodes만 사용하고, 지도에서
    // 다시 열람하면 MapPathFinder가 새로 계산하므로 여기서 채울 필요가 없다.
    public void ImportSelectedRoute(List<string> nodeGuids)
    {
        if (nodeGuids == null || nodeGuids.Count < 2 || MapGraph.Instance == null)
        {
            _selectedRoute = null;
            return;
        }

        var nodes = new List<MapNodeData>(nodeGuids.Count);
        foreach (var guid in nodeGuids)
        {
            var node = MapGraph.Instance.GetNode(guid);
            if (node == null) { _selectedRoute = null; return; } // 그래프 데이터가 바뀌어 guid가 더는 유효하지 않음
            nodes.Add(node);
        }
        _selectedRoute = new PathResult { Nodes = nodes };
    }

    // 출발 — 이후 지도 열람·장비·경로 변경이 모두 잠긴다.
    public bool StartTravel()
    {
        if (_selectedRoute == null || !_selectedRoute.IsValid)
        {
            Debug.LogWarning("[RouteModule] 선택된 경로가 없습니다.");
            return false;
        }
        _isTraveling = true;
        _currentNodeIndex = 0;
        _currentLocation = _selectedRoute.Nodes[0];
        TrySubscribeCombatBridge(); // 전투 결과를 받아 진행해야 하므로 출발 시점에 반드시 연결
        Debug.Log($"[RouteModule] 출발 → {_selectedRoute.Nodes[0].nodeName}");
        OnTravelStarted?.Invoke();
        return true;
    }

    // 다음 전투 대상 맵 (= 지금부터 도달해야 할 다음 노드). 2026-07-14 완료 — 원래는 "다음 연결"(GetCurrentConnection).
    public MapNodeData GetCurrentTargetNode()
    {
        if (!_isTraveling || _selectedRoute == null || _currentNodeIndex + 1 >= _selectedRoute.Nodes.Count)
            return null;
        return _selectedRoute.Nodes[_currentNodeIndex + 1];
    }

    // 맵 전투 완료 후 다음 노드로 진행. 도달한 노드는 방문 처리(단서 자동 획득 포함)된다.
    public void AdvanceToNextNode()
    {
        if (!_isTraveling || _selectedRoute == null) return;
        if (_currentNodeIndex + 1 >= _selectedRoute.Nodes.Count) return;

        _currentNodeIndex++;

        var arrived = _selectedRoute.Nodes[_currentNodeIndex];
        _currentLocation = arrived;
        Progress.MarkNodeVisited(arrived);
        Debug.Log($"[RouteModule] 도달 → {arrived.nodeName}");
        OnNodeArrived?.Invoke(arrived);

        if (_currentNodeIndex >= _selectedRoute.Nodes.Count - 1)
        {
            _isTraveling = false;
            Debug.Log("[RouteModule] 목적지 도달!");
            OnTravelEnded?.Invoke(true);
        }
    }

    // 이동 중단 (전투 실패·사망 등). _currentLocation은 마지막으로 정상 도달한 노드에 그대로 남는다.
    public void AbortTravel()
    {
        if (!_isTraveling) return;
        _isTraveling = false;
        Debug.Log("[RouteModule] 이동 중단");
        OnTravelEnded?.Invoke(false);
    }

    // ─── 경로 탐색 (모듈 상태 기준 래퍼) ─────────────────────────
    // UI·다른 시스템은 장비 배열을 직접 다루지 말고 이 메서드를 사용한다.
    // 현재 장착 장비와 진행 상태(단서 공개 여부)가 자동으로 반영된다.

    public PathResult FindPath(MapNodeData start, MapNodeData destination, PathType pathType) =>
        MapPathFinder.FindPath(start, destination, pathType, MapGraph.Instance, Progress, EquippedGearArray, _avoidNoClueNodes);

    // 시작 지점(집)에서 destination까지의 경로 탐색.
    public PathResult FindPathFromStart(MapNodeData destination, PathType pathType)
    {
        var graph = MapGraph.Instance;
        if (graph == null || graph.StartNode == null || destination == null)
            return new PathResult();
        return MapPathFinder.FindPath(graph.StartNode, destination, pathType, graph, Progress, EquippedGearArray, _avoidNoClueNodes);
    }

    // ─── 세이브 연동 (IEventSaveProvider) ───────────────────────────
    // SaveModule.SaveEvents()/LoadEvents()가 이 프로퍼티·메서드만으로 루트파인딩 진행 상태를
    // 저장/복원한다 — RouteFinding 쪽은 SaveSlotData 타입을 몰라도 된다(RouteProgressState 참고).
    // Progress가 null(MapGraph 미배치)이면 빈 상태로 취급해 세이브/로드 파이프라인이 죽지 않게 한다.

    public Dictionary<string, bool> EventFlags => Progress?.BuildEventFlagsSnapshot() ?? new Dictionary<string, bool>();
    public void SetEventFlag(string id, bool value) => Progress?.ApplyEventFlag(id, value);

    public Dictionary<string, bool> Passages => Progress?.BuildPassagesSnapshot() ?? new Dictionary<string, bool>();
    public void SetPassage(string id, bool opened) => Progress?.ApplyPassage(id, opened);

    // 로드 시작 시 SaveModule.LoadEvents()가 항목을 하나씩 SetEventFlag/SetPassage로 넘기기 전에
    // 1회 호출한다 — 이걸 안 하면 이전 상태(또는 기본값)와 새로 로드되는 값이 섞인다.
    public void ResetForLoad() => Progress?.ResetToInitial();

    // ─── 사망 처리 ──────────────────────────────────────────────

    // 사망 시 호출 — 미세이브 맵 진도 손실 (마지막 세이브 상태로 복귀).
    // SaveModule을 거치지 않고 직접 SaveSlotData를 복원한다(사망 복귀는 사용자가 명시적으로 "불러오기"를
    // 누른 게 아니므로 SaveModule의 로드 스테이트머신을 다시 돌릴 필요가 없다 — 이미 메모리에 있는
    // 마지막 세이브 스냅샷을 그대로 반영). 이벤트 진행 진도(스토리)는 별도 시스템이 관리하므로
    // 여기서는 맵 진행(eventFlags/passages)만 되돌린다.
    public void RevertToLastSave(SaveSlotData lastSaved)
    {
        if (Progress == null) return;
        Progress.ResetToInitial();
        if (lastSaved == null) return;

        foreach (var f in lastSaved.eventFlags)
            Progress.ApplyEventFlag(f.id, f.value);
        foreach (var p in lastSaved.passages)
            Progress.ApplyPassage(p.id, p.opened);
    }

    // ─── 전투 연동 ───────────────────────────────────────────────
    // WaveCombatBridge는 전투의 시작/종료 "사실"만 이벤트로 알린다.
    // 그 결과로 일어나는 일(연결 영구 개방, 노드 진행, 이동 중단)은 모두 이 모듈의 책임이다.

    private bool _bridgeSubscribed;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[RouteModule] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start() => TrySubscribeCombatBridge();

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;

        if (_bridgeSubscribed && WaveCombatBridge.Instance != null)
        {
            WaveCombatBridge.Instance.OnCombatCompleted -= HandleCombatCompleted;
            WaveCombatBridge.Instance.OnCombatFailed -= HandleCombatFailed;
        }
    }

    private void TrySubscribeCombatBridge()
    {
        if (_bridgeSubscribed || WaveCombatBridge.Instance == null) return;
        WaveCombatBridge.Instance.OnCombatCompleted += HandleCombatCompleted;
        WaveCombatBridge.Instance.OnCombatFailed += HandleCombatFailed;
        _bridgeSubscribed = true;
    }

    private void HandleCombatCompleted(MapNodeData node)
    {
        // 전투 승리 → 맵 영구 클리어(기획서: 한 번 클리어하면 이후 전투 없이 재방문 가능) 후 다음 노드로.
        // 단, 일기장 세이브 전에 사망하면 RevertToLastSave로 클리어가 취소된다.
        Progress.MarkNodeCleared(node);
        AdvanceToNextNode();
    }

    private void HandleCombatFailed(MapNodeData node) => AbortTravel();
}
