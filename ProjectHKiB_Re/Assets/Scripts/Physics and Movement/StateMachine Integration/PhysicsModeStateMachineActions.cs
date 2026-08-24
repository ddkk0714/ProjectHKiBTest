using UnityEngine;

namespace StateMachine
{
    /// <summary>PhysicsManager의 전환 절차를 통해 엔티티의 이동 모드를 변경한다.</summary>
    [AddTypeMenu("Physics/Set Movement Mode")]
    [System.Serializable]
    public sealed class SetPhysicsMovementModeAction : StateAction
    {
        [SerializeField] private MovementMode mode = MovementMode.Physics;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out IPhysics physics))
            {
                Debug.LogError("ERROR: IPhysics interface not found.", stateController);
                return;
            }

            physics.SetMovementMode(mode);
        }
    }
}
