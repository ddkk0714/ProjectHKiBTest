using System;
using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// EmotionModule을 읽기 전용으로 폴링만 한다 — 기존 코드 무수정 (spec §7.1).
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 다른 시스템이 감정 벡터 상태를 읽거나 반응하려면 이 클래스만
// 참조하면 된다. GetComponent<EmotionVectorModule>()로 접근(같은 GameObject에 붙어있음,
// RequireComponent로 EmotionModule도 항상 동반).
//
// ▸ 행동 고정(4축) 상태 조회 — StateMachine 연동의 핵심 진입점
//   IsAxisBehaviorActive(axis) : 이 축이 실제로 State 전환을 일으켜야 하는지(후속 버프
//                                진행 중엔 false로 억제됨). StateMachine/Debuff/EmotionVector/
//                                EmotionAxisActiveDecision.cs가 이 값을 읽어 잠/황홀/광기/파멸
//                                전용 State로 분기시킨다(적용 예: Enemy_Rusher_SleepState/
//                                EcstasyState, Delta_Base_Sleep/Ecstasy_Start/Keep/EndState).
//   IsAxisActive(axis)         : 후속 버프 억제를 무시한 원시 축 활성 여부(디버그/로그용).
//
// ▸ 감정 상태 변화 구독
//   OnStateChanged(EmotionState, bool) : 4축 중 하나가 켜지거나 꺼질 때 발행. UI/연출/
//                                        다른 게임플레이 시스템이 이 이벤트만 구독하면
//                                        폴링 없이 반응 가능.
//
// ▸ 현재 값 조회 (읽기 전용)
//   Vector / Magnitude / Entropy / DominantColor / Mental : 감정 평면 좌표와 파생값.
//   GetRawStack(color)                                    : 특정 색상의 원본 스택 수.
//
// ▸ 감정 → RouteFinding 연동 (2026-07-26 신규)
//   감정축 진입 State(Enemy_Rusher_SleepState/EcstasyState, Delta_Base_Sleep/Ecstasy_
//   StartState)의 EnterActions에 StateMachine/Actions/Event/SetRouteEventFlagAction이
//   배선되어 있어, 해당 축이 발동하는 순간 RouteModule.Instance.Progress.SetEventFlag가
//   자동 호출된다(이벤트 키: playerSlept/playerEcstasy/enemySlept/enemyEcstasy). 새로운
//   ClueData.requiredEventKey를 이 값 중 하나로 지정하면 감정 트리거만으로 단서가
//   공개된다 — RouteModule.cs 상단 주석 참고.
// ════════════════════════════════════════════════════════════════
[RequireComponent(typeof(EmotionModule))]
public class EmotionVectorModule : MonoBehaviour
{
    // ⚠️ Phase 5에서 발견된 하드코딩 지점 — "SO 편집만으로 신규 감정 추가"가 여기서는 안 통한다.
    // 새 감정을 벡터 합산/폴링/조합 판정 대상에 포함시키려면 이 배열에 추가해야 한다(코드 1줄).
    public static readonly EmotionColor[] PolledColors =
    {
        EmotionColor.SadnessBlue,
        EmotionColor.SadnessSky,
        EmotionColor.ExcitementDeepPink,
        EmotionColor.HappinessYellow,
        EmotionColor.AngerOrange,
        EmotionColor.AngerScarlet,
        EmotionColor.VoidBlack,
        EmotionColor.FearDarkRed,
        EmotionColor.Disgust,
        EmotionColor.Stress,
        EmotionColor.Satisfaction,
        EmotionColor.Peace,
        EmotionColor.Fatigue,
        EmotionColor.Love, // Step 5.2 — 임시 등록, spec §2.5 재논의 전까지 잠정치
    };

    [SerializeField] private EmotionVectorTableSO table;
    [SerializeField] private bool showDebugLog = false;

    [Header("Threshold")]
    [SerializeField] private EmotionThresholdProfileSO profile;
    [SerializeField] private EmotionThresholdProfileSO defaultProfile;
    [SerializeField] private bool applyThresholds = true; // 플레이어는 false로 둘 것 (spec §5.5)
    [SerializeField] private EmotionCombinationRuleSO rule; // 공허 촉매 파라미터 (Step 2.3, spec §2.4) + 조합 판정 파라미터 (Step 4.1)

    [Header("Combination Shadow Mode (Step 4.1)")]
    [SerializeField] private bool showCombinationShadowLog = false; // 기존 EvaluateReaction()과 병행 실행, 로그만 — 실제 적용 안 함 (spec §4.1~4.3)

    [Header("Combination Live Apply (Step 4.3)")]
    [SerializeField] private bool useVectorCombination = false; // true면 기존 EvaluateReaction()을 끄고 이 모듈이 실제로 상쇄/대체/복합을 적용한다. 기본값 false — 롤백 안전선(spec §5, 계획서 "롤백 안전선" 표)

    private static readonly EmotionAxis[] AllAxes =
    {
        EmotionAxis.PositiveX, EmotionAxis.NegativeX, EmotionAxis.PositiveY, EmotionAxis.NegativeY
    };

    // 역치로 부여한 statBuff는 SO 자체의 BuffTime(예: Madness_Other의 5초 자동만료)을 무시하고
    // 이 값으로 오버라이드한다 — 지속 여부는 벡터 조건(EvaluateThresholds)이 결정해야지 타이머가 결정하면 안 됨.
    private const float ThresholdBuffOverrideDuration = 86400f;

    private EmotionModule _emotionModule;
    private IBuffable _buffable;
    private Enemy _enemy; // 없으면(플레이어 등) Mental 100 고정 — spec §5.5 "적 전용"
    private readonly Dictionary<EmotionColor, int> _lastStacks = new();
    private readonly HashSet<EmotionAxis> _activeAxes = new();
    private readonly Dictionary<EmotionAxis, Coroutine> _followUpCoroutines = new();
    private readonly HashSet<EmotionAxis> _followedUpAxes = new(); // followUpBuff가 이미 적용된 축 (statBuff -> followUpBuff 전환 완료)
    private bool _isDirty;
    private EmotionVector _current;
    private bool _warnedNoProfile;

    public event Action<EmotionState, bool> OnStateChanged;

    [ShowNativeProperty] public Vector2 Vector => new Vector2(_current.X, _current.Y);
    [ShowNativeProperty] public float Magnitude => _current.Magnitude;
    [ShowNativeProperty] public float Entropy { get; private set; }
    [ShowNativeProperty] public EmotionColor DominantColor { get; private set; }
    [ShowNativeProperty] public int RecalculateCallCount { get; private set; } // 게이트 1.1 검증용 (Step 4 이후 제거 검토)

    public EmotionVectorTableSO Table => table;

    // 디버그 뷰의 "가짜 주입" 미리보기용 — 실제 게임 상태(EmotionModule)는 건드리지 않는다 (Step 1.2, spec §8).
    // Awake() 이전(에디터에서 컴포넌트만 막 추가한 시점 등)엔 _emotionModule이 null일 수 있어 방어.
    public int GetRawStack(EmotionColor color)
    {
        if (_emotionModule == null) _emotionModule = GetComponent<EmotionModule>();
        if (_emotionModule == null) return 0;

        return _emotionModule.GetStacks(color, EmotionModule.EmotionApplyTarget.Other);
    }

    [ShowNativeProperty] public float Mental => _enemy != null && _enemy.BaseData != null ? _enemy.BaseData.Mental : 100f;

    public bool IsAxisActive(EmotionAxis axis) => _activeAxes.Contains(axis);

    // FSM 행동 오버라이드(EmotionAxisActiveDecision)가 참조하는 값 — followUpBuff로 이미 전환된 축은
    // (예: 황홀 초반 -> 후기 그로기) 더 이상 "초반 전용 State"로 라우팅하면 안 되므로 false를 반환한다.
    // locked=true라 _activeAxes 자체는 계속 true로 남아있어도(축 판정/스탯은 유지) FSM 진입만 여기서 끊는다.
    public bool IsAxisBehaviorActive(EmotionAxis axis) => _activeAxes.Contains(axis) && !_followedUpAxes.Contains(axis);

    private void Awake()
    {
        _emotionModule = GetComponent<EmotionModule>();
        _buffable = GetComponent<IBuffable>();
        _enemy = GetComponent<Enemy>();
    }

    // profile 미지정 시 defaultProfile로 폴백 (게이트 2.1). 둘 다 없으면 경고 1회 + null 반환.
    public EmotionThresholdProfileSO GetEffectiveProfile()
    {
        if (profile != null) return profile;
        if (defaultProfile != null) return defaultProfile;

        if (!_warnedNoProfile)
        {
            _warnedNoProfile = true;
            Debug.LogWarning($"[EmotionVectorModule] {name}: profile/defaultProfile 둘 다 없음 — 역치 조회 불가");
        }

        return null;
    }

    // 촉매 적용 전, 멘탈만 반영한 값 — 디버그 뷰의 스케일 기준용.
    // (촉매로 줄어든 값을 스케일 기준으로 쓰면 배율이 같이 커져서 화면 위치가 고정되는 자기상쇄가 생김)
    public float GetMentalOnlyThreshold(EmotionAxis axis)
    {
        EmotionThresholdProfileSO effectiveProfile = GetEffectiveProfile();
        return effectiveProfile != null ? effectiveProfile.GetEffectiveThreshold(axis, Mental) : float.PositiveInfinity;
    }

    // T = baseThreshold * (mental/100) 이후 공허 촉매 적용 (spec §2.4, §5.3)
    public float GetEffectiveThreshold(EmotionAxis axis)
    {
        float mentalThreshold = GetMentalOnlyThreshold(axis);
        if (float.IsPositiveInfinity(mentalThreshold)) return mentalThreshold;

        return rule != null ? rule.ApplyCatalyst(mentalThreshold, GetRawStack(EmotionColor.VoidBlack)) : mentalThreshold;
    }

    // 0~1 게이지 바 등 표시용 (Step 2.3)
    [ShowNativeProperty]
    public float CatalystRatio => rule != null ? rule.GetCatalystRatio(GetRawStack(EmotionColor.VoidBlack)) : 0f;

    private void Update()
    {
        // 이 모듈이 켜져 있는 동안은 매 프레임 동기화 — 인스펙터에서 값을 바꿔도(Play 모드 포함) 바로 반영됨.
        if (_emotionModule != null) _emotionModule.SuppressLegacyReaction = useVectorCombination;

        for (int i = 0; i < PolledColors.Length; i++)
        {
            EmotionColor color = PolledColors[i];
            int current = _emotionModule.GetStacks(color, EmotionModule.EmotionApplyTarget.Other);

            _lastStacks.TryGetValue(color, out int last);
            if (last == current) continue;

            _lastStacks[color] = current;
            _isDirty = true;
        }
    }

    private void OnDisable()
    {
        // 컴포넌트가 꺼지면 억제도 같이 풀어서 기존 반응 시스템이 고아 상태로 막혀있지 않게 함.
        if (_emotionModule != null) _emotionModule.SuppressLegacyReaction = false;
    }

    private void LateUpdate()
    {
        if (!_isDirty) return;
        Recalculate();
        if (applyThresholds) EvaluateThresholds();
        if (useVectorCombination) ApplyCombinations();
        else if (showCombinationShadowLog) LogShadowCombinations();
        _isDirty = false;
    }

    // Step 4.1 — 섀도 모드. 기존 EvaluateReaction()은 손대지 않고 그대로 병행 가동되며, 여기서는
    // "새 벡터 조합 판정이라면 이 두 색을 어떻게 처리했을지"를 로그로만 남긴다(실제 스택/버프 미적용).
    // 기존 시스템은 대개 두 번째 색이 적용되는 그 프레임에 즉시 반응해 소각하므로, 이 폴링이 두 색이
    // 공존하는 순간을 실제로 잡아내는 경우는 드물다 — 주 검증 수단은 EditMode 단위 테스트(§10 검증쌍).
    private void LogShadowCombinations()
    {
        if (table == null || rule == null) return;

        for (int i = 0; i < PolledColors.Length; i++)
        {
            EmotionColor colorA = PolledColors[i];
            if (colorA == EmotionColor.VoidBlack) continue;
            int stackA = _emotionModule.GetStacks(colorA, EmotionModule.EmotionApplyTarget.Other);
            if (stackA <= 0) continue;

            for (int j = i + 1; j < PolledColors.Length; j++)
            {
                EmotionColor colorB = PolledColors[j];
                if (colorB == EmotionColor.VoidBlack) continue;
                int stackB = _emotionModule.GetStacks(colorB, EmotionModule.EmotionApplyTarget.Other);
                if (stackB <= 0) continue;

                EmotionCombinationResult result = EmotionCombinationEvaluator.Evaluate(
                    table.GetPosition(colorA), stackA, colorA,
                    table.GetPosition(colorB), stackB, colorB,
                    rule.ReplaceThreshold);

                if (result.Type != EmotionCombinationType.Overlap)
                {
                    string extra = "";
                    if (result.Type == EmotionCombinationType.Composite &&
                        rule.TryGetCompositeColor(result.CompositeQuadrant, out EmotionColor composite))
                    {
                        int fusionStack = EmotionCombinationEvaluator.ComputeFusionStack(result.ConsumedStack, rule.FusionEfficiency);
                        extra = $", composite={composite}(fusionStack={fusionStack})";
                    }

                    Debug.Log($"[EmotionVectorModule] Shadow combination: {colorA}({stackA}) + {colorB}({stackB}) -> {result}{extra}");
                }
            }
        }
    }

    // Step 4.3 — 실제 적용. useVectorCombination이 켜져 있을 때만 LateUpdate에서 호출되며, 이때
    // EmotionModule.SuppressLegacyReaction이 Update()에서 이미 true로 동기화되어 있어 기존
    // EvaluateReaction()과 이중 처리될 일은 없다. 한 프레임에 "재공급 1건" 또는 "쌍 1건"만 적용하고
    // 멈춘다 — 여기서 호출하는 RemoveColor/ApplyColor가 다음 프레임 Update() 폴링에서 새 dirty로
    // 잡혀 자연히 이어지므로(EvaluateThresholds와 동일한 "같은 프레임 재귀 금지" 원칙), 한 프레임에
    // 여러 쌍을 연달아 처리하면서 이미 소각된 색의 stale 값을 참조하는 문제가 생기지 않는다.
    private void ApplyCombinations()
    {
        if (table == null || rule == null) return;

        if (TryApplyReplenishment()) return;

        for (int i = 0; i < PolledColors.Length; i++)
        {
            EmotionColor colorA = PolledColors[i];
            if (colorA == EmotionColor.VoidBlack) continue;
            int stackA = _emotionModule.GetStacks(colorA, EmotionModule.EmotionApplyTarget.Other);
            if (stackA <= 0) continue;

            for (int j = i + 1; j < PolledColors.Length; j++)
            {
                EmotionColor colorB = PolledColors[j];
                if (colorB == EmotionColor.VoidBlack) continue;
                int stackB = _emotionModule.GetStacks(colorB, EmotionModule.EmotionApplyTarget.Other);
                if (stackB <= 0) continue;

                EmotionCombinationResult result = EmotionCombinationEvaluator.Evaluate(
                    table.GetPosition(colorA), stackA, colorA,
                    table.GetPosition(colorB), stackB, colorB,
                    rule.ReplaceThreshold);

                if (ApplyCombinationResult(colorA, stackA, colorB, stackB, result))
                    return;
            }
        }
    }

    // spec §4.4 — 활성 복합의 재료가 되는 기본색이 새로 유입되면, 그 재료를 소각하고 0.5배 효율로
    // 기존 복합 스택에 합류시킨다(새 복합을 따로 만들지 않음, 재료 색 자체는 남지 않음).
    // 지속시간은 건드리지 않는다 — ApplyColor가 기존 BuffableModule 재적용 규칙을 그대로 따른다.
    private bool TryApplyReplenishment()
    {
        IReadOnlyList<EmotionCombinationRuleSO.CompositeEntry> composites = rule.Composites;
        for (int i = 0; i < composites.Count; i++)
        {
            EmotionColor composite = composites[i].result;
            int compositeStack = _emotionModule.GetStacks(composite, EmotionModule.EmotionApplyTarget.Other);
            if (compositeStack <= 0) continue;

            List<EmotionColor> materials = composites[i].materials;
            if (materials == null) continue;

            for (int m = 0; m < materials.Count; m++)
            {
                EmotionColor material = materials[m];
                int materialStack = _emotionModule.GetStacks(material, EmotionModule.EmotionApplyTarget.Other);
                if (materialStack <= 0) continue;

                int replenishStack = EmotionCombinationEvaluator.ComputeReplenishStack(materialStack);
                _emotionModule.RemoveColor(material, EmotionModule.EmotionApplyTarget.Other, materialStack);
                if (replenishStack > 0)
                    _emotionModule.ApplyColor(composite, replenishStack, EmotionModule.EmotionApplyTarget.Other);

                if (showDebugLog)
                    Debug.Log($"[EmotionVectorModule] Replenish: {composite} += {replenishStack} (material {material} consumed {materialStack})");

                return true;
            }
        }

        return false;
    }

    // 판정 결과를 실제 EmotionModule 스택에 반영. 처리했으면 true(호출부가 이번 프레임을 종료함).
    private bool ApplyCombinationResult(EmotionColor colorA, int stackA, EmotionColor colorB, int stackB, EmotionCombinationResult result)
    {
        switch (result.Type)
        {
            case EmotionCombinationType.Cancel:
                _emotionModule.RemoveColor(colorA, EmotionModule.EmotionApplyTarget.Other, result.ConsumedStack);
                _emotionModule.RemoveColor(colorB, EmotionModule.EmotionApplyTarget.Other, result.ConsumedStack);
                if (showDebugLog)
                    Debug.Log($"[EmotionVectorModule] Cancel: {colorA} - {colorB} (-{result.ConsumedStack} each)");
                return true;

            case EmotionCombinationType.Replace:
            {
                bool aWins = result.Winner == colorA;
                EmotionColor loser = aWins ? colorB : colorA;
                int loserStack = aWins ? stackB : stackA;
                _emotionModule.RemoveColor(loser, EmotionModule.EmotionApplyTarget.Other, loserStack);
                if (showDebugLog)
                    Debug.Log($"[EmotionVectorModule] Replace: {result.Winner} survives, {loser} annihilated (-{loserStack})");
                return true;
            }

            case EmotionCombinationType.Composite:
            {
                if (!rule.TryGetCompositeColor(result.CompositeQuadrant, out EmotionColor composite))
                    return false; // 아직 등록되지 않은 사분면(1사분면 등) — 조용히 스킵, 다음 새 쌍에서 재시도

                _emotionModule.RemoveColor(colorA, EmotionModule.EmotionApplyTarget.Other, result.ConsumedStack);
                _emotionModule.RemoveColor(colorB, EmotionModule.EmotionApplyTarget.Other, result.ConsumedStack);

                int fusionStack = EmotionCombinationEvaluator.ComputeFusionStack(result.ConsumedStack, rule.FusionEfficiency);
                if (fusionStack > 0)
                    _emotionModule.ApplyColor(composite, fusionStack, EmotionModule.EmotionApplyTarget.Other);

                if (showDebugLog)
                    Debug.Log($"[EmotionVectorModule] Composite: {colorA} + {colorB} -> {composite} (+{fusionStack})");
                return true;
            }

            default:
                return false; // Overlap — 처리 없음
        }
    }

    // 4축 독립 판정 + 히스테리시스 (spec §5.4). 여기서 발생하는 스택 변경(예: 역치 버프가 감정색을
    // 부여하도록 설정된 경우)은 Update()의 폴링이 dirty만 세팅하고 끝나므로, 같은 프레임에 재귀적으로
    // 다시 판정되지 않는다 — 다음 프레임에 반영된다 (EvaluateReaction의 _isEvaluatingReaction과 동일 취지).
    private void EvaluateThresholds()
    {
        EmotionThresholdProfileSO effectiveProfile = GetEffectiveProfile();
        if (effectiveProfile == null) return;

        for (int i = 0; i < AllAxes.Length; i++)
        {
            EmotionAxis axis = AllAxes[i];
            if (!effectiveProfile.TryGetEntry(axis, out EmotionThresholdProfileSO.ThresholdEntry entry)) continue;

            float value = GetAxisValue(_current, axis);
            float effectiveThreshold = GetEffectiveThreshold(axis); // 멘탈 스케일 + 공허 촉매 포함
            bool wasActive = _activeAxes.Contains(axis);

            bool nextActive = EvaluateAxisActive(wasActive, value, effectiveThreshold, entry.hysteresis, entry.locked);

            if (nextActive && !wasActive) ActivateAxis(axis, entry);
            else if (!nextActive && wasActive) DeactivateAxis(axis, entry);
        }
    }

    // 순수 함수로 분리 — 히스테리시스/locked 판정 로직 자체를 실제 버프 시스템 없이 단위 테스트 가능하게 함.
    // 부여: value >= threshold / 해제: value < threshold - hysteresis / locked면 활성 상태 유지.
    public static bool EvaluateAxisActive(bool wasActive, float value, float threshold, float hysteresis, bool locked)
    {
        if (!wasActive) return value >= threshold;
        if (locked) return true;
        return !(value < threshold - hysteresis);
    }

    private void ActivateAxis(EmotionAxis axis, EmotionThresholdProfileSO.ThresholdEntry entry)
    {
        _activeAxes.Add(axis);

        if (entry.statBuff != null && _buffable != null)
            _buffable.Buff(entry.statBuff, 1, 1, ThresholdBuffOverrideDuration);

        if (entry.followUpBuff != null && entry.statBuff != null && entry.statBuff.BuffTime > 0f)
            _followUpCoroutines[axis] = StartCoroutine(FollowUpRoutine(axis, entry));

        EmotionState state = AxisToState(axis);
        OnStateChanged?.Invoke(state, true);

        if (showDebugLog)
            Debug.Log($"[EmotionVectorModule] Threshold ON: {axis} -> {state} (value={GetAxisValue(_current, axis):F1})");
    }

    private void DeactivateAxis(EmotionAxis axis, EmotionThresholdProfileSO.ThresholdEntry entry)
    {
        _activeAxes.Remove(axis);

        if (_followUpCoroutines.TryGetValue(axis, out Coroutine routine))
        {
            StopCoroutine(routine);
            _followUpCoroutines.Remove(axis);
        }

        if (_buffable != null)
        {
            if (_followedUpAxes.Remove(axis))
            {
                if (entry.followUpBuff != null) _buffable.UnBuff(entry.followUpBuff);
            }
            else if (entry.statBuff != null)
            {
                _buffable.UnBuff(entry.statBuff);
            }
        }

        EmotionState state = AxisToState(axis);
        OnStateChanged?.Invoke(state, false);

        if (showDebugLog)
            Debug.Log($"[EmotionVectorModule] Threshold OFF: {axis} -> {state}");
    }

    // entry.statBuff의 BuffTime이 지나면 entry.followUpBuff로 교체 (예: 황홀 초반 버프 -> 황홀 후기 그로기).
    // 역치로 부여한 statBuff는 ActivateAxis에서 지속시간을 override하므로, 이 타이머는 BuffableModule의
    // 자연 만료를 기다리는 게 아니라 EmotionVectorModule이 statBuff.BuffTime 값을 "페이즈 길이"로 직접 읽어 독자적으로 잰다.
    private IEnumerator FollowUpRoutine(EmotionAxis axis, EmotionThresholdProfileSO.ThresholdEntry entry)
    {
        yield return new WaitForSeconds(entry.statBuff.BuffTime);

        _followUpCoroutines.Remove(axis);
        if (!_activeAxes.Contains(axis)) yield break;

        if (_buffable != null)
        {
            _buffable.UnBuff(entry.statBuff);
            // followUpBuff는 override 없이 SO 자체의 BuffTime으로 적용 — 유한 시간 뒤 자연 만료되어야
            // (예: 황홀 후기 그로기가 일정 시간 후 풀리고 정상 행동으로 복귀) 하기 때문에, 축이 계속
            // "활성" 상태인 것과 별개로 이 버프만은 시간이 다 되면 스스로 사라지게 둔다.
            _buffable.Buff(entry.followUpBuff, 1, 1, -1f);
        }
        _followedUpAxes.Add(axis);

        if (showDebugLog)
            Debug.Log($"[EmotionVectorModule] FollowUp: {axis} -> {entry.followUpBuff.name}");
    }

    public static EmotionState AxisToState(EmotionAxis axis) => axis switch
    {
        EmotionAxis.PositiveY => EmotionState.Madness,
        EmotionAxis.NegativeY => EmotionState.Sleep,
        EmotionAxis.NegativeX => EmotionState.Doom,
        EmotionAxis.PositiveX => EmotionState.Ecstasy,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    // 부정(-x)/비각성(-y) 방향은 값이 음수일수록 강해지므로 부호를 뒤집어 "역치 초과"를 항상 >=로 통일한다.
    public static float GetAxisValue(EmotionVector v, EmotionAxis axis) => axis switch
    {
        EmotionAxis.PositiveY => v.Y,
        EmotionAxis.NegativeY => -v.Y,
        EmotionAxis.NegativeX => -v.X,
        EmotionAxis.PositiveX => v.X,
        _ => 0f,
    };

    private void Recalculate()
    {
        RecalculateCallCount++;

        _current = ComputeVector(table, PolledColors,
            color => _emotionModule.GetStacks(color, EmotionModule.EmotionApplyTarget.Other),
            out float entropy, out EmotionColor dominant);

        Entropy = entropy;
        DominantColor = dominant;

        if (showDebugLog)
            Debug.Log($"[EmotionVectorModule] Recalculate #{RecalculateCallCount}: V={_current}, Magnitude={_current.Magnitude:F2}, Entropy={Entropy:F2}, Dominant={DominantColor}");
    }

    // 순수 함수로 분리 — EmotionModule/씬 없이도 공식 자체를 단위 테스트할 수 있게 함 (spec §3.1, §3.2).
    // 기존 반응 시스템이 살아있는 한 실제 씬에서는 서로 다른 두 그룹의 감정이 동시에 존재할 수 없어서
    // (즉시 반응으로 소각됨) "슬픔+행복" 같은 조합 케이스는 이 함수를 통한 코드 테스트로만 검증 가능하다.
    public static EmotionVector ComputeVector(
        EmotionVectorTableSO table,
        IReadOnlyList<EmotionColor> colors,
        Func<EmotionColor, int> stackLookup,
        out float entropy,
        out EmotionColor dominantColor)
    {
        Vector2 sum = Vector2.zero;
        float weightedMagnitudeSum = 0f;
        float bestContribution = 0f;
        EmotionColor dominant = default;
        bool hasDominant = false;

        if (table != null)
        {
            for (int i = 0; i < colors.Count; i++)
            {
                EmotionColor color = colors[i];
                if (color == EmotionColor.VoidBlack) continue; // 공허는 촉매, 합에서 제외 (spec §2.4, §3.1)

                int stack = stackLookup(color);
                if (stack <= 0) continue;

                Vector2 contribution = table.GetPosition(color) * stack;
                sum += contribution;

                float contributionMagnitude = contribution.magnitude;
                weightedMagnitudeSum += contributionMagnitude;

                if (!hasDominant || contributionMagnitude > bestContribution)
                {
                    bestContribution = contributionMagnitude;
                    dominant = color;
                    hasDominant = true;
                }
            }
        }

        EmotionVector result = new EmotionVector(sum.x, sum.y);
        entropy = weightedMagnitudeSum > 0f ? 1f - (result.Magnitude / weightedMagnitudeSum) : 0f;
        dominantColor = dominant;
        return result;
    }

    // 게이트 1.1 검증용 — 한 프레임에 여러 색이 동시에 바뀌어도 Recalculate()가 1회만 도는지 확인
    [Button("Test: Apply 3 Colors At Once", EButtonEnableMode.Playmode)]
    private void TestApplyThreeColorsAtOnce()
    {
        _emotionModule.ApplyColor(EmotionColor.SadnessBlue, 10, EmotionModule.EmotionApplyTarget.Other);
        _emotionModule.ApplyColor(EmotionColor.HappinessYellow, 10, EmotionModule.EmotionApplyTarget.Other);
        _emotionModule.ApplyColor(EmotionColor.AngerOrange, 10, EmotionModule.EmotionApplyTarget.Other);
    }
}
