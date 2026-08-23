using UnityEngine;
namespace StateMachine
{
    // 지금 재생 중인 클립이 한 바퀴 끝나면 이어서 틀 클립을 예약한다(SimpleAnimationPlayer.Reserve).
    //
    // "시작 → 유지 → 종료" 3단 연출을 만들 때 쓴다. 시작 동작을 Play로 틀고 유지 동작을 여기서
    // 예약해 두면, 시작이 끝나는 순간 자연스럽게 넘어간다 — 시작 클립의 길이를 코드가 몰라도 된다.
    //
    // [주의] 유지 클립이 반복(isLoop)이면 스스로는 절대 끝나지 않는다. 그 연출을 끝내는 단계에서
    // 다른 클립을 Play로 덮어써야 한다 — 안 그러면 영원히 그 동작에 머문다.
    //
    // 클립 이름이 비어 있으면 아무 일도 하지 않는다(아직 클립이 없는 자리에 미리 배선해 두는 용도).
    [System.Serializable]
    public class ReserveAnimationAction : StateAction
    {
        public string animationName;

        public override void Act(StateController stateController)
        {
            if (string.IsNullOrEmpty(animationName)) return;

            if (!stateController.TryGetInterface(out IAnimatable animatable))
            {
                Debug.LogError("ERROR: ReserveAnimationAction - IAnimatable을 찾을 수 없습니다.");
                return;
            }

            if (animatable.AnimationPlayer == null)
            {
                Debug.LogError("ERROR: ReserveAnimationAction - AnimationPlayer가 비어 있습니다.");
                return;
            }

            animatable.AnimationPlayer.Reserve(animationName);
        }
    }
}
