using UnityEngine;
using UnityEngine.Events;
using UnityEngine.U2D.Animation;

[CreateAssetMenu(fileName = "Enemy Data", menuName = "Scriptable Objects/Data/Enemy Data", order = 1)]
public class EnemyDataSO : ScriptableObject, IPhysicsBase, IAttackableBase, ITargetableBase,
IPathFindableBase, IDamagableBase, IPoolable, IAnimatableBase, IFootstepBase
{
    [field: Header("Physics")]
    [field: SerializeField] public Vector2Int Size { get; set; } = Vector2Int.one;
    [field: SerializeField] public float Mass { get; set; } = 1f;

    [field: SerializeField] public float WalkAcceleration { get; set; } = 300f;
    [field: SerializeField] public float MaxWalkSpeed { get; set; } = 5f;
    [field: SerializeField] public float SprintCoeff { get; set; } = 1.5f;
    [field: SerializeField] public float FrictionWalkInfluence { get; set; } = 1f;
    [field: SerializeField] public AudioDataSO DefaultFootstepAudio { get; set; }

    [field: SerializeField] public float GroundFriction { get; set; } = 0.6f;
    [field: SerializeField] public float AirFriction { get; set; } = 0.98f;
    [field: SerializeField] public float BounceCoeff { get; set; } = 0.3f;

    [field: SerializeField] public float GridEndureSpeed { get; set; } = 10f;
    [field: SerializeField] public float GridEndureForce { get; set; } = 50f;
    [field: SerializeField] public float StaticEndureForce { get; set; } = 100f;

    [field: SerializeField] public float StepUpTolerance { get; set; } = 0.5f;
    [field: SerializeField] public float StepDownTolerance { get; set; } = 0.2f;

    [field: SerializeField] public LayerMask WallLayer { get; set; }
    [field: SerializeField] public LayerMask FloorLayer { get; set; }
    [field: SerializeField] public LayerMask CanPushLayer { get; set; }
    

    [field: Header("Attack")]
    [field: SerializeField] public int BaseATK { get; set; } = 100;
    [field: SerializeField] public float CriticalChanceRate { get; set; } = 0.1f;
    [field: SerializeField] public float CriticalDamageRate { get; set; } = 2f;
    [field: SerializeField] public AttackDataSO[] AttackDatas { get; set; }
    [field: SerializeField] public LayerMask[] TargetLayers { get; set; }
    [field: SerializeField] public DamageParticleDataSO DamageParticle { get; set; }

    public int ID { get; set; }
    [field: SerializeField] public int PoolSize { get; set; }

    [field: Header("Health")]
    [field: SerializeField] public float BaseMaxHP { get; set; } = 100;
    [field: SerializeField] public float BaseDEF { get; set; } = 0;
    [field: SerializeField] public AudioDataSO HitSound { get; set; }
    [field: SerializeField] public ParticlePlayer HitParticle { get; set; }

    // 감정 벡터 역치 저항력 — 100이 기준치, 낮을수록 역치가 쉽게 발동됨 (emotion-vector-module-spec.md §5.3)
    [field: SerializeField] public float Mental { get; set; } = 100f;

    [field: Header("Control")]
    [field: SerializeField] public StateMachineSO StateMachine { get; set; }
    [field: Header("Visual")]
    [field: SerializeField] public SkinDataSO SkinData { get; set; }
    [field: SerializeField] public SimpleAnimationDataSO AnimationData { get; set; }
    [field: SerializeField] public SimpleAnimationDataSO EffectAnimationData { get; set; }
    [field: SerializeField] public SpriteLibraryAsset EffectSpriteLibrary { get; set; }

    public UnityEvent<int, int> OnGameObjectDisabled { get; set; }
    public float PathFindCooltime { get; set; }
    public void OnDisable()
    { }
}