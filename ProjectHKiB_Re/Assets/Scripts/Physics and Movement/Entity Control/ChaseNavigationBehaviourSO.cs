using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// Agent.Target의 이동을 주기적으로 반영하며 stopDistance까지 추적한다.
    /// Target은 SetNavigationBehaviourAction의 CurrentTarget 연계로 지정하는 것이 일반적이다.
    /// </summary>
    [CreateAssetMenu(fileName = "Chase Navigation", menuName = "Entity Control/Behaviours/Chase")]
    public class ChaseNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0.05f)] private float targetUpdateInterval = 0.2f;
        [SerializeField, Min(0f)] private float stopDistance = 0.5f;

        /// <summary>갱신 주기마다 Target의 최신 위치를 목적지로 전달한다.</summary>
        public override void Tick(NavigationAgentModule agent, float deltaTime)
        {
            if (agent.Target == null) return;

            if (Vector2.Distance(agent.Position, agent.Target.position) <= stopDistance)
            {
                agent.HoldPosition();
                return;
            }

            if (Time.time < agent.BehaviourNextUpdate) return;
            agent.BehaviourNextUpdate = Time.time + targetUpdateInterval;
            agent.SetDestination(agent.Target.position);
        }
    }
}
