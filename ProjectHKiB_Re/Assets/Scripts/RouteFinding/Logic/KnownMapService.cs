using System.Collections.Generic;

// "알려진 맵(known nodes)" 판정 — MapViewer.Refresh()가 지도에 표시할 노드를 정하는 기준
// (밝혀진 노드 + 그 노드에 닿은 간선의 반대편 노드까지)과 완전히 동일하다. 노트의 "미획득 후보
// 자동 노출"(NoteSystem_기획서.md 규칙 3), 도감의 "미발견 단서 자리 표시"(Clue_System.md 6-2)가
// 공유한다 — 지도에 안 보이는 완전 미확인 맵의 정보가 다른 화면으로 새어나가지 않으려면
// 반드시 같은 기준을 재사용해야 한다. 무상태 정적 클래스 — MapPathFinder/DifficultyCalculator와 같은 패턴.
public static class KnownMapService
{
    public static HashSet<string> ComputeKnownNodeGuids(MapGraph graph, RouteProgressState progress)
    {
        var revealed = new HashSet<string>();
        foreach (var node in graph.AllNodes)
            if (node.isStartNode || progress.HasNodeClue(node)) revealed.Add(node.guid);

        var known = new HashSet<string>(revealed);
        foreach (var conn in graph.AllConnections)
        {
            if (!revealed.Contains(conn.fromGuid) && !revealed.Contains(conn.toGuid)) continue;
            known.Add(conn.fromGuid);
            known.Add(conn.toGuid);
        }
        return known;
    }
}
