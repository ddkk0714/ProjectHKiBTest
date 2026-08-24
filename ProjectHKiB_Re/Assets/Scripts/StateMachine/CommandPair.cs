using System;
using UnityEngine.InputSystem;

[Serializable]
public class CommandPair
{
    public CommandPair(StateSO conditionState, EnumManager.InputType triggerInput, InputActionReference trigger, EnumManager.InputActionType type)
    {
        this.conditionState = conditionState;
        this.triggerInput = triggerInput;
        this.trigger = trigger;
        this.type = type;
    }
    public StateSO conditionState;
    public EnumManager.InputType triggerInput;
    public InputActionReference trigger;
    public EnumManager.InputActionType type;

    private Action<InputAction.CallbackContext> _cachedBindFunction;
    private InputAction _boundAction;

    public void Bind(StateController stateController)
    {
        if (_cachedBindFunction != null) return;

        _cachedBindFunction = (context) =>
        {
            if (stateController.CurrentState == conditionState)
                stateController.CurrentState.CheckInputDecision(stateController, triggerInput);
        };

        InputManager inputManager = GameManager.instance.inputManager;
        _boundAction = inputManager != null ? inputManager.GetRuntimeAction(trigger) : null;
        // 이벤트 등 런타임 입력 컬렉션에 없는 참조는 기존 자산 액션을 그대로 사용한다.
        _boundAction ??= trigger ? trigger.action : null;

        if (_boundAction == null) return;

        switch (type)
        {
            case EnumManager.InputActionType.Performed: _boundAction.performed += _cachedBindFunction; break;
            case EnumManager.InputActionType.Started: _boundAction.started += _cachedBindFunction; break;
            case EnumManager.InputActionType.Canceled: _boundAction.canceled += _cachedBindFunction; break;
        }
    }

    public void Unbind()
    {
        switch (type)
        {
            case EnumManager.InputActionType.Performed: if (_boundAction != null) _boundAction.performed -= _cachedBindFunction; break;
            case EnumManager.InputActionType.Started: if (_boundAction != null) _boundAction.started -= _cachedBindFunction; break;
            case EnumManager.InputActionType.Canceled: if (_boundAction != null) _boundAction.canceled -= _cachedBindFunction; break;
        }
        _cachedBindFunction = null;
        _boundAction = null;
    }

}
