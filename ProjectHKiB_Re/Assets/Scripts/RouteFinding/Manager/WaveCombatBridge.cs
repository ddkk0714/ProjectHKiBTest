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
