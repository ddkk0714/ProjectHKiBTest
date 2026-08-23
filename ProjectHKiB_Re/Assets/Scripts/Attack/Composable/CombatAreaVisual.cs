using UnityEngine;

namespace Combat
{
    /// <summary>선택 사항인 표시 프리팹 어댑터. 프리팹의 Frame/Fill SpriteRenderer를 공격 크기에 맞춘다.</summary>
    public sealed class CombatAreaVisual : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer frame;
        [SerializeField] private SpriteRenderer fill;

        public void Apply(CombatArea area)
        {
            Vector2 size = area.Shape == CombatAreaShape.Circle
                ? Vector2.one * area.Radius * 2f
                : area.Size;

            transform.localPosition = area.LocalOffset;
            transform.localRotation = Quaternion.Euler(0f, 0f, area.LocalAngle);
            if (frame != null) frame.size = size;
            if (fill != null) fill.size = size;
        }

        public void SetProgress(float progress)
        {
            if (fill == null) return;
            fill.transform.localScale = Vector3.one * Mathf.Clamp01(progress);
        }
    }
}
