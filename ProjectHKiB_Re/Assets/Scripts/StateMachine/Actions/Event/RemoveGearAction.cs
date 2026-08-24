using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StateMachine
{
    [System.Serializable]
    public class RemoveGearAction : StateAction
    {
        public GearDataSO gear;
        public bool deactivateIfActive = true;

        public override void Act(StateController stateController)
        {
            // InventoryManager.RemoveGear(gear, deactivateIfActive);
        }
    }
}
