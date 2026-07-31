using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class AttackAreaIndicateAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IAttackable attackable) && stateController.TryGetInterface(out IAttackIndicatable areaIndicatable) && stateController.TryGetInterface(out IDirAnimatable animatable))
            {
                areaIndicatable.StartIndicating(attackable.AttackDatas[attackable.AttackNumber].attackAreaIndicatorData, stateController.transform, animatable.AnimationDirection.DirToQuaternion4());
            }
            else
                Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}
