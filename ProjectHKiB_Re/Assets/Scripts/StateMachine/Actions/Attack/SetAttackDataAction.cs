using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class SetAttackDataAction : StateAction
    {
        [SerializeField] private int attackNumber;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IAttackable attackable))
            {
                attackable.SetAttackData(attackNumber);

                // 공격이 시작되는 지점이다(모든 공격 State가 이걸 EnterActions에 갖고 있다).
                // 걷던 속도를 여기서 끊어야 공격 중에 원래 속도로 미끄러지지 않고,
                // AttackMove가 의도한 거리만큼만 움직인다.
                if (stateController.TryGetInterface(out IPhysics movable)) movable.StopMove();
            }
            else
                Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}