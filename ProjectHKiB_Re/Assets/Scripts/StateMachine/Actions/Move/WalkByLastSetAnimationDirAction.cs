using UnityEngine;

[CreateAssetMenu(
    fileName = "WalkByLastSetAnimationDir",
    menuName = "State Machine/Action/Move/WalkByLastSetAnimationDirAction")]
public class WalkByLastSetAnimationDirAction : StateActionSO
{
    [SerializeField] private bool _negate;
    [SerializeField] private MovementManagerSO movementManager;
    [SerializeField, Range(0.01f, 2f)] private float _speedMultiplier = 1f;

    public override void Act(StateController stateController)
    {
        if (stateController.TryGetInterface(out IMovable movable) &&
            stateController.TryGetInterface(out IDirAnimatable animatable))
        {
            Vector2 dir = animatable.LastSetAnimationDir8;

            if (_negate)
                dir *= -1f;

            movementManager.WalkMove(
                stateController.transform,
                movable,
                movable.Speed * _speedMultiplier,
                dir,
                movable.WallLayer
            );
        }
        else
        {
            Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}