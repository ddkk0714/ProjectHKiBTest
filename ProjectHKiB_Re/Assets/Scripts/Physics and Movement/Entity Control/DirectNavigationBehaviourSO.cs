using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// 하나의 고정 Transform 또는 Agent.Target 위치로 한 번 이동한다.
    /// Scene Transform은 Asset에 직접 저장하기 어려울 수 있으므로 일반적으로 Target을 Action에서 전달한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Direct Navigation", menuName = "Entity Control/Behaviours/Direct")]
    public class DirectNavigationBehaviourSO : NavigationBehaviourSO
    {
        [SerializeField] private Transform fixedDestination;

        /// <summary>fixedDestination을 우선하고, 없으면 Agent.Target을 목적지로 설정한다.</summary>
        public override void Enter(NavigationAgentModule agent)
        {
            Transform destination = fixedDestination != null ? fixedDestination : agent.Target;
            if (destination != null)
                agent.SetDestination(destination.position, true);
        }

        /// <summary>Direct 패턴은 Enter에서 한 번 목적지를 지정하므로 매 프레임 작업이 없다.</summary>
        public override void Tick(NavigationAgentModule agent, float deltaTime) { }
    }
}
