using UnityEngine;
namespace StateMachine
{
    // 대상을 등장시키거나 퇴장시킨다 — EVT-001의 "금발 아이의 퇴장", EVT-006의 "중앙에 문이 생김".
    // TargetEntityManipulateAction/TargetEntitiesManipulateAction 안에 넣어 대상을 지목한다.
    //
    // [주의] 끄면 그 오브젝트의 StateController도 같이 멈춘다(그게 퇴장의 목적이다). 다시 켤 때
    // 상태 기계를 원하는 상태에서 시작시키려면 ChangeStateMachineAction을 뒤이어 배선할 것.
    // 맵을 다시 로드하면 MapDataSO의 초기화 정보가 이 활성 상태를 되살리므로, 퇴장을 영구히
    // 남기려면 해당 dood 값에 대한 initinfo도 함께 저작해야 한다(SaveInitInfoAction).
    [System.Serializable]
    public class SetEntityActiveAction : StateAction
    {
        public bool active;

        public override void Act(StateController stateController)
        {
            stateController.gameObject.SetActive(active);
        }
    }
}
