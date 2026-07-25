using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class PlayAnimationAction : StateAction
    {
        public string animationName;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IAnimatable animatable))
            {
                animatable.Play(animationName);
            }
            else if (stateController.TryGetInterface(out IDirAnimatable dirAnimatable))
            {
                dirAnimatable.Play(animationName);
            }
            else Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}
