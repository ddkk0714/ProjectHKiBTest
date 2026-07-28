using UnityEngine;

namespace EntityControl
{
    public abstract class NavigationBehaviourSO : ScriptableObject
    {
        public virtual void Enter(NavigationAgentModule agent) { }
        public abstract void Tick(NavigationAgentModule agent, float deltaTime);
        public virtual void Exit(NavigationAgentModule agent) { }
    }

    [CreateAssetMenu(fileName = "Direct Navigation", menuName = "Entity Control/Behaviours/Direct")]
    public class DirectNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField] private Transform fixedDestination;

        public override void Enter(NavigationAgentModule agent)
        {
            Transform destination = fixedDestination != null ? fixedDestination : agent.Target;
            if (destination != null)
                agent.SetDestination(destination.position, true);
        }

        public override void Tick(NavigationAgentModule agent, float deltaTime) { }
    }

    [CreateAssetMenu(fileName = "Patrol Navigation", menuName = "Entity Control/Behaviours/Patrol")]
    public class PatrolNavigationBehaviourSO : NavigationBehaviourSO
    {
        public enum PatrolOrder { Loop, PingPong, Random }

        [SerializeField] private PatrolOrder order = PatrolOrder.Loop;
        [SerializeField, Min(0f)] private float waitAtPoint;
        private const int ReverseFlag = 1 << 30;

        public override void Enter(NavigationAgentModule agent)
        {
            agent.BehaviourIndex = 0;
            agent.BehaviourNextUpdate = 0f;
            SelectCurrentPoint(agent, true);
        }

        public override void Tick(NavigationAgentModule agent, float deltaTime)
        {
            if (agent.PatrolPoints == null || agent.PatrolPoints.Count == 0) return;
            if (!agent.HasArrived) return;

            if (agent.BehaviourNextUpdate <= 0f)
            {
                agent.BehaviourNextUpdate = Time.time + waitAtPoint;
                return;
            }
            if (Time.time < agent.BehaviourNextUpdate) return;

            AdvanceIndex(agent);
            agent.BehaviourNextUpdate = 0f;
            SelectCurrentPoint(agent, true);
        }

        private void SelectCurrentPoint(NavigationAgentModule agent, bool force)
        {
            if (agent.PatrolPoints == null || agent.PatrolPoints.Count == 0) return;
            int index = agent.BehaviourIndex & ~ReverseFlag;
            index = Mathf.Clamp(index, 0, agent.PatrolPoints.Count - 1);
            Transform point = agent.PatrolPoints[index];
            if (point != null) agent.SetDestination(point.position, force);
        }

        private void AdvanceIndex(NavigationAgentModule agent)
        {
            int count = agent.PatrolPoints.Count;
            if (count <= 1) return;

            int index = agent.BehaviourIndex & ~ReverseFlag;
            bool reverse = (agent.BehaviourIndex & ReverseFlag) != 0;

            switch (order)
            {
                case PatrolOrder.Random:
                    int next = Random.Range(0, count - 1);
                    if (next >= index) next++;
                    agent.BehaviourIndex = next;
                    break;

                case PatrolOrder.PingPong:
                    index += reverse ? -1 : 1;
                    if (index >= count)
                    {
                        index = count - 2;
                        reverse = true;
                    }
                    else if (index < 0)
                    {
                        index = 1;
                        reverse = false;
                    }
                    agent.BehaviourIndex = index | (reverse ? ReverseFlag : 0);
                    break;

                default:
                    agent.BehaviourIndex = (index + 1) % count;
                    break;
            }
        }
    }

    [CreateAssetMenu(fileName = "Wander Navigation", menuName = "Entity Control/Behaviours/Wander")]
    public class WanderNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0.1f)] private float radius = 6f;
        [SerializeField, Min(0f)] private float minWait = 0.5f;
        [SerializeField, Min(0f)] private float maxWait = 2f;

        public override void Enter(NavigationAgentModule agent)
        {
            agent.BehaviourOrigin = agent.Position;
            agent.BehaviourNextUpdate = 0f;
            PickDestination(agent);
        }

        public override void Tick(NavigationAgentModule agent, float deltaTime)
        {
            bool needsNewDestination = agent.HasArrived ||
                                       agent.Status == NavigationStatus.Failed ||
                                       agent.Status == NavigationStatus.Blocked;
            if (!needsNewDestination || Time.time < agent.BehaviourNextUpdate) return;
            PickDestination(agent);
        }

        private void PickDestination(NavigationAgentModule agent)
        {
            agent.BehaviourNextUpdate = Time.time + Random.Range(minWait, Mathf.Max(minWait, maxWait));
            if (agent.Manager.World.TryGetRandomNode(agent.BehaviourOrigin, radius, out NavigationNode node))
                agent.SetDestination(node.Position, true);
        }
    }

    [CreateAssetMenu(fileName = "Chase Navigation", menuName = "Entity Control/Behaviours/Chase")]
    public class ChaseNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0.05f)] private float targetUpdateInterval = 0.2f;
        [SerializeField, Min(0f)] private float stopDistance = 0.5f;

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

    [CreateAssetMenu(fileName = "Flee Navigation", menuName = "Entity Control/Behaviours/Flee")]
    public class FleeNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0.1f)] private float searchRadius = 8f;
        [SerializeField, Range(1, 32)] private int candidateCount = 10;
        [SerializeField, Min(0.05f)] private float targetUpdateInterval = 0.35f;
        [SerializeField, Min(0f)] private float safeDistance = 8f;

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

    [CreateAssetMenu(fileName = "Keep Distance Navigation", menuName = "Entity Control/Behaviours/Keep Distance")]
    public class KeepDistanceNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField, Min(0f)] private float minDistance = 3f;
        [SerializeField, Min(0f)] private float maxDistance = 5f;
        [SerializeField, Min(0.1f)] private float fleeSearchRadius = 6f;
        [SerializeField, Min(0.05f)] private float targetUpdateInterval = 0.25f;
        [SerializeField, Range(1, 24)] private int fleeCandidateCount = 8;

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

            NavigationNode best = null;
            float bestDistance = distance;
            for (int i = 0; i < fleeCandidateCount; i++)
            {
                if (!agent.Manager.World.TryGetRandomNode(agent.Position, fleeSearchRadius, out NavigationNode candidate))
                    break;

                float candidateDistance = Vector2.Distance(candidate.Position, agent.Target.position);
                if (candidateDistance <= bestDistance) continue;
                bestDistance = candidateDistance;
                best = candidate;
            }

            if (best != null) agent.SetDestination(best.Position, true);
        }
    }
}
