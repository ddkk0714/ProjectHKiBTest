// 기획서 난이도 공식 (순수 계산 — 단서/클리어 여부는 호출 측에서 판단):
// Σ 소형 × (동일감정? 1.1 : 1) × (다른감정? 1 : 2)
// Σ 중형 × (동일감정? 5 : 10) × (다른감정? 1 : 2)
// Σ 대형 × (동일감정? 10 : 100) × (다른감정? 1 : 2)
// 동일/다른 감정 판단은 EmotionGroup 기준 — SadnessBlue·SadnessSky는 모두 Sadness로 동일 취급
// 장비 미입력 → 상성 미적용, 적 기본 수치(소1·중5·대10)만 합산
public static class DifficultyCalculator
{
    public static float Calculate(MapConnectionData connection, EmotionColor[] equippedGears)
    {
        bool hasGear = equippedGears != null && equippedGears.Length > 0;
        float total = 0f;

        foreach (var group in connection.enemyGroups)
        {
            // 장비 미입력 시 hasSame=false 결과(소1·중5·대10)가 그대로 기본 수치가 된다.
            bool hasSame  = hasGear && HasSameEmotion(equippedGears, group.emotionType);
            bool hasOther = hasGear && HasOtherEmotion(equippedGears, group.emotionType);

            float sameMult = group.scale switch
            {
                EnemyScale.Small  => hasGear ? (hasSame ? 1.1f : 1f)   : 1f,
                EnemyScale.Medium => hasGear ? (hasSame ? 5f   : 10f)  : 5f,
                EnemyScale.Large  => hasGear ? (hasSame ? 10f  : 100f) : 10f,
                _                 => 1f
            };
            float otherMult = hasGear ? (hasOther ? 1f : 2f) : 1f;

            total += group.count * sameMult * otherMult;
        }

        return total;
    }

    private static bool HasSameEmotion(EmotionColor[] gears, EmotionColor target)
    {
        var targetGroup = EmotionColorConfig.ToGroup(target);
        foreach (var g in gears)
            if (EmotionColorConfig.ToGroup(g) == targetGroup) return true;
        return false;
    }

    private static bool HasOtherEmotion(EmotionColor[] gears, EmotionColor target)
    {
        var targetGroup = EmotionColorConfig.ToGroup(target);
        foreach (var g in gears)
            if (EmotionColorConfig.ToGroup(g) != targetGroup) return true;
        return false;
    }
}
