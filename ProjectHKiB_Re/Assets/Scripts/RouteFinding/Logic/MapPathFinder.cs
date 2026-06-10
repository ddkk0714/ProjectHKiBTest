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

    public bool IsValid => Nodes != null && Nodes.Count >= 2;
    public bool IsSelectable => IsValid && !IsBlocked;
}

public static class MapPathFinder
{
    public static PathResult FindPath(
        MapNodeData start,
        MapNodeData destination,
        PathType pathType,
        MapGraph graph,
        EmotionColor[] equippedGears = null)
    {
        if (start == null || destination == null || graph == null)
            return new PathResult();

        var result = pathType switch
        {
            PathType.Shortest => BFS(start, destination, graph, equippedGears),
            PathType.Balanced => Dijkstra(start, destination, graph, equippedGears, useHopPenalty: true),
            _                 => Dijkstra(start, destination, graph, equippedGears, useHopPenalty: false),
        };

        if (!result.IsValid) return result;

        result.IsBlocked = ContainsBlockedConnection(result.Connections, equippedGears);
        if (result.IsBlocked)
        {
            // 차선 경로: 통과 불가 연결을 그래프에서 모두 제외하고 같은 기준으로 재탐색
            var excluded = GetBlockedConnectionGuids(graph, equippedGears);
            var alt = pathType switch
            {
                PathType.Shortest => BFS(start, destination, graph, equippedGears, excluded),
                PathType.Balanced => Dijkstra(start, destination, graph, equippedGears, useHopPenalty: true, excluded),
                _                 => Dijkstra(start, destination, graph, equippedGears, useHopPenalty: false, excluded),
            };
            if (alt.IsValid) result.AlternativePath = alt;
        }

        return result;
    }

    // 장비로 통과 불가능한 연결을 하나라도 포함하는지
    private static bool ContainsBlockedConnection(List<MapConnectionData> connections, EmotionColor[] gears)
    {
        foreach (var conn in connections)
            if (!conn.IsPassableWith(gears)) return true;
        return false;
    }

    // 현재 장비로 통과 불가능한 모든 연결의 GUID (차선 경로 탐색 시 제외 대상)
    private static HashSet<string> GetBlockedConnectionGuids(MapGraph graph, EmotionColor[] gears)
    {
        var blocked = new HashSet<string>();
        foreach (var conn in graph.AllConnections)
            if (!conn.IsPassableWith(gears)) blocked.Add(conn.guid);
        return blocked;
    }

    // ─── BFS (최단 경로) ──────────────────────────────────────────
    private static PathResult BFS(MapNodeData start, MapNodeData destination, MapGraph graph, EmotionColor[] gears, HashSet<string> excluded = null)
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
                return BuildResult(start, destination, prev, graph, gears);

            foreach (var conn in graph.GetConnectionsFrom(current))
            {
                if (excluded != null && excluded.Contains(conn.guid)) continue;
                var neighbor = graph.GetNeighbor(conn, current);
                if (neighbor == null || visited.Contains(neighbor.guid)) continue;
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
        EmotionColor[] gears,
        bool useHopPenalty,
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
                return BuildResult(start, destination, prev, graph, gears);

            if (cost > dist[current.guid]) continue;

            foreach (var conn in graph.GetConnectionsFrom(current))
            {
                if (excluded != null && excluded.Contains(conn.guid)) continue;
                var neighbor = graph.GetNeighbor(conn, current);
                if (neighbor == null) continue;

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
    // ContainsNoClueNode: 경로 중 단서 없는 노드 포함 여부 (start 제외)
    private static PathResult BuildResult(
        MapNodeData start,
        MapNodeData destination,
        Dictionary<string, (MapNodeData node, MapConnectionData conn)> prev,
        MapGraph graph,
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
            if (!graph.HasNodeClue(cur) && cur.guid != start.guid)
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
