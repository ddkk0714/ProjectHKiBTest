using System;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Random = UnityEngine.Random;

public interface IAttackableBase
{
    public int BaseATK { get; set; }

    public float CriticalChanceRate { get; set; }
    public float CriticalDamageRate { get; set; }
    public AttackDataSO[] AttackDatas { get; set; }
    public DamageParticleDataSO DamageParticle { get; set; }
    public SimpleAnimationDataSO EffectAnimationData { get; set; }
    public SpriteLibraryAsset EffectSpriteLibrary { get; set; }
}

public interface IAttackable : IAttackableBase, IInitializable
{
    public FloatBuffContainer ATKBuffer { get; set; }
    public int ATK { get => (int)ATKBuffer.GetBuffedStat(BaseATK, 0); }
    public Action<float> OnATKChanged { get; set; }
    public bool IsAttackCooltime { get; set; }
    public float DamageIndicatorRandomPosInfo { get; set; }
    public int AttackNumber { get; set; }

    public void SetAttacker();
    public void StartAttackCooltime();
    public void SetAttackData(int attackNumber);
    public void ApplyEffectAnimationData();

    public void Attack(int damageNumber);

    public void StopEffect(int animPlayerNum);
}

public class AttackableModule : InterfaceModule, IAttackable
{
    public int BaseATK { get; set; }
    public FloatBuffContainer ATKBuffer { get; set; }

    public float CriticalChanceRate { get; set; }
    public float CriticalDamageRate { get; set; }
    public AttackDataSO[] AttackDatas { get; set; }
    public DamageParticleDataSO DamageParticle { get; set; }
    public float DamageIndicatorRandomPosInfo { get; set; }

    [SerializeField] private Damager damager;
    public bool IsAttackCooltime { get; set; }
    public int AttackNumber { get; set; }
    public Transform CurrentTarget { get; set; }
    public SimpleAnimationDataSO EffectAnimationData { get; set; }
    public SpriteLibraryAsset EffectSpriteLibrary { get; set; }

    public Timer attackCooltime;

    public CooltimeMultiplierBuffContainer AttackCooltimeBuffer { get; private set; }

    private float _currentBaseAttackCooltime;

    public float AttackCooltimeMultiplier =>
        AttackCooltimeBuffer != null ? AttackCooltimeBuffer.BuffedMultiplier : 1f;

    //Accuracy Debuff Method
    public FloatBuffContainer AccuracyMissChanceBuffer { get; private set; }

    public float AccuracyMissChance =>
        AccuracyMissChanceBuffer != null ? Mathf.Clamp01(AccuracyMissChanceBuffer.GetBuffedStat(0)) : 0f;

    public bool HasRolledAccuracyDebuffThisAttack { get; private set; }
    public bool IsAccuracyDirDistortedThisAttack { get; private set; }

    //SelfDamage Debuff Method
    public FloatBuffContainer SelfDamageChanceBuffer { get; private set; }

    public float SelfDamageChance =>
        SelfDamageChanceBuffer != null ? Mathf.Clamp01(SelfDamageChanceBuffer.GetBuffedStat(0)) : 0f;

    public bool IsSelfDamageTriggered { get; private set; }
    public int PendingSelfDamageDamageNumber { get; private set; }

    // Groggy / Runaway Debuff Method
    public BoolBuffContainer GroggyBuffer { get; private set; }
    public BoolBuffContainer RunawayBuffer { get; private set; }

    public bool IsGroggy =>
        GroggyBuffer != null && GroggyBuffer.GetBuffedStat(0, isNegative: true);

    public bool IsRunaway =>
        RunawayBuffer != null && RunawayBuffer.GetBuffedStat(0, isNegative: true);

    public System.Action<float> OnATKChanged { get; set; }

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IAttackable>(this);
    }

    public virtual void Initialize()
    {
        ATKBuffer = new();
        AttackCooltimeBuffer = new(1f);
        AccuracyMissChanceBuffer = new();
        SelfDamageChanceBuffer = new();
        AttackCooltimeBuffer.OnBuffed += OnAttackCooltimeBuffChanged;

        GroggyBuffer = new();
        RunawayBuffer = new();

        SetAttacker();
        attackCooltime = new();
        IsAttackCooltime = false;
        _currentBaseAttackCooltime = 0f;

        ResetAccuracyDebuffAttackState();
        ResetSelfDamageState();

        ApplyEffectAnimationData();
    }

    // EffectAnimationData를 damager의 이펙트 플레이어로 밀어 넣는다. 프로퍼티에 값만 넣는 건
    // 아무 일도 하지 않으므로, 기어를 바꿀 때 이걸 불러 주지 않으면 새 기어가 이전 기어의
    // 이펙트를 그대로 쓴다.
    //
    // Initialize에서 떼어낸 이유: Initialize를 다시 돌리면 버프 컨테이너까지 전부 초기화된다.
    public void ApplyEffectAnimationData()
    {
        if (damager != null)
            damager.SetAnimationData(EffectAnimationData, EffectSpriteLibrary);
    }

    public void ResetAccuracyDebuffAttackState()
    {
        HasRolledAccuracyDebuffThisAttack = false;
        IsAccuracyDirDistortedThisAttack = false;
    }

    public bool TryRollAccuracyDebuff()
    {
        if (HasRolledAccuracyDebuffThisAttack)
            return IsAccuracyDirDistortedThisAttack;

        HasRolledAccuracyDebuffThisAttack = true;
        IsAccuracyDirDistortedThisAttack = Random.value < AccuracyMissChance;
        return IsAccuracyDirDistortedThisAttack;
    }

    public void SetAttacker()
    {
        if (damager == null) return;
        damager.SetAttackable(this);
    }

    public void StartAttackCooltime()
    {
        // 플래그를 세우기 전에 검사한다 — IsAttackCooltime을 내리는 건 타이머 콜백뿐이라,
        // 플래그만 세워 놓고 아래에서 빠져나가면 영영 쿨타임이 안 풀린다.
        if (AttackDatas == null || AttackDatas.Length == 0)
        {
            Debug.LogError("ERROR: AttackDatas is missing!!!");
            return;
        }

        // AttackNumber는 캐릭터가 바뀌어도 남아 있는데 AttackDatas 길이는 캐릭터마다 다르다.
        // SetAttackData가 인자를 다루는 방식과 똑같이 0으로 되돌린다.
        if (AttackNumber < 0 || AttackNumber >= AttackDatas.Length)
        {
            Debug.LogError($"ERROR: AttackData[{AttackNumber}] is missing!!! Falling back to 0.");
            AttackNumber = 0;
        }

        IsAttackCooltime = true;

        _currentBaseAttackCooltime = AttackDatas[AttackNumber].coolTime;
        float finalCooltime = _currentBaseAttackCooltime * AttackCooltimeMultiplier;

        attackCooltime.StartTimer(finalCooltime, () => IsAttackCooltime = false);
    }

    private void OnAttackCooltimeBuffChanged(float multiplier)
    {
        if (attackCooltime == null || attackCooltime.IsCooltimeEnded) return;
        if (_currentBaseAttackCooltime <= 0f) return;

        float newTotalCooltime = _currentBaseAttackCooltime * multiplier;
        attackCooltime.RecalculateTotalTime(newTotalCooltime);
    }

    public void ResetSelfDamageState()
    {
        IsSelfDamageTriggered = false;
        PendingSelfDamageDamageNumber = 0;
    }

    public void RollSelfDamage(int damageNumber)
    {
        PendingSelfDamageDamageNumber = damageNumber;
        IsSelfDamageTriggered = Random.value < SelfDamageChance;
    }

    public void ExecuteSelfDamage()
    {
        if (AttackDatas == null)
        {
            Debug.LogError("ERROR: AttackDatas is missing!!!");
            return;
        }

        if (AttackNumber < 0 || AttackNumber >= AttackDatas.Length)
        {
            Debug.LogError($"ERROR: AttackData[{AttackNumber}] is missing!!!");
            return;
        }

        if (AttackDatas[AttackNumber].damageDatas == null ||
            PendingSelfDamageDamageNumber < 0 ||
            PendingSelfDamageDamageNumber >= AttackDatas[AttackNumber].damageDatas.Length)
        {
            Debug.LogError($"ERROR: DamageData[{PendingSelfDamageDamageNumber}] is missing!!!");
            return;
        }

        if (TryGetComponent(out IDamagable selfDamagable))
        {
            DamageDataSO selfDamageData = AttackDatas[AttackNumber].damageDatas[PendingSelfDamageDamageNumber];
            selfDamagable.Damage(selfDamageData, this, transform.position);
        }
        else
        {
            Debug.LogError("ERROR: Self damage failed - IDamagable not found.");
        }

        ResetSelfDamageState();
    }

    public void SetAttackData(int attackNumber)
    {
        if (AttackDatas == null)
        {
            Debug.LogError("ERROR: AttackDatas is missing!!!"); return;
        }

        ResetAccuracyDebuffAttackState();

        DamageIndicatorRandomPosInfo = Random.value;

        if (attackNumber < AttackDatas.Length)
            AttackNumber = attackNumber;
        else
            AttackNumber = 0;
    }

    public void Attack(int damageNumber)
    {
        if (!damager)
        {
            Debug.LogError("ERROR: Damager is missing!!!"); return;
        }
        damager.SetDamageData(AttackNumber, damageNumber);
        damager.Damage();
    }

    // Attack과 같은 가드가 필요하다. 이건 StopAttackEffectAction을 통해 State의 ExitActions에서도
    // 불리는데(Delta_Roza_TransformStartState), StateController.ChangeState는 ExitState를 먼저 부르고
    // 그 다음에 CurrentState를 바꾼다. 여기서 예외가 나면 상태 전환이 통째로 취소되어 떠나려던
    // State에 영구히 갇힌다.
    public void StopEffect(int animPlayerNum)
    {
        if (!damager) return;

        damager.StopEffect(animPlayerNum);
    }
}