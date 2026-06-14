using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

// 에디터 인스펙터에서 [Button]으로 루트파인딩 시스템을 검증하는 테스트 컴포넌트.
// MapGraph / RouteSpawnManager 는 씬에 있어야 하고, RouteModule은 없으면 자동 생성된다.
// 노드는 map_database.json에 정의된 GUID 문자열로 참조한다.
// 진행 상태(방문/단서/클리어)는 RouteModule.Instance.Progress를 통해 조작한다.
public class RouteSystemTest : MonoBehaviour
{
    [Header("경로 탐색 테스트")]
    [SerializeField] private string _startNodeGuid;
    [SerializeField] private string _destinationNodeGuid;
    [SerializeField] private EmotionColor[] _testEquippedGears;

    [Header("침대 스폰 테스트")]
    [SerializeField] private string _testBedNodeGuid;

    [Header("단서 획득 테스트")]
    [SerializeField] private string _testClueMapGuid;
    [SerializeField] private string _testEventKey;

    [Header("결과 (읽기 전용)")]
    [SerializeField, ReadOnly] private string _shortestPath;
    [SerializeField, ReadOnly] private string _balancedPath;
    [SerializeField, ReadOnly] private string _minDiffPath;
    [SerializeField, ReadOnly] private float _shortestTotalDiff;
    [SerializeField, ReadOnly] private float _balancedTotalDiff;
    [SerializeField, ReadOnly] private float _minDiff;
    [SerializeField, ReadOnly] private bool _pathContainsNoClue;

    // ─── 경로 탐색 ───────────────────────────────────────────────

    [Button("최단 경로 탐색")]
    private void TestShortestPath() =>
        RunPathTest(PathType.Shortest, ref _shortestPath, ref _shortestTotalDiff, "최단", trackNoClue: true);

    [Button("균형 경로 탐색")]
    private void TestBalancedPath() =>
        RunPathTest(PathType.Balanced, ref _balancedPath, ref _balancedTotalDiff, "균형", trackNoClue: true);

    [Button("최소 난이도 경로 탐색")]
    private void TestMinDifficultyPath() =>
        RunPathTest(PathType.MinDifficulty, ref _minDiffPath, ref _minDiff, "최소난이도");

    private void RunPathTest(PathType type, ref string display, ref float diff, string label, bool trackNoClue = false)
    {
        if (!ValidateGraph()) return;
        var (start, dest) = GetNodes();
        if (start == null || dest == null) return;

        // 테스트 전용 장비(_testEquippedGears)를 쓰므로 모듈 래퍼 대신 MapPathFinder를 직접 호출한다.
        var result = MapPathFinder.FindPath(start, dest, type, MapGraph.Instance, RouteModule.Instance.Progress, _testEquippedGears);
        ApplyResult(result, ref display, ref diff, label);
        if (trackNoClue) _pathContainsNoClue = result.ContainsNoClueNode;
    }

    // ─── 난이도 계산 ─────────────────────────────────────────────

    [Button("연결 목록 난이도 출력 (장비 적용)")]
    private void TestDifficultyAll()
    {
        if (!ValidateGraph()) return;

        foreach (var conn in MapGraph.Instance.AllConnections)
        {
            float diff = DifficultyCalculator.Calculate(conn, _testEquippedGears);
            bool hasClue = RouteModule.Instance.Progress.HasConnectionClue(conn);
            bool passable = conn.IsPassableWith(_testEquippedGears);
            var from = MapGraph.Instance.GetNode(conn.fromGuid);
            var to   = MapGraph.Instance.GetNode(conn.toGuid);
            Debug.Log($"[난이도] {from?.nodeName}→{to?.nodeName}  diff={diff:F1}  단서={hasClue}  통과가능={passable}");
        }
    }

    // ─── 스폰 / 사망 ─────────────────────────────────────────────

    [Button("침대 스폰 등록")]
    private void TestRegisterBed()
    {
        if (!ValidateGraph()) return;
        var bed = MapGraph.Instance.GetNode(_testBedNodeGuid);
        if (bed == null) { Debug.LogError($"[RouteSystemTest] GUID에 해당하는 노드가 없습니다: {_testBedNodeGuid}"); return; }
        RouteSpawnManager.Instance.RegisterBedSpawn(bed);
    }

    [Button("사망 시뮬레이션 (맵 진도 손실 + 스폰 복귀)")]
    private void TestDeath()
    {
        var spawnNode = RouteSpawnManager.Instance.ConsumeRespawnNode();
        Debug.Log($"[RouteSystemTest] 사망 처리 완료 — 복귀: {spawnNode?.nodeName ?? "없음"}");
        Debug.Log("[RouteSystemTest] ※ 실게임에서는 RouteModule.Instance.RevertToLastSave(lastSaved) 호출 필요");
    }

    // ─── 단서 획득 ───────────────────────────────────────────────

    [Button("맵 방문 처리")]
    private void TestVisitMap()
    {
        if (!ValidateGraph()) return;
        var node = MapGraph.Instance.GetNode(_testClueMapGuid);
        if (node == null) { Debug.LogError($"[RouteSystemTest] GUID에 해당하는 노드가 없습니다: {_testClueMapGuid}"); return; }
        RouteModule.Instance.Progress.MarkNodeVisited(node);
        Debug.Log($"[RouteSystemTest] 방문 처리: {node.nodeName}");
    }

    [Button("이벤트 발생 (단서 획득 시도)")]
    private void TestTriggerEvent()
    {
        if (!ValidateGraph()) return;
        var node = MapGraph.Instance.GetNode(_testClueMapGuid);
        if (node == null) { Debug.LogError($"[RouteSystemTest] GUID에 해당하는 노드가 없습니다: {_testClueMapGuid}"); return; }
        RouteModule.Instance.Progress.SetEventFlag(node.guid, _testEventKey);
        Debug.Log($"[RouteSystemTest] 이벤트 발생: {node.nodeName} / {_testEventKey}");
    }

    [Button("획득한 단서 목록 출력")]
    private void TestPrintAcquiredClues()
    {
        if (!ValidateGraph()) return;
        foreach (var clueId in RouteModule.Instance.Progress.AcquiredClueIds)
        {
            var clue = MapGraph.Instance.GetClue(clueId);
            if (clue == null) continue;
            var target = MapGraph.Instance.GetNode(clue.targetMapGuid);
            Debug.Log($"[RouteSystemTest] 단서 획득됨: {clue.name} — {clue.description}  (대상 맵: {target?.nodeName ?? "없음"})");
        }
    }

    // ─── 경로 선택 → 출발 시뮬레이션 ────────────────────────────

    [Button("최단 경로로 출발 시뮬레이션")]
    private void TestStartTravel()
    {
        if (!ValidateGraph()) return;
        var (start, dest) = GetNodes();
        if (start == null || dest == null) return;

        var result = RouteModule.Instance.FindPath(start, dest, PathType.Shortest);
        if (!result.IsValid) { Debug.LogWarning("[RouteSystemTest] 유효한 경로가 없습니다."); return; }

        RouteModule.Instance.SelectRoute(result);
        RouteModule.Instance.StartTravel();
    }

    [Button("다음 노드로 진행 (전투 완료 시뮬레이션)")]
    private void TestAdvance()
    {
        if (!RouteModule.Instance.IsTraveling)
        {
            Debug.LogWarning("[RouteSystemTest] 이동 중이 아닙니다. 먼저 출발하세요.");
            return;
        }
        var conn = RouteModule.Instance.GetCurrentConnection();
        if (conn != null)
        {
            RouteModule.Instance.Progress.MarkConnectionCleared(conn);
            var from = MapGraph.Instance.GetNode(conn.fromGuid);
            var to   = MapGraph.Instance.GetNode(conn.toGuid);
            Debug.Log($"[RouteSystemTest] 연결 클리어: {from?.nodeName}→{to?.nodeName}");
        }
        RouteModule.Instance.AdvanceToNextNode();
    }

    // ─── 내부 헬퍼 ───────────────────────────────────────────────

    private bool ValidateGraph()
    {
        if (MapGraph.Instance == null) { Debug.LogError("[RouteSystemTest] MapGraph가 씬에 없습니다."); return false; }
        return true;
    }

    private (MapNodeData start, MapNodeData dest) GetNodes()
    {
        var start = MapGraph.Instance.GetNode(_startNodeGuid);
        var dest  = MapGraph.Instance.GetNode(_destinationNodeGuid);
        if (start == null) Debug.LogError($"[RouteSystemTest] 시작 노드를 찾을 수 없습니다: {_startNodeGuid}");
        if (dest == null)  Debug.LogError($"[RouteSystemTest] 목적지 노드를 찾을 수 없습니다: {_destinationNodeGuid}");
        return (start, dest);
    }

    private static void ApplyResult(PathResult result, ref string display, ref float diffOut, string label)
    {
        if (!result.IsValid)
        {
            display = "경로 없음";
            diffOut = 0f;
            Debug.LogWarning($"[RouteSystemTest][{label}] 경로를 찾을 수 없습니다.");
            return;
        }

        var names = new List<string>();
        foreach (var n in result.Nodes) names.Add(n.nodeName);
        display = string.Join(" → ", names);
        diffOut = result.TotalDifficulty;

        Debug.Log($"[RouteSystemTest][{label}] {display}  총난이도={result.TotalDifficulty:F1}  단서없는맵={result.ContainsNoClueNode}");

        if (result.IsBlocked)
        {
            if (result.AlternativePath != null && result.AlternativePath.IsValid)
            {
                var altNames = new List<string>();
                foreach (var n in result.AlternativePath.Nodes) altNames.Add(n.nodeName);
                Debug.LogWarning($"[RouteSystemTest][{label}] ↑ 통과 불가 구간 포함 — 선택 불가. 차선 경로: " +
                    $"{string.Join(" → ", altNames)}  총난이도={result.AlternativePath.TotalDifficulty:F1}");
            }
            else
            {
                Debug.LogWarning($"[RouteSystemTest][{label}] ↑ 통과 불가 구간 포함 — 선택 불가. 차선 경로 없음 (도달 불가).");
            }
        }
    }
}
