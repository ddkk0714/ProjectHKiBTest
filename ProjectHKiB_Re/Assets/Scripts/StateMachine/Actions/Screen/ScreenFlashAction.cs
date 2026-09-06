using UnityEngine;
namespace StateMachine
{
    // 화면을 한 번 확 덮었다가 되돌린다. 충격 순간(넉백·날개 폭발)의 강조용.
    [System.Serializable]
    public class ScreenFlashAction : StateAction
    {
        public Color color = Color.white;
        [Min(0f)] public float duration = 0.2f;

        [Header("효과음 (선택)")]
        [Tooltip("섬광과 함께 재생할 SO 기반 원샷 효과음입니다. 비워 두면 무음입니다.")]
        public EffectAudioCue audioCue = new();

        public override void Act(StateController stateController)
        {
            Play(stateController);
        }

        public void Play()
        {
            Play(null);
        }

        private void Play(StateController stateController)
        {
            ScreenEffectManager.Instance.Flash(color, duration);

            // 실제 이벤트의 Act와 테스트베드의 Play가 반드시 같은 경로에서 한 번만 재생한다.
            audioCue?.Play(stateController);
        }
    }
}
