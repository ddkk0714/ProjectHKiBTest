using UnityEngine;
namespace StateMachine
{
    // TV 노이즈. 백발의 눈동자가 지지직거리는 연출(EVT-002)이나 가위질 순간의 화면 노이즈(EVT-001)용.
    // duration을 0으로 두면 stop을 켠 다른 액션이 끌 때까지 계속된다.
    [System.Serializable]
    public class ScreenNoiseAction : StateAction
    {
        [Header("노이즈")]
        [Tooltip("노이즈 입자 자체의 강도입니다. 화면을 얼마나 가릴지는 Alpha로 따로 조절합니다.")]
        [Range(0f, 1f)] public float intensity = 0.5f;
        [Tooltip("노이즈 강도와 별도로 적용되는 최종 투명도. 1이면 원본 화면을 완전히 가린다.")]
        [Range(0f, 1f)] public float alpha = 0.5f;
        [Tooltip("0이면 Stop Noise 액션이 호출될 때까지 계속됩니다.")]
        [Min(0f)] public float duration = 1f;
        // 알갱이 크기 — 클수록 잘다. 0이면 기본값(16)을 쓴다.
        [Min(0f)] public float tiling;
        [Header("노이즈와 함께 재생할 화면 흔들림 (0이면 사용 안 함)")]
        [Min(0f)] public float shakeStrength;
        [Min(0f)] public float shakeDuration;
        [Min(0)] public int shakeCount;

        [Header("효과음 (선택)")]
        [Tooltip("노이즈 연출과 같은 프레임에 재생할 SO 기반 원샷 효과음입니다. 비워 두면 무음입니다.")]
        public EffectAudioCue audioCue = new();

        // 켜는 대신 끄는 용도로 쓸 때 체크. 위 값들은 무시된다.
        public bool stop;

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
            // stop은 다른 수치를 무시하고 현재 노이즈 요청만 취소한다.
            if (stop) ScreenEffectManager.Instance.StopNoise();
            else ScreenEffectManager.Instance.SetNoise(
                intensity,
                duration,
                tiling,
                alpha,
                shakeStrength,
                shakeDuration,
                shakeCount);

            // 실제 이벤트의 Act와 테스트베드의 Play가 반드시 같은 경로에서 한 번만 재생한다.
            audioCue?.Play(stateController);
        }
    }
}
