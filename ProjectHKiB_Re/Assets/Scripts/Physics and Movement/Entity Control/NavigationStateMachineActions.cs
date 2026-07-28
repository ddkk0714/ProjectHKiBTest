using EntityControl;
using UnityEngine;

namespace StateMachine
{
    [System.Serializable]
    public class SetNavigationBehaviourAction : StateAction
    {
        [SerializeField] private NavigationBehaviourSO behaviour;
        [SerializeField] private bool useCurrentTarget = true;
        [SerializeField, NaughtyAttributes.HideIf(nameof(useCurrentTarget))] private Transform explicitTarget;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out INavigationAgent agent))
            {
                Debug.LogError("ERROR: INavigationAgent interface not found.", stateController);
                return;
            }

            Transform selectedTarget = explicitTarget;
            if (useCurrentTarget &&
                stateController.TryGetInterface(out ITargetable targetable))
                selectedTarget = targetable.CurrentTarget;

            agent.SetBehaviour(behaviour, selectedTarget);
        }
    }

    [System.Serializable]
    public class NavigateToCurrentTargetAction : StateAction
    {
        [SerializeField] private bool forceRepath;

        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent) &&
                stateController.TryGetInterface(out ITargetable targetable) &&
                targetable.CurrentTarget != null)
            {
                agent.Target = targetable.CurrentTarget;
                agent.SetDestination(targetable.CurrentTarget.position, forceRepath);
                return;
            }

            Debug.LogError("ERROR: Navigation target or required interface not found.", stateController);
        }
    }

    [System.Serializable]
    public class NavigateToTransformAction : StateAction
    {
        [SerializeField] private Transform destination;
        [SerializeField] private bool forceRepath;

        public override void Act(StateController stateController)
        {
            if (destination != null &&
                stateController.TryGetInterface(out INavigationAgent agent))
            {
                agent.SetDestination(destination.position, forceRepath);
                return;
            }

            Debug.LogError("ERROR: Navigation destination or INavigationAgent not found.", stateController);
        }
    }

    [System.Serializable]
    public class StopNavigationAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent))
                agent.StopNavigation();
        }
    }

    [System.Serializable]
    public class ClearNavigationDestinationAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent))
                agent.ClearDestination();
        }
    }

    [System.Serializable]
    public class ForceNavigationRepathAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent))
                agent.ForceRepath();
        }
    }
}
