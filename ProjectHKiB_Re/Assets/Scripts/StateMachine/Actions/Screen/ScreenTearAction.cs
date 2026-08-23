using UnityEngine;
namespace StateMachine
{
    // 화면이 종이처럼 찢어지는 연출(EVT-006 최종 탈출).
    // 아트가 없어 지금은 섬광+암전 더미로 동작하며 실행 시 경고 로그를 남긴다.
    // 리소스가 들어오면 ScreenEffectManager.ScreenTear 본문만 갈아끼우면 이 액션은 그대로 쓴다.
    [System.Serializable]
    public class ScreenTearAction : StateAction
    {
        [Min(0f)] public float duration = 1f;

        public override void Act(StateController stateController)
        {
            ScreenEffectManager.Instance.ScreenTear(duration);
        }
    }
}
