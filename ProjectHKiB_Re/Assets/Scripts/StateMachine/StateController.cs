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
        // Timers는 StateSO.timerID로 접근하고 그 값이 0~9로 제한돼 있어(MinValue/MaxValue) 10개면 충분하다.
        // TransitionConditions/TransitionSequences는 전이 인덱스로 접근하므로 State에 따라 더 필요할 수
        // 있다 — 부족한 만큼은 EnsureTransitionCapacity가 채운다.
        for (int i = 0; i < 10; i++)
        {
            TransitionSequences.Add(null);
            TransitionConditions.Add(false);
            Timers.Add(new());
        }
    }

    /// <summary>
    /// 전이 인덱스로 접근하는 두 리스트가 최소 count개는 되도록 보장한다.
    /// Timers는 timerID로 접근하는 별개의 축이라 여기서 늘리지 않는다.
    ///
    /// 전에는 Awake가 채우는 10개가 전부여서, 전이가 10개를 넘는 State에 진입하면
    /// StateSO.ReserveTransitions가 TransitionConditions[10]을 건드리며 터졌다
    /// (Delta_Lily_NormalAttack4~8State가 전이 11개다).
    /// </summary>
    public void EnsureTransitionCapacity(int count)
    {
        while (TransitionConditions.Count < count)
        {
            TransitionConditions.Add(false);
            TransitionSequences.Add(null);
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
        // customVariables를 **먼저** 바꿔야 한다. ResetStateMachine은 그 자리에서 초기 State의
        // 진입 액션을 실행하는데, 예전 순서에서는 그 액션이 쓴 값이 전부 직전 상태 기계의 저장소로
        // 들어갔다가 바로 다음 줄의 대입으로 통째로 버려졌다. 그리고 읽는 쪽은 새 저장소를 보므로,
        // "진입할 때 쓰고 곧바로 읽는" 값(이벤트 단계 타임아웃의 시각 표식 등)이 항상 어긋났다.
        //
        // 게다가 이 대입은 SO의 객체를 그대로 참조로 물어간다(아래 경고). 그래서 읽는 쪽이 보는 값은
        // 0이 아니라 **지난 플레이에서 남은 값**이었다 — 이벤트 단계가 지난 판의 시각을 기준으로
        // 기다리는 바람에, 같은 이벤트인데도 진입한 시점에 따라 대기 시간이 제멋대로 달라졌다.
        customVariables = stateMachine.customVariables;
        ResetStateMachine(stateMachine);
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