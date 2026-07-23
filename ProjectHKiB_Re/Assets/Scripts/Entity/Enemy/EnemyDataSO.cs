using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.U2D.Animation;

[CreateAssetMenu(fileName = "Enemy Data", menuName = "Scriptable Objects/Data/Enemy Data", order = 1)]
public class EnemyDataSO : ScriptableObject, IMovableBase, IAttackableBase, ITargetableBase,
IPathFindableBase, IDamagableBase, IPoolable, IAnimatableBase, IFootstepBase
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

    public int ID { get; set; }
    [field: SerializeField] public int PoolSize { get; set; }

    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public float BaseMaxHP { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public float BaseDEF { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public float Mass { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public AudioDataSO HitSound { get; set; }
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public ParticlePlayer HitParticle { get; set; }

    // 감정 벡터 역치 저항력 — 100이 기준치, 낮을수록 역치가 쉽게 발동됨 (emotion-vector-module-spec.md §5.3)
    [field: NaughtyAttributes.Foldout("Health")][field: SerializeField] public float Mental { get; set; } = 100f;


    [field: NaughtyAttributes.Foldout("Control")][field: SerializeField] public StateMachineSO StateMachine { get; set; }
    [field: NaughtyAttributes.Foldout("Visual")][field: SerializeField] public SimpleAnimationDataSO AnimationData { get; set; }
    [field: NaughtyAttributes.Foldout("Visual")][field: SerializeField] public SimpleAnimationDataSO EffectAnimationData { get; set; }
    [field: NaughtyAttributes.Foldout("Visual")][field: SerializeField] public SpriteLibraryAsset EffectSpriteLibrary { get; set; }

    public UnityEvent<int, int> OnGameObjectDisabled { get; set; }
    public float PathFindCooltime { get; set; }
    public void OnDisable()
    { }
}