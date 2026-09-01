using UnityEngine;

public class GameEventSetFlag : GameEvent
{
    [SerializeField] private EventFlagSO eventFlag;
    [SerializeField] private bool increment;
    [SerializeField] private int value;

    public override GameEventExecutionResult TryTriggerEvent()
    {
        if (!eventFlag)
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.MissingEventFlag,
                "GameEventSetFlag에 EventFlagSO가 연결되지 않았습니다.");
        if (!GameManager.instance)
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.MissingGameManager,
                "GameManager 인스턴스가 없습니다.");

        EventManager e = GameManager.instance.eventManager;
        if (!e)
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.MissingEventManager,
                "GameManager에 EventManager가 연결되지 않았습니다.");

        if (increment && e.TryGetEventFlag(eventFlag, out int v))
            e.SetEventFlag(eventFlag, v + value);
        else
            e.SetEventFlag(eventFlag, value);

        return GameEventExecutionResult.Success();
    }

    // 기존 직접 호출부 호환.
    public override void TriggerEvent()
    {
        TryTriggerEvent();
    }
}
