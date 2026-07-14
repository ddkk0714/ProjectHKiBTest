using System;
using System.Collections.Generic;
using UnityEngine;

// 노트 전용 싱글턴 — CodexModule과 같은 패턴으로, 노트에 담긴 NoteEntry 목록의 단일 소유자.
//
// 도감(CodexModule)은 "획득한 단서 전체"를 보여주는 반면, 노트는 그중 실제로 쓸 것만 추려
// 다중 목적지 이동 계획에 쓴다(NoteSystem_기획서.md). 규칙 1(경로 연동 자동 편입)·규칙 2(도감 수동 핀)·
// 4단계(다중 목적지 이동 계획, 자동 순차 실행)가 동작한다. 세이브 연동(5단계)만 남음.
// (2026-07-14: 규칙 3 "미획득 후보 자동 노출"은 요청으로 제거됨 — 노트는 이제 항상 획득한 단서만 다룬다.)
public class NoteModule : MonoBehaviour
{
    private static NoteModule _instance;
    private static bool _isQuitting; // 종료 중에는 다른 오브젝트의 OnDestroy가 Instance를 건드려도 재생성하지 않는다.

    public static NoteModule Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying && !_isQuitting)
            {
                _instance = FindObjectOfType<NoteModule>();
                if (_instance == null)
                    _instance = new GameObject(nameof(NoteModule)).AddComponent<NoteModule>();
            }
            return _instance;
        }
    }

    // 플레이 모드 종료(에디터) / 앱 종료 시 OnDestroy들보다 먼저 호출되는 것이 보장된다 —
    // CodexModule과 동일한 이유로, 종료 도중 재생성을 막는다.
    private void OnApplicationQuit() => _isQuitting = true;

    private readonly List<NoteEntry> _entries = new();
    public IReadOnlyList<NoteEntry> Entries => _entries;

    // NotePanel 등 UI가 구독 — 노트 목록이 바뀔 때마다 다시 그리라는 신호.
    public event Action OnNoteChanged;

    // 이동 중에는 노트 편집(항목 삭제·핀·이동 계획 변경 등)을 잠근다 — 열람(NotePanel.Open)과는 분리된 권한
    // (NoteSystem_기획서.md "UI 진입 / 영속성" 절의 NoteModule.CanEditPlan 규칙을 1단계 기능인
    // 개별 삭제에도 동일하게 적용한 것 — 장비·경로가 이동 중 변경 불가한 것과 같은 이유).
    public bool CanEdit => RouteModule.Instance == null || !RouteModule.Instance.IsTraveling;

    private bool _subscribed;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[NoteModule] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start() => TrySubscribe();

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_subscribed && RouteModule.Instance != null)
        {
            RouteModule.Instance.OnRouteSelected -= HandleRouteSelected;
            RouteModule.Instance.OnTravelEnded -= HandleTravelEnded;
        }
    }

    private void TrySubscribe()
    {
        if (_subscribed || RouteModule.Instance == null) return;
        RouteModule.Instance.OnRouteSelected += HandleRouteSelected;
        RouteModule.Instance.OnTravelEnded += HandleTravelEnded;
        _subscribed = true;
    }

    private void HandleRouteSelected(PathResult route)
    {
        Debug.Log($"[NoteModule] HandleRouteSelected — route={(route == null ? "null" : $"valid={route.IsValid},nodes={route.Nodes?.Count}")}");
        TrySubscribe(); // RouteModule이 이 컴포넌트보다 늦게 준비된 경우를 대비
        RebuildRouteLinkedEntries(route);
    }

    // 규칙 1(경로 연동 자동 편입): 선택된 경로가 지나는 맵들과 연관된, 이미 획득한 단서를 노트에 채운다.
    // "연관"의 기준은 ClueData.codexMapGuid(도감 분류 기준, Clue_System.md 1-1장)가
    // 경로의 노드 목록에 포함되는지 — NoteSystem_기획서.md "노트 편입 규칙 1"에 명시된 그대로.
    public void RebuildRouteLinkedEntries(PathResult route)
    {
        _entries.RemoveAll(e => e.reason == NotePinReason.RouteLinked);

        var progress = RouteModule.Instance != null ? RouteModule.Instance.Progress : null;
        var graph = MapGraph.Instance;
        Debug.Log($"[NoteModule] RebuildRouteLinkedEntries 입력 — route={(route == null ? "null" : $"valid={route.IsValid}")}, progress={(progress != null)}, graph={(graph != null)}, acquiredClues={progress?.AcquiredClueIds?.Count}");
        if (route != null && route.IsValid && progress != null && graph != null)
        {
            var routeNodeGuids = new HashSet<string>();
            foreach (var node in route.Nodes) routeNodeGuids.Add(node.guid);

            foreach (var clueId in progress.AcquiredClueIds)
            {
                var clue = graph.GetClue(clueId);
                if (clue == null || string.IsNullOrEmpty(clue.codexMapGuid)) continue;
                if (!routeNodeGuids.Contains(clue.codexMapGuid)) continue;

                // 이미 다른 경로로(수동 핀 등) 노트에 들어와 있으면 중복 추가하지 않는다.
                bool alreadyPresent = false;
                foreach (var existing in _entries)
                {
                    if (existing.clueId == clueId) { alreadyPresent = true; break; }
                }
                if (alreadyPresent) continue;

                _entries.Add(new NoteEntry
                {
                    clueId = clueId,
                    reason = NotePinReason.RouteLinked,
                });
            }
        }

        int routeLinkedCount = 0;
        foreach (var e in _entries) if (e.reason == NotePinReason.RouteLinked) routeLinkedCount++;
        Debug.Log($"[NoteModule] RebuildRouteLinkedEntries 완료 — RouteLinked 항목 {routeLinkedCount}개, 전체 {_entries.Count}개");
        OnNoteChanged?.Invoke();
    }

    // 1단계: 노트 항목 개별 삭제. 이동 중이면 거부(CanEdit) — 재선택 시 RebuildRouteLinkedEntries가
    // 다시 채울 수 있는 RouteLinked 항목도 동일하게 삭제 대상이다(2단계 이전엔 재선택 전까지는 유지됨).
    public bool RemoveEntry(string clueId)
    {
        if (!CanEdit)
        {
            Debug.LogWarning("[NoteModule] 이동 중에는 노트를 편집할 수 없습니다.");
            return false;
        }

        int removed = _entries.RemoveAll(e => e.clueId == clueId);
        if (removed > 0) OnNoteChanged?.Invoke();
        return removed > 0;
    }

    // ─── 2단계: 도감 → 노트 수동 핀 ──────────────────────────────

    public bool IsPinned(string clueId) => _entries.Exists(e => e.clueId == clueId);

    // 도감 카드의 "노트에 핀" 액션이 호출한다(CodexPanel 참고 — 획득한 단서에만 핀 버튼이 뜨므로
    // clueId는 항상 이미 획득한 단서를 가리킨다). 이미 핀돼 있거나(경로 연동 포함) 이동 중이면 거부.
    public bool AddManualPin(string clueId)
    {
        if (string.IsNullOrEmpty(clueId)) return false;
        if (!CanEdit)
        {
            Debug.LogWarning("[NoteModule] 이동 중에는 노트를 편집할 수 없습니다.");
            return false;
        }
        if (IsPinned(clueId)) return false;

        _entries.Add(new NoteEntry
        {
            clueId = clueId,
            reason = NotePinReason.ManualPin,
        });
        OnNoteChanged?.Invoke();
        return true;
    }

    // ─── 4단계: 다중 목적지 이동 계획 ─────────────────────────────

    private readonly List<RouteWaypointPlan> _plans = new();
    public IReadOnlyList<RouteWaypointPlan> Plans => _plans;

    // 한 번에 하나의 계획만 실행 중일 수 있다 — RouteModule 자체가 단일 이동 상태만 가지므로 자연스러운 제약.
    private NotePlanExecutionState _execution;
    public NotePlanExecutionState CurrentExecution => _execution;

    public event Action<RouteWaypointPlan> OnPlanCompleted;
    public event Action<RouteWaypointPlan> OnPlanHalted;

    public RouteWaypointPlan GetPlan(string planId) => _plans.Find(p => p.planId == planId);

    public RouteWaypointPlan CreatePlan(string name = null)
    {
        var plan = new RouteWaypointPlan
        {
            planId = Guid.NewGuid().ToString("N"),
            planName = string.IsNullOrWhiteSpace(name) ? "새 계획" : name,
            pathType = PathType.Shortest,
        };
        _plans.Add(plan);
        OnNoteChanged?.Invoke();
        return plan;
    }

    public bool RemovePlan(string planId)
    {
        if (!CanEdit) { Debug.LogWarning("[NoteModule] 이동 중에는 계획을 편집할 수 없습니다."); return false; }
        if (_execution != null && _execution.planId == planId)
        {
            Debug.LogWarning("[NoteModule] 실행 중인 계획은 삭제할 수 없습니다.");
            return false;
        }
        int removed = _plans.RemoveAll(p => p.planId == planId);
        if (removed > 0) OnNoteChanged?.Invoke();
        return removed > 0;
    }

    public bool SetPlanPathType(string planId, PathType type)
    {
        if (!CanEdit) { Debug.LogWarning("[NoteModule] 이동 중에는 계획을 편집할 수 없습니다."); return false; }
        var plan = GetPlan(planId);
        if (plan == null) return false;
        plan.pathType = type;
        OnNoteChanged?.Invoke();
        return true;
    }

    // 노트 항목(clueId)이 가리키는 단서의 targetMapGuid를 목적지로 추가한다 — 편입 규칙에 명시된
    // "목적지 성격을 가진 것(targetMapGuid가 있는 것)을 골라 계획에 추가"에 대응.
    public bool AddWaypoint(string planId, string mapGuid)
    {
        if (!CanEdit) { Debug.LogWarning("[NoteModule] 이동 중에는 계획을 편집할 수 없습니다."); return false; }
        var plan = GetPlan(planId);
        if (plan == null || string.IsNullOrEmpty(mapGuid)) return false;
        plan.orderedMapGuids.Add(mapGuid);
        OnNoteChanged?.Invoke();
        return true;
    }

    public bool RemoveWaypoint(string planId, int index)
    {
        if (!CanEdit) { Debug.LogWarning("[NoteModule] 이동 중에는 계획을 편집할 수 없습니다."); return false; }
        var plan = GetPlan(planId);
        if (plan == null || index < 0 || index >= plan.orderedMapGuids.Count) return false;
        plan.orderedMapGuids.RemoveAt(index);
        OnNoteChanged?.Invoke();
        return true;
    }

    // direction: -1(위로) 또는 +1(아래로).
    public bool MoveWaypoint(string planId, int index, int direction)
    {
        if (!CanEdit) { Debug.LogWarning("[NoteModule] 이동 중에는 계획을 편집할 수 없습니다."); return false; }
        var plan = GetPlan(planId);
        if (plan == null) return false;
        int newIndex = index + direction;
        if (index < 0 || index >= plan.orderedMapGuids.Count || newIndex < 0 || newIndex >= plan.orderedMapGuids.Count)
            return false;

        (plan.orderedMapGuids[index], plan.orderedMapGuids[newIndex]) = (plan.orderedMapGuids[newIndex], plan.orderedMapGuids[index]);
        OnNoteChanged?.Invoke();
        return true;
    }

    // 계획 미리보기 — RoutePlanEditorView가 "구간 N개 / 총 난이도 X / 통과불가 여부"를 보여주는 데 쓴다.
    // 시작점은 RouteModule.CurrentLocation(이동 중이 아닐 때도 유지되는 마지막 위치) 기준.
    public RouteWaypointPlanner.PlanPreview ComputePreview(string planId)
    {
        var plan = GetPlan(planId);
        var route = RouteModule.Instance;
        var graph = MapGraph.Instance;
        if (plan == null || route == null || graph == null)
            return new RouteWaypointPlanner.PlanPreview { IsValid = false };

        return RouteWaypointPlanner.ComputePreview(route.CurrentLocation, plan, graph, route.Progress, route.EquippedGearArray, route.AvoidNoClueNodes);
    }

    // ─── 실행 (자동 순차 트리거, NoteSystem_기획서.md "실행 방식" 확정 사항) ──────

    public bool ExecutePlan(string planId)
    {
        var route = RouteModule.Instance;
        if (route == null || route.IsTraveling)
        {
            Debug.LogWarning("[NoteModule] 이미 이동 중이라 새 계획을 실행할 수 없습니다.");
            return false;
        }
        var plan = GetPlan(planId);
        if (plan == null || plan.orderedMapGuids.Count == 0) return false;

        _execution = new NotePlanExecutionState { planId = planId, currentLegIndex = 0, isHalted = false };
        bool started = StartLeg(plan, 0);
        OnNoteChanged?.Invoke();
        return started;
    }

    // 전투 실패 등으로 중단(isHalted)된 계획을 같은 구간부터 다시 시작 — 자동 재개는 하지 않고
    // 플레이어가 직접 눌러야 한다(NoteSystem_기획서.md "실행 방식" 4번 참고).
    public bool ResumePlan(string planId)
    {
        if (_execution == null || _execution.planId != planId || !_execution.isHalted) return false;
        if (RouteModule.Instance == null || RouteModule.Instance.IsTraveling) return false;

        var plan = GetPlan(planId);
        if (plan == null) return false;

        _execution.isHalted = false;
        bool started = StartLeg(plan, _execution.currentLegIndex);
        OnNoteChanged?.Invoke();
        return started;
    }

    // 목적지 정상 도달(completed=true)이면 다음 구간으로 자동 연쇄, 중단(false)이면 실행을 멈추고 대기.
    // 노트 계획 실행 중이 아닐 때(일반 단일 경로 이동)는 무시한다.
    private void HandleTravelEnded(bool completed)
    {
        if (_execution == null) return;
        var plan = GetPlan(_execution.planId);
        if (plan == null) { _execution = null; OnNoteChanged?.Invoke(); return; }

        if (!completed)
        {
            _execution.isHalted = true;
            OnPlanHalted?.Invoke(plan);
            OnNoteChanged?.Invoke();
            return;
        }

        _execution.currentLegIndex++;
        if (_execution.currentLegIndex >= plan.orderedMapGuids.Count)
        {
            _execution = null;
            OnPlanCompleted?.Invoke(plan);
            OnNoteChanged?.Invoke();
            return;
        }

        StartLeg(plan, _execution.currentLegIndex);
        OnNoteChanged?.Invoke();
    }

    // 한 구간을 계산해 RouteModule에 선택·출발시킨다. 실패(도달 불가/통과 불가/거부)하면 실행을 멈춘다.
    private bool StartLeg(RouteWaypointPlan plan, int legIndex)
    {
        var route = RouteModule.Instance;
        var graph = MapGraph.Instance;
        if (route == null || graph == null) { HaltExecution(plan); return false; }

        var leg = RouteWaypointPlanner.ComputeLeg(route.CurrentLocation, plan, legIndex, graph, route.Progress, route.EquippedGearArray, route.AvoidNoClueNodes);
        if (!leg.IsValid || leg.IsBlocked)
        {
            Debug.LogWarning($"[NoteModule] 계획 '{plan.planName}' 구간 {legIndex}을 진행할 수 없습니다 (도달 불가 또는 통과 불가).");
            HaltExecution(plan);
            return false;
        }

        if (!route.SelectRoute(leg) || !route.StartTravel())
        {
            HaltExecution(plan);
            return false;
        }
        return true;
    }

    private void HaltExecution(RouteWaypointPlan plan)
    {
        if (_execution != null) _execution.isHalted = true;
        OnPlanHalted?.Invoke(plan);
    }
}
