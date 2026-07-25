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

    public void Bind(StateController stateController)
    {
        if (_cachedBindFunction != null) return;

        _cachedBindFunction = (context) =>
        {
            if (stateController.CurrentState == conditionState)
                stateController.CurrentState.CheckInputDecision(stateController, triggerInput);
        };

        InputManager inputManager = GameManager.instance.inputManager;

        switch (type)
        {
            case EnumManager.InputActionType.Performed: if (trigger) trigger.action.performed += _cachedBindFunction; break;
            case EnumManager.InputActionType.Started: if (trigger) trigger.action.started += _cachedBindFunction; break;
            case EnumManager.InputActionType.Canceled: if (trigger) trigger.action.canceled += _cachedBindFunction; break;
        }
    }

    public void Unbind()
    {
        switch (type)
        {
            case EnumManager.InputActionType.Performed: if (trigger) trigger.action.performed -= _cachedBindFunction; break;
            case EnumManager.InputActionType.Started: if (trigger) trigger.action.started -= _cachedBindFunction; break;
            case EnumManager.InputActionType.Canceled: if (trigger) trigger.action.canceled -= _cachedBindFunction; break;
        }
        _cachedBindFunction = null;
    }

}