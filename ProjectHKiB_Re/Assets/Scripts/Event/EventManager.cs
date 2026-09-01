using System;
using System.Collections.Generic;
using UnityEngine;
using AYellowpaper.SerializedCollections;

[Serializable]
public class EventTargets
{
    public EventTargets()
    {
        targetEntities = new();
        targetAnimations = new();
    }
    public SerializedDictionary<string, EventControllableEntity> targetEntities;
    public SerializedDictionary<string, EventControllableAnimation> targetAnimations;
}

public class EventManager : StateController, IEventSaveProvider
{
    public enum TargetSearchType { Player, FromMap, Manual }

    [Header("Event Diagnostics")]
    [SerializeField, Min(1f)] private float _stallWarningSeconds = 10f;
    private StateSO _diagnosticState;
    private float _diagnosticStateEnteredAt;
    private float _nextStallWarningAt;

    // 스토리/월드 이벤트 플래그의 단일 진실 — 인스펙터에서 저작한 값도, 플레이 중 인스펙터에서
    // 직접 고친 값도, SetEventFlag로 들어온 값도 전부 여기 하나에 모인다. 판정·세이브 모두
    // 이 딕셔너리를 본다.
    public SerializedDictionary<EventFlagSO, int> eventFlags;

    // ─── 세이브에서 읽었지만 에셋을 못 찾은 항목 (2026-07-28) ────────
    // 세이브 파일에는 에셋 참조를 넣을 수 없어 문자열 ID(EventFlagSO.Id = 에셋 GUID)로 저장하는데,
    // 로드할 때 그 ID로 EventFlagSO를 되찾으려면 에셋들이 Resources 폴더 아래 있거나 별도
    // 레지스트리 에셋이 있어야 한다. 대신 이 프로젝트가 가진 성질을 이용한다 — 플래그를 읽는 쪽
    // (EventControllableEntity/Animation.Initialize, SetEventFlagAction)은 **항상** EventFlagSO
    // 참조를 손에 들고 들어온다. 그래서 로드 시점에 에셋을 몰라도 여기 담아뒀다가, 나중에 그
    // 참조를 들고 조회가 들어오는 순간 eventFlags로 승격시키면 된다(TryGetEventFlag 참고).
    private readonly Dictionary<string, int> _pendingFlagsById = new();

    public EventTargets currentTargets;

    public void SetEventFlag(EventFlagSO flag, int value)
    {
        if (flag == null) return;

        eventFlags ??= new();
        string id = flag.Id;

        // Addressables 빌드에서는 같은 EventFlagSO 에셋이 서로 다른 번들에서
        // 별개의 Unity 인스턴스로 로드될 수 있다. 참조 자체를 key로만 쓰면
        // 같은 플래그가 분리되므로, 이미 로드된 같은 ID를 함께 갱신한다.
        if (!SetLoadedEventFlagsById(id, value))
            eventFlags[flag] = value;

        _pendingFlagsById.Remove(id);
        EventDiagnostics.LogFlagSet(this, flag, value);
    }


    // 맵 진입 시 "아직 한 번도 설정된 적 없는 플래그만" 기본값으로 채운다. 이미 들어 있는 값
    // (플레이 중 진행분이든 세이브 로드분이든)은 절대 덮지 않는다.
    //
    // 이게 필요한 이유는 아래 HasEventFlag의 의미론 때문이다 — 미설정 플래그는 값이 0이어도
    // false다. 그래서 `dood == 0`처럼 "아직 아무것도 안 한 상태"를 조건으로 삼는 이벤트는
    // 누군가 0을 명시적으로 넣어주지 않으면 영영 발동하지 않는다.
    // 채워 넣었으면 true를 돌려준다(이미 있어서 건드리지 않았으면 false).
    public bool EnsureEventFlag(EventFlagSO flag, int defaultValue)
    {
        if (flag == null) return false;
        if (TryGetEventFlag(flag, out _)) return false;

        SetEventFlag(flag, defaultValue);
        return true;
    }
    // 플래그가 "설정된 적 있고 그 값이 value와 같은가" — 기존 호출부
    // (eventFlags.ContainsKey(flag) && eventFlags[flag] == condition)와 정확히 같은 의미다.
    // 설정된 적 없는 플래그는 value가 0이어도 매치되지 않는다.
    public bool HasEventFlag(EventFlagSO flag, int value)
        => TryGetEventFlag(flag, out int current) && current == value;

    public bool TryGetEventFlag(EventFlagSO flag, out int value)
    {
        value = 0;
        if (flag == null) return false;

        if (eventFlags != null && eventFlags.TryGetValue(flag, out value)) return true;

        // 번들 경계를 넘은 같은 에셋은 Unity 참조 비교가 실패할 수 있으므로,
        // 직접 참조를 못 찾으면 안정적인 EventFlagSO.Id로 다시 조회한다.
        if (TryGetLoadedEventFlagById(flag.Id, out value)) return true;

        // 로드로 들어왔지만 에셋을 몰라 대기 중이던 항목 — 이제 참조를 알았으니 승격시킨다.
        // 조회 경로에서 상태를 바꾸는 게 깔끔하진 않지만, ID → 에셋 역참조 없이 인스펙터 표시까지
        // 정상화하려면 "참조가 들어오는 순간"이 유일한 기회다.
        if (_pendingFlagsById.TryGetValue(flag.Id, out value))
        {
            SetEventFlag(flag, value);
            return true;
        }

        return false;
    }

    // ─── 세이브 연동 (IEventSaveProvider, 2026-07-28) ────────────────
    // SaveModule.eventProvider는 단일 슬롯이라 RouteModule이 이미 차지하고 있었는데, SaveModule이
    // GameManager.instance.eventManager를 직접 붙잡고 EventFlags/ImportFlags라는 별도 API로
    // 특별 취급하던 상태였다(부채로 예고됨, SaveTester.cs 주석 참고). SaveModule이 이제 provider
    // 목록을 합성하므로, EventManager도 RouteModule과 동일하게 이 표준 인터페이스로 들어온다.
    public string ProviderId => "EventManager";

    public Dictionary<string, int> EventFlags
    {
        get
        {
            var snapshot = new Dictionary<string, int>();

            if (eventFlags != null)
            {
                foreach (var kv in eventFlags)
                {
                    if (kv.Key == null) continue;
                    snapshot[kv.Key.Id] = kv.Value;
                }
            }

            // 아직 조회된 적 없어 승격되지 않은 항목도 그대로 다시 저장해야 유실되지 않는다.
            foreach (var kv in _pendingFlagsById)
                snapshot[kv.Key] = kv.Value;

            return snapshot;
        }
    }

    // ResetForLoad()가 채워두는 "이번 로드 시작 시점에 알던 ID→에셋" 캐시 — 로드 중 여러 번 오는
    // SetEventFlag(string,int) 개별 호출마다 eventFlags를 통째로 훑지 않도록 1회만 만들어 재사용한다.
    private Dictionary<string, EventFlagSO> _knownAssetsByIdForLoad;

    public void ResetForLoad()
    {
        _knownAssetsByIdForLoad = new Dictionary<string, EventFlagSO>();
        if (eventFlags != null)
        {
            foreach (var kv in eventFlags)
                if (kv.Key != null) _knownAssetsByIdForLoad[kv.Key.Id] = kv.Key;
        }

        eventFlags ??= new();
        eventFlags.Clear();
        _pendingFlagsById.Clear();
    }

    // IEventSaveProvider 전용 — EventFlagSO 참조 없이 ID만으로 들어오는 로드 경로.
    // 위 SetEventFlag(EventFlagSO, int)(게임플레이용)와는 오버로드로 공존한다.
    public void SetEventFlag(string id, int value)
    {
        if (string.IsNullOrEmpty(id)) return;

        if (_knownAssetsByIdForLoad != null && _knownAssetsByIdForLoad.TryGetValue(id, out var asset) && asset != null)
        {
            SetEventFlag(asset, value);
        }
        else if (SetLoadedEventFlagsById(id, value))
        {
            _pendingFlagsById.Remove(id);
        }
        else
        {
            _pendingFlagsById[id] = value;
        }
    }

    // EventManager는 "통로 개방" 개념이 없다 — RouteModule과 인터페이스를 공유하기 위한 빈 구현.
    /// <summary>
    /// 이미 로드된 플래그 중 ID가 같은 모든 인스턴스를 갱신한다.
    /// Addressables 번들 경계를 넘은 ScriptableObject 중복 로드를 흡수하는 내부 경로다.
    /// </summary>
    private bool SetLoadedEventFlagsById(string id, int value)
    {
        if (eventFlags == null || string.IsNullOrEmpty(id)) return false;

        var matchingFlags = new List<EventFlagSO>();
        foreach (KeyValuePair<EventFlagSO, int> pair in eventFlags)
        {
            if (pair.Key != null && pair.Key.Id == id)
                matchingFlags.Add(pair.Key);
        }

        foreach (EventFlagSO matchingFlag in matchingFlags)
            eventFlags[matchingFlag] = value;

        return matchingFlags.Count > 0;
    }

    /// <summary>
    /// 참조가 다른 EventFlagSO라도 동일한 안정 ID를 공유하면 같은 진행 플래그로 조회한다.
    /// </summary>
    private bool TryGetLoadedEventFlagById(string id, out int value)
    {
        value = 0;
        if (eventFlags == null || string.IsNullOrEmpty(id)) return false;

        foreach (KeyValuePair<EventFlagSO, int> pair in eventFlags)
        {
            if (pair.Key == null || pair.Key.Id != id) continue;

            value = pair.Value;
            return true;
        }

        return false;
    }

    public Dictionary<string, bool> Passages => _emptyPassages;
    private static readonly Dictionary<string, bool> _emptyPassages = new();
    public void SetPassage(string id, bool opened) { }

    // 지금 이벤트가 진행 중인가. 빌드된 이벤트는 마지막 State에 전이가 하나도 없으므로
    // "전이가 남아 있는 State에 머물러 있다"가 곧 "아직 끝나지 않았다"는 뜻이다.
    public bool IsEventRunning => CurrentState && CurrentState.transitions != null && CurrentState.transitions.Length > 0;

    // 이번 이벤트가 시작된 unscaled 시각 — 단계별 로그가 "시작 후 몇 초"를 찍는 데 쓴다.
    public float EventStartedAtUnscaled { get; private set; }

    /// <summary>
    /// 진행 중이던 이벤트를 그 자리에서 끊는다. 사망처럼 "하던 걸 무르고 처음으로" 가야 하는
    /// 경우에 쓴다 — 끊지 않으면 아래 StartEvent의 재진입 가드에 막혀 복귀 이벤트가 못 뜬다.
    /// </summary>
    /// <remarks>
    /// 끊긴 이벤트가 걸어둔 것들(컷신 입력 잠금, 화면 연출, 카메라)은 **저절로 풀리지 않는다.**
    /// 이 메서드는 상태 기계만 세운다. 복귀 이벤트의 첫 단계에서 SetInputModeAction(Play),
    /// ScreenFadeAction 등으로 직접 되돌려 줄 것.
    /// </remarks>
    public void AbortEvent()
    {
        if (!CurrentState) return;

        StateSO abortedState = CurrentState;
        EventDiagnostics.LogEventAborted(this, abortedState);
        Debug.Log($"[EventManager] 진행 중이던 이벤트를 중단합니다 (State: '{CurrentState.name}').");
        EliminateStateMachine();
        ResetStateDiagnostics(null);
    }

    /// <summary>
    /// 이벤트 플래그를 마지막 세이브에 담긴 값으로 되돌린다 — 사망 시 "진행 중이던 이벤트만큼의
    /// 진도"를 무르는 용도.
    /// </summary>
    /// <param name="lastSaved">
    /// SaveModule.CurrentSaveData 또는 LoadedData. null이거나 이 provider의 스냅샷이 없으면
    /// **아무것도 지우지 않는다** — SaveModule.LoadEvents와 같은 판단이다. 여기서 전부 비우면
    /// 한 번도 저장하지 않고 죽었을 때 맵이 저작해 둔 초기 플래그까지 날아가 이벤트가 영영 안 뜬다.
    /// </param>
    /// <returns>실제로 되돌렸으면 true.</returns>
    /// <remarks>
    /// 이미 Initialize를 마친 월드 오브젝트(EventControllableEntity/Animation)는 값을 되돌려도
    /// 스스로 다시 배치되지 않는다. 그래서 이 호출은 **맵을 다시 여는 흐름과 짝지어** 써야 한다
    /// (사망 복귀는 대개 현실의 방으로 맵을 옮기므로 자연히 충족된다).
    /// </remarks>
    public bool RevertFlagsToSave(SaveSlotData lastSaved)
    {
        ProviderFlagsSaveInfo snapshot = lastSaved?.providerFlags?.Find(p => p.providerId == ProviderId);
        if (snapshot == null)
        {
            Debug.LogWarning("[EventManager] 되돌릴 세이브 스냅샷이 없어 이벤트 플래그를 그대로 둡니다 " +
                             "(아직 한 번도 저장하지 않았거나 이 provider의 기록이 없는 세이브).");
            return false;
        }

        ResetForLoad();
        if (snapshot.eventFlags != null)
        {
            foreach (EventFlagSaveInfo flag in snapshot.eventFlags)
                SetEventFlag(flag.id, flag.value);
        }

        Debug.Log($"[EventManager] 이벤트 플래그를 마지막 세이브 시점으로 되돌렸습니다 " +
                  $"({snapshot.eventFlags?.Count ?? 0}개).");
        return true;
    }

    /// <param name="interruptRunning">
    /// 진행 중인 이벤트를 끊고 시작한다. 사망 복귀처럼 **반드시 끼어들어야 하는** 이벤트만 켤 것 —
    /// 평상시엔 꺼둬야 트리거가 겹쳐 이벤트가 되감기는 사고를 가드가 잡아준다.
    /// </param>
    public void StartEvent(EventSO eventSO, EventTargets manualTargets = null, bool interruptRunning = false)
    {
        TryStartEvent(eventSO, manualTargets, interruptRunning, out _);
    }

    /// <summary>
    /// EventSO를 실제로 시작했는지 반환한다. GameStateEvent와 트리거 진단 경로가
    /// "호출했다"와 "시작됐다"를 구분할 수 있도록 기존 StartEvent의 결과형을 제공한다.
    /// </summary>
    public bool TryStartEvent(
        EventSO eventSO,
        EventTargets manualTargets,
        bool interruptRunning,
        out string rejectionDetail)
    {
        if (!eventSO)
        {
            rejectionDetail = "시작할 EventSO가 null입니다.";
            Debug.LogError($"[EventManager] {rejectionDetail}");
            return false;
        }

        // 트리거가 겹쳐 있거나 두 번 발동하면 진행 중인 이벤트가 첫 단계부터 다시 시작된다 —
        // 연출이 처음부터 되감기니 "이벤트가 유난히 오래 걸린다"로 보인다. 막고 알린다.
        if (IsEventRunning && interruptRunning) AbortEvent();

        if (IsEventRunning)
        {
            rejectionDetail = $"'{eventSO.name}'을 시작하려 했지만 이미 이벤트가 진행 중입니다 " +
                              $"(현재 State: '{CurrentState.name}').";
            Debug.LogWarning($"[EventManager] {rejectionDetail} 무시합니다 — 같은 트리거가 두 번 발동했거나 " +
                             "트리거가 겹쳐 있는지 확인하세요.");
            return false;
        }

        EventStartedAtUnscaled = Time.unscaledTime;
        FindTargets(eventSO, manualTargets);

        // 대상은 반드시 Initialize보다 먼저 넘겨야 한다. Initialize는 ResetStateMachine을 거쳐
        // **첫 State의 진입 액션을 그 자리에서 실행**하는데, 그 액션이 대상을 쓰면(예: NPC 애니메이션
        // 재생) 아직 CurrentTargets가 비어 있어 NullReferenceException이 난다.
        // 예전엔 첫 단계가 대상을 안 써서 드러나지 않았을 뿐이다.
        if (TryGetInterface(out IEvent @event)) @event.CurrentTargets = currentTargets;

        Initialize(eventSO);
        ResetStateDiagnostics(CurrentState);
        EventDiagnostics.LogEventStarted(this, eventSO, CurrentState);
        if (!IsEventRunning) EventDiagnostics.LogEventCompleted(this);
        rejectionDetail = string.Empty;
        return true;
    }

    public override void ChangeState(StateSO state)
    {
        StateSO previous = CurrentState;
        base.ChangeState(state);
        ResetStateDiagnostics(CurrentState);
        EventDiagnostics.LogStateChanged(this, previous, CurrentState);
        if (!IsEventRunning) EventDiagnostics.LogEventCompleted(this);
    }

    public override void UpdateState()
    {
        base.UpdateState();

        if (CurrentState != _diagnosticState)
            ResetStateDiagnostics(CurrentState);
        if (!IsEventRunning || !CurrentState) return;

        float now = Time.unscaledTime;
        if (now < _nextStallWarningAt) return;

        EventDiagnostics.LogEventStall(this, now - _diagnosticStateEnteredAt);
        _nextStallWarningAt = now + Mathf.Max(1f, _stallWarningSeconds);
    }

    private void ResetStateDiagnostics(StateSO state)
    {
        _diagnosticState = state;
        _diagnosticStateEnteredAt = Time.unscaledTime;
        _nextStallWarningAt = _diagnosticStateEnteredAt + Mathf.Max(1f, _stallWarningSeconds);
    }

    public void FindTargets(EventSO eventSO, EventTargets manualTargets)
    {
        currentTargets = new();
        for (int i = 0; i < eventSO.involvedEventTargets.Length; i++)
        {
            EventTargetSearchInfo target = eventSO.involvedEventTargets[i];
            if (target.targetSearchType == TargetSearchType.Player)
            {
                currentTargets.targetEntities[target.ID] = GameManager.instance.player.GetComponent<EventControllableEntity>();
            }
            else if (target.targetSearchType == TargetSearchType.FromMap)
            {
                // 여기서 못 찾고 조용히 넘어가면, 나중에 그 대상을 쓰는 액션이 훨씬 뒤에서 엉뚱한
                // 에러를 내며 죽는다(TargetEntityManipulateAction 등). 원인이 드러나는 이 자리에서 알린다.
                MapLocalManager localManager = GameManager.instance.mapManager.localManager;
                if (!localManager)
                {
                    Debug.LogWarning($"[EventManager] FromMap 대상 '{target.ID}'를 찾을 수 없습니다 — 로드된 맵에 " +
                                     "MapLocalManager가 없습니다(맵이 아직 안 떴거나 맵 씬에 그 컴포넌트가 없음).");
                    continue;
                }

                EventTargets targets = localManager.allEventTargets;
                if (targets.targetEntities.ContainsKey(target.ID)) currentTargets.targetEntities[target.ID] = targets.targetEntities[target.ID];
                else if (targets.targetAnimations.ContainsKey(target.ID)) currentTargets.targetAnimations[target.ID] = targets.targetAnimations[target.ID];
                else
                {
                    Debug.LogWarning($"[EventManager] FromMap 대상 '{target.ID}'가 '{localManager.gameObject.scene.name}' 씬의 " +
                                     $"MapLocalManager.allEventTargets에 없습니다. 등록된 대상: " +
                                     $"[{string.Join(", ", targets.targetEntities.Keys)}]. " +
                                     "그 ID의 EventControllableEntity가 이 맵 씬 안에 있어야 하고(다른 씬은 안 됨), " +
                                     "MapLocalManager의 Auto Find Event Targets를 누른 뒤 씬을 저장해야 합니다.");
                }
            }
            else if (target.targetSearchType == TargetSearchType.Manual && manualTargets != null)
            {
                currentTargets.targetEntities[target.ID] = manualTargets.targetEntities[target.ID];
                currentTargets.targetAnimations[target.ID] = manualTargets.targetAnimations[target.ID];
            }
        }
    }

}
