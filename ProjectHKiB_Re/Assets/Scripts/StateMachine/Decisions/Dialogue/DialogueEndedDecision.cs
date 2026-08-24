namespace StateMachine
{
    // 대화창이 완전히 닫혔는지. "NPC와 대화가 끝나면 보스화" 같은 순차 진행의 연결 고리다.
    //
    // DialogueLineEndedDecision은 "지금 줄의 출력이 끝났는가"라서 대사 사이사이마다 참이 된다.
    // 대화 전체가 끝나는 시점을 잡으려면 창이 닫혔는지를 봐야 한다(DialogueModule.ExitDialogue가
    // onExitDialogue로 UIManager에 Dialogue 창을 닫게 한다).
    [System.Serializable]
    public class DialogueEndedDecision : StateDecision
    {
        public override bool Decide(StateController stateController)
        {
            UIManager ui = GameManager.instance.UIManager;
            if (!ui) return true; // UI가 없으면 기다릴 대상도 없다

            return !ui.IsWindowOpen("Dialogue");
        }
    }
}
