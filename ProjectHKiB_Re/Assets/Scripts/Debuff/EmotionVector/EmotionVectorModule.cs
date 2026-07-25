using System;
using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// EmotionModule을 읽기 전용으로 폴링만 한다 — 기존 코드 무수정 (spec §7.1).
// 역치 판정/버프 부여/조합 판정은 아직 하지 않는다. 순수 벡터 계산만 (Phase 1).
[RequireComponent(typeof(EmotionModule))]
public class EmotionVectorModule : MonoBehaviour
{
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
    };

    [SerializeField] private EmotionVectorTableSO table;
    [SerializeField] private bool showDebugLog = false;

    [Header("Threshold")]
    [SerializeField] private EmotionThresholdProfileSO profile;
    [SerializeField] private EmotionThresholdProfileSO defaultProfile;
    [SerializeField] private bool applyThresholds = true; // 플레이어는 false로 둘 것 (spec §5.5)
    [SerializeField] private EmotionCombinationRuleSO rule; // 공허 촉매 파라미터 (Step 2.3, spec §2.4)

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

    private void LateUpdate()
    {
        if (!_isDirty) return;
        Recalculate();
        if (applyThresholds) EvaluateThresholds();
        _isDirty = false;
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
