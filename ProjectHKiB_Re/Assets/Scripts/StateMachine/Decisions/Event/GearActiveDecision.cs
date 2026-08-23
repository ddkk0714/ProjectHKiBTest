using UnityEngine;

namespace StateMachine
{
    /// <summary>
    /// 지정한 기어가 현재 활성화(변신) 상태인지 판정한다.
    /// EVT-006에서는 Lily GearData를 가위 대역으로 사용한다.
    /// </summary>
    [System.Serializable]
    public class GearActiveDecision : StateDecision
    {
        public GearDataSO gear;

        public override bool Decide(StateController stateController)
        {
            GearManager gearManager = GameManager.instance != null ? GameManager.instance.gearManager : null;
            if (gear == null || gearManager == null || gearManager.activeGear == null)
                return false;

            return gearManager.activeGear.Exists(activeGear => activeGear != null && activeGear.data == gear);
        }
    }
}
