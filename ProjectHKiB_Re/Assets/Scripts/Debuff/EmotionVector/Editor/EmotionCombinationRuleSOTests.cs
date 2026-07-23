using NUnit.Framework;
using UnityEngine;

// 게이트 2.3 — 공허 촉매 공식 자체를 실제 버프 시스템 없이 검증 (spec §2.4)
public class EmotionCombinationRuleSOTests
{
    private static EmotionCombinationRuleSO CreateRule()
    {
        // 기본값(voidSaturation=120, maxReduction=0.7, minThresholdRatio=0.3)을 그대로 사용
        return ScriptableObject.CreateInstance<EmotionCombinationRuleSO>();
    }

    [Test]
    public void VoidZero_ThresholdUnchanged()
    {
        var rule = CreateRule();
        Assert.AreEqual(50f, rule.ApplyCatalyst(50f, 0), 0.001f);
        Object.DestroyImmediate(rule);
    }

    [Test]
    public void Void60_ThresholdIs65Percent()
    {
        var rule = CreateRule();
        float result = rule.ApplyCatalyst(50f, 60);
        Assert.AreEqual(50f * 0.65f, result, 0.01f);
        Object.DestroyImmediate(rule);
    }

    [Test]
    public void Void120_ThresholdAtFloor_30Percent()
    {
        var rule = CreateRule();
        float result = rule.ApplyCatalyst(50f, 120);
        Assert.AreEqual(50f * 0.3f, result, 0.01f);
        Object.DestroyImmediate(rule);
    }

    [Test]
    public void VoidBeyondSaturation_DoesNotGoBelowFloor()
    {
        var rule = CreateRule();
        float at120 = rule.ApplyCatalyst(50f, 120);
        float at200 = rule.ApplyCatalyst(50f, 200);
        Assert.AreEqual(at120, at200, 0.001f);
        Assert.AreEqual(50f * 0.3f, at200, 0.01f);
        Object.DestroyImmediate(rule);
    }

    [Test]
    public void UnconfiguredAxis_StaysPositiveInfinity_RegardlessOfVoid()
    {
        var rule = CreateRule();
        float result = rule.ApplyCatalyst(float.PositiveInfinity, 200);
        Assert.IsTrue(float.IsPositiveInfinity(result));
        Object.DestroyImmediate(rule);
    }
}
