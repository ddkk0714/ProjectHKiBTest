using System;
using System.Collections.Generic;
using UnityEngine;

// 루트파인딩의 "지도 데이터베이스" — 읽기 전용 데이터 레이어.
//
// Resources의 JSON 두 개를 로드해 정적 데이터를 보관하고 조회를 제공한다:
//   Resources/RouteFinding/map_database.json → 맵 노드 + 연결
//   Resources/RouteFinding/clues.json        → 단서 정의
//
// 이 클래스는 데이터를 변경하지 않는다.
// "어디를 방문했고 어떤 단서를 얻었는지" 같은 플레이어 진행 상태는
// RouteModule.Instance.Progress (RouteProgressState)가 관리한다.
public class MapGraph : MonoBehaviour
{
    public static MapGraph Instance { get; private set; }

    [SerializeField] private string _mapDatabasePath = "RouteFinding/map_database";
    [SerializeField] private string _clueDatabasePath = "RouteFinding/clues";

    private MapNodeData[] _allNodes = Array.Empty<MapNodeData>();
    private MapConnectionData[] _allConnections = Array.Empty<MapConnectionData>();
    private ClueData[] _allClues = Array.Empty<ClueData>();
    private Dictionary<string, MapNodeData> _nodeByGuid = new();
    private Dictionary<string, MapConnectionData> _connectionByGuid = new();
    private Dictionary<string, ClueData> _clueById = new();

    // 노드 GUID → 그 노드에 닿아 있는 연결 목록 (양방향).
    // 경로 탐색이 노드마다 인접 연결을 조회하므로 한 번만 만들어 캐시한다.
    private Dictionary<string, List<MapConnectionData>> _connectionsByNodeGuid = new();
    private static readonly List<MapConnectionData> EmptyConnections = new();

    private MapNodeData _startNode;

    public IReadOnlyList<MapNodeData> AllNodes => _allNodes;
    public IReadOnlyList<MapConnectionData> AllConnections => _allConnections;
    public IReadOnlyList<ClueData> AllClues => _allClues;

    // 시작 지점(집) 노드 — isStartNode가 true인 노드. 지도 열람·경로 탐색의 기준점.
    public MapNodeData StartNode => _startNode;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MapGraph] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        LoadDatabase();
        LoadClues();
        BuildIndexes();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LoadDatabase()
    {
        var asset = Resources.Load<TextAsset>(_mapDatabasePath);
        if (asset == null)
        {
            Debug.LogError($"[MapGraph] 데이터베이스를 찾을 수 없습니다: Resources/{_mapDatabasePath}");
            return;
        }

        var db = JsonUtility.FromJson<MapDatabase>(asset.text);
        _allNodes = db.maps ?? Array.Empty<MapNodeData>();
        _allConnections = db.connections ?? Array.Empty<MapConnectionData>();

        Debug.Log($"[MapGraph] 로드 완료 — 맵 {_allNodes.Length}개, 연결 {_allConnections.Length}개");
    }

    private void LoadClues()
    {
        var asset = Resources.Load<TextAsset>(_clueDatabasePath);
        if (asset == null)
        {
            Debug.LogWarning($"[MapGraph] 단서 데이터베이스를 찾을 수 없습니다: Resources/{_clueDatabasePath}");
            return;
        }

        var db = JsonUtility.FromJson<ClueDatabase>(asset.text);
        _allClues = db.clues ?? Array.Empty<ClueData>();
    }

    // GUID 조회 테이블과 인접 연결 캐시 구축
    private void BuildIndexes()
    {
        _nodeByGuid = new Dictionary<string, MapNodeData>(_allNodes.Length);
        foreach (var n in _allNodes)
        {
            _nodeByGuid[n.guid] = n;
            if (n.isStartNode && _startNode == null) _startNode = n;
        }

        _connectionByGuid = new Dictionary<string, MapConnectionData>(_allConnections.Length);
        _connectionsByNodeGuid = new Dictionary<string, List<MapConnectionData>>(_allNodes.Length);
        foreach (var c in _allConnections)
        {
            _connectionByGuid[c.guid] = c;
            AddAdjacency(c.fromGuid, c);
            AddAdjacency(c.toGuid, c);
        }

        _clueById = new Dictionary<string, ClueData>(_allClues.Length);
        foreach (var clue in _allClues)
            _clueById[clue.id] = clue;
    }

    private void AddAdjacency(string nodeGuid, MapConnectionData conn)
    {
        if (!_connectionsByNodeGuid.TryGetValue(nodeGuid, out var list))
        {
            list = new List<MapConnectionData>();
            _connectionsByNodeGuid[nodeGuid] = list;
        }
        list.Add(conn);
    }

    // ─── 조회 ────────────────────────────────────────────────────
    public MapNodeData GetNode(string guid) =>
        _nodeByGuid.TryGetValue(guid, out var n) ? n : null;

    public MapConnectionData GetConnection(string guid) =>
        _connectionByGuid.TryGetValue(guid, out var c) ? c : null;

    public ClueData GetClue(string clueId) =>
        _clueById.TryGetValue(clueId, out var c) ? c : null;

    // 해당 노드에 닿아 있는 모든 연결 (연결은 양방향으로 취급)
    public IReadOnlyList<MapConnectionData> GetConnectionsFrom(MapNodeData node) =>
        _connectionsByNodeGuid.TryGetValue(node.guid, out var list) ? list : EmptyConnections;

    // 연결의 반대편 노드
    public MapNodeData GetNeighbor(MapConnectionData conn, MapNodeData from)
    {
        var neighborGuid = conn.fromGuid == from.guid ? conn.toGuid : conn.fromGuid;
        return _nodeByGuid.TryGetValue(neighborGuid, out var neighbor) ? neighbor : null;
    }
}
