using System.Collections.Generic;
using UnityEngine;

// 선택된 경로 상태 관리 및 이동 진행 추적.
// 출발 후에는 경로·장비 변경 불가 (기획서 규칙).
public class RouteManager : MonoBehaviour
{
    public static RouteManager Instance { get; private set; }

    private PathResult _selectedRoute;
    private int _currentNodeIndex;
    private bool _isTraveling;

    public PathResult SelectedRoute => _selectedRoute;
    public bool IsTraveling => _isTraveling;

    public MapNodeData CurrentNode =>
        _isTraveling && _selectedRoute.IsValid
            ? _selectedRoute.Nodes[_currentNodeIndex]
            : null;

    private void Awake() => Instance = this;

    // 출발 전 경로 선택 (지도 화면에서 호출)
    // 통과 불가 연결을 포함한 경로(IsBlocked)는 선택할 수 없다 — AlternativePath를 대신 선택해야 한다.
    public void SelectRoute(PathResult route)
    {
        if (_isTraveling)
        {
            Debug.LogWarning("[RouteManager] 이동 중에는 경로를 변경할 수 없습니다.");
            return;
        }
        if (route != null && route.IsBlocked)
        {
            Debug.LogWarning("[RouteManager] 현재 장비로 통과할 수 없는 구간이 포함된 경로는 선택할 수 없습니다.");
            return;
        }
        _selectedRoute = route;
        Debug.Log($"[RouteManager] 경로 선택 완료 — {route?.Nodes?.Count ?? 0}개 노드");
    }

    // 출발 (이후 지도·장비 변경 불가)
    public bool StartTravel()
    {
        if (_selectedRoute == null || !_selectedRoute.IsValid)
        {
            Debug.LogWarning("[RouteManager] 선택된 경로가 없습니다.");
            return false;
        }
        _isTraveling = true;
        _currentNodeIndex = 0;
        Debug.Log($"[RouteManager] 출발 → {_selectedRoute.Nodes[0].nodeName}");
        return true;
    }

    // 연결 전투 완료 후 WaveCombatBridge에서 호출
    public void AdvanceToNextNode()
    {
        if (!_isTraveling) return;
        _currentNodeIndex++;

        var arrived = _selectedRoute.Nodes[_currentNodeIndex];
        MapGraph.Instance.MarkNodeVisited(arrived);
        Debug.Log($"[RouteManager] 도달 → {arrived.nodeName}");

        if (_currentNodeIndex >= _selectedRoute.Nodes.Count - 1)
        {
            _isTraveling = false;
            Debug.Log("[RouteManager] 목적지 도달!");
        }
    }

    // 현재 통과해야 할 연결 (전투 대상)
    public MapConnectionData GetCurrentConnection()
    {
        if (!_isTraveling || _currentNodeIndex >= _selectedRoute.Connections.Count)
            return null;
        return _selectedRoute.Connections[_currentNodeIndex];
    }

    public void AbortTravel()
    {
        _isTraveling = false;
        Debug.Log("[RouteManager] 이동 중단");
    }
}
