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
//   WaveCombatBridge  (Manager): 연결 전투 실행 창구 — 시작/종료 이벤트만 발행
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
public class RouteModule : MonoBehaviour
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

    // 이동 진행 알림 — 확장 시스템(RouteNote, UI 등)이 구독한다.
    public event Action OnTravelStarted;
    public event Action<MapNodeData> OnNodeArrived; // 연결 전투 통과 후 새 노드 도달
    public event Action OnTravelEnded;              // 목적지 도달 또는 이동 중단

    // 출발 전 경로 선택 (지도 화면에서 호출).
    // 통과 불가 연결을 포함한 경로(IsBlocked)는 선택할 수 없다 — AlternativePath를 대신 선택해야 한다.
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
        return true;
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
        TrySubscribeCombatBridge(); // 전투 결과를 받아 진행해야 하므로 출발 시점에 반드시 연결
        Debug.Log($"[RouteModule] 출발 → {_selectedRoute.Nodes[0].nodeName}");
        OnTravelStarted?.Invoke();
        return true;
    }

    // 현재 통과해야 할 연결 (= 다음 전투 대상). 경로의 i번째 노드와 i+1번째 노드 사이 연결.
    public MapConnectionData GetCurrentConnection()
    {
        if (!_isTraveling || _selectedRoute == null || _currentNodeIndex >= _selectedRoute.Connections.Count)
            return null;
        return _selectedRoute.Connections[_currentNodeIndex];
    }

    // 연결 전투 완료 후 다음 노드로 진행. 도달한 노드는 방문 처리(단서 자동 획득 포함)된다.
    public void AdvanceToNextNode()
    {
        if (!_isTraveling || _selectedRoute == null) return;
        if (_currentNodeIndex + 1 >= _selectedRoute.Nodes.Count) return;

        _currentNodeIndex++;

        var arrived = _selectedRoute.Nodes[_currentNodeIndex];
        Progress.MarkNodeVisited(arrived);
        Debug.Log($"[RouteModule] 도달 → {arrived.nodeName}");
        OnNodeArrived?.Invoke(arrived);

        if (_currentNodeIndex >= _selectedRoute.Nodes.Count - 1)
        {
            _isTraveling = false;
            Debug.Log("[RouteModule] 목적지 도달!");
            OnTravelEnded?.Invoke();
        }
    }

    // 이동 중단 (전투 실패·사망 등)
    public void AbortTravel()
    {
        if (!_isTraveling) return;
        _isTraveling = false;
        Debug.Log("[RouteModule] 이동 중단");
        OnTravelEnded?.Invoke();
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

    // ─── 세이브 / 사망 처리 ──────────────────────────────────────

    // 일기장 세이브 시 호출 — 현재 진행 상태를 SaveSlotData에 기록한다.
    public void ExportToSaveData(SaveSlotData data) => Progress.ExportToSaveData(data);

    // 사망 시 호출 — 미세이브 맵 진도 손실 (마지막 세이브 상태로 복귀).
    // 이벤트 진행 진도(스토리)는 별도 시스템이 관리하므로 여기서는 맵 진행만 되돌린다.
    public void RevertToLastSave(SaveSlotData lastSaved) => Progress.ImportFromSaveData(lastSaved);

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

    private void HandleCombatCompleted(MapConnectionData connection)
    {
        // 전투 승리 → 연결 영구 개방(기획서: 한 번 통과하면 이후 전투 없이 이동 가능) 후 다음 노드로.
        // 단, 일기장 세이브 전에 사망하면 RevertToLastSave로 개방이 취소된다.
        Progress.MarkConnectionCleared(connection);
        AdvanceToNextNode();
    }

    private void HandleCombatFailed(MapConnectionData connection) => AbortTravel();
}
