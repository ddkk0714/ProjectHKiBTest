using System;
using System.Collections.Generic;
using DG.Tweening;
using Movement;
using UnityEngine;

namespace StateMachine
{
    /// <summary>Prefab을 생성하고 ControlledTargetModule의 슬롯에 등록한다.</summary>
    [AddTypeMenu("Target Control/Instantiate And Register")]
    [Serializable]
    public sealed class InstanciateObjectAction : StateAction
    {
        [Tooltip("생성한 StateController를 등록할 슬롯 이름.")]
        [SerializeField] private string targetID = "Default";
        [SerializeField] private GameObject prefab;
        [SerializeField] private PositionReference position;

        [Tooltip("OwnerAnimationDirection은 소유자의 4방향, TowardDestination은 소유자의 CurrentTarget 방향을 사용한다.")]
        [SerializeField] private Combat.CombatAttackDirectionSource directionSource;

        [Tooltip("생성한 GameObject를 이 Action을 실행한 StateController의 자식으로 둔다.")]
        [SerializeField] private bool parentToOwner;

        [Tooltip("같은 슬롯이 이미 있으면 새 Instance로 교체한다.")]
        [SerializeField] private bool replaceExisting = true;

        [NaughtyAttributes.ShowIf(nameof(replaceExisting))]
        [NaughtyAttributes.AllowNesting]
        [Tooltip("교체되는 대상도 이 모듈이 생성했던 Instance라면 함께 파괴한다.")]
        [SerializeField] private bool destroyReplacedOwnedTarget = true;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out ITargetControl targets))
            {
                Debug.LogError("ERROR: InstanciateObjectAction - IControlledTargetModule을 찾을 수 없습니다. " +
                               "실행 주체에 ControlledTargetModule을 추가하세요.", stateController);
                return;
            }

            if (prefab == null)
            {
                Debug.LogError("ERROR: InstanciateObjectAction - Prefab이 비어 있습니다.", stateController);
                return;
            }

            if (!position.TryResolve(stateController, out Vector3 spawnPosition))
            {
                Debug.LogError($"ERROR: InstanciateObjectAction - '{targetID}'의 생성 위치를 계산할 수 없습니다.", stateController);
                return;
            }

            Quaternion rotation = ResolveRotation(stateController, spawnPosition);
            targets.InstantiateAndRegister(
                targetID,
                prefab,
                spawnPosition,
                rotation,
                parentToOwner ? stateController.transform : null,
                replaceExisting,
                destroyReplacedOwnedTarget);
        }

        private Quaternion ResolveRotation(StateController owner, Vector3 spawnPosition)
        {
            EnumManager.AnimDir direction;
            switch (directionSource)
            {
                case Combat.CombatAttackDirectionSource.OwnerAnimationDirection:
                    if (owner.TryGetInterface(out IDirAnimatable animatable))
                        return animatable.AnimationDirection.DirToQuaternion4();
                    direction = EnumManager.AnimDir.D;
                    break;

                case Combat.CombatAttackDirectionSource.TowardDestination:
                    if (owner.TryGetInterface(out ITargetable targetable) && targetable.CurrentTarget != null)
                        direction = DirectionFromVector(targetable.CurrentTarget.position - spawnPosition);
                    else
                        direction = EnumManager.AnimDir.D;
                    break;

                case Combat.CombatAttackDirectionSource.MovementDirection:
                    if (owner.TryGetInterface(out IPhysics physics))
                    {
                        Vector2 movement = physics.HVelocity;
                        if (movement.sqrMagnitude <= 0.000001f) movement = physics.WalkingDir;
                        if (movement.sqrMagnitude <= 0.000001f) movement = physics.LastSetDir;
                        direction = DirectionFromVector(movement);
                    }
                    else
                        direction = EnumManager.AnimDir.D;
                    break;

                case Combat.CombatAttackDirectionSource.Left:
                    direction = EnumManager.AnimDir.L;
                    break;
                case Combat.CombatAttackDirectionSource.Right:
                    direction = EnumManager.AnimDir.R;
                    break;
                case Combat.CombatAttackDirectionSource.Up:
                    direction = EnumManager.AnimDir.U;
                    break;
                default:
                    direction = EnumManager.AnimDir.D;
                    break;
            }

            return direction.DirToQuaternion4();
        }

        private static EnumManager.AnimDir DirectionFromVector(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.000001f) return EnumManager.AnimDir.D;
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return direction.x < 0f ? EnumManager.AnimDir.L : EnumManager.AnimDir.R;
            return direction.y < 0f ? EnumManager.AnimDir.D : EnumManager.AnimDir.U;
        }
    }

    /// <summary>등록된 다른 StateController를 대상으로 중첩 StateAction을 실행한다.</summary>
    [AddTypeMenu("Target Control/Manipulate Registered Target")]
    [Serializable]
    public sealed class ManipulateRegisteredTargetAction : StateAction
    {
        [SerializeField] private string targetID = "Default";
        [SerializeReference, SubclassSelector] private StateAction targetAction;

        public override void Act(StateController stateController)
        {
            if (targetAction == null) return;
            if (!stateController.TryGetInterface(out ITargetControl targets))
            {
                Debug.LogError("ERROR: ManipulateRegisteredTargetAction - IControlledTargetModule을 찾을 수 없습니다.", stateController);
                return;
            }

            if (!targets.TryGetTarget(targetID, out StateController target))
            {
                Debug.LogError($"ERROR: ManipulateRegisteredTargetAction - '{targetID}'에 등록된 StateController가 없습니다.", stateController);
                return;
            }

            targetAction.Act(target);
        }
    }

    /// <summary>슬롯을 해제하고, 모듈이 생성한 Instance라면 함께 파괴한다.</summary>
    [AddTypeMenu("Target Control/Destroy Registered Target")]
    [Serializable]
    public sealed class DestroyRegisteredTargetAction : StateAction
    {
        [SerializeField] private string targetID = "Default";

        [Tooltip("Scene에서 직접 등록한 대상도 파괴한다. 끄면 Scene 대상은 슬롯에서만 해제한다.")]
        [SerializeField] private bool destroyRegisteredSceneObject;

        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out ITargetControl targets))
                targets.DestroyTarget(targetID, destroyRegisteredSceneObject);
        }
    }

    /// <summary>이 모듈이 생성한 모든 Target Instance를 파괴한다. Scene 등록 대상은 유지한다.</summary>
    [AddTypeMenu("Target Control/Destroy All Owned Targets")]
    [Serializable]
    public sealed class DestroyAllOwnedTargetsAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out ITargetControl targets))
                targets.DestroyAllOwnedTargets();
        }
    }

    [AddTypeMenu("General/Group Action")]
    [Serializable]
    public sealed class GroupAction : StateAction
    {
        [SerializeReference, SubclassSelector] private StateAction[] actions =
            Array.Empty<StateAction>();

        public override void Act(StateController stateController)
        {
            if (actions == null) return;

            for (int i = 0; i < actions.Length; i++)
                actions[i]?.Act(stateController);
        }
    }

    public enum SequenceStateExitBehaviour
    {
        Kill,
        CompleteThenKill
    }

    [AddTypeMenu("General/Sequence Action")]
    [Serializable]
    public sealed class SequenceAction : StateAction
    {
        [Serializable]
        public struct ActionTween
        {
            [SerializeReference, SubclassSelector]
            public StateAction action;

            [Min(0f)]
            public float delay;
        }

        private sealed class RunningSequence
        {
            public Sequence Sequence;
            public StateSO StartingState;
            public bool Infinite;
            public bool CompletingForStateExit;
            public StateController Owner;
            public Action<StateSO, StateSO> StateChangingHandler;
        }

        [Tooltip("각 항목의 Delay만큼 기다린 뒤 Action을 실행한다. Action이 비어 있으면 대기 구간으로만 사용한다.")]
        [SerializeField] private ActionTween[] actions = Array.Empty<ActionTween>();

        [Tooltip("1은 한 번 실행, 2 이상은 지정 횟수만큼 반복, -1은 무한 반복. 0은 안전하게 1로 처리한다.")]
        [SerializeField] private int loops = 1;

        [Tooltip("이 Sequence를 시작한 State에서 벗어날 때 남은 Action을 버릴지, 즉시 모두 실행하고 종료할지 선택한다. 무한 반복은 항상 Kill한다.")]
        [SerializeField] private SequenceStateExitBehaviour stateExitBehaviour =
            SequenceStateExitBehaviour.Kill;

        [NonSerialized] private Dictionary<int, RunningSequence> _runningSequences;

        public override void Act(StateController stateController)
        {
            if (stateController == null) return;

            _runningSequences ??= new Dictionary<int, RunningSequence>();
            int ownerId = stateController.GetInstanceID();

            // 같은 SequenceAction이 Update나 반복 ActionSequence에서 다시 실행되어도
            // 이전 예약과 새 예약이 동시에 콜백을 호출하지 않게 한다.
            Stop(ownerId, false, stateController);

            if (actions == null || actions.Length == 0) return;

            Sequence sequence = DOTween.Sequence();
            RunningSequence running = new()
            {
                Sequence = sequence,
                StartingState = stateController.CurrentState,
                Owner = stateController
            };
            bool hasTimeline = false;

            for (int i = 0; i < actions.Length; i++)
            {
                ActionTween item = actions[i];
                float delay = Mathf.Max(0f, item.delay);
                if (delay > 0f)
                {
                    sequence.AppendInterval(delay);
                    hasTimeline = true;
                }

                // null Action도 순수 대기 항목으로 사용할 수 있다.
                if (item.action == null) continue;

                StateAction callbackAction = item.action;
                sequence.AppendCallback(() =>
                {
                    // 루트 Sequence의 OnUpdate보다 자식 Callback이 먼저 호출될 수 있다.
                    // Callback마다 검사해야 같은 프레임에 State가 바뀐 뒤의 Action이 새 State에서
                    // 잘못 실행되는 것을 막을 수 있다.
                    if (!running.CompletingForStateExit &&
                        !CanExecuteCallback(ownerId, stateController, running))
                        return;

                    callbackAction.Act(stateController);
                });
                hasTimeline = true;
            }

            if (!hasTimeline)
            {
                sequence.Kill(false);
                return;
            }

            int safeLoops = loops == -1 ? -1 : Mathf.Max(1, loops);
            sequence.SetLoops(safeLoops, LoopType.Restart);

            running.Infinite = safeLoops == -1;
            running.StateChangingHandler = (_, _) =>
            {
                bool complete = stateExitBehaviour == SequenceStateExitBehaviour.CompleteThenKill;
                Stop(ownerId, complete, stateController);
            };
            stateController.StateChanging += running.StateChangingHandler;
            _runningSequences[ownerId] = running;

            sequence.OnUpdate(() =>
            {
                if (stateController == null || !stateController.isActiveAndEnabled)
                {
                    Stop(ownerId, false, stateController);
                    return;
                }

                if (stateController.CurrentState == running.StartingState) return;

                bool complete = stateExitBehaviour == SequenceStateExitBehaviour.CompleteThenKill;
                Stop(ownerId, complete, stateController);
            });

            sequence.OnComplete(() => RemoveIfCurrent(ownerId, running));
            sequence.OnKill(() => RemoveIfCurrent(ownerId, running));
            sequence.Play();
        }

        private bool CanExecuteCallback(
            int ownerId,
            StateController stateController,
            RunningSequence running)
        {
            if (stateController == null || !stateController.isActiveAndEnabled)
            {
                Stop(ownerId, false, stateController);
                return false;
            }

            if (stateController.CurrentState == running.StartingState) return true;

            bool complete = stateExitBehaviour == SequenceStateExitBehaviour.CompleteThenKill;
            Stop(ownerId, complete, stateController);
            return false;
        }

        private void Stop(int ownerId, bool complete, StateController logContext)
        {
            if (_runningSequences == null ||
                !_runningSequences.TryGetValue(ownerId, out RunningSequence running))
                return;

            _runningSequences.Remove(ownerId);
            DetachStateChanging(running);
            if (running.Sequence == null || !running.Sequence.IsActive()) return;

            if (complete && running.Infinite)
            {
                Debug.LogWarning(
                    "[SequenceAction] 무한 반복 Sequence는 Complete할 수 없어 State 이탈 시 Kill합니다.",
                    logContext);
                complete = false;
            }

            // Kill(true)는 남은 콜백을 완료한 뒤 Sequence를 제거하고,
            // Kill(false)는 남은 콜백을 실행하지 않고 즉시 제거한다.
            // CompletingForStateExit가 true인 동안은 남은 콜백의 State 검사를 통과시킨다.
            running.CompletingForStateExit = complete;
            running.Sequence.Kill(complete);
        }

        private void RemoveIfCurrent(int ownerId, RunningSequence running)
        {
            if (_runningSequences != null &&
                _runningSequences.TryGetValue(ownerId, out RunningSequence current) &&
                ReferenceEquals(current, running))
            {
                _runningSequences.Remove(ownerId);
                DetachStateChanging(running);
            }
        }

        private static void DetachStateChanging(RunningSequence running)
        {
            if (running.Owner != null && running.StateChangingHandler != null)
                running.Owner.StateChanging -= running.StateChangingHandler;

            running.StateChangingHandler = null;
        }
    }
}
