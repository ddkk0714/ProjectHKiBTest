using UnityEngine;

// 감정 조합/촉매 관련 튜닝 상수 모음 (spec §2.4, §6). Phase 2에서는 공허 촉매 파라미터만 사용하고,
// Phase 4에서 데드존/대체임계/융합효율/k_atk/k_spd 등을 추가로 채운다.
[CreateAssetMenu(menuName = "Emotion/Combination Rule")]
public class EmotionCombinationRuleSO : ScriptableObject
{
    [Header("공허 촉매 (spec §2.4)")]
    [SerializeField] private float voidSaturation = 120f;
    [SerializeField] [Range(0f, 1f)] private float maxReduction = 0.7f;
    [SerializeField] [Range(0f, 1f)] private float minThresholdRatio = 0.3f;

    public float VoidSaturation => voidSaturation;
    public float MaxReduction => maxReduction;
    public float MinThresholdRatio => minThresholdRatio;

    // 0~maxReduction — 게이지 바 등 표시용
    public float GetCatalystRatio(int voidStack)
    {
        return Mathf.Min(1f, voidStack / voidSaturation) * maxReduction;
    }

    // T_실효 = max(T_기본 * (1 - catalystRatio), T_기본 * minThresholdRatio)
    public float ApplyCatalyst(float baseThreshold, int voidStack)
    {
        if (float.IsPositiveInfinity(baseThreshold)) return baseThreshold;

        float catalystRatio = GetCatalystRatio(voidStack);
        float reduced = baseThreshold * (1f - catalystRatio);
        float floor = baseThreshold * minThresholdRatio;
        return Mathf.Max(reduced, floor);
    }
}
