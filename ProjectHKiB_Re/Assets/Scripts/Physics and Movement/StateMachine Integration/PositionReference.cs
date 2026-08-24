using System;
using UnityEngine;

namespace Movement
{
    public enum PositionSource
    {
        Self,
        Player,
        CurrentTarget,
        World,
        SceneAnchor
    }

    public enum PositionOffsetSpace
    {
        World,
        SourceLocal
    }

    /// <summary>
    /// StateMachine asset이 Scene 오브젝트를 직접 참조하지 않아도 목표 위치를 표현한다.
    /// follow가 꺼져 있으면 사용하는 시스템이 시작 위치를 캡처할 수 있고,
    /// 켜져 있으면 매 프레임 실제 Transform을 다시 읽을 수 있다.
    /// </summary>
    [Serializable]
    public struct PositionReference
    {
        [Tooltip("위치를 가져올 기준을 선택한다.")]
        [SerializeField] private PositionSource source;

        [NaughtyAttributes.HideIf(nameof(source), PositionSource.World)]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("시작 위치를 캡처하지 않고 Source의 현재 Transform을 계속 읽는다.")]
        [SerializeField] private bool follow;

        [NaughtyAttributes.ShowIf(nameof(source), PositionSource.World)]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("Source가 World일 때 사용할 고정 월드 좌표.")]
        [SerializeField] private Vector3 worldPosition;

        [NaughtyAttributes.ShowIf(nameof(source), PositionSource.SceneAnchor)]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("Scene의 PositionSceneAnchor에 등록된 고유 ID.")]
        [SerializeField] private string sceneAnchorId;

        [Tooltip("선택한 기준 위치에 더할 추가 위치.")]
        [SerializeField] private Vector3 offset;

        [NaughtyAttributes.HideIf(nameof(source), PositionSource.World)]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("Offset을 월드 축 또는 Source의 로컬 축 중 어느 기준으로 적용할지 선택한다.")]
        [SerializeField] private PositionOffsetSpace offsetSpace;

        [Tooltip("Source와 Offset을 계산한 최종 XY 위치를 가장 가까운 그리드 교점에 맞춘다.")]
        [SerializeField] private bool snapFinalPositionToGrid;

        [NaughtyAttributes.ShowIf(nameof(snapFinalPositionToGrid))]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("월드 좌표 기준 정사각형 그리드 한 칸의 크기. 0 이하는 1로 처리한다.")]
        [SerializeField] private float gridCellSize;

        [NaughtyAttributes.ShowIf(nameof(snapFinalPositionToGrid))]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("그리드 (0, 0) 교점이 놓일 월드 XY 좌표.")]
        [SerializeField] private Vector2 gridOrigin;

        public readonly PositionSource Source => source;
        public readonly bool Follow => follow;
        public readonly string SceneAnchorId => sceneAnchorId;
        public readonly bool SnapFinalPositionToGrid => snapFinalPositionToGrid;

        public static PositionReference Self(bool follow = true)
        {
            return new PositionReference
            {
                source = PositionSource.Self,
                follow = follow,
                offsetSpace = PositionOffsetSpace.SourceLocal,
                gridCellSize = 1f
            };
        }

        public readonly Transform ResolveTransform(StateController owner)
        {
            switch (source)
            {
                case PositionSource.Self:
                    return owner != null ? owner.transform : null;
                case PositionSource.Player:
                    return GameManager.instance != null && GameManager.instance.player != null
                        ? GameManager.instance.player.transform
                        : null;
                case PositionSource.CurrentTarget:
                    if (owner != null && owner.TryGetInterface(out ITargetable targetable))
                        return targetable.CurrentTarget;
                    return null;
                case PositionSource.SceneAnchor:
                    return PositionSceneAnchor.Find(sceneAnchorId);
                default:
                    return null;
            }
        }

        public readonly bool TryResolve(StateController owner, out Vector3 position)
        {
            if (source == PositionSource.World)
            {
                position = worldPosition + offset;
                position = SnapToGrid(position);
                return true;
            }

            Transform sourceTransform = ResolveTransform(owner);
            if (sourceTransform == null)
            {
                position = default;
                return false;
            }

            position = sourceTransform.position;
            position += offsetSpace == PositionOffsetSpace.SourceLocal
                ? sourceTransform.TransformVector(offset)
                : offset;
            position = SnapToGrid(position);
            return true;
        }

        private readonly Vector3 SnapToGrid(Vector3 position)
        {
            if (!snapFinalPositionToGrid) return position;

            // 기존 에셋에는 새 필드가 0으로 역직렬화될 수 있으므로 1을 안전한 기본값으로 쓴다.
            float cellSize = gridCellSize > 0f ? gridCellSize : 1f;
            position.x = gridOrigin.x + Mathf.Round((position.x - gridOrigin.x) / cellSize) * cellSize;
            position.y = gridOrigin.y + Mathf.Round((position.y - gridOrigin.y) / cellSize) * cellSize;
            return position;
        }
    }

    public sealed class RuntimePositionReference
    {
        private readonly PositionReference _reference;
        private readonly StateController _owner;
        private Vector3 _capturedPosition;
        private bool _hasCapturedPosition;

        public RuntimePositionReference(PositionReference reference, StateController owner)
        {
            _reference = reference;
            _owner = owner;
            if (!_reference.Follow)
                _hasCapturedPosition = _reference.TryResolve(_owner, out _capturedPosition);
        }

        public bool TryGetPosition(out Vector3 position)
        {
            if (!_reference.Follow)
            {
                position = _capturedPosition;
                return _hasCapturedPosition;
            }

            return _reference.TryResolve(_owner, out position);
        }

        public Transform CurrentTransform => _reference.ResolveTransform(_owner);
    }
}
