using System.Collections.Generic;
using UnityEngine;

namespace Combat
{
    /// <summary>한 번 시작된 공격의 위치, 표시, 판정, 피격 이력과 수명을 독립 소유한다.</summary>
    public sealed class CombatAttackInstance : MonoBehaviour
    {
        private const int QueryBufferSize = 128;

        private readonly Collider2D[] _queryBuffer = new Collider2D[QueryBufferSize];
        private readonly RaycastHit2D[] _castBuffer = new RaycastHit2D[QueryBufferSize];
        private readonly HashSet<int> _hitTargets = new HashSet<int>();
        private readonly Dictionary<int, float> _lastHitTimes = new Dictionary<int, float>();

        private CombatAttackModule _module;
        private StateController _owner;
        private CombatAttackDefinitionSO _definition;
        private RuntimePositionReference _origin;
        private RuntimePositionReference _destination;
        private IAttackable _attacker;
        private GameObject _telegraphVisual;
        private GameObject _activeVisual;
        private Vector2 _heading = Vector2.right;
        private float _speed;
        private float _elapsed;
        private float _nextDamageTime;
        private int _handle;
        private string _slot;
        private bool _active;
        private bool _ended;
        private Vector3 _previousPosition;

        public bool IsActive => _active && !_ended;

        public void Initialize(
            CombatAttackModule module,
            StateController owner,
            int handle,
            string slot,
            CombatAttackDefinitionSO definition,
            CombatPositionReference origin,
            CombatPositionReference destination)
        {
            _module = module;
            _owner = owner;
            _handle = handle;
            _slot = slot;
            _definition = definition;
            _origin = new RuntimePositionReference(origin, owner);
            _destination = new RuntimePositionReference(destination, owner);
            _speed = definition.Speed;

            owner.TryGetInterface(out _attacker);
            if (!_origin.TryGetPosition(out Vector3 startPosition))
            {
                Debug.LogError($"{owner.name}: Attack '{definition.name}' could not resolve its origin.", owner);
                Cancel();
                return;
            }

            transform.position = startPosition;
            _previousPosition = startPosition;
            AimAtDestination(true);
            _telegraphVisual = CreateVisual(definition.TelegraphPrefab);
            if (definition.TelegraphDuration <= 0f)
                EnterActivePhase();
        }

        public void Retarget(CombatPositionReference destination)
        {
            _destination = new RuntimePositionReference(destination, _owner);
            AimAtDestination(false);
        }

        public void Cancel()
        {
            EndAttack();
        }

        public bool Contains(Vector3 worldPosition, bool includeTelegraph)
        {
            if (_ended || (!_active && !includeTelegraph)) return false;

            Quaternion areaRotation = transform.rotation * Quaternion.Euler(0f, 0f, _definition.Area.LocalAngle);
            Vector3 center = transform.position + transform.rotation * (Vector3)_definition.Area.LocalOffset;
            Vector3 local = Quaternion.Inverse(areaRotation) * (worldPosition - center);

            if (_definition.Area.Shape == CombatAreaShape.Circle)
                return ((Vector2)local).sqrMagnitude <= _definition.Area.Radius * _definition.Area.Radius;

            Vector2 half = _definition.Area.Size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
        }

        public bool HasHit(Transform target)
        {
            if (target == null) return false;
            InterfaceRegister register = target.GetComponentInParent<InterfaceRegister>();
            int id = register != null ? register.GetInstanceID() : target.root.GetInstanceID();
            return _hitTargets.Contains(id);
        }

        private void Update()
        {
            if (_ended || _definition == null) return;

            float deltaTime = Time.deltaTime;
            _elapsed += deltaTime;
            _previousPosition = transform.position;
            TickMovement(deltaTime);

            if (!_active)
            {
                UpdateTelegraphProgress();
                if (_elapsed >= _definition.TelegraphDuration)
                    EnterActivePhase();
                return;
            }

            if (Time.time >= _nextDamageTime)
                ApplyDamage();

            if (_elapsed >= _definition.TotalDuration)
                EndAttack();
        }

        private void TickMovement(float deltaTime)
        {
            switch (_definition.Motion)
            {
                case CombatAttackMotion.Stationary:
                    if (_definition.FaceDestination) AimAtDestination(false);
                    break;

                case CombatAttackMotion.FollowOrigin:
                    if (_origin.TryGetPosition(out Vector3 originPosition))
                        transform.position = originPosition;
                    if (_definition.FaceDestination) AimAtDestination(false);
                    break;

                case CombatAttackMotion.Linear:
                    _speed += _definition.Acceleration * deltaTime;
                    transform.position += (Vector3)(_heading * (_speed * deltaTime));
                    break;

                case CombatAttackMotion.SeekDestination:
                    MoveDirectlyToDestination(deltaTime);
                    break;

                case CombatAttackMotion.Homing:
                    MoveHoming(deltaTime);
                    break;
            }
        }

        private void MoveDirectlyToDestination(float deltaTime)
        {
            if (!_destination.TryGetPosition(out Vector3 targetPosition)) return;
            Vector2 difference = (Vector2)(targetPosition - transform.position);
            float step = Mathf.Min(difference.magnitude, (_speed += _definition.Acceleration * deltaTime) * deltaTime);
            if (difference.sqrMagnitude > 0.000001f)
            {
                _heading = difference.normalized;
                transform.position += (Vector3)(_heading * step);
                ApplyHeadingRotation();
            }
            if (_definition.EndOnArrival && difference.magnitude <= Mathf.Max(step, 0.01f)) EndAttack();
        }

        private void MoveHoming(float deltaTime)
        {
            if (_destination.TryGetPosition(out Vector3 targetPosition))
            {
                Vector2 desired = (Vector2)(targetPosition - transform.position);
                if (desired.sqrMagnitude > 0.000001f)
                {
                    float currentAngle = Mathf.Atan2(_heading.y, _heading.x) * Mathf.Rad2Deg;
                    float desiredAngle = Mathf.Atan2(desired.y, desired.x) * Mathf.Rad2Deg;
                    float nextAngle = Mathf.MoveTowardsAngle(
                        currentAngle,
                        desiredAngle,
                        _definition.HomingTurnSpeed * deltaTime);
                    _heading = new Vector2(
                        Mathf.Cos(nextAngle * Mathf.Deg2Rad),
                        Mathf.Sin(nextAngle * Mathf.Deg2Rad));
                }
            }

            _speed += _definition.Acceleration * deltaTime;
            transform.position += (Vector3)(_heading * (_speed * deltaTime));
            ApplyHeadingRotation();
        }

        private void AimAtDestination(bool force)
        {
            if (!_destination.TryGetPosition(out Vector3 targetPosition)) return;
            Vector2 difference = (Vector2)(targetPosition - transform.position);
            if (difference.sqrMagnitude <= 0.000001f) return;
            if (force || _definition.Motion != CombatAttackMotion.Linear)
                _heading = difference.normalized;
            ApplyHeadingRotation();
        }

        private void ApplyHeadingRotation()
        {
            if (!_definition.FaceDestination || _heading.sqrMagnitude <= 0f) return;
            transform.rotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(_heading.y, _heading.x) * Mathf.Rad2Deg);
        }

        private void EnterActivePhase()
        {
            if (_active || _ended) return;
            _active = true;
            if (_telegraphVisual != null) Destroy(_telegraphVisual);
            _activeVisual = CreateVisual(_definition.ActivePrefab);
            _nextDamageTime = Time.time;

            DamageDataSO damageData = _definition.DamageData;
            if (damageData != null && damageData.initialSound != null &&
                GameManager.instance != null && GameManager.instance.audioManager != null)
                GameManager.instance.audioManager.PlayAudioOneShot(damageData.initialSound, 1f, transform.position);
        }

        private void ApplyDamage()
        {
            if (_definition.RepeatInterval > 0f)
                _nextDamageTime = Time.time + _definition.RepeatInterval;
            else
                _nextDamageTime = _definition.Kind == CombatAttackKind.Area
                    ? float.PositiveInfinity
                    : Time.time;

            DamageDataSO damageData = _definition.DamageData;
            if (damageData == null || _attacker == null) return;

            int count = QueryOverlaps(_definition.QueryLayer);
            bool hitDamageable = false;
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = _queryBuffer[i];
                if (collider == null) continue;
                InterfaceRegister register = collider.GetComponentInParent<InterfaceRegister>();
                if (register == null || register == _owner) continue;
                if (!register.TryGetInterface(out IDamagable damageable)) continue;

                int targetId = register.GetInstanceID();
                if (_definition.HitEachTargetOnce && _hitTargets.Contains(targetId)) continue;
                if (_definition.RepeatInterval > 0f &&
                    _lastHitTimes.TryGetValue(targetId, out float lastHit) &&
                    Time.time - lastHit < _definition.RepeatInterval)
                    continue;

                Vector3 hitPoint = collider.ClosestPoint(transform.position);
                damageable.Damage(damageData, _attacker, hitPoint);
                _hitTargets.Add(targetId);
                _lastHitTimes[targetId] = Time.time;
                hitDamageable = true;

                // 총알/미사일은 한 충돌 프레임에 겹친 모든 대상을 관통시키지 않는다.
                if (_definition.EndOnDamageableHit)
                    break;
            }

            if (hitDamageable && _definition.EndOnDamageableHit)
                EndAttack();
        }

        private int QueryOverlaps(LayerMask layerMask)
        {
            CombatArea area = _definition.Area;
            Vector2 center = transform.position + transform.rotation * (Vector3)area.LocalOffset;
            int count;
            if (area.Shape == CombatAreaShape.Circle)
                count = Physics2D.OverlapCircleNonAlloc(center, area.Radius, _queryBuffer, layerMask);
            else
            {
                float angle = transform.eulerAngles.z + area.LocalAngle;
                count = Physics2D.OverlapBoxNonAlloc(center, area.Size, angle, _queryBuffer, layerMask);
            }

            // 빠른 총알/미사일이 한 프레임에 Collider를 통과해도 놓치지 않도록 이전 위치부터
            // 현재 위치까지 공격 모양을 sweep한다. Area 공격은 현재 범위 overlap만 사용한다.
            if (_definition.Kind == CombatAttackKind.Area || count >= QueryBufferSize)
                return count;

            Vector2 previousCenter = _previousPosition + transform.rotation * (Vector3)area.LocalOffset;
            Vector2 movement = center - previousCenter;
            float distance = movement.magnitude;
            if (distance <= 0.000001f) return count;

            int castCount;
            if (area.Shape == CombatAreaShape.Circle)
            {
                castCount = Physics2D.CircleCastNonAlloc(
                    previousCenter,
                    area.Radius,
                    movement / distance,
                    _castBuffer,
                    distance,
                    layerMask);
            }
            else
            {
                float angle = transform.eulerAngles.z + area.LocalAngle;
                castCount = Physics2D.BoxCastNonAlloc(
                    previousCenter,
                    area.Size,
                    angle,
                    movement / distance,
                    _castBuffer,
                    distance,
                    layerMask);
            }

            for (int i = 0; i < castCount && count < QueryBufferSize; i++)
            {
                Collider2D collider = _castBuffer[i].collider;
                if (collider == null || ContainsCollider(collider, count)) continue;
                _queryBuffer[count++] = collider;
            }
            return count;
        }

        private bool ContainsCollider(Collider2D collider, int count)
        {
            for (int i = 0; i < count; i++)
                if (_queryBuffer[i] == collider) return true;
            return false;
        }

        private GameObject CreateVisual(GameObject prefab)
        {
            if (prefab == null) return null;
            GameObject visual = Instantiate(prefab, transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            CombatAreaVisual adapter = visual.GetComponent<CombatAreaVisual>();
            if (adapter != null) adapter.Apply(_definition.Area);
            return visual;
        }

        private void UpdateTelegraphProgress()
        {
            if (_telegraphVisual == null || _definition.TelegraphDuration <= 0f) return;
            CombatAreaVisual adapter = _telegraphVisual.GetComponent<CombatAreaVisual>();
            if (adapter != null) adapter.SetProgress(_elapsed / _definition.TelegraphDuration);
        }

        private void EndAttack()
        {
            if (_ended) return;
            _ended = true;
            if (_module != null) _module.NotifyEnded(_handle, _slot);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (!_ended && _module != null)
            {
                _ended = true;
                _module.NotifyEnded(_handle, _slot);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_definition == null) return;
            CombatArea area = _definition.Area;
            Gizmos.color = _active ? Color.red : Color.yellow;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                transform.position + transform.rotation * (Vector3)area.LocalOffset,
                transform.rotation * Quaternion.Euler(0f, 0f, area.LocalAngle),
                Vector3.one);
            if (area.Shape == CombatAreaShape.Circle)
                Gizmos.DrawWireSphere(Vector3.zero, area.Radius);
            else
                Gizmos.DrawWireCube(Vector3.zero, area.Size);
            Gizmos.matrix = previous;
        }
#endif
    }
}
