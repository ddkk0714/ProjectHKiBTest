using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public enum ReliableTriggerActivation
{
    Enter,
    Stay,
    Exit,
    Input,
    ConfirmDirection
}

public enum ReliableTriggerTargetScope
{
    ColliderObject,
    RigidbodyOrColliderObject,
    RootObject
}

public enum ReliableTriggerNameMatch
{
    Exact,
    Contains,
    StartsWith
}

public enum ReliableTriggerKnockbackRequirement
{
    Any,
    Required,
    Forbidden
}

[Serializable]
public sealed class ReliableAttackFilter
{
    [Tooltip("같은 Scene의 특정 공격자를 직접 제한할 때만 사용합니다. Scene을 넘는 대상은 아래 이름 필터를 사용하세요.")]
    [SerializeField] private GameObject requiredAttacker;

    [Tooltip("비워 두면 공격자 이름을 검사하지 않습니다. 프리팹 인스턴스의 (Clone) 접미사는 Starts With로 처리할 수 있습니다.")]
    [SerializeField] private string requiredAttackerName;

    [SerializeField] private ReliableTriggerNameMatch attackerNameMatch = ReliableTriggerNameMatch.Exact;
    [SerializeField] private bool ignoreAttackerNameCase;

    [Tooltip("센서의 방어력/저항을 적용하고 치명타는 제외한 예상 피해량의 최솟값입니다.")]
    [Min(0)]
    [SerializeField] private int minimumDamage;

    [Tooltip("0이면 상한을 사용하지 않습니다.")]
    [Min(0)]
    [SerializeField] private int maximumDamage;

    [SerializeField] private ReliableTriggerKnockbackRequirement knockback = ReliableTriggerKnockbackRequirement.Any;

    public bool Matches(ReliableEventAttackContext context)
    {
        if (context == null) return false;
        if (requiredAttacker && context.AttackerObject != requiredAttacker) return false;
        if (!MatchesAttackerName(context.AttackerObject)) return false;
        if (context.Damage < minimumDamage) return false;
        if (maximumDamage > 0 && context.Damage > maximumDamage) return false;
        if (knockback == ReliableTriggerKnockbackRequirement.Required && !context.WouldKnockBack) return false;
        if (knockback == ReliableTriggerKnockbackRequirement.Forbidden && context.WouldKnockBack) return false;
        return true;
    }

    private bool MatchesAttackerName(GameObject attacker)
    {
        if (string.IsNullOrEmpty(requiredAttackerName)) return true;
        if (!attacker) return false;

        StringComparison comparison = ignoreAttackerNameCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return attackerNameMatch switch
        {
            ReliableTriggerNameMatch.Contains => attacker.name.IndexOf(requiredAttackerName, comparison) >= 0,
            ReliableTriggerNameMatch.StartsWith => attacker.name.StartsWith(requiredAttackerName, comparison),
            _ => string.Equals(attacker.name, requiredAttackerName, comparison),
        };
    }
}

[Serializable]
public sealed class ReliableEventTriggerFilter
{
    [Tooltip("일반 감지에서는 대상, 공격 감지에서는 공격자의 레이어를 검사합니다.")]
    [SerializeField] private LayerMask layers = ~0;

    [Tooltip("비워 두면 이름을 검사하지 않습니다. 공격 감지에서는 공격자 이름을 검사합니다.")]
    [SerializeField] private string requiredName;

    [SerializeField] private ReliableTriggerNameMatch nameMatch = ReliableTriggerNameMatch.Exact;
    [SerializeField] private bool ignoreNameCase;

    [Tooltip("켜면 영역 진입 대신 실제 공격 판정(IDamagable.Damage 호출)만 받습니다.")]
    [SerializeField] private bool attackOnly;

    [SerializeField] private ReliableAttackFilter attack = new ReliableAttackFilter();

    public LayerMask Layers => layers;
    public bool AttackOnly => attackOnly;

    public bool Matches(GameObject target, ReliableEventAttackContext attackContext = null)
    {
        if (!target) return false;
        if ((layers.value & (1 << target.layer)) == 0) return false;
        if (!MatchesName(target.name)) return false;
        return !attackOnly || attack.Matches(attackContext);
    }

    private bool MatchesName(string candidate)
    {
        if (string.IsNullOrEmpty(requiredName)) return true;

        StringComparison comparison = ignoreNameCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return nameMatch switch
        {
            ReliableTriggerNameMatch.Contains => candidate.IndexOf(requiredName, comparison) >= 0,
            ReliableTriggerNameMatch.StartsWith => candidate.StartsWith(requiredName, comparison),
            _ => string.Equals(candidate, requiredName, comparison),
        };
    }
}

public sealed class ReliableEventTriggerContext
{
    public ReliableGameEventTrigger Trigger { get; }
    public GameObject Target { get; }
    public Collider2D TargetCollider { get; }
    public ReliableEventAttackContext Attack { get; }
    public bool IsAttack => Attack != null;

    public ReliableEventTriggerContext(
        ReliableGameEventTrigger trigger,
        GameObject target,
        Collider2D targetCollider,
        ReliableEventAttackContext attack)
    {
        Trigger = trigger;
        Target = target;
        TargetCollider = targetCollider;
        Attack = attack;
    }
}

/// <summary>
/// 진입/체류/이탈/입력/공격 감지를 한 컴포넌트에서 설정하는 이벤트 트리거입니다.
/// 일반 대상은 프로젝트의 Z축 판정을 보존하기 위해 ZCollider2D.OverlapCollider로 감지하고,
/// 공격은 ReliableEventAttackSensor가 전달한 실제 Damage 호출만 사용합니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Reliable Game Event Trigger")]
public sealed class ReliableGameEventTrigger : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] private ReliableTriggerActivation activation = ReliableTriggerActivation.Enter;
    [SerializeField] private ReliableEventTriggerFilter filter = new ReliableEventTriggerFilter();
    [SerializeField] private ZCollider2D areaCollider;
    [SerializeField] private ReliableTriggerTargetScope targetScope = ReliableTriggerTargetScope.RigidbodyOrColliderObject;
    [SerializeField] private ChunkData chunk;

    [Header("Input")]
    [Tooltip("StateTransition과 같은 방식으로 Input System 액션을 직접 참조합니다.")]
    [SerializeField] private InputActionReference inputAction;
    [Tooltip("PlayerInputDecision과 동일한 규칙으로 액션 상태를 판정합니다.")]
    [SerializeField, EnumDropdown(typeof(EnumManager.InputProcessType))]
    private EnumManager.InputProcessType inputProcessType = EnumManager.InputProcessType.WasPerformedThisFrame;
    [SerializeField] private Vector2 requiredDirection = Vector2.down;
    [Range(-1f, 1f)]
    [SerializeField] private float minimumDirectionDot = 0.5f;
    [SerializeField] private bool consumeConfirmDirection = true;

    [Header("Execution")]
    [Tooltip("비워 두면 같은 오브젝트와 부모에서 GameEvent를 찾습니다.")]
    [SerializeField] private GameEvent gameEvent;
    [SerializeField] private UnityEvent onTriggered;
    [Min(0f)]
    [SerializeField] private float cooldown;
    [SerializeField] private bool triggerOnce;

    private readonly List<Collider2D> overlapResults = new List<Collider2D>(16);
    private readonly Dictionary<int, TargetRecord> previousTargets = new Dictionary<int, TargetRecord>();
    private readonly Dictionary<int, TargetRecord> currentTargets = new Dictionary<int, TargetRecord>();
    private ContactFilter2D contactFilter;
    private float nextTriggerTime;
    private bool hasTriggered;

    public event Action<ReliableEventTriggerContext> Triggered;
    public ReliableEventTriggerContext LastContext { get; private set; }

    private sealed class TargetRecord
    {
        public GameObject Target { get; }
        public Collider2D Collider { get; }

        public TargetRecord(GameObject target, Collider2D collider)
        {
            Target = target;
            Collider = collider;
        }
    }

    private void Awake()
    {
        if (!areaCollider) areaCollider = GetComponent<ZCollider2D>();
        if (!gameEvent) gameEvent = GetComponent<GameEvent>();
        if (!gameEvent) gameEvent = GetComponentInParent<GameEvent>();

        contactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = filter.Layers,
            useTriggers = true
        };
    }

    private void OnEnable()
    {
        previousTargets.Clear();
        currentTargets.Clear();
    }

    private void FixedUpdate()
    {
        if (filter.AttackOnly || !IsAvailableInCurrentChunk()) return;
        if (!areaCollider) return;

        CollectCurrentTargets();
        if (activation != ReliableTriggerActivation.Input &&
            activation != ReliableTriggerActivation.ConfirmDirection)
            EvaluatePresence();
        StoreCurrentTargetsAsPrevious();
    }

    private void Update()
    {
        if (filter.AttackOnly ||
            (activation != ReliableTriggerActivation.Input &&
             activation != ReliableTriggerActivation.ConfirmDirection))
            return;

        // 짧은 performed 입력을 FixedUpdate 사이에서 놓치지 않도록 입력만 렌더 프레임에서 읽는다.
        if (!IsAvailableInCurrentChunk()) return;

        EvaluateInput();
    }

    public void TriggerManually(GameObject target = null)
    {
        TryTrigger(new ReliableEventTriggerContext(this, target, null, null));
    }

    public void ResetTrigger()
    {
        hasTriggered = false;
        nextTriggerTime = 0f;
    }

    internal void ReceiveAttack(ReliableEventAttackContext attackContext)
    {
        if (!filter.AttackOnly || !IsAvailableInCurrentChunk()) return;
        if (attackContext == null || !filter.Matches(attackContext.AttackerObject, attackContext)) return;

        TryTrigger(new ReliableEventTriggerContext(
            this,
            attackContext.AttackerObject,
            attackContext.SensorCollider,
            attackContext));
    }

    private void CollectCurrentTargets()
    {
        currentTargets.Clear();
        overlapResults.Clear();
        areaCollider.OverlapCollider(contactFilter, overlapResults);

        for (int i = 0; i < overlapResults.Count; i++)
        {
            Collider2D candidateCollider = overlapResults[i];
            if (!candidateCollider) continue;

            GameObject candidate = ResolveTarget(candidateCollider);
            if (!filter.Matches(candidate)) continue;

            int id = candidate.GetInstanceID();
            if (!currentTargets.ContainsKey(id))
                currentTargets.Add(id, new TargetRecord(candidate, candidateCollider));
        }
    }

    private void EvaluatePresence()
    {
        if (activation == ReliableTriggerActivation.Exit)
        {
            foreach (KeyValuePair<int, TargetRecord> pair in previousTargets)
            {
                if (!currentTargets.ContainsKey(pair.Key) && TryTrigger(pair.Value)) return;
            }
            return;
        }

        foreach (KeyValuePair<int, TargetRecord> pair in currentTargets)
        {
            bool isCandidate = activation == ReliableTriggerActivation.Stay ||
                               !previousTargets.ContainsKey(pair.Key);
            if (isCandidate && TryTrigger(pair.Value)) return;
        }
    }

    private void EvaluateInput()
    {
        if (!ReadInputCondition() || currentTargets.Count == 0) return;
        if (activation == ReliableTriggerActivation.ConfirmDirection && !MatchesRequiredDirection()) return;

        foreach (KeyValuePair<int, TargetRecord> pair in currentTargets)
        {
            if (!TryTrigger(pair.Value)) continue;

            if (activation == ReliableTriggerActivation.ConfirmDirection && consumeConfirmDirection)
                GameManager.instance.inputManager.LastSetMoveInput = Vector2.zero;
            return;
        }
    }

    private bool ReadInputCondition()
    {
        if (!inputAction || inputAction.action == null) return false;

        return inputProcessType switch
        {
            EnumManager.InputProcessType.InProgress => inputAction.action.inProgress,
            EnumManager.InputProcessType.Triggered => inputAction.action.triggered,
            EnumManager.InputProcessType.Enabled => inputAction.action.enabled,
            EnumManager.InputProcessType.WasPerformedThisFrame => inputAction.action.WasPerformedThisFrame(),
            EnumManager.InputProcessType.WasPressedThisFrame => inputAction.action.WasPressedThisFrame(),
            EnumManager.InputProcessType.WasReleasedThisFrame => inputAction.action.WasReleasedThisFrame(),
            _ => false,
        };
    }

    private bool MatchesRequiredDirection()
    {
        if (GameManager.instance == null || GameManager.instance.inputManager == null) return false;

        Vector2 input = GameManager.instance.inputManager.LastSetMoveInput;
        if (input.sqrMagnitude <= Mathf.Epsilon || requiredDirection.sqrMagnitude <= Mathf.Epsilon) return false;
        return Vector2.Dot(input.normalized, requiredDirection.normalized) >= minimumDirectionDot;
    }

    private bool TryTrigger(TargetRecord record)
    {
        if (record == null) return false;
        return TryTrigger(new ReliableEventTriggerContext(this, record.Target, record.Collider, null));
    }

    private bool TryTrigger(ReliableEventTriggerContext context)
    {
        if (!isActiveAndEnabled || !IsAvailableInCurrentChunk()) return false;
        if (triggerOnce && hasTriggered) return false;
        if (Time.time < nextTriggerTime) return false;

        hasTriggered = true;
        nextTriggerTime = Time.time + cooldown;
        LastContext = context;

        Triggered?.Invoke(context);
        if (gameEvent) gameEvent.TriggerEvent();
        onTriggered?.Invoke();
        return true;
    }

    private bool IsAvailableInCurrentChunk()
    {
        return !chunk || chunk.Active;
    }

    private GameObject ResolveTarget(Collider2D candidate)
    {
        switch (targetScope)
        {
            case ReliableTriggerTargetScope.RootObject:
                return candidate.transform.root.gameObject;
            case ReliableTriggerTargetScope.RigidbodyOrColliderObject:
                return candidate.attachedRigidbody
                    ? candidate.attachedRigidbody.gameObject
                    : candidate.gameObject;
            default:
                return candidate.gameObject;
        }
    }

    private void StoreCurrentTargetsAsPrevious()
    {
        previousTargets.Clear();
        foreach (KeyValuePair<int, TargetRecord> pair in currentTargets)
            previousTargets.Add(pair.Key, pair.Value);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!areaCollider) areaCollider = GetComponent<ZCollider2D>();
        if (filter != null && !filter.AttackOnly && !areaCollider)
            Debug.LogWarning("일반 감지를 사용하려면 ZCollider2D가 필요합니다.", this);
        if ((activation == ReliableTriggerActivation.Input ||
             activation == ReliableTriggerActivation.ConfirmDirection) && !inputAction)
            Debug.LogWarning("입력 감지를 사용하려면 InputActionReference가 필요합니다.", this);
    }
#endif
}
