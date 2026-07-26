using UnityEngine;

namespace StateMachine
{
    // 이벤트 그래프/State 어디서든 코드 없이 배선만으로 RouteFinding 모듈에 이벤트 발생을
    // 기록하는 StateAction (SetEventFlagAction과 동일한 자리, 대상만 EventManager가 아니라
    // RouteModule.Progress). mapGuid를 비워두면 현재 위치(RouteModule.CurrentLocation)를 사용한다
    // — 인스펙터에서 eventKey만 정하고 다른 State에 그대로 복사해 붙여도 항상 "지금 있는 맵"
    // 기준으로 기록된다.
    //
    // 현재 배선 예시(2026-07-26): 감정 벡터 모듈의 잠/황홀 축 진입 State
    //   Enemy_Rusher_SleepState.EnterActions      -> eventKey: enemySlept
    //   Enemy_Rusher_EcstasyState.EnterActions    -> eventKey: enemyEcstasy
    //   Delta_Base_Sleep_StartState.EnterActions  -> eventKey: playerSlept
    //   Delta_Base_Ecstasy_StartState.EnterActions-> eventKey: playerEcstasy
    // 새 트리거를 추가하고 싶으면 원하는 State의 EnterActions에 이 액션을 배선하고 eventKey만
    // 정하면 된다 — ClueData.requiredEventKey를 그 값으로 지정하면 자동으로 단서가 공개된다.
    // 상세는 RouteModule.cs 상단 [외부 모듈 연동 API] 참고.
    [System.Serializable]
    public class SetRouteEventFlagAction : StateAction
    {
        public string mapGuid;
        public string eventKey;

        public override void Act(StateController stateController)
        {
            string guid = string.IsNullOrEmpty(mapGuid) ? RouteModule.Instance.CurrentLocation?.guid : mapGuid;
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError("ERROR: SetRouteEventFlagAction - mapGuid를 확인할 수 없습니다.");
                return;
            }

            RouteModule.Instance.Progress.SetEventFlag(guid, eventKey);
        }
    }
}
