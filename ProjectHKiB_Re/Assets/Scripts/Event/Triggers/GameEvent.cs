using UnityEngine;

public enum GameEventRejectReason
{
    None,
    MissingGameManager,
    MissingEventManager,
    MissingEventAsset,
    MissingEventFlag,
    WrongWorld,
    FlagMismatch,
    MissingRouteProgress,
    MissingClue,
    EventAlreadyRunning,
}

/// <summary>
/// GameEvent가 실제 처리를 수행했는지, 거부했다면 왜 거부했는지를 호출자에게 돌려준다.
/// 기존 void TriggerEvent 호출부와 공존하기 위한 비파괴 결과 API다.
/// </summary>
public readonly struct GameEventExecutionResult
{
    public bool Succeeded { get; }
    public GameEventRejectReason RejectReason { get; }
    public string Detail { get; }

    private GameEventExecutionResult(bool succeeded, GameEventRejectReason rejectReason, string detail)
    {
        Succeeded = succeeded;
        RejectReason = rejectReason;
        Detail = detail ?? string.Empty;
    }

    public static GameEventExecutionResult Success()
        => new GameEventExecutionResult(true, GameEventRejectReason.None, string.Empty);

    public static GameEventExecutionResult Rejected(GameEventRejectReason reason, string detail)
        => new GameEventExecutionResult(false, reason, detail);
}

public abstract class GameEvent : MonoBehaviour
{
    /// <summary>
    /// 결과를 요구하는 새 트리거 경로. 단순 GameEvent는 기존 TriggerEvent 실행을 성공으로 취급하고,
    /// 조건이 있는 구현은 재정의해 실제 거부 사유를 반환한다.
    /// </summary>
    public virtual GameEventExecutionResult TryTriggerEvent()
    {
        TriggerEvent();
        return GameEventExecutionResult.Success();
    }

    // start event by enabling controller update
    public abstract void TriggerEvent();
}
