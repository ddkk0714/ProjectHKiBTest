using System;
using System.Collections.Generic;

// 노트 상단 툴바의 "저장한 루트" 창에서 이름 붙여 저장해두는 스냅샷 한 건 — 선택 경로 + 수동 핀 단서 +
// 그래프상 배치 위치. 예전에 있었다가 제거된 RouteWaypointPlan("이동 계획")과는 성격이 다르다 —
// 이건 "지금 이 노트 화면 배치 상태"를 그대로 떠두는 것뿐, 실행/구간 개념이 없는 순수 스냅샷이다.
// NoteEntry.cs와 같은 패턴으로 네임스페이스 없이 배치(런타임 상태 데이터).
[Serializable]
public class NoteSavedBoard
{
    public string boardId;
    public string boardName;

    // 저장 시점의 선택 경로(RouteModule.SelectedRoute) — 노드 GUID 순서만 저장한다. PathResult 자체는
    // MapNodeData 객체 참조를 들고 있어 그대로 직렬화할 수 없어서(RouteModule.ImportSelectedRoute와
    // 같은 패턴), 불러올 때 MapGraph에서 다시 조회한다.
    public List<string> routeNodeGuids = new();

    // 저장 시점의 수동 핀 단서 clueId만 — 경로연동(RouteLinked) 항목은 routeNodeGuids를 복원하면
    // NoteModule.RebuildRouteLinkedEntries가 자동으로 다시 채우므로 중복 저장하지 않는다.
    public List<string> manualPinClueIds = new();

    // [2026-07-21 확장] 그래프에서 사용자가 옮겨둔 단서 노드의 위치 — 원래는 수동 핀 단서만 대상이었으나,
    // 경로연동 단서를 노드로 펼쳐서 직접 옮겨둔 경우에도 그 위치가 사라진다는 요청으로 대상을 넓혔다.
    // 지금 노드로 "펼쳐져" 있는 단서라면(NoteRouteGraphView.GetPlacedClueIds — 수동 핀/경로연동 구분 없음)
    // 전부 포함한다. 아래 expandedClueIds를 먼저 복원해야 이 위치가 실제로 적용될 노드가 존재한다.
    public List<CluePositionEntry> cluePositions = new();

    // [신설, 2026-07-21] 저장 시점에 "카드가 아니라 노드로 펼쳐져 있던" 경로연동 단서 clueId 목록
    // (NoteRouteGraphView._expandedClueIds). 이걸 먼저 복원하지 않으면 cluePositions에 위치가 있어도
    // 그 단서가 여전히 카드인 채로 남아 위치 정보가 적용될 자리가 없다 — 수동 핀 단서는 애초에 항상
    // 노드로 표시되므로(카드로 접힐 자리가 없음) 이 목록과 무관하다.
    public List<string> expandedClueIds = new();

    // [신설, 2026-07-21] "단서 연동 모드"로 이어둔 단서 관계 스냅샷 — 양쪽 단서 종류(경로연동/수동핀)와
    // 무관하게 저장 시점의 전체 연결을 그대로 담는다. 불러올 때 한쪽 끝이 이 보드에 없는 링크는 그냥
    // 화면에 안 그려질 뿐 무해하다(NoteRouteGraphView.RelayoutEdges가 두 끝 다 보일 때만 그림).
    public List<NoteClueLink> clueLinks = new();
}

[Serializable]
public class CluePositionEntry
{
    public string clueId;
    public float x;
    public float y;
}
