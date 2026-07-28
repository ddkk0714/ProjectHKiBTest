using EntityControl;
using UnityEngine;

namespace StateMachine
{
    [System.Serializable]
    public class NavigationStatusDecision : StateDecision
    {
        [SerializeField] private NavigationStatus expectedStatus;

        public override bool Decide(StateController stateController)
        {
            return stateController.TryGetInterface(out INavigationAgent agent) &&
                   agent.Status == expectedStatus;
        }
    }

    [System.Serializable]
    public class NavigationArrivedDecision : StateDecision
    {
        public override bool Decide(StateController stateController)
        {
            return stateController.TryGetInterface(out INavigationAgent agent) &&
                   agent.HasArrived;
        }
    }

    [System.Serializable]
    public class NavigationBlockedDecision : StateDecision
    {
        public override bool Decide(StateController stateController)
        {
            if (!stateController.TryGetInterface(out INavigationAgent agent)) return false;
            return agent.Status == NavigationStatus.Blocked ||
                   agent.Status == NavigationStatus.Failed;
        }
    }

    [System.Serializable]
    public class NavigationTargetDistanceDecision : StateDecision
    {
        [SerializeField, Min(0f)] private float distance = 1f;
        [SerializeField] private EnumManager.CompareType compareType;

        public override bool Decide(StateController stateController)
        {
            if (!stateController.TryGetInterface(out INavigationAgent agent) ||
                agent.Target == null) return false;

            float current = Vector2.Distance(stateController.transform.position, agent.Target.position);
            return compareType switch
            {
                EnumManager.CompareType.SameAs => Mathf.Approximately(current, distance),
                EnumManager.CompareType.BiggerThan => current > distance,
                EnumManager.CompareType.BiggerOrSameAs => current >= distance,
                EnumManager.CompareType.SmallerThan => current < distance,
                EnumManager.CompareType.SmallerOrSameAs => current <= distance,
                EnumManager.CompareType.NotSame => !Mathf.Approximately(current, distance),
                _ => false
            };
        }
    }
}
