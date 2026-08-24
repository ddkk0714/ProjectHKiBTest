using UnityEngine;
namespace StateMachine
{
    // 화면을 한 번 확 덮었다가 되돌린다. 충격 순간(넉백·날개 폭발)의 강조용.
    [System.Serializable]
    public class ScreenFlashAction : StateAction
    {
        public Color color = Color.white;
        [Min(0f)] public float duration = 0.2f;

        public override void Act(StateController stateController)
        {
            Play();
        }

        public void Play()
        {
            ScreenEffectManager.Instance.Flash(color, duration);
        }
    }
}
