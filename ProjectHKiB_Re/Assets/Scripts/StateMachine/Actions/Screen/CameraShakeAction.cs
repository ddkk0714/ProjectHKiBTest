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

        public override void Act(StateController stateController)
        {
            CameraManager camera = CameraManager.instance;
            if (!camera)
            {
                Debug.LogError("ERROR: CameraShakeAction - CameraManager가 없습니다.");
                return;
            }
            camera.Shake(direction, strength);
        }
    }
}
