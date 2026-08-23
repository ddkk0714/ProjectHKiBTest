using System;
using UnityEngine;

namespace Combat
{
    public enum CombatAttackKind
    {
        Area,
        Bullet,
        Missile
    }

    public enum CombatAreaShape
    {
        Circle,
        Box
    }

    public enum CombatAttackMotion
    {
        Stationary,
        FollowOrigin,
        Linear,
        SeekDestination,
        Homing
    }

    [Serializable]
    public struct CombatArea
    {
        [SerializeField] private CombatAreaShape shape;
        [SerializeField, Min(0.01f)] private float radius;
        [SerializeField] private Vector2 size;
        [SerializeField] private Vector2 localOffset;
        [SerializeField] private float localAngle;

        public CombatAreaShape Shape => shape;
        public float Radius => Mathf.Max(0.01f, radius);
        public Vector2 Size => new Vector2(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y));
        public Vector2 LocalOffset => localOffset;
        public float LocalAngle => localAngle;
    }

    [CreateAssetMenu(fileName = "ComposableAttack", menuName = "Scriptable Objects/Attack/Composable Attack")]
    public sealed class CombatAttackDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private CombatAttackKind kind;

        [Header("Area and damage")]
        [SerializeField] private CombatArea area;
        [SerializeField] private DamageDataSO damageData;
        [SerializeField] private LayerMask queryLayer;
        [SerializeField] private bool hitEachTargetOnce = true;
        [SerializeField, Min(0f)] private float repeatInterval;

        [Header("Phases")]
        [SerializeField, Min(0f)] private float telegraphDuration;
        [SerializeField, Min(0.01f)] private float activeDuration = 0.1f;

        [Header("Independent movement")]
        [SerializeField] private CombatAttackMotion motion;
        [SerializeField, Min(0f)] private float speed;
        [SerializeField, Min(0f)] private float acceleration;
        [SerializeField, Min(0f)] private float homingTurnSpeed = 360f;
        [SerializeField] private bool faceDestination = true;
        [SerializeField] private bool endOnArrival;
        [SerializeField] private bool endOnDamageableHit;

        [Header("Optional world-space visuals")]
        [SerializeField] private GameObject telegraphPrefab;
        [SerializeField] private GameObject activePrefab;

        public CombatAttackKind Kind => kind;
        public CombatArea Area => area;
        public DamageDataSO DamageData => damageData;
        public LayerMask QueryLayer => damageData != null ? damageData.damageLayer : queryLayer;
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
