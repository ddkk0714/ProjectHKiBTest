using UnityEngine;
namespace StateMachine
{
    // 아이템을 인벤토리에 넣어준다. 이벤트 보상·연출 소품 지급용.
    [System.Serializable]
    public class GrantItemAction : StateAction
    {
        public ItemDataSO item;
        [Min(1)] public int count = 1;

        public override void Act(StateController stateController)
        {
            if (!item)
            {
                Debug.LogError("ERROR: GrantItemAction - item이 비어 있습니다.");
                return;
            }

            GameManager.instance.inventoryManager.AddItem(item, count);
        }
    }
}
