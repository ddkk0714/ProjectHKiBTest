using NUnit.Framework;

// 게이트 2.2 중 순수 계산 부분만 유닛 테스트로 분리 검증. 실제 활성/해제/히스테리시스/locked
// 동작은 계획서상 🔧 씬 테스트 항목이라 Play 모드에서 직접 확인한다.
public class EmotionVectorThresholdMathTests
{
    [Test]
    public void AxisToState_MapsCorrectly()
    {
        Assert.AreEqual(EmotionState.Madness, EmotionVectorModule.AxisToState(EmotionAxis.PositiveY));
        Assert.AreEqual(EmotionState.Sleep, EmotionVectorModule.AxisToState(EmotionAxis.NegativeY));
        Assert.AreEqual(EmotionState.Doom, EmotionVectorModule.AxisToState(EmotionAxis.NegativeX));
        Assert.AreEqual(EmotionState.Ecstasy, EmotionVectorModule.AxisToState(EmotionAxis.PositiveX));
    }

    [Test]
    public void GetAxisValue_FlipsSignForNegativeAxes()
    {
        var v = new EmotionVector(-30f, 40f);

        Assert.AreEqual(40f, EmotionVectorModule.GetAxisValue(v, EmotionAxis.PositiveY), 0.001f);
        Assert.AreEqual(-40f, EmotionVectorModule.GetAxisValue(v, EmotionAxis.NegativeY), 0.001f);
        Assert.AreEqual(30f, EmotionVectorModule.GetAxisValue(v, EmotionAxis.NegativeX), 0.001f);
        Assert.AreEqual(-30f, EmotionVectorModule.GetAxisValue(v, EmotionAxis.PositiveX), 0.001f);
    }

    // --- EvaluateAxisActive: 실제 버프 시스템 없이 히스테리시스/locked 로직 자체를 검증 (게이트 2.2) ---

    [Test]
    public void Inactive_CrossesThreshold_Activates()
    {
        Assert.IsTrue(EmotionVectorModule.EvaluateAxisActive(wasActive: false, value: 50f, threshold: 50f, hysteresis: 5f, locked: false));
        Assert.IsFalse(EmotionVectorModule.EvaluateAxisActive(wasActive: false, value: 49.9f, threshold: 50f, hysteresis: 5f, locked: false));
    }

    [Test]
    public void Active_StaysActive_WhileWithinHysteresisBand()
    {
        // 활성화 후 threshold 밑으로 살짝 내려가도(45~50 사이, hysteresis=5) 꺼지면 안 됨 — 깜빡임 방지
        Assert.IsTrue(EmotionVectorModule.EvaluateAxisActive(wasActive: true, value: 46f, threshold: 50f, hysteresis: 5f, locked: false));
        Assert.IsTrue(EmotionVectorModule.EvaluateAxisActive(wasActive: true, value: 45f, threshold: 50f, hysteresis: 5f, locked: false));
    }

    [Test]
    public void Active_DropsBelowHysteresisBand_Deactivates()
    {
        Assert.IsFalse(EmotionVectorModule.EvaluateAxisActive(wasActive: true, value: 44.9f, threshold: 50f, hysteresis: 5f, locked: false));
    }

    [Test]
    public void Locked_NeverDeactivates_EvenFarBelowThreshold()
    {
        Assert.IsTrue(EmotionVectorModule.EvaluateAxisActive(wasActive: true, value: -1000f, threshold: 50f, hysteresis: 5f, locked: true));
    }

    [Test]
    public void FullCycle_ActivateThenDeactivateThenReactivate_WorksCorrectly()
    {
        bool active = false;

        active = EmotionVectorModule.EvaluateAxisActive(active, value: 60f, threshold: 50f, hysteresis: 5f, locked: false);
        Assert.IsTrue(active, "60 >= 50 이므로 활성화되어야 함");

        active = EmotionVectorModule.EvaluateAxisActive(active, value: 47f, threshold: 50f, hysteresis: 5f, locked: false);
        Assert.IsTrue(active, "히스테리시스 구간(45~50) 안이라 유지되어야 함");

        active = EmotionVectorModule.EvaluateAxisActive(active, value: 0f, threshold: 50f, hysteresis: 5f, locked: false);
        Assert.IsFalse(active, "완전히 빠졌으므로(0 < 45) 해제되어야 함");

        active = EmotionVectorModule.EvaluateAxisActive(active, value: 55f, threshold: 50f, hysteresis: 5f, locked: false);
        Assert.IsTrue(active, "다시 넘었으므로 재부여되어야 함");
    }
}
