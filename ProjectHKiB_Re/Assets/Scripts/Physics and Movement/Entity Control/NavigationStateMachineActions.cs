using EntityControl;
using UnityEngine;

namespace StateMachine
{
    /// <summary>
    /// State 진입 시 Agent의 지속 이동 패턴을 교체한다.
    /// Chase/Flee/KeepDistance는 useCurrentTarget을 켜서 ITargetable.CurrentTarget을 전달한다.
    /// 보통 EnterActions에 배치하고, 해당 이동을 완전히 끝내야 하면 ExitActions에 StopNavigationAction을 둔다.
    /// </summary>
    [System.Serializable]
    public class SetNavigationBehaviourAction : StateAction
    {
        // 적용할 공유 Behaviour Asset.
        [SerializeField] private NavigationBehaviourSO behaviour;
        // true면 상태 Controller의 ITargetable.CurrentTarget을 사용한다.
        [SerializeField] private bool useCurrentTarget = true;
        // false일 때 사용할 직접 Target. State asset에서는 Scene Transform 참조가 제한될 수 있다.
        [SerializeField, NaughtyAttributes.HideIf(nameof(useCurrentTarget))] private Transform explicitTarget;

        /// <summary>INavigationAgent와 선택 Target을 찾아 새 Behaviour를 적용한다.</summary>
        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out INavigationAgent agent))
            {
                Debug.LogError("ERROR: INavigationAgent interface not found.", stateController);
                return;
            }

            Transform selectedTarget = explicitTarget;
            if (useCurrentTarget &&
                stateController.TryGetInterface(out ITargetable targetable))
                selectedTarget = targetable.CurrentTarget;

            agent.SetBehaviour(behaviour, selectedTarget);
        }
    }

    /// <summary>
    /// ITargetable.CurrentTarget의 현재 위치를 Agent의 목적지로 전달한다.
    /// 일회성 이동에 사용하며 지속 추적에는 ChaseNavigationBehaviourSO가 더 적합하다.
    /// </summary>
    [System.Serializable]
    public class NavigateToCurrentTargetAction : StateAction
    {
        // true면 처리 중인 경로 요청도 무효화하고 즉시 새 요청을 제출한다.
        [SerializeField] private bool forceRepath;

        /// <summary>현재 Target을 Agent.Target과 Destination에 함께 설정한다.</summary>
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent) &&
                stateController.TryGetInterface(out ITargetable targetable) &&
                targetable.CurrentTarget != null)
            {
                agent.Target = targetable.CurrentTarget;
                agent.SetDestination(targetable.CurrentTarget.position, forceRepath);
                return;
            }

            Debug.LogError("ERROR: Navigation target or required interface not found.", stateController);
        }
    }

    /// <summary>
    /// Inspector에 지정한 Transform 위치로 일회성 이동을 요청한다.
    /// StateSO asset에서 Scene 오브젝트를 참조하기 어려운 경우 Patrol Point나 Target 기반 Action을 사용한다.
    /// </summary>
    [System.Serializable]
    public class NavigateToTransformAction : StateAction
    {
        // 이동할 Transform과 기존 요청을 무시할지 여부.
        [SerializeField] private Transform destination;
        [SerializeField] private bool forceRepath;

        /// <summary>지정 Transform이 유효하면 그 위치를 Agent 목적지로 설정한다.</summary>
        public override void Act(StateController stateController)
        {
            if (destination != null &&
                stateController.TryGetInterface(out INavigationAgent agent))
            {
                agent.SetDestination(destination.position, forceRepath);
                return;
            }

            Debug.LogError("ERROR: Navigation destination or INavigationAgent not found.", stateController);
        }
    }

    /// <summary>
    /// Behaviour, 목적지, 경로를 모두 종료한다.
    /// AI 이동 상태에서 완전히 빠져나가는 ExitAction에 사용한다.
    /// </summary>
    [System.Serializable]
    public class StopNavigationAction : StateAction
    {
        /// <summary>등록된 INavigationAgent의 StopNavigation을 호출한다.</summary>
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent))
                agent.StopNavigation();
        }
    }

    /// <summary>
    /// 현재 목적지와 경로만 제거하고 Behaviour는 유지한다.
    /// Behaviour가 다음 Tick에 다른 목적지를 다시 선택할 수 있는 일시 초기화용 Action이다.
    /// </summary>
    [System.Serializable]
    public class ClearNavigationDestinationAction : StateAction
    {
        /// <summary>등록된 INavigationAgent의 ClearDestination을 호출한다.</summary>
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent))
                agent.ClearDestination();
        }
    }

    /// <summary>
    /// 넉백 복귀, 동적 장애물 변경 등 외부 사건 직후 현재 목적지 경로를 강제로 다시 계산한다.
    /// UpdateAction에 상시 배치하지 말고 사건성 Enter/Transition Action으로 사용한다.
    /// </summary>
    [System.Serializable]
    public class ForceNavigationRepathAction : StateAction
    {
        /// <summary>등록된 INavigationAgent에 즉시 재탐색을 요청한다.</summary>
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out INavigationAgent agent))
                agent.ForceRepath();
        }
    }
}
