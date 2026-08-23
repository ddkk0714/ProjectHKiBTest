using UnityEngine;
namespace StateMachine
{
    // 죽어서 꺼진 대상을 되살린다 — 사망 후 리스폰 이벤트에서 쓴다.
    //
    // DamagableModule.Die()가 gameObject.SetActive(false)까지 해버리므로, 위치를 옮기거나
    // 상태를 되돌리기 전에 먼저 이걸로 켜야 나머지 액션이 먹는다.
    // healToFull을 켜면 최대 체력까지 회복시킨다(Heal은 현재 HP에 더하는 방식이라 최대치를 넘겨도
    // 내부에서 clamp된다).
    [System.Serializable]
    public class ReviveEntityAction : StateAction
    {
        public bool healToFull = true;

        public override void Act(StateController stateController)
        {
            stateController.gameObject.SetActive(true);

            if (!healToFull) return;

            if (!stateController.TryGetInterface(out IDamagable damagable))
            {
                Debug.LogWarning("[ReviveEntityAction] IDamagable을 찾을 수 없어 체력을 회복시키지 못했습니다.");
                return;
            }

            damagable.Heal(Mathf.CeilToInt(damagable.MaxHP));
        }
    }
}
