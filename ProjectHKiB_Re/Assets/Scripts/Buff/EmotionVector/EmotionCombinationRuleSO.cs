using System.Collections.Generic;
using NaughtyAttributes;
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

    [Header("조합 판정 (spec §4.2, Phase 4)")]
    [SerializeField] private float replaceThreshold = 0.35f; // |s_a - s_b| 이 값 미만이면 상쇄, 이상이면 대체
    [SerializeField] [Range(0f, 1f)] private float fusionEfficiency = 1f; // 복합 감정 생성 시 min(stack)에 곱하는 효율 (Step 4.2에서 사용)

    [System.Serializable]
    public struct CompositeEntry
    {
        public int quadrant;               // 합벡터가 속하는 사분면 (2/3/4 — spec §4.3, 1사분면은 현재 구현색으로는 도달 불가)
        public EmotionColor result;        // 결과색 — 전부 이미 존재하는 EmotionColor를 재사용한다(신규 enum 값 추가 안 함, Step 4.2 방침)
        public List<EmotionColor> materials; // 재공급 판정용(spec §4.4) — 이 색들 중 하나가 재유입되면 활성 복합 스택에 0.5배로 합산
    }

    [Header("복합 감정 (spec §4.3, Step 4.2 — 이미 구현된 반응색 Longing/Collapse/Bluff 재사용)")]
    [ReorderableList]
    [SerializeField] private List<CompositeEntry> composites = new();

    public float VoidSaturation => voidSaturation;
    public float MaxReduction => maxReduction;
    public float MinThresholdRatio => minThresholdRatio;
    public float ReplaceThreshold => replaceThreshold;
    public float FusionEfficiency => fusionEfficiency;
    public IReadOnlyList<CompositeEntry> Composites => composites;

    // 합벡터 사분면 -> 복합 감정색 (spec §4.3). 1사분면 등 미등록 사분면은 false.
    public bool TryGetCompositeColor(int quadrant, out EmotionColor result)
    {
        for (int i = 0; i < composites.Count; i++)
        {
            if (composites[i].quadrant != quadrant) continue;
            result = composites[i].result;
            return true;
        }

        result = default;
        return false;
    }

    // 활성 복합(composite) 상태에서 incoming 색이 그 재료 중 하나인지 — 재공급 판정(spec §4.4)
    public bool IsMaterialOf(EmotionColor composite, EmotionColor incoming)
    {
        for (int i = 0; i < composites.Count; i++)
        {
            if (composites[i].result != composite) continue;
            return composites[i].materials != null && composites[i].materials.Contains(incoming);
        }

        return false;
    }

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
