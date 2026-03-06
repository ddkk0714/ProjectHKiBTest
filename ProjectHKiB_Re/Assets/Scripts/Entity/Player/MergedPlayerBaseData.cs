using System;
using UnityEditor.Animations;
using UnityEngine;
public class MergedPlayerBaseData : IMovableBase, IAttackableBase, ITargetableBase, IDodgeableBase,
IDamagableBase, ISkinableBase, IAnimatableBase, IFootstepBase
{
    public MovePoint MovePoint { get; set; }
    public float Speed { get; set; }
    public float SprintCoeff { get; set; }
    public LayerMask WallLayer { get; set; }
    public LayerMask CanPushLayer { get; set; }
    public AudioDataSO FootStepAudio { get; set; }

    public int BaseATK { get; set; }
    public float CriticalChanceRate { get; set; }
    public float CriticalDamageRate { get; set; }
    public AttackDataSO[] AttackDatas { get; set; }
    public LayerMask[] TargetLayers { get; set; }
    public DamageParticleDataSO DamageParticle { get; set; }

    public float BaseDodgeCooltime { get; set; }
    public float BaseDodgeSpeed { get; set; }
    public float InitialDodgeMaxDistance { get; set; }
    public int BaseContinuousDodgeLimit { get; set; }
    public LayerMask KeepDodgeWallLayer { get; set; }
    public float BaseKeepDodgeMaxTime { get; set; }
    public float BaseDodgeInvincibleTime { get; set; }
    public ParticlePlayer KeepDodgeParticle { get; set; }

    public float BaseMaxHP { get; set; }
    public float BaseDEF { get; set; }
    public float Mass { get; set; }
    public AudioDataSO HitSound { get; set; }
    public ParticlePlayer HitParticle { get; set; }

    public int MaxGP { get; set; }
    public int GP { get; set; }

    public SkinDataSO SkinData { get; set; }

    public StateMachineSO StateMachine { get; set; }
    public SimpleAnimationDataSO AnimationData { get; set; }
    public StatBuffCompilation JustDodgeBuff { get; set; }
    public AudioDataSO DefaultFootstepAudio { get; set; }

    public EntityTypeSO entityType;
    public GearTypeSO gearType;
}