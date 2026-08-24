using System;
using System.Collections;
using StateMachine;
using UnityEditor;
using UnityEngine;

[Serializable]
public struct ActionSequence
{
    public float time;
    [SerializeReference, SubclassSelector] public StateAction Action;
}

[Serializable]
public struct ExposedVariable
{
    [HideInInspector] public string propertyPath;
    public string displayName;
}

[CreateAssetMenu(fileName = "State", menuName = "State Machine/State")]
public class StateSO : ScriptableObject
{
    [HideInInspector] public bool isPacked = false;   // 패킹 여부
    [HideInInspector] public bool isTemplate = false; // 생성 메뉴에 띄울 템플릿 여부
    public System.Collections.Generic.List<ExposedVariable> exposedVariables = new(); // 노출할 변수 목록

    [NaughtyAttributes.Button]
    public void Save()
    {
        AssetDatabase.SaveAssets();
    }
    [HideInInspector] public float temporaryID;

    // 배열은 반드시 비어 있는 상태로라도 존재해야 한다 — 그래프 편집기가 CreateInstance로 만든
    // State는 에셋으로 저장·재로드되기 전까지 이 필드들이 null이라, .Length에서 그대로 터진다.
    public StateTransition[] transitions = System.Array.Empty<StateTransition>();
    public StateTransition[] additionalTransitions = System.Array.Empty<StateTransition>();
    [SerializeReference, SubclassSelector] public StateAction[] EnterActions = System.Array.Empty<StateAction>();
    [SerializeReference, SubclassSelector] public StateAction[] UpdateActions = System.Array.Empty<StateAction>();
    [SerializeReference, SubclassSelector] public StateAction[] ExitActions = System.Array.Empty<StateAction>();
    public ActionSequence[] actionSequence = System.Array.Empty<ActionSequence>();
    public bool loopActionSequence;
    public bool useTimer;
    [NaughtyAttributes.ShowIf("useTimer")][NaughtyAttributes.MinValue(0)][NaughtyAttributes.MaxValue(9)] public int timerID;
    [NaughtyAttributes.ShowIf("useTimer")] public float time;

    // 액션 목록에는 빈 칸이 섞일 수 있다. 그래프 편집기에서 슬롯만 늘리고 클래스를 안 고르면
    // SerializeReference가 null(rid: -2)로 저장되고, 실제로 Delta_Roza_TransformStartState의
    // updateActions에 그런 칸이 하나 있다. UpdateState는 CheckDecision 바로 앞에서 도는 터라
    // 여기서 터지면 그 State의 전이가 한 번도 평가되지 못하고 캐릭터가 멈춘 것처럼 보인다.
    public virtual void EnterState(StateController stateController)
    {
        for (int i = 0; i < EnterActions.Length; i++)
        {
            EnterActions[i]?.Act(stateController);
        }
        //ReserveFrameDecisions(stateController);
        ReserveTransitions(stateController);
        if (useTimer) stateController.Timers[timerID].StartTimer(time);
        if (actionSequence.Length > 0) stateController.StartActionSequence(actionSequence, loopActionSequence);
    }

    public void ReserveTransitions(StateController stateController)
    {
        // 아래 루프와 CheckDecision/ResetTimers가 전이 인덱스로 리스트를 건드린다.
        // 이 State에 진입하는 지금이 그만큼 자리를 확보해 둘 자리다.
        stateController.EnsureTransitionCapacity(transitions.Length);

        for (int i = 0; i < transitions.Length; i++)
        {
            stateController.TransitionConditions[i] = false;
            stateController.TransitionSequences[i] = stateController.StartCoroutine(TransitionWaitAvailableCoroutine(i, stateController));
            stateController.TransitionSequences[i] = stateController.StartCoroutine(TransitionWaitDisableCoroutine(i, stateController));
        }
    }

    public virtual void UpdateState(StateController stateController)
    {
        for (int i = 0; i < UpdateActions.Length; i++)
            UpdateActions[i]?.Act(stateController);
    }

    public IEnumerator TransitionWaitAvailableCoroutine(int i, StateController stateController)
    {
        if (transitions[i].availableTime > 0)
            yield return new WaitForSeconds(transitions[i].availableTime);
        stateController.TransitionConditions[i] = true;
    }

    public IEnumerator TransitionWaitDisableCoroutine(int i, StateController stateController)
    {
        if (transitions[i].disableTime > 0)
        {
            yield return new WaitForSeconds(transitions[i].disableTime);
            stateController.TransitionConditions[i] = false;
        }
    }

    public virtual void ExitState(StateController stateController)
    {
        if (actionSequence.Length > 0) stateController.StopActionSequence();
        for (int i = 0; i < ExitActions.Length; i++)
        {
            ExitActions[i]?.Act(stateController);
        }
        ResetTimers(stateController);
    }

    private void ResetTimers(StateController stateController)
    {
        for (int i = 0; i < transitions.Length; i++)
            if (stateController.TransitionConditions[i])
                stateController.TransitionConditions[i] = false;

        stateController.StopAllCoroutines();
    }

    public void ResetStateTimer(StateController stateController)
    {
        ResetTimers(stateController);
        ReserveTransitions(stateController);
    }

    public void CheckDecision(StateController stateController)
    {
        for (int i = 0; i < transitions.Length; i++)
        {
            if (transitions[i].activationInput != EnumManager.InputType.None) continue;
            if (!stateController.TransitionConditions[i]) continue;

            if (transitions[i].CheckDecisions(stateController))
            {
                transitions[i].Action?.Act(stateController);
                if (transitions[i].trueState) { stateController.ChangeState(transitions[i].trueState); break; }
            }
            else
            {
                if (transitions[i].falseState) { stateController.ChangeState(transitions[i].falseState); break; }
            }
        }
    }

    public void CheckInputDecision(StateController stateController, EnumManager.InputType inputType)
    {
        for (int i = 0; i < transitions.Length; i++)
        {
            if (transitions[i].activationInput == EnumManager.InputType.None) continue;
            if (!stateController.TransitionConditions[i]) continue;
            if (inputType != transitions[i].activationInput) continue;

            if (transitions[i].CheckDecisions(stateController))
            {
                transitions[i].Action?.Act(stateController);
                if (transitions[i].trueState) { stateController.ChangeState(transitions[i].trueState); break; }
            }
            else
            {
                if (transitions[i].falseState) { stateController.ChangeState(transitions[i].falseState); break; }
            }
        }
    }
}
