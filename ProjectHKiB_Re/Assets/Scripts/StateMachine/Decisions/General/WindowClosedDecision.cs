namespace StateMachine
{
    // 지정한 UI 창이 닫혀 있는지. "플레이어가 노트를 덮으면 다음으로" 같은, 조작을 기다리는 단계에 쓴다.
    //
    // 창 이름은 UIManager.windows에 등록된 문자열이다(각 패널의 WindowName 상수).
    //   지도 "Map" / 노트 "Note" / 도감 "Clue" / 인터넷 "Internet" / 대화 "Dialogue"
    //
    // [주의] 아직 열리지도 않은 창에 대해서도 참이다. 창을 여는 단계와 같은 State에 두면 열자마자
    // 통과해 버리므로, 반드시 연 뒤의 State에서 판정할 것.
    [System.Serializable]
    public class WindowClosedDecision : StateDecision
    {
        public string windowName = "Note";

        public override bool Decide(StateController stateController)
        {
            UIManager ui = GameManager.instance.UIManager;
            if (!ui) return true; // UI가 없으면 기다릴 대상도 없다

            return !ui.IsWindowOpen(windowName);
        }
    }
}
