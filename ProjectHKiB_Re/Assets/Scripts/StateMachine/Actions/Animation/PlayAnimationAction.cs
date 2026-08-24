using UnityEngine;
namespace StateMachine
{
    // 클립 이름이 비어 있으면 아무 일도 하지 않는다 — 아직 애니메이션이 없는 연출에 "나중에 여기
    // 채운다"는 뜻의 빈칸으로 미리 배선해 둘 수 있게 하려는 것이다. 대상 조회보다 먼저 빠져나가므로
    // 애니메이션 모듈이 없는 더미 대상에 걸려 있어도 에러가 나지 않는다.
    [System.Serializable]
    public class PlayAnimationAction : StateAction
    {
        public string animationName;
        public override void Act(StateController stateController)
        {
            if (string.IsNullOrEmpty(animationName)) return;

            if (stateController.TryGetInterface(out IAnimatable animatable))
            {
                animatable.Play(animationName);
            }
            else if (stateController.TryGetInterface(out IDirAnimatable dirAnimatable))
            {
                dirAnimatable.Play(animationName);
            }
            else Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}
