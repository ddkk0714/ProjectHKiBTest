using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class EventFlagEqualDecision : StateDecision
    {
        [SerializeField] private EventFlagSO flag;
        [SerializeField] private int value;
        public override bool Decide(StateController stateController)
        {
            if (GameManager.instance.eventManager.TryGetEventFlag(flag, out int v))
                return v == value;
            return false;
        }
    }

    [System.Serializable]
    public class EventFlagGreaterDecision : StateDecision
    {
        [SerializeField] private EventFlagSO flag;
        [SerializeField] private int value;
        public override bool Decide(StateController stateController)
        {
            if (GameManager.instance.eventManager.TryGetEventFlag(flag, out int v))
                return v > value;
            return false;
        }
    }

    [System.Serializable]
    public class EventFlagLessDecision : StateDecision
    {
        [SerializeField] private EventFlagSO flag;
        [SerializeField] private int value;
        public override bool Decide(StateController stateController)
        {
            if (GameManager.instance.eventManager.TryGetEventFlag(flag, out int v))
                return v < value;
            return false;
        }
    }
}