using System.Collections.Generic;
using UnityEngine;

// 플레이어의 루트파인딩 진행 상태(공용 상태) 저장소.
//
// MapGraph가 "지도 원본 데이터(읽기 전용)"라면, 이 클래스는 그 위에
// 플레이어가 남긴 흔적을 기록한다:
//   - 방문한 맵 / 클리어(영구 개방)한 연결
//   - 단서가 공개된 맵·연결 / 획득한 단서 / 스토리 이벤트 플래그
//
// RouteModule이 단일 인스턴스를 소유하며, 다른 시스템은
// RouteModule.Instance.Progress 로 접근한다. (MonoBehaviour 아님 — 순수 C# 클래스)
//
// 단서 획득 규칙 (기획서):
//   단서는 출발 맵(MapNodeData.clueIds에 등록된 맵)을 "방문"한 뒤,
//   requiredEventKey가 비어있으면 즉시, 아니면 해당 이벤트가 발생했을 때 획득된다.
//   획득된 단서는 targetMapGuid / targetConnectionGuid가 가리키는 맵·연결을 지도에 공개한다.
public class RouteProgressState
{
    private readonly MapGraph _graph;

    private readonly HashSet<string> _visitedNodeGuids = new();      // 방문한 맵
    private readonly HashSet<string> _clearedConnectionGuids = new(); // 클리어(영구 개방)된 연결
    private readonly HashSet<string> _cluedNodeGuids = new();         // 단서가 공개된 맵
    private readonly HashSet<string> _cluedConnectionGuids = new();   // 단서가 공개된 연결
    private readonly HashSet<string> _acquiredClueIds = new();        // 획득한 단서
    private readonly HashSet<string> _eventFlags = new();             // "mapGuid:eventKey"

    public IReadOnlyCollection<string> AcquiredClueIds => _acquiredClueIds;

    public RouteProgressState(MapGraph graph)
    {
        _graph = graph;
        ResetToInitial();
    }

    // 모든 진행 상태를 게임 시작 직후 상태로 되돌린다.
    // (startsWithClue로 표시된 맵·연결은 처음부터 단서가 공개된 상태)
    public void ResetToInitial()
    {
        _visitedNodeGuids.Clear();
        _clearedConnectionGuids.Clear();
        _cluedNodeGuids.Clear();
        _cluedConnectionGuids.Clear();
        _acquiredClueIds.Clear();
        _eventFlags.Clear();

        if (_graph == null) return;

        foreach (var node in _graph.AllNodes)
            if (node.startsWithClue) _cluedNodeGuids.Add(node.guid);

        foreach (var conn in _graph.AllConnections)
            if (conn.startsWithClue) _cluedConnectionGuids.Add(conn.guid);
    }

    // ─── 상태 조회 ───────────────────────────────────────────────
    public bool IsNodeVisited(MapNodeData node) => _visitedNodeGuids.Contains(node.guid);
    public bool IsConnectionCleared(MapConnectionData conn) => _clearedConnectionGuids.Contains(conn.guid);
    public bool HasNodeClue(MapNodeData node) => _cluedNodeGuids.Contains(node.guid);
    public bool HasConnectionClue(MapConnectionData conn) => _cluedConnectionGuids.Contains(conn.guid);
    public bool IsClueAcquired(string clueId) => _acquiredClueIds.Contains(clueId);
    public bool HasEventFlag(string mapGuid, string eventKey) => _eventFlags.Contains(mapGuid + ":" + eventKey);

    // ─── 상태 갱신 ───────────────────────────────────────────────

    // 맵 도달 처리. 방문 = 해당 맵의 단서 자동 획득이며,
    // 이 맵에서 얻을 수 있는 단서(clueIds)의 획득 조건도 함께 검사한다.
    public void MarkNodeVisited(MapNodeData node)
    {
        _visitedNodeGuids.Add(node.guid);
        _cluedNodeGuids.Add(node.guid);
        TryAcquireCluesForMap(node.guid);
    }

    // 연결 전투 클리어 → 영구 개방. (단, 일기장 세이브 전에 사망하면 손실 — RouteModule.RevertToLastSave 참조)
    public void MarkConnectionCleared(MapConnectionData conn) => _clearedConnectionGuids.Add(conn.guid);

    // 단서 공개 — 지도에서 해당 맵/연결의 정보(이름·난이도 등)가 보이게 된다.
    public void GrantNodeClue(MapNodeData node) => _cluedNodeGuids.Add(node.guid);
    public void GrantConnectionClue(MapConnectionData conn) => _cluedConnectionGuids.Add(conn.guid);

    // 특정 맵에서 스토리 이벤트가 발생했음을 기록하고,
    // 그로 인해 획득 가능해진 단서가 있으면 함께 획득 처리한다.
    public void SetEventFlag(string mapGuid, string eventKey)
    {
        _eventFlags.Add(mapGuid + ":" + eventKey);
        TryAcquireCluesForMap(mapGuid);
    }

    // 해당 맵의 clueIds 중 "방문 완료 + (필요 시) 이벤트 발생" 조건을 만족한 단서를 모두 획득 처리.
    private void TryAcquireCluesForMap(string mapGuid)
    {
        var node = _graph?.GetNode(mapGuid);
        if (node?.clueIds == null || !IsNodeVisited(node)) return;

        foreach (var clueId in node.clueIds)
        {
            if (_acquiredClueIds.Contains(clueId)) continue;
            var clue = _graph.GetClue(clueId);
            if (clue == null) continue;

            bool eventOk = string.IsNullOrEmpty(clue.requiredEventKey) || HasEventFlag(mapGuid, clue.requiredEventKey);
            if (eventOk) AcquireClue(clue);
        }
    }

    // 단서 획득 — 단서가 가리키는 맵·연결을 지도에 공개한다.
    private void AcquireClue(ClueData clue)
    {
        _acquiredClueIds.Add(clue.id);

        if (!string.IsNullOrEmpty(clue.targetMapGuid))
        {
            var target = _graph.GetNode(clue.targetMapGuid);
            if (target != null) GrantNodeClue(target);
        }
        if (!string.IsNullOrEmpty(clue.targetConnectionGuid))
        {
            var target = _graph.GetConnection(clue.targetConnectionGuid);
            if (target != null) GrantConnectionClue(target);
        }

        Debug.Log($"[RouteProgressState] 단서 획득: {clue.name}");
    }

    // ─── SaveSlotData 연동 (일기장 세이브) ────────────────────────
    // 진행 상태는 SaveSlotData에 아래 키 규칙으로 직렬화된다:
    //   passages              : 클리어된 연결 GUID
    //   eventFlags(접두사별)  : "mapnode_<guid>"   방문한 맵
    //                           "mapclue_<guid>"   단서 공개된 맵
    //                           "connclue_<guid>"  단서 공개된 연결
    //                           "clueacq_<clueId>" 획득한 단서
    //                           "storyevent_<mapGuid>:<eventKey>" 스토리 이벤트

    public void ExportToSaveData(SaveSlotData data)
    {
        foreach (var conn in _graph.AllConnections)
            SetOrUpdatePassage(data, conn.guid, _clearedConnectionGuids.Contains(conn.guid));

        foreach (var node in _graph.AllNodes)
        {
            SetOrUpdateFlag(data, "mapnode_" + node.guid, _visitedNodeGuids.Contains(node.guid));
            SetOrUpdateFlag(data, "mapclue_" + node.guid, _cluedNodeGuids.Contains(node.guid));
        }
        foreach (var conn in _graph.AllConnections)
            SetOrUpdateFlag(data, "connclue_" + conn.guid, _cluedConnectionGuids.Contains(conn.guid));

        foreach (var clue in _graph.AllClues)
            SetOrUpdateFlag(data, "clueacq_" + clue.id, _acquiredClueIds.Contains(clue.id));

        foreach (var key in _eventFlags)
            SetOrUpdateFlag(data, "storyevent_" + key, true);
    }

    public void ImportFromSaveData(SaveSlotData data)
    {
        ResetToInitial();
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
