using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// Target과의 거리를 minDistance~maxDistance 범위에 유지한다.
    /// 너무 멀면 접근하고, 범위 안이면 정지하며, 너무 가까우면 Target 반대 방향의 노드를 선택한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Keep Distance Navigation", menuName = "Entity Control/Behaviours/Keep Distance")]
    public class KeepDistanceNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0f)] private float minDistance = 3f;
        [SerializeField, Min(0f)] private float maxDistance = 5f;
        [SerializeField, Min(0.1f)] private float fleeSearchRadius = 6f;
        [SerializeField, Min(0.05f)] private float targetUpdateInterval = 0.25f;
        [SerializeField, Range(1, 24)] private int fleeCandidateCount = 8;

        /// <summary>현재 Target 거리로 접근, 정지, 후퇴 중 하나를 선택한다.</summary>
        public override void Tick(NavigationAgentModule agent, float deltaTime)
        {
            if (agent.Target == null || Time.time < agent.BehaviourNextUpdate) return;
            agent.BehaviourNextUpdate = Time.time + targetUpdateInterval;

            float distance = Vector2.Distance(agent.Position, agent.Target.position);
            if (distance > maxDistance)
            {
                agent.SetDestination(agent.Target.position);
                return;
            }

            if (distance >= minDistance)
            {
                agent.HoldPosition();
                return;
            }

            Vector2 agentPosition = agent.Position;
            Vector2 targetPosition = agent.Target.position;
            Vector2 away = agentPosition - targetPosition;

            // 완전히 겹친 경우에는 이전 이동 방향을 우선하고, 그것도 없으면 Transform의 오른쪽을 사용한다.
            if (away.sqrMagnitude <= PhysicsManager.EPSILON)
            {
                away = agent.HasDestination
                    ? (Vector2)(agent.Destination - agent.Position)
                    : (Vector2)agent.transform.right;
            }
            away.Normalize();

            NavigationNode best = null;
            float bestDistance = distance;

            // 무작위 표본이 한쪽에 몰려도 후퇴 방향 후보를 확보할 수 있도록 정반대 지점을 먼저 검사한다.
            Vector3 directAwayPosition = agent.Position + (Vector3)(away * fleeSearchRadius);
            if (agent.Manager.World.TryGetClosestNode(directAwayPosition, agent.Profile, out NavigationNode directCandidate))
                EvaluateFleeCandidate(directCandidate, agentPosition, targetPosition, away, ref best, ref bestDistance);

            for (int i = 0; i < fleeCandidateCount; i++)
            {
                if (!agent.Manager.World.TryGetRandomNode(agent.Position, fleeSearchRadius, out NavigationNode candidate))
                    break;

                EvaluateFleeCandidate(candidate, agentPosition, targetPosition, away, ref best, ref bestDistance);
            }

            if (best != null)
            {
                agent.SetDestination(best.Position, true);
                return;
            }

            // 반대 방향에 유효한 노드가 없으면 이전 목적지가 Target 너머에 있을 수 있으므로 안전하게 중지한다.
            agent.HoldPosition();
        }

        /// <summary>
        /// 후보가 Agent에서 Target 반대 방향으로 진행하며 실제 Target 거리도 늘리는 경우에만 채택한다.
        /// 이 반평면 검사가 Target 너머의 먼 노드를 선택해 오히려 Target 쪽으로 출발하는 현상을 막는다.
        /// </summary>
        private static void EvaluateFleeCandidate(
            NavigationNode candidate,
            Vector2 agentPosition,
            Vector2 targetPosition,
            Vector2 away,
            ref NavigationNode best,
            ref float bestDistance)
        {
            Vector2 candidatePosition = candidate.Position;
            Vector2 offsetFromAgent = candidatePosition - agentPosition;

            // Agent를 지나는 경계면보다 Target 쪽에 있는 후보는 거리가 더 멀어도 사용하지 않는다.
            if (Vector2.Dot(offsetFromAgent, away) <= PhysicsManager.EPSILON) return;

            float candidateDistance = Vector2.Distance(candidatePosition, targetPosition);
            if (candidateDistance <= bestDistance) return;

            bestDistance = candidateDistance;
            best = candidate;
        }
    }
}
