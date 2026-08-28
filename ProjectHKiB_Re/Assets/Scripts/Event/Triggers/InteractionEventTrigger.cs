using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum InteractionEventActivation
{
    Press,
    Hold,
    ConfirmDirection
}

/// <summary>
/// 영역 안에서 InputAction 조건을 만족할 때 발동하는 상호작용 트리거입니다.
/// 단일 입력, 홀드 입력, 입력과 이동 방향의 조합을 한 역할 안에서 지원합니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Interaction Event Trigger")]
public class InteractionEventTrigger : SpatialEventTriggerBase
{
    private const string _inputActionsAssetPath = "Assets/Scripts/PlayerAction.inputactions";

    [Tooltip("상호작용 입력을 처리할 방식입니다.")]
    [SerializeField]
    private InteractionEventActivation _activation = InteractionEventActivation.Press;

    [Tooltip("감지할 Input System 액션입니다. 런타임에는 InputManager의 같은 이름 액션을 사용합니다.")]
    [SerializeField]
    [NaughtyAttributes.Required]
    private InputActionReference _inputAction;

    [Tooltip("InputAction 상태를 판정할 규칙입니다.")]
    [SerializeField, EnumDropdown(typeof(EnumManager.InputProcessType))]
    private EnumManager.InputProcessType _inputProcessType = EnumManager.InputProcessType.WasPerformedThisFrame;

    [Tooltip("Hold 모드에서 입력을 유지해야 하는 시간입니다.")]
    [SerializeField, Min(0f), FormerlySerializedAs("_holdTime")]
    [NaughtyAttributes.ShowIf(nameof(UsesHold))]
    private float _holdDuration = 1f;

    [Tooltip("Confirm Direction 모드에서 요구하는 이동 방향입니다.")]
    [SerializeField, FormerlySerializedAs("requiredDir")]
    [NaughtyAttributes.ShowIf(nameof(UsesDirection))]
    private Vector2 _requiredDirection = Vector2.down;

    [Tooltip("현재 이동 입력과 요구 방향 사이에 필요한 최소 내적입니다.")]
    [SerializeField, Range(-1f, 1f)]
    [NaughtyAttributes.ShowIf(nameof(UsesDirection))]
    private float _minimumDirectionDot = 0.5f;

    [Tooltip("발동 뒤 LastSetMoveInput을 비워 같은 방향 입력이 이어서 소비되지 않게 합니다.")]
    [SerializeField]
    [NaughtyAttributes.ShowIf(nameof(UsesDirection))]
    private bool _consumeDirection = true;

    [Tooltip("기존 EventInputTrigger의 직렬화 값을 InputActionReference로 옮길 때만 사용합니다.")]
    [SerializeField, HideInInspector, FormerlySerializedAs("_inputType")]
    private int _legacyInputType = -1;

    private float _heldTime;
    private bool _armed;

    public float HoldProgress => _holdDuration <= 0f ? 1f : Mathf.Clamp01(_heldTime / _holdDuration);
    private bool UsesHold => EffectiveActivation == InteractionEventActivation.Hold;
    private bool UsesDirection => EffectiveActivation == InteractionEventActivation.ConfirmDirection;

    /// <summary>
    /// 실제 상호작용 트리거가 사용할 입력 방식을 반환합니다.
    /// 기존 트리거 호환 클래스는 직렬화 변경 없이 이 값을 재정의합니다.
    /// </summary>
    protected virtual InteractionEventActivation EffectiveActivation => _activation;

    /// <summary>
    /// 직렬화된 옛 입력 값이 없을 때 호환 클래스가 사용할 기본 액션 이름입니다.
    /// 새 InteractionEventTrigger는 명시적인 InputActionReference를 요구합니다.
    /// </summary>
    protected virtual string LegacyDefaultActionName => null;

    /// <summary>
    /// 외부 생성 도구가 InputActionReference와 판정 규칙을 안전하게 함께 설정합니다.
    /// EnumManager.InputType 호환 경로는 사용하지 않습니다.
    /// </summary>
    public void SetInput(InputActionReference inputAction, EnumManager.InputProcessType inputProcessType)
    {
        _inputAction = inputAction;
        _inputProcessType = inputProcessType;
        _legacyInputType = -1;
    }

    /// <summary>
    /// 활성화될 때 홀드 진행도와 입력 무장 상태를 초기화합니다.
    /// Enabled 판정만 입력 해제 순간이 없으므로 즉시 한 번 무장합니다.
    /// </summary>
    private void OnEnable()
    {
        ClearCurrentTargets();
        _heldTime = 0f;
        _armed = _inputProcessType == EnumManager.InputProcessType.Enabled;
    }

    /// <summary>
    /// 물리 프레임에서 영역 대상만 갱신합니다.
    /// 한 프레임 입력을 놓치지 않도록 실제 입력 판정은 Update에서 수행합니다.
    /// </summary>
    private void FixedUpdate()
    {
        CollectCurrentTargets();
    }

    /// <summary>
    /// 렌더 프레임마다 현재 InputAction 상태를 읽어 선택한 상호작용 규칙을 처리합니다.
    /// 영역을 벗어나거나 입력을 놓으면 홀드 진행도와 무장 상태를 정리합니다.
    /// </summary>
    private void Update()
    {
        if (!IsAvailableInCurrentChunk()) return;

        bool inputCondition = ReadInputCondition();
        bool hasTarget = CurrentTargets.Count > 0;

        if (!inputCondition || !hasTarget)
        {
            _heldTime = 0f;
            if (!inputCondition) _armed = true;
            if (!hasTarget && _inputProcessType == EnumManager.InputProcessType.Enabled) _armed = true;
            return;
        }

        if (EffectiveActivation == InteractionEventActivation.Hold)
        {
            EvaluateHold();
            return;
        }

        if (!_armed) return;
        if (EffectiveActivation == InteractionEventActivation.ConfirmDirection && !MatchesRequiredDirection()) return;
        if (!TryTriggerFirstTarget()) return;

        _armed = false;
        if (EffectiveActivation == InteractionEventActivation.ConfirmDirection && _consumeDirection &&
            GameManager.instance != null && GameManager.instance.inputManager != null)
            GameManager.instance.inputManager.LastSetMoveInput = Vector2.zero;
    }

    /// <summary>
    /// Hold 모드의 누적 시간을 진행하고 완료 시 첫 대상에 이벤트를 실행합니다.
    /// 완료 뒤에는 입력을 한 번 놓기 전까지 다시 누적하지 않습니다.
    /// </summary>
    private void EvaluateHold()
    {
        if (!_armed) return;

        _heldTime += Time.unscaledDeltaTime;
        if (_heldTime < _holdDuration) return;

        _heldTime = 0f;
        if (TryTriggerFirstTarget()) _armed = false;
    }

    /// <summary>
    /// InputActionReference를 InputManager의 런타임 액션으로 변환한 뒤 설정된 규칙으로 읽습니다.
    /// 런타임 액션을 찾지 못하면 참조가 직접 가리키는 원본 액션을 사용합니다.
    /// </summary>
    private bool ReadInputCondition()
    {
        if (!_inputAction || _inputAction.action == null) return false;

        InputAction runtimeAction = GameManager.instance != null && GameManager.instance.inputManager != null
            ? GameManager.instance.inputManager.GetRuntimeAction(_inputAction)
            : null;
        InputAction action = runtimeAction ?? _inputAction.action;

        return _inputProcessType switch
        {
            EnumManager.InputProcessType.InProgress => action.inProgress,
            EnumManager.InputProcessType.Triggered => action.triggered,
            EnumManager.InputProcessType.Enabled => action.enabled,
            EnumManager.InputProcessType.WasPerformedThisFrame => action.WasPerformedThisFrame(),
            EnumManager.InputProcessType.WasPressedThisFrame => action.WasPressedThisFrame(),
            EnumManager.InputProcessType.WasReleasedThisFrame => action.WasReleasedThisFrame(),
            _ => false,
        };
    }

    /// <summary>
    /// 마지막 이동 입력과 요구 방향의 정규화 내적을 비교합니다.
    /// 방향 정보나 InputManager가 없으면 잘못 발동하지 않도록 false를 반환합니다.
    /// </summary>
    private bool MatchesRequiredDirection()
    {
        if (GameManager.instance == null || GameManager.instance.inputManager == null) return false;

        Vector2 input = GameManager.instance.inputManager.LastSetMoveInput;
        if (input.sqrMagnitude <= Mathf.Epsilon || _requiredDirection.sqrMagnitude <= Mathf.Epsilon) return false;
        return Vector2.Dot(input.normalized, _requiredDirection.normalized) >= _minimumDirectionDot;
    }

    /// <summary>
    /// 현재 영역 안의 첫 번째 대상에 공통 실행 정책을 적용합니다.
    /// 대상이 없거나 쿨타임 중이면 false를 반환합니다.
    /// </summary>
    private bool TryTriggerFirstTarget()
    {
        foreach (SpatialTargetRecord record in CurrentTargets.Values)
        {
            if (TryTrigger(new EventTriggerContext(this, record.Target, record.Collider))) return true;
        }

        return false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 기존 InputType 직렬화 값을 대응하는 PLAY InputActionReference로 한 번 이전합니다.
    /// 새 구성에서는 InputActionReference와 InputProcessType만 검증합니다.
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();
        MigrateLegacyInputReference();

        if (!_inputAction)
            Debug.LogWarning("상호작용 트리거에는 InputActionReference가 필요합니다.", this);
    }

    /// <summary>
    /// 예전 InputType 정수 값을 Input System의 정본 서브에셋으로 변환합니다.
    /// 변환을 마치면 호환 값을 제거해 이후 실행 경로에서 다시 사용하지 않습니다.
    /// </summary>
    private void MigrateLegacyInputReference()
    {
        if (_inputAction) return;
        if (_legacyInputType < 0 && string.IsNullOrEmpty(LegacyDefaultActionName)) return;

        string actionName = _legacyInputType switch
        {
            0 => "Move",
            1 => "Sprint",
            2 => "Attack",
            3 or 4 => "Dodge",
            5 => "MovePressedD",
            6 => "MovePressedL",
            7 => "MovePressedR",
            8 => "MovePressedU",
            9 => "Confirm",
            12 => "Skill",
            22 => "Attack",
            23 => "Skill",
            _ => null,
        };

        actionName ??= LegacyDefaultActionName;

        if (string.IsNullOrEmpty(actionName)) return;

        UnityEngine.Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(_inputActionsAssetPath);
        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is not InputActionReference reference || reference.action == null) continue;
            if (reference.action.actionMap?.name != "PLAY" || reference.action.name != actionName) continue;
            if (reference.hideFlags.HasFlag(HideFlags.HideInHierarchy)) continue;

            _inputAction = reference;
            _inputProcessType = EffectiveActivation == InteractionEventActivation.Hold ||
                                actionName.StartsWith("MovePressed") || actionName == "Move"
                ? EnumManager.InputProcessType.InProgress
                : EnumManager.InputProcessType.WasPerformedThisFrame;
            _legacyInputType = -1;
            UnityEditor.EditorUtility.SetDirty(this);
            return;
        }

        Debug.LogWarning($"PLAY/{actionName} InputActionReference를 찾지 못해 기존 입력 트리거를 이전하지 못했습니다.", this);
    }
#endif
}
