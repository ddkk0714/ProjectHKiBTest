using UnityEngine;
namespace StateMachine
{
    // 넉백이 끝났는지 — IPhysics.IsKnockedBack이 내려갔는지 본다.
    //
    // 밀려나는 시간은 가속·마찰에 따라 달라지므로 초 단위로 못 박을 수 없다. 이 판정을 쓰면
    // "실제로 멈출 때까지 유지 동작을 계속하다가, 멈추는 순간 종료 동작으로 넘어가는" 연출이 된다.
    //
    // [targetID를 반드시 채울 것 — 이벤트에서 쓸 때]
    // 이벤트의 상태 기계는 EventManager 위에서 돈다. 그래서 판정에 넘어오는 stateController는
    // 밀려나는 대상이 아니라 EventManager다. targetID를 비워 두면 EventManager 자신에게서
    // IPhysics를 찾다가 실패하고, "기다릴 대상이 없다"고 보아 곧바로 참이 된다 — 넉백이 시작되자마자
    // 다음 단계로 넘어가 애니메이션이 한 프레임 만에 덮여 사라진다.
    //
    // 캐릭터 자신의 상태 기계(Roza/Lily의 KnockbackState 등)에서 쓸 때는 그 컨트롤러가 곧 대상이므로
    // 비워 두면 된다.
    [System.Serializable]
    public class KnockbackEndedDecision : StateDecision
    {
        [Tooltip("이벤트에서 쓸 때는 밀려나는 대상의 ID(예: Player). 캐릭터 자신의 상태 기계에서는 비워 둔다.")]
        public string targetID;

        public override bool Decide(StateController stateController)
        {
            IPhysics physics = ResolvePhysics(stateController);

            // 대상을 못 찾았으면 여기서 붙잡아 두지 않는다(무한 대기보다는 진행). 다만 그건 배선이
            // 잘못됐다는 뜻이라 조용히 넘어가지 않고 알린다 — 이 경우가 정확히 "애니메이션이 안 나온다"로 보인다.
            if (physics == null)
            {
                Debug.LogWarning($"[KnockbackEndedDecision] 넉백 대상을 찾지 못해 곧바로 통과시킵니다 " +
                                 $"(targetID: '{targetID}'). 이벤트에서 쓴다면 targetID를 채워야 합니다.");
                return true;
            }

            return !physics.IsKnockedBack;
        }

        private IPhysics ResolvePhysics(StateController stateController)
        {
            if (string.IsNullOrEmpty(targetID))
                return stateController.TryGetInterface(out IPhysics own) ? own : null;

            if (!stateController.TryGetInterface(out IEvent @event) || @event.CurrentTargets == null) return null;
            if (!@event.CurrentTargets.targetEntities.ContainsKey(targetID)) return null;

            EventControllableEntity target = @event.CurrentTargets.targetEntities[targetID];
            if (target == null || target.Target == null) return null;

            return target.Target.TryGetInterface(out IPhysics physics) ? physics : null;
        }
    }
}
