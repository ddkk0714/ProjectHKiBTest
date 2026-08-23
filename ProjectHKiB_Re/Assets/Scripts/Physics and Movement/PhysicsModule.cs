using UnityEngine;

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
    // 지금 넉백으로 밀려나는 중인가. 상태 기계가 KnockbackState로 들어갈지 판정하는 근거다
    // (KnockbackMoveDecision). KnockBack()이 켜고, 멈춰 서면 PhysicsManager가 끈다.
    public bool IsKnockedBack { get; set; }

    // 넉백을 "한 방에 속도를 꽂는" 대신 짧게 밀어내는 가속으로 표현하기 위한 상태.
    // PhysicsManager가 남은 시간 동안 매 틱 KnockbackForce를 ExForce에 더한다.
    public Vector3 KnockbackForce { get; set; }
    public float KnockbackTimeLeft { get; set; }

    // 넉백 중에만 쓰는 지면 마찰(매 틱 곱해지는 감쇠). 0이면 평소 마찰을 그대로 쓴다.
    // 1에 가까울수록 천천히 감속해 "튕겨 나가 미끄러지는" 느낌이 된다.
    public float KnockbackFriction { get; set; }

    public void KnockBack(Vector3 dir, float strength);
    public void KnockBack(Vector3 dir, float acceleration, float duration, float friction);
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
    public void StopMove();
}

public class PhysicsModule : InterfaceModule, IPhysics
{
    public PhysicsManager physManager;
    public FloatBuffContainer SpeedBuffer { get; set; }

    [field: SerializeField] public float GroundFriction { get; set; }
    [field: SerializeField] public float BounceCoeff { get; set; }
    [field: SerializeField] public float AirFriction { get; set; }
    [field: SerializeField] public float FrictionWalkInfluence { get; set; }

    [field: SerializeField] public MovementMode Mode { get; set; }
    public GridState Grid { get; set; }
    public PhysicsState Phys { get; set; }
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

    [NaughtyAttributes.Button]
    public void Jump()
    {
        ExForce += 100 * jump * Vector3.forward;
    }
    public float jump;

    [field: NaughtyAttributes.ReadOnly][field: SerializeField] public bool IsKnockedBack { get; set; }
    [field: NaughtyAttributes.ReadOnly][field: SerializeField] public Vector3 KnockbackForce { get; set; }
    [field: NaughtyAttributes.ReadOnly][field: SerializeField] public float KnockbackTimeLeft { get; set; }
    [field: NaughtyAttributes.ReadOnly][field: SerializeField] public float KnockbackFriction { get; set; }

    public void KnockBack(Vector3 dir, float strength)
    {
        ExForce += dir * strength;
        IsKnockedBack = true;
    }

    /// <summary>
    /// 짧은 가속으로 밀어낸 뒤 천천히 감속시키는 넉백.
    /// </summary>
    /// <param name="acceleration">밀어내는 가속도(units/s²). 질량을 곱해 힘으로 쓴다.</param>
    /// <param name="duration">그 가속을 주는 시간(초). 이 동안 매 물리 틱마다 힘이 더해진다.</param>
    /// <param name="friction">넉백 중 매 틱 곱해지는 감쇠. 1에 가까울수록 오래 미끄러진다. 0이면 평소 마찰.</param>
    public void KnockBack(Vector3 dir, float acceleration, float duration, float friction)
    {
        IsKnockedBack = true;
        KnockbackForce = dir.normalized * (acceleration * Mass);
        KnockbackTimeLeft = duration;
        KnockbackFriction = friction;
    }

    // 넉백을 도중에 끊는다(예: 벽에 부딪혀 멈췄을 때). 남은 가속까지 같이 지워야 계속 밀리지 않는다.
    public void EndKnockbackEarly() => ClearKnockback();
    public void KnockBackEndCallback() => ClearKnockback();

    private void ClearKnockback()
    {
        IsKnockedBack = false;
        KnockbackForce = Vector3.zero;
        KnockbackTimeLeft = 0f;
        KnockbackFriction = 0f;
    }

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
        Grid = new();
        Phys = new();
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

    // 인스펙터 버튼은 클릭하려고 게임 창 포커스를 빼야 해서 그 순간 이동 입력이 끊기고 IsWalking이
    // False로 찍힌다 — 이동 키를 누른 채로 확인할 수 있도록 키 입력으로도 트리거한다.
    [SerializeField] private KeyCode dumpSpeedDiagnosticsKey = KeyCode.F6;

    private void Update()
    {
        if (Input.GetKeyDown(dumpSpeedDiagnosticsKey))
            DumpSpeedDiagnostics();
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