using Combat;
using UnityEngine;

namespace StateMachine
{
    [System.Serializable]
    public sealed class IsComposableAttackRunningDecision : StateDecision
    {
        [SerializeField] private string slot = "Default";

        public override bool Decide(StateController stateController)
        {
            return stateController.TryGetInterface(out ICombatAttackModule attacks) &&
                   attacks.IsRunning(slot);
        }
    }

    /// <summary>
    /// Player, Self, CurrentTarget, World 좌표 또는 SceneAnchor가 해당 slot의 어느 공격 범위에 있는지 검사한다.
    /// includeTelegraph를 켜면 예고 단계의 범위도 판단할 수 있다.
    /// </summary>
    [System.Serializable]
    public sealed class IsPositionInComposableAttackDecision : StateDecision
    {
        [SerializeField] private string slot = "Default";
        [SerializeField] private CombatPositionReference target;
        [SerializeField] private bool includeTelegraph;

        public override bool Decide(StateController stateController)
        {
            if (!stateController.TryGetInterface(out ICombatAttackModule attacks)) return false;

            return target.TryResolve(stateController, out Vector3 position) &&
                   attacks.Contains(slot, position, includeTelegraph);
        }
    }

    [System.Serializable]
    public sealed class HasComposableAttackHitTargetDecision : StateDecision
    {
        [SerializeField] private string slot = "Default";
        [SerializeField] private CombatPositionReference target;

        public override bool Decide(StateController stateController)
        {
            if (!stateController.TryGetInterface(out ICombatAttackModule attacks)) return false;
            Transform targetTransform = target.ResolveTransform(stateController);
            return targetTransform != null && attacks.HasHit(slot, targetTransform);
        }
    }
}
