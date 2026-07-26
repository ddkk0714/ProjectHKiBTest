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
    [SerializeField] private bool _testAvoidNoClueNodes;

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
        var result = MapPathFinder.FindPath(start, dest, type, MapGraph.Instance, RouteModule.Instance.Progress, _testEquippedGears, _testAvoidNoClueNodes);
        ApplyResult(result, ref display, ref diff, label);
        if (trackNoClue) _pathContainsNoClue = result.ContainsNoClueNode;
    }

    // ─── 난이도 계산 ─────────────────────────────────────────────

    [Button("맵 목록 난이도 출력 (장비 적용)")]
    private void TestDifficultyAll()
    {
        if (!ValidateGraph()) return;

        // 2026-07-14 — 전투(난이도/통과 조건)가 연결에서 맵으로 이동해 맵 기준으로 순회한다.
        foreach (var node in MapGraph.Instance.AllNodes)
        {
            float diff = DifficultyCalculator.Calculate(node, _testEquippedGears);
            bool hasClue = RouteModule.Instance.Progress.HasNodeClue(node);
            bool passable = node.IsPassableWith(_testEquippedGears);
            Debug.Log($"[난이도] {node.nodeName}  diff={diff:F1}  단서={hasClue}  통과가능={passable}");
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
        // 테스트용 — 실제로는 사망을 감지한 쪽(전투/플레이어 시스템)이 플레이어의 SaveModule에서
        // resolve한 CurrentSaveData(또는 LoadedData)를 넘겨야 한다. 여기서는 null(한 번도 세이브
        // 안 한 상태와 동일하게 처리됨 — RouteModule.RevertToLastSave 참고)로 시뮬레이션한다.
        var respawnNode = DeathHandler.Instance.HandleDeath(null);
        Debug.Log($"[RouteSystemTest] 사망 처리 완료 — 복귀: {respawnNode?.nodeName ?? "없음"}, 현재 위치: {RouteModule.Instance.CurrentLocation?.nodeName}");
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

    // 방문·이벤트 조건을 전부 무시하고 모든 단서를 즉시 획득 처리 — 도감/노트 UI를 실제 플레이 없이
    // 빠르게 검증할 때 쓴다(RouteProgressState.ForceAcquireAllClues 참고).
    [Button("단서 - 전체 획득 처리 (테스트)")]
    private void TestForceAcquireAllClues()
    {
        if (!ValidateGraph()) return;
        int before = RouteModule.Instance.Progress.AcquiredClueIds.Count;
        RouteModule.Instance.Progress.ForceAcquireAllClues();
        int after = RouteModule.Instance.Progress.AcquiredClueIds.Count;
        Debug.Log($"[RouteSystemTest] 단서 전체 획득 처리 완료 — {before} → {after} (전체 {MapGraph.Instance.AllClues.Count}개)");
    }

    // ─── 노트(Note) 0단계 — 경로 연동 자동 편입 ─────────────────

    [Button("노트 - 최단 경로 선택 후 연동 항목 출력")]
    private void TestNoteRouteLinked()
    {
        if (!ValidateGraph()) return;
        var (start, dest) = GetNodes();
        if (start == null || dest == null) return;

        var result = RouteModule.Instance.FindPath(start, dest, PathType.Shortest);
        if (!result.IsValid) { Debug.LogWarning("[RouteSystemTest] 유효한 경로가 없습니다."); return; }

        RouteModule.Instance.SelectRoute(result); // NoteModule.OnRouteSelected 구독 → 자동 재계산

        if (NoteModule.Instance.Entries.Count == 0)
        {
            Debug.Log("[RouteSystemTest][Note] 노트가 비어 있습니다 (경로에 포함된 맵과 연관된 획득 단서가 없음).");
            return;
        }
        foreach (var entry in NoteModule.Instance.Entries)
        {
            var clue = MapGraph.Instance.GetClue(entry.clueId);
            Debug.Log($"[RouteSystemTest][Note] {clue?.name ?? entry.clueId} — {entry.reason}");
        }
    }

    // [2026-07-21] 노트의 다중 목적지 이동 계획(4단계) 테스트 버튼("계획 생성 후 실행"/"계획 다음 구간
    // 진행")은 그 기능 자체(RouteWaypointPlan/NoteModule.CreatePlan 등)가 요청으로 완전히 제거되면서
    // 함께 삭제됨 — 대체 기능(단서 서랍, ClueDrawerView)은 드래그 UI 조작이라 이런 버튼형 테스트로는
    // 검증하기 어려워 별도 테스트 버튼을 두지 않았다. NoteSystem_기획서.md "단서 서랍으로 교체" 참고.

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
        var target = RouteModule.Instance.GetCurrentTargetNode();
        if (target != null)
        {
            RouteModule.Instance.Progress.MarkNodeCleared(target);
            Debug.Log($"[RouteSystemTest] 맵 클리어: {target.nodeName}");
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

        Debug.Log($"[RouteSystemTest][{label}] {display}  총난이도={result.TotalDifficulty:F1}  단서없는맵={result.ContainsNoClueNode}" +
            (result.NoClueAvoidanceFailed ? "  (단서만 있는 경로 없음 → 일반 경로로 대체됨)" : ""));

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
