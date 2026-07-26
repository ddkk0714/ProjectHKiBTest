using NUnit.Framework;

public class EmotionVectorTests
{
    [Test]
    public void Addition_ComputesMagnitudeAndAngle()
    {
        EmotionVector sum = new EmotionVector(1f, 0f) + new EmotionVector(0f, 1f);
        Assert.AreEqual(1.414f, sum.Magnitude, 0.01f);
        Assert.AreEqual(45f, sum.AngleDeg, 0.1f);
    }

    [Test]
    public void Quadrant_NegativeXPositiveY_Returns2()
    {
        var v = new EmotionVector(-0.6f, 0.38f);
        Assert.AreEqual(2, v.Quadrant);
    }

    [Test]
    public void Quadrant_WithinDeadZone_ReturnsZero()
    {
        var v = new EmotionVector(0.03f, 0.02f);
        Assert.AreEqual(0, v.Quadrant);
    }

    [Test]
    public void Multiply_ScalesComponents()
    {
        EmotionVector v = new EmotionVector(2f, -3f) * 2f;
        Assert.AreEqual(4f, v.X, 0.001f);
        Assert.AreEqual(-6f, v.Y, 0.001f);
    }
}
