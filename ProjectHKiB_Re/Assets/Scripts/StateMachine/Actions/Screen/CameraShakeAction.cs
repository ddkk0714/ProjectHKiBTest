using UnityEngine;
namespace StateMachine
{
    // 카메라 흔들기(Cinemachine Impulse). 충격 순간 연출용.
    // 세기를 0으로 두면 씬의 임펄스 소스에 설정된 기본값을 쓴다 — 연출마다 다르게 흔들고 싶을 때만
    // 값을 준다(예: 가위질은 약하게, 날개 폭발은 강하게).
    [System.Serializable]
    public class CameraShakeAction : StateAction
    {
        [Min(0f)] public float strength;
        public Vector3 direction = Vector3.one;

        [Header("효과음 (선택)")]
        [Tooltip("흔들림과 함께 재생할 SO 기반 원샷 효과음입니다. 비워 두면 무음입니다.")]
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
            CameraManager camera = CameraManager.instance;
            if (camera) camera.Shake(direction, strength);
            else Debug.LogError("ERROR: CameraShakeAction - CameraManager가 없습니다.");

            // 실제 이벤트의 Act와 테스트베드의 Play가 반드시 같은 경로에서 한 번만 재생한다.
            // 카메라가 없어도 소리는 낸다 — 사운드는 카메라에 의존하지 않는다.
            audioCue?.Play(stateController);
        }
    }
}
