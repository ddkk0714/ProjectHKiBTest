using EntityControl;
using Combat;
using UnityEngine;

namespace StateMachine
{
    [System.Serializable]
    public sealed class StartComposableAttackAction : StateAction
    {
        [SerializeField] private CombatAttackDefinitionSO definition;
        [SerializeField] private string slot = "Default";
        [SerializeField] private bool cancelExistingInSlot;
        [SerializeField] private CombatPositionReference origin;
        [SerializeField] private CombatPositionReference destination;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out ICombatAttackModule attacks))
            {
                Debug.LogError("ERROR: ICombatAttackModule interface not found.", stateController);
                return;
            }

            if (cancelExistingInSlot) attacks.CancelSlot(slot);
            attacks.StartAttack(definition, origin, destination, slot);
        }
    }

    [System.Serializable]
    public sealed class RetargetComposableAttackAction : StateAction
    {
        [SerializeField] private string slot = "Default";
        [SerializeField] private CombatPositionReference destination;

        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out ICombatAttackModule attacks))
                attacks.RetargetLatest(slot, destination);
        }
    }

    [System.Serializable]
    public sealed class CancelComposableAttackAction : StateAction
    {
        [SerializeField] private string slot = "Default";
        [SerializeField] private bool cancelAllInSlot = true;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out ICombatAttackModule attacks)) return;
            if (cancelAllInSlot) attacks.CancelSlot(slot);
            else attacks.CancelLatest(slot);
        }
    }

    [System.Serializable]
    public sealed class CancelAllComposableAttacksAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out ICombatAttackModule attacks))
                attacks.CancelAll();
        }
    }

    public enum CombatOwnerMovementBackend
    {
        PhysicsStep,
        Navigation
    }

    /// <summary>
    /// 공격 실행과 별개로 공격자만 이동시킨다. UpdateActions에 놓으면 Player/Anchor처럼 움직이는 목표도 추적한다.
    /// PhysicsStep은 IPhysics, Navigation은 INavigationAgent를 사용한다.
    /// </summary>
    [System.Serializable]
    public sealed class MoveCombatOwnerAction : StateAction
    {
        [SerializeField] private CombatOwnerMovementBackend backend;
        [SerializeField] private CombatPositionReference destination;
        [SerializeField, Min(0f)] private float speed = 1f;
        [SerializeField] private bool forceRepath;

        public override void Act(StateController stateController)
        {
            if (!destination.TryResolve(stateController, out Vector3 targetPosition)) return;

            if (backend == CombatOwnerMovementBackend.Navigation)
            {
                if (stateController.TryGetInterface(out INavigationAgent navigation))
                    navigation.SetDestination(targetPosition, forceRepath);
                return;
            }

            if (stateController.TryGetInterface(out IPhysics physics))
                physics.MoveToward(targetPosition, speed * Time.deltaTime);
        }
    }
}
