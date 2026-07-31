using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// NavigationAgentModule.patrolPoints를 Loop/PingPong/Random 순서로 순회한다.
    /// Patrol Point는 Agent Component의 patrolPoints에 Scene Transform으로 지정한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Patrol Navigation", menuName = "Entity Control/Behaviours/Patrol")]
    public class PatrolNavigationBehaviourSO : NavigationBehaviourSO
    {
        public enum PatrolOrder { Loop, PingPong, Random }

        [SerializeField] private PatrolOrder order = PatrolOrder.Loop;
        [SerializeField, Min(0f)] private float waitAtPoint;
        private const int ReverseFlag = 1 << 30;

        /// <summary>첫 Patrol Point를 선택하고 순회 인덱스를 초기화한다.</summary>
        public override void Enter(NavigationAgentModule agent)
        {
            agent.BehaviourIndex = 0;
            agent.BehaviourNextUpdate = 0f;
            SelectCurrentPoint(agent, true);
        }

        /// <summary>현재 Point 도착 후 waitAtPoint만큼 기다렸다가 다음 Point를 지정한다.</summary>
        public override void Tick(NavigationAgentModule agent, float deltaTime)
        {
            if (agent.PatrolPoints == null || agent.PatrolPoints.Count == 0) return;
            if (!agent.HasArrived) return;

            // 첫 도착 프레임에는 대기 종료 시각만 예약하고, 이후 프레임에서 다음 Point로 진행한다.
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

        /// <summary>현재 BehaviourIndex에 대응하는 Transform을 Agent 목적지로 적용한다.</summary>
        private void SelectCurrentPoint(NavigationAgentModule agent, bool force)
        {
            if (agent.PatrolPoints == null || agent.PatrolPoints.Count == 0) return;
            int index = agent.BehaviourIndex & ~ReverseFlag;
            index = Mathf.Clamp(index, 0, agent.PatrolPoints.Count - 1);
            Transform point = agent.PatrolPoints[index];
            if (point != null) agent.SetDestination(point.position, force);
        }

        /// <summary>PatrolOrder에 따라 다음 배열 인덱스와 PingPong 방향을 계산한다.</summary>
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
}
