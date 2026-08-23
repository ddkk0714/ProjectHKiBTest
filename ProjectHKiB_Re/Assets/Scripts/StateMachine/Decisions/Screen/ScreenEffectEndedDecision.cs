namespace StateMachine
{
    // 화면 연출이 전부 끝났는지. 암전이 다 덮인 다음에 맵을 갈아끼우는 식으로 순서를 만들 때 쓴다.
    // 아무 연출도 시작하지 않았으면 곧바로 true다 — 시작하는 액션과 같은 State에 두지 말고
    // 다음 State의 전이 조건으로 걸 것.
    [System.Serializable]
    public class ScreenEffectEndedDecision : StateDecision
    {
        public override bool Decide(StateController stateController)
        {
            return !ScreenEffectManager.Instance.IsPlaying;
        }
    }
}
