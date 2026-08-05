using UnityEngine;
namespace StateMachine
{
    /// <summary>
    /// 미구현 스텁 — 언제나 false를 돌려준다.
    ///
    /// "넉백 중인가"를 판정하려면 그것을 표시하는 상태가 필요한데 아직 없다.
    /// IPhysics.KnockBack은 ExForce에 힘을 더하기만 하고 EndKnockbackEarly와 KnockBackEndCallback은
    /// 빈 껍데기다(PhysicsModule.cs). 판정 기준(넉백 플래그 + 지속시간이냐, 속도/ExForce 임계값이냐)이
    /// 정해지면 여기를 채우면 된다.
    ///
    /// 그때까지의 실제 동작 — CheckDecisions의 `!Decide() ^ negate` 때문에 이렇게 갈린다.
    ///   negate 0으로 쓰인 전이(Idle/Walk/Run/NormalAttack* → Knockback, Roza·Lily 합쳐 24곳)는
    ///     절대 성립하지 않는다 → 넉백 State로 못 들어간다.
    ///   negate 1로 쓰인 KnockbackState의 탈출 전이는 항상 성립한다 → 들어가도 즉시 빠져나온다.
    /// 즉 넉백은 통째로 꺼져 있는 상태다.
    ///
    /// 로그는 도메인 리로드당 한 번만 남긴다. Decide는 CheckDecision을 통해 매 프레임 불리므로
    /// 매번 찍으면 콘솔이 묻히고 에디터가 눈에 띄게 느려진다.
    /// (TryGetInterface 호출이 있었지만 찾든 못 찾든 false였고 부수효과도 없어 걷어냈다.)
    /// </summary>
    [System.Serializable]
    public class KnockbackMoveDecision : StateDecision
    {
        private static bool _warned;

        public override bool Decide(StateController stateController)
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("KnockbackMoveDecision은 아직 미구현입니다 — 항상 false를 돌려주므로 " +
                                 "넉백 전이가 동작하지 않습니다. (이 경고는 한 번만 표시됩니다)");
            }

            return false;
        }
    }
}
