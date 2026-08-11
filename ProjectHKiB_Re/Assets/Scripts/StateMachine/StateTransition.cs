using StateMachine;
using UnityEngine;
using UnityEngine.InputSystem;

[System.Serializable]
public class StateTransition
{
    public string name;
    [System.Serializable]
    public struct DecisionSet
    {
        [SerializeReference, SubclassSelector] public StateDecision Decision;
        public bool negate;
    }

    public EnumManager.InputType activationInput = EnumManager.InputType.None;
    public InputActionReference trigger;
    public EnumManager.InputActionType type;
    public float availableTime;
    public float disableTime;
    public DecisionSet[] decisions;
    public StateSO trueState;
    public StateSO falseState;
    [SerializeReference, SubclassSelector] public StateAction Action;

    public bool showTrueStatePort = true;
    public bool showFalseStatePort = true;

    public bool CheckDecisions(StateController stateController)
    {
        for (int j = 0; j < decisions.Length; j++)
        {
            // 빈 Decision 칸은 조건 없음으로 본다. 여기서 터지면 CheckDecision 루프가 통째로
            // 죽어서 그 State의 다른 전이까지 같이 막힌다 - StateSO의 액션 목록과 같은 사정이다.
            if (decisions[j].Decision == null) continue;
            if (!decisions[j].Decision.Decide(stateController) ^ decisions[j].negate)
                return false;
        }
        return true;
    }
}