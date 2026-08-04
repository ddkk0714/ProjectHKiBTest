using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RouteFinding(추상 그래프)과 MapManager(실제 씬 로드)를 잇는 다리.
///
/// [왜 RouteFinding 폴더 밖에 있나]
/// RouteFinding은 외부 시스템을 직접 참조하지 않는 구조를 유지해 왔다(MapViewer가 노트를 직접
/// 참조하지 않고 씬에서 찾는 방식, RouteProgressState가 SaveSlotData 대신 IEventSaveProvider를
/// 거치는 방식). 그 원칙을 깨지 않도록 양쪽을 아는 코드는 이 클래스 하나로 몰아둔다 —
/// RouteFinding과 MapManager는 서로를 계속 모른다.
///
/// [연결 규칙]
///   정방향  RouteModule.OnNodeArrived(node)
///             → MapDataRegistry.Find(node.sceneName) → MapManager.LoadMap()
///   역방향  MapManager.OnMapLoaded(mapData)
///             → MapGraph.FindNodeBySceneName() → RouteModule.ImportCurrentLocation() + 방문 처리
///
/// 역방향이 필요한 이유: 단서 공개와 이벤트 플래그가 RouteModule.CurrentLocation 기준으로
/// 동작한다(SetRouteEventFlagAction은 mapGuid를 비워두면 CurrentLocation을 자동 사용).
/// 실제 로드된 맵과 CurrentLocation이 어긋나면 단서가 엉뚱한 맵에 기록된다.
///
/// [알려진 제약 — 전투 연동 시 수정 필요]
/// OnNodeArrived는 이미 "전투 통과 후" 시점이다. 실제 전투는 로드된 씬 안에서 벌어져야 하므로
/// 정상 흐름은 맵 로드 → 전투 → 도달 순이어야 한다. 지금은 전투 연동 자체가 미완이라
/// (WaveCombatBridge TODO) 이 훅으로 충분하지만, 전투를 붙일 때 로드 시점을 OnNodeArrived보다
/// 앞으로 옮겨야 한다.
/// </summary>
public class RouteFindingMapBridge : MonoBehaviour
{
    [SerializeField] private MapManager mapManager;
    [SerializeField] private MapDataRegistrySO mapDataRegistry;

    [Tooltip("아직 대응 맵이 없는 노드에 도달했을 때 경고를 남길지 여부. " +
             "콘텐츠 제작 전에는 대부분의 노드가 여기 해당하므로 기본은 꺼둔다.")]
    [SerializeField] private bool warnOnMissingMap = false;

    // 맵이 없다고 이미 알린 노드 — 도달할 때마다 같은 경고가 반복되는 걸 막는다.
    private readonly HashSet<string> _warnedNodeGuids = new();

    // Start에서 구독한다 — GameManager.instance가 Awake에서 채워지므로 OnEnable은 이르다.
    private void Start()
    {
        if (mapManager == null && GameManager.instance != null)
            mapManager = GameManager.instance.mapManager;

        if (mapManager != null) mapManager.OnMapLoaded += HandleMapLoaded;
        if (RouteModule.Instance != null) RouteModule.Instance.OnNodeArrived += HandleNodeArrived;
    }

    private void OnDestroy()
    {
        if (mapManager != null) mapManager.OnMapLoaded -= HandleMapLoaded;
        if (RouteModule.Instance != null) RouteModule.Instance.OnNodeArrived -= HandleNodeArrived;
    }

    // ─── 정방향: 노드 도달 → 실제 맵 로드 ───────────────────────

    private void HandleNodeArrived(MapNodeData node)
    {
        if (node == null) return;

        MapDataSO mapData = ResolveMapData(node);
        if (mapData == null)
        {
            // 대응 맵이 아직 없는 노드다. 씬 전환만 생략하고 그래프상 이동은 그대로 진행한다 —
            // 경로 탐색은 그래프 전체를 쓰는데 실제 맵은 일부만 만들어져 있어서, 여기서 막으면
            // 프로토타입 진행 자체가 불가능해진다.
            if (warnOnMissingMap && _warnedNodeGuids.Add(node.guid))
            {
                Debug.LogWarning($"[RouteFindingMapBridge] '{node.nodeName}'(sceneName='{node.sceneName}')에 " +
                                 $"대응하는 맵이 없어 씬 전환을 생략합니다.");
            }
            return;
        }

        // 이미 그 맵이 로드돼 있으면 다시 로드하지 않는다 — 같은 맵을 재로드하면 씬이 통째로
        // 다시 만들어져 진행 중이던 상태가 날아간다.
        if (mapManager != null && mapManager.CurrentMapData == mapData) return;

        mapManager?.LoadMap(mapData);
    }

    private MapDataSO ResolveMapData(MapNodeData node)
    {
        if (mapDataRegistry == null || string.IsNullOrEmpty(node.sceneName)) return null;
        return mapDataRegistry.Find(node.sceneName);
    }

    // ─── 역방향: 맵 로드 완료 → 그래프 위치 동기화 ──────────────

    private void HandleMapLoaded(MapDataSO mapData)
    {
        if (mapData == null || RouteModule.Instance == null || MapGraph.Instance == null) return;

        MapNodeData node = MapGraph.Instance.FindNodeBySceneName(mapData.mapAddressableID);
        if (node == null) return; // 그래프에 없는 맵(테스트 씬 등) — 동기화할 대상이 없다

        // 정방향으로 이미 갱신된 경우 같은 값을 다시 쓰게 되는데, 그때는 조용히 빠진다.
        if (RouteModule.Instance.CurrentLocation == node) return;

        RouteModule.Instance.ImportCurrentLocation(node.guid);
        RouteModule.Instance.Progress?.MarkNodeVisited(node);
    }
}
