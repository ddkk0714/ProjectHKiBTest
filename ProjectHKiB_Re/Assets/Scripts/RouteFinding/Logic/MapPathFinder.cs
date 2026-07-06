using System.Collections.Generic;

public enum PathType { Shortest, MinDifficulty, Balanced }

public class PathResult
{
    public List<MapNodeData> Nodes { get; set; } = new();
    public List<MapConnectionData> Connections { get; set; } = new();
    public float TotalDifficulty { get; set; }
    public bool ContainsNoClueNode { get; set; }

    // 현재 장비로 통과 불가능한 연결을 포함하는지 여부.
    // true면 이 경로는 표시만 되고 선택할 수 없으며, AlternativePath가 대신 추천된다.
    public bool IsBlocked { get; set; }
    public PathResult AlternativePath { get; set; }

    // "단서 있는 경로 우선" 옵션(avoidNoClueNodes)으로 탐색했으나, 단서 없는 맵을
    // 전혀 경유하지 않는 경로가 존재하지 않아 어쩔 수 없이 일반 경로로 대체됐음을 표시.
    public bool NoClueAvoidanceFailed { get; set; }

    public bool IsValid => Nodes != null && Nodes.Count >= 2;
    public bool IsSelectable => IsValid && !IsBlocked;
}

// 경로 탐색 알고리즘 모음 — 무상태 정적 클래스.
// 그래프 데이터(graph), 진행 상태(progress, 단서 공개 여부 판단용),
// 장착 장비(equippedGears, 난이도 가중치·통과 가능 판정용)를 모두 파라미터로 받는다.
// 일반적으로는 RouteModule.FindPath / FindPathFromStart 래퍼를 통해 호출된다.
public static class MapPathFinder
{
    public static PathResult FindPath(
        MapNodeData start,
        MapNodeData destination,
        PathType pathType,
        MapGraph graph,
        RouteProgressState progress,
        EmotionColor[] equippedGears = null,
        bool avoidNoClueNodes = false)
    {
        if (start == null || destination == null || graph == null)
            return new PathResult();

        var result = Search(start, destination, pathType, graph, progress, equippedGears, avoidNoClueNodes, excluded: null);

        // "단서 있는 경로 우선" 옵션인데 그런 경로가 아예 없으면, 일반 탐색으로 대체하고 실패했음을 표시.
        if (avoidNoClueNodes && !result.IsValid)
        {
            result = Search(start, destination, pathType, graph, progress, equippedGears, avoidNoClueNodes: false, excluded: null);
            if (result.IsValid) result.NoClueAvoidanceFailed = true;
        }

        if (!result.IsValid) return result;

        result.IsBlocked = ContainsBlockedConnection(result.Connections, equippedGears);
        if (result.IsBlocked)
        {
            // 차선 경로: 통과 불가 연결을 그래프에서 모두 제외하고 같은 기준으로 재탐색
            var excluded = GetBlockedConnectionGuids(graph, equippedGears);
            var alt = Search(start, destination, pathType, graph, progress, equippedGears, avoidNoClueNodes, excluded);
            if (avoidNoClueNodes && !alt.IsValid)
            {
                alt = Search(start, destination, pathType, graph, progress, equippedGears, avoidNoClueNodes: false, excluded);
                if (alt.IsValid) alt.NoClueAvoidanceFailed = true;
            }
            if (alt.IsValid) result.AlternativePath = alt;
        }

        return result;
    }

    private static PathResult Search(
        MapNodeData start,
        MapNodeData destination,
        PathType pathType,
        MapGraph graph,
        RouteProgressState progress,
        EmotionColor[] gears,
        bool avoidNoClueNodes,
        HashSet<string> excluded) => pathType switch
    {
        PathType.Shortest => BFS(start, destination, graph, progress, gears, avoidNoClueNodes, excluded),
        PathType.Balanced => Dijkstra(start, destination, graph, progress, gears, useHopPenalty: true, avoidNoClueNodes: avoidNoClueNodes, excluded: excluded),
        _                 => Dijkstra(start, destination, graph, progress, gears, useHopPenalty: false, avoidNoClueNodes: avoidNoClueNodes, excluded: excluded),
    };

    // 장비로 통과 불가능한 연결을 하나라도 포함하는지
    private static bool ContainsBlockedConnection(List<MapConnectionData> connections, EmotionColor[] gears)
    {
        foreach (var conn in connections)
            if (!conn.IsPassableWith(gears)) return true;
        return false;
    }

    // 노드가 단서를 보유한(또는 시작) 상태 = 지도 UI에서 "밝혀진 노드"와 동일한 기준.
    private static bool IsNodeRevealed(MapNodeData node, RouteProgressState progress) =>
        progress == null || node.isStartNode || progress.HasNodeClue(node);

    // 간선(current-neighbor)을 경로 계산에 쓸 수 있는지 판단 — 지도 UI의 "간선 표시" 규칙(둘 중 하나라도
    // 밝혀졌으면 표시)과 완전히 동일한 기준이다. 노드 단위로만 걸러내면, neighbor가 *다른* 밝혀진 노드를
    // 통해 독립적으로 "안다" 판정을 받았을 때 지금 건너는 이 간선 자체는 화면에 보이지도 않는데
    // 몰래 경로에 쓰이는 틈이 생긴다 — 그래서 반드시 "지금 이 간선"의 두 끝점 중 하나가 밝혀졌는지로 검사한다.
    // (current가 밝혀졌으면 어느 이웃으로든 한 칸까지 허용되고, current가 안 밝혀진 이웃(프론티어)이면
    //  neighbor 쪽이 밝혀진 경우만 통과 — 그 이상은 연쇄되지 않는다.)
    private static bool IsConnectionKnown(MapNodeData current, MapNodeData neighbor, RouteProgressState progress) =>
        IsNodeRevealed(current, progress) || IsNodeRevealed(neighbor, progress);

    // "단서 있는 경로 우선(avoidNoClueNodes)" 옵션 적용 시, neighbor를 경유지로 통과할 수 있는지 판단.
    // 목적지 자체는 단서가 없어도(=아직 밝혀지지 않은 프론티어 맵이어도) 항상 허용한다 —
    // 이 옵션은 "중간에 거치는" 단서 없는 맵을 최대한 피하자는 것이지, 목적지 선택 자체를 막는 게 아니다.
    private static bool CanPassThrough(MapNodeData neighbor, MapNodeData destination, RouteProgressState progress) =>
        neighbor.guid == destination.guid || IsNodeRevealed(neighbor, progress);

    // 현재 장비로 통과 불가능한 모든 연결의 GUID (차선 경로 탐색 시 제외 대상)
    private static HashSet<string> GetBlockedConnectionGuids(MapGraph graph, EmotionColor[] gears)
    {
        var blocked = new HashSet<string>();
        foreach (var conn in graph.AllConnections)
            if (!conn.IsPassableWith(gears)) blocked.Add(conn.guid);
        return blocked;
    }

    // ─── BFS (최단 경로) ──────────────────────────────────────────
    private static PathResult BFS(MapNodeData start, MapNodeData destination, MapGraph graph, RouteProgressState progress, EmotionColor[] gears, bool avoidNoClueNodes = false, HashSet<string> excluded = null)
    {
        var queue   = new Queue<MapNodeData>();
        var visited = new HashSet<string>();
        var prev    = new Dictionary<string, (MapNodeData node, MapConnectionData conn)>();

        queue.Enqueue(start);
        visited.Add(start.guid);
        prev[start.guid] = (null, null);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.guid == destination.guid)
                return BuildResult(start, destination, prev, progress, gears);

            foreach (var conn in graph.GetConnectionsFrom(current))
            {
                if (excluded != null && excluded.Contains(conn.guid)) continue;
                var neighbor = graph.GetNeighbor(conn, current);
                if (neighbor == null || visited.Contains(neighbor.guid)) continue;
                if (!IsConnectionKnown(current, neighbor, progress)) continue;
                if (avoidNoClueNodes && !CanPassThrough(neighbor, destination, progress)) continue;
                visited.Add(neighbor.guid);
                prev[neighbor.guid] = (current, conn);
                queue.Enqueue(neighbor);
            }
        }

        return new PathResult();
    }

    // ─── Dijkstra (최소 난이도 / 균형 경로) ──────────────────────
    // 엣지 비용 = difficulty (+ useHopPenalty면 hopPenalty 추가)
    // hopPenalty = 전체 연결의 평균 난이도 → 홉 1개 추가 비용이 평균 연결 1개와 동일해져
    // 최단보다 조금 길지만 쉽고, 최소난이도보다 조금 어렵지만 짧은 "균형" 경로가 나온다.
    // 비용은 항상 실제 난이도 사용. "단서 없는 연결 = 최하 판정"은 UI 표시 규칙이며
    // 탐색 비용에 적용하면 모든 엣지가 0이 되어 BFS와 동일해지는 문제가 생긴다.
    private static PathResult Dijkstra(
        MapNodeData start,
        MapNodeData destination,
        MapGraph graph,
        RouteProgressState progress,
        EmotionColor[] gears,
        bool useHopPenalty,
        bool avoidNoClueNodes = false,
        HashSet<string> excluded = null)
    {
        float hopPenalty = useHopPenalty ? CalcAvgDifficulty(graph, gears) : 0f;

        var dist = new Dictionary<string, float>();
        var prev = new Dictionary<string, (MapNodeData node, MapConnectionData conn)>();
        // (비용, 고유id, 노드) — 동점 시 id로 결정론적 정렬
        var pq = new SortedSet<(float cost, int id, MapNodeData node)>(
            Comparer<(float, int, MapNodeData)>.Create((a, b) =>
            {
                int c = a.Item1.CompareTo(b.Item1);
                return c != 0 ? c : a.Item2.CompareTo(b.Item2);
            }));

        int uid = 0;
        foreach (var node in graph.AllNodes)
            dist[node.guid] = float.MaxValue;

        dist[start.guid] = 0f;
        prev[start.guid] = (null, null);
        pq.Add((0f, uid++, start));

        while (pq.Count > 0)
        {
            var (cost, _, current) = pq.Min;
            pq.Remove(pq.Min);

            if (current.guid == destination.guid)
                return BuildResult(start, destination, prev, progress, gears);

            if (cost > dist[current.guid]) continue;

            foreach (var conn in graph.GetConnectionsFrom(current))
            {
                if (excluded != null && excluded.Contains(conn.guid)) continue;
                var neighbor = graph.GetNeighbor(conn, current);
                if (neighbor == null) continue;
                if (!IsConnectionKnown(current, neighbor, progress)) continue;
                if (avoidNoClueNodes && !CanPassThrough(neighbor, destination, progress)) continue;

                float edgeCost = DifficultyCalculator.Calculate(conn, gears) + hopPenalty;
                float newCost = dist[current.guid] + edgeCost;

                if (!dist.ContainsKey(neighbor.guid)) dist[neighbor.guid] = float.MaxValue;
                if (newCost < dist[neighbor.guid])
                {
                    dist[neighbor.guid] = newCost;
                    prev[neighbor.guid] = (current, conn);
                    pq.Add((newCost, uid++, neighbor));
                }
            }
        }

        return new PathResult();
    }

    private static float CalcAvgDifficulty(MapGraph graph, EmotionColor[] gears)
    {
        var conns = graph.AllConnections;
        if (conns.Count == 0) return 1f;
        float total = 0f;
        foreach (var c in conns)
            total += DifficultyCalculator.Calculate(c, gears);
        return total / conns.Count;
    }

    // ─── 경로 복원 ────────────────────────────────────────────────
    // TotalDifficulty: 클루 여부 무관하게 실제 난이도 합산 (표시용)
    // ContainsNoClueNode: 경로 중 단서 없는 노드 포함 여부 (start 제외, progress가 null이면 검사 생략)
    private static PathResult BuildResult(
        MapNodeData start,
        MapNodeData destination,
        Dictionary<string, (MapNodeData node, MapConnectionData conn)> prev,
        RouteProgressState progress,
        EmotionColor[] gears)
    {
        var nodes = new List<MapNodeData>();
        var conns = new List<MapConnectionData>();
        bool noClue = false;
        float totalDiff = 0f;

        var cur = destination;
        while (cur != null)
        {
            nodes.Insert(0, cur);
            if (progress != null && !progress.HasNodeClue(cur) && cur.guid != start.guid)
                noClue = true;

            var (prevNode, conn) = prev[cur.guid];
            if (conn != null)
            {
                conns.Insert(0, conn);
                totalDiff += DifficultyCalculator.Calculate(conn, gears);
            }
            cur = prevNode;
        }

        return new PathResult
        {
            Nodes              = nodes,
            Connections        = conns,
            TotalDifficulty    = totalDiff,
            ContainsNoClueNode = noClue
        };
    }
}
