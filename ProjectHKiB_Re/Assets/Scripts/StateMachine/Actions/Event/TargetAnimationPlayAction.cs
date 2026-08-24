using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class TargetAnimationPlayAction : StateAction
    {
        public string targetID;
        public string animationName;
        public override void Act(StateController stateController)
        {
            // PlayAnimationAction과 같은 이유 — 빈 이름은 "아직 안 정한 빈칸"으로 보고 조용히 넘어간다.
            if (string.IsNullOrEmpty(animationName)) return;

            if (stateController.TryGetInterface(out IEvent @event) && @event.CurrentTargets != null && @event.CurrentTargets.targetAnimations.ContainsKey(targetID))
            {
                @event.CurrentTargets.targetAnimations[targetID].Target.Play(animationName);
            }
            else Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}