using System.Collections.Generic;

// 여러 목적지를 순서대로 MapPathFinder에 넘겨 구간(leg)별 PathResult를 이어붙이는 무상태 계산 로직 —
// MapPathFinder/DifficultyCalculator와 같은 패턴. RouteWaypointPlan(정의)을 소비하기만 하고
// 아무 상태도 갖지 않는다 — 실행 상태(NotePlanExecutionState)는 NoteModule이 소유한다.
public static class RouteWaypointPlanner
{
    // 계획 전체 미리보기 — 목적지 순서 편집 UI가 구간 수/총 난이도/통과 불가 여부를 보여주는 데 쓴다.
    public class PlanPreview
    {
        public List<PathResult> Legs { get; } = new();
        public bool IsValid { get; set; } = true;   // 모든 구간이 실제로 도달 가능한지 (하나라도 끊기면 false)
        public bool IsBlocked { get; set; }          // 하나 이상의 구간이 통과 불가 맵을 포함하는지(대체 경로 유무 무관)
        public float TotalDifficulty { get; set; }
    }

    // start(보통 RouteModule.CurrentLocation)에서 시작해 plan.orderedMapGuids를 순서대로 경유하는
    // 전체 미리보기를 계산한다. 중간에 도달 불가능한 구간이 나오면 그 지점에서 멈추고 IsValid=false.
    public static PlanPreview ComputePreview(
        MapNodeData start, RouteWaypointPlan plan, MapGraph graph, RouteProgressState progress,
        EmotionColor[] gears, bool avoidNoClueNodes)
    {
        var preview = new PlanPreview();
        if (start == null || plan == null || graph == null || plan.orderedMapGuids.Count == 0)
        {
            preview.IsValid = false;
            return preview;
        }

        var current = start;
        foreach (var guid in plan.orderedMapGuids)
        {
            var dest = graph.GetNode(guid);
            if (dest == null) { preview.IsValid = false; break; }

            var leg = MapPathFinder.FindPath(current, dest, plan.pathType, graph, progress, gears, avoidNoClueNodes);
            preview.Legs.Add(leg);
            if (!leg.IsValid) { preview.IsValid = false; break; }

            if (leg.IsBlocked) preview.IsBlocked = true;
            preview.TotalDifficulty += leg.TotalDifficulty;
            current = dest;
        }

        return preview;
    }

    // 실행 중 한 구간만 계산 — NoteModule이 자동 순차 진행(4단계 "실행 방식") 시 매 구간마다 호출한다.
    public static PathResult ComputeLeg(
        MapNodeData from, RouteWaypointPlan plan, int legIndex, MapGraph graph, RouteProgressState progress,
        EmotionColor[] gears, bool avoidNoClueNodes)
    {
        if (from == null || plan == null || graph == null) return new PathResult();
        if (legIndex < 0 || legIndex >= plan.orderedMapGuids.Count) return new PathResult();

        var dest = graph.GetNode(plan.orderedMapGuids[legIndex]);
        if (dest == null) return new PathResult();

        return MapPathFinder.FindPath(from, dest, plan.pathType, graph, progress, gears, avoidNoClueNodes);
    }
}
