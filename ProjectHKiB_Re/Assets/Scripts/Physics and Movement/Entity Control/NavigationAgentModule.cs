using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    /// <summary>
    /// StateMachine과 다른 시스템이 구체 Module에 의존하지 않고 내비게이션을 제어하기 위한 API다.
    /// StateController.TryGetInterface&lt;INavigationAgent&gt;()로 가져와 Behaviour 또는 목적지를 지정한다.
    /// </summary>
    public interface INavigationAgent : IInitializable
    {
        /// <summary>현재 경로 계산/추종 상태. StateMachine Decision에서 사용할 수 있다.</summary>
        NavigationStatus Status { get; }
        /// <summary>마지막으로 지정한 월드 목적지.</summary>
        Vector3 Destination { get; }
        /// <summary>Chase/Flee/KeepDistance Behaviour가 참조할 대상.</summary>
        Transform Target { get; set; }
        /// <summary>현재 유효한 목적지가 지정되어 있는지 여부.</summary>
        bool HasDestination { get; }
        /// <summary>Status가 Arrived인지 간단히 확인하는 프로퍼티.</summary>
        bool HasArrived { get; }

        /// <summary>월드 목적지를 지정하고 필요하면 A* 경로를 요청한다.</summary>
        void SetDestination(Vector3 destination, bool forceRepath = false);
        /// <summary>현재 목적지와 경로만 제거하고 Behaviour는 유지한다.</summary>
        void ClearDestination();
        /// <summary>현재 위치에서 기존 목적지까지 즉시 재탐색을 요청한다.</summary>
        void ForceRepath();
        /// <summary>Behaviour와 목적지를 모두 중지한다.</summary>
        void StopNavigation();
        /// <summary>지속적으로 목적지를 선택할 이동 패턴과 선택적 Target을 지정한다.</summary>
        void SetBehaviour(NavigationBehaviourSO behaviour, Transform target = null);
    }

    /// <summary>
    /// 엔티티 한 개의 고수준 이동을 담당하는 InterfaceModule이다.
    /// Entity와 같은 GameObject에 IPhysics 구현 및 이 Module을 부착하고,
    /// NavigationManager와 NavigationAgentProfile을 Inspector에서 연결한다.
    /// Behaviour가 목적지를 선택하면 이 Module이 경로 요청, 추종, 점프, 회피,
    /// 예약, 이탈/끼임 복구를 수행하고 최종 이동 의도를 IPhysics에 전달한다.
    /// </summary>
    public class NavigationAgentModule : InterfaceModule, INavigationAgent
    {
        [Header("References")]
        // Scene의 공유 경로 매니저. 비어 있으면 Initialize에서 한 번 FindObjectOfType으로 탐색한다.
        [SerializeField] private NavigationManager navigationManager;
        // 이 엔티티의 크기, 이동 능력, 재탐색/군중 회피 튜닝 Asset.
        [SerializeField] private NavigationAgentProfile profile;
        // 초기화 직후 자동 시작할 패턴. 상태 머신이 즉시 지정한다면 비워 둘 수 있다.
        [SerializeField] private NavigationBehaviourSO defaultBehaviour;
        // 추적/도주/거리 유지 패턴이 사용할 기본 대상.
        [SerializeField] private Transform target;
        // Patrol Behaviour가 순회할 Scene Transform 목록.
        [SerializeField] private Transform[] patrolPoints;

        [Header("Runtime")]
        // 아래 값은 디버깅 확인용이며 런타임 API를 통해 갱신된다.
        [SerializeField, NaughtyAttributes.ReadOnly] private NavigationStatus status;
        [SerializeField, NaughtyAttributes.ReadOnly] private Vector3 destination;
        [SerializeField, NaughtyAttributes.ReadOnly] private int currentWaypoint;

        // 현재 경로와 NonAlloc 물리 검사 버퍼. 밀집도가 버퍼보다 크면 일부 이웃이 제외될 수 있다.
        private readonly List<NavigationPathPoint> _path = new(32);
        private readonly Collider2D[] _agentBuffer = new Collider2D[32];
        private readonly RaycastHit2D[] _wallBuffer = new RaycastHit2D[8];
        private readonly HashSet<int> _seenAgents = new(32);

        // 등록된 Owner와 실제 이동을 수행하는 IPhysics 참조.
        private InterfaceRegister _owner;
        private IPhysics _physics;
        private NavigationBehaviourSO _behaviour;
        // 경로 요청/재탐색/예약/끼임 감지를 위한 Agent별 런타임 상태.
        private bool _initialized;
        private bool _hasDestination;
        private bool _pathPending;
        private int _requestVersion;
        private float _nextRepathTime;
        private float _reservationBlockedSince = -1f;
        private float _lastStuckCheck;
        private float _stuckSince = -1f;
        private Vector3 _lastStuckPosition;
        private Vector3 _lastPlannedDestination;
        // A* 결과에는 시작 노드가 포함되지 않으므로 첫 waypoint까지의 경로 선분 시작점은 별도로 보관한다.
        private Vector3 _pathStartPosition;
        private bool _jumpStartedForWaypoint;
        // Method group을 한 번만 delegate로 만들고 모든 경로 요청에서 재사용해 Callback closure allocation을 없앤다.
        private System.Action<int, NavigationPathResult> _pathResultCallback;

        // 공유 BehaviourSO가 런타임 mutable 상태를 갖지 않도록 Agent별 임시 값을 이곳에 보관한다.
        internal int BehaviourIndex { get; set; }
        internal float BehaviourNextUpdate { get; set; }
        internal Vector3 BehaviourOrigin { get; set; }

        public NavigationStatus Status => status;
        public Vector3 Destination => destination;
        public Transform Target { get => target; set => target = value; }
        public bool HasDestination => _hasDestination;
        public bool HasArrived => status == NavigationStatus.Arrived;
        public NavigationManager Manager => navigationManager;
        public NavigationAgentProfile Profile => profile;
        public IPhysics Physics => _physics;
        public IReadOnlyList<Transform> PatrolPoints => patrolPoints;
        public Vector3 Position => _physics != null ? _physics.Position : transform.position;

        /// <summary>
        /// Entity의 인터페이스 딕셔너리에 INavigationAgent로 등록한다.
        /// StateController.RegisterModules()가 같은 GameObject의 Module을 순회하며 호출한다.
        /// </summary>
        public override void Register(IInterfaceRegistable interfaceRegistable)
        {
            interfaceRegistable.RegisterInterface<INavigationAgent>(this);
            _owner = interfaceRegistable as InterfaceRegister;
        }

        /// <summary>
        /// IPhysics, NavigationManager, Profile을 검증하고 기본 Behaviour를 시작한다.
        /// 일반 Entity에서는 RegisterModules/Data 주입 후 InitializeModules()를 통해 호출한다.
        /// 누락된 경우 첫 Update에서도 한 번 지연 초기화를 시도한다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            // 1) 등록된 인터페이스를 우선 사용하고, 초기화 순서가 이른 경우 같은 GameObject의 Module을 fallback으로 찾는다.
            if (_owner == null) _owner = GetComponent<InterfaceRegister>();
            if (_owner != null) _physics = _owner.GetInterface<IPhysics>();
            _physics ??= GetComponent<PhysicsModule>();
            if (navigationManager == null) navigationManager = FindObjectOfType<NavigationManager>();

            // 2) 필수 의존성이 없으면 잘못된 이동을 방지하기 위해 Module을 비활성화한다.
            if (_physics == null)
            {
                Debug.LogError($"{name}: NavigationAgentModule requires IPhysics.", this);
                enabled = false;
                return;
            }
            if (navigationManager == null)
            {
                Debug.LogError($"{name}: NavigationManager was not found.", this);
                enabled = false;
                return;
            }
            if (profile == null)
            {
                Debug.LogError($"{name}: NavigationAgentProfile is missing.", this);
                enabled = false;
                return;
            }

            // 3) 런타임 상태를 설정하고 Inspector 기본 패턴이 있으면 시작한다.
            _initialized = true;
            _pathResultCallback ??= OnPathResult;
            status = NavigationStatus.Idle;
            _lastStuckPosition = Position;
            if (defaultBehaviour != null)
                SetBehaviour(defaultBehaviour, target);
        }

        /// <summary>Behaviour가 목적지를 갱신한 뒤 현재 경로를 한 프레임 추종한다.</summary>
        private void Update()
        {
            if (!_initialized)
            {
                Initialize();
                if (!_initialized) return;
            }

            _behaviour?.Tick(this, Time.deltaTime);
            TickNavigation();
        }

        /// <summary>풀링/비활성화 시 보행 입력과 군중 예약을 정리한다.</summary>
        private void OnDisable()
        {
            if (_physics != null) _physics.IsWalking = false;
            if (navigationManager != null) navigationManager.ReleaseReservations(GetInstanceID());
        }

        /// <summary>파괴 시 Manager에 남아 있을 수 있는 예약을 정리한다.</summary>
        private void OnDestroy()
        {
            if (navigationManager != null) navigationManager.ReleaseReservations(GetInstanceID());
        }

        /// <summary>
        /// 현재 Behaviour를 종료하고 새 이동 패턴을 시작한다.
        /// StateMachine의 SetNavigationBehaviourAction에서 호출하는 것이 일반적이다.
        /// null을 전달하면 목적지 없이 정지한 상태가 된다.
        /// </summary>
        public void SetBehaviour(NavigationBehaviourSO behaviour, Transform newTarget = null)
        {
            if (!_initialized) Initialize();
            if (!_initialized) return;

            // 이전 패턴의 정리 → 경로 초기화 → Agent별 Behaviour 임시 상태 초기화 → 새 패턴 진입 순서다.
            if (_behaviour) _behaviour.Exit(this);
            ClearDestination();
            _behaviour = behaviour;
            if (newTarget != null) target = newTarget;
            BehaviourIndex = 0;
            BehaviourNextUpdate = 0f;
            BehaviourOrigin = Position;
            if (_behaviour) _behaviour.Enter(this);
        }

        /// <summary>
        /// 새 목적지를 설정한다. 이전 목적지와 충분히 다르거나 실패/막힘 상태일 때 경로를 요청한다.
        /// 움직이는 Target을 추적할 때는 매 프레임 force하지 말고 Behaviour의 갱신 주기를 사용한다.
        /// </summary>
        public void SetDestination(Vector3 newDestination, bool forceRepath = false)
        {
            if (!_initialized) Initialize();
            if (!_initialized) return;

            bool moved = !_hasDestination ||
                         Vector3.Distance(destination, newDestination) >= profile.targetMoveRepathDistance;
            destination = newDestination;
            _hasDestination = true;

            if (forceRepath || moved || status == NavigationStatus.Failed || status == NavigationStatus.Blocked)
                RequestPath(forceRepath);
        }

        /// <summary>
        /// 처리 중인 Callback을 requestVersion으로 무효화하고 경로/목적지/예약을 제거한다.
        /// 현재 Behaviour는 유지되므로 다음 Tick에서 새 목적지를 선택할 수 있다.
        /// </summary>
        public void ClearDestination()
        {
            _requestVersion++;
            _pathPending = false;
            _hasDestination = false;
            _path.Clear();
            currentWaypoint = 0;
            status = NavigationStatus.Idle;
            _reservationBlockedSince = -1f;
            _jumpStartedForWaypoint = false;
            if (_physics != null) _physics.IsWalking = false;
            if (navigationManager != null) navigationManager.ReleaseReservations(GetInstanceID());
        }

        /// <summary>현재 Behaviour까지 종료하고 완전히 정지한다. State ExitAction 등에 사용한다.</summary>
        public void StopNavigation()
        {
            if (_behaviour) _behaviour.Exit(this);
            _behaviour = null;
            ClearDestination();
        }

        /// <summary>
        /// Behaviour는 유지하면서 현재 위치에 정지하고 Arrived로 표시한다.
        /// Chase의 정지 거리나 KeepDistance의 허용 구간에서 사용한다.
        /// </summary>
        public void HoldPosition()
        {
            if (_physics != null) _physics.IsWalking = false;
            _hasDestination = false;
            _path.Clear();
            currentWaypoint = 0;
            _requestVersion++;
            _pathPending = false;
            status = NavigationStatus.Arrived;
            if (navigationManager != null) navigationManager.ReleaseReservations(GetInstanceID());
        }

        /// <summary>목적지가 있다면 쿨다운과 관계없이 현재 위치에서 경로를 다시 계산한다.</summary>
        public void ForceRepath()
        {
            if (_hasDestination) RequestPath(true);
        }

        /// <summary>
        /// 현재 위치와 destination을 Manager Queue에 제출한다.
        /// requestVersion이 일치하는 최신 Callback만 적용해 오래된 비동기 결과의 역전을 막는다.
        /// </summary>
        private void RequestPath(bool force)
        {
            // 1) 목적지/중복 요청/재탐색 쿨다운을 검사한다. force는 진행 중 요청도 무효화한다.
            if (!_hasDestination) return;
            if (force && _pathPending)
            {
                _requestVersion++;
                _pathPending = false;
            }
            if (_pathPending) return;
            if (!force && Time.time < _nextRepathTime) return;

            // 2) Planning 상태로 전환하고 기존 노드 예약을 놓아 다른 Agent가 통과할 수 있게 한다.
            _pathPending = true;
            status = NavigationStatus.Planning;
            _nextRepathTime = Time.time + profile.repathInterval;
            _lastPlannedDestination = destination;
            _pathStartPosition = Position;
            navigationManager.ReleaseReservations(GetInstanceID());
            int version = ++_requestVersion;

            // 3) 캐시된 Method delegate와 version token을 전달한다. 요청마다 closure를 만들지 않는다.
            navigationManager.RequestPath(
                Position,
                destination,
                profile,
                _pathResultCallback,
                GetInstanceID(),
                version);
        }

        /// <summary>
        /// NavigationManager가 계산을 마친 즉시 호출한다.
        /// Result Path는 Manager의 재사용 버퍼이므로 이 메서드 안에서 Agent 소유 List로 복사한다.
        /// </summary>
        private void OnPathResult(int requestVersion, NavigationPathResult result)
        {
            if (this == null || requestVersion != _requestVersion) return;
            _pathPending = false;

            if (!result.Succeeded || result.Path == null || result.Path.Count == 0)
            {
                _path.Clear();
                _physics.IsWalking = false;
                status = NavigationStatus.Failed;
                return;
            }

            // 새 경로는 첫 waypoint부터 다시 추종하며 끼임/점프 임시 상태도 초기화한다.
            _path.Clear();
            _path.AddRange(result.Path);
            currentWaypoint = 0;
            status = NavigationStatus.Following;
            _reservationBlockedSince = -1f;
            _stuckSince = -1f;
            _lastStuckPosition = Position;
            _lastStuckCheck = Time.time;
            _jumpStartedForWaypoint = false;
        }

        /// <summary>
        /// 경로 대기, 물리 복구, 경로 이탈, waypoint 도착, 예약, 점프, 조향을 순서대로 처리한다.
        /// 최종 결과는 IPhysics.WalkingDir/IsWalking/ZVelocity에 기록된다.
        /// </summary>
        private void TickNavigation()
        {
            if (!_hasDestination) return;

            // 1) 경로 계산 중에는 이전 방향으로 계속 걷지 않도록 보행을 멈춘다.
            if (_pathPending)
            {
                _physics.IsWalking = false;
                return;
            }

            // 2) 경로가 끝났다면 실제 목적지 거리로 도착 여부를 확인하고 필요하면 재탐색한다.
            if (_path.Count == 0 || currentWaypoint >= _path.Count)
            {
                float distance = Vector2.Distance(Position, destination);
                if (distance <= profile.arrivalDistance)
                {
                    Arrive();
                    return;
                }
                RequestPath(false);
                return;
            }

            // 3) 계획되지 않은 공중/강제 물리 이동은 안정될 때까지 기다리고, 경로 이탈 시 재탐색한다.
            NavigationPathPoint waypoint = _path[currentWaypoint];
            if (ShouldWaitForPhysicalRecovery(waypoint.LinkType))
            {
                status = NavigationStatus.Displaced;
                _physics.IsWalking = false;
                return;
            }

            if (IsPathDisplaced())
            {
                Debug.Log("Wait for displace recover");
                status = NavigationStatus.Displaced;
                _physics.IsWalking = false;
                RequestPath(false);
                return;
            }

            // 4) 현재 waypoint 허용 반경에 들어오면 다음 지점으로 진행한다.
            Vector3 toWaypoint3 = waypoint.Position - Position;
            float horizontalDistance = ((Vector2)toWaypoint3).magnitude;
            float verticalDistance = Mathf.Abs(toWaypoint3.z);

            if (HasReachedWaypoint(waypoint, horizontalDistance, verticalDistance))
            {
                currentWaypoint++;
                _jumpStartedForWaypoint = false;
                navigationManager.ReleaseReservations(GetInstanceID());

                if (currentWaypoint >= _path.Count)
                {
                    if (Vector2.Distance(Position, destination) <= profile.arrivalDistance)
                        Arrive();
                    else
                        RequestPath(false);
                }
                return;
            }

            // 5) 군중 충돌을 줄이기 위해 다음 노드를 선점한다. 장시간 실패하면 Blocked 및 재탐색 상태가 된다.
            if (!navigationManager.TryReserveNode(
                    waypoint.NodeId,
                    GetInstanceID(),
                    profile.reservationPriority,
                    profile.reservationDuration))
            {
                _physics.IsWalking = false;
                status = NavigationStatus.Blocked;
                if (_reservationBlockedSince < 0f) _reservationBlockedSince = Time.time;
                if (Time.time - _reservationBlockedSince >= profile.reservationWaitBeforeRepath)
                    RequestPath(false);
                return;
            }

            _reservationBlockedSince = -1f;
            status = NavigationStatus.Following;

            // 6) Jump 링크는 지상에서 waypoint당 한 번만 Z 초기 속도를 준다.
            if (waypoint.LinkType == NavigationLinkType.Jump &&
                !_jumpStartedForWaypoint &&
                _physics.Ground != null)
            {
                _physics.ZVelocity = Mathf.Max(_physics.ZVelocity, profile.jumpSpeed);
                _jumpStartedForWaypoint = true;
            }

            // 7) 경로 방향에 Agent 분리/예측 회피와 벽 접선 방향을 합성한다.
            Vector2 desired = ((Vector2)toWaypoint3).normalized;
            Vector2 steering = CalculateCrowdSteering(desired);
            steering += CalculateWallSteering(desired);
            Vector2 finalDirection = (desired + steering).normalized;
            if (finalDirection.sqrMagnitude < PhysicsManager.EPSILON)
                finalDirection = desired;

            // 8) 실제 위치 이동은 PhysicsManager가 담당하도록 이동 의도만 IPhysics에 기록한다.
            _physics.WalkingDir = finalDirection;
            _physics.IsWalking = true;
            CheckStuck();

            if (Vector3.Distance(_lastPlannedDestination, destination) >= profile.targetMoveRepathDistance)
                RequestPath(false);
        }

        /// <summary>
        /// 현재 waypoint에 도달했는지 XY와 Z를 함께 검사한다.
        /// 계단/경사는 물리가 몸체 크기를 반영해 셀 중심 표본보다 Z를 먼저 변경할 수 있으므로,
        /// 해당 표면 위에서는 XY 반경에 들어온 것을 우선해 waypoint를 지나치지 않게 한다.
        /// </summary>
        private bool HasReachedWaypoint(
            NavigationPathPoint waypoint,
            float horizontalDistance,
            float verticalDistance)
        {
            if (horizontalDistance > profile.waypointDistance) return false;

            if (waypoint.LinkType == NavigationLinkType.Drop ||
                verticalDistance <= profile.maxStepDown + profile.waypointDistance)
                return true;

            // 경로상 Stair/Slope 링크는 XY 도달로 충분하다.
            if (waypoint.LinkType == NavigationLinkType.Stair ||
                waypoint.LinkType == NavigationLinkType.Slope)
                return true;

            // 계단 시작 셀이 평면 Walk로 표본화된 경우에도 실제 Ground 정보를 통해 보정한다.
            ZCollider2D ground = _physics.Ground;
            return ground != null &&
                   (ground.isStair || ground.useSlopeDU || ground.useSlopeRL);
        }

        /// <summary>
        /// Jump/Drop 계획 없이 넉백이나 낙하로 공중에 떠 있는 동안 경로 추종을 잠시 멈출지 판단한다.
        /// </summary>
        private bool ShouldWaitForPhysicalRecovery(NavigationLinkType linkType)
        {
            if (_physics.Ground != null) return false;
            if (linkType == NavigationLinkType.Jump || linkType == NavigationLinkType.Drop) return false;
            return _physics.Mode == MovementMode.Physics &&
                   (_physics.HVelocity.magnitude > profile.landingRecoverySpeed ||
                    Mathf.Abs(_physics.ZVelocity) > profile.landingRecoverySpeed);
        }

        /// <summary>
        /// 현재 이동 중인 경로 선분과의 XY 거리가 허용 범위를 넘었는지 검사한다.
        /// 첫 waypoint 앞에는 A* 결과에 포함되지 않은 경로 요청 시작점을 선분 시작점으로 사용한다.
        /// 예상치 못한 밀림 이후 기존 경로가 더 이상 유효하지 않은 상황을 찾는다.
        /// </summary>
        private bool IsPathDisplaced()
        {
            if (_path.Count == 0) return false;

            Vector2 position = Position;
            Vector2 segmentStart = currentWaypoint > 0
                ? (Vector2)_path[currentWaypoint - 1].Position
                : (Vector2)_pathStartPosition;
            Vector2 segmentEnd = _path[currentWaypoint].Position;

            // waypoint 간격이 cellSize보다 크거나 대각선이어도 선분 위에 있다면 정상 경로로 취급한다.
            float closestSq = DistanceToSegmentSquared(position, segmentStart, segmentEnd);
            return closestSq > profile.pathDeviationDistance * profile.pathDeviationDistance;
        }

        /// <summary>점과 유한 선분 사이의 XY 제곱 거리를 할당 없이 계산한다.</summary>
        private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSq = segment.sqrMagnitude;
            if (lengthSq <= PhysicsManager.EPSILON)
                return (point - start).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSq);
            Vector2 closest = start + segment * t;
            return (point - closest).sqrMagnitude;
        }

        /// <summary>
        /// agentLayer의 가까운 IPhysics 엔티티를 찾아 겹침 분리와 진행 방향 충돌 회피 벡터를 계산한다.
        /// 반환값은 경로 desired 방향에 더할 보정값이며 실제 이동은 수행하지 않는다.
        /// </summary>
        private Vector2 CalculateCrowdSteering(Vector2 desired)
        {
            if (profile.neighbourRadius <= 0f || profile.agentLayer.value == 0) return Vector2.zero;

            // 1) Layer로 필터링한 근거리 Collider 후보를 NonAlloc 버퍼에 수집한다.
            int count = Physics2D.OverlapCircleNonAlloc(
                Position,
                profile.neighbourRadius,
                _agentBuffer,
                profile.agentLayer);

            _seenAgents.Clear();
            Vector2 separation = Vector2.zero;
            Vector2 avoidance = Vector2.zero;

            // 2) 같은 Entity의 복수 Collider를 ID로 중복 제거하고 Z가 겹치는 Agent만 계산한다.
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = _agentBuffer[i];
                if (collider == null || collider.transform == transform) continue;

                InterfaceRegister register = collider.GetComponentInParent<InterfaceRegister>();
                IPhysics other = register != null ? register.GetInterface<IPhysics>() : null;
                if (other == null || other == _physics || !_seenAgents.Add(other.ID)) continue;
                if (!_physics.ZCol.OverlapsZ(other.ZCol)) continue;

                Vector2 offset = _physics.HPosition - other.HPosition;
                float distance = offset.magnitude;
                if (distance <= PhysicsManager.EPSILON || distance > profile.neighbourRadius) continue;

                // 가까울수록 강한 Separation을 적용한다.
                float proximity = 1f - distance / profile.neighbourRadius;
                separation += offset / distance * proximity * profile.separationWeight;

                // 상대 속도가 서로 가까워지는 방향이면 원하는 진행 방향의 측면으로 비킨다.
                Vector2 relativeVelocity = _physics.HVelocity - other.HVelocity;
                float closing = Vector2.Dot(relativeVelocity, -offset.normalized);
                if (closing > 0f)
                {
                    Vector2 side = Vector2.Perpendicular(desired);
                    if (Vector2.Dot(side, offset) < 0f) side = -side;
                    avoidance += side * proximity * profile.avoidanceWeight;
                }
            }

            return separation + avoidance;
        }

        /// <summary>
        /// 진행 방향 전방을 Z-aware CircleCast하여 벽이 있으면 벽의 접선 방향 회피 벡터를 반환한다.
        /// PhysicsManager의 충돌 해결 전 자연스럽게 벽을 따라 흐르게 하는 보조 조향이다.
        /// </summary>
        private Vector2 CalculateWallSteering(Vector2 desired)
        {
            if (desired.sqrMagnitude < PhysicsManager.EPSILON) return Vector2.zero;

            // Agent가 실제로 넘을 수 있는 Step 높이 위부터 몸 상단까지의 벽만 검사한다.
            int count = ZPhysics2D.CircleCastNonAlloc(
                Position,
                profile.radius,
                desired,
                _wallBuffer,
                Mathf.Max(profile.radius * 2f, 0.5f),
                _physics.WallLayer,
                _physics.ZCol.ZMin + _physics.StepUpTolerance + PhysicsManager.EPSILON,
                _physics.ZCol.ZMax);

            if (count <= 0) return Vector2.zero;

            // 여러 충돌 중 가장 가까운 벽의 normal을 선택한다.
            RaycastHit2D nearest = default;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (_wallBuffer[i].collider == null || _wallBuffer[i].collider.transform == transform) continue;
                if (_wallBuffer[i].distance >= nearestDistance) continue;
                nearest = _wallBuffer[i];
                nearestDistance = _wallBuffer[i].distance;
            }

            if (nearest.collider == null) return Vector2.zero;
            // 현재 desired와 같은 방향 성분을 갖는 접선을 선택해 역주행을 피한다.
            Vector2 tangent = Vector2.Perpendicular(nearest.normal);
            if (Vector2.Dot(tangent, desired) < 0f) tangent = -tangent;
            return tangent * profile.avoidanceWeight;
        }

        /// <summary>
        /// 일정 주기 동안 실제 이동량을 측정해 오래 움직이지 못한 Agent를 Blocked로 표시하고 재탐색한다.
        /// 매 프레임 검사하지 않아 군중 상황의 비용과 순간적인 정지를 줄인다.
        /// </summary>
        private void CheckStuck()
        {
            if (Time.time - _lastStuckCheck < profile.stuckCheckInterval) return;

            float moved = Vector3.Distance(Position, _lastStuckPosition);
            if (moved < profile.stuckMoveDistance)
            {
                if (_stuckSince < 0f) _stuckSince = Time.time;
                if (Time.time - _stuckSince >= profile.stuckTimeBeforeRepath)
                {
                    status = NavigationStatus.Blocked;
                    RequestPath(false);
                    _stuckSince = Time.time;
                }
            }
            else
            {
                _stuckSince = -1f;
            }

            _lastStuckPosition = Position;
            _lastStuckCheck = Time.time;
        }

        /// <summary>경로 추종과 예약을 끝내고 보행을 중지한 뒤 Arrived 상태로 전환한다.</summary>
        private void Arrive()
        {
            _path.Clear();
            currentWaypoint = 0;
            status = NavigationStatus.Arrived;
            _physics.IsWalking = false;
            navigationManager.ReleaseReservations(GetInstanceID());
        }

#if UNITY_EDITOR
        /// <summary>선택된 Agent의 남은 waypoint 경로를 Scene View에 표시한다.</summary>
        private void OnDrawGizmosSelected()
        {
            if (_path.Count == 0) return;
            Gizmos.color = Color.green;
            Vector3 previous = transform.position;
            for (int i = currentWaypoint; i < _path.Count; i++)
            {
                Gizmos.DrawLine(previous, _path[i].Position);
                Gizmos.DrawSphere(_path[i].Position, 0.08f);
                previous = _path[i].Position;
            }
        }
#endif
    }
}
