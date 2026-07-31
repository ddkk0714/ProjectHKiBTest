using System;
using NaughtyAttributes;
using UnityEngine;

public interface IDamagableBase
{
    public float BaseMaxHP { get; set; }
    public float BaseDEF { get; set; }

    public AudioDataSO HitSound { get; set; }
    public ParticlePlayer HitParticle { get; set; }
}

public interface IDamagable : IDamagableBase, IInitializable
{
    public FloatBuffContainer MaxHPBuffer { get; set; }
    public float MaxHP { get => MaxHPBuffer.GetBuffedStat(BaseMaxHP, 0); }
    public float HP { get; set; }
    public Action<float> OnHPChanged { get; set; }

    public FloatBuffContainer DEFBuffer { get; set; }
    public float DEF { get => DEFBuffer.GetBuffedStat(BaseDEF, 0); }
    public Action<float> OnDEFChanged { get; set; }

    public FloatBuffContainer ResistanceBuffer { get; set; }
    public float Resistance { get => ResistanceBuffer.GetBuffedStat(0); }
    public Action<float> OnResistanceChanged { get; set; }

    public BoolBuffContainer InvincibleBuffer { get; set; }
    public bool Invincible { get => InvincibleBuffer.GetBuffedStat(0, isNegative: false); }
    public Action<bool> OnInvincibleChanged { get; set; }

    public BoolBuffContainer SuperArmourBuffer { get; set; }
    public bool SuperArmour { get => SuperArmourBuffer.GetBuffedStat(0, isNegative: false); }
    public Action<bool> OnSuperArmourChanged { get; set; }

    public Action OnDamaged { get; set; }
    public Action OnDie { get; set; }
    public Action OnHealed { get; set; }

    public void Damage(DamageDataSO damageData, IAttackable hitter, Vector3 origin);
    public void Die();
    public void Heal(int amount);

}

namespace Assets.Scripts.Interfaces.Modules
{
    [RequireComponent(typeof(PhysicsModule))]
    public class DamagableModule : InterfaceModule, IDamagable
    {
        public float BaseMaxHP { get; set; }
        public FloatBuffContainer MaxHPBuffer { get; set; }
        public float MaxHP { get => MaxHPBuffer.GetBuffedStat(BaseMaxHP, 0); }
        private float _prevMaxHP;
        public float HP { get; set; }
        public float BaseDEF { get; set; }
        public float DEF { get => DEFBuffer.GetBuffedStat(BaseDEF, 0); }
        public FloatBuffContainer DEFBuffer { get; set; }
        public FloatBuffContainer ResistanceBuffer { get; set; }
        public BoolBuffContainer InvincibleBuffer { get; set; }
        public BoolBuffContainer SuperArmourBuffer { get; set; }
        public AudioDataSO HitSound { get; set; }
        public ParticlePlayer HitParticle { get; set; }

        [SerializeField] protected DamageManagerSO damageManager;
        [SerializeField] protected PhysicsModule _physics;

        public Action OnDie { get; set; }
        public Action OnDamaged { get; set; }
        public Action OnHealed { get; set; }
        public Action<float> OnHPChanged { get; set; }
        public Action<float> OnDEFChanged { get; set; }
        public Action<float> OnResistanceChanged { get; set; }
        public Action<bool> OnInvincibleChanged { get; set; }
        public Action<bool> OnSuperArmourChanged { get; set; }

        public override void Register(IInterfaceRegistable interfaceRegistable)
        {
            interfaceRegistable.RegisterInterface<IDamagable>(this);
        }

        public void Initialize()
        {
            MaxHPBuffer = new();
            DEFBuffer = new();
            ResistanceBuffer = new();
            InvincibleBuffer = new();
            SuperArmourBuffer = new();
            HP = MaxHP;
            OnHPChanged?.Invoke(HP);
            _prevMaxHP = MaxHP;
            MaxHPBuffer.OnBuffed += OnMaxHpChanged;
            if (!_physics) _physics = GetComponent<PhysicsModule>();
        }

        private void OnMaxHpChanged()
        {
            HP *= MaxHP / _prevMaxHP;
            _prevMaxHP = MaxHP;
            OnHPChanged?.Invoke(HP);
        }
        [Button]
        public void Damage10()
        {

            HP -= 10;
            Debug.Log($"[Damage10] {name} HP now = {HP}");
            OnDamaged?.Invoke();
        }

        public virtual void Damage(DamageDataSO damageData, IAttackable hitter, Vector3 origin)
        {
            OnDamaged?.Invoke();
            bool IsKnockback = false;
            if (damageData.knockBack > _physics.Mass && !SuperArmourBuffer.GetBuffedStat(0, isNegative: false))
            {
                _physics.KnockBack(transform.position - origin, damageData.knockBack);
                IsKnockback = true;
            }
            damageManager.Damage(damageData, hitter, this, transform.position, IsKnockback);
            OnHPChanged?.Invoke(HP);
            if (HP <= 0)
                Die();
        }

        public virtual void Die()
        {
            Debug.Log("Dead: " + gameObject.name);
            gameObject.SetActive(false);
            OnDie?.Invoke();
        }

        public virtual void Heal(int amount)
        {
            OnHealed?.Invoke();
            if (amount <= 0) return;
            HP += amount;
            if (HP > MaxHP) HP = MaxHP;
            OnHPChanged?.Invoke(HP);
        }
        //Save 시스템에서 Hp 저장
        public void ApplySavedHP(float savedHp)
        {
            // 현재 MaxHP 기준으로 clamp
            HP = Mathf.Clamp(savedHp, 0f, MaxHP);

            // "현재 MaxHP"를 기준으로 prev 동기화해서
            // 이후 MaxHP 변경 이벤트에서 HP가 또 비율 보정되는 걸 줄임
            _prevMaxHP = MaxHP;

            OnHPChanged?.Invoke(HP);
        }
    }
}