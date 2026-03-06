using UnityEditor.Animations;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerBaseData", menuName = "Scriptable Objects/Data/PlayerBaseData", order = 1)]
public class PlayerBaseDataSO : ScriptableObject, IMovableBase, IAttackableBase, ITargetableBase,
IDodgeableBase, IDamagableBase, ISkinableBase, IAnimatableBase, IFootstepBase
{
    [field: NaughtyAttributes.Foldout("Move")][field: SerializeField] public float Speed { get; set; }
    [field: NaughtyAttributes.Foldout("Move")][field: SerializeField] public float SprintCoeff { get; set; }
    [field: NaughtyAttributes.Foldout("Move")][field: SerializeField] public LayerMask WallLayer { get; set; }
    [field: NaughtyAttributes.Foldout("Move")][field: SerializeField] public LayerMask CanPushLayer { get; set; }
    [field: NaughtyAttributes.Foldout("Move")][field: SerializeField] public AudioDataSO DefaultFootstepAudio { get; set; }

    [field: NaughtyAttributes.Foldout("Attack")][field: SerializeField] public int BaseATK { get; set; }
    [field: NaughtyAttributes.Foldout("Attack")][field: SerializeField] public float CriticalChanceRate { get; set; }
    [field: NaughtyAttributes.Foldout("Attack")][field: SerializeField] public float CriticalDamageRate { get; set; }
    [field: NaughtyAttributes.Foldout("Attack")][field: SerializeField] public AttackDataSO[] AttackDatas { get; set; }
    [field: NaughtyAttributes.Foldout("Attack")][field: SerializeField] public LayerMask[] TargetLayers { get; set; }
    [field: NaughtyAttributes.Foldout("Attack")][field: SerializeField] public DamageParticleDataSO DamageParticle { get; set; }

    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public float BaseDodgeCooltime { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public float InitialDodgeMaxDistance { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public float BaseDodgeSpeed { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public int BaseContinuousDodgeLimit { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public LayerMask KeepDodgeWallLayer { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public float BaseKeepDodgeMaxTime { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public float BaseDodgeInvincibleTime { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public ParticlePlayer KeepDodgeParticle { get; set; }
    [field: NaughtyAttributes.Foldout("Dodge")][field: SerializeField] public StatBuffCompilation JustDodgeBuff { get; set; }

    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public float BaseMaxHP { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public float BaseDEF { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public float Mass { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public AudioDataSO HitSound { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public ParticlePlayer HitParticle { get; set; }

    [field: NaughtyAttributes.Foldout("Graffiti")][field: SerializeField] public int MaxGP { get; set; }
    [field: NaughtyAttributes.Foldout("Graffiti")][field: SerializeField] public int GP { get; set; }

    [field: NaughtyAttributes.Foldout("Skin")][field: SerializeField] public SkinDataSO SkinData { get; set; }

    [field: NaughtyAttributes.Foldout("Control")][field: SerializeField] public StateMachineSO StateMachine { get; set; }
    [field: NaughtyAttributes.Foldout("Control")][field: SerializeField] public SimpleAnimationDataSO AnimationData { get; set; }
}