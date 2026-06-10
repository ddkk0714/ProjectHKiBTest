using UnityEngine;

// 침대 임시 스폰 포인트 등록 및 1회 소모 복귀 처리.
// 사망 처리 흐름: 침대 있음 → 침대 복귀 + 소모, 없음 → 집(homeNodeGuid) 복귀
public class RouteSpawnManager : MonoBehaviour
{
    public static RouteSpawnManager Instance { get; private set; }

    [SerializeField] private string _homeNodeGuid; // 집 노드 GUID (인스펙터에서 설정)

    private MapNodeData _bedSpawnNode; // 1회 소모

    private void Awake() => Instance = this;

    // 침대에서 잠들 때 호출 (HP 전체 회복은 별도 처리)
    public void RegisterBedSpawn(MapNodeData bedNode)
    {
        _bedSpawnNode = bedNode;
        Debug.Log($"[RouteSpawnManager] 침대 스폰 등록 → {bedNode.nodeName}");
    }

    // 사망 시 복귀 위치 결정 (1회 소모 후 초기화)
    public MapNodeData ConsumeRespawnNode()
    {
        if (_bedSpawnNode != null)
        {
            var bed = _bedSpawnNode;
            _bedSpawnNode = null;
            Debug.Log($"[RouteSpawnManager] 침대 복귀 → {bed.nodeName} (소모됨)");
            return bed;
        }
        Debug.Log("[RouteSpawnManager] 집으로 복귀");
        return MapGraph.Instance.GetNode(_homeNodeGuid);
    }

    public bool HasBedSpawn => _bedSpawnNode != null;
}
