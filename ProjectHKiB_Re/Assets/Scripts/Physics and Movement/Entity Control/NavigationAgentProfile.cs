using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// 엔티티별 내비게이션 능력과 경로 추종/군중 회피 튜닝값을 보관한다.
    /// Project 창의 Create > Entity Control > Navigation Agent Profile로 생성한 뒤
    /// NavigationAgentModule.profile에 연결해 사용한다.
    /// 여러 Agent가 공유할 수 있는 설정 Asset이므로 런타임 상태를 추가하지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "Navigation Agent Profile", menuName = "Entity Control/Navigation Agent Profile")]
    public class NavigationAgentProfile : ScriptableObject
    {
        [Header("Body")]
        // 경로의 벽 Cast 및 근거리 회피에 사용하는 수평 반경과 필요한 수직 공간이다.
        [Min(0.05f)] public float radius = 0.35f;
        [Min(0.1f)] public float height = 1f;

        [Header("Traversal")]
        // 인접 노드의 높이 차이에 따라 Walk/Jump/Drop 링크 사용 가능 여부를 결정한다.
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
        // 도착/경유점 판정 및 목적지 이동·경로 이탈 시 재탐색 민감도다.
        [Min(0.01f)] public float arrivalDistance = 0.15f;
        [Min(0.05f)] public float waypointDistance = 0.2f;
        [Min(0.05f)] public float repathInterval = 0.35f;
        [Min(0.1f)] public float pathDeviationDistance = 1.25f;
        [Min(0f)] public float targetMoveRepathDistance = 0.5f;
        [Min(0f)] public float landingRecoverySpeed = 0.5f;

        [Header("Crowd")]
        // agentLayer를 0으로 두면 근거리 Agent 회피가 비활성화된다.
        public LayerMask agentLayer;
        [Min(0f)] public float neighbourRadius = 1.25f;
        [Min(0f)] public float separationWeight = 1.5f;
        [Min(0f)] public float avoidanceWeight = 0.8f;
        [Range(0, 100)] public int reservationPriority = 10;
        [Min(0.05f)] public float reservationDuration = 0.4f;
        [Min(0f)] public float reservationWaitBeforeRepath = 0.8f;
        [Min(0f)] public float reservedNodePathCost = 4f;

        [Header("Stuck Recovery")]
        // 지정 시간 동안 실제 이동량이 부족하면 Blocked로 판단하고 경로를 다시 요청한다.
        [Min(0.05f)] public float stuckCheckInterval = 0.5f;
        [Min(0f)] public float stuckMoveDistance = 0.08f;
        [Min(0.1f)] public float stuckTimeBeforeRepath = 1.25f;

        [Header("Path Costs")]
        // 같은 거리라면 비용이 낮은 링크를 A*가 우선하도록 만드는 추가 비용이다.
        [Min(0f)] public float slopeCost = 0.25f;
        [Min(0f)] public float stairCost = 0.4f;
        [Min(0f)] public float jumpCost = 2f;
        [Min(0f)] public float dropCost = 1f;
    }
}
