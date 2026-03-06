using System;
using UnityEngine;

[CreateAssetMenu(fileName = "DamagableObjectDataSO", menuName = "Scriptable Objects/Data/DamagableObjectData", order = 0)]
public class DamagableObjectDataSO : ScriptableObject, IDamagableBase, IMovableBase, IAttackableBase
{
    [field: SerializeField] public float BaseMaxHP { get; set; }
    [field: SerializeField] public float BaseDEF { get; set; }
    [field: SerializeField] public AudioDataSO HitSound { get; set; }
    [field: SerializeField] public ParticlePlayer HitParticle { get; set; }


    [field: SerializeField] public float Mass { get; set; }
    [field: SerializeField] public float Speed { get; set; }
    [field: SerializeField] public float SprintCoeff { get; set; }
    [field: SerializeField] public LayerMask WallLayer { get; set; }
    [field: SerializeField] public AudioDataSO FootStepAudio { get; set; }
    [field: SerializeField] public bool DieWhenKnockBack { get; set; }
    [field: SerializeField] public LayerMask CanPushLayer { get; set; }

    public int BaseATK { get; set; }
    public float CriticalChanceRate { get; set; }
    public float CriticalDamageRate { get; set; }
    public AttackDataSO[] AttackDatas { get; set; }
    [field: SerializeField] public DamageParticleDataSO DamageParticle { get; set; }
}
