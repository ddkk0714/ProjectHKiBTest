using System.Collections.Generic;
using UnityEngine;

/*
 * ── EmotionModule 사용 가이드 ────────────────────────────────────────────────
 *
 * [1] Inspector 사전 세팅
 *   emotionBuffEntries 에 사용할 모든 EmotionColor 의 StatBuffSO 를 등록해야 함.
 *     · selfBuff  = 자신에게 적용 (긍정 효과)
 *     · otherBuff = 상대에게 적용 (부정 효과)
 *   등록 대상: 기본 8색(SadnessBlue ~ FearDarkRed) + 반응 결과색(Sorrow, Fury, Collapse 등) 전부.
 *   미등록 색상은 적용/반응 시 조용히 스킵됨 (Console 경고 발생).
 *
 * [2] 초기화
 *   Initialize() 를 반드시 호출. 같은 GameObject 의 BuffableModule 을 자동으로 연결함.
 *
 * [3] 색상 적용
 *   ApplyColor(color, stack)                          // 상대에게 N스택 적용 (기본 Other)
 *   ApplyColor(color, stack, EmotionApplyTarget.Self) // 자신에게 적용
 *   ApplyColor(color, gear, stack)                    // 소스 Gear 직접 지정
 *
 * [4] 색상 제거
 *   RemoveColor(color)                                // 1스택 제거 (Self + Other 동시)
 *   RemoveColor(color, stack)                         // N스택 제거 (Self + Other 동시)
 *   RemoveColor(color, EmotionApplyTarget.Other, stack) // Other 만 제거
 *
 * [5] 조회
 *   GetStacks(color)                                  // Self/Other 중 큰 값 반환
 *   GetStacks(color, EmotionApplyTarget.Other)        // Other 스택만
 *   HasColor(color)                                   // 1스택 이상이면 true
 *   GetApproxRomanStack(color)                        // 5단위 반올림 로마자 표기 (UI용)
 *
 * [6] 반응은 자동 처리
 *   ApplyColor() 호출 시 기본 감정색(8종)이면 자동으로 반응 평가를 실행함.
 *   반응으로 생성된 색상(Sorrow 등)은 추가 반응을 트리거하지 않음.
 *
 * ────────────────────────────────────────────────────────────────────────────
 */
public class EmotionModule : InterfaceModule, IEmotionModule
{
    // Self: 자신에게 걸리는 긍정 효과 / Other: 상대(적)에게 걸리는 부정 효과
    public enum EmotionApplyTarget
    {
        Self,
        Other
    }

    private enum EmotionGroup
    {
        None,
        Sadness,
        Excitement,
        Happiness,
        Anger,
        Void,
        Fear
    }

    private enum ReactionStackMode
    {
        Sum,
        Product
    }

    [System.Serializable]
    public class EmotionBuffEntry
    {
        public EmotionColor color;

        [Header("Self = Positive")]
        public StatBuffSO selfBuff;

        [Header("Other = Negative")]
        public StatBuffSO otherBuff;
    }

    [Header("Manual Emotion Buffs")]
    [SerializeField] private List<EmotionBuffEntry> emotionBuffEntries = new();

    [Header("Reaction")]
    [SerializeField] private bool applyCancelColor = false;

    [Tooltip("합 스택이 이 값 이상이면 반응 버프 SO 내부 효과로 그로기/명중률 감소 등을 크게 잡아두면 됨. 현재 모듈은 반응 결과 스택을 넘겨주는 역할만 함.")]
    [SerializeField] private int highStackThreshold = 5;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    // Step 4.3(EmotionVector) — 새 벡터 조합 시스템이 대신 처리 중일 때 기존 자동 반응을 끄는 스위치.
    // EmotionVectorModule 등 외부에서만 설정한다. 기본값 false면 지금까지와 완전히 동일하게 동작한다.
    public bool SuppressLegacyReaction = false;

    private IBuffable _buffable;
    private BuffableModule _buffableModule;

    private readonly Dictionary<EmotionColor, StatBuffSO> _selfBuffMap = new();
    private readonly Dictionary<EmotionColor, StatBuffSO> _otherBuffMap = new();
    // 반응 평가 중 재귀 호출 방지 (반응 결과 적용이 다시 반응을 트리거하는 것을 막음)
    private bool _isEvaluatingReaction;

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IEmotionModule>(this);
    }

    public void Initialize()
    {
        _buffableModule = GetComponent<BuffableModule>();
        _buffable = _buffableModule;

        if (_buffable == null)
            _buffable = GetComponent<IBuffable>();

        _selfBuffMap.Clear();
        _otherBuffMap.Clear();

        for (int i = 0; i < emotionBuffEntries.Count; i++)
            RegisterEmotionBuffEntry(emotionBuffEntries[i]);

        if (showDebugLog)
            Debug.Log($"[EmotionModule] Emotion Buff Loaded: Self {_selfBuffMap.Count}, Other {_otherBuffMap.Count}");
    }

    private void RegisterEmotionBuffEntry(EmotionBuffEntry entry)
    {
        if (entry == null) return;
        RegisterEmotionBuff(entry.color, entry.selfBuff, _selfBuffMap, "Self");
        RegisterEmotionBuff(entry.color, entry.otherBuff, _otherBuffMap, "Other");
    }

    private void RegisterEmotionBuff(EmotionColor color, StatBuffSO buff, Dictionary<EmotionColor, StatBuffSO> targetMap, string mapName)
    {
        if (buff == null) return;

        if (!buff.IsEmotionBuff)
        {
            if (showDebugLog)
                Debug.LogWarning($"[EmotionModule] IsEmotionBuff가 꺼져 있음: {buff.name}");
            return;
        }

        if (buff.EmotionColor != color && showDebugLog)
            Debug.LogWarning($"[EmotionModule] Entry Color({color})와 Buff EmotionColor({buff.EmotionColor})가 다름: {buff.name}");

        if (targetMap.ContainsKey(color) && showDebugLog)
            Debug.LogWarning($"[EmotionModule] {mapName} EmotionColor 중복 등록됨: {color}");

        targetMap[color] = buff;
    }

    public void ApplyColor(EmotionColor color, int stack, float overrideDuration = -1f)
    {
        ApplyColor(color, GetCurrentSourceGear(), stack, EmotionApplyTarget.Other, overrideDuration);
    }

    public void ApplyColor(EmotionColor color, Gear sourceGear, int stack, float overrideDuration = -1f)
    {
        ApplyColor(color, sourceGear, stack, EmotionApplyTarget.Other, overrideDuration);
    }

    public void ApplyColor(EmotionColor color, int stack, EmotionApplyTarget applyTarget, float overrideDuration = -1f)
    {
        ApplyColor(color, GetCurrentSourceGear(), stack, applyTarget, overrideDuration);
    }

    public void ApplyColor(EmotionColor color, Gear sourceGear, int stack, EmotionApplyTarget applyTarget, float overrideDuration = -1f)
    {
        ApplyColorInternal(color, sourceGear, stack, applyTarget, overrideDuration, true);
    }

    private void ApplyColorInternal(EmotionColor color, Gear sourceGear, int stack, EmotionApplyTarget applyTarget, float overrideDuration, bool evaluateReaction)
    {
        if (_buffable == null || stack <= 0) return;

        if (!TryGetBuff(color, applyTarget, out StatBuffSO buff))
        {
            if (showDebugLog)
                Debug.LogWarning($"[EmotionModule] Emotion Buff not found: {color}, Target: {applyTarget}");
            return;
        }

        int previousStack = GetStacks(color, sourceGear, applyTarget);
        int nextStack = Mathf.Min(buff.MaxStack, previousStack + stack);
        int addStack = nextStack - previousStack;
        if (addStack <= 0) return;

        _buffable.Buff(buff, sourceGear, addStack, 1, overrideDuration);

        if (showDebugLog)
            Debug.Log($"[EmotionModule] Apply {color} / {applyTarget} +{addStack}, Total {nextStack}");

        if (evaluateReaction && IsBaseColor(color) && !SuppressLegacyReaction)
            EvaluateReaction(color, sourceGear, applyTarget, previousStack);
    }

    private bool TryGetBuff(EmotionColor color, EmotionApplyTarget applyTarget, out StatBuffSO buff)
    {
        Dictionary<EmotionColor, StatBuffSO> map = applyTarget == EmotionApplyTarget.Self ? _selfBuffMap : _otherBuffMap;
        return map.TryGetValue(color, out buff) && buff != null;
    }

    public int GetStacks(EmotionColor color) => GetStacks(color, GetCurrentSourceGear());

    // Self와 Other 중 큰 값 반환. 한 색상에 둘 다 활성화되는 경우는 없으므로 사실상 활성 스택 수
    public int GetStacks(EmotionColor color, Gear sourceGear)
    {
        return Mathf.Max(
            GetStacks(color, sourceGear, EmotionApplyTarget.Self),
            GetStacks(color, sourceGear, EmotionApplyTarget.Other)
        );
    }

    public int GetStacks(EmotionColor color, EmotionApplyTarget applyTarget)
    {
        return GetStacks(color, GetCurrentSourceGear(), applyTarget);
    }

    public int GetStacks(EmotionColor color, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        if (_buffable == null) return 0;
        if (!TryGetBuff(color, applyTarget, out StatBuffSO buff)) return 0;

        BuffInfo info = _buffable.FindBuff(buff, sourceGear);
        return info != null ? info.BuffStack : 0;
    }

    public bool HasColor(EmotionColor color) => GetStacks(color) > 0;
    public bool HasColor(EmotionColor color, Gear sourceGear) => GetStacks(color, sourceGear) > 0;
    public bool HasColor(EmotionColor color, EmotionApplyTarget applyTarget) => GetStacks(color, applyTarget) > 0;
    public bool HasColor(EmotionColor color, Gear sourceGear, EmotionApplyTarget applyTarget) => GetStacks(color, sourceGear, applyTarget) > 0;

    public void RemoveColor(EmotionColor color, int stack = 1)
    {
        RemoveColor(color, GetCurrentSourceGear(), stack);
    }

    // Self + Other 양쪽 모두에서 N스택 제거. 특정 대상만 제거하려면 ApplyTarget 오버로드 사용
    public void RemoveColor(EmotionColor color, Gear sourceGear, int stack = 1)
    {
        RemoveColor(color, sourceGear, EmotionApplyTarget.Self, stack);
        RemoveColor(color, sourceGear, EmotionApplyTarget.Other, stack);
    }

    public void RemoveColor(EmotionColor color, EmotionApplyTarget applyTarget, int stack = 1)
    {
        RemoveColor(color, GetCurrentSourceGear(), applyTarget, stack);
    }

    public void RemoveColor(EmotionColor color, Gear sourceGear, EmotionApplyTarget applyTarget, int stack = 1)
    {
        if (_buffable == null || stack <= 0) return;
        if (!TryGetBuff(color, applyTarget, out StatBuffSO buff)) return;

        _buffable.UnBuff(buff, sourceGear, stack);

        if (showDebugLog)
            Debug.Log($"[EmotionModule] Remove {color} / {applyTarget} -{stack}");
    }

    private void RemoveAll(EmotionColor color, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        int stack = GetStacks(color, sourceGear, applyTarget);
        if (stack > 0)
            RemoveColor(color, sourceGear, applyTarget, stack);
    }

    private void EvaluateReaction(EmotionColor appliedColor, Gear sourceGear, EmotionApplyTarget applyTarget, int previousStack)
    {
        if (_isEvaluatingReaction) return;
        _isEvaluatingReaction = true;

        try
        {
            // 우선순위: 상쇄/덮어쓰기 → 동그룹 반응 → 공허 반응 → 교차 반응
            if (TryCancelOrOverwrite(sourceGear, applyTarget)) return;
            if (TrySameGroupReaction(appliedColor, sourceGear, applyTarget, previousStack)) return;
            if (TryVoidReaction(appliedColor, sourceGear, applyTarget)) return;
            TryNormalReaction(appliedColor, sourceGear, applyTarget);
        }
        finally
        {
            _isEvaluatingReaction = false;
        }
    }

    private bool TryCancelOrOverwrite(Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        // 공포+행복: 행복만 제거하고 공포 유지 (덮어쓰기 — 공포가 행복을 압도)
        if (GetGroupStacks(EmotionGroup.Fear, sourceGear, applyTarget) > 0 &&
            GetGroupStacks(EmotionGroup.Happiness, sourceGear, applyTarget) > 0)
        {
            RemoveGroupStacks(EmotionGroup.Happiness, sourceGear, applyTarget, int.MaxValue);

            if (showDebugLog)
                Debug.Log("[EmotionModule] Fear overwrites Happiness");

            return true;
        }

        if (TryCancel(EmotionGroup.Void, EmotionGroup.Excitement, sourceGear, applyTarget)) return true;
        if (TryCancel(EmotionGroup.Anger, EmotionGroup.Happiness, sourceGear, applyTarget)) return true;
        return false;
    }

    private bool TryCancel(EmotionGroup a, EmotionGroup b, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        int stackA = GetGroupStacks(a, sourceGear, applyTarget);
        int stackB = GetGroupStacks(b, sourceGear, applyTarget);
        if (stackA <= 0 || stackB <= 0) return false;

        int resultStack = stackA + stackB;
        RemoveGroupStacks(a, sourceGear, applyTarget, stackA);
        RemoveGroupStacks(b, sourceGear, applyTarget, stackB);

        // EmotionVector Phase 4(useVectorCombination)에서 상쇄가 결과색을 안 남기는 걸로 흡수됨 —
        // 필요하면 이 두 줄만 살리면 원복됨(Cancel은 enum에 [Obsolete] 표시, 삭제 아님).
        // if (applyCancelColor)
        //     ApplyReactionColor(EmotionColor.Cancel, sourceGear, applyTarget, resultStack);

        if (showDebugLog)
            Debug.Log($"[EmotionModule] {a} + {b} = Cancel");

        return true;
    }

    private bool TrySameGroupReaction(EmotionColor appliedColor, Gear sourceGear, EmotionApplyTarget applyTarget, int previousStack)
    {
        EmotionGroup group = GetGroup(appliedColor);

        // 같은 그룹 내 서로 다른 색상 2종이 동시에 존재할 때만 반응 (단일 색상 중첩은 반응 없음)
        // 예: SadnessBlue + SadnessSky → 설움(Sorrow)
        if (GetActiveColorCountInGroup(group, sourceGear, applyTarget) < 2) return false;

        // EmotionVector Phase 4(useVectorCombination)에서 동일 사분면 중첩(단순 합산)으로 흡수됨 —
        // Sorrow/Madness/Fury 전부 [Obsolete] 표시만, 삭제 아님. 되돌리려면 아래 3줄만 살리면 됨.
        // if (group == EmotionGroup.Sadness)
        //     return ReactSameGroup(EmotionGroup.Sadness, EmotionColor.Sorrow, sourceGear, applyTarget, ReactionStackMode.Sum);
        //
        // if (group == EmotionGroup.Excitement)
        //     return ReactSameGroup(EmotionGroup.Excitement, EmotionColor.Madness, sourceGear, applyTarget, ReactionStackMode.Sum);
        //
        // if (group == EmotionGroup.Anger)
        //     return ReactSameGroup(EmotionGroup.Anger, EmotionColor.Fury, sourceGear, applyTarget, ReactionStackMode.Product);

        return false;
    }

    private bool ReactSameGroup(EmotionGroup group, EmotionColor result, Gear sourceGear, EmotionApplyTarget applyTarget, ReactionStackMode stackMode)
    {
        int totalStack = GetGroupStacks(group, sourceGear, applyTarget);
        if (totalStack <= 0) return false;

        if (!CanApplyColor(result, applyTarget))
        {
            if (showDebugLog)
                Debug.LogWarning($"[EmotionModule] Reaction Buff not found: {result}, Target: {applyTarget}");
            return false;
        }

        int resultStack = stackMode == ReactionStackMode.Product
            ? Mathf.Max(1, GetGroupStackProduct(group, sourceGear, applyTarget))
            : totalStack;

        RemoveGroupStacks(group, sourceGear, applyTarget, totalStack);
        ApplyReactionColor(result, sourceGear, applyTarget, resultStack);

        if (showDebugLog)
            Debug.Log($"[EmotionModule] {group} + {group} = {result}, Stack {resultStack}, TotalBefore {totalStack}, Threshold {highStackThreshold}");

        return true;
    }

    // EmotionVector Phase 4(useVectorCombination)에서 공허 촉매(spec §2.4)로 대체됨 — VoidReaction은
    // [Obsolete] 표시만, 삭제 아님. 되돌리려면 return false; 줄 지우고 아래 원본 로직 주석만 풀면 됨.
    // 공허(Void)는 흥분(Excitement) 을 제외한 모든 그룹과 곱 방식으로 반응 → VoidReaction
    private bool TryVoidReaction(EmotionColor appliedColor, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        return false;
        // EmotionGroup appliedGroup = GetGroup(appliedColor);
        // if (appliedGroup == EmotionGroup.Excitement) return false;
        // if (GetGroupStacks(EmotionGroup.Void, sourceGear, applyTarget) <= 0) return false;
        // if (appliedGroup == EmotionGroup.Void)
        // {
        //     return TryPair(EmotionGroup.Void, EmotionGroup.Sadness, EmotionColor.VoidReaction, sourceGear, applyTarget, ReactionStackMode.Product)
        //         || TryPair(EmotionGroup.Void, EmotionGroup.Happiness, EmotionColor.VoidReaction, sourceGear, applyTarget, ReactionStackMode.Product)
        //         || TryPair(EmotionGroup.Void, EmotionGroup.Anger, EmotionColor.VoidReaction, sourceGear, applyTarget, ReactionStackMode.Product)
        //         || TryPair(EmotionGroup.Void, EmotionGroup.Fear, EmotionColor.VoidReaction, sourceGear, applyTarget, ReactionStackMode.Product);
        // }
        //
        // if (appliedGroup == EmotionGroup.None || appliedGroup == EmotionGroup.Void) return false;
        // return TryPair(appliedGroup, EmotionGroup.Void, EmotionColor.VoidReaction, sourceGear, applyTarget, ReactionStackMode.Product);
    }

    // 교차 반응 전체 (기획서 반응표):
    //   슬픔+흥분→붕괴(곱)  슬픔+공포→붕괴(곱)  행복+흥분→광기(합)  분노+흥분→광기(합)
    //   슬픔+행복→그리움(합) 슬픔+분노→울분(곱)  공포+흥분→혼비백산(합) 공포+분노→허세(합)
    private bool TryNormalReaction(EmotionColor appliedColor, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        EmotionGroup appliedGroup = GetGroup(appliedColor);

        // Madness/Resentment/Panic 결과 분기는 EmotionVector Phase 4(useVectorCombination)에서
        // 각각 각성 역치 상태/대체(Replace) 판정/(현재는 미사용 분기, 이후 스트레스+만족 복합으로
        // 재활용)로 흡수됨 — [Obsolete] 표시만, 삭제 아님. 되돌리려면 아래 주석 처리된 3줄만 살리면 됨.
        return TryPair(appliedGroup, EmotionGroup.Sadness, EmotionGroup.Excitement, EmotionColor.Collapse, sourceGear, applyTarget, ReactionStackMode.Product)
            || TryPair(appliedGroup, EmotionGroup.Sadness, EmotionGroup.Fear, EmotionColor.Collapse, sourceGear, applyTarget, ReactionStackMode.Product)
            // || TryPair(appliedGroup, EmotionGroup.Happiness, EmotionGroup.Excitement, EmotionColor.Madness, sourceGear, applyTarget, ReactionStackMode.Sum)
            // || TryPair(appliedGroup, EmotionGroup.Anger, EmotionGroup.Excitement, EmotionColor.Madness, sourceGear, applyTarget, ReactionStackMode.Sum)
            || TryPair(appliedGroup, EmotionGroup.Sadness, EmotionGroup.Happiness, EmotionColor.Longing, sourceGear, applyTarget, ReactionStackMode.Sum)
            // || TryPair(appliedGroup, EmotionGroup.Sadness, EmotionGroup.Anger, EmotionColor.Resentment, sourceGear, applyTarget, ReactionStackMode.Product)
            // || TryPair(appliedGroup, EmotionGroup.Fear, EmotionGroup.Excitement, EmotionColor.Panic, sourceGear, applyTarget, ReactionStackMode.Sum)
            || TryPair(appliedGroup, EmotionGroup.Fear, EmotionGroup.Anger, EmotionColor.Bluff, sourceGear, applyTarget, ReactionStackMode.Sum);
    }

    private bool TryPair(EmotionGroup appliedGroup, EmotionGroup a, EmotionGroup b, EmotionColor result, Gear sourceGear, EmotionApplyTarget applyTarget, ReactionStackMode stackMode)
    {
        if (appliedGroup != a && appliedGroup != b) return false;
        return TryPair(a, b, result, sourceGear, applyTarget, stackMode);
    }

    private bool TryPair(EmotionGroup a, EmotionGroup b, EmotionColor result, Gear sourceGear, EmotionApplyTarget applyTarget, ReactionStackMode stackMode)
    {
        int stackA = GetGroupStacks(a, sourceGear, applyTarget);
        int stackB = GetGroupStacks(b, sourceGear, applyTarget);
        if (stackA <= 0 || stackB <= 0) return false;

        if (!CanApplyColor(result, applyTarget))
        {
            if (showDebugLog)
                Debug.LogWarning($"[EmotionModule] Reaction Buff not found: {result}, Target: {applyTarget}");
            return false;
        }

        int resultStack = stackMode == ReactionStackMode.Product ? stackA * stackB : stackA + stackB;

        RemoveGroupStacks(a, sourceGear, applyTarget, stackA);
        RemoveGroupStacks(b, sourceGear, applyTarget, stackB);
        ApplyReactionColor(result, sourceGear, applyTarget, resultStack);

        if (showDebugLog)
            Debug.Log($"[EmotionModule] {a} + {b} = {result}, Stack {resultStack}, Sum {stackA + stackB}, Product {stackA * stackB}, Threshold {highStackThreshold}");

        return true;
    }

    private void ApplyReactionColor(EmotionColor color, Gear sourceGear, EmotionApplyTarget applyTarget, int stack)
    {
        ApplyColorInternal(color, sourceGear, Mathf.Max(1, stack), applyTarget, -1f, false);
    }

    private bool CanApplyColor(EmotionColor color, EmotionApplyTarget applyTarget)
    {
        return TryGetBuff(color, applyTarget, out _);
    }

    private bool IsBaseColor(EmotionColor color)
    {
        return GetGroup(color) != EmotionGroup.None;
    }

    private EmotionGroup GetGroup(EmotionColor color)
    {
        switch (color)
        {
            case EmotionColor.SadnessBlue:
            case EmotionColor.SadnessSky:
                return EmotionGroup.Sadness;

            case EmotionColor.ExcitementDeepPink:
                return EmotionGroup.Excitement;

            case EmotionColor.HappinessYellow:
                return EmotionGroup.Happiness;

            case EmotionColor.AngerOrange:
            case EmotionColor.AngerScarlet:
                return EmotionGroup.Anger;

            case EmotionColor.VoidBlack:
                return EmotionGroup.Void;

            case EmotionColor.FearDarkRed:
                return EmotionGroup.Fear;

            default:
                return EmotionGroup.None;
        }
    }

    private EmotionColor[] GetGroupColors(EmotionGroup group)
    {
        switch (group)
        {
            case EmotionGroup.Sadness:
                return new[] { EmotionColor.SadnessBlue, EmotionColor.SadnessSky };
            case EmotionGroup.Excitement:
                return new[] { EmotionColor.ExcitementDeepPink };
            case EmotionGroup.Happiness:
                return new[] { EmotionColor.HappinessYellow };
            case EmotionGroup.Anger:
                return new[] { EmotionColor.AngerOrange, EmotionColor.AngerScarlet };
            case EmotionGroup.Void:
                return new[] { EmotionColor.VoidBlack };
            case EmotionGroup.Fear:
                return new[] { EmotionColor.FearDarkRed };
            default:
                return new EmotionColor[0];
        }
    }


    private int GetActiveColorCountInGroup(EmotionGroup group, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        int count = 0;
        EmotionColor[] colors = GetGroupColors(group);

        for (int i = 0; i < colors.Length; i++)
        {
            if (GetStacks(colors[i], sourceGear, applyTarget) > 0)
                count++;
        }

        return count;
    }

    private int GetGroupStacks(EmotionGroup group, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        int result = 0;
        EmotionColor[] colors = GetGroupColors(group);
        for (int i = 0; i < colors.Length; i++)
            result += GetStacks(colors[i], sourceGear, applyTarget);
        return result;
    }

    private int GetGroupStackProduct(EmotionGroup group, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        int product = 1;
        bool hasAny = false;

        EmotionColor[] colors = GetGroupColors(group);
        for (int i = 0; i < colors.Length; i++)
        {
            int stack = GetStacks(colors[i], sourceGear, applyTarget);
            if (stack <= 0) continue;

            product *= stack;
            hasAny = true;
        }

        return hasAny ? product : 0;
    }

    private void RemoveGroupStacks(EmotionGroup group, Gear sourceGear, EmotionApplyTarget applyTarget, int stackToRemove)
    {
        EmotionColor[] colors = GetGroupColors(group);
        int remain = stackToRemove;

        for (int i = 0; i < colors.Length; i++)
        {
            if (remain <= 0) return;

            int stack = GetStacks(colors[i], sourceGear, applyTarget);
            if (stack <= 0) continue;

            int remove = Mathf.Min(stack, remain);
            RemoveColor(colors[i], sourceGear, applyTarget, remove);
            remain -= remove;
        }
    }

    private Gear GetCurrentSourceGear()
    {
        if (_buffableModule == null)
            _buffableModule = GetComponent<BuffableModule>();

        return _buffableModule != null ? _buffableModule.GetCurrentSourceGear() : null;
    }


    public string GetApproxRomanStack(EmotionColor color)
    {
        return GetApproxRomanStack(color, GetCurrentSourceGear());
    }

    public string GetApproxRomanStack(EmotionColor color, Gear sourceGear)
    {
        int stack = GetStacks(color, sourceGear);
        return StackToRomanText(stack);
    }

    public string GetApproxRomanStack(EmotionColor color, EmotionApplyTarget applyTarget)
    {
        return GetApproxRomanStack(color, GetCurrentSourceGear(), applyTarget);
    }

    public string GetApproxRomanStack(EmotionColor color, Gear sourceGear, EmotionApplyTarget applyTarget)
    {
        int stack = GetStacks(color, sourceGear, applyTarget);
        return StackToRomanText(stack);
    }

    private string StackToRomanText(int stack)
    {
        if (stack <= 0) return string.Empty;

        // 5단위 반올림 표기. 최솟값 V (1~9 → V, 10~14 → X …)
        int rounded = Mathf.Max(5, (stack / 5) * 5);
        return ToRoman(rounded);
    }

    private string ToRoman(int number)
    {
        // 뺄셈 표기 없이 순수 덧셈만 사용 (40=XXXX, 90=LXXXX)
        var map = new (int value, string symbol)[]
        {
            (100, "C"), (50, "L"), (10, "X"), (5, "V")
        };

        System.Text.StringBuilder sb = new();

        for (int i = 0; i < map.Length; i++)
        {
            while (number >= map[i].value)
            {
                sb.Append(map[i].symbol);
                number -= map[i].value;
            }
        }

        return sb.ToString();
    }
}
