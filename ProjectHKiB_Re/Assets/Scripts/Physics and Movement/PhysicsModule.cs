using System.Collections.Generic;
using UnityEngine;

public enum PhysicsLayerMaskOperation
{
    Replace,
    Add,
    Remove
}

public interface IPhysicsBase
{
    public Vector2Int Size { get; set; }
    public float Mass { get; set; }

    public float WalkAcceleration { get; set; }
    public float MaxWalkSpeed { get; set; }
    public float SprintCoeff { get; set; }
    public float FrictionWalkInfluence { get; set; }

    public float GroundFriction { get; set; }
    public float AirFriction { get; set; }
    public float BounceCoeff { get; set; }

    public float GridEndureSpeed { get; set; }
    public float GridEndureForce { get; set; }
    public float StaticEndureForce { get; set; }

    public float StepUpTolerance { get; set; }
    public float StepDownTolerance { get; set; }

    public LayerMask WallLayer { get; set; }
    public LayerMask FloorLayer { get; set; }
    public LayerMask CanPushLayer { get; set; }
}

public interface IPhysics : IPhysicsBase, IInitializable
{
    public FloatBuffContainer SpeedBuffer { get; set; }
    public float BuffedWalkAcceleration { get => SpeedBuffer.GetBuffedStat(WalkAcceleration, 0); }
    public float BuffedMaxWalkSpeed { get => SpeedBuffer.GetBuffedStat(MaxWalkSpeed, 0); }
    public Vector3 LastSetDir { get; set; }
    public bool IsWalking { get; set; }
    public Vector2 WalkingDir { get; set; }
    public Vector2 WalkingVel { get; set; }
    public bool IsSprinting { get; set; }
    public Vector3 ExForce { get; set; }
    public BodyComponent[] BodyComponents { get; set; }
    public float ZPosition { get; set; }
    public Vector2 HPosition { get; set; }
    public float ZVelocity { get; set; }
    public Vector2 HVelocity { get; set; }
    public GridState Grid { get; set; }
    public PhysicsState Phys { get; set; }
    public MovementMode Mode { get; set; }
    public ZCollider2D Ground { get; set; }
    public int CanWalkFrameLeft { get; set; }
    public bool IsGroundedPrev { get; set; }
    public bool IsOnSlope { get; set; }
    public AudioDataSO FootStepAudio { get; set; }
    public float InvM { get; set; }
    public Vector3 PrevEntityPos { get; set; }
    public ZCollider2D ZCol { get; set; }
    public int ID { get; set; }
    public Vector3 CurrentWallNormal { get; set; }
    public void KnockBack(Vector3 dir, float strength);
    public void EndKnockbackEarly();
    public void KnockBackEndCallback();

    public Vector3 Position { get => new(HPosition.x, HPosition.y, ZPosition); }
    public Vector3 Velocity { get => new(HVelocity.x, HVelocity.y, ZVelocity); }

    public void SetZLevel(float z);
    public void SetBodyPartSnapOffset(Vector2 nextWorldPos);
    public void DecayBodyPartOffset(float renderDecaySpeed, float snapDecaySpeed);
    public void SnapBodyPart();

    public void LogicalTeleport(Vector3 position);
    public void RealTeleport(Vector3 position);
    public void MoveToward(Vector3 targetPos, float maxDistance);
    public void MoveTowardByPhysics(Vector3 targetPos, float arrivalTime);
    public void CancelMoveTowardByPhysics();
    public void SetMovementMode(MovementMode mode);
    public void StopMove();
    public void SetLayerOverride(
        string slot,
        bool changeWallLayer,
        PhysicsLayerMaskOperation wallOperation,
        LayerMask wallMask,
        bool changeFloorLayer,
        PhysicsLayerMaskOperation floorOperation,
        LayerMask floorMask);
    public void ClearLayerOverride(string slot);
    public void DisableAllCollisions(string slot);
    public void RestoreCollisions(string slot);
    public void ClearAllLayerOverrides();
}

public class PhysicsModule : InterfaceModule, IPhysics
{
    private sealed class PhysicsLayerOverride
    {
        public string Slot;
        public bool ChangeWallLayer;
        public PhysicsLayerMaskOperation WallOperation;
        public LayerMask WallMask;
        public bool ChangeFloorLayer;
        public PhysicsLayerMaskOperation FloorOperation;
        public LayerMask FloorMask;
    }

    public PhysicsManager physManager;
    public FloatBuffContainer SpeedBuffer { get; set; }

    [field: SerializeField] public float GroundFriction { get; set; }
    [field: SerializeField] public float BounceCoeff { get; set; }
    [field: SerializeField] public float AirFriction { get; set; }
    [field: SerializeField] public float FrictionWalkInfluence { get; set; }

    [field: SerializeField] public MovementMode Mode { get; set; }
    public GridState Grid { get; set; } = new();
    public PhysicsState Phys { get; set; } = new();
    [field: SerializeField] public Vector2Int Size { get; set; }

    [field: SerializeField] public float GridEndureSpeed { get; set; }
    [field: SerializeField] public float GridEndureForce { get; set; }
    [field: SerializeField] public float StaticEndureForce { get; set; }
    [field: SerializeField] public float StepUpTolerance { get; set; }
    [field: SerializeField] public float StepDownTolerance { get; set; }

    public LayerMask FloorLayer { get; set; }

    public float ZPosition { get => transform.position.z; set => SetZLevel(value); }
    public Vector2 HPosition { get => transform.position; set => transform.position = new Vector3(value.x, value.y, transform.position.z); }
    public float ZVelocity { get; set; }
    public Vector2 HVelocity { get; set; }
    public ZCollider2D Ground { get; set; }
    public int CanWalkFrameLeft { get; set; }
    public bool IsGroundedPrev { get; set; }
    public bool IsOnSlope { get; set; }

    [field: NaughtyAttributes.ReadOnly][field: SerializeField] public Vector3 ExForce { get; set; }
    [field: SerializeField] public float Mass { get; set; }
    [field: SerializeField] public float InvM { get; set; }
    public Vector3 LastSetDir { get; set; }
    public bool IsSprinting { get; set; }
    public bool IsWalking { get; set; }

    public Vector2 WalkingDir { get; set; }
    public Vector2 WalkingVel { get; set; }
    public float SprintCoeff { get; set; }
    [field: SerializeField] public float MaxWalkSpeed { get; set; }
    [field: SerializeField] public LayerMask WallLayer { get; set; }
    public LayerMask CanPushLayer { get; set; }
    public AudioDataSO FootStepAudio { get; set; }
    [field: SerializeField] public BodyComponent[] BodyComponents { get; set; }
    [field: SerializeField] public float WalkAcceleration { get; set; }


    public Vector3 PrevEntityPos { get; set; }

    [field: SerializeField] public ZCollider2D ZCol { get; set; }
    public int ID { get; set; }
    public Vector3 CurrentWallNormal { get; set; }

    private readonly List<PhysicsLayerOverride> _layerOverrides = new();
    private readonly List<string> _collisionDisableSlots = new();
    private LayerMask _baseWallLayer;
    private LayerMask _baseFloorLayer;
    private bool _hasLayerOverrides;

    [NaughtyAttributes.Button]
    public void Jump()
    {
        ExForce += 100 * jump * Vector3.forward;
    }
    public float jump;

    public void KnockBack(Vector3 dir, float strength) => ExForce += dir * strength;
    public void EndKnockbackEarly() { }
    public void KnockBackEndCallback() { }

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IPhysics>(this);
    }

    [NaughtyAttributes.Button]
    public void Initialize()
    {
        if (!ZCol && TryGetComponent(out ZBoxCollider2D z)) ZCol = z;
        if (!physManager) physManager = FindObjectOfType<PhysicsManager>();
        ExForce = new();
        Grid ??= new();
        Phys ??= new();
        SpeedBuffer = new();
        PrevEntityPos = transform.position;
        if (Size.x <= 0 || Size.y <= 0) Size = Vector2Int.one;
        InvM = 1f / Mass;
        ZCol.frictionCoeff = GroundFriction;
        ZCol.bounceCoeff = BounceCoeff;
        ID = GetInstanceID();
        GridEndureSpeed = GridEndureSpeed < 0 ? MaxWalkSpeed * 2 : GridEndureSpeed;
        physManager.RemovePhysicsObject(this);
        physManager.AddPhysicsObject(this);
    }

    public void SetZLevel(float z)
    {
        float d = z - transform.position.z;
        for (int i = 0; i < BodyComponents.Length; i++) BodyComponents[i].SetZ(z);
        transform.position += Vector3.forward * d;
    }

    public void SetBodyPartSnapOffset(Vector2 nextWorldPos)
    {
        for (int i = 0; i < BodyComponents.Length; i++) BodyComponents[i].SetSnapOffset(nextWorldPos);
    }
    public void DecayBodyPartOffset(float renderDecaySpeed, float snapDecaySpeed)
    {
        for (int i = 0; i < BodyComponents.Length; i++) BodyComponents[i].DecayOffsets(renderDecaySpeed, snapDecaySpeed);
    }
    public void SnapBodyPart()
    {
        for (int i = 0; i < BodyComponents.Length; i++) BodyComponents[i].Snap();
    }

    public void LogicalTeleport(Vector3 position) => physManager.LogicalTeleport(this, position);
    public void RealTeleport(Vector3 position) => physManager.RealTeleport(this, position);

    /// <summary>
    /// 이름이 같은 slot은 교체하고, 서로 다른 slot은 적용 순서대로 합성한다.
    /// 첫 override가 등록될 때 Data에서 받은 현재 LayerMask를 원본으로 캡처한다.
    /// </summary>
    public void SetLayerOverride(
        string slot,
        bool changeWallLayer,
        PhysicsLayerMaskOperation wallOperation,
        LayerMask wallMask,
        bool changeFloorLayer,
        PhysicsLayerMaskOperation floorOperation,
        LayerMask floorMask)
    {
        if (!changeWallLayer && !changeFloorLayer) return;

        string normalizedSlot = NormalizeLayerOverrideSlot(slot);
        CaptureBaseLayersIfNeeded();

        _layerOverrides.RemoveAll(layerOverride => layerOverride.Slot == normalizedSlot);
        _layerOverrides.Add(new PhysicsLayerOverride
        {
            Slot = normalizedSlot,
            ChangeWallLayer = changeWallLayer,
            WallOperation = wallOperation,
            WallMask = wallMask,
            ChangeFloorLayer = changeFloorLayer,
            FloorOperation = floorOperation,
            FloorMask = floorMask
        });
        ApplyLayerOverrides();
    }

    public void ClearLayerOverride(string slot)
    {
        if (!_hasLayerOverrides) return;

        string normalizedSlot = NormalizeLayerOverrideSlot(slot);
        if (_layerOverrides.RemoveAll(layerOverride => layerOverride.Slot == normalizedSlot) == 0)
            return;

        if (_layerOverrides.Count == 0 && _collisionDisableSlots.Count == 0)
            ClearAllLayerOverrides();
        else
            ApplyLayerOverrides();
    }

    /// <summary>
    /// 해당 slot이 유지되는 동안 Wall/Floor LayerMask를 모두 비워 모든 커스텀 물리 충돌을 막는다.
    /// 다른 Layer override보다 항상 우선하며, 여러 slot이 있으면 모두 해제될 때 복구된다.
    /// </summary>
    public void DisableAllCollisions(string slot)
    {
        string normalizedSlot = NormalizeLayerOverrideSlot(slot);
        CaptureBaseLayersIfNeeded();

        if (!_collisionDisableSlots.Contains(normalizedSlot))
            _collisionDisableSlots.Add(normalizedSlot);

        ApplyLayerOverrides();
    }

    public void RestoreCollisions(string slot)
    {
        if (!_hasLayerOverrides) return;

        string normalizedSlot = NormalizeLayerOverrideSlot(slot);
        if (!_collisionDisableSlots.Remove(normalizedSlot)) return;

        if (_layerOverrides.Count == 0 && _collisionDisableSlots.Count == 0)
            ClearAllLayerOverrides();
        else
            ApplyLayerOverrides();
    }

    public void ClearAllLayerOverrides()
    {
        _layerOverrides.Clear();
        _collisionDisableSlots.Clear();
        if (!_hasLayerOverrides) return;

        WallLayer = _baseWallLayer;
        FloorLayer = _baseFloorLayer;
        _hasLayerOverrides = false;
    }

    private void ApplyLayerOverrides()
    {
        LayerMask wallLayer = _baseWallLayer;
        LayerMask floorLayer = _baseFloorLayer;

        for (int i = 0; i < _layerOverrides.Count; i++)
        {
            PhysicsLayerOverride layerOverride = _layerOverrides[i];
            if (layerOverride.ChangeWallLayer)
                wallLayer = ApplyLayerMaskOperation(
                    wallLayer,
                    layerOverride.WallOperation,
                    layerOverride.WallMask);
            if (layerOverride.ChangeFloorLayer)
                floorLayer = ApplyLayerMaskOperation(
                    floorLayer,
                    layerOverride.FloorOperation,
                    layerOverride.FloorMask);
        }

        if (_collisionDisableSlots.Count > 0)
        {
            wallLayer = 0;
            floorLayer = 0;
        }

        WallLayer = wallLayer;
        FloorLayer = floorLayer;
    }

    private void CaptureBaseLayersIfNeeded()
    {
        if (_hasLayerOverrides) return;

        _baseWallLayer = WallLayer;
        _baseFloorLayer = FloorLayer;
        _hasLayerOverrides = true;
    }

    private static LayerMask ApplyLayerMaskOperation(
        LayerMask current,
        PhysicsLayerMaskOperation operation,
        LayerMask operand)
    {
        switch (operation)
        {
            case PhysicsLayerMaskOperation.Add:
                return current.value | operand.value;
            case PhysicsLayerMaskOperation.Remove:
                return current.value & ~operand.value;
            default:
                return operand;
        }
    }

    private static string NormalizeLayerOverrideSlot(string slot)
    {
        return string.IsNullOrWhiteSpace(slot) ? "Default" : slot.Trim();
    }

    // 수평 이동을 멈춘다. 공격을 시작할 때처럼 "여기서부터는 액션이 옮기는 만큼만 움직인다"를
    // 만들 때 쓴다. ZVelocity는 건드리지 않는다 — 낙하/점프는 별개 축이라 같이 끄면 간섭한다.
    //
    // 걷기는 HVelocity가 아니라 IsWalking/WalkingDir이 구동한다. PhysicsManager가 매 프레임
    // 그 방향으로 최대 보행속도까지 가속하므로(187~199행), 속도만 0으로 만들면 다음 프레임에
    // 도로 가속된다. WalkingDir을 갱신해 주는 WalkByInputAction이 없는 State(공격 등)에서는
    // 마지막 입력 방향으로 계속 밀려나간다 — 반드시 걷기 자체를 꺼야 한다.
    public void StopMove()
    {
        IsWalking = false;
        WalkingDir = Vector2.zero;
        HVelocity = Vector2.zero;
    }

    // 공격 돌진용. 목표 쪽으로 maxDistance까지만 가고, 목표가 더 가까우면 거기까지만 간다.
    // 벽과 다른 엔티티는 InstantMove가 격자 한 칸씩 전진하며 검사해 막히는 자리에서 멈춘다.
    // interpolate=true라 논리 위치만 옮기고 몸통은 따라붙으므로 순간이동처럼 보이지 않는다.
    public void MoveToward(Vector3 targetPos, float maxDistance)
    {
        Vector2 toTarget = (Vector2)targetPos - HPosition;
        float distance = Mathf.Min(toTarget.magnitude, maxDistance);
        if (distance <= 0f) return;

        physManager.InstantMove(this, toTarget.normalized * distance, true);
    }

    /// <summary>
    /// 위치를 직접 덮어쓰지 않고 PhysicsManager가 FixedUpdate에서 속도와 충돌을 처리하도록 요청한다.
    /// arrivalTime은 Time.time 기준의 절대 도착 시각이다.
    /// </summary>
    public void MoveTowardByPhysics(Vector3 targetPos, float arrivalTime)
    {
        if (TryResolvePhysicsManager(out PhysicsManager manager))
            manager.RequestMoveToPosition(this, targetPos, arrivalTime);
    }

    public void CancelMoveTowardByPhysics()
    {
        if (TryResolvePhysicsManager(out PhysicsManager manager))
            manager.CancelMoveToPosition(this);
    }

    public void SetMovementMode(MovementMode mode)
    {
        if (TryResolvePhysicsManager(out PhysicsManager manager))
            manager.SetMovementMode(this, mode);
    }

    private bool TryResolvePhysicsManager(out PhysicsManager manager)
    {
        if (!physManager) physManager = FindObjectOfType<PhysicsManager>();
        manager = physManager;
        if (manager) return true;

        Debug.LogError("ERROR: PhysicsManager not found.", this);
        return false;
    }

    // 인스펙터 버튼은 클릭하려고 게임 창 포커스를 빼야 해서 그 순간 이동 입력이 끊기고 IsWalking이
    // False로 찍힌다 — 이동 키를 누른 채로 확인할 수 있도록 키 입력으로도 트리거한다.
    [SerializeField] private KeyCode dumpSpeedDiagnosticsKey = KeyCode.F6;

    private void Update()
    {
        if (Input.GetKeyDown(dumpSpeedDiagnosticsKey))
            DumpSpeedDiagnostics();
    }

    private void OnDisable()
    {
        ClearAllLayerOverrides();
        Grid = new GridState();
        Phys = new PhysicsState();
        HVelocity = Vector2.zero;
    }

    // SpeedBuffType이 걸어둔 버프가 실제로 이동 속도에 반영되는지 직접 확인하기 위한 진단 도구.
    // BuffedMaxWalkSpeed/BuffedWalkAcceleration은 IPhysics 인터페이스의 디폴트 구현이라 이 클래스
    // 몸체에서 바로 안 보인다 — 인터페이스 타입으로 캐스팅해야 접근된다(C# 8 디폴트 인터페이스 멤버 특성).
    [NaughtyAttributes.Button]
    public void DumpSpeedDiagnostics()
    {
        IPhysics self = this;
        Debug.Log($"[PhysicsModule] {name}: MaxWalkSpeed={MaxWalkSpeed:F2} -> Buffed={self.BuffedMaxWalkSpeed:F2}, " +
                  $"WalkAcceleration={WalkAcceleration:F2} -> Buffed={self.BuffedWalkAcceleration:F2}, " +
                  $"SpeedBuffer(StatBuffAdd={SpeedBuffer?.StatBuffAdd:F2}, StatBuffProp={SpeedBuffer?.StatBuffProp:F2}), " +
                  $"HVelocity.magnitude={HVelocity.magnitude:F2}, IsWalking={IsWalking}, IsSprinting={IsSprinting}, " +
                  $"Mode={Mode}, CanWalkFrameLeft={CanWalkFrameLeft}, SprintCoeff={SprintCoeff:F2}");
    }
}
