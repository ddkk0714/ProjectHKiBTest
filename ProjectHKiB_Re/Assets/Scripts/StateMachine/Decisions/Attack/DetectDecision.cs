using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class DetectDecision : StateDecision
    {
        [SerializeField] private float radius;
        [SerializeField] private TargetingManagerSO targetManager;
        public override bool Decide(StateController stateController)
        {
            if (stateController.TryGetInterface(out ITargetable targetable))
            {
                return targetManager.PositianalTarget(stateController.transform.position, radius, targetable.TargetLayers, targetable.CurrentTarget);
            }
            return false;
        }
    }
}