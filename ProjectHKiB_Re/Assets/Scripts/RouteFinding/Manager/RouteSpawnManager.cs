using UnityEngine;

// 침대 임시 스폰 포인트 관리.
//
// 기획서의 사망 처리 흐름:
//   사망
//   ├── 침대에서 잠든 적 있음 → 해당 침대로 복귀 (1회 소모)
//   └── 침대 없음            → 집(homeNodeGuid)으로 복귀
//
// 침대 스폰은 "1회한"이다 — 한 번 복귀에 사용되면 다시 집 기준으로 초기화된다.
// 미세이브 맵 진도 손실(RouteModule.RevertToLastSave)은 별도로 호출해야 한다.
public class RouteSpawnManager : MonoBehaviour
{
    public static RouteSpawnManager Instance { get; private set; }

    [SerializeField] private string _homeNodeGuid; // 집 노드 GUID (인스펙터에서 설정)

    private MapNodeData _bedSpawnNode; // 등록된 침대 (1회 소모)

    public bool HasBedSpawn => _bedSpawnNode != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RouteSpawnManager] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 침대에서 잠들 때 호출. 다음 사망 시 이 침대로 복귀하게 된다.
    // (HP 전체 회복은 침대 상호작용 쪽에서 별도 처리)
    public void RegisterBedSpawn(MapNodeData bedNode)
    {
        _bedSpawnNode = bedNode;
        Debug.Log($"[RouteSpawnManager] 침대 스폰 등록 → {bedNode.nodeName}");
    }

    // 사망 시 복귀할 노드를 반환한다. 침대가 등록돼 있으면 소모하고 침대로,
    // 없으면 집으로 복귀한다.
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
}
