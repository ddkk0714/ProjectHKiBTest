using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using System;

public class StateController : InterfaceRegister
{
    [HideInInspector] public CustomVariableSets customVariables = new();
    [NaughtyAttributes.ReadOnly][SerializeField] protected StateSO _currentState;
    [NaughtyAttributes.ReadOnly][SerializeField] protected StateMachineSO _stateMachine;
    public StateSO CurrentState
    {
        get => _currentState;
        protected set
        {
            if (value != _currentState)
                _currentState = value;
            //Debug.Log(_currentState);
        }
    }
    public StateMachineSO StateMachine => _stateMachine;
    [HideInInspector] public List<Coroutine> TransitionSequences = new(10);
    [HideInInspector] public List<bool> TransitionConditions = new(10);
    [HideInInspector] public List<Timer> Timers = new(10);
    private Sequence _actionSequence;
    private ActionSequence[] _currentActionSequence;
    private int _sequenceInt;
    public void StartActionSequence(ActionSequence[] actionSequence, bool loop)
    {
        if (_actionSequence != null && _actionSequence.active) _actionSequence.Kill();
        _sequenceInt = 0;
        _actionSequence = DOTween.Sequence();
        _currentActionSequence = actionSequence;
        _actionSequence.AppendCallback(ActionSequenceResetCallback);
        for (int i = 0; i < actionSequence.Length; i++)
        {
            _actionSequence.AppendInterval(actionSequence[i].time);
            _actionSequence.AppendCallback(ActionSequenceCallback);
        }
        if (loop) _actionSequence.SetLoops(-1, LoopType.Restart);
        _actionSequence.Play();
    }
    public void ActionSequenceResetCallback() => _sequenceInt = 0;
    public void ActionSequenceCallback()
    {
        _currentActionSequence[_sequenceInt].Action?.Act(this);
        _sequenceInt++;
    }
    public void StopActionSequence()
    {
        if (_actionSequence != null && _actionSequence.active) _actionSequence.Kill();
        _sequenceInt = 0;
    }

    public virtual void Awake()
    {
        for (int i = 0; i < 10; i++)
        {
            TransitionSequences.Add(null);
            TransitionConditions.Add(false);
            Timers.Add(new());
        }
    }

    public virtual void Start()
    {
        Initialize();
    }

    public virtual void Initialize()
    {
        RegisterModules(transform);
    }

    public virtual void ChangeState(StateSO state)
    {
        CurrentState.ExitState(this);
        CurrentState = state;
        CurrentState.EnterState(this);
    }

    public virtual void ChangeState(string stateName)
    {
        StateSO targetState = StateMachine.allStates.Find(a => a.name == stateName);
        if (targetState) ChangeState(targetState);
    }

    public virtual void UpdateState()
    {
        CurrentState.UpdateState(this);
        CurrentState.CheckDecision(this);
    }

    public void Update()
    {
        if (this.enabled && CurrentState)
            UpdateState();
    }

    public void Initialize(StateMachineSO stateMachine)
    {
        ResetStateMachine(stateMachine);
        customVariables = stateMachine.customVariables;
        //////
        ///  HAVE TO FIX THIS NOT TO DEEP REFERENCE CUSTOMVARS!!!
        //////
    }

    public void ResetStateMachine(StateMachineSO stateMachine)
    {
        if (stateMachine == null)
        {
            Debug.LogError("ERROR: StateMachine Missing!!!");
            return;
        }
        stateMachine.UnbindCommands();
        _stateMachine = stateMachine;
        stateMachine.BindCommands(this);
        CurrentState = stateMachine.initialState;
        CurrentState.EnterState(this);
    }

    public void EliminateStateMachine()
    {
        if (_stateMachine) _stateMachine.UnbindCommands();
        _stateMachine = null;
        if (CurrentState) CurrentState.ExitState(this);
        CurrentState = null;
        StopAllCoroutines();
    }

    public void SetBoolParameterTrue(string name)
    {
        if (!customVariables.boolVariables.ContainsKey(name))
        {
            Debug.LogWarning("Warning: Generated missing variable: " + name);
            customVariables.boolVariables[name] = new() { Value = true };
        }
        else
            customVariables.boolVariables[name].Value = true;
    }

    public void SetBoolParameterFalse(string name)
    {
        if (!customVariables.boolVariables.ContainsKey(name))
        {
            Debug.LogWarning("Warning: Generated missing variable: " + name);
            customVariables.boolVariables[name] = new() { Value = false };
        }
        else
            customVariables.boolVariables[name].Value = false;
    }

    public void SetIntParameter(string name, int value)
    {
        if (!customVariables.intVariables.ContainsKey(name))
        {
            Debug.LogWarning("Warning: Generated missing variable: " + name);
            customVariables.intVariables[name] = new() { Value = value };
        }
        else
            customVariables.intVariables[name].Value = value;
    }

    public void IncrementIntParameter(string name, int value)
    {
        if (!customVariables.intVariables.ContainsKey(name))
        {
            Debug.LogWarning("Warning: Generated missing variable: " + name);
            customVariables.intVariables[name] = new() { Value = value };
        }
        else
            customVariables.intVariables[name].Value += value;
    }

    public bool GetBoolParameter(string name)
    {
        if (!customVariables.boolVariables.ContainsKey(name))
        {
            Debug.LogWarning("Warning: Generated missing variable: " + name);
            customVariables.boolVariables[name] = new() { Value = false };
        }

        return customVariables.boolVariables[name].Value;
    }

    public int GetIntParameter(string name)
    {
        if (!customVariables.intVariables.ContainsKey(name))
        {
            Debug.LogWarning("Warning: Generated missing variable: " + name);
            customVariables.intVariables[name] = new() { Value = 0 };
        }

        return customVariables.intVariables[name].Value;
    }
}