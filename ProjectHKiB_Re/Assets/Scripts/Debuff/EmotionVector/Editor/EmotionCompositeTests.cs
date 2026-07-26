using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Step 4.2 게이트 — 복합 감정 3종(그리움/붕괴/허세 재활용) 등록 검증.
// 신규 EmotionColor를 추가하지 않고 이미 구현되어 있던 반응색(Longing/Collapse/Bluff)을
// 재사용하는 방침이라(spec §4.5 "Panic, Bluff → 재활용 검토"), 실제 프로덕션 에셋
// (EmotionVectorTable_Default, EmotionCombinationRuleSO_Default)을 직접 로드해 검증한다.
public class EmotionCompositeTests
{
    private const string TablePath = "Assets/Scripts/Debuff/EmotionVector/EmotionVectorTable_Default.asset";
    private const string RulePath = "Assets/Scripts/Debuff/EmotionVector/EmotionCombinationRuleSO_Default.asset";

    private static readonly Vector2 Sadness = new(-0.62f, -0.45f);      // 슬픔 SadnessBlue
    private static readonly Vector2 Melancholy = new(-0.50f, -0.62f);   // 우울 SadnessSky
    private static readonly Vector2 Excitement = new(0.18f, 0.60f);     // 흥분 ExcitementDeepPink
    private static readonly Vector2 Happiness = new(0.78f, 0.12f);      // 행복 HappinessYellow

    private const float ReplaceThreshold = 0.35f;

    private EmotionVectorTableSO _table;
    private EmotionCombinationRuleSO _rule;

    [SetUp]
    public void LoadAssets()
    {
        _table = AssetDatabase.LoadAssetAtPath<EmotionVectorTableSO>(TablePath);
        _rule = AssetDatabase.LoadAssetAtPath<EmotionCombinationRuleSO>(RulePath);
        Assert.IsNotNull(_table, $"테스트 대상 에셋을 찾을 수 없음: {TablePath}");
        Assert.IsNotNull(_rule, $"테스트 대상 에셋을 찾을 수 없음: {RulePath}");
    }

    // 복합 3종 모두 GetPosition이 (0,0)이 아니어야 함 — 게이트 4.2 "스탯 효과가 0이 아님"
    [Test]
    public void CompositeColors_HaveNonZeroPosition()
    {
        Assert.AreNotEqual(Vector2.zero, _table.GetPosition(EmotionColor.Longing));
        Assert.AreNotEqual(Vector2.zero, _table.GetPosition(EmotionColor.Collapse));
        Assert.AreNotEqual(Vector2.zero, _table.GetPosition(EmotionColor.Bluff));
    }

    [Test]
    public void CompositeColors_FallInExpectedQuadrant()
    {
        Assert.AreEqual(4, new EmotionVector(_table.GetPosition(EmotionColor.Longing).x, _table.GetPosition(EmotionColor.Longing).y).Quadrant);
        Assert.AreEqual(2, new EmotionVector(_table.GetPosition(EmotionColor.Collapse).x, _table.GetPosition(EmotionColor.Collapse).y).Quadrant);
        Assert.AreEqual(3, new EmotionVector(_table.GetPosition(EmotionColor.Bluff).x, _table.GetPosition(EmotionColor.Bluff).y).Quadrant);
    }

    // 사분면 -> 복합색 매핑 (spec §4.3)
    [Test]
    public void RuleAsset_MapsQuadrantsToReusedColors()
    {
        Assert.IsTrue(_rule.TryGetCompositeColor(4, out EmotionColor q4));
        Assert.AreEqual(EmotionColor.Longing, q4);

        Assert.IsTrue(_rule.TryGetCompositeColor(2, out EmotionColor q2));
        Assert.AreEqual(EmotionColor.Collapse, q2);

        Assert.IsTrue(_rule.TryGetCompositeColor(3, out EmotionColor q3));
        Assert.AreEqual(EmotionColor.Bluff, q3);

        // 스트레스+만족(Phase 5 신규) 도입으로 1사분면도 도달 가능해져서 Panic(기존 미사용 반응색)을 재활용 등록함
        Assert.IsTrue(_rule.TryGetCompositeColor(1, out EmotionColor q1));
        Assert.AreEqual(EmotionColor.Panic, q1);
    }

    // 스트레스+만족이 1사분면으로 떨어져 Panic 재활용 복합으로 해석되는지 (spec §4.3이 예견한 신규 케이스)
    [Test]
    public void StressPlusSatisfaction_ResolvesToPanic()
    {
        Vector2 stress = _table.GetPosition(EmotionColor.Stress);
        Vector2 satisfaction = _table.GetPosition(EmotionColor.Satisfaction);

        var result = EmotionCombinationEvaluator.Evaluate(
            stress, 10, EmotionColor.Stress,
            satisfaction, 10, EmotionColor.Satisfaction,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Composite, result.Type);
        Assert.IsTrue(_rule.TryGetCompositeColor(result.CompositeQuadrant, out EmotionColor composite));
        Assert.AreEqual(EmotionColor.Panic, composite);
    }

    // #1/#2 재현 + 사분면->색 해석까지 엔드투엔드로 확인 (기존 4.1 테스트는 사분면 숫자까지만 검증했음)
    [Test]
    public void SadnessPlusHappiness_ResolvesToLonging()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Sadness, 10, EmotionColor.SadnessBlue,
            Happiness, 10, EmotionColor.HappinessYellow,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Composite, result.Type);
        Assert.IsTrue(_rule.TryGetCompositeColor(result.CompositeQuadrant, out EmotionColor composite));
        Assert.AreEqual(EmotionColor.Longing, composite);
    }

    [Test]
    public void MelancholyPlusHappiness_AlsoResolvesToLonging()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Melancholy, 10, EmotionColor.SadnessSky,
            Happiness, 10, EmotionColor.HappinessYellow,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Composite, result.Type);
        Assert.IsTrue(_rule.TryGetCompositeColor(result.CompositeQuadrant, out EmotionColor composite));
        Assert.AreEqual(EmotionColor.Longing, composite);
    }

    [Test]
    public void SadnessPlusExcitement_ResolvesToCollapse()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Sadness, 10, EmotionColor.SadnessBlue,
            Excitement, 10, EmotionColor.ExcitementDeepPink,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Composite, result.Type);
        Assert.IsTrue(_rule.TryGetCompositeColor(result.CompositeQuadrant, out EmotionColor composite));
        Assert.AreEqual(EmotionColor.Collapse, composite);
    }

    // 핵심 검증 — 슬픔+흥분과 우울+흥분이 서로 다른 복합으로 갈라짐 (spec §4.3, 붕괴 vs 3사분면 신규)
    [Test]
    public void MelancholyPlusExcitement_ResolvesToBluff_NotCollapse()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Melancholy, 10, EmotionColor.SadnessSky,
            Excitement, 10, EmotionColor.ExcitementDeepPink,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Composite, result.Type);
        Assert.IsTrue(_rule.TryGetCompositeColor(result.CompositeQuadrant, out EmotionColor composite));
        Assert.AreEqual(EmotionColor.Bluff, composite);
        Assert.AreNotEqual(EmotionColor.Collapse, composite);
    }

    // 재료 재공급 판정 (spec §4.4) — 그리움 활성 중 행복 재공급은 재료로 인정, 흥분은 재료 아님
    [Test]
    public void IsMaterialOf_RecognizesValidMaterialsOnly()
    {
        Assert.IsTrue(_rule.IsMaterialOf(EmotionColor.Longing, EmotionColor.HappinessYellow));
        Assert.IsTrue(_rule.IsMaterialOf(EmotionColor.Longing, EmotionColor.SadnessBlue));
        Assert.IsTrue(_rule.IsMaterialOf(EmotionColor.Longing, EmotionColor.SadnessSky));
        Assert.IsFalse(_rule.IsMaterialOf(EmotionColor.Longing, EmotionColor.ExcitementDeepPink));

        Assert.IsTrue(_rule.IsMaterialOf(EmotionColor.Collapse, EmotionColor.SadnessBlue));
        Assert.IsFalse(_rule.IsMaterialOf(EmotionColor.Collapse, EmotionColor.SadnessSky), "붕괴는 슬픔 전용 — 우울+흥분은 별개 복합(허세)");

        Assert.IsTrue(_rule.IsMaterialOf(EmotionColor.Bluff, EmotionColor.SadnessSky));
        Assert.IsFalse(_rule.IsMaterialOf(EmotionColor.Bluff, EmotionColor.SadnessBlue));

        Assert.IsTrue(_rule.IsMaterialOf(EmotionColor.Panic, EmotionColor.Stress));
        Assert.IsTrue(_rule.IsMaterialOf(EmotionColor.Panic, EmotionColor.Satisfaction));
        Assert.IsFalse(_rule.IsMaterialOf(EmotionColor.Panic, EmotionColor.HappinessYellow));
    }

    // 재료 재공급 스택/지속시간 규칙(spec §4.4) — 0.5배, 반올림
    [Test]
    public void ComputeReplenishStack_HalvesIncomingStack()
    {
        Assert.AreEqual(10, EmotionCombinationEvaluator.ComputeReplenishStack(20));
        Assert.AreEqual(6, EmotionCombinationEvaluator.ComputeReplenishStack(11)); // 5.5 -> 반올림(Mathf.RoundToInt는 짝수 쪽으로 반올림하되 6이 짝수라 6)
    }

    [Test]
    public void ComputeFusionStack_AppliesFusionEfficiency()
    {
        Assert.AreEqual(10, EmotionCombinationEvaluator.ComputeFusionStack(10, 1f));
        Assert.AreEqual(5, EmotionCombinationEvaluator.ComputeFusionStack(10, 0.5f));
    }
}
