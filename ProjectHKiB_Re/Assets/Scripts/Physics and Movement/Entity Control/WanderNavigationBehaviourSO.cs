using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// Behaviour 시작 위치 주변에서 임의의 NavigationNode를 계속 선택한다.
    /// 영구 배회 NPC에 사용하며, 실패/막힘 이후에도 새 목적지를 선택한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Wander Navigation", menuName = "Entity Control/Behaviours/Wander")]
    public class WanderNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0.1f)] private float radius = 6f;
        [SerializeField, Min(0f)] private float minWait = 0.5f;
        [SerializeField, Min(0f)] private float maxWait = 2f;

        /// <summary>현재 위치를 배회 중심으로 저장하고 첫 목적지를 고른다.</summary>
        public override void Enter(NavigationAgentModule agent)
        {
            agent.BehaviourOrigin = agent.Position;
            agent.BehaviourNextUpdate = 0f;
            PickDestination(agent);
        }

        /// <summary>도착/실패/막힘 이후 대기 시간이 지나면 새 임의 목적지를 고른다.</summary>
        public override void Tick(NavigationAgentModule agent, float deltaTime)
        {
            bool needsNewDestination = agent.HasArrived ||
                                       agent.Status == NavigationStatus.Failed ||
                                       agent.Status == NavigationStatus.Blocked;
            if (!needsNewDestination || Time.time < agent.BehaviourNextUpdate) return;
            PickDestination(agent);
        }

        /// <summary>NavigationWorld에서 반경 내 임의 노드를 받아 강제 재탐색한다.</summary>
        private void PickDestination(NavigationAgentModule agent)
        {
            agent.BehaviourNextUpdate = Time.time + Random.Range(minWait, Mathf.Max(minWait, maxWait));
            if (agent.Manager.World.TryGetRandomNode(agent.BehaviourOrigin, radius, out NavigationNode node))
                agent.SetDestination(node.Position, true);
        }
    }
}
