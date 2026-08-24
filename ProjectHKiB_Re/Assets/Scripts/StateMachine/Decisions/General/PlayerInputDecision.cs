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

            // InputActionReference는 원본 .inputactions 자산의 액션이다. InputManager가 켠
            // 런타임 액션 인스턴스를 읽어야 변신으로 교체된 상태 머신도 이동 전이를 받는다.
            InputAction runtimeAction = GameManager.instance != null && GameManager.instance.inputManager != null
                ? GameManager.instance.inputManager.GetRuntimeAction(_trigger)
                : null;
            InputAction action = runtimeAction ?? _trigger.action;
            if (action == null) return false;

            return _type switch
            {
                EnumManager.InputProcessType.InProgress => action.inProgress,
                EnumManager.InputProcessType.Triggered => action.triggered,
                EnumManager.InputProcessType.Enabled => action.enabled,
                EnumManager.InputProcessType.WasPerformedThisFrame => action.WasPerformedThisFrame(),
                EnumManager.InputProcessType.WasPressedThisFrame => action.WasPressedThisFrame(),
                EnumManager.InputProcessType.WasReleasedThisFrame => action.WasReleasedThisFrame(),
                _ => false,
            };
            //return GameManager.instance.inputManager.GetInputByEnum(_inputType);
        }
    }
}
