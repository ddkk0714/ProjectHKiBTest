using System;
using UnityEngine;

// 연결의 웨이브 전투 시퀀스 실행.
// 기존 WaveTileManager / EnemyManager 와의 연동은 TODO로 남겨둠.
public class WaveCombatBridge : MonoBehaviour
{
    public static WaveCombatBridge Instance { get; private set; }

    public event Action OnCombatCompleted;
    public event Action OnCombatFailed;

    private MapConnectionData _currentConnection;
    private bool _inCombat;

    public bool InCombat => _inCombat;

    private void Awake() => Instance = this;

    public void StartCombat(MapConnectionData connection)
    {
        if (_inCombat)
        {
            Debug.LogWarning("[WaveCombatBridge] 이미 전투 중입니다.");
            return;
        }

        _currentConnection = connection;
        _inCombat = true;

        var from = MapGraph.Instance.GetNode(connection.fromGuid);
        var to   = MapGraph.Instance.GetNode(connection.toGuid);
        Debug.Log($"[WaveCombatBridge] 전투 시작 — {from?.nodeName} → {to?.nodeName}");

        // TODO: LoadWaves(connection.wavePaths) 결과를 WaveTileManager에 전달
        // 예: WaveTileManager.Instance.StartWaveSequence(LoadWaves(connection.wavePaths));
    }

    // 기존 웨이브 시스템 종료 콜백에서 호출
    public void NotifyCombatCompleted()
    {
        if (!_inCombat) return;
        _inCombat = false;

        MapGraph.Instance.MarkConnectionCleared(_currentConnection);
        RouteManager.Instance.AdvanceToNextNode();

        OnCombatCompleted?.Invoke();
        Debug.Log("[WaveCombatBridge] 전투 완료 — 연결 영구 개방");
    }

    // 전투 실패(사망) 시 호출
    public void NotifyCombatFailed()
    {
        if (!_inCombat) return;
        _inCombat = false;
        RouteManager.Instance.AbortTravel();
        OnCombatFailed?.Invoke();
        Debug.Log("[WaveCombatBridge] 전투 실패");
    }

    private WaveDataSO[] LoadWaves(string[] paths)
    {
        if (paths == null || paths.Length == 0) return Array.Empty<WaveDataSO>();
        var waves = new WaveDataSO[paths.Length];
        for (int i = 0; i < paths.Length; i++)
            waves[i] = Resources.Load<WaveDataSO>(paths[i]);
        return waves;
    }
}
