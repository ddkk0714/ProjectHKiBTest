using System;
using UnityEngine;

// 맵(Map) 전투의 실행 창구.
//
// 2026-07-14 — 전투가 연결(Connection)에서 맵(Node)으로 이동함에 따라 이 브릿지가 다루는
// 단위도 연결 → 맵으로 바뀌었다. 흐름 자체는 그대로다:
//   1) 루트파인딩 쪽에서 StartCombat(맵)으로 전투 시작을 요청한다.
//   2) 기존 웨이브 시스템(WaveTileManager / EnemyManager)이 전투를 수행한다. (연동 TODO)
//   3) 웨이브 시스템의 종료 콜백이 NotifyCombatCompleted / NotifyCombatFailed를 호출한다.
//
// 이 클래스는 전투의 시작/종료 "사실"만 이벤트로 알린다.
// 그 결과 처리(맵 영구 클리어, 다음 노드 진행, 이동 중단)는
// RouteModule이 OnCombatCompleted / OnCombatFailed를 구독해 담당한다.
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 실제 웨이브 전투 시스템(WaveTileManager/EnemyManager)이 루트파인딩과
// 연결되는 유일한 접점. ★ 아직 연동 미완료(TODO) — 이 클래스가 "지금은 무엇을 해야 하고, 웨이브
// 시스템 쪽에서는 무엇을 호출해줘야 하는지"의 계약(contract)만 정의된 상태다.
//
// ▸ 접근: WaveCombatBridge.Instance
//   ※ RouteSpawnManager와 같은 패턴 — 자동 생성 안 됨, 씬에 GameObject로 미리 배치해야 한다.
//
// ▸ 루트파인딩 → 웨이브 시스템 (전투를 "시작해야 한다"는 신호)
//     WaveCombatBridge.Instance.StartCombat(mapNode);
//   RouteModule이 노드 도달 시 자동으로 호출한다(외부에서 직접 호출할 일은 거의 없음) — mapNode에는
//   enemyGroups(적 구성)/wavePaths(웨이브 데이터 Resources 경로)/requiredGears(통과 장비 조건, 이미
//   MapPathFinder가 걸러줬으므로 여기서는 신경 안 써도 됨)가 들어있다.
//   ★ 지금은 Debug.Log만 찍고 실제 웨이브 실행 로직이 없다(LoadWaves()가 WaveDataSO[]를 로드하는
//     것까지만 구현됨, private, 아직 아무도 안 씀) — 실제 연동 시 이 메서드 안에서
//     WaveTileManager.Instance.StartWaveSequence(LoadWaves(node.wavePaths)) 같은 식으로 웨이브
//     시스템에 실행을 넘겨야 한다.
//
// ▸ 웨이브 시스템 → 루트파인딩 (전투가 "끝났다"는 신호 — ★ 실제 연동 시 웨이브 시스템이 호출해야 함)
//     WaveCombatBridge.Instance.NotifyCombatCompleted();  // 승리
//     WaveCombatBridge.Instance.NotifyCombatFailed();     // 패배(사망 등)
//   웨이브 시스템의 전투 종료 콜백(마지막 웨이브 클리어 / 플레이어 사망) 안에서 반드시 이 둘 중
//   하나를 호출해줘야 한다 — 안 부르면 RouteModule이 "아직 전투 중"으로 알고 다음 노드 진행이
//   영원히 멈춘다. 호출 후 자동으로 일어나는 일(RouteModule이 구독):
//     완료 → Progress.MarkNodeCleared(맵 영구 안전) + AdvanceToNextNode(다음 노드로 진행)
//     실패 → AbortTravel(이동 중단) — 실제 사망 처리(스폰 복귀 등)는 별도로
//            Manager/DeathHandler.cs의 HandleDeath()를 호출해야 한다(자동 연결 안 됨, TODO).
//
// ▸ 상태 조회: InCombat(전투 중 여부), CurrentNode(전투 중인 맵) — UI가 "지금 전투 중" 표시할 때 사용.
// ════════════════════════════════════════════════════════════════
public class WaveCombatBridge : MonoBehaviour
{
    public static WaveCombatBridge Instance { get; private set; }

    // 전투가 끝난 맵을 함께 전달한다 — 구독자가 어느 맵의 결과인지 알 수 있도록.
    public event Action<MapNodeData> OnCombatCompleted;
    public event Action<MapNodeData> OnCombatFailed;

    private MapNodeData _currentNode;
    private bool _inCombat;

    public bool InCombat => _inCombat;
    public MapNodeData CurrentNode => _currentNode;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[WaveCombatBridge] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void StartCombat(MapNodeData node)
    {
        if (_inCombat)
        {
            Debug.LogWarning("[WaveCombatBridge] 이미 전투 중입니다.");
            return;
        }

        _currentNode = node;
        _inCombat = true;

        Debug.Log($"[WaveCombatBridge] 전투 시작 — {node?.nodeName}");

        // TODO: LoadWaves(node.wavePaths) 결과를 WaveTileManager에 전달
        // 예: WaveTileManager.Instance.StartWaveSequence(LoadWaves(node.wavePaths));
    }

    // 기존 웨이브 시스템의 전투 종료(승리) 콜백에서 호출
    public void NotifyCombatCompleted()
    {
        if (!_inCombat) return;
        _inCombat = false;

        var finished = _currentNode;
        _currentNode = null;

        Debug.Log("[WaveCombatBridge] 전투 완료");
        OnCombatCompleted?.Invoke(finished);
    }

    // 전투 실패(사망) 시 호출
    public void NotifyCombatFailed()
    {
        if (!_inCombat) return;
        _inCombat = false;

        var failed = _currentNode;
        _currentNode = null;

        Debug.Log("[WaveCombatBridge] 전투 실패");
        OnCombatFailed?.Invoke(failed);
    }

    // 맵에 정의된 웨이브 데이터(Resources 경로) 로드. 웨이브 시스템 연동 시 사용 예정.
    private WaveDataSO[] LoadWaves(string[] paths)
    {
        if (paths == null || paths.Length == 0) return Array.Empty<WaveDataSO>();
        var waves = new WaveDataSO[paths.Length];
        for (int i = 0; i < paths.Length; i++)
            waves[i] = Resources.Load<WaveDataSO>(paths[i]);
        return waves;
    }
}
