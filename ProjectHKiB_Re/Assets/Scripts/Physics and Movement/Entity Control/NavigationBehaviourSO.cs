using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// Agent의 목적지를 지속적으로 선택하는 고수준 이동 패턴의 기반 Asset이다.
    /// Asset은 여러 Agent가 공유하므로 엔티티별 런타임 상태는 Agent에 보관한다.
    /// </summary>
    public abstract class NavigationBehaviourSO : ScriptableObject
    {
        /// <summary>Behaviour가 Agent에 적용될 때 한 번 호출된다.</summary>
        public virtual void Enter(NavigationAgentModule agent) { }

        /// <summary>적용 중인 Agent의 Update마다 호출되어 목적지를 선택/갱신한다.</summary>
        public abstract void Tick(NavigationAgentModule agent, float deltaTime);

        /// <summary>다른 Behaviour로 교체되거나 내비게이션이 중지될 때 호출된다.</summary>
        public virtual void Exit(NavigationAgentModule agent) { }
    }
}
