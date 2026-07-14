using System;
using System.Collections.Generic;

// 다중 목적지 이동 계획 한 건 — 노트(Note)의 핵심 산출물(NoteSystem_기획서.md "핵심 기능" 참고).
// 순서가 있는 목적지 목록 + 계획 전체에 적용되는 경로 방식(MVP는 구간별 개별 지정 없이 단일 적용, 확정).
// NoteEntry(Data/NoteEntry.cs)와 같은 패턴으로 네임스페이스 없이 둔다 — 런타임 상태 데이터.
[Serializable]
public class RouteWaypointPlan
{
    public string planId;
    public string planName;
    public List<string> orderedMapGuids = new(); // 목적지 순서 (출발 지점은 포함하지 않음 — 실행 시점의 RouteModule.CurrentLocation이 첫 구간의 시작점)
    public PathType pathType;
}

// 계획 실행 중 진행 상태 — RouteWaypointPlan(정의) 자체와 분리해서 NoteModule이 소유한다
// (RouteModule이 "선택 경로"와 "이동 중 진행"을 분리해서 갖는 것과 같은 패턴).
public class NotePlanExecutionState
{
    public string planId;
    public int currentLegIndex;  // 몇 번째 구간(orderedMapGuids의 인덱스)을 향해 가고 있는지 (0-based)
    public bool isHalted;        // true면 전투 실패 등으로 자동 연쇄가 멈춘 상태 — 플레이어가 "재개"를 눌러야 함
}
