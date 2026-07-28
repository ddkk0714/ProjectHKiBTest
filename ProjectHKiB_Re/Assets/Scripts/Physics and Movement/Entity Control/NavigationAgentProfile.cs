using UnityEngine;

namespace EntityControl
{
    [CreateAssetMenu(fileName = "Navigation Agent Profile", menuName = "Entity Control/Navigation Agent Profile")]
    public class NavigationAgentProfile : ScriptableObject
    {
        [Header("Body")]
        [Min(0.05f)] public float radius = 0.35f;
        [Min(0.1f)] public float height = 1f;

        [Header("Traversal")]
        [Min(0f)] public float maxStepUp = 0.35f;
        [Min(0f)] public float maxStepDown = 0.5f;
        public bool canUseSlopes = true;
        public bool canUseStairs = true;
        public bool canJump;
        [Min(0f)] public float maxJumpHeight = 1.5f;
        [Min(0f)] public float maxJumpDistance = 1.5f;
        [Min(0f)] public float jumpSpeed = 6f;
        public bool canDrop = true;
        [Min(0f)] public float maxDropHeight = 3f;

        [Header("Path Following")]
        [Min(0.01f)] public float arrivalDistance = 0.15f;
        [Min(0.05f)] public float waypointDistance = 0.2f;
        [Min(0.05f)] public float repathInterval = 0.35f;
        [Min(0.1f)] public float pathDeviationDistance = 1.25f;
        [Min(0f)] public float targetMoveRepathDistance = 0.5f;
        [Min(0f)] public float landingRecoverySpeed = 0.5f;

        [Header("Crowd")]
        public LayerMask agentLayer;
        [Min(0f)] public float neighbourRadius = 1.25f;
        [Min(0f)] public float separationWeight = 1.5f;
        [Min(0f)] public float avoidanceWeight = 0.8f;
        [Range(0, 100)] public int reservationPriority = 10;
        [Min(0.05f)] public float reservationDuration = 0.4f;
        [Min(0f)] public float reservationWaitBeforeRepath = 0.8f;
        [Min(0f)] public float reservedNodePathCost = 4f;

        [Header("Stuck Recovery")]
        [Min(0.05f)] public float stuckCheckInterval = 0.5f;
        [Min(0f)] public float stuckMoveDistance = 0.08f;
        [Min(0.1f)] public float stuckTimeBeforeRepath = 1.25f;

        [Header("Path Costs")]
        [Min(0f)] public float slopeCost = 0.25f;
        [Min(0f)] public float stairCost = 0.4f;
        [Min(0f)] public float jumpCost = 2f;
        [Min(0f)] public float dropCost = 1f;
    }
}
