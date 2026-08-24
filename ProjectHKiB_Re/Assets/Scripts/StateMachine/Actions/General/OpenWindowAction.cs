using UnityEngine;
namespace StateMachine
{
    // UI 창을 연다/닫는다 — EVT-003에서 현실의 책상 노트를 펼치는 데 쓴다.
    //
    // 창 이름은 UIManager.windows에 등록된 문자열이다. 등록명이 한 글자만 어긋나도 UIManager가
    // 경고만 남기고 조용히 넘어가므로, 각 패널의 WindowName 상수를 그대로 쓸 것.
    //   지도 "Map" / 노트 "Note" / 도감 "Clue" / 인터넷 "Internet" / 대화 "Dialogue"
    [System.Serializable]
    public class OpenWindowAction : StateAction
    {
        public string windowName = "Note";
        public bool close;

        public override void Act(StateController stateController)
        {
            UIManager ui = GameManager.instance.UIManager;
            if (!ui)
            {
                Debug.LogError("ERROR: OpenWindowAction - UIManager를 찾을 수 없습니다.");
                return;
            }

            if (close) ui.CloseWindow(windowName);
            else ui.OpenWindow(windowName);
        }
    }
}
