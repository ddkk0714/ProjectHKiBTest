using UnityEngine;
namespace StateMachine
{
    // 장비를 인벤토리에 넣어준다 — EVT-005의 "금발이 새 장비 '가위'를 넘겨줌".
    // 지급만 하고 장착은 하지 않는다(플레이어가 카드에 직접 끼우는 흐름을 유지).
    [System.Serializable]
    public class GrantGearAction : StateAction
    {
        public GearDataSO gear;

        public override void Act(StateController stateController)
        {
            if (!gear)
            {
                Debug.LogError("ERROR: GrantGearAction - gear가 비어 있습니다.");
                return;
            }

            GameManager.instance.inventoryManager.AddGear(gear);
        }
    }
}
