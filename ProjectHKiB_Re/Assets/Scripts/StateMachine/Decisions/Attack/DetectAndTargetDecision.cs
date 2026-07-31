using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class DetectAndTargetDecision : StateDecision
    {
        [SerializeField] private float radius;
        [SerializeField] private TargetingManagerSO targetManager;
        public override bool Decide(StateController stateController)
        {
            if (stateController.TryGetInterface(out ITargetable targetable))
            {
                Transform t = targetManager.PositianalTarget(stateController.transform.position, radius, targetable.TargetLayers, targetable.CurrentTarget);
                targetable.CurrentTarget = t;
                return t;
            }
            return false;
        }
    }
}