using System.Collections.Generic;
using Movement;
using UnityEngine;

namespace Combat
{
    /// <summary>한 번 시작된 공격의 위치, 4방향, 표시, 판정, 연출, 피격 이력과 수명을 독립 소유한다.</summary>
    public sealed class CombatAttackInstance : MonoBehaviour
    {
        private const int QueryBufferSize = 128;

        private readonly Collider2D[] _queryBuffer = new Collider2D[QueryBufferSize];
        private readonly RaycastHit2D[] _castBuffer = new RaycastHit2D[QueryBufferSize];
        private readonly HashSet<int> _hitTargets = new();
        private readonly Dictionary<int, float> _lastHitTimes = new();

        private CombatAttackModule _module;
        private StateController _owner;
        private CombatAttackDefinitionSO _definition;
        private RuntimePositionReference _origin;
        private RuntimePositionReference _destination;
        private CombatAttackDirectionSource _directionSource;
        private IAttackable _attacker;
        private GameObject _telegraphVisual;
        private GameObject _activeVisual;
        private CombatAreaVisual _telegraphAreaVisual;
        private CombatAreaVisual _activeAreaVisual;
        private CombatAttackEffectPlayer _effectPlayer;
        private Vector2 _heading = Vector2.right;
        private EnumManager.AnimDir _attackDirection = EnumManager.AnimDir.D;
        private EnumManager.AnimDir _previousAttackDirection = EnumManager.AnimDir.D;
        private Vector3 _previousPosition;
        private float _speed;
        private float _elapsed;
        private float _nextDamageTime;
        private float _damageIndicatorRandomPosition;
        private int _handle;
        private string _slot;
        private bool _active;
        private bool _ended;

        public bool IsActive => _active && !_ended;

        public void Initialize(
            CombatAttackModule module,
            StateController owner,
            int handle,
            string slot,
            CombatAttackDefinitionSO definition,
            PositionReference origin,
            PositionReference destination,
            CombatAttackDirectionSource directionSource)
        {
            _module = module;
            _owner = owner;
            _handle = handle;
            _slot = slot;
            _definition = definition;
            _origin = new RuntimePositionReference(origin, owner);
            _destination = new RuntimePositionReference(destination, owner);
            _directionSource = directionSource;
            _speed = definition.Speed;
            _damageIndicatorRandomPosition = Random.value;

            owner.TryGetInterface(out _attacker);
            if (definition.DamageData == null || definition.DamageArea == null)
            {
                Debug.LogError($"{owner.name}: Attack '{definition.name}' requires DamageDataSO with downwardDamageArea.", owner);
                Cancel();
                return;
            }

            if (!_origin.TryGetPosition(out Vector3 startPosition))
            {
                Debug.LogError($"{owner.name}: Attack '{definition.name}' could not resolve its origin.", owner);
                Cancel();
                return;
            }

            transform.position = startPosition;
            _previousPosition = startPosition;
            AimAtDestination(true);
            _attackDirection = ResolveDirection(directionSource);
            _previousAttackDirection = _attackDirection;
            _telegraphVisual = CreateVisual(definition.TelegraphPrefab, false);
            UpdateVisualPlacement();

            if (definition.TelegraphDuration <= 0f)
                EnterActivePhase();
        }

        public void Retarget(PositionReference destination)
        {
            _destination = new RuntimePositionReference(destination, _owner);
            AimAtDestination(false);
            if (_directionSource == CombatAttackDirectionSource.TowardDestination)
                SetAttackDirection(ResolveDirection(_directionSource));
        }

        public void StopEffect()
        {
            if (_effectPlayer != null) _effectPlayer.StopEffect();
        }

        public void Cancel()
        {
            EndAttack();
        }

        public bool Contains(Vector3 worldPosition, bool includeTelegraph)
        {
            if (_ended || (!_active && !includeTelegraph)) return false;

            BoxData area = _definition.DamageArea;
            Quaternion rotation = _attackDirection.DirToQuaternion4();
            Vector3 center = transform.position + rotation * (Vector3)area.offset;
            Vector3 local = Quaternion.Inverse(rotation) * (worldPosition - center);
            Vector2 half = GetAreaSize(area) * 0.5f;
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
            _previousAttackDirection = _attackDirection;
            TickMovement(deltaTime);

            if (_directionSource == CombatAttackDirectionSource.MovementDirection)
                SetAttackDirection(DirectionFromVector(_heading));
            UpdateVisualPlacement();

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

        private EnumManager.AnimDir ResolveDirection(CombatAttackDirectionSource source)
        {
            switch (source)
            {
                case CombatAttackDirectionSource.OwnerAnimationDirection:
                    if (_owner != null && _owner.TryGetInterface(out IDirAnimatable animatable))
                        return animatable.AnimationDirection;
                    return EnumManager.AnimDir.D;
                case CombatAttackDirectionSource.TowardDestination:
                    if (_destination.TryGetPosition(out Vector3 targetPosition))
                        return DirectionFromVector(targetPosition - transform.position);
                    return EnumManager.AnimDir.D;
                case CombatAttackDirectionSource.MovementDirection:
                    return DirectionFromVector(_heading);
                case CombatAttackDirectionSource.Left:
                    return EnumManager.AnimDir.L;
                case CombatAttackDirectionSource.Right:
                    return EnumManager.AnimDir.R;
                case CombatAttackDirectionSource.Up:
                    return EnumManager.AnimDir.U;
                default:
                    return EnumManager.AnimDir.D;
            }
        }

        private static EnumManager.AnimDir DirectionFromVector(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.000001f) return EnumManager.AnimDir.D;
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return direction.x < 0f ? EnumManager.AnimDir.L : EnumManager.AnimDir.R;
            return direction.y < 0f ? EnumManager.AnimDir.D : EnumManager.AnimDir.U;
        }

        private void SetAttackDirection(EnumManager.AnimDir direction)
        {
            if (_attackDirection == direction) return;
            _attackDirection = direction;
            if (_effectPlayer != null) _effectPlayer.SetDirection(direction);
        }

        private void EnterActivePhase()
        {
            if (_active || _ended) return;
            _active = true;
            if (_telegraphVisual != null) Destroy(_telegraphVisual);
            _telegraphVisual = null;
            _telegraphAreaVisual = null;
            _activeVisual = CreateVisual(_definition.ActivePrefab, true);
            _nextDamageTime = Time.time;

            DamageDataSO damageData = _definition.DamageData;
            if (damageData.initialSound != null && GameManager.instance != null &&
                GameManager.instance.audioManager != null)
                GameManager.instance.audioManager.PlayAudioOneShot(
                    damageData.initialSound,
                    1f,
                    transform.position);

            PlayDirectionalParticle(damageData);
            UpdateVisualPlacement();
        }

        private void PlayDirectionalParticle(DamageDataSO damageData)
        {
            if (damageData.DLRUDamageEffects == null ||
                !damageData.DLRUDamageEffects.ContainsKey(_attackDirection))
                return;

            ParticlePlayer particle = damageData.DLRUDamageEffects[_attackDirection];
            if (particle == null || GameManager.instance == null ||
                GameManager.instance.particleManager == null)
                return;

            GameManager.instance.particleManager.PlayParticle(
                particle.GetHashCode(),
                transform,
                damageData.attatchParticleToBody);
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

            int count = QueryOverlaps(damageData.damageLayer);
            bool hitDamageable = false;
            Quaternion directionRotation = _attackDirection.DirToQuaternion4();
            Vector3 knockbackOrigin = transform.position +
                                      directionRotation * damageData.downwardDamageArea.pivot;

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

                // DamageManager가 표시 위치 난수를 IAttackable에서 읽으므로, 동시 공격에서도
                // 이 인스턴스가 시작할 때 정한 값을 동기 호출 직전에 복원한다.
                _attacker.DamageIndicatorRandomPosInfo = _damageIndicatorRandomPosition;
                damageable.Damage(damageData, _attacker, knockbackOrigin);
                _hitTargets.Add(targetId);
                _lastHitTimes[targetId] = Time.time;
                hitDamageable = true;

                if (_definition.EndOnDamageableHit)
                    break;
            }

            if (hitDamageable && damageData.camShake && GameManager.instance != null &&
                GameManager.instance.cameraManager != null)
                GameManager.instance.cameraManager.Shake();

            if (hitDamageable && _definition.EndOnDamageableHit)
                EndAttack();
        }

        private int QueryOverlaps(LayerMask layerMask)
        {
            BoxData area = _definition.DamageArea;
            Vector2 size = GetAreaSize(area);
            Quaternion currentRotation = _attackDirection.DirToQuaternion4();
            Vector2 center = transform.position + currentRotation * (Vector3)area.offset;
            int count = Physics2D.OverlapBoxNonAlloc(
                center,
                size,
                _attackDirection.DirToAngle4(),
                _queryBuffer,
                layerMask);

            // 빠른 총알/미사일이 한 프레임에 Collider를 통과해도 놓치지 않도록 이전 위치부터
            // 현재 위치까지 DamageData의 box를 sweep한다. Area 공격은 현재 범위 overlap만 사용한다.
            if (_definition.Kind == CombatAttackKind.Area || count >= QueryBufferSize)
                return count;

            Quaternion previousRotation = _previousAttackDirection.DirToQuaternion4();
            Vector2 previousCenter = _previousPosition + previousRotation * (Vector3)area.offset;
            Vector2 movement = center - previousCenter;
            float distance = movement.magnitude;
            if (distance <= 0.000001f) return count;

            int castCount = Physics2D.BoxCastNonAlloc(
                previousCenter,
                size,
                _previousAttackDirection.DirToAngle4(),
                movement / distance,
                _castBuffer,
                distance,
                layerMask);

            for (int i = 0; i < castCount && count < QueryBufferSize; i++)
            {
                Collider2D collider = _castBuffer[i].collider;
                if (collider == null || ContainsCollider(collider, count)) continue;
                _queryBuffer[count++] = collider;
            }
            return count;
        }

        private static Vector2 GetAreaSize(BoxData area)
        {
            return new Vector2(
                Mathf.Max(0.01f, Mathf.Abs(area.size.x)),
                Mathf.Max(0.01f, Mathf.Abs(area.size.y)));
        }

        private bool ContainsCollider(Collider2D collider, int count)
        {
            for (int i = 0; i < count; i++)
                if (_queryBuffer[i] == collider) return true;
            return false;
        }

        private GameObject CreateVisual(GameObject prefab, bool playEffect)
        {
            if (prefab == null) return null;
            GameObject visual = Instantiate(prefab, transform);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (visual.TryGetComponent(out CombatAreaVisual areaVisual))
            {
                if (playEffect) _activeAreaVisual = areaVisual;
                else _telegraphAreaVisual = areaVisual;
            }

            if (playEffect && visual.TryGetComponent(out CombatAttackEffectPlayer effectPlayer))
            {
                _effectPlayer = effectPlayer;
                _effectPlayer.Play(_attacker, _definition.DamageData, _attackDirection);
            }

            return visual;
        }

        private void UpdateVisualPlacement()
        {
            BoxData area = _definition.DamageArea;
            if (_telegraphAreaVisual != null)
                _telegraphAreaVisual.Apply(area, _attackDirection, transform);
            if (_activeAreaVisual != null)
                _activeAreaVisual.Apply(area, _attackDirection, transform);
            if (_effectPlayer != null)
                _effectPlayer.AlignToAttackRoot(transform);
        }

        private void UpdateTelegraphProgress()
        {
            if (_telegraphAreaVisual == null || _definition.TelegraphDuration <= 0f) return;
            _telegraphAreaVisual.SetProgress(_elapsed / _definition.TelegraphDuration);
        }

        private void EndAttack()
        {
            if (_ended) return;
            _ended = true;
            StopEffect();
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
            if (_definition == null || _definition.DamageArea == null) return;
            BoxData area = _definition.DamageArea;
            Quaternion rotation = _attackDirection.DirToQuaternion4();
            Gizmos.color = _active ? Color.red : Color.yellow;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                transform.position + rotation * (Vector3)area.offset,
                rotation,
                Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, GetAreaSize(area));
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(
                area.pivot - (Vector3)area.offset,
                0.08f);
            Gizmos.matrix = previous;
        }
#endif
    }
}
