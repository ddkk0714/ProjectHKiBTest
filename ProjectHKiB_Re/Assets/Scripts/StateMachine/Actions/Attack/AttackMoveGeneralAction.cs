using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class AttackMoveGeneralAction : StateAction
    {
        [SerializeField] private TargetingManagerSO targetingManager;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IAttackable attackable)
            && stateController.TryGetInterface(out IPhysics movable)
            && stateController.TryGetInterface(out IDirAnimatable animatable)
            && stateController.TryGetInterface(out ITargetable targetable))
            {
                if (attackable.AttackDatas.Equals(null))
                {
                    Debug.LogError("ERROR: AttackDatas is missing!!!"); return;
                }
                if (attackable.AttackDatas.Length - 1 < attackable.AttackNumber)
                {
                    Debug.LogError("ERROR: AttackData[" + attackable.AttackNumber + "] is missing !!!"); return;
                }

                AttackDataSO attackData = attackable.AttackDatas[attackable.AttackNumber];
                int moveRadius = attackData.attackMoveMaxRange;
                Transform thisTransform = stateController.transform;
                Transform target;
                if (!targetable.CurrentTarget)
                {
                    target = targetingManager.PositianalTarget(thisTransform.position, moveRadius, targetable.TargetLayers, targetable.CurrentTarget);
                    targetable.CurrentTarget = target;
                    if (!target) return;
                }
                Debug.DrawLine(thisTransform.position, targetable.CurrentTarget.position, Color.blue, 0.4f);
                animatable.SetAnimationDirection(targetable.CurrentTarget.position - thisTransform.position);
                Debug.LogError("ERROR: Not Implemented!!!");
                //movementManager.AttackMove(thisTransform, movable, targetable.CurrentTarget.position, moveRadius);
            }
            else
                Debug.LogError("ERROR: Interface Not Found!!!");

        }
    }
}