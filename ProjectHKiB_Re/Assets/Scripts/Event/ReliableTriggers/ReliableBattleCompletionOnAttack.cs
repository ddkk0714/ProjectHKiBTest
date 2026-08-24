using UnityEngine;

/// <summary>
/// ReliableEventAttackSensor가 감지한 공격을 현재 이벤트의 완료 bool로 전달합니다.
///
/// 사용법:
/// 1. 같은 엔티티에 ReliableGameEventTrigger와 ReliableEventAttackSensor를 붙입니다.
/// 2. Trigger Filter의 Attack Only를 켭니다.
/// 3. 이 컴포넌트에 완료할 EventSO와 해당 이벤트의 bool 이름을 지정합니다.
///
/// 지정한 이벤트가 실제로 진행 중일 때만 완료하므로, 전투 시작 전의 우발적인 공격은 무시됩니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Reliable Battle Completion On Attack")]
public sealed class ReliableBattleCompletionOnAttack : MonoBehaviour
{
    [Header("Attack source")]
    [SerializeField] private ReliableGameEventTrigger trigger;

    [Header("Event completion")]
    [Tooltip("이 EventSO가 현재 EventManager에서 진행 중일 때만 완료 신호를 보냅니다.")]
    [SerializeField] private EventSO requiredEvent;
    [Tooltip("완료 대기 State가 확인하는 Custom Bool 이름입니다.")]
    [SerializeField] private string completionBoolName;
    [SerializeField] private bool disableTriggerAfterCompletion = true;

    private bool completed;

    private void Awake()
    {
        if (!trigger) trigger = GetComponent<ReliableGameEventTrigger>();
    }

    private void OnEnable()
    {
        if (trigger) trigger.Triggered += HandleAttackTriggered;
    }

    private void OnDisable()
    {
        if (trigger) trigger.Triggered -= HandleAttackTriggered;
    }

    private void HandleAttackTriggered(ReliableEventTriggerContext context)
    {
        if (completed || string.IsNullOrWhiteSpace(completionBoolName)) return;

        EventManager eventManager = GameManager.instance ? GameManager.instance.eventManager : null;
        if (eventManager == null) return;

        // 공격 트리거는 맵에 상시 존재할 수 있으므로, 현재 진행 중인 이벤트까지 확인한다.
        if (requiredEvent && eventManager.StateMachine != requiredEvent) return;

        eventManager.SetBoolParameterTrue(completionBoolName);
        completed = true;

        if (disableTriggerAfterCompletion && trigger)
            trigger.enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!trigger) trigger = GetComponent<ReliableGameEventTrigger>();
    }
#endif
}
