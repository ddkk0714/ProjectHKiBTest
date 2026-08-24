using UnityEngine;

namespace Combat
{
    /// <summary>선택 사항인 표시 프리팹 어댑터. 프리팹의 Frame/Fill SpriteRenderer를 공격 크기에 맞춘다.</summary>
    public sealed class CombatAreaVisual : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer frame;
        [SerializeField] private SpriteRenderer fill;

        public void Apply(BoxData downwardArea, EnumManager.AnimDir direction, Transform attackRoot)
        {
            if (downwardArea == null || attackRoot == null) return;

            Quaternion directionRotation = direction.DirToQuaternion4();
            transform.SetPositionAndRotation(
                attackRoot.position + directionRotation * (Vector3)downwardArea.offset,
                directionRotation);
            if (frame != null) frame.size = downwardArea.size;
            if (fill != null) fill.size = downwardArea.size;
        }

        public void SetProgress(float progress)
        {
            if (fill == null) return;
            fill.transform.localScale = Vector3.one * Mathf.Clamp01(progress);
        }
    }
}
