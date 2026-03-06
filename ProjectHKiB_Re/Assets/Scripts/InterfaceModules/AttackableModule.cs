using System.Collections;
using UnityEngine;
using UnityEngine.U2D.Animation;

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

    public Cooltime attackCooltime;

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IAttackable>(this);
    }

    public virtual void Initialize()
    {
        ATKBuffer = new(BaseATK);
        SetAttacker();
        attackCooltime = new();
        IsAttackCooltime = false;
        damager.Initialize(EffectAnimationData, EffectSpriteLibrary);
    }

    public void SetAttacker()
    {
        if (damager == null) return;
        damager.SetAttackable(this);
    }

    public void StartAttackCooltime()
    {
        IsAttackCooltime = true;
        attackCooltime.StartCooltime(AttackDatas[AttackNumber].coolTime, () => IsAttackCooltime = false);
    }

    public void SetAttackData(int attackNumber)
    {
        if (AttackDatas == null)
        {
            Debug.LogError("ERROR: AttackDatas is missing!!!"); return;
        }

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
}