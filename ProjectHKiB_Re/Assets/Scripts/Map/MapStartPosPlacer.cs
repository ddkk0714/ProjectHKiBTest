using UnityEngine;

/// <summary>
/// 맵이 바뀌었을 때 플레이어를 새 맵의 <see cref="MapStartPos"/>로 옮긴다.
///
/// 원래 이 일은 MapChangeManager.AfterLoadMap()이 했는데, 세이브 시스템이 MapManager
/// (Addressables 기반)로 이관되면서 MapManager 경로에는 배치 담당이 없어졌다. MapManager를
/// 크게 건드리지 않기 위해, 이 컴포넌트가 OnMapLoaded를 구독해 같은 일을 대신한다.
///
/// MapStartPos는 "어느 맵에서 넘어왔는가(fromScene)"로 자기 차례를 판단하므로, 직전 맵 ID를
/// 여기서 기억한다. 최초 로드에는 직전 맵이 없으니 아무것도 하지 않는다.
/// </summary>
public class MapStartPosPlacer : MonoBehaviour
{
    private struct PlacementRequest
    {
        public bool pending;
        public StateMachine.MapEntryPlacementMode mode;
        public string entryPointId;
        public Vector3 position;
        public bool overrideDirection;
        public EnumManager.AnimDir direction;
    }

    private static MapStartPosPlacer _instance;
    public static MapStartPosPlacer Instance
    {
        get
        {
            if (_instance == null) _instance = FindObjectOfType<MapStartPosPlacer>();
            return _instance;
        }
    }

    [SerializeField] private MapManager mapManager;

    private string _previousMapID = "";
    private bool _skipNextPlacement;
    private PlacementRequest _nextPlacement;

    private void Awake()
    {
        if (_instance == null) _instance = this;
    }

    public void RequestNextPlacement(
        StateMachine.MapEntryPlacementMode mode,
        string entryPointId,
        Vector3 position,
        bool overrideDirection,
        EnumManager.AnimDir direction)
    {
        _nextPlacement = new PlacementRequest
        {
            pending = true,
            mode = mode,
            entryPointId = entryPointId,
            position = position,
            overrideDirection = overrideDirection,
            direction = direction,
        };
    }

    /// <summary>
    /// 다음 한 번의 맵 로드에서 배치를 건너뛴다.
    ///
    /// 세이브 로드가 쓴다 — 복원은 저장된 좌표로 직접 텔레포트하므로 시작 지점 배치가 필요 없고,
    /// 무엇보다 MapStartPos.SetPlayerToStartPos()가 endEvent를 발동시켜서 그대로 두면 세이브를
    /// 불러올 때마다 맵 진입 이벤트가 잘못 재생된다.
    /// </summary>
    public void SkipNextPlacement() => _skipNextPlacement = true;

    // Start에서 구독한다 — MapManager는 GameManager.instance를 통해 찾는데 그 값이 Awake에서
    // 채워지므로, OnEnable 시점에는 아직 없을 수 있다. 첫 맵 로드는 Addressables 비동기 콜백이라
    // Start 이후에 완료되므로 이 시점에 구독해도 놓치지 않는다.
    private void Start()
    {
        if (mapManager == null && GameManager.instance != null)
            mapManager = GameManager.instance.mapManager;

        if (mapManager != null) mapManager.OnMapLoaded += HandleMapLoaded;
    }

    private void OnDestroy()
    {
        if (mapManager != null) mapManager.OnMapLoaded -= HandleMapLoaded;
        if (_instance == this) _instance = null;
    }

    private bool TryApplyRequestedPlacement(PlacementRequest request)
    {
        if (request.mode == StateMachine.MapEntryPlacementMode.CustomPosition)
        {
            bool placed = PlacePlayerAt(request.position, request.overrideDirection, request.direction);
            if (!placed)
                Debug.LogWarning("[MapStartPosPlacer] Could not place the player at the requested custom position.");
            return placed;
        }

        if (request.mode != StateMachine.MapEntryPlacementMode.NamedEntry || string.IsNullOrWhiteSpace(request.entryPointId))
            return false;

        MapStartPos[] startPoses = FindObjectsOfType<MapStartPos>();
        for (int i = 0; i < startPoses.Length; i++)
        {
            if (startPoses[i].EntryPointId != request.entryPointId) continue;

            EnumManager.AnimDir? direction = request.overrideDirection
                ? request.direction
                : (EnumManager.AnimDir?)null;
            startPoses[i].SetPlayerToStartPos(direction);
            return true;
        }

        Debug.LogWarning($"[MapStartPosPlacer] Entry point ID '{request.entryPointId}' was not found; using the normal entry rules.");
        return false;
    }

    private static bool PlacePlayerAt(Vector3 position, bool overrideDirection, EnumManager.AnimDir direction)
    {
        if (GameManager.instance == null || GameManager.instance.player == null) return false;

        PhysicsManager physicsManager = FindObjectOfType<PhysicsManager>();
        if (physicsManager == null) return false;

        IPhysics physics = GameManager.instance.player.GetInterface<IPhysics>();
        if (physics == null) return false;

        physicsManager.RealTeleport(physics, position);
        if (!overrideDirection) return true;

        IDirAnimatable dirAnimatable = GameManager.instance.player.GetInterface<IDirAnimatable>();
        if (dirAnimatable == null) return true;

        dirAnimatable.SetAnimationDirection(direction);
        SimpleAnimationPlayer animationPlayer = dirAnimatable.AnimationPlayer;
        if (animationPlayer != null) animationPlayer.Play(animationPlayer.CurrentAnimationName);
        return true;
    }

    private void HandleMapLoaded(MapDataSO mapData)
    {
        string previousMapID = _previousMapID;
        _previousMapID = mapData != null ? mapData.mapAddressableID : "";

        PlacementRequest request = _nextPlacement;
        _nextPlacement = default;
        if (request.pending && TryApplyRequestedPlacement(request)) return;

        // 건너뛰더라도 직전 맵 기록은 위에서 갱신해둔다 — 안 그러면 그다음 전환에서 엉뚱한
        // MapStartPos가 선택된다.
        if (_skipNextPlacement)
        {
            _skipNextPlacement = false;
            return;
        }

        if (string.IsNullOrEmpty(previousMapID)) return; // 최초 로드 — 넘어온 맵이 없다

        // 전용 진입 지점(fromScene 지정)을 먼저 찾고, 없으면 기본 진입 지점으로 떨어진다.
        // 한 맵에 연결이 여러 개여도 대부분은 기본 지점 하나로 충분하다 — 방향별로 다른 자리에
        // 세워야 할 때만 전용 지점을 추가하면 된다.
        MapStartPos[] startPoses = FindObjectsOfType<MapStartPos>();
        MapStartPos defaultEntry = null;

        for (int i = 0; i < startPoses.Length; i++)
        {
            if (startPoses[i].MatchesSource(previousMapID))
            {
                startPoses[i].SetPlayerToStartPos();
                return;
            }

            if (defaultEntry == null && startPoses[i].IsDefaultEntry) defaultEntry = startPoses[i];
        }

        if (defaultEntry != null)
        {
            defaultEntry.SetPlayerToStartPos();
            return;
        }

        string loadedMapID = mapData != null ? mapData.mapAddressableID : "(없음)";
        Debug.LogWarning($"[MapStartPosPlacer] '{loadedMapID}' 맵에 쓸 수 있는 MapStartPos가 없습니다" +
                         $"('{previousMapID}'에서 넘어옴). 플레이어가 이전 좌표에 그대로 남습니다. " +
                         $"— 맵에 MapStartPos를 하나 두고 Is Default Entry를 켜면 모든 진입 방향이 처리됩니다.");
    }
}
