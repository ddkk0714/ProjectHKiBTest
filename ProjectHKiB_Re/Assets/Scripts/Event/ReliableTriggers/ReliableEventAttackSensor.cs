using System;
using UnityEngine;

public sealed class ReliableEventAttackContext
{
    public ReliableEventAttackSensor Sensor { get; }
    public Collider2D SensorCollider { get; }
    public DamageDataSO DamageData { get; }
    public IAttackable Attacker { get; }
    public GameObject AttackerObject { get; }
    public Vector3 Origin { get; }
    public int Damage { get; }
    public bool WouldKnockBack { get; }

    public ReliableEventAttackContext(
        ReliableEventAttackSensor sensor,
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
/// 기존 Damager가 IDamagable.Damage를 호출하는 순간을 이벤트 트리거로 전달하는 무피해 센서입니다.
/// 공격 레이어에 포함된 전용 자식 오브젝트에 Collider2D와 함께 배치해야 합니다.
/// 실제 체력, 피격 이펙트, 넉백 상태는 변경하지 않습니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Reliable Event Attack Sensor")]
public sealed class ReliableEventAttackSensor : MonoBehaviour, IDamagable
{
    [SerializeField] private ReliableGameEventTrigger trigger;
    [Tooltip("넉백 판정에 사용할 가상 질량입니다. DamageDataSO.knockBack이 이 값보다 커야 넉백으로 봅니다.")]
    [Min(0f)]
    [SerializeField] private float knockbackThreshold;
    [SerializeField] private bool superArmour;

    [Header("Virtual defense used by the damage filter")]
    [Min(0f)]
    [SerializeField] private float defense;
    [Range(0f, 100f)]
    [SerializeField] private float resistance;
    [SerializeField] private bool invincible;

    private Collider2D sensorCollider;
    private int lastDamageFrame = -1;
    private DamageDataSO lastDamageData;
    private IAttackable lastAttacker;

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

    private void Awake()
    {
        sensorCollider = GetComponent<Collider2D>();
        if (!trigger) trigger = GetComponentInParent<ReliableGameEventTrigger>();
        Initialize();
    }

    public void Initialize()
    {
        MaxHPBuffer = new FloatBuffContainer();
        DEFBuffer = new FloatBuffContainer();
        ResistanceBuffer = new FloatBuffContainer();
        InvincibleBuffer = new BoolBuffContainer();
        SuperArmourBuffer = new BoolBuffContainer();
        BaseDEF = defense;
        HP = BaseMaxHP;
    }

    public void Damage(DamageDataSO damageData, IAttackable hitter, Vector3 origin)
    {
        if (!isActiveAndEnabled || !damageData || hitter == null || !trigger) return;

        // OverlapBox가 같은 센서 오브젝트의 복수 콜라이더를 돌려주는 경우 한 공격이 중복 실행되지 않게 한다.
        if (lastDamageFrame == Time.frameCount &&
            lastDamageData == damageData &&
            ReferenceEquals(lastAttacker, hitter))
            return;

        lastDamageFrame = Time.frameCount;
        lastDamageData = damageData;
        lastAttacker = hitter;

        GameObject attackerObject = (hitter as Component)?.gameObject;
        int predictedDamage = CalculateNonCriticalDamage(damageData, hitter);
        bool wouldKnockBack = !superArmour && damageData.knockBack > knockbackThreshold;

        OnDamaged?.Invoke();
        trigger.ReceiveAttack(new ReliableEventAttackContext(
            this,
            sensorCollider,
            damageData,
            hitter,
            attackerObject,
            origin,
            predictedDamage,
            wouldKnockBack));
    }

    public void Die()
    {
        OnDie?.Invoke();
    }

    public void Heal(int amount)
    {
        OnHealed?.Invoke();
    }

    private int CalculateNonCriticalDamage(DamageDataSO damageData, IAttackable hitter)
    {
        if (invincible) return 0;

        float resistanceCoefficient = Mathf.Pow((100f - resistance) / 100f, 2f);
        int value = (int)(damageData.damageCoefficient * hitter.ATK * resistanceCoefficient - defense);
        return Mathf.Max(1, value);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!trigger) trigger = GetComponentInParent<ReliableGameEventTrigger>();
        if (!GetComponent<Collider2D>())
            Debug.LogWarning("공격 센서는 Damager가 찾을 수 있도록 같은 오브젝트에 Collider2D가 필요합니다.", this);
    }
#endif
}
