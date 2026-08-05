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

    public StateTransition[] transitions;
    public StateTransition[] additionalTransitions;
    [SerializeReference, SubclassSelector] public StateAction[] EnterActions;
    [SerializeReference, SubclassSelector] public StateAction[] UpdateActions;
    [SerializeReference, SubclassSelector] public StateAction[] ExitActions;
    public ActionSequence[] actionSequence;
    public bool loopActionSequence;
    public bool useTimer;
    [NaughtyAttributes.ShowIf("useTimer")][NaughtyAttributes.MinValue(0)][NaughtyAttributes.MaxValue(9)] public int timerID;
    [NaughtyAttributes.ShowIf("useTimer")] public float time;

    public virtual void EnterState(StateController stateController)
    {
        for (int i = 0; i < EnterActions.Length; i++)
        {
            EnterActions[i].Act(stateController);
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
            UpdateActions[i].Act(stateController);
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
            ExitActions[i].Act(stateController);
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
