using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum AttackKnockbackRequirement
{
    Any,
    Required,
    Forbidden
}

/// <summary>
/// 실제 Damage 호출에서 전달된 공격 세부 조건을 검사합니다.
/// 공격자의 직접 참조, 이름, 예상 피해량, 넉백 여부를 선택적으로 제한합니다.
/// </summary>
[Serializable]
public sealed class AttackDetailFilter
{
    [Tooltip("같은 Scene의 특정 공격자만 허용할 때 지정합니다.")]
    [SerializeField, FormerlySerializedAs("requiredAttacker")]
    private GameObject _requiredAttacker;

    [Tooltip("비워 두면 공격자 이름을 검사하지 않습니다.")]
    [SerializeField, FormerlySerializedAs("requiredAttackerName")]
    private string _requiredAttackerName;

    [Tooltip("공격자 이름을 비교할 방법입니다.")]
    [SerializeField, FormerlySerializedAs("attackerNameMatch")]
    private EventTriggerNameMatch _attackerNameMatch = EventTriggerNameMatch.Exact;

    [Tooltip("공격자 이름 비교에서 대소문자를 무시할지 결정합니다.")]
    [SerializeField, FormerlySerializedAs("ignoreAttackerNameCase")]
    private bool _ignoreAttackerNameCase;

    [Tooltip("가상 방어력과 저항을 반영한 예상 피해량의 최솟값입니다.")]
    [SerializeField, Min(0), FormerlySerializedAs("minimumDamage")]
    private int _minimumDamage;

    [Tooltip("예상 피해량의 최댓값이며 0이면 상한을 사용하지 않습니다.")]
    [SerializeField, Min(0), FormerlySerializedAs("maximumDamage")]
    private int _maximumDamage;

    [Tooltip("이 공격에 넉백이 필요한지 또는 금지되는지 지정합니다.")]
    [SerializeField, FormerlySerializedAs("knockback")]
    private AttackKnockbackRequirement _knockback = AttackKnockbackRequirement.Any;

    /// <summary>
    /// 공격 Context가 설정된 모든 세부 제한을 만족하는지 검사합니다.
    /// 제한하지 않은 항목은 통과시켜 인스펙터 설정 수를 최소화합니다.
    /// </summary>
    public bool Matches(EventAttackContext context)
    {
        if (context == null) return false;
        if (_requiredAttacker && context.AttackerObject != _requiredAttacker) return false;
        if (!MatchesAttackerName(context.AttackerObject)) return false;
        if (context.Damage < _minimumDamage) return false;
        if (_maximumDamage > 0 && context.Damage > _maximumDamage) return false;
        if (_knockback == AttackKnockbackRequirement.Required && !context.WouldKnockBack) return false;
        if (_knockback == AttackKnockbackRequirement.Forbidden && context.WouldKnockBack) return false;
        return true;
    }

    /// <summary>
    /// 선택된 문자열 비교 방식으로 공격자 이름을 확인합니다.
    /// 이름 제한이 비어 있으면 모든 공격자를 허용합니다.
    /// </summary>
    private bool MatchesAttackerName(GameObject attacker)
    {
        if (string.IsNullOrEmpty(_requiredAttackerName)) return true;
        if (!attacker) return false;

        StringComparison comparison = _ignoreAttackerNameCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return _attackerNameMatch switch
        {
            EventTriggerNameMatch.Contains => attacker.name.IndexOf(_requiredAttackerName, comparison) >= 0,
            EventTriggerNameMatch.StartsWith => attacker.name.StartsWith(_requiredAttackerName, comparison),
            _ => string.Equals(attacker.name, _requiredAttackerName, comparison),
        };
    }
}

/// <summary>
/// 공격자의 공통 대상 조건과 실제 공격 세부 조건을 함께 검사합니다.
/// 공격 감지 전용이므로 영역 감지 모드와 충돌하는 설정을 노출하지 않습니다.
/// </summary>
[Serializable]
public sealed class AttackEventFilter
{
    [Tooltip("허용할 공격자 레이어입니다.")]
    [SerializeField, FormerlySerializedAs("layers")]
    private LayerMask _attackerLayers = ~0;

    [Tooltip("비워 두면 공격자 이름을 공통 단계에서 검사하지 않습니다.")]
    [SerializeField, FormerlySerializedAs("requiredName")]
    private string _requiredAttackerName;

    [Tooltip("공통 공격자 이름을 비교할 방법입니다.")]
    [SerializeField, FormerlySerializedAs("nameMatch")]
    private EventTriggerNameMatch _attackerNameMatch = EventTriggerNameMatch.Exact;

    [Tooltip("공통 공격자 이름 비교에서 대소문자를 무시할지 결정합니다.")]
    [SerializeField, FormerlySerializedAs("ignoreNameCase")]
    private bool _ignoreAttackerNameCase;

    [Tooltip("피해량과 넉백 등 실제 공격의 세부 제한입니다.")]
    [SerializeField, FormerlySerializedAs("attack")]
    private AttackDetailFilter _details = new();

    /// <summary>
    /// 공격자의 레이어와 이름, 공격 세부 정보가 모두 허용되는지 검사합니다.
    /// 실제 Damage 콜백에서 만들어진 Context만 유효합니다.
    /// </summary>
    public bool Matches(EventAttackContext context)
    {
        GameObject attacker = context?.AttackerObject;
        if (!attacker || (_attackerLayers.value & (1 << attacker.layer)) == 0) return false;
        if (!MatchesCommonName(attacker.name)) return false;
        return _details != null && _details.Matches(context);
    }

    /// <summary>
    /// 공통 공격자 이름 제한을 선택된 비교 방식으로 확인합니다.
    /// 세부 필터와 별개로 빠르게 대상을 좁히는 용도입니다.
    /// </summary>
    private bool MatchesCommonName(string candidate)
    {
        if (string.IsNullOrEmpty(_requiredAttackerName)) return true;

        StringComparison comparison = _ignoreAttackerNameCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return _attackerNameMatch switch
        {
            EventTriggerNameMatch.Contains => candidate.IndexOf(_requiredAttackerName, comparison) >= 0,
            EventTriggerNameMatch.StartsWith => candidate.StartsWith(_requiredAttackerName, comparison),
            _ => string.Equals(candidate, _requiredAttackerName, comparison),
        };
    }
}

/// <summary>
/// EventAttackSensor가 전달한 실제 Damage 호출만 이벤트로 변환합니다.
/// 영역 및 입력을 폴링하지 않아 전투 콜백의 책임을 독립적으로 유지합니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Attack Event Trigger")]
public class AttackEventTrigger : EventTriggerBase
{
    [Tooltip("허용할 공격자와 공격 수치를 제한하는 필터입니다.")]
    [SerializeField, FormerlySerializedAs("filter")]
    private AttackEventFilter _filter = new();

    /// <summary>
    /// 공격 센서에서 실제 Damage 호출 정보를 받아 필터를 통과한 공격만 실행합니다.
    /// 센서가 아닌 일반 충돌이나 Trigger 메시지는 공격으로 취급하지 않습니다.
    /// </summary>
    internal void ReceiveAttack(EventAttackContext attackContext)
    {
        if (attackContext == null || _filter == null || !_filter.Matches(attackContext)) return;

        // Chunk 제한은 EventTriggerBase에서 판정해야 LastResult/Evaluated에 거부 사유가 남는다.
        TryTrigger(new EventTriggerContext(
            this,
            attackContext.AttackerObject,
            attackContext.SensorCollider,
            attackContext));
    }
}
