using UnityEngine;

namespace Combat
{
    public enum CombatAttackKind
    {
        Area,
        Bullet,
        Missile
    }

    /// <summary>StateMachine이 공격을 시작할 때 4방향을 결정하는 방법.</summary>
    public enum CombatAttackDirectionSource
    {
        OwnerAnimationDirection,
        TowardDestination,
        MovementDirection,
        Down,
        Left,
        Right,
        Up
    }

    public enum CombatAttackMotion
    {
        Stationary,
        FollowOrigin,
        Linear,
        SeekDestination,
        Homing
    }

    [CreateAssetMenu(fileName = "ComposableAttack", menuName = "Scriptable Objects/Attack/Composable Attack")]
    public sealed class CombatAttackDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private CombatAttackKind kind;
        [Tooltip("생성된 공격 인스턴스를 공격자 StateController의 Transform 자식으로 둔다. 끄면 기존처럼 Scene root에 생성한다.")]
        [SerializeField] private bool parentInstanceToOwner;

        [Header("Damage and area (single source)")]
        [Tooltip("damageLayer, downwardDamageArea, pivot, sounds, effects and animation are all read from this asset.")]
        [SerializeField] private DamageDataSO damageData;
        [SerializeField] private bool hitEachTargetOnce = true;
        [SerializeField, Min(0f)] private float repeatInterval;

        [Header("Phases")]
        [SerializeField, Min(0f)] private float telegraphDuration;
        [SerializeField, Min(0.01f)] private float activeDuration = 0.1f;

        [Header("Independent movement")]
        [NaughtyAttributes.ShowIf(nameof(kind), CombatAttackKind.Area)]
        [SerializeField] private CombatAttackMotion motion;

        [NaughtyAttributes.ShowIf(nameof(UsesSpeed))]
        [SerializeField, Min(0f)] private float speed;

        [NaughtyAttributes.ShowIf(nameof(UsesAcceleration))]
        [SerializeField, Min(0f)] private float acceleration;

        [NaughtyAttributes.ShowIf(nameof(UsesHomingTurnSpeed))]
        [SerializeField, Min(0f)] private float homingTurnSpeed = 360f;
        [SerializeField] private bool faceDestination = true;

        [NaughtyAttributes.ShowIf(nameof(CanEndOnArrival))]
        [SerializeField] private bool endOnArrival;

        [NaughtyAttributes.ShowIf(nameof(kind), CombatAttackKind.Area)]
        [SerializeField] private bool endOnDamageableHit;

        [Header("Optional world-space visuals")]
        [SerializeField] private GameObject telegraphPrefab;
        [SerializeField] private GameObject activePrefab;

        public CombatAttackKind Kind => kind;
        public bool ParentInstanceToOwner => parentInstanceToOwner;
        public DamageDataSO DamageData => damageData;
        public BoxData DamageArea => damageData != null ? damageData.downwardDamageArea : null;
        public LayerMask QueryLayer => damageData != null ? damageData.damageLayer : (LayerMask)0;
        public bool HitEachTargetOnce => hitEachTargetOnce;
        public float RepeatInterval => repeatInterval;
        public float TelegraphDuration => telegraphDuration;
        public float ActiveDuration => activeDuration;
        public float TotalDuration => telegraphDuration + activeDuration;
        public CombatAttackMotion Motion
        {
            get
            {
                if (kind == CombatAttackKind.Bullet) return CombatAttackMotion.Linear;
                if (kind == CombatAttackKind.Missile) return CombatAttackMotion.Homing;
                return motion;
            }
        }
        public float Speed => speed;
        public float Acceleration => acceleration;
        public float HomingTurnSpeed => homingTurnSpeed;
        public bool FaceDestination => faceDestination;
        public bool EndOnArrival => endOnArrival;
        public bool EndOnDamageableHit => endOnDamageableHit || kind != CombatAttackKind.Area;
        public GameObject TelegraphPrefab => telegraphPrefab;
        public GameObject ActivePrefab => activePrefab;

        private CombatAttackMotion InspectorMotion
        {
            get
            {
                if (kind == CombatAttackKind.Bullet) return CombatAttackMotion.Linear;
                if (kind == CombatAttackKind.Missile) return CombatAttackMotion.Homing;
                return motion;
            }
        }

        private bool UsesSpeed =>
            InspectorMotion == CombatAttackMotion.Linear ||
            InspectorMotion == CombatAttackMotion.SeekDestination ||
            InspectorMotion == CombatAttackMotion.Homing;

        private bool UsesAcceleration => UsesSpeed;
        private bool UsesHomingTurnSpeed => InspectorMotion == CombatAttackMotion.Homing;
        private bool CanEndOnArrival => InspectorMotion == CombatAttackMotion.SeekDestination;

        private void OnValidate()
        {
            activeDuration = Mathf.Max(0.01f, activeDuration);
            repeatInterval = Mathf.Max(0f, repeatInterval);
            speed = Mathf.Max(0f, speed);
            acceleration = Mathf.Max(0f, acceleration);
            homingTurnSpeed = Mathf.Max(0f, homingTurnSpeed);
        }
    }
}
