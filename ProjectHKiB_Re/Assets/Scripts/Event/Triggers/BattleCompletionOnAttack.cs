using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 트리거가 감지한 공격을 현재 진행 중인 이벤트의 완료 Custom Bool로 전달합니다.
/// 전투 시스템이 아직 완료 신호를 직접 보내지 않는 단계에서, 씬에 놓인 공격 트리거를
/// 그 신호로 대신 쓰기 위한 다리 역할입니다.
/// </summary>
/// <remarks>
/// 사용법: 같은 오브젝트에 AttackEventTrigger와 EventAttackSensor를 붙이고, 이 컴포넌트에
/// 완료를 알릴 EventSO와 해당 이벤트가 기다리는 Custom Bool 이름을 지정합니다.
/// 지정한 이벤트가 실제로 진행 중일 때만 완료하므로 전투 시작 전의 우발적인 공격은 무시됩니다.
/// </remarks>
public sealed class BattleCompletionOnAttack : MonoBehaviour
{
    [Tooltip("완료 신호로 쓸 트리거입니다. 비워 두면 같은 오브젝트에서 자동으로 찾습니다.")]
    [SerializeField, FormerlySerializedAs("trigger")]
    private EventTriggerBase _trigger;

    [Tooltip("이 EventSO가 현재 EventManager에서 진행 중일 때만 완료 신호를 보냅니다.")]
    [SerializeField, FormerlySerializedAs("requiredEvent")]
    private EventSO _requiredEvent;

    [Tooltip("완료 대기 State가 확인하는 Custom Bool 이름입니다.")]
    [SerializeField, FormerlySerializedAs("completionBoolName")]
    private string _completionBoolName;

    [Tooltip("켜면 완료를 알린 뒤 트리거를 비활성화합니다.")]
    [SerializeField, FormerlySerializedAs("disableTriggerAfterCompletion")]
    private bool _disableTriggerAfterCompletion = true;

    private bool _completed;

    private void Awake()
    {
        if (!_trigger) _trigger = GetComponent<EventTriggerBase>();
    }

    private void OnEnable()
    {
        if (_trigger) _trigger.Triggered += HandleTriggered;
    }

    private void OnDisable()
    {
        if (_trigger) _trigger.Triggered -= HandleTriggered;
    }

    /// <summary>
    /// 트리거가 발동했을 때 진행 중인 이벤트를 확인하고 완료 Bool을 한 번만 설정합니다.
    /// 조건이 맞지 않아 넘긴 경우도 남겨, 완료 신호가 오지 않는 원인을 로그에서 찾을 수 있게 합니다.
    /// </summary>
    private void HandleTriggered(EventTriggerContext context)
    {
        if (_completed || string.IsNullOrWhiteSpace(_completionBoolName)) return;

        EventManager eventManager = GameManager.instance ? GameManager.instance.eventManager : null;
        if (eventManager == null) return;

        // 공격 트리거는 맵에 상시 존재할 수 있으므로 현재 진행 중인 이벤트까지 확인한다.
        if (_requiredEvent && eventManager.StateMachine != _requiredEvent)
        {
            Debug.Log($"[BattleCompletionOnAttack] '{_completionBoolName}' 완료 신호를 넘깁니다 — " +
                      $"'{_requiredEvent.name}'이 진행 중이 아닙니다(현재: " +
                      $"{(eventManager.StateMachine ? eventManager.StateMachine.name : "없음")}).", this);
            return;
        }

        eventManager.SetBoolParameterTrue(_completionBoolName);
        _completed = true;
        context?.SuppressGameEvent($"'{_completionBoolName}' 외부 완료 신호로 공격을 소비했습니다.");
        Debug.Log($"[BattleCompletionOnAttack] '{_completionBoolName}'를 true로 설정했습니다.", this);

        if (_disableTriggerAfterCompletion && _trigger) _trigger.enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!_trigger) _trigger = GetComponent<EventTriggerBase>();
    }
#endif
}
