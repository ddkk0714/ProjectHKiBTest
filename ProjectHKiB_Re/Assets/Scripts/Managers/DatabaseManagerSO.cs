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

    public void SetIMovable(IInterfaceRegistable entity, IMovableBase data)
    => SetIMovable(entity.GetInterface<IMovable>(), data);
    public void SetIMovable(IMovableBase movable, IMovableBase data)
    {
        movable.Speed = data.Speed;
        movable.SprintCoeff = data.SprintCoeff;
        movable.WallLayer = data.WallLayer;
        movable.CanPushLayer = data.CanPushLayer;
        movable.Mass = data.Mass;
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
        animatable.AnimationData = data.AnimationData;
    }

    public void SetIDirAnimatable(IInterfaceRegistable entity, IAnimatableBase data)
    => SetIDirAnimatable(entity.GetInterface<IDirAnimatable>(), data);
    public void SetIDirAnimatable(IAnimatableBase animatable, IAnimatableBase data)
    {
        animatable.AnimationData = data.AnimationData;
    }
}