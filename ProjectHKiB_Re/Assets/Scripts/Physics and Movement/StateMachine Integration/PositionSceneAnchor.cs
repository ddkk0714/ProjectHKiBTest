using System;
using System.Collections.Generic;
using UnityEngine;

namespace Movement
{
    /// <summary>
    /// Scene의 움직이는 오브젝트를 문자열 ID로 StateMachine asset에 노출한다.
    /// Transform은 캐시하지 않고 실제 Scene Transform을 반환하므로 애니메이션/Timeline/수동 이동도 반영된다.
    /// </summary>
    [AddComponentMenu("Movement/Position Scene Anchor")]
    public sealed class PositionSceneAnchor : MonoBehaviour
    {
        [SerializeField] private string anchorId;

        private static readonly Dictionary<string, PositionSceneAnchor> Anchors =
            new(StringComparer.Ordinal);

        public string AnchorId => anchorId;

        private void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(anchorId))
            {
                Debug.LogError($"{name}: PositionSceneAnchor ID is empty.", this);
                return;
            }

            if (Anchors.TryGetValue(anchorId, out PositionSceneAnchor existing) && existing != this)
                Debug.LogWarning($"PositionSceneAnchor '{anchorId}' was replaced by {name}.", this);

            Anchors[anchorId] = this;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(anchorId) &&
                Anchors.TryGetValue(anchorId, out PositionSceneAnchor current) && current == this)
                Anchors.Remove(anchorId);
        }

        public static Transform Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Anchors.TryGetValue(id, out PositionSceneAnchor anchor) && anchor != null
                ? anchor.transform
                : null;
        }
    }
}
