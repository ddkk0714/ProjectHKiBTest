using UnityEngine;

namespace StateMachine
{
    /// <summary>
    /// IPhysics의 Wall/Floor LayerMask에 이름 있는 임시 override를 추가한다.
    /// 같은 slot은 교체되며, 다른 slot의 override와 적용 순서대로 합성된다.
    /// </summary>
    [AddTypeMenu("Physics/Set Layer Override")]
    [System.Serializable]
    public sealed class SetPhysicsLayerOverrideAction : StateAction
    {
        [Tooltip("ExitAction이나 ActionSequence에서 같은 이름으로 해제할 override 식별자.")]
        [SerializeField] private string slot = "Default";

        [SerializeField] private bool changeWallLayer = true;

        [NaughtyAttributes.ShowIf(nameof(changeWallLayer))]
        [NaughtyAttributes.AllowNesting]
        [SerializeField] private PhysicsLayerMaskOperation wallOperation = PhysicsLayerMaskOperation.Remove;

        [NaughtyAttributes.ShowIf(nameof(changeWallLayer))]
        [NaughtyAttributes.AllowNesting]
        [SerializeField] private LayerMask wallMask;

        [SerializeField] private bool changeFloorLayer;

        [NaughtyAttributes.ShowIf(nameof(changeFloorLayer))]
        [NaughtyAttributes.AllowNesting]
        [SerializeField] private PhysicsLayerMaskOperation floorOperation = PhysicsLayerMaskOperation.Remove;

        [NaughtyAttributes.ShowIf(nameof(changeFloorLayer))]
        [NaughtyAttributes.AllowNesting]
        [SerializeField] private LayerMask floorMask;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out IPhysics physics))
            {
                Debug.LogError("ERROR: IPhysics interface not found.", stateController);
                return;
            }

            physics.SetLayerOverride(
                slot,
                changeWallLayer,
                wallOperation,
                wallMask,
                changeFloorLayer,
                floorOperation,
                floorMask);
        }
    }

    /// <summary>같은 slot으로 등록한 임시 Physics LayerMask를 제거하고 이전 합성 결과로 복원한다.</summary>
    [AddTypeMenu("Physics/Clear Layer Override")]
    [System.Serializable]
    public sealed class ClearPhysicsLayerOverrideAction : StateAction
    {
        [SerializeField] private string slot = "Default";

        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics physics))
                physics.ClearLayerOverride(slot);
        }
    }

    /// <summary>같은 slot이 복구될 때까지 벽, 엔티티, 바닥과의 모든 커스텀 물리 충돌을 비활성화한다.</summary>
    [AddTypeMenu("Physics/Disable All Collisions")]
    [System.Serializable]
    public sealed class DisableAllPhysicsCollisionsAction : StateAction
    {
        [Tooltip("Restore All Collisions에서 같은 이름으로 복구할 비충돌 상태 식별자.")]
        [SerializeField] private string slot = "NoCollision";

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out IPhysics physics))
            {
                Debug.LogError("ERROR: IPhysics interface not found.", stateController);
                return;
            }

            physics.DisableAllCollisions(slot);
        }
    }

    /// <summary>같은 slot의 전체 충돌 비활성화를 제거하고 기존 Layer 합성 결과를 복구한다.</summary>
    [AddTypeMenu("Physics/Restore All Collisions")]
    [System.Serializable]
    public sealed class RestoreAllPhysicsCollisionsAction : StateAction
    {
        [SerializeField] private string slot = "NoCollision";

        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics physics))
                physics.RestoreCollisions(slot);
        }
    }

    /// <summary>등록된 모든 임시 Physics LayerMask를 제거하고 Data에서 받은 원본 값으로 복원한다.</summary>
    [AddTypeMenu("Physics/Clear All Layer Overrides")]
    [System.Serializable]
    public sealed class ClearAllPhysicsLayerOverridesAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics physics))
                physics.ClearAllLayerOverrides();
        }
    }
}
