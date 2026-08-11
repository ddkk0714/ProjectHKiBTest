using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Database Manager", menuName = "Scriptable Objects/Manager/Database Manager", order = 0)]
public class DatabaseManagerSO : ScriptableObject
{
    public void SetIDamagable(IInterfaceRegistable entity, IDamagableBase data)
    => SetIDamagable(entity.GetInterface<IDamagable>(), data);
    public void SetIDamagable(IDamagableBase damagable, IDamagableBase data)
    {
        damagable.BaseMaxHP = data.BaseMaxHP;
        damagable.BaseDEF = data.BaseDEF;
        damagable.HitParticle = data.HitParticle;
        damagable.HitSound = data.HitSound;
    }

    public void SetIAttackable(IInterfaceRegistable entity, IAttackableBase data)
    => SetIAttackable(entity.GetInterface<IAttackable>(), data);
    public void SetIAttackable(IAttackableBase attackable, IAttackableBase data)
    {
        attackable.BaseATK = data.BaseATK;
        if (data.AttackDatas != null)
        {
            attackable.AttackDatas = new AttackDataSO[data.AttackDatas.Length];
            Array.Copy(data.AttackDatas, attackable.AttackDatas, data.AttackDatas.Length);
        }
        attackable.CriticalChanceRate = data.CriticalChanceRate;
        attackable.CriticalDamageRate = data.CriticalDamageRate;
        attackable.DamageParticle = data.DamageParticle;
        attackable.EffectAnimationData = data.EffectAnimationData;
        attackable.EffectSpriteLibrary = data.EffectSpriteLibrary;
    }

    public void SetITargetable(IInterfaceRegistable entity, ITargetableBase data)
    => SetITargetable(entity.GetInterface<ITargetable>(), data);
    public void SetITargetable(ITargetableBase targetable, ITargetableBase data)
    {
        targetable.TargetLayers = data.TargetLayers;
    }

    public void SetIPathFindable(IInterfaceRegistable entity, IPathFindableBase data)
    => SetIPathFindable(entity.GetInterface<IPathFindable>(), data);
    public void SetIPathFindable(IPathFindableBase pathFindable, IPathFindableBase data)
    {
        pathFindable.PathFindCooltime = data.PathFindCooltime;
    }

    public void SetIDodgeable(IInterfaceRegistable entity, IDodgeableBase data)
    => SetIDodgeable(entity.GetInterface<IDodgeable>(), data);
    public void SetIDodgeable(IDodgeableBase dodgeable, IDodgeableBase data)
    => CopyDodgeableData(dodgeable, data);

    // GearDataSO도 기어를 켤 때 같은 복사가 필요한데, ScriptableObject라 인스펙터로 이 매니저를
    // 물려 주려면 기어 에셋마다 참조를 채워야 한다. 순수 복사라 정적으로 열어 둔다.
    public static void CopyDodgeableData(IDodgeableBase dodgeable, IDodgeableBase data)
    {
        dodgeable.BaseDodgeCooltime = data.BaseDodgeCooltime;
        dodgeable.InitialDodgeMaxDistance = data.InitialDodgeMaxDistance;
        dodgeable.BaseDodgeSpeed = data.BaseDodgeSpeed;
        dodgeable.BaseContinuousDodgeLimit = data.BaseContinuousDodgeLimit;
        dodgeable.KeepDodgeWallLayer = data.KeepDodgeWallLayer;
        dodgeable.BaseKeepDodgeMaxTime = data.BaseKeepDodgeMaxTime;
        dodgeable.BaseDodgeInvincibleTime = data.BaseDodgeInvincibleTime;
        dodgeable.KeepDodgeParticle = data.KeepDodgeParticle;
        dodgeable.JustDodgeBuff = data.JustDodgeBuff;
    }

    public void SetIPhysics(IInterfaceRegistable entity, IPhysicsBase data)
    => SetIPhysics(entity.GetInterface<IPhysics>(), data);
    public void SetIPhysics(IPhysicsBase phys, IPhysicsBase data)
    {
        phys.Size = data.Size;
        phys.Mass = data.Mass;

        phys.WalkAcceleration = data.WalkAcceleration;
        phys.MaxWalkSpeed = data.MaxWalkSpeed;
        phys.SprintCoeff = data.SprintCoeff;
        phys.FrictionWalkInfluence = data.FrictionWalkInfluence;

        phys.GroundFriction = data.GroundFriction;
        phys.AirFriction = data.AirFriction;
        phys.BounceCoeff = data.BounceCoeff;

        phys.GridEndureSpeed = data.GridEndureSpeed;
        phys.GridEndureForce = data.GridEndureForce;

        phys.StepUpTolerance = data.StepUpTolerance;
        phys.StepDownTolerance = data.StepDownTolerance;

        phys.WallLayer = data.WallLayer;
        phys.FloorLayer = data.FloorLayer;
        phys.CanPushLayer = data.CanPushLayer;
    }

    public void SetISkinable(IInterfaceRegistable entity, ISkinableBase data)
    => SetISkinable(entity.GetInterface<ISkinable>(), data);
    public void SetISkinable(ISkinableBase skinable, ISkinableBase data)
    {
        skinable.SkinData = data.SkinData;
    }

    public void SetIFootstep(IInterfaceRegistable entity, IFootstepBase data)
    => SetIFootstep(entity.GetInterface<IFootstep>(), data);
    public void SetIFootstep(IFootstepBase footstep, IFootstepBase data)
    {
        footstep.DefaultFootstepAudio = data.DefaultFootstepAudio;
    }

    public void SetIAnimatable(IInterfaceRegistable entity, IAnimatableBase data)
    => SetIAnimatable(entity.GetInterface<IAnimatable>(), data);
    public void SetIAnimatable(IAnimatableBase animatable, IAnimatableBase data)
    {
        animatable.MainAnimationData = data.MainAnimationData;
        animatable.MainSpriteLibrary = data.MainSpriteLibrary;
    }

    public void SetIDirAnimatable(IInterfaceRegistable entity, IAnimatableBase data)
    => SetIDirAnimatable(entity.GetInterface<IDirAnimatable>(), data);
    public void SetIDirAnimatable(IAnimatableBase animatable, IAnimatableBase data)
    {
        animatable.MainAnimationData = data.MainAnimationData;
        animatable.MainSpriteLibrary = data.MainSpriteLibrary;
    }

    public void SetIGraffitiable(IInterfaceRegistable entity, IGraffitiableBase data)
    => SetIGraffitiable(entity.GetInterface<IGraffitiable>(), data);
    public void SetIGraffitiable(IGraffitiableBase graffitiable, IGraffitiableBase data)
    {
        graffitiable.GraffitiAttackState = data.GraffitiAttackState;
        graffitiable.GraffitiSkillState = data.GraffitiSkillState;
        graffitiable.GraffitiTinkerOffset = data.GraffitiTinkerOffset;
    }
}