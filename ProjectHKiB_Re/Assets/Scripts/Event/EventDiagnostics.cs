using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 이벤트 시스템의 실행 결과를 Unity Console과 Player.log에 남긴다.
/// 화면 UI는 만들지 않으며 Editor 또는 Development Build에서만 상세 로그를 출력한다.
/// </summary>
public static class EventDiagnostics
{
    private const string Prefix = "[EventTrace]";
    private static readonly Dictionary<int, string> LastTriggerSignatures = new Dictionary<int, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        LastTriggerSignatures.Clear();
    }

    public static void LogTriggerResult(EventTriggerBase trigger, EventTriggerResult result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!trigger || result == null) return;

        string signature = $"{result.Status}|{result.TriggerRejectReason}|{result.GameEventRejectReason}";
        int instanceId = trigger.GetInstanceID();
        bool suppressRepeated = result.Status == EventTriggerResultStatus.Rejected ||
                                result.Status == EventTriggerResultStatus.GameEventRejected ||
                                result.Status == EventTriggerResultStatus.SignalAccepted;
        if (suppressRepeated && LastTriggerSignatures.TryGetValue(instanceId, out string previous) &&
            previous == signature) return;
        LastTriggerSignatures[instanceId] = signature;

        string target = result.Context?.Target ? result.Context.Target.name : "없음";
        string gameEvent = trigger.GameEvent ? trigger.GameEvent.name : "없음";
        string message = $"{Prefix}[Trigger] '{trigger.name}' => {result.Status} " +
                         $"(GameEvent: {gameEvent}, Target: {target}, " +
                         $"TriggerReason: {result.TriggerRejectReason}, " +
                         $"GameEventReason: {result.GameEventRejectReason})";
        if (!string.IsNullOrEmpty(result.Detail)) message += $" — {result.Detail}";

        if (result.Status == EventTriggerResultStatus.GameEventRejected ||
            result.TriggerRejectReason == EventTriggerRejectReason.MissingGameEvent)
            Debug.LogWarning(message, trigger);
        else
            Debug.Log(message, trigger);
#endif
    }

    public static void LogDirectGameEventRejection(GameEvent gameEvent, GameEventExecutionResult result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!gameEvent || result.Succeeded) return;
        Debug.LogWarning(
            $"{Prefix}[GameEvent] 직접 실행 거부 '{gameEvent.name}' — {result.RejectReason}: {result.Detail}",
            gameEvent);
#endif
    }

    public static void LogEventStarted(EventManager manager, EventSO eventAsset, StateSO initialState)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
            $"{Prefix}[Event] 시작 '{(eventAsset ? eventAsset.name : "없음")}' " +
            $"→ State '{(initialState ? initialState.name : "없음")}'",
            manager);
#endif
    }

    public static void LogStateChanged(EventManager manager, StateSO from, StateSO to)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
            $"{Prefix}[State] '{(manager.StateMachine ? manager.StateMachine.name : "이벤트 없음")}' " +
            $"{(from ? from.name : "없음")} → {(to ? to.name : "없음")}",
            manager);
#endif
    }

    public static void LogEventCompleted(EventManager manager)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
            $"{Prefix}[Event] 완료 '{(manager.StateMachine ? manager.StateMachine.name : "이벤트 없음")}' " +
            $"(Final State: {(manager.CurrentState ? manager.CurrentState.name : "없음")})",
            manager);
#endif
    }

    public static void LogEventAborted(EventManager manager, StateSO abortedState)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning(
            $"{Prefix}[Event] 중단 '{(manager.StateMachine ? manager.StateMachine.name : "이벤트 없음")}' " +
            $"(State: {(abortedState ? abortedState.name : "없음")})",
            manager);
#endif
    }

    public static void LogFlagSet(EventManager manager, EventFlagSO flag, int value)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"{Prefix}[Flag] '{(flag ? flag.name : "null")}' = {value}", manager);
#endif
    }

    public static void LogEventStall(EventManager manager, float stateElapsedSeconds)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        StateSO state = manager.CurrentState;
        var builder = new StringBuilder();
        builder.Append($"{Prefix}[Stall] 이벤트 '{(manager.StateMachine ? manager.StateMachine.name : "없음")}'가 ");
        builder.Append($"State '{(state ? state.name : "없음")}'에서 {stateElapsedSeconds:0.0}초 동안 대기 중입니다.");

        StateTransition[] transitions = state && state.transitions != null
            ? state.transitions
            : Array.Empty<StateTransition>();
        for (int i = 0; i < transitions.Length; i++)
        {
            StateTransition transition = transitions[i];
            bool available = i < manager.TransitionConditions.Count && manager.TransitionConditions[i];
            string decisions = transition?.decisions == null
                ? "없음"
                : string.Join(", ", transition.decisions
                    .Where(set => set.Decision != null)
                    .Select(set => (set.negate ? "!" : string.Empty) + set.Decision.GetType().Name));
            builder.Append($"\n  [{i}] '{transition?.name ?? "이름 없음"}' " +
                           $"Available={available}, Decisions=[{decisions}]");
        }

        Debug.LogWarning(builder.ToString(), manager);
#endif
    }
}
