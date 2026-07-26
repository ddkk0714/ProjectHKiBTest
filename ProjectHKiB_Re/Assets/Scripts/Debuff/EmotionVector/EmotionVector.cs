using UnityEngine;

// 감정 평면(긍정도-각성도) 위의 벡터. 좌표는 정규화하지 않는다 — 원점에서의 거리가 감정 강도 그 자체다 (spec §2.2).
public readonly struct EmotionVector
{
    private const float DeadZone = 0.05f; // spec §4.1 Q6 확정값

    public readonly float X; // 긍정도 (Valence)
    public readonly float Y; // 각성도 (Arousal)

    public static readonly EmotionVector Zero = new EmotionVector(0f, 0f);

    public EmotionVector(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float Magnitude => Mathf.Sqrt(X * X + Y * Y);
    public float AngleDeg => Mathf.Atan2(Y, X) * Mathf.Rad2Deg;

    // 1~4 사분면(수학 표준: 1=+x+y, 2=-x+y, 3=-x-y, 4=+x-y), 데드존 이내면 0
    public int Quadrant
    {
        get
        {
            if (Magnitude < DeadZone) return 0;
            if (X >= 0f && Y >= 0f) return 1;
            if (X < 0f && Y >= 0f) return 2;
            if (X < 0f && Y < 0f) return 3;
            return 4;
        }
    }

    public static EmotionVector operator +(EmotionVector a, EmotionVector b)
        => new EmotionVector(a.X + b.X, a.Y + b.Y);

    public static EmotionVector operator *(EmotionVector v, float s)
        => new EmotionVector(v.X * s, v.Y * s);

    public override string ToString() => $"({X:F2}, {Y:F2})";
}
