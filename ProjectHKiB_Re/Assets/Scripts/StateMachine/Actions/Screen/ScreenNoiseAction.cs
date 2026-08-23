using UnityEngine;
namespace StateMachine
{
    // TV 노이즈. 백발의 눈동자가 지지직거리는 연출(EVT-002)이나 가위질 순간의 화면 노이즈(EVT-001)용.
    // duration을 0으로 두면 stop을 켠 다른 액션이 끌 때까지 계속된다.
    [System.Serializable]
    public class ScreenNoiseAction : StateAction
    {
        [Range(0f, 1f)] public float intensity = 0.5f;
        [Min(0f)] public float duration = 1f;
        // 알갱이 크기 — 클수록 잘다. 0이면 기본값(16)을 쓴다.
        [Min(0f)] public float tiling;
        // 켜는 대신 끄는 용도로 쓸 때 체크. 위 값들은 무시된다.
        public bool stop;

        public override void Act(StateController stateController)
        {
            if (stop) ScreenEffectManager.Instance.StopNoise();
            else ScreenEffectManager.Instance.SetNoise(intensity, duration, tiling);
        }
    }
}
