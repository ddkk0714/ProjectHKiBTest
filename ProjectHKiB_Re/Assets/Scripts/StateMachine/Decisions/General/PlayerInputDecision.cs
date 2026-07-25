using UnityEngine;
using UnityEngine.InputSystem;
namespace StateMachine
{
    [System.Serializable]
    public class PlayerInputDecision : StateDecision
    {
        [SerializeField] private InputActionReference _trigger;
        [SerializeField, EnumDropdown(typeof(EnumManager.InputProcessType))] private EnumManager.InputProcessType _type;
        [SerializeField, EnumDropdown(typeof(EnumManager.InputType))] private EnumManager.InputType _inputType;
        public override bool Decide(StateController stateController)
        {
            if (_trigger == null) return false;
            return _type switch
            {
                EnumManager.InputProcessType.InProgress => _trigger.action.inProgress,
                EnumManager.InputProcessType.Triggered => _trigger.action.triggered,
                EnumManager.InputProcessType.Enabled => _trigger.action.enabled,
                EnumManager.InputProcessType.WasPerformedThisFrame => _trigger.action.WasPerformedThisFrame(),
                EnumManager.InputProcessType.WasPressedThisFrame => _trigger.action.WasPressedThisFrame(),
                EnumManager.InputProcessType.WasReleasedThisFrame => _trigger.action.WasReleasedThisFrame(),
                _ => false,
            };
            //return GameManager.instance.inputManager.GetInputByEnum(_inputType);
        }
    }
}
