using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// Target과 멀어지는 NavigationNode 후보를 점수화해 도주한다.
    /// safeDistance에 도달하면 Behaviour를 유지한 채 HoldPosition한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Flee Navigation", menuName = "Entity Control/Behaviours/Flee")]
    public class FleeNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0.1f)] private float searchRadius = 8f;
        [SerializeField, Range(1, 32)] private int candidateCount = 10;
        [SerializeField, Min(0.05f)] private float targetUpdateInterval = 0.35f;
        [SerializeField, Min(0f)] private float safeDistance = 8f;

        /// <summary>여러 후보 중 Target과 멀고 도주 방향에 가까운 노드를 선택한다.</summary>
        public override void Tick(NavigationAgentModule agent, float deltaTime)
        {
            if (agent.Target == null) return;

            if (Vector2.Distance(agent.Position, agent.Target.position) >= safeDistance)
            {
                agent.HoldPosition();
                return;
            }
            if (Time.time < agent.BehaviourNextUpdate) return;

            agent.BehaviourNextUpdate = Time.time + targetUpdateInterval;
            NavigationNode best = null;
            float bestScore = float.MinValue;
            Vector2 away = ((Vector2)(agent.Position - agent.Target.position)).normalized;

            // 단순 반대 직선 대신 여러 후보를 비교해 벽/막다른 길에서 선택 폭을 확보한다.
            for (int i = 0; i < candidateCount; i++)
            {
                if (!agent.Manager.World.TryGetRandomNode(agent.Position, searchRadius, out NavigationNode candidate))
                    break;

                Vector2 offset = candidate.Position - agent.Position;
                float score = Vector2.Distance(candidate.Position, agent.Target.position);
                score += Vector2.Dot(offset.normalized, away) * searchRadius;
                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            if (best != null) agent.SetDestination(best.Position, true);
        }
    }
}
