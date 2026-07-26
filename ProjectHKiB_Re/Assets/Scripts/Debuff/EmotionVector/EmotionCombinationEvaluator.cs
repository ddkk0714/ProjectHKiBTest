using UnityEngine;

// 사분면 부호 판정으로 두 감정의 조합 결과(상쇄/대체/복합/중첩)를 결정하는 순수 함수 모음 (spec §4.1~4.3).
// Phase 4 Step 4.1(섀도 모드) 전용 — 여기서 나온 결과는 로그로만 쓰고 실제 스택/버프에 적용하지 않는다.
// 기존 EmotionModule.EvaluateReaction()은 이 클래스와 완전히 무관하게 그대로 병행 가동된다.
public enum EmotionCombinationType
{
    Overlap,   // 두 색 다 같은 부호(같은 사분면) — 단순 합산, 특별 처리 없음
    Cancel,    // 직선 대립(한 축만 반대 부호) + |s차| < 대체임계 — 양쪽 min(stack)만큼 소각
    Replace,   // 직선 대립 + |s차| >= 대체임계 — s가 큰 쪽 잔존, 반대쪽 min(stack)만큼 소각
    Composite, // 대각선 대립(두 축 다 반대 부호) — 합벡터가 속한 사분면에 복합 감정 생성
}

public readonly struct EmotionCombinationResult
{
    public readonly EmotionCombinationType Type;
    public readonly EmotionColor Winner;        // Replace 전용 — 잔존하는 색
    public readonly int CompositeQuadrant;      // Composite 전용 — 합벡터가 속한 사분면(1~4)
    public readonly int ConsumedStack;          // Cancel/Replace/Composite에서 소각되는 스택 (min(stackA, stackB) 기준)

    private EmotionCombinationResult(EmotionCombinationType type, EmotionColor winner, int compositeQuadrant, int consumedStack)
    {
        Type = type;
        Winner = winner;
        CompositeQuadrant = compositeQuadrant;
        ConsumedStack = consumedStack;
    }

    public static EmotionCombinationResult Overlap() => new(EmotionCombinationType.Overlap, default, 0, 0);
    public static EmotionCombinationResult Cancel(int consumedStack) => new(EmotionCombinationType.Cancel, default, 0, consumedStack);
    public static EmotionCombinationResult Replace(EmotionColor winner, int consumedStack) => new(EmotionCombinationType.Replace, winner, 0, consumedStack);
    public static EmotionCombinationResult Composite(int quadrant, int consumedStack) => new(EmotionCombinationType.Composite, default, quadrant, consumedStack);

    public override string ToString() => Type switch
    {
        EmotionCombinationType.Replace => $"Replace(winner={Winner})",
        EmotionCombinationType.Composite => $"Composite(quadrant={CompositeQuadrant})",
        EmotionCombinationType.Cancel => $"Cancel(consumed={ConsumedStack})",
        _ => "Overlap",
    };
}

public static class EmotionCombinationEvaluator
{
    // spec §4.4 규칙4 — 복합 활성 중 재료 재공급은 0.5배 효율로 스택만 증가(지속시간 갱신 없음)
    public const float ReplenishEfficiency = 0.5f;

    // s = (-x + y) / √2 — 좌상단(부정·각성) 방향 투영, 대체 판정의 우세도 지표 (spec §2.2, §4.2)
    public static float ComputeS(Vector2 position) => (-position.x + position.y) / Mathf.Sqrt(2f);

    // 복합 감정 생성 스택 = min(stackA, stackB) * 융합효율 (spec §4.3)
    public static int ComputeFusionStack(int consumedStack, float fusionEfficiency) =>
        Mathf.RoundToInt(consumedStack * fusionEfficiency);

    // 재료 재공급 시 늘어나는 스택 (spec §4.4)
    public static int ComputeReplenishStack(int incomingStack) =>
        Mathf.RoundToInt(incomingStack * ReplenishEfficiency);

    public static EmotionCombinationResult Evaluate(
        Vector2 posA, int stackA, EmotionColor colorA,
        Vector2 posB, int stackB, EmotionColor colorB,
        float replaceThreshold)
    {
        bool sameX = Mathf.Sign(posA.x) == Mathf.Sign(posB.x);
        bool sameY = Mathf.Sign(posA.y) == Mathf.Sign(posB.y);

        if (sameX && sameY)
            return EmotionCombinationResult.Overlap();

        int consumedStack = Mathf.Min(stackA, stackB);

        if (!sameX && !sameY)
        {
            Vector2 h = posA * stackA + posB * stackB;
            int quadrant = new EmotionVector(h.x, h.y).Quadrant;
            return EmotionCombinationResult.Composite(quadrant, consumedStack);
        }

        // 직선 대립 (한 축만 반대 부호) — 상쇄 vs 대체
        float sA = ComputeS(posA);
        float sB = ComputeS(posB);

        if (Mathf.Abs(sA - sB) < replaceThreshold)
            return EmotionCombinationResult.Cancel(consumedStack);

        EmotionColor winner = sA >= sB ? colorA : colorB;
        return EmotionCombinationResult.Replace(winner, consumedStack);
    }
}
