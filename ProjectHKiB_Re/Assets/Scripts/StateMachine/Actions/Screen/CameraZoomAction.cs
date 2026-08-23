using Cinemachine;
using UnityEngine;
namespace StateMachine
{
    // 카메라 줌. 기본 해상도(OriginalRes) 대비 배율로 지정한다 — 1보다 작으면 확대(클로즈업).
    //
    // [아트 없음] 기획서 EVT-002의 "눈가를 화면 전체에 2D 일러스트로 클로즈업"은 전용 일러스트가
    // 있어야 하는 연출이다. 지금은 카메라 줌으로 대체하고, warnMissingIllustration을 켜면
    // 무엇이 빠졌는지 로그로 남긴다. 일러스트가 들어오면 그 표시용 액션을 따로 만들어 이 액션과
    // 나란히 배선하면 된다.
    [System.Serializable]
    public class CameraZoomAction : StateAction
    {
        [Min(0.01f)] public float sizeMultiplier = 0.4f;
        [Min(0f)] public float blendTime = 0.5f;
        // 켜면 배율을 무시하고 원래 해상도로 되돌린다.
        public bool returnToOriginal;
        // 블렌딩 곡선. 예전엔 EaseInOut으로 코드에 박혀 있어 연출마다 조절할 수 없었다.
        public CinemachineBlendDefinition.Style blendStyle = CinemachineBlendDefinition.Style.EaseInOut;
        public bool warnMissingIllustration;

        public override void Act(StateController stateController)
        {
            if (warnMissingIllustration)
                Debug.LogWarning("[CameraZoomAction] 클로즈업 일러스트가 없어 카메라 줌으로 대체합니다.");

            CameraManager camera = CameraManager.instance;
            if (!camera)
            {
                Debug.LogError("ERROR: CameraZoomAction - CameraManager가 없습니다.");
                return;
            }

            if (returnToOriginal) camera.ReturntoOrigRes(blendTime, blendStyle);
            else camera.ZoomViaOrig(sizeMultiplier, blendTime, blendStyle);
        }
    }
}
