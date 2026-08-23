using System;
using UnityEngine;

namespace Combat
{
    public enum CombatPositionSource
    {
        Self,
        Player,
        CurrentTarget,
        World,
        SceneAnchor
    }

    public enum CombatOffsetSpace
    {
        World,
        SourceLocal
    }

    /// <summary>
    /// StateMachine asset이 Scene 오브젝트를 직접 참조하지 않아도 공격 위치를 표현한다.
    /// follow가 꺼져 있으면 공격 시작 시점의 위치를 캡처하고, 켜져 있으면 매 프레임 Transform을 다시 읽는다.
    /// </summary>
    [Serializable]
    public struct CombatPositionReference
    {
        [SerializeField] private CombatPositionSource source;
        [SerializeField] private bool follow;
        [SerializeField] private Vector3 worldPosition;
        [SerializeField] private string sceneAnchorId;
        [SerializeField] private Vector3 offset;
        [SerializeField] private CombatOffsetSpace offsetSpace;

        public CombatPositionSource Source => source;
        public bool Follow => follow;
        public string SceneAnchorId => sceneAnchorId;

        public static CombatPositionReference Self(bool follow = true)
        {
            return new CombatPositionReference
            {
                source = CombatPositionSource.Self,
                follow = follow,
                offsetSpace = CombatOffsetSpace.SourceLocal
            };
        }

        public Transform ResolveTransform(StateController owner)
        {
            switch (source)
            {
                case CombatPositionSource.Self:
                    return owner != null ? owner.transform : null;
                case CombatPositionSource.Player:
                    return GameManager.instance != null && GameManager.instance.player != null
                        ? GameManager.instance.player.transform
                        : null;
                case CombatPositionSource.CurrentTarget:
                    if (owner != null && owner.TryGetInterface(out ITargetable targetable))
                        return targetable.CurrentTarget;
                    return null;
                case CombatPositionSource.SceneAnchor:
                    return CombatSceneAnchor.Find(sceneAnchorId);
                default:
                    return null;
            }
        }

        public bool TryResolve(StateController owner, out Vector3 position)
        {
            if (source == CombatPositionSource.World)
            {
                position = worldPosition + offset;
                return true;
            }

            Transform sourceTransform = ResolveTransform(owner);
            if (sourceTransform == null)
            {
                position = default;
                return false;
            }

            position = sourceTransform.position;
            position += offsetSpace == CombatOffsetSpace.SourceLocal
                ? sourceTransform.TransformVector(offset)
                : offset;
            return true;
        }
    }

    internal sealed class RuntimePositionReference
    {
        private readonly CombatPositionReference _reference;
        private readonly StateController _owner;
        private Vector3 _capturedPosition;
        private bool _hasCapturedPosition;

        public RuntimePositionReference(CombatPositionReference reference, StateController owner)
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
