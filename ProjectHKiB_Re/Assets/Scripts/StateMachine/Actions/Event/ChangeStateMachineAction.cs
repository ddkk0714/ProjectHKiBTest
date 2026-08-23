using UnityEngine;
namespace StateMachine
{
    // 대상의 상태 기계를 통째로 갈아끼운다 — NPC의 "보스화"가 이것이다.
    // TargetEntityManipulateAction(또는 TargetEntitiesManipulateAction) 안에 넣어서 쓰면
    // 이벤트에 등장하는 NPC를 지목해 보스 상태 기계로 바꿀 수 있다.
    //
    // [주의] 여기서 바꾸는 건 상태 기계뿐이다. 겉모습(스프라이트·애니메이션 세트)까지 바꾸려면
    // 그 교체를 하는 액션을 나란히 배선해야 한다 — 한쪽만 바꿔놓고 "모습이 안 변한다"고 헤매기 쉽다.
    [System.Serializable]
    public class ChangeStateMachineAction : StateAction
    {
        public StateMachineSO stateMachine;
        // 비워두면 상태 기계에 정의된 initialState에서 시작한다.
        public StateSO startState;
        [SerializeReference, SubclassSelector] public StateAction[] followUpActions;

        public override void Act(StateController stateController)
        {
            if (!stateMachine)
            {
                Debug.LogError("ERROR: ChangeStateMachineAction - stateMachine이 비어 있습니다.");
                return;
            }

            // 꺼져 있는 대상에는 걸 수 없다. StateSO.EnterState가 ReserveTransitions에서 코루틴을
            // 돌리는데, 비활성 GameObject에서 StartCoroutine을 부르면 Unity가 예외를 던져 그 뒤
            // 액션들까지 통째로 죽는다. 이벤트에서 퇴장시킨(SetEntityActiveAction) NPC를 나중 단계가
            // 다시 건드릴 때 실제로 걸리는 경로라, 조용히 죽는 대신 이유를 남기고 건너뛴다.
            if (!stateController.gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"[ChangeStateMachineAction] '{stateController.name}'이(가) 비활성이라 " +
                                 "상태 기계를 바꾸지 않고 건너뜁니다. 되살리려면 ReviveEntityAction이나 " +
                                 "SetEntityActiveAction(active=true)을 먼저 배선하세요.");
                return;
            }

            stateController.Initialize(stateMachine);
            if (startState) stateController.ChangeState(startState);

            if (followUpActions == null) return;
            for (int i = 0; i < followUpActions.Length; i++)
                followUpActions[i]?.Act(stateController);
        }
    }
}
