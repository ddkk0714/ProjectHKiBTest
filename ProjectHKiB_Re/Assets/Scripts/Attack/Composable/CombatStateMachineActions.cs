using Combat;
using Movement;
using UnityEngine;

namespace StateMachine
{
    [System.Serializable]
    public sealed class StartComposableAttackAction : StateAction
    {
        [SerializeField] private CombatAttackDefinitionSO definition;
        [SerializeField] private string slot = "Default";
        [SerializeField] private bool cancelExistingInSlot;
        [SerializeField] private PositionReference origin;
        [SerializeField] private PositionReference destination;
        [SerializeField] private CombatAttackDirectionSource directionSource;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out ICombatAttackModule attacks))
            {
                Debug.LogError("ERROR: ICombatAttackModule interface not found.", stateController);
                return;
            }

            if (cancelExistingInSlot) attacks.CancelSlot(slot);
            attacks.StartAttack(definition, origin, destination, directionSource, slot);
        }
    }

    [System.Serializable]
    public sealed class RetargetComposableAttackAction : StateAction
    {
        [SerializeField] private string slot = "Default";
        [SerializeField] private PositionReference destination;

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

    [System.Serializable]
    public sealed class StopComposableAttackEffectAction : StateAction
    {
        [SerializeField] private string slot = "Default";

        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out ICombatAttackModule attacks))
                attacks.StopLatestEffect(slot);
        }
    }

}
