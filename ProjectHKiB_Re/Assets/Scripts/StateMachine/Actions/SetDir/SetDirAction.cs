using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class SetDirAction : StateAction
    {
        [SerializeField] private EnumManager.AnimDir animDir;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IDirAnimatable animatable))
            {
                animatable.SetAnimationDirection(animDir);
            }
        }
    }

    [System.Serializable]
    public class SetDirFromLastSetDirAction : StateAction
    {
        [SerializeField] private bool negative;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics movable))
            {
                if (stateController.TryGetInterface(out IDirAnimatable animatable))
                {
                    animatable.SetAnimationDirection(movable.LastSetDir);
                }
            }
        }
    }

    [System.Serializable]
    public class SetDirFromPlayerInputAction : StateAction
    {
        [SerializeField] private bool negative;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IDirAnimatable animatable))
                animatable.SetAnimationDirection(negative ? GameManager.instance.inputManager.MoveInput * -1 : GameManager.instance.inputManager.MoveInput);
        }
    }

    [System.Serializable]
    public class SetDirFromTargetPosAction : StateAction
    {
        [SerializeField] private bool negative;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IDirAnimatable animatable)
            && stateController.TryGetInterface(out ITargetable targetable))
            {
                if (!targetable.CurrentTarget) return;
                Vector2 dir = targetable.CurrentTarget.position - stateController.transform.position;
                if (!animatable.CheckIfLastSetDirectionSame(dir))
                    animatable.SetAnimationDirection(negative ? dir * -1 : dir);
            }
        }
    }

    [System.Serializable]
    public class SetDirFromVelocityAction : StateAction
    {
        [SerializeField] private bool negative;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics movable))
            {
                Vector2 dir = movable.ExForce * (negative ? -1 : 1);
                if (stateController.TryGetInterface(out IDirAnimatable animatable))
                    animatable.SetAnimationDirection(dir);
            }

        }
    }

    [System.Serializable]
    public class SetDirRandomAction : StateAction
    {
        [SerializeField] private bool negative;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IDirAnimatable animatable))
            {
                Vector2 dir = Vector2.up * Random.Range(-1, 2) + Vector2.right * Random.Range(-1, 2);
                if (!animatable.CheckIfLastSetDirectionSame(dir))
                    animatable.SetAnimationDirection(negative ? dir * -1 : dir);
            }
        }
    }
}