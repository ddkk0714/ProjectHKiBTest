using UnityEngine;

namespace StateMachine
{
    // 그로기/도주/자해/감정축처럼 State 전환만으로는 화면에 드러나지 않는 버프 반응에 색 틴트로
    // 시각 피드백을 준다. 진입 State의 EnterActions에 색을 걸고, 빠져나가는 State의
    // ExitActions에 흰색(Color.white)으로 되돌리는 식으로 짝을 맞춰 쓴다.
    // GetComponentInChildren<SpriteRenderer>()로 메인 스프라이트 하나만 찾아 칠한다 — 보조
    // 스프라이트(예: dither 레이어)는 대상이 아니다.
    //
    // 현재 사용 중인 색 배정(새 State에 추가할 때 겹치지 않게 참고):
    //   그로기=노랑 {1, 0.85, 0.2}   도주=하늘색 {0.4, 0.85, 1}   자해=빨강 {1, 0.3, 0.3}
    //   잠(Sleep)=남색 {0.35, 0.35, 0.65}   황홀(Ecstasy)=핑크 {1, 0.45, 0.8}
    [System.Serializable]
    public class SetSpriteTintAction : StateAction
    {
        [SerializeField] private Color color = Color.white;

        public override void Act(StateController stateController)
        {
            SpriteRenderer renderer = stateController.GetComponentInChildren<SpriteRenderer>();
            if (renderer) renderer.color = color;
        }
    }
}
