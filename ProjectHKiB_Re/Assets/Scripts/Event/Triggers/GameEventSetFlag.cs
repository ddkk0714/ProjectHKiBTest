using UnityEngine;

public class GameEventSetFlag : GameEvent
{
    [SerializeField] private EventFlagSO eventFlag;
    [SerializeField] private bool increment;
    [SerializeField] private int value;

    // start event by enabling controller update
    public override void TriggerEvent()
    {
        EventManager e = GameManager.instance.eventManager;
        if (increment && e.TryGetEventFlag(eventFlag, out int v))
            e.SetEventFlag(eventFlag, v + value);
        else
            e.SetEventFlag(eventFlag, value);
    }
}