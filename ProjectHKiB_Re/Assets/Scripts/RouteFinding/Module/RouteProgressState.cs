using System;
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
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 전투/퀘스트/대화 시스템이 "이 사건이 일어났다"를 루트파인딩에 알리고,
// 그 결과로 지도에 새 단서가 공개되게 하려면 아래 훅 하나만 사용하면 된다.
//
// ▸ 접근: RouteModule.Instance.Progress (MonoBehaviour 아님, RouteModule이 소유한 순수 C# 인스턴스)
//
// ▸ 핵심 훅 — 스토리/전투 이벤트 발생 기록
//     RouteModule.Instance.Progress.SetEventFlag(mapGuid, eventKey);
//   특정 맵(mapGuid)에서 어떤 사건(eventKey, 임의 문자열 — 네이밍 규칙은 콘텐츠 설계 시 팀 합의 필요,
//   예: "kill_<enemyId>", "pacify_<enemyId>", "met_<npcId>")이 발생했다는 사실을 기록한다.
//   호출 즉시, 그 맵의 clueIds 중 requiredEventKey가 이 eventKey와 일치하고 아직 미획득인 단서가
//   있으면 자동으로 획득 처리되고(OnClueAcquired 이벤트 발행), 그 단서가 가리키는 맵/연결이 지도에
//   공개된다 — 호출자는 단서 시스템 내부를 몰라도 된다.
//   ※ mapGuid는 "지금 그 사건이 일어난 맵"의 MapNodeData.guid — 전투 중이면 보통
//     RouteModule.Instance.CurrentNode.guid 또는 RouteModule.Instance.GetCurrentTargetNode().guid.
//
//   사용 예시 (전투 시스템에서 보스를 처치했을 때):
//     RouteModule.Instance.Progress.SetEventFlag(bossMapGuid, "kill_boss01");
//
// ▸ 그 외 조회용 공개 API (주로 UI/디버그용, 필요하면 다른 시스템도 사용 가능)
//   IsNodeVisited(node) / IsNodeCleared(node) / HasNodeClue(node) / HasConnectionClue(conn)
//   IsClueAcquired(clueId) / HasEventFlag(mapGuid, eventKey) / AcquiredClueIds / AcquisitionOrder
//   OnClueAcquired(ClueData) 이벤트 — 단서 획득 순간 구독하고 싶은 시스템(도감 NEW 배지 등)이 사용.
//   ForceAcquireAllClues() — 테스트/디버그 전용, 실제 게임플레이 코드에서는 호출하지 말 것.
//
// ▸ MarkNodeVisited/MarkNodeCleared/GrantNodeClue/GrantConnectionClue는 RouteModule 내부
//   (노드 도달·전투 클리어 처리)에서만 호출한다 — 외부 시스템이 직접 호출할 일은 없다.
// ▸ ApplyEventFlag/ApplyPassage/BuildEventFlagsSnapshot/BuildPassagesSnapshot은 세이브 시스템
//   전용(RouteModule의 IEventSaveProvider 구현이 내부적으로 위임) — 게임플레이 코드에서 직접 호출하지 말 것.
// ════════════════════════════════════════════════════════════════
public class RouteProgressState
{
    private readonly MapGraph _graph;

    private readonly HashSet<string> _visitedNodeGuids = new();      // 방문한 맵
    private readonly HashSet<string> _clearedNodeGuids = new();       // 클리어(영구 안전)된 맵 — 2026-07-14 이전, 원래는 연결
    private readonly HashSet<string> _cluedNodeGuids = new();         // 단서가 공개된 맵
    private readonly HashSet<string> _cluedConnectionGuids = new();   // 단서가 공개된 연결
    private readonly HashSet<string> _acquiredClueIds = new();        // 획득한 단서
    private readonly HashSet<string> _eventFlags = new();             // "mapGuid:eventKey"

    // 획득 순서 — _acquiredClueIds(HashSet, 순서 보장 안 됨)와 별개로 도감의 "획득 최신순" 정렬
    // (Clue_System.md 6-5)에만 쓰인다. 세이브 로드로 복원된 항목은 실제 획득 시각이 아니라
    // SaveSlotData.eventFlags에 저장된 순서(맵 그래프 정의 순서)로 채워지는 best-effort 값이다 —
    // 정확한 획득 타임스탬프까지 저장할 정도로 중요한 기능은 아니라고 판단해 단순화했다.
    private readonly List<string> _acquisitionOrder = new();
    public IReadOnlyList<string> AcquisitionOrder => _acquisitionOrder;

    public IReadOnlyCollection<string> AcquiredClueIds => _acquiredClueIds;

    // 단서를 새로 획득할 때마다 발행 — CodexModule 등 확장 시스템이 폴링 없이 구독한다.
    // 세이브 로드(ApplyEventFlag)는 이 이벤트를 발행하지 않으므로(컬렉션 직접 갱신),
    // 로드 직후에는 구독자가 직접 전체 재계산(AcquiredClueIds 순회)을 한 번 해줘야 한다.
    public event Action<ClueData> OnClueAcquired;

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
        _clearedNodeGuids.Clear();
        _cluedNodeGuids.Clear();
        _cluedConnectionGuids.Clear();
        _acquiredClueIds.Clear();
        _eventFlags.Clear();
        _acquisitionOrder.Clear();

        if (_graph == null) return;

        foreach (var node in _graph.AllNodes)
            if (node.startsWithClue) _cluedNodeGuids.Add(node.guid);

        foreach (var conn in _graph.AllConnections)
            if (conn.startsWithClue) _cluedConnectionGuids.Add(conn.guid);
    }

    // ─── 상태 조회 ───────────────────────────────────────────────
    public bool IsNodeVisited(MapNodeData node) => _visitedNodeGuids.Contains(node.guid);
    public bool IsNodeCleared(MapNodeData node) => _clearedNodeGuids.Contains(node.guid);
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

    // 맵 전투 클리어 → 영구 안전. (단, 일기장 세이브 전에 사망하면 손실 — RouteModule.RevertToLastSave 참조)
    public void MarkNodeCleared(MapNodeData node) => _clearedNodeGuids.Add(node.guid);

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
        _acquisitionOrder.Add(clue.id);

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
        OnClueAcquired?.Invoke(clue);
    }

    // 테스트/디버그 전용 — 방문·이벤트 조건을 전부 무시하고 그래프의 모든 단서를 즉시 획득 처리한다.
    // 도감(Codex)/노트(Note) UI를 실제 플레이 없이 빠르게 검증할 때 쓴다(RouteSystemTest 참고).
    public void ForceAcquireAllClues()
    {
        if (_graph == null) return;
        foreach (var clue in _graph.AllClues)
        {
            if (_acquiredClueIds.Contains(clue.id)) continue;
            AcquireClue(clue);
        }
    }

    // ─── 세이브 연동 (일기장 세이브) ───────────────────────────────
    // SaveSlotData(Save/New_Save, RouteFinding 폴더 밖)를 직접 참조하지 않는다 — 대신
    // Dictionary<string,bool> 스냅샷/적용 메서드만 제공하고, 실제 SaveSlotData와의 입출력은
    // RouteModule(IEventSaveProvider 구현)이 담당한다. 키 규칙:
    //   passages              : 클리어된 맵 GUID (2026-07-14 이전 — 원래는 연결 GUID)
    //   eventFlags(접두사별)  : "mapnode_<guid>"   방문한 맵
    //                           "mapclue_<guid>"   단서 공개된 맵
    //                           "connclue_<guid>"  단서 공개된 연결
    //                           "clueacq_<clueId>" 획득한 단서
    //                           "storyevent_<mapGuid>:<eventKey>" 스토리 이벤트

    public Dictionary<string, bool> BuildEventFlagsSnapshot()
    {
        var dict = new Dictionary<string, bool>();
        if (_graph == null) return dict;

        foreach (var node in _graph.AllNodes)
        {
            dict["mapnode_" + node.guid] = _visitedNodeGuids.Contains(node.guid);
            dict["mapclue_" + node.guid] = _cluedNodeGuids.Contains(node.guid);
        }
        foreach (var conn in _graph.AllConnections)
            dict["connclue_" + conn.guid] = _cluedConnectionGuids.Contains(conn.guid);

        foreach (var clue in _graph.AllClues)
            dict["clueacq_" + clue.id] = _acquiredClueIds.Contains(clue.id);

        foreach (var key in _eventFlags)
            dict["storyevent_" + key] = true;

        return dict;
    }

    public Dictionary<string, bool> BuildPassagesSnapshot()
    {
        var dict = new Dictionary<string, bool>();
        if (_graph == null) return dict;

        foreach (var node in _graph.AllNodes)
            dict[node.guid] = _clearedNodeGuids.Contains(node.guid);

        return dict;
    }

    // 세이브 항목 하나를 복원한다. 게임플레이 갱신 메서드(MarkNodeVisited 등)와 달리
    // TryAcquireCluesForMap/OnClueAcquired 같은 파생 로직을 다시 태우지 않고 원본 컬렉션에
    // 그대로 반영한다 — 세이브에 이미 "acquired" 등 파생 결과가 저장돼 있으므로 재계산이 불필요하고,
    // 재계산하면 로드 중 이벤트가 잘못 발행될 위험도 있다.
    // 로드 시작 전 ResetToInitial()을 먼저 호출해야 한다(항목별로 호출되므로 자체 리셋 없음).
    public void ApplyEventFlag(string id, bool value)
    {
        if (!value) return; // false는 ResetToInitial()의 기본값과 같으므로 별도 처리 불필요
        if (id.StartsWith("mapnode_"))
            _visitedNodeGuids.Add(id.Substring("mapnode_".Length));
        else if (id.StartsWith("mapclue_"))
            _cluedNodeGuids.Add(id.Substring("mapclue_".Length));
        else if (id.StartsWith("connclue_"))
            _cluedConnectionGuids.Add(id.Substring("connclue_".Length));
        else if (id.StartsWith("clueacq_"))
        {
            var clueId = id.Substring("clueacq_".Length);
            _acquiredClueIds.Add(clueId);
            _acquisitionOrder.Add(clueId); // best-effort 순서(세이브 파일에 저장된 순서) — 위 필드 주석 참고
        }
        else if (id.StartsWith("storyevent_"))
            _eventFlags.Add(id.Substring("storyevent_".Length));
    }

    public void ApplyPassage(string id, bool opened)
    {
        if (opened) _clearedNodeGuids.Add(id);
    }
}
