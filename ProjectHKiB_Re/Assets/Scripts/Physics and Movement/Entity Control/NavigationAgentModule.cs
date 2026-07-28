using System.Collections.Generic;
using UnityEngine;

namespace EntityControl
{
    public interface INavigationAgent : IInitializable
    {
        NavigationStatus Status { get; }
        Vector3 Destination { get; }
        Transform Target { get; set; }
        bool HasDestination { get; }
        bool HasArrived { get; }

        void SetDestination(Vector3 destination, bool forceRepath = false);
        void ClearDestination();
        void ForceRepath();
        void StopNavigation();
        void SetBehaviour(NavigationBehaviourSO behaviour, Transform target = null);
    }

    public class NavigationAgentModule : InterfaceModule, INavigationAgent
    {
        [Header("References")]
        [SerializeField] private NavigationManager navigationManager;
        [SerializeField] private NavigationAgentProfile profile;
        [SerializeField] private NavigationBehaviourSO defaultBehaviour;
        [SerializeField] private Transform target;
        [SerializeField] private Transform[] patrolPoints;

        [Header("Runtime")]
        [SerializeField, NaughtyAttributes.ReadOnly] private NavigationStatus status;
        [SerializeField, NaughtyAttributes.ReadOnly] private Vector3 destination;
        [SerializeField, NaughtyAttributes.ReadOnly] private int currentWaypoint;

        private readonly List<NavigationPathPoint> _path = new();
        private readonly Collider2D[] _agentBuffer = new Collider2D[32];
        private readonly RaycastHit2D[] _wallBuffer = new RaycastHit2D[8];
        private readonly HashSet<int> _seenAgents = new();

        private InterfaceRegister _owner;
        private IPhysics _physics;
        private NavigationBehaviourSO _behaviour;
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
        private bool _jumpStartedForWaypoint;

        // Per-agent scratch state used by stateless NavigationBehaviourSO assets.
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

        public override void Register(IInterfaceRegistable interfaceRegistable)
        {
            interfaceRegistable.RegisterInterface<INavigationAgent>(this);
            _owner = interfaceRegistable as InterfaceRegister;
        }

        public void Initialize()
        {
            if (_initialized) return;

            if (_owner == null) _owner = GetComponent<InterfaceRegister>();
            if (_owner != null) _physics = _owner.GetInterface<IPhysics>();
            _physics ??= GetComponent<PhysicsModule>();
            if (navigationManager == null) navigationManager = FindObjectOfType<NavigationManager>();

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

            _initialized = true;
            status = NavigationStatus.Idle;
            _lastStuckPosition = Position;
            if (defaultBehaviour != null)
                SetBehaviour(defaultBehaviour, target);
        }

        private void Start()
        {
            Initialize();
        }

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

        private void OnDisable()
        {
            if (_physics != null) _physics.IsWalking = false;
            if (navigationManager != null) navigationManager.ReleaseReservations(GetInstanceID());
        }

        private void OnDestroy()
        {
            if (navigationManager != null) navigationManager.ReleaseReservations(GetInstanceID());
        }

        public void SetBehaviour(NavigationBehaviourSO behaviour, Transform newTarget = null)
        {
            if (!_initialized) Initialize();
            if (!_initialized) return;

            _behaviour?.Exit(this);
            ClearDestination();
            _behaviour = behaviour;
            if (newTarget != null) target = newTarget;
            BehaviourIndex = 0;
            BehaviourNextUpdate = 0f;
            BehaviourOrigin = Position;
            _behaviour?.Enter(this);
        }

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

        public void StopNavigation()
        {
            _behaviour?.Exit(this);
            _behaviour = null;
            ClearDestination();
        }

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

        public void ForceRepath()
        {
            if (_hasDestination) RequestPath(true);
        }

        private void RequestPath(bool force)
        {
            if (!_hasDestination) return;
            if (force && _pathPending)
            {
                _requestVersion++;
                _pathPending = false;
            }
            if (_pathPending) return;
            if (!force && Time.time < _nextRepathTime) return;

            _pathPending = true;
            status = NavigationStatus.Planning;
            _nextRepathTime = Time.time + profile.repathInterval;
            _lastPlannedDestination = destination;
            navigationManager.ReleaseReservations(GetInstanceID());
            int version = ++_requestVersion;

            navigationManager.RequestPath(Position, destination, profile, result =>
            {
                if (this == null || version != _requestVersion) return;
                _pathPending = false;

                if (!result.Succeeded || result.Path == null || result.Path.Count == 0)
                {
                    _path.Clear();
                    _physics.IsWalking = false;
                    status = NavigationStatus.Failed;
                    return;
                }

                _path.Clear();
                _path.AddRange(result.Path);
                currentWaypoint = 0;
                status = NavigationStatus.Following;
                _reservationBlockedSince = -1f;
                _stuckSince = -1f;
                _lastStuckPosition = Position;
                _lastStuckCheck = Time.time;
                _jumpStartedForWaypoint = false;
            }, GetInstanceID());
        }

        private void TickNavigation()
        {
            if (!_hasDestination) return;

            if (_pathPending)
            {
                _physics.IsWalking = false;
                return;
            }

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

            NavigationPathPoint waypoint = _path[currentWaypoint];
            if (ShouldWaitForPhysicalRecovery(waypoint.LinkType))
            {
                status = NavigationStatus.Displaced;
                _physics.IsWalking = false;
                return;
            }

            if (IsPathDisplaced())
            {
                status = NavigationStatus.Displaced;
                _physics.IsWalking = false;
                RequestPath(false);
                return;
            }

            Vector3 toWaypoint3 = waypoint.Position - Position;
            float horizontalDistance = ((Vector2)toWaypoint3).magnitude;
            float verticalDistance = Mathf.Abs(toWaypoint3.z);

            if (horizontalDistance <= profile.waypointDistance &&
                (verticalDistance <= profile.maxStepDown + profile.waypointDistance ||
                 waypoint.LinkType == NavigationLinkType.Drop))
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

            if (waypoint.LinkType == NavigationLinkType.Jump &&
                !_jumpStartedForWaypoint &&
                _physics.Ground != null)
            {
                _physics.ZVelocity = Mathf.Max(_physics.ZVelocity, profile.jumpSpeed);
                _jumpStartedForWaypoint = true;
            }

            Vector2 desired = ((Vector2)toWaypoint3).normalized;
            Vector2 steering = CalculateCrowdSteering(desired);
            steering += CalculateWallSteering(desired);
            Vector2 finalDirection = (desired + steering).normalized;
            if (finalDirection.sqrMagnitude < PhysicsManager.EPSILON)
                finalDirection = desired;

            _physics.WalkingDir = finalDirection;
            _physics.IsWalking = true;
            CheckStuck();

            if (Vector3.Distance(_lastPlannedDestination, destination) >= profile.targetMoveRepathDistance)
                RequestPath(false);
        }

        private bool ShouldWaitForPhysicalRecovery(NavigationLinkType linkType)
        {
            if (_physics.Ground != null) return false;
            if (linkType == NavigationLinkType.Jump || linkType == NavigationLinkType.Drop) return false;
            return _physics.Mode == MovementMode.Physics &&
                   (_physics.HVelocity.magnitude > profile.landingRecoverySpeed ||
                    Mathf.Abs(_physics.ZVelocity) > profile.landingRecoverySpeed);
        }

        private bool IsPathDisplaced()
        {
            if (_path.Count == 0) return false;

            float closestSq = float.MaxValue;
            int start = Mathf.Max(0, currentWaypoint - 1);
            int end = Mathf.Min(_path.Count - 1, currentWaypoint + 2);
            Vector2 position = Position;
            for (int i = start; i <= end; i++)
            {
                float sq = ((Vector2)_path[i].Position - position).sqrMagnitude;
                if (sq < closestSq) closestSq = sq;
            }
            return closestSq > profile.pathDeviationDistance * profile.pathDeviationDistance;
        }

        private Vector2 CalculateCrowdSteering(Vector2 desired)
        {
            if (profile.neighbourRadius <= 0f || profile.agentLayer.value == 0) return Vector2.zero;

            int count = Physics2D.OverlapCircleNonAlloc(
                Position,
                profile.neighbourRadius,
                _agentBuffer,
                profile.agentLayer);

            _seenAgents.Clear();
            Vector2 separation = Vector2.zero;
            Vector2 avoidance = Vector2.zero;

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

                float proximity = 1f - distance / profile.neighbourRadius;
                separation += offset / distance * proximity * profile.separationWeight;

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

        private Vector2 CalculateWallSteering(Vector2 desired)
        {
            if (desired.sqrMagnitude < PhysicsManager.EPSILON) return Vector2.zero;

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
            Vector2 tangent = Vector2.Perpendicular(nearest.normal);
            if (Vector2.Dot(tangent, desired) < 0f) tangent = -tangent;
            return tangent * profile.avoidanceWeight;
        }

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

        private void Arrive()
        {
            _path.Clear();
            currentWaypoint = 0;
            status = NavigationStatus.Arrived;
            _physics.IsWalking = false;
            navigationManager.ReleaseReservations(GetInstanceID());
        }

#if UNITY_EDITOR
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
