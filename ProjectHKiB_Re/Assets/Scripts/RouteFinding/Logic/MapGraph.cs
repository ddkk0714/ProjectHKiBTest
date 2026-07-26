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
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 맵/단서 원본 데이터(기획 데이터, 플레이어 진행과 무관하게 항상 같은 값)를
// GUID/ID로 조회해야 하는 모든 시스템(전투가 "지금 이 맵의 enemyGroups"를 읽어야 할 때, 퀘스트가
// 특정 단서의 이름·설명을 표시해야 할 때 등)이 사용한다.
//
// ▸ 접근: MapGraph.Instance
//   ★ RouteModule/CodexModule/NoteModule과 달리 자동 생성 싱글턴이 아니다 — 반드시 씬에 GameObject로
//     배치해야 한다(JSON을 Awake에서 로드). null이면 씬 배치를 잊은 것 — RouteModule.Progress 등
//     이 클래스에 의존하는 다른 API도 MapGraph가 없으면 정상 동작하지 않는다.
//
// ▸ 조회 API
//   GetNode(guid)            : MapNodeData(맵 하나) — 이름/설명/이벤트/전투 데이터(enemyGroups 등) 포함
//   GetConnection(guid)      : MapConnectionData(맵과 맵 사이 연결) — 순수 그래프 구조 + 단서 트리거만
//   GetClue(clueId)          : ClueData(단서 정의) — 이름/설명/타겟/획득 조건
//   AllNodes / AllConnections / AllClues : 전체 목록(읽기 전용) — 순회가 필요할 때
//   StartNode                : 시작 지점(집) 노드
//   GetConnectionsFrom(node) : 특정 노드에 닿아 있는 연결 목록(양방향)
//   GetNeighbor(conn, from)  : 연결의 반대쪽 끝 노드
//
// ▸ 이 클래스는 절대 데이터를 쓰지 않는다(읽기 전용) — "이 맵을 방문했다/이 단서를 얻었다" 같은
//   진행 상태는 여기 없다. 그건 RouteModule.Instance.Progress(RouteProgressState)를 참고할 것 —
//   상세는 RouteProgressState.cs 상단 API 가이드 참고.
// ════════════════════════════════════════════════════════════════
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
