using UnityEngine;
namespace StateMachine
{
    // 이벤트 연출 도중 플레이어 조작을 잠그고 푸는 액션.
    // 강제 컷신은 EnterActions에서 Cutscene으로, 연출이 끝나는 State에서 Play로 되돌리는 식으로 쓴다.
    // 되돌리는 배선을 빼먹으면 조작이 영영 잠기므로 반드시 짝으로 넣을 것.
    [System.Serializable]
    public class SetInputModeAction : StateAction
    {
        public EnumManager.InputMode mode;
        public override void Act(StateController stateController)
        {
            GameManager.instance.inputManager.SetInputMode(mode);
        }
    }
}
