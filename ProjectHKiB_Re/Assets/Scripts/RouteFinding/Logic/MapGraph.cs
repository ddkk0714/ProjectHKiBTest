using System;
using System.Collections.Generic;
using UnityEngine;

// 전체 맵 노드·연결 보관 및 런타임 상태(방문/단서/클리어) 관리.
// 데이터 출처: Resources/RouteFinding/map_database.json, Resources/RouteFinding/clues.json
// SaveSlotData.passages: 클리어된 연결 GUID
// SaveSlotData.eventFlags: "mapnode_<guid>", "mapclue_<guid>", "connclue_<guid>",
//                          "clueacq_<clueId>", "storyevent_<mapGuid>:<eventKey>"
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
    private Dictionary<string, ClueData> _clueByGuid = new();
    private Dictionary<string, string> _clueSourceMapGuid = new(); // clueId -> 획득 가능한 맵 GUID

    private readonly HashSet<string> _visitedNodeGuids = new();
    private readonly HashSet<string> _clearedConnectionGuids = new();
    private readonly HashSet<string> _cluedNodeGuids = new();
    private readonly HashSet<string> _cluedConnectionGuids = new();
    private readonly HashSet<string> _acquiredClueIds = new();
    private readonly HashSet<string> _eventFlags = new(); // "mapGuid:eventKey"

    public IReadOnlyList<MapNodeData> AllNodes => _allNodes;
    public IReadOnlyList<MapConnectionData> AllConnections => _allConnections;
    public IReadOnlyCollection<string> AcquiredClueIds => _acquiredClueIds;

    private void Awake()
    {
        Instance = this;
        LoadDatabase();
        LoadClues();
        InitializeClues();
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

        _nodeByGuid = new Dictionary<string, MapNodeData>(_allNodes.Length);
        foreach (var n in _allNodes)
            _nodeByGuid[n.guid] = n;

        _connectionByGuid = new Dictionary<string, MapConnectionData>(_allConnections.Length);
        foreach (var c in _allConnections)
            _connectionByGuid[c.guid] = c;

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

        _clueByGuid = new Dictionary<string, ClueData>(_allClues.Length);
        foreach (var clue in _allClues)
            _clueByGuid[clue.id] = clue;

        // clueIds 역매핑: 어느 맵을 방문해야 이 단서를 획득할 수 있는지
        _clueSourceMapGuid = new Dictionary<string, string>();
        foreach (var node in _allNodes)
        {
            if (node.clueIds == null) continue;
            foreach (var clueId in node.clueIds)
                _clueSourceMapGuid[clueId] = node.guid;
        }
    }

    private void InitializeClues()
    {
        foreach (var node in _allNodes)
            if (node.startsWithClue) _cluedNodeGuids.Add(node.guid);

        foreach (var conn in _allConnections)
            if (conn.startsWithClue) _cluedConnectionGuids.Add(conn.guid);
    }

    // ─── 단서 획득 ───────────────────────────────────────────────
    // 단서는 "출발 맵(clueIds에 등록된 맵)을 방문 + (필요 시) 특정 이벤트 발생" 후 획득된다.
    public ClueData GetClue(string clueId) =>
        _clueByGuid.TryGetValue(clueId, out var c) ? c : null;

    public bool IsClueAcquired(string clueId) => _acquiredClueIds.Contains(clueId);

    public bool HasEventFlag(string mapGuid, string eventKey) => _eventFlags.Contains(mapGuid + ":" + eventKey);

    // 특정 맵에서 이벤트가 발생했음을 기록하고, 그로 인해 획득 가능해진 단서를 처리한다.
    public void SetEventFlag(string mapGuid, string eventKey)
    {
        _eventFlags.Add(mapGuid + ":" + eventKey);
        TryAcquireCluesForMap(mapGuid);
    }

    // 해당 맵의 clueIds 중, 방문 + 이벤트 조건을 만족한 단서를 모두 획득 처리한다.
    private void TryAcquireCluesForMap(string mapGuid)
    {
        var node = GetNode(mapGuid);
        if (node?.clueIds == null || !IsNodeVisited(node)) return;

        foreach (var clueId in node.clueIds)
        {
            if (_acquiredClueIds.Contains(clueId)) continue;
            var clue = GetClue(clueId);
            if (clue == null) continue;

            bool eventOk = string.IsNullOrEmpty(clue.requiredEventKey) || HasEventFlag(mapGuid, clue.requiredEventKey);
            if (eventOk) AcquireClue(clue);
        }
    }

    private void AcquireClue(ClueData clue)
    {
        _acquiredClueIds.Add(clue.id);

        if (!string.IsNullOrEmpty(clue.targetMapGuid))
        {
            var target = GetNode(clue.targetMapGuid);
            if (target != null) GrantNodeClue(target);
        }
        if (!string.IsNullOrEmpty(clue.targetConnectionGuid))
        {
            var target = GetConnection(clue.targetConnectionGuid);
            if (target != null) GrantConnectionClue(target);
        }

        Debug.Log($"[MapGraph] 단서 획득: {clue.name}");
    }

    // ─── 조회 헬퍼 ───────────────────────────────────────────────
    public MapNodeData GetNode(string guid) =>
        _nodeByGuid.TryGetValue(guid, out var n) ? n : null;

    public MapConnectionData GetConnection(string guid) =>
        _connectionByGuid.TryGetValue(guid, out var c) ? c : null;

    // ─── 상태 조회 ───────────────────────────────────────────────
    public bool IsNodeVisited(MapNodeData node) => _visitedNodeGuids.Contains(node.guid);
    public bool IsConnectionCleared(MapConnectionData conn) => _clearedConnectionGuids.Contains(conn.guid);
    public bool HasNodeClue(MapNodeData node) => _cluedNodeGuids.Contains(node.guid);
    public bool HasConnectionClue(MapConnectionData conn) => _cluedConnectionGuids.Contains(conn.guid);

    // ─── 상태 갱신 ───────────────────────────────────────────────
    public void MarkNodeVisited(MapNodeData node)
    {
        _visitedNodeGuids.Add(node.guid);
        _cluedNodeGuids.Add(node.guid); // 방문 = 단서 획득
        TryAcquireCluesForMap(node.guid);
    }

    public void MarkConnectionCleared(MapConnectionData conn) => _clearedConnectionGuids.Add(conn.guid);
    public void GrantNodeClue(MapNodeData node) => _cluedNodeGuids.Add(node.guid);
    public void GrantConnectionClue(MapConnectionData conn) => _cluedConnectionGuids.Add(conn.guid);

    // ─── 그래프 탐색 헬퍼 ────────────────────────────────────────
    public List<MapConnectionData> GetConnectionsFrom(MapNodeData node)
    {
        var result = new List<MapConnectionData>();
        foreach (var conn in _allConnections)
            if (conn.fromGuid == node.guid || conn.toGuid == node.guid)
                result.Add(conn);
        return result;
    }

    public MapNodeData GetNeighbor(MapConnectionData conn, MapNodeData from)
    {
        var neighborGuid = conn.fromGuid == from.guid ? conn.toGuid : conn.fromGuid;
        return _nodeByGuid.TryGetValue(neighborGuid, out var neighbor) ? neighbor : null;
    }

    // ─── SaveSlotData 연동 ────────────────────────────────────────
    public void ExportToSaveData(SaveSlotData data)
    {
        foreach (var conn in _allConnections)
            SetOrUpdatePassage(data, conn.guid, _clearedConnectionGuids.Contains(conn.guid));

        foreach (var node in _allNodes)
        {
            SetOrUpdateFlag(data, "mapnode_" + node.guid, _visitedNodeGuids.Contains(node.guid));
            SetOrUpdateFlag(data, "mapclue_" + node.guid, _cluedNodeGuids.Contains(node.guid));
        }
        foreach (var conn in _allConnections)
            SetOrUpdateFlag(data, "connclue_" + conn.guid, _cluedConnectionGuids.Contains(conn.guid));

        foreach (var clue in _allClues)
            SetOrUpdateFlag(data, "clueacq_" + clue.id, _acquiredClueIds.Contains(clue.id));

        foreach (var key in _eventFlags)
            SetOrUpdateFlag(data, "storyevent_" + key, true);
    }

    public void ImportFromSaveData(SaveSlotData data)
    {
        _visitedNodeGuids.Clear();
        _clearedConnectionGuids.Clear();
        _cluedNodeGuids.Clear();
        _cluedConnectionGuids.Clear();
        _acquiredClueIds.Clear();
        _eventFlags.Clear();
        InitializeClues();

        if (data == null) return;

        foreach (var p in data.passages)
            if (p.opened) _clearedConnectionGuids.Add(p.id);

        foreach (var f in data.eventFlags)
        {
            if (!f.value) continue;
            if (f.id.StartsWith("mapnode_"))
                _visitedNodeGuids.Add(f.id.Substring("mapnode_".Length));
            else if (f.id.StartsWith("mapclue_"))
                _cluedNodeGuids.Add(f.id.Substring("mapclue_".Length));
            else if (f.id.StartsWith("connclue_"))
                _cluedConnectionGuids.Add(f.id.Substring("connclue_".Length));
            else if (f.id.StartsWith("clueacq_"))
                _acquiredClueIds.Add(f.id.Substring("clueacq_".Length));
            else if (f.id.StartsWith("storyevent_"))
                _eventFlags.Add(f.id.Substring("storyevent_".Length));
        }
    }

    // 사망 처리 — 미세이브 진도 손실 (마지막 ImportFromSaveData 상태로 복귀)
    public void RevertToLastSave(SaveSlotData lastSaved) => ImportFromSaveData(lastSaved);

    private static void SetOrUpdatePassage(SaveSlotData data, string id, bool opened)
    {
        foreach (var p in data.passages)
        {
            if (p.id == id) { p.opened = opened; return; }
        }
        data.passages.Add(new PassageSaveInfo { id = id, opened = opened });
    }

    private static void SetOrUpdateFlag(SaveSlotData data, string id, bool value)
    {
        foreach (var f in data.eventFlags)
        {
            if (f.id == id) { f.value = value; return; }
        }
        data.eventFlags.Add(new EventFlagSaveInfo { id = id, value = value });
    }
}
