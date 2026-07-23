using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// 엔티티별 4축 역치 + 역치 도달 시 부여할 스탯/행동 정의 (spec §5.2).
[CreateAssetMenu(menuName = "Emotion/Threshold Profile")]
public class EmotionThresholdProfileSO : ScriptableObject
{
    [System.Serializable]
    public struct ThresholdEntry
    {
        public EmotionAxis axis;
        [Range(0f, 200f)] public float value;
        [Range(0f, 50f)] public float hysteresis; // Step 2.2에서 사용
        public StatBuffSO statBuff;
        public BehaviorOverrideSO behavior; // Phase 3(Step 3.1) 전까지는 null
        public bool locked; // Step 2.2에서 사용 — true면 해제 불가 (잠·황홀 후기)
    }

    [ReorderableList]
    [SerializeField] private List<ThresholdEntry> thresholds = new();

    public IReadOnlyList<ThresholdEntry> Thresholds => thresholds;

    private Dictionary<EmotionAxis, ThresholdEntry> _lookup;
    private readonly HashSet<EmotionAxis> _warned = new();

    private void OnValidate() => _lookup = null;

    private void BuildLookupIfNeeded()
    {
        if (_lookup != null) return;

        _lookup = new Dictionary<EmotionAxis, ThresholdEntry>();
        for (int i = 0; i < thresholds.Count; i++)
            _lookup[thresholds[i].axis] = thresholds[i];
    }

    public bool TryGetEntry(EmotionAxis axis, out ThresholdEntry entry)
    {
        BuildLookupIfNeeded();
        return _lookup.TryGetValue(axis, out entry);
    }

    // 미설정 축은 PositiveInfinity — "역치가 낮다"가 아니라 "이 축은 아예 발동 안 함"이 안전한 기본값이다.
    public float GetBaseThreshold(EmotionAxis axis)
    {
        BuildLookupIfNeeded();

        if (_lookup.TryGetValue(axis, out ThresholdEntry entry))
            return entry.value;

        if (_warned.Add(axis))
            Debug.LogWarning($"[EmotionThresholdProfileSO] {name}: 미설정 축 조회: {axis} → 발동 안 함(PositiveInfinity) 처리");

        return float.PositiveInfinity;
    }

    // T = baseThreshold * (mental / 100) (spec §5.3)
    public float GetEffectiveThreshold(EmotionAxis axis, float mental)
    {
        float baseThreshold = GetBaseThreshold(axis);
        if (float.IsPositiveInfinity(baseThreshold)) return baseThreshold;
        return baseThreshold * (mental / 100f);
    }
}
