using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class SetPositionalTargetAction : StateAction
    {
        [SerializeField] private float radius;
        [SerializeField] private TargetingManagerSO targetManager;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out ITargetable targetable))
            {
                Transform t = targetManager.PositianalTarget(stateController.transform.position, radius, targetable.TargetLayers, targetable.CurrentTarget);
                targetable.CurrentTarget = t;
            }
        }
    }

    [System.Serializable]
    public class SetDirectionalTargetAction : StateAction
    {
        public enum DirSource { Explicit, Anim, MovementIntend, PhysicalMovement }
        [SerializeField] private float radius;
        [SerializeField] private DirSource dirSource;
        [SerializeField] private Vector2 _explicitDir;
        [SerializeField] private TargetingManagerSO targetManager;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out ITargetable targetable))
            {
                Vector2 dir = Vector2.down;
                switch (dirSource)
                {
                    case DirSource.Explicit: dir = _explicitDir; break;
                    case DirSource.Anim: if (stateController.TryGetInterface(out IDirAnimatable dirAnimatable)) dir = dirAnimatable.AnimationDirection.DirToVector2(); break;
                    case DirSource.MovementIntend: if (stateController.TryGetInterface(out IPhysics phys)) dir = phys.WalkingDir; break;
                    case DirSource.PhysicalMovement: if (stateController.TryGetInterface(out IPhysics phys2)) dir = phys2.HVelocity; break;
                }
                Transform t = targetManager.DirectionalTarget(stateController.transform.position, radius, dir, targetable.TargetLayers, targetable.CurrentTarget);
                targetable.CurrentTarget = t;
            }
        }
    }
}