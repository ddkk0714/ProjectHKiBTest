using NUnit.Framework;
using UnityEngine;

// Step 4.1 게이트 — spec §10 검증쌍 중 지금 구현된 7색으로 실제 재현 가능한 것 전부(#1,#2,#3,#5,#6).
// #4(스트레스+흥분→상쇄)는 스트레스가 P5 신규 색이라 아직 실좌표로 테스트 불가 — Cancel 분기 자체는
// 합성 좌표로 별도 검증한다. #7(공허 촉매)은 Step 2.3에서 이미 검증 완료, #8(재료 재공급)은 Step 4.2 대상.
public class EmotionCombinationEvaluatorTests
{
    // spec §2.2 좌표표
    private static readonly Vector2 Fear = new(-0.72f, 0.75f);          // 공포 FearDarkRed
    private static readonly Vector2 Hate = new(-0.85f, 0.55f);          // 증오 AngerScarlet
    private static readonly Vector2 Anger = new(-0.60f, 0.38f);         // 분노 AngerOrange
    private static readonly Vector2 Excitement = new(0.18f, 0.60f);     // 흥분 ExcitementDeepPink
    private static readonly Vector2 Happiness = new(0.78f, 0.12f);      // 행복 HappinessYellow
    private static readonly Vector2 Melancholy = new(-0.50f, -0.62f);   // 우울 SadnessSky
    private static readonly Vector2 Sadness = new(-0.62f, -0.45f);      // 슬픔 SadnessBlue

    private const float ReplaceThreshold = 0.35f;

    [Test]
    public void ComputeS_MatchesSpecTable()
    {
        Assert.AreEqual(1.04f, EmotionCombinationEvaluator.ComputeS(Fear), 0.01f);
        Assert.AreEqual(0.99f, EmotionCombinationEvaluator.ComputeS(Hate), 0.01f);
        Assert.AreEqual(0.69f, EmotionCombinationEvaluator.ComputeS(Anger), 0.01f);
        Assert.AreEqual(0.30f, EmotionCombinationEvaluator.ComputeS(Excitement), 0.01f);
        Assert.AreEqual(-0.47f, EmotionCombinationEvaluator.ComputeS(Happiness), 0.01f);
        Assert.AreEqual(-0.08f, EmotionCombinationEvaluator.ComputeS(Melancholy), 0.01f);
        Assert.AreEqual(0.12f, EmotionCombinationEvaluator.ComputeS(Sadness), 0.01f);
    }

    // #1 슬픔 + 행복 -> 그리움(4사분면 복합)
    [Test]
    public void SadnessPlusHappiness_ProducesCompositeInQuadrant4()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Sadness, 1, EmotionColor.SadnessBlue,
            Happiness, 1, EmotionColor.HappinessYellow,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Composite, result.Type);
        Assert.AreEqual(4, result.CompositeQuadrant);
    }

    // #2 슬픔 + 흥분 -> 붕괴(2사분면 복합). #1과 똑같이 "반대,반대"인데 사분면이 갈라지는 게 핵심 검증.
    [Test]
    public void SadnessPlusExcitement_ProducesCompositeInQuadrant2()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Sadness, 1, EmotionColor.SadnessBlue,
            Excitement, 1, EmotionColor.ExcitementDeepPink,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Composite, result.Type);
        Assert.AreEqual(2, result.CompositeQuadrant);
    }

    // #3 공포 + 행복 -> 공포 잔존, 행복 소멸 (직선 대체, s차 1.51)
    [Test]
    public void FearPlusHappiness_ReplacesWithFearSurviving()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Fear, 10, EmotionColor.FearDarkRed,
            Happiness, 10, EmotionColor.HappinessYellow,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Replace, result.Type);
        Assert.AreEqual(EmotionColor.FearDarkRed, result.Winner);
        Assert.AreEqual(10, result.ConsumedStack);
    }

    // #5 슬픔 + 우울 -> 중첩(동일 3사분면). 잠 역치로 이어지는 건 기존 EvaluateThresholds()가 처리 —
    // 여기서는 "조합 판정이 아무 것도 안 한다(Overlap)"만 확인한다.
    [Test]
    public void SadnessPlusMelancholy_IsOverlap_NoSpecialHandling()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Sadness, 10, EmotionColor.SadnessBlue,
            Melancholy, 10, EmotionColor.SadnessSky,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Overlap, result.Type);
    }

    // #6 분노 + 흥분 -> 분노 잔존(직선 대체, s차 0.396 — 대체임계 0.35를 겨우 넘는 경계 케이스)
    [Test]
    public void AngerPlusExcitement_ReplacesWithAngerSurviving()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Anger, 5, EmotionColor.AngerOrange,
            Excitement, 5, EmotionColor.ExcitementDeepPink,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Replace, result.Type);
        Assert.AreEqual(EmotionColor.AngerOrange, result.Winner);
    }

    // spec §11 Q1 확정 — 슬픔 + 분노는 대체(분노 잔존), 원한(Resentment)은 폐기
    [Test]
    public void SadnessPlusAnger_ReplacesWithAngerSurviving()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Sadness, 5, EmotionColor.SadnessBlue,
            Anger, 5, EmotionColor.AngerOrange,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Replace, result.Type);
        Assert.AreEqual(EmotionColor.AngerOrange, result.Winner);
    }

    // 현재 구현된 7색 중에는 Cancel(상쇄)이 실제로 나오는 조합이 없음(스트레스가 P5 신규라서) —
    // 그래도 로직 자체는 합성 좌표로 검증해둔다. spec #4 "스트레스(-0.18,+0.60) + 흥분(0.18,+0.60)"과
    // 동일한 형태(각성축 위에서 x부호만 반대, s차가 작음)를 재현.
    [Test]
    public void CloseSValues_ProduceCancel()
    {
        var stressLike = new Vector2(-0.18f, 0.60f);

        var result = EmotionCombinationEvaluator.Evaluate(
            stressLike, 10, EmotionColor.AngerOrange, // 색 자체는 임의 — 좌표만 시험용
            Excitement, 10, EmotionColor.ExcitementDeepPink,
            ReplaceThreshold);

        Assert.AreEqual(EmotionCombinationType.Cancel, result.Type);
        Assert.AreEqual(10, result.ConsumedStack);
    }

    [Test]
    public void ConsumedStack_IsMinimumOfBothStacks()
    {
        var result = EmotionCombinationEvaluator.Evaluate(
            Fear, 30, EmotionColor.FearDarkRed,
            Happiness, 12, EmotionColor.HappinessYellow,
            ReplaceThreshold);

        Assert.AreEqual(12, result.ConsumedStack);
    }
}
