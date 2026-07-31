using EntityControl;
using UnityEngine;

namespace StateMachine
{
    /// <summary>
    /// Agent의 NavigationStatus가 Inspector의 expectedStatus와 같은지 검사한다.
    /// Planning/Following/Displaced 등 특정 상태에 대한 세밀한 전이에 사용한다.
    /// </summary>
    [System.Serializable]
    public class NavigationStatusDecision : StateDecision
    {
        // Transition이 true가 되기를 원하는 Agent 상태.
        [SerializeField] private NavigationStatus expectedStatus;

        /// <summary>INavigationAgent가 등록되어 있고 상태가 일치하면 true를 반환한다.</summary>
        public override bool Decide(StateController stateController)
        {
            return stateController.TryGetInterface(out INavigationAgent agent) &&
                   agent.Status == expectedStatus;
        }
    }

    /// <summary>
    /// Agent가 목적지 허용 반경 안에 도착했는지 검사한다.
    /// Patrol Behaviour 자체는 도착 후 다음 지점으로 진행하므로 별도 State 전이가 필요할 때만 사용한다.
    /// </summary>
    [System.Serializable]
    public class NavigationArrivedDecision : StateDecision
    {
        /// <summary>Agent.HasArrived를 반환한다.</summary>
        public override bool Decide(StateController stateController)
        {
            return stateController.TryGetInterface(out INavigationAgent agent) &&
                   agent.HasArrived;
        }
    }

    /// <summary>
    /// 예약/끼임으로 Blocked이거나 A* 경로 탐색이 Failed인지 한 번에 검사한다.
    /// Idle 전환, 순간이동 복구, 다른 이동 패턴 선택 등의 실패 처리 Transition에 사용한다.
    /// </summary>
    [System.Serializable]
    public class NavigationBlockedDecision : StateDecision
    {
        /// <summary>Blocked 또는 Failed 상태라면 true를 반환한다.</summary>
        public override bool Decide(StateController stateController)
        {
            if (!stateController.TryGetInterface(out INavigationAgent agent)) return false;
            return agent.Status == NavigationStatus.Blocked ||
                   agent.Status == NavigationStatus.Failed;
        }
    }

    /// <summary>
    /// Agent.Target까지의 XY 거리를 지정값과 비교한다.
    /// 공격 사거리 진입, 추적 시작/중단, 도주 완료 State 전이에 사용한다.
    /// </summary>
    [System.Serializable]
    public class NavigationTargetDistanceDecision : StateDecision
    {
        // 비교 기준 거리와 EnumManager의 비교 연산 종류.
        [SerializeField, Min(0f)] private float distance = 1f;
        [SerializeField] private EnumManager.CompareType compareType;

        /// <summary>Target이 없으면 false, 있으면 XY 거리를 compareType으로 평가한다.</summary>
        public override bool Decide(StateController stateController)
        {
            if (!stateController.TryGetInterface(out INavigationAgent agent) ||
                agent.Target == null) return false;

            float current = Vector2.Distance(stateController.transform.position, agent.Target.position);
            return compareType switch
            {
                EnumManager.CompareType.SameAs => Mathf.Approximately(current, distance),
                EnumManager.CompareType.BiggerThan => current > distance,
                EnumManager.CompareType.BiggerOrSameAs => current >= distance,
                EnumManager.CompareType.SmallerThan => current < distance,
                EnumManager.CompareType.SmallerOrSameAs => current <= distance,
                EnumManager.CompareType.NotSame => !Mathf.Approximately(current, distance),
                _ => false
            };
        }
    }
}
