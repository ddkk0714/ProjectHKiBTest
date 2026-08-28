using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 공격 트리거가 실행 조건을 판정할 수 있도록 실제 Damage 호출 정보를 보관합니다.
/// 센서, 공격자, 예상 피해량, 넉백 여부를 하나의 불변 Context로 전달합니다.
/// </summary>
public sealed class EventAttackContext
{
    public EventAttackSensor Sensor { get; }
    public Collider2D SensorCollider { get; }
    public DamageDataSO DamageData { get; }
    public IAttackable Attacker { get; }
    public GameObject AttackerObject { get; }
    public Vector3 Origin { get; }
    public int Damage { get; }
    public bool WouldKnockBack { get; }

    /// <summary>
    /// 한 번의 Damage 호출에서 계산된 공격 정보를 묶습니다.
    /// 이후 필터에서 원본 값을 다시 계산하지 않도록 스냅샷으로 유지합니다.
    /// </summary>
    public EventAttackContext(
        EventAttackSensor sensor,
        Collider2D sensorCollider,
        DamageDataSO damageData,
        IAttackable attacker,
        GameObject attackerObject,
        Vector3 origin,
        int damage,
        bool wouldKnockBack)
    {
        Sensor = sensor;
        SensorCollider = sensorCollider;
        DamageData = damageData;
        Attacker = attacker;
        AttackerObject = attackerObject;
        Origin = origin;
        Damage = damage;
        WouldKnockBack = wouldKnockBack;
    }
}

/// <summary>
/// 기존 Damager가 IDamagable.Damage를 호출하는 순간을 AttackEventTrigger로 전달합니다.
/// 실제 체력, 피격 이펙트, 넉백 상태는 변경하지 않는 무피해 전용 센서입니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Event Attack Sensor")]
public sealed class EventAttackSensor : InterfaceModule, IDamagable
{
    [Tooltip("감지한 공격을 전달할 AttackEventTrigger입니다.")]
    [SerializeField, FormerlySerializedAs("trigger")]
    [NaughtyAttributes.Required]
    private AttackEventTrigger _trigger;

    [Tooltip("DamageDataSO.knockBack이 이 값보다 클 때 넉백 가능한 공격으로 봅니다.")]
    [SerializeField, Min(0f), FormerlySerializedAs("knockbackThreshold")]
    private float _knockbackThreshold;

    [Tooltip("켜면 공격 필터에서 이 센서가 넉백되지 않는 것으로 판정합니다.")]
    [SerializeField, FormerlySerializedAs("superArmour")]
    private bool _superArmour;

    [Tooltip("예상 피해량 계산에 사용할 가상 방어력입니다.")]
    [SerializeField, Min(0f), FormerlySerializedAs("defense")]
    private float _defense;

    [Tooltip("예상 피해량 계산에 사용할 가상 저항력입니다.")]
    [SerializeField, Range(0f, 100f), FormerlySerializedAs("resistance")]
    private float _resistance;

    [Tooltip("켜면 예상 피해량을 0으로 계산합니다.")]
    [SerializeField, FormerlySerializedAs("invincible")]
    private bool _invincible;

    private Collider2D _sensorCollider;
    private int _lastDamageFrame = -1;
    private DamageDataSO _lastDamageData;
    private IAttackable _lastAttacker;

    public float BaseMaxHP { get; set; } = 1f;
    public FloatBuffContainer MaxHPBuffer { get; set; }
    public float HP { get; set; } = 1f;
    public float BaseDEF { get; set; }
    public FloatBuffContainer DEFBuffer { get; set; }
    public FloatBuffContainer ResistanceBuffer { get; set; }
    public BoolBuffContainer InvincibleBuffer { get; set; }
    public BoolBuffContainer SuperArmourBuffer { get; set; }
    public AudioDataSO HitSound { get; set; }
    public ParticlePlayer HitParticle { get; set; }
    public Action OnDamaged { get; set; }
    public Action OnDie { get; set; }
    public Action OnHealed { get; set; }
    public Action<float> OnHPChanged { get; set; }
    public Action<float> OnDEFChanged { get; set; }
    public Action<float> OnResistanceChanged { get; set; }
    public Action<bool> OnInvincibleChanged { get; set; }
    public Action<bool> OnSuperArmourChanged { get; set; }

    /// <summary>
    /// 조합형 전투가 이 센서를 IDamagable 대상으로 찾을 수 있게 등록합니다.
    /// 센서를 엔티티 루트에 붙인 경우에도 기존 Damager 경로와 동일하게 동작합니다.
    /// </summary>
    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IDamagable>(this);
    }

    /// <summary>
    /// 센서 콜라이더와 부모 공격 트리거를 찾고 가상 방어 컨테이너를 초기화합니다.
    /// 실제 대상의 전투 상태에는 접근하지 않습니다.
    /// </summary>
    private void Awake()
    {
        _sensorCollider = GetComponent<Collider2D>();
        if (!_trigger) _trigger = GetComponentInParent<AttackEventTrigger>();
        Initialize();
    }

    /// <summary>
    /// IDamagable 계약에 필요한 가상 버프 컨테이너와 기본 수치를 준비합니다.
    /// 공격 감지마다 할당하지 않고 센서 생성 시 한 번만 수행합니다.
    /// </summary>
    public void Initialize()
    {
        MaxHPBuffer = new FloatBuffContainer();
        DEFBuffer = new FloatBuffContainer();
        ResistanceBuffer = new FloatBuffContainer();
        InvincibleBuffer = new BoolBuffContainer();
        SuperArmourBuffer = new BoolBuffContainer();
        BaseDEF = _defense;
        HP = BaseMaxHP;
    }

    /// <summary>
    /// 실제 공격 정보를 중복 제거하고 필터에 필요한 예상값을 계산해 트리거로 전달합니다.
    /// 같은 프레임의 동일 DamageData와 공격자는 한 번만 처리합니다.
    /// </summary>
    public void Damage(DamageDataSO damageData, IAttackable hitter, Vector3 origin)
    {
        if (!isActiveAndEnabled || !damageData || hitter == null || !_trigger) return;

        if (_lastDamageFrame == Time.frameCount &&
            _lastDamageData == damageData &&
            ReferenceEquals(_lastAttacker, hitter))
            return;

        _lastDamageFrame = Time.frameCount;
        _lastDamageData = damageData;
        _lastAttacker = hitter;

        GameObject attackerObject = (hitter as Component)?.gameObject;
        int predictedDamage = CalculateNonCriticalDamage(damageData, hitter);
        bool wouldKnockBack = !_superArmour && damageData.knockBack > _knockbackThreshold;

        OnDamaged?.Invoke();
        _trigger.ReceiveAttack(new EventAttackContext(
            this,
            _sensorCollider,
            damageData,
            hitter,
            attackerObject,
            origin,
            predictedDamage,
            wouldKnockBack));
    }

    /// <summary>
    /// IDamagable 계약의 사망 콜백만 전달합니다.
    /// 센서 오브젝트를 비활성화하거나 파괴하지 않습니다.
    /// </summary>
    public void Die()
    {
        OnDie?.Invoke();
    }

    /// <summary>
    /// IDamagable 계약의 회복 콜백만 전달합니다.
    /// 공격 센서의 가상 체력은 변경하지 않습니다.
    /// </summary>
    public void Heal(int amount)
    {
        OnHealed?.Invoke();
    }

    /// <summary>
    /// 치명타를 제외하고 센서의 가상 방어력과 저항을 반영한 피해량을 계산합니다.
    /// 공격 필터용 예상값이며 실제 체력에는 적용하지 않습니다.
    /// </summary>
    private int CalculateNonCriticalDamage(DamageDataSO damageData, IAttackable hitter)
    {
        if (_invincible) return 0;

        float resistanceCoefficient = Mathf.Pow((100f - _resistance) / 100f, 2f);
        int value = (int)(damageData.damageCoefficient * hitter.ATK * resistanceCoefficient - _defense);
        return Mathf.Max(1, value);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 인스펙터에서 부모 공격 트리거를 보완하고 필수 콜라이더 누락을 경고합니다.
    /// 빌드 코드에는 UnityEditor 의존성이 포함되지 않습니다.
    /// </summary>
    private void OnValidate()
    {
        if (!_trigger) _trigger = GetComponentInParent<AttackEventTrigger>();
        if (!GetComponent<Collider2D>())
            Debug.LogWarning("공격 센서에는 Damager가 찾을 수 있는 Collider2D가 필요합니다.", this);
    }
#endif
}
