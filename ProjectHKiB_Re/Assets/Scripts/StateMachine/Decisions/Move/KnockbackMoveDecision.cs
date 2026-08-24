using UnityEngine;
namespace StateMachine
{
    /// <summary>
    /// 지금 넉백으로 밀려나는 중인지 — IPhysics.IsKnockedBack을 그대로 본다.
    ///
    /// [예전 상태] 오랫동안 미구현 스텁이라 항상 false였고, 그 탓에 넉백이 통째로 꺼져 있었다.
    /// CheckDecisions의 `!Decide() ^ negate` 때문에 이렇게 갈렸다:
    ///   negate 0으로 쓰인 전이(Idle/Walk/Run/NormalAttack* → Knockback, Roza·Lily 합쳐 24곳)는
    ///     절대 성립하지 않아 넉백 State로 못 들어갔고,
    ///   negate 1로 쓰인 KnockbackState의 탈출 전이는 항상 성립해 들어가도 즉시 빠져나왔다.
    ///
    /// [지금] IPhysics.KnockBack이 표시를 켜고, 멈춰 서면(속도가 정착 임계값 아래 + 미는 힘 없음)
    /// PhysicsManager가 끈다. 그래서 위 두 전이가 모두 의도대로 동작한다 — 밀려나는 동안
    /// KnockbackState에 머무르며 그 State에 배선된 애니메이션이 재생된다.
    /// 도중에 끊어야 하면 IPhysics.EndKnockbackEarly()를 부르면 된다.
    /// </summary>
    [System.Serializable]
    public class KnockbackMoveDecision : StateDecision
    {
        public override bool Decide(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics physics)) return physics.IsKnockedBack;

            Debug.LogError("ERROR: KnockbackMoveDecision - IPhysics를 찾을 수 없습니다.");
            return false;
        }
    }
}
