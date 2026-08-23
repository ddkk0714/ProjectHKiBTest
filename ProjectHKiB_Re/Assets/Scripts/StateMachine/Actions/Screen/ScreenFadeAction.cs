using UnityEngine;
namespace StateMachine
{
    // 화면을 지정 색으로 덮거나(암전) 걷어낸다. duration 0이면 즉시 적용된다.
    // 연출을 "덮은 다음"에 이어가야 하면 다음 State를 두고 ScreenEffectEndedDecision으로 받을 것.
    [System.Serializable]
    public class ScreenFadeAction : StateAction
    {
        // 알파까지 포함한 목표 색. 완전 암전은 (0,0,0,1), 복귀는 (0,0,0,0).
        public Color targetColor = new(0f, 0f, 0f, 1f);
        [Min(0f)] public float duration = 1f;

        public override void Act(StateController stateController)
        {
            Play();
        }

        public void Play()
        {
            ScreenEffectManager.Instance.Fade(targetColor, duration);
        }
    }
}
