using System;
using System.Collections.Generic;
using EntityControl;
using Movement;
using UnityEngine;
using UnityEngine.Serialization;

namespace StateMachine
{
    /// <summary>
    /// 위치 기반 이동을 어떤 이동 모듈에 위임할지 정한다.
    /// </summary>
    public enum PositionMovementMode
    {
        InterpolatedPhysicsStep,
        InstantTeleport,
        Navigation
    }

    internal sealed class TimedPositionMovementSegment
    {
        public RuntimePositionReference Destination;
        public float Elapsed;
        public float ArrivalTime;
        public bool TimingInitialized;
        public bool NavigationIssued;
    }

    internal static class PositionMovementExecutor
    {
        public static bool TryBeginInterpolated(
            StateController owner,
            PositionReference destination,
            out TimedPositionMovementSegment segment)
        {
            segment = null;
            if (!owner.TryGetInterface(out IPhysics physics)) return false;

            RuntimePositionReference runtimeDestination = new(destination, owner);
            if (!runtimeDestination.TryGetPosition(out _)) return false;

            segment = new TimedPositionMovementSegment
            {
                Destination = runtimeDestination
            };
            return true;
        }

        public static bool TryBeginNavigation(
            StateController owner,
            PositionReference destination,
            out TimedPositionMovementSegment segment)
        {
            segment = null;
            RuntimePositionReference runtimeDestination = new(destination, owner);
            if (!runtimeDestination.TryGetPosition(out _)) return false;

            segment = new TimedPositionMovementSegment { Destination = runtimeDestination };
            return true;
        }

        public static bool TickInterpolated(
            StateController owner,
            TimedPositionMovementSegment segment,
            float duration)
        {
            if (!owner.TryGetInterface(out IPhysics physics) ||
                !segment.Destination.TryGetPosition(out Vector3 targetPosition))
                return false;

            if (!segment.TimingInitialized)
            {
                segment.ArrivalTime = Time.time + duration;
                segment.TimingInitialized = true;
            }

            segment.Elapsed = Mathf.Min(segment.Elapsed + Time.deltaTime, duration);
            physics.MoveTowardByPhysics(targetPosition, segment.ArrivalTime);
            return Time.time >= segment.ArrivalTime;
        }

        public static bool TickNavigation(
            StateController owner,
            TimedPositionMovementSegment segment,
            float duration,
            bool forceRepath)
        {
            if (!owner.TryGetInterface(out INavigationAgent navigation) ||
                !segment.Destination.TryGetPosition(out Vector3 targetPosition))
                return false;

            navigation.SetDestination(targetPosition, forceRepath && !segment.NavigationIssued);
            segment.NavigationIssued = true;
            segment.Elapsed = Mathf.Min(segment.Elapsed + Time.deltaTime, duration);
            return duration <= 0f || segment.Elapsed >= duration;
        }

        public static bool Teleport(
            StateController owner,
            PositionReference destination,
            bool stopHorizontalMovement)
        {
            if (!owner.TryGetInterface(out IPhysics physics) ||
                !destination.TryResolve(owner, out Vector3 targetPosition))
                return false;

            if (stopHorizontalMovement)
                physics.StopMove();

            physics.RealTeleport(targetPosition);
            return true;
        }
    }

    /// <summary>
    /// 복합 경로의 한 구간. InterpolatedPhysicsStep은 duration 동안 다음 위치로 이동하고,
    /// Navigation은 duration 동안 해당 목적지를 추적하며, InstantTeleport는 즉시 다음 구간으로 넘어간다.
    /// </summary>
    [Serializable]
    public sealed class PositionMovementKeyframe
    {
        [SerializeField] private PositionMovementMode mode;
        [SerializeField] private PositionReference destination;

        [NaughtyAttributes.ShowIf(nameof(UsesDuration))]
        [NaughtyAttributes.AllowNesting]
        [NaughtyAttributes.MinValue(0.01f)]
        [Tooltip("이 키프레임 구간에 사용할 시간(초).")]
        [SerializeField] private float duration = 1f;

        [NaughtyAttributes.ShowIf(nameof(mode), PositionMovementMode.Navigation)]
        [NaughtyAttributes.AllowNesting]
        [SerializeField] private bool forceRepath;

        [NaughtyAttributes.ShowIf(nameof(mode), PositionMovementMode.InstantTeleport)]
        [NaughtyAttributes.AllowNesting]
        [SerializeField] private bool stopHorizontalMovementBeforeTeleport = true;

        internal PositionMovementMode Mode => mode;
        internal PositionReference Destination => destination;
        internal float Duration => duration;
        internal bool ForceRepath => forceRepath;
        internal bool StopHorizontalMovementBeforeTeleport => stopHorizontalMovementBeforeTeleport;

        private bool UsesDuration => mode != PositionMovementMode.InstantTeleport;
    }

    /// <summary>
    /// 공격 실행과 무관하게 StateController의 소유자를 지정 위치로 이동시킨다.
    /// UpdateActions에 두면 Player, CurrentTarget, SceneAnchor처럼 Scene에서 움직이는 목적지를 계속 다시 읽는다.
    /// </summary>
    [AddTypeMenu("Movement/Move To Position")]
    [System.Serializable]
    public sealed class MoveToPositionAction : StateAction
    {
        private sealed class RuntimeState
        {
            public StateSO OwnerState;
            public int LastFrame;
            public bool Completed;
            public TimedPositionMovementSegment Segment;
        }

        [Tooltip("이동을 처리할 모듈과 이동 방식을 선택한다.")]
        [SerializeField] private PositionMovementMode mode;

        [Tooltip("Self, Player, CurrentTarget, World 또는 SceneAnchor 기반 목적지.")]
        [SerializeField] private PositionReference destination;

        [NaughtyAttributes.ShowIf(nameof(mode), PositionMovementMode.InterpolatedPhysicsStep)]
        [NaughtyAttributes.AllowNesting]
        [NaughtyAttributes.MinValue(0.01f)]
        [Tooltip("현재 위치에서 목적지까지 이동하는 데 사용할 시간(초).")]
        [FormerlySerializedAs("speed")]
        [SerializeField] private float duration = 1f;

        [NaughtyAttributes.ShowIf(nameof(mode), PositionMovementMode.Navigation)]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("진행 중인 경로 요청을 무효화하고 즉시 새 경로를 요청한다.")]
        [SerializeField] private bool forceRepath;

        [NaughtyAttributes.ShowIf(nameof(mode), PositionMovementMode.InstantTeleport)]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("순간이동 전에 걷기 상태와 수평 속도를 정지한다.")]
        [SerializeField] private bool stopHorizontalMovementBeforeTeleport = true;

        [NonSerialized] private Dictionary<int, RuntimeState> _runtimeStates;

        public override void Act(StateController stateController)
        {
            switch (mode)
            {
                case PositionMovementMode.Navigation:
                    Navigate(stateController);
                    break;
                case PositionMovementMode.InstantTeleport:
                    PositionMovementExecutor.Teleport(
                        stateController,
                        destination,
                        stopHorizontalMovementBeforeTeleport);
                    break;
                default:
                    MoveInterpolated(stateController);
                    break;
            }
        }

        private void MoveInterpolated(StateController stateController)
        {
            _runtimeStates ??= new Dictionary<int, RuntimeState>();
            int ownerId = stateController.GetInstanceID();
            int currentFrame = Time.frameCount;

            if (!_runtimeStates.TryGetValue(ownerId, out RuntimeState runtime) ||
                runtime.OwnerState != stateController.CurrentState ||
                currentFrame > runtime.LastFrame + 1)
            {
                runtime = new RuntimeState { OwnerState = stateController.CurrentState };
                _runtimeStates[ownerId] = runtime;
            }

            runtime.LastFrame = currentFrame;
            if (runtime.Completed) return;

            if (runtime.Segment == null &&
                !PositionMovementExecutor.TryBeginInterpolated(
                    stateController,
                    destination,
                    out runtime.Segment))
                return;

            runtime.Completed = PositionMovementExecutor.TickInterpolated(
                stateController,
                runtime.Segment,
                Mathf.Max(0.01f, duration));
        }

        private void Navigate(StateController stateController)
        {
            if (destination.TryResolve(stateController, out Vector3 targetPosition) &&
                stateController.TryGetInterface(out INavigationAgent navigation))
                navigation.SetDestination(targetPosition, forceRepath);
        }
    }

    /// <summary>
    /// 여러 Move To Position 구간을 키프레임처럼 순서대로 실행한다.
    /// UpdateActions에 배치해야 duration 진행과 움직이는 목적지 추적이 계속 갱신된다.
    /// </summary>
    [AddTypeMenu("Movement/Move Along Position Keyframes")]
    [Serializable]
    public sealed class MoveAlongPositionKeyframesAction : StateAction
    {
        private sealed class RuntimeState
        {
            public StateSO OwnerState;
            public int LastFrame;
            public int KeyframeIndex;
            public bool Completed;
            public TimedPositionMovementSegment Segment;
        }

        [Tooltip("순서대로 실행할 위치 이동 구간.")]
        [SerializeField] private PositionMovementKeyframe[] keyframes = Array.Empty<PositionMovementKeyframe>();
        [SerializeField] private bool loop;

        [NonSerialized] private Dictionary<int, RuntimeState> _runtimeStates;

        public override void Act(StateController stateController)
        {
            if (keyframes == null || keyframes.Length == 0) return;

            RuntimeState runtime = GetRuntime(stateController);
            runtime.LastFrame = Time.frameCount;
            if (runtime.Completed) return;

            // null 또는 즉시 이동 키프레임은 같은 프레임에 넘긴다. loop가 전부 즉시 이동일 때의
            // 무한 반복을 막기 위해 한 번의 Act에서 배열 길이만큼만 처리한다.
            int remainingImmediateSteps = keyframes.Length;
            while (!runtime.Completed && remainingImmediateSteps-- > 0)
            {
                PositionMovementKeyframe keyframe = keyframes[runtime.KeyframeIndex];
                if (keyframe == null)
                {
                    Advance(runtime);
                    continue;
                }

                if (keyframe.Mode == PositionMovementMode.InstantTeleport)
                {
                    if (!PositionMovementExecutor.Teleport(
                            stateController,
                            keyframe.Destination,
                            keyframe.StopHorizontalMovementBeforeTeleport))
                        return;

                    Advance(runtime);
                    continue;
                }

                if (!EnsureSegment(stateController, runtime, keyframe)) return;

                float keyframeDuration = Mathf.Max(0.01f, keyframe.Duration);
                bool segmentCompleted = keyframe.Mode == PositionMovementMode.Navigation
                    ? PositionMovementExecutor.TickNavigation(
                        stateController,
                        runtime.Segment,
                        keyframeDuration,
                        keyframe.ForceRepath)
                    : PositionMovementExecutor.TickInterpolated(
                        stateController,
                        runtime.Segment,
                        keyframeDuration);

                if (segmentCompleted) Advance(runtime);
                return;
            }
        }

        private RuntimeState GetRuntime(StateController stateController)
        {
            _runtimeStates ??= new Dictionary<int, RuntimeState>();
            int ownerId = stateController.GetInstanceID();

            if (!_runtimeStates.TryGetValue(ownerId, out RuntimeState runtime) ||
                runtime.OwnerState != stateController.CurrentState ||
                Time.frameCount > runtime.LastFrame + 1 ||
                (!runtime.Completed && runtime.KeyframeIndex >= keyframes.Length))
            {
                runtime = new RuntimeState { OwnerState = stateController.CurrentState };
                _runtimeStates[ownerId] = runtime;
            }

            return runtime;
        }

        private static bool EnsureSegment(
            StateController stateController,
            RuntimeState runtime,
            PositionMovementKeyframe keyframe)
        {
            if (runtime.Segment != null) return true;

            return keyframe.Mode == PositionMovementMode.Navigation
                ? PositionMovementExecutor.TryBeginNavigation(
                    stateController,
                    keyframe.Destination,
                    out runtime.Segment)
                : PositionMovementExecutor.TryBeginInterpolated(
                    stateController,
                    keyframe.Destination,
                    out runtime.Segment);
        }

        private void Advance(RuntimeState runtime)
        {
            runtime.Segment = null;
            runtime.KeyframeIndex++;
            if (runtime.KeyframeIndex < keyframes.Length) return;

            if (loop)
                runtime.KeyframeIndex = 0;
            else
                runtime.Completed = true;
        }
    }
}
