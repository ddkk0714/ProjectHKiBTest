using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// EmotionColor -> 감정 평면(긍정도-각성도) 좌표 매핑 (spec §2.2, §2.3)
[CreateAssetMenu(menuName = "Emotion/Vector Table")]
public class EmotionVectorTableSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public EmotionColor color;
        public string displayName; // "공포", "증오" …
        public Vector2 position;   // 감정 평면 좌표 = 기본 벡터. 정규화하지 않는다
        public bool isCatalyst;    // VoidBlack 전용 (spec §2.4) — 평면에 배치되지 않는 역치 촉매
    }

    [ReorderableList]
    [SerializeField] private List<Entry> entries = new();

    public IReadOnlyList<Entry> Entries => entries; // 디버그 뷰 라벨 순회용 (Step 1.2)

    private Dictionary<EmotionColor, Entry> _lookup;
    private readonly HashSet<EmotionColor> _warned = new();

    private void OnValidate() => _lookup = null; // 인스펙터에서 편집 시 캐시 무효화

    private void BuildLookupIfNeeded()
    {
        if (_lookup != null) return;

        _lookup = new Dictionary<EmotionColor, Entry>();
        for (int i = 0; i < entries.Count; i++)
            _lookup[entries[i].color] = entries[i];
    }

    public Vector2 GetPosition(EmotionColor color)
    {
        BuildLookupIfNeeded();

        if (_lookup.TryGetValue(color, out Entry entry))
            return entry.position;

        if (_warned.Add(color))
            Debug.LogWarning($"[EmotionVectorTableSO] 미등록 EmotionColor 조회: {color} → (0,0) 반환");

        return Vector2.zero;
    }

    public bool IsCatalyst(EmotionColor color)
    {
        BuildLookupIfNeeded();
        return _lookup.TryGetValue(color, out Entry entry) && entry.isCatalyst;
    }

    public bool TryGetEntry(EmotionColor color, out Entry entry)
    {
        BuildLookupIfNeeded();
        return _lookup.TryGetValue(color, out entry);
    }
}
