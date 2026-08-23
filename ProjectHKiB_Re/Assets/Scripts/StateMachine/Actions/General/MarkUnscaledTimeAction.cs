using UnityEngine;
namespace StateMachine
{
    // 지금의 unscaled 시각을 커스텀 int(밀리초)로 기록해 둔다. UnscaledTimeElapsedDecision과 짝이다.
    //
    // [왜 필요한가] StateSO의 useTimer/TimerDecision은 TimerManager를 타는데, 그쪽은 DOTween 기본
    // UpdateType.Normal(= Time.timeScale의 영향을 받는 스케일 시간)을 의도적으로 쓴다(버프 쿨타임이
    // 메뉴를 열면 같이 멈춰야 하므로). 그래서 컷신이 TimeManager.Pause로 게임을 멈추면 그 타이머도
    // 같이 얼어붙어 이벤트가 그 단계에서 영영 안 넘어간다 — 컷신이 스스로를 가둬버리는 셈이다.
    //
    // 이벤트 연출은 게임플레이가 멈춰 있어도 흘러야 하므로 unscaled 시간을 따로 쓴다.
    // 밀리초 int로 담는 이유는 StateController에 float 파라미터 API가 없기 때문이다(int만 있다).
    [System.Serializable]
    public class MarkUnscaledTimeAction : StateAction
    {
        // 같은 컨트롤러 안에서 단계끼리 겹치지 않게 State마다 다른 이름을 쓴다.
        public string key = "unscaledMark";

        public override void Act(StateController stateController)
        {
            stateController.SetIntParameter(key, Mathf.RoundToInt(Time.unscaledTime * 1000f));
        }
    }
}
