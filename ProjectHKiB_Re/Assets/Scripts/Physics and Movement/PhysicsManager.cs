using System.Collections.Generic;
using UnityEngine;

// ──────────────────────────────────────────────
//  Data Structure
// ──────────────────────────────────────────────

public enum MovementMode { Grid, Physics, Static }

[System.Serializable]
public class GridState
{
    public Vector2Int CurrentCell;
    public Vector2Int TargetCell;
    public float      CellProgress;
    public Vector2Int LastMoveDir;
    public bool       IsSettling;
    public Vector2    PhysicsReturnOffset;
}

[System.Serializable]
public class PhysicsState
{
    public int KeepPhysics;
    public bool HasMoveToPositionRequest;
    public Vector2 MoveToPositionTarget;
    public float MoveToPositionArrivalTime;
    public int MoveToPositionRequestFrame;
    public bool CompleteMoveToPositionAfterStep;
}

public class PhysicsManager : MonoBehaviour
{
    private List<IPhysics> AllPhysicsEntitys = new();
    private readonly Dictionary<Vector2Int, List<IPhysics>> cellOccupancy = new();

    public const float EPSILON = 0.00001f;

    public float gravity          = -9.8f;
    public float gridSize         = 1f;
    public float stopThreshold    = 0.1f;
    public bool enable;
    public float gridSettleSpeed  = 1f;
    public float bounceTolerance  = 0.1f;
    public int keepCanWalkFrames  = 10;
    public int keepPhysicsFrames  = 4;
    public float snapDecaySpeed   = 8f;
    public float renderDecaySpeed = 8f;
    public bool interpolateRender = true;
    public int physicsIterations  = 4;
    public float stabilization    = 1f;

    private readonly Collider2D[] overlapBuffer = new Collider2D[32];
    private readonly Vector2Int[] cellBuffer = new Vector2Int[32];
    private readonly IPhysics[] objBuffer = new IPhysics[32];
    private readonly RaycastHit2D[] staticCastBuffer = new RaycastHit2D[32];

#region PUBLIC API
    public void AddPhysicsObject(IPhysics obj)
    {
        AllPhysicsEntitys.Add(obj);
        obj.Grid.CurrentCell  = WorldCenterToAnchorCell(obj.HPosition, obj.Size);
        obj.Grid.TargetCell   = obj.Grid.CurrentCell;
        obj.Grid.CellProgress = 1f;
    }
    public void RemovePhysicsObject(IPhysics obj) => AllPhysicsEntitys.Remove(obj);
    public void ResetPhysicsObjectList() => AllPhysicsEntitys.Clear();
    
    public void LogicalTeleport(IPhysics obj, Vector3 position) 
    {
        obj.HPosition = position;
        obj.ZPosition = position.z;
        obj.Grid.CurrentCell  = WorldCenterToAnchorCell(obj.HPosition, obj.Size);
        obj.Grid.TargetCell   = obj.Grid.CurrentCell;
        obj.Grid.CellProgress = 1f;
    }

    public void RealTeleport(IPhysics obj, Vector3 position)
    {
        LogicalTeleport(obj, position);
        obj.SnapBodyPart();
    }

    public void RemoveVelocity(IPhysics obj)
    {
        obj.HVelocity = Vector2.zero;
        obj.ZVelocity = 0;
    }

    /// <summary>
    /// 다음 물리 갱신부터 목표 위치와 도착 시각을 바탕으로 속도를 계산한다.
    /// 위치를 직접 변경하지 않으므로 Physics 이동의 벽/엔티티 충돌 처리를 그대로 거친다.
    /// </summary>
    public void RequestMoveToPosition(IPhysics obj, Vector3 targetPosition, float arrivalTime)
    {
        obj.Phys.HasMoveToPositionRequest = true;
        obj.Phys.MoveToPositionTarget = targetPosition;
        obj.Phys.MoveToPositionArrivalTime = arrivalTime;
        obj.Phys.MoveToPositionRequestFrame = Time.frameCount;
        obj.Phys.CompleteMoveToPositionAfterStep = false;
    }

    public void CancelMoveToPosition(IPhysics obj)
    {
        if (!obj.Phys.HasMoveToPositionRequest) return;

        obj.Phys.HasMoveToPositionRequest = false;
        obj.Phys.CompleteMoveToPositionAfterStep = false;
        obj.HVelocity = Vector2.zero;
    }

    /// <summary>자동 모드 판정 조건과 관계없이 요청한 모드로 전환한다.</summary>
    public void SetMovementMode(IPhysics obj, MovementMode mode)
    {
        switch (mode)
        {
            case MovementMode.Physics:
                SwitchToPhysics(obj, true);
                obj.Phys.KeepPhysics = keepPhysicsFrames;
                break;
            case MovementMode.Static:
                SwitchToStatic(obj, true);
                break;
            default:
                SwitchToGrid(obj, true);
                break;
        }
    }

    public void InstantMove(IPhysics obj, Vector2 displacement, bool interpolate)
    {
        SwitchToGrid(obj);
        Vector2 realDisp = obj.HVelocity * Time.fixedDeltaTime + displacement;
        Vector2 destination = obj.HPosition;
        float budget = realDisp.magnitude;
        Vector2 dir = realDisp.normalized;
        while (budget > 0)
        {
            budget -= gridSize;
            destination += dir * gridSize;
            Vector2Int destAnchor = WorldCenterToAnchorCell(destination, obj.Size);
            bool staticWall       = IsStaticWall(obj, destAnchor);
            int entityCnt         = GetOccupantsInFootprint(obj, destAnchor, objBuffer);
            if (staticWall || entityCnt > 0)
            {
                destination -= dir * gridSize;
                destAnchor = WorldCenterToAnchorCell(destination, obj.Size);
                destination = AnchorCellToWorldCenter(destAnchor, obj.Size);
                break;
            }
        }
        if (interpolate) LogicalTeleport(obj, destination);
        else             RealTeleport(obj, destination);
    }

    public void InstantMoveReversed(IPhysics obj, Vector2 displacement, bool interpolate)
    {
        SwitchToGrid(obj);
        Vector2 realDisp = obj.HVelocity * Time.fixedDeltaTime + displacement;
        Vector2 destination = obj.HPosition + realDisp;
        float budget = realDisp.magnitude;
        Vector2 dir = realDisp.normalized;
        while (budget > 0)
        {
            budget -= gridSize;
            destination -= dir * gridSize;
            Vector2Int destAnchor = WorldCenterToAnchorCell(destination, obj.Size);
            bool staticWall       = IsStaticWall(obj, destAnchor);
            int entityCnt         = GetOccupantsInFootprint(obj, destAnchor, objBuffer);
            if (staticWall || entityCnt > 0)
            {
                destination += dir * gridSize;
                destAnchor = WorldCenterToAnchorCell(destination, obj.Size);
                destination = AnchorCellToWorldCenter(destAnchor, obj.Size);
                break;
            }
        }
        if (interpolate) LogicalTeleport(obj, destination);
        else             RealTeleport(obj, destination);
    }
    
#endregion

#region MAIN PIPELINE
    private void FixedUpdate()
    {
        if (!enable) return;

        AllPhysicsEntitys = AllPhysicsEntitys.ShuffleList();

        for (int i = 0; i < AllPhysicsEntitys.Count; i++)
            UpdateVelocity(AllPhysicsEntitys[i]);

        for (int i = 0; i < AllPhysicsEntitys.Count; i++)
            PrepareMoveToPosition(AllPhysicsEntitys[i]);
        
        for (int i = 0; i < AllPhysicsEntitys.Count; i++)
            UpdateVerticalPhysics(AllPhysicsEntitys[i]);

        for (int i = 0; i < AllPhysicsEntitys.Count; i++)
            UpdateMode(AllPhysicsEntitys[i]);

        RebuildCellOccupancy();
    
        for (int i = 0; i < AllPhysicsEntitys.Count; i++)
        {
            if      (AllPhysicsEntitys[i].Mode == MovementMode.Grid
                  || AllPhysicsEntitys[i].Mode == MovementMode.Static)  UpdateGridMovement(AllPhysicsEntitys[i]);
            else if (AllPhysicsEntitys[i].Mode == MovementMode.Physics) UpdatePhysicsMovementOnlyWall(AllPhysicsEntitys[i]); 
        }

        for (int i = 0; i < physicsIterations; i++)
        {
            for (int j = 0; j < AllPhysicsEntitys.Count; j++)
                ResolveEntityCollision(AllPhysicsEntitys[j]);
        }

        for (int i = 0; i < AllPhysicsEntitys.Count; i++)
        {
            IPhysics obj = AllPhysicsEntitys[i];
            FinalizeMoveToPosition(obj);
            obj.ExForce       = Vector3.zero;
            obj.PrevEntityPos = obj.HPosition;
            obj.LastSetDir    = obj.IsWalking ? (Vector3)obj.WalkingDir : (Vector3)obj.HVelocity.normalized;
        }
    }

    private void PrepareMoveToPosition(IPhysics obj)
    {
        PhysicsState state = obj.Phys;
        if (!state.HasMoveToPositionRequest) return;

        // UpdateAction이 더 이상 실행되지 않는 상태 전환 뒤에는 남은 요청을 자동 폐기한다.
        if (Time.frameCount > state.MoveToPositionRequestFrame + 1)
        {
            CancelMoveToPosition(obj);
            return;
        }

        Vector2 displacement = state.MoveToPositionTarget - obj.HPosition;
        if (displacement.sqrMagnitude <= EPSILON * EPSILON)
        {
            CancelMoveToPosition(obj);
            return;
        }

        SwitchToPhysics(obj, true);
        obj.Phys.KeepPhysics = keepPhysicsFrames;

        float remainingTime = state.MoveToPositionArrivalTime - Time.fixedTime;
        float movementTime = Mathf.Max(Time.fixedDeltaTime, remainingTime);
        obj.HVelocity = displacement / movementTime;
        state.CompleteMoveToPositionAfterStep = remainingTime <= Time.fixedDeltaTime;
    }

    private void FinalizeMoveToPosition(IPhysics obj)
    {
        if (!obj.Phys.HasMoveToPositionRequest ||
            !obj.Phys.CompleteMoveToPositionAfterStep)
            return;

        obj.Phys.HasMoveToPositionRequest = false;
        obj.Phys.CompleteMoveToPositionAfterStep = false;
        obj.HVelocity = Vector2.zero;
    }

    private void UpdateVelocity(IPhysics obj)
    {
        obj.ExForce   += gravity * obj.Mass * Vector3.forward;
        obj.ZVelocity += obj.ExForce.z * obj.InvM * Time.fixedDeltaTime;
        
        obj.ZVelocity = ApplyAirFriction(obj, obj.ZVelocity);
        obj.HVelocity = ApplyGroundFriction(obj, obj.HVelocity);

        if (obj.IsWalking && obj.WalkingDir.sqrMagnitude > EPSILON)
        {
            float maxSpd = (obj.IsSprinting ? obj.BuffedMaxWalkSpeed * obj.SprintCoeff : obj.BuffedMaxWalkSpeed) * (obj.CanWalkFrameLeft > 0 ? 1f : 0.1f);
            float frictionAccInfluence = obj.Ground ? 1 - Mathf.Clamp01(Mathf.Max(obj.GroundFriction, obj.Ground.frictionCoeff) * obj.FrictionWalkInfluence): 1f;
            float WalkAcceleration     = (obj.IsSprinting ? obj.BuffedWalkAcceleration * obj.SprintCoeff : obj.BuffedWalkAcceleration) * (obj.CanWalkFrameLeft > 0 ? 1f : 0.1f);
            float currentAlongWalk = Vector2.Dot(obj.HVelocity, obj.WalkingDir.normalized);
            float deficit          = maxSpd - currentAlongWalk;

            if (deficit > 0f)
            {
                float accel = WalkAcceleration * frictionAccInfluence * Time.fixedDeltaTime;
                obj.HVelocity += obj.WalkingDir.normalized * Mathf.Min(accel, deficit);
            }
        }
        else if (obj.Mode == MovementMode.Grid)
        {
            if (obj.HVelocity.magnitude < gridSettleSpeed)
            {
                obj.HVelocity = Vector2.zero;
                StartGridSettle(obj);
                return;
            }
        }

        obj.HVelocity += obj.InvM * Time.fixedDeltaTime * (Vector2)obj.ExForce;
    }
#endregion

#region MODE SWITCH
    private void UpdateMode(IPhysics obj)
    {
        bool wantsGrid    = obj.Ground && !obj.IsOnSlope
                         && obj.HVelocity.magnitude < obj.GridEndureSpeed * obj.BuffedMaxWalkSpeed
                         && obj.ExForce.magnitude < obj.GridEndureForce;
        bool wantsPhysics = !wantsGrid;

        if (obj.Mode == MovementMode.Physics && obj.Phys.KeepPhysics > 0)
        {
            obj.Phys.KeepPhysics--;
            return;
        }

        if      (wantsGrid)    SwitchToGrid(obj);
        else if (wantsPhysics) SwitchToPhysics(obj);
        else if (obj.Mode == MovementMode.Static) return;
    }

    public void SwitchToPhysics(IPhysics obj) => SwitchToPhysics(obj, false);

    private void SwitchToPhysics(IPhysics obj, bool force)
    {
        if (obj.Mode == MovementMode.Physics)
        {
            if (force) obj.Phys.KeepPhysics = keepPhysicsFrames;
            return;
        }
        if (!force && obj.Mode == MovementMode.Static && obj.ExForce.magnitude < obj.StaticEndureForce) return;

        RemoveFootprintOccupant(obj, obj.Grid.CurrentCell);
        RemoveFootprintOccupant(obj, obj.Grid.TargetCell);

        obj.Mode             = MovementMode.Physics;
        obj.Phys.KeepPhysics = keepPhysicsFrames;

        Vector2Int anchor = WorldCenterToAnchorCell(obj.HPosition, obj.Size);
        AddFootprintOccupant(obj, anchor);
    }

    public void SwitchToGrid(IPhysics obj) => SwitchToGrid(obj, false);

    private void SwitchToGrid(IPhysics obj, bool force)
    {
        if (obj.Mode == MovementMode.Grid) return;
        if (!force && obj.Mode == MovementMode.Static) return;
        obj.Mode = MovementMode.Grid;

        obj.Grid.CurrentCell  = WorldCenterToAnchorCell(obj.HPosition, obj.Size);
        obj.Grid.TargetCell   = obj.Grid.CurrentCell;
        obj.Grid.CellProgress = 1f;
        obj.Grid.IsSettling   = false;

        if (obj.HVelocity.sqrMagnitude > EPSILON)
        {
            Vector2 d = obj.HVelocity.normalized;
            obj.Grid.LastMoveDir = new Vector2Int(
                Mathf.RoundToInt(d.x), Mathf.RoundToInt(d.y));
        }

        Vector2 gridCenter = AnchorCellToWorldCenter(obj.Grid.CurrentCell, obj.Size);
        obj.Grid.PhysicsReturnOffset = obj.HPosition - gridCenter;
    }

    public void SwitchToStatic(IPhysics obj) => SwitchToStatic(obj, false);

    private void SwitchToStatic(IPhysics obj, bool force)
    {
        if (obj.Mode == MovementMode.Static)
        {
            obj.HVelocity = Vector2.zero;
            return;
        }
        if (!force && obj.ExForce.magnitude >= obj.StaticEndureForce) return;
        if (obj.Mode == MovementMode.Physics) SwitchToGrid(obj, force);
        
        obj.Mode = MovementMode.Static;
        obj.HVelocity = Vector2.zero;
    }
#endregion

#region MOVEMENT
    private void UpdateGridMovement(IPhysics obj)
    {
        if (obj.Grid.IsSettling)
        {
            bool hasInput = obj.IsWalking && obj.WalkingDir.sqrMagnitude > EPSILON;
            if (!hasInput) { UpdateGridSettle(obj); return; }
            obj.Grid.IsSettling = false;
        }

        if (obj.Grid.PhysicsReturnOffset.sqrMagnitude > EPSILON) // Physics snap offset decay logic
        {
            obj.Grid.PhysicsReturnOffset = Vector2.Lerp(obj.Grid.PhysicsReturnOffset, Vector2.zero, Time.fixedDeltaTime * snapDecaySpeed);

            if (obj.Grid.PhysicsReturnOffset.sqrMagnitude < EPSILON)
                obj.Grid.PhysicsReturnOffset = Vector2.zero;
        }

        float currentSpeed = obj.HVelocity.magnitude;
        if (currentSpeed > stopThreshold && obj.Mode != MovementMode.Static)
        {
            Vector2    normalizedVel = obj.HVelocity.normalized;
            Vector2Int moveDir       = new(Mathf.RoundToInt(normalizedVel.x),
                                           Mathf.RoundToInt(normalizedVel.y));
            if (moveDir == Vector2Int.zero) moveDir = obj.Grid.LastMoveDir;

            if (obj.Grid.CellProgress >= 1f)
            {
                obj.Grid.CurrentCell  = obj.Grid.TargetCell;
                obj.Grid.CellProgress = 1f;

                obj.Grid.LastMoveDir = moveDir;
                Vector2Int desiredAnchor = obj.Grid.CurrentCell + moveDir;
                TryMoveToCellGrid(obj, desiredAnchor, moveDir);
                //if (obj.Mode != MovementMode.Grid) return;
            }

            if (obj.Grid.CurrentCell != obj.Grid.TargetCell)
            {
                float cellDistance = Vector2.Distance(AnchorCellToWorldCenter(obj.Grid.CurrentCell, obj.Size),
                                                      AnchorCellToWorldCenter(obj.Grid.TargetCell,  obj.Size));
                obj.Grid.CellProgress += currentSpeed * Time.fixedDeltaTime / cellDistance;
                obj.Grid.CellProgress  = Mathf.Clamp01(obj.Grid.CellProgress);
            }
        }
        else obj.HVelocity = Vector2.zero;
        Vector2 worldCur    = AnchorCellToWorldCenter(obj.Grid.CurrentCell, obj.Size);
        Vector2 worldTarget = AnchorCellToWorldCenter(obj.Grid.TargetCell,  obj.Size);
        Vector2 newPos      = Vector2.Lerp(worldCur, worldTarget, obj.Grid.CellProgress);

        //if (obj.Grid.CannotSettleFrameLeft < 1)
        newPos += obj.Grid.PhysicsReturnOffset; // Add the physics offset!

        obj.HPosition = new Vector3(newPos.x, newPos.y, obj.ZPosition);
    }

    private void StartGridSettle(IPhysics obj)
    {
        float cellDistance = Vector2.Distance(
            AnchorCellToWorldCenter(obj.Grid.CurrentCell, obj.Size),
            AnchorCellToWorldCenter(obj.Grid.TargetCell,  obj.Size));
        if (cellDistance < EPSILON) cellDistance = gridSize;

        obj.Grid.CellProgress += gridSettleSpeed * Time.fixedDeltaTime / cellDistance;

        if (obj.Grid.CellProgress >= 1f)
        {
            obj.Grid.CurrentCell  = obj.Grid.TargetCell;
            obj.Grid.CellProgress = 1f;
            obj.Grid.IsSettling   = false;
            SnapPositionToCell(obj, obj.Grid.TargetCell);
            return;
        }

        if (obj.Grid.CellProgress < 0.2f)
        {
            (obj.Grid.CurrentCell, obj.Grid.TargetCell) = (obj.Grid.TargetCell, obj.Grid.CurrentCell);
            obj.Grid.CellProgress = 1f - obj.Grid.CellProgress;
        }

        obj.Grid.IsSettling = true;
    }

    private void UpdateGridSettle(IPhysics obj)
    {
        if (obj.Grid.PhysicsReturnOffset.sqrMagnitude > EPSILON) // Physics offset decay
        {
            obj.Grid.PhysicsReturnOffset = Vector2.Lerp(obj.Grid.PhysicsReturnOffset, Vector2.zero, Time.fixedDeltaTime * snapDecaySpeed);
            if (obj.Grid.PhysicsReturnOffset.sqrMagnitude < EPSILON)
                obj.Grid.PhysicsReturnOffset = Vector2.zero;
        }

        obj.Grid.CellProgress += gridSettleSpeed * Time.fixedDeltaTime / gridSize;

        if (obj.Grid.CellProgress >= 1f)
        {
            obj.Grid.CellProgress = 1f;
            obj.Grid.CurrentCell  = obj.Grid.TargetCell;
            obj.Grid.IsSettling   = false;

            Vector2 targetCenter = AnchorCellToWorldCenter(obj.Grid.TargetCell, obj.Size);
            targetCenter += obj.Grid.PhysicsReturnOffset; // Add physics offset same as normal movement
            obj.HPosition = new Vector3(targetCenter.x, targetCenter.y, obj.ZPosition);
            return;
        }

        Vector2 from   = AnchorCellToWorldCenter(obj.Grid.CurrentCell, obj.Size);
        Vector2 to     = AnchorCellToWorldCenter(obj.Grid.TargetCell,  obj.Size);
        Vector2 newPos = Vector2.Lerp(from, to, obj.Grid.CellProgress);

        newPos += obj.Grid.PhysicsReturnOffset; // Add physics offset same as normal movement
        obj.HPosition = new Vector3(newPos.x, newPos.y, obj.ZPosition);
    }

    private void TryMoveToCellGrid(IPhysics obj, Vector2Int desiredAnchor, Vector2Int moveDir)
    {
        bool staticWall = IsStaticWall(obj, desiredAnchor); // Static wall check
        if (staticWall)
        {
            bool slid = TrySlideGrid(obj, moveDir);
            if (!slid) obj.HVelocity = Vector2.zero;
            return;
        }

        int cnt = GetOccupantsInFootprint(obj, desiredAnchor, objBuffer); // Entity check
        if (cnt > 0)
        {
            SwitchToPhysics(obj);
            for (int i = 0; i < cnt; i++)
                if (objBuffer[i].Mode == MovementMode.Grid)
                    SwitchToPhysics(objBuffer[i]);
            return;
        }

        obj.Grid.TargetCell   = desiredAnchor; // Front is empty!
        obj.Grid.CellProgress = 0f;
        AddFootprintOccupant(obj, desiredAnchor);
    }

    private bool TrySlideGrid(IPhysics obj, Vector2Int moveDir)
    {
        if (moveDir.x != 0)
        {
            var slideDir    = new Vector2Int(moveDir.x, 0);
            var slideAnchor = obj.Grid.CurrentCell + slideDir;
            if (!IsStaticWall(obj, slideAnchor) && GetOccupantsInFootprint(obj, slideAnchor, objBuffer) < 1)
            {
                obj.Grid.TargetCell   = slideAnchor;
                obj.Grid.CellProgress = 0f;
                obj.Grid.LastMoveDir  = slideDir;
                AddFootprintOccupant(obj, slideAnchor);
                return true;
            }
        }
        if (moveDir.y != 0)
        {
            var slideDir    = new Vector2Int(0, moveDir.y);
            var slideAnchor = obj.Grid.CurrentCell + slideDir;
            if (!IsStaticWall(obj, slideAnchor) && GetOccupantsInFootprint(obj, slideAnchor, objBuffer) < 1)
            {
                obj.Grid.TargetCell   = slideAnchor;
                obj.Grid.CellProgress = 0f;
                obj.Grid.LastMoveDir  = slideDir;
                AddFootprintOccupant(obj, slideAnchor);
                return true;
            }
        }

        obj.Grid.TargetCell   = obj.Grid.CurrentCell;
        obj.Grid.CellProgress = 1f;
        return false;
    }

    private void UpdatePhysicsMovementOnlyWall(IPhysics obj)
    {
        if (!obj.Phys.HasMoveToPositionRequest && obj.HVelocity.magnitude < stopThreshold)
        {
            obj.HVelocity = Vector2.zero;
            return;
        }
        float totalDist = obj.HVelocity.magnitude * Time.fixedDeltaTime;
        int   steps = Mathf.Max(1, Mathf.CeilToInt(totalDist / gridSize));
        float dt    = Time.fixedDeltaTime / steps;

        bool collided = false;
        for (int s = 0; s < steps; s++)
        {
            Vector2 delta = obj.HVelocity * dt;

            Vector2 origin       = obj.HPosition;
            Vector2 nextPosition = origin + delta;

            if (TryResolveStaticCellCollision(obj, nextPosition, delta.normalized)) // Check static wall collision before entity collision!
            {
                totalDist = obj.HVelocity.magnitude * Time.fixedDeltaTime;
                steps     = Mathf.Max(1, Mathf.CeilToInt(totalDist / gridSize));
                dt        = Time.fixedDeltaTime / steps;
                delta     = obj.HVelocity * dt;
                collided  = true;
            }

            Vector2Int previousAnchor = WorldCenterToAnchorCell(origin, obj.Size);
            obj.HPosition += delta;
            UpdatePhysicsCellOccupancy(obj, previousAnchor);

            if (collided) break;
        }
    }
#endregion

#region STATIC COLLISION
    private bool TryResolveStaticCellCollision(IPhysics obj, Vector2 nextPosition, Vector2 fallbackDir)
    {
        obj.CurrentWallNormal = Vector2.zero;
        Vector2 origin = obj.HPosition;
        Vector2 delta = nextPosition - origin;
        float distance = delta.magnitude;
        if (distance < EPSILON) return false;
        float radius = 0.25f * (obj.Size.x + obj.Size.y) * 0.8f;

        int hitCount = ZPhysics2D.CircleCastNonAlloc(origin, radius, delta.normalized, staticCastBuffer, distance, obj.WallLayer, 
                                                  obj.ZCol.ZMin + obj.StepUpTolerance + EPSILON, obj.ZCol.ZMax);

        ZCollider2D validHit = null;
        Vector2 normal = Vector2.zero;
        float minHitDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = staticCastBuffer[i];
            if (hit.collider.transform == obj.ZCol.transform) continue;
            if (hit.distance < minHitDistance)
            {
                minHitDistance = hit.distance;
                normal = hit.normal;
                ZPhysics2D.TryGet(hit.collider, out validHit);
            }
        }

        if (validHit)
        {
            if (normal.sqrMagnitude < EPSILON) normal = -fallbackDir.normalized;
            ResolveStaticCollision(obj, validHit, normal);
            obj.CurrentWallNormal = normal;
            return true;
        }

        return false;
    }

    private void ResolveStaticCollision(IPhysics obj, ZCollider2D wall, Vector2 normal)
    {
        float vDotN = Vector2.Dot(obj.HVelocity, normal);
        if (vDotN >= 0f) return;

        obj.HVelocity -= (1f + Mathf.Min(wall.bounceCoeff, obj.BounceCoeff)) * vDotN * normal;

        if (obj.HVelocity.magnitude < stopThreshold)
            obj.HVelocity = Vector2.zero;
    }
#endregion

#region DYNAMIC COLLISION
    private void ResolveEntityCollision(IPhysics obj)
    {
        Vector2Int currentAnchor = WorldCenterToAnchorCell(obj.HPosition, obj.Size);
        
        for (int x = -1; x <= obj.Size.x; x++) for (int y = -1; y <= obj.Size.y; y++) // Broadphase: Check cells nearby
        {
            Vector2Int queryCell = currentAnchor + new Vector2Int(x, y);
            if (!cellOccupancy.TryGetValue(queryCell, out var occupants)) continue;
            
            for (int o = 0; o < occupants.Count; o++)
            {
                var occupant = occupants[o];
                if (occupant == null || occupant == obj) continue;
                if (!CanEntitiesCollide(obj, occupant)) continue;
                
                if (obj.ID > occupant.ID) continue; // Prevent double collision 
    
                if (obj.ZCol.ZMin + obj.StepUpTolerance < occupant.ZCol.ZMax &&
                    obj.ZCol.ZMax                   > occupant.ZCol.ZMin) // Narrowphase: AABB overlap check
                {
                    Vector2 posA = obj.HPosition;
                    Vector2 posB = occupant.HPosition;
                    Vector2 extentsA = 0.5f * gridSize * (Vector2)obj.Size;
                    Vector2 extentsB = 0.5f * gridSize * (Vector2)occupant.Size;
                    float tolerance = 0.01f;
    
                    bool isOverlappingX = Mathf.Abs(posA.x - posB.x) < extentsA.x + extentsB.x - tolerance;
                    bool isOverlappingY = Mathf.Abs(posA.y - posB.y) < extentsA.y + extentsB.y - tolerance;
    
                    if (isOverlappingX && isOverlappingY)
                    {
                        Vector2 normal = obj.HPosition - occupant.HPosition;
                        if (normal.sqrMagnitude < EPSILON) normal = Vector2.up;
                        
                        CalculateEntityCollision(obj, occupant, normal.normalized);
                    }
                }
            }
        }
    }

    private void CalculateEntityCollision(IPhysics objA, IPhysics objB, Vector2 normal)
    {
        Vector2 vA = objA.HVelocity;
        Vector2 vB = objB.HVelocity;
        bool infA = objA.Mode == MovementMode.Static && vB.magnitude * objB.Mass / Time.fixedDeltaTime < objA.StaticEndureForce;
        bool infB = objB.Mode == MovementMode.Static && vA.magnitude * objA.Mass / Time.fixedDeltaTime < objB.StaticEndureForce;
        float invMA = infA ? 0f : objA.InvM;
        float invMB = infB ? 0f : objB.InvM;

        Vector2 sep = objA.HPosition - objB.HPosition;
        float dist = sep.magnitude;
        Vector2 pushDir = dist > EPSILON ? sep / dist : normal;

        Vector2 ConstrainToWall(Vector2 vector, Vector2 wallNormal)
        {
            if (wallNormal == Vector2.zero) return vector;

            float dot = Vector2.Dot(vector, wallNormal);
            if (dot < 0f) return vector - wallNormal * dot;
            else          return vector;
        }

        float totalInvMass = invMA + invMB;
        if (totalInvMass < EPSILON) return;

        float allowedDist = Mathf.Abs(pushDir.x) * (objA.Size.x + objB.Size.x) * 0.5f * gridSize +
                            Mathf.Abs(pushDir.y) * (objA.Size.y + objB.Size.y) * 0.5f * gridSize;

        float vRelative = Vector2.Dot(vA - vB, normal);
        float penetration = allowedDist - dist;

        const float slop = 0.001f;
        const float velocitySlop = 0.05f;

        bool needImpulse = vRelative < -velocitySlop;
        bool needCorrection = penetration > slop; 

        if (objA.HVelocity.magnitude < stopThreshold && objB.HVelocity.magnitude < stopThreshold) return;
        if (!needImpulse && !needCorrection) return;

        objA.Phys.KeepPhysics = keepPhysicsFrames;
        objB.Phys.KeepPhysics = keepPhysicsFrames;
        if (objA.Mode == MovementMode.Grid) SwitchToPhysics(objA);
        if (objB.Mode == MovementMode.Grid) SwitchToPhysics(objB);

        if (needImpulse)
        {
            float e = Mathf.Min(objA.BounceCoeff, objB.BounceCoeff);
            float j = -(1f + e) * vRelative / totalInvMass;
            Vector2 impulse = j * normal;

            Vector2 nextVelA = vA + impulse * invMA;
            Vector2 nextVelB = vB - impulse * invMB;

            objA.HVelocity = ConstrainToWall(nextVelA, objA.CurrentWallNormal);
            objB.HVelocity = ConstrainToWall(nextVelB, objB.CurrentWallNormal);
        }

        if (needCorrection)
        {
            float correctAmount = (penetration - slop) * stabilization / totalInvMass;
            Vector2 correction = pushDir * correctAmount;

            Vector2 constrainedMoveA = ConstrainToWall(correction * invMA, objA.CurrentWallNormal);
            Vector2 constrainedMoveB = ConstrainToWall(-correction * invMB, objB.CurrentWallNormal);

            Vector2Int prevCellA = WorldCenterToAnchorCell(objA.HPosition, objA.Size);
            Vector2Int prevCellB = WorldCenterToAnchorCell(objB.HPosition, objB.Size);

            if (interpolateRender)
            {
                objA.SetBodyPartSnapOffset(objA.HPosition + constrainedMoveA);
                objB.SetBodyPartSnapOffset(objB.HPosition + constrainedMoveB);
            }

            objA.HPosition += constrainedMoveA;
            objB.HPosition += constrainedMoveB;

            UpdatePhysicsCellOccupancy(objA, prevCellA);
            UpdatePhysicsCellOccupancy(objB, prevCellB);
        }
    }
#endregion

#region VERTICAL PHYS
    private void UpdateVerticalPhysics(IPhysics obj)
    {
        Vector2 checkSize = (Vector2)obj.Size - new Vector2(0.2f, 0.2f);

        ZCollider2D possibleFloor = ZPhysics2D.ZBoxGetFloor(
            obj.HPosition, checkSize, 0, obj.FloorLayer,
            obj.ZCol.ZMin - Mathf.Abs(obj.ZVelocity) * Time.fixedDeltaTime * 2,
            obj.ZCol.ZMin + obj.StepUpTolerance + EPSILON);
        ZCollider2D possibleCeiling = ZPhysics2D.ZBoxGetCeiling(
            obj.HPosition, checkSize, 0, obj.FloorLayer,
            obj.ZCol.ZMax - obj.StepUpTolerance - EPSILON,
            obj.ZCol.ZMax + Mathf.Abs(obj.ZVelocity) * Time.fixedDeltaTime * 2);

        float posMinZ = possibleFloor   ? possibleFloor.ZmaxBox(obj.HPosition, checkSize, 0)   : float.MinValue;
        float posMaxZ = possibleCeiling ? possibleCeiling.ZminBox(obj.HPosition, checkSize, 0) : float.MaxValue;
        float displacement = obj.ZVelocity * Time.fixedDeltaTime;
    
        Vector3 surfaceNormal = possibleFloor ? possibleFloor.GetSurfaceNormal() : Vector3.forward;
        Vector3 vel           = new(obj.HVelocity.x, obj.HVelocity.y, obj.ZVelocity);
        bool towardsFloor     = Vector3.Dot(surfaceNormal, vel) < EPSILON;

        bool willPenetFloor   = obj.ZCol.ZMin + displacement < posMinZ;
        bool willPenetCeiling = obj.ZCol.ZMax + displacement > posMaxZ;

        if (willPenetFloor && willPenetCeiling) obj.ZPosition = (posMinZ + posMaxZ) * 0.5f - obj.ZCol.zCenter;
        else if (willPenetFloor)                obj.ZPosition = posMinZ - obj.ZCol.zCenter + obj.ZCol.height * 0.5f;
        else if (willPenetCeiling)              obj.ZPosition = posMaxZ - obj.ZCol.zCenter - obj.ZCol.height * 0.5f;

        ZCollider2D floor   = posMinZ > obj.ZCol.ZMin - obj.StepDownTolerance - EPSILON ? possibleFloor   : null;
        ZCollider2D ceiling = posMaxZ < obj.ZCol.ZMax + obj.StepDownTolerance + EPSILON ? possibleCeiling : null;

        obj.Ground = willPenetFloor && towardsFloor ? floor : null;

        obj.IsOnSlope  = Vector3.Dot(surfaceNormal, Vector3.forward) < 1f - EPSILON;

        bool calcGround = false;
        if (obj.Ground)
        {
            if (!obj.IsGroundedPrev && towardsFloor) // floor bounce
            {
                Vector3 reflected = vel - (1f + Mathf.Min(obj.BounceCoeff, floor.bounceCoeff)) * Vector3.Dot(vel, surfaceNormal) * surfaceNormal;
                obj.ZVelocity     = reflected.z;
                obj.HVelocity     = new(reflected.x, reflected.y);

                float verAcc       = Mathf.Abs(obj.ExForce.z * obj.InvM);
                float minEscapeVel = verAcc * bounceTolerance;
                if (obj.ZVelocity < minEscapeVel) obj.ZVelocity = 0f;
            }
            else calcGround = true;
        }
        else if (obj.IsGroundedPrev && floor && towardsFloor)
        {
            obj.Ground = floor;
            calcGround = true;
        }

        if (calcGround)
        {
            obj.ZPosition = posMinZ - obj.ZCol.zCenter + obj.ZCol.height * 0.5f;
            if (obj.IsOnSlope)
            {
                Vector3 slopeVel = vel - Vector3.Dot(vel, surfaceNormal) * surfaceNormal;
                obj.HVelocity = (Vector2)slopeVel;
                obj.ZVelocity = slopeVel.z;
            }
            else obj.ZVelocity = 0;
            obj.ExForce = new(obj.ExForce.x, obj.ExForce.y, 0);
        }

        if (ceiling != null && obj.ZVelocity > EPSILON)
        {
            if (willPenetCeiling) // ceiling bounce
            {
                Vector3 ceilNormal = ceiling.GetSurfaceNormal();
                float vDotN = Vector3.Dot(vel, ceilNormal);
                if (vDotN < 0f)
                {
                    Vector3 reflected   = vel - (1f + Mathf.Min(obj.BounceCoeff, ceiling.bounceCoeff)) * vDotN * ceilNormal;
                    obj.ZVelocity       = reflected.z;
                    Vector2 newHorizVel = new(reflected.x, reflected.y);
                    if (obj.Mode == MovementMode.Grid) obj.HVelocity = newHorizVel;
                    else                               obj.HVelocity = newHorizVel;
                }
                else
                {
                    obj.ZVelocity = -Mathf.Abs(obj.ZVelocity) * Mathf.Min(obj.BounceCoeff, ceiling.bounceCoeff);
                }
                if (Mathf.Abs(obj.ZVelocity) < stopThreshold) obj.ZVelocity = 0f;
            }
        }

        obj.ZPosition += obj.ZVelocity * Time.fixedDeltaTime;

        if (obj.Ground) obj.CanWalkFrameLeft = keepCanWalkFrames;
        else if (obj.CanWalkFrameLeft > 0) obj.CanWalkFrameLeft--;

        obj.IsGroundedPrev     = obj.Ground;
        Vector3 ep             = obj.HPosition;
        obj.HPosition = new Vector3(ep.x, ep.y, obj.ZPosition);
    }

    private Vector2 ApplyGroundFriction(IPhysics obj, Vector2 vel)
    {
        float friction = obj.Ground ? Mathf.Max(obj.GroundFriction, obj.Ground.frictionCoeff) : obj.AirFriction;
        vel *= friction;

        float iceBlend      = Mathf.Clamp01((friction - 0.5f) / 0.5f);
        float effectiveStop = stopThreshold * (1f - iceBlend);

        if (vel.magnitude < effectiveStop) vel = Vector2.zero;
        return vel;
    }

    private float ApplyAirFriction(IPhysics obj, float vel) => vel * obj.AirFriction;
    
#endregion

#region CELL HELPERS
    private void SnapPositionToCell(IPhysics obj, Vector2Int anchorCell)
    {
        Vector2 worldCenter    = AnchorCellToWorldCenter(anchorCell, obj.Size);
        obj.HPosition = new Vector3(worldCenter.x, worldCenter.y, obj.ZPosition);
    }
    private void RebuildCellOccupancy()
    {
        cellOccupancy.Clear();
        for (int i = 0; i < AllPhysicsEntitys.Count; i++)
        {
            IPhysics obj = AllPhysicsEntitys[i];
            if (obj.Mode == MovementMode.Grid)
            {
                int cnt = GetOccupiedCells(obj.Grid.CurrentCell, obj.Size, cellBuffer); //might be better remove
                for (int j = 0; j < cnt; j++)
                    AddCellOccupant(cellBuffer[j], obj);
                cnt = GetOccupiedCells(obj.Grid.TargetCell, obj.Size, cellBuffer);
                for (int j = 0; j < cnt; j++)
                    AddCellOccupant(cellBuffer[j], obj);
            }
            else
            {
                Vector2Int anchor = WorldCenterToAnchorCell(obj.HPosition, obj.Size);
                int cnt = GetOccupiedCells(anchor, obj.Size, cellBuffer);
                for (int j = 0; j < cnt; j++)
                    AddCellOccupant(cellBuffer[j], obj);
            }
        }
    }

    private IPhysics GetCellOccupant(Vector2Int cell, IPhysics self)
    {
        if (!cellOccupancy.TryGetValue(cell, out var occupants)) return null;
        for (int i = 0; i < occupants.Count; i++)
        {
            IPhysics occupant = occupants[i];
            if (occupant != null && occupant != self
            && CanEntitiesCollide(self, occupant)
            && self.ZCol.ZMin + self.StepUpTolerance + EPSILON < occupant.ZCol.ZMax
            && self.ZCol.ZMax                                  > occupant.ZCol.ZMin)
                return occupant;
        }
        return null;
    }

    private int GetOccupantsInFootprint(IPhysics obj, Vector2Int anchorCell, IPhysics[] results)
    {
        HashSet<IPhysics> check = new(32);
        int validCnt = 0;
        int cnt = GetOccupiedCells(anchorCell, obj.Size, cellBuffer);
        for (int i = 0; i < cnt; i++)
        {
            var occ = GetCellOccupant(cellBuffer[i], obj);
            if (occ != null && check.Add(occ)) results[validCnt++] = occ;
        }
        return validCnt;
    }

    /// <summary>
    /// 동적 엔티티 충돌은 양쪽 WallLayer가 서로의 실제 Collider Layer를 허용할 때만 성립한다.
    /// 어느 한쪽이라도 상대 Layer를 제외하면 Grid 점유 검사와 Physics 충돌 해결 모두 통과한다.
    /// </summary>
    private static bool CanEntitiesCollide(IPhysics objA, IPhysics objB)
    {
        if (objA == null || objB == null || objA.ZCol == null || objB.ZCol == null)
            return false;

        return ContainsLayer(objA.WallLayer, objB.ZCol.gameObject.layer) &&
               ContainsLayer(objB.WallLayer, objA.ZCol.gameObject.layer);
    }

    private static bool ContainsLayer(LayerMask mask, int layer)
    {
        return layer >= 0 && layer < 32 && (mask.value & (1 << layer)) != 0;
    }

    private void AddCellOccupant(Vector2Int cell, IPhysics obj)
    {
        if (!cellOccupancy.TryGetValue(cell, out var occupants))
        {
            occupants = new List<IPhysics>();
            cellOccupancy[cell] = occupants;
        }
        if (!occupants.Contains(obj))
            occupants.Add(obj);
    }

    private void RemoveCellOccupant(Vector2Int cell, IPhysics obj)
    {
        if (!cellOccupancy.TryGetValue(cell, out var occupants)) return;
        occupants.Remove(obj);
        if (occupants.Count == 0) cellOccupancy.Remove(cell);
    }

    private void RemoveFootprintOccupant(IPhysics obj, Vector2Int anchorCell)
    {
        int cnt = GetOccupiedCells(anchorCell, obj.Size, cellBuffer);
        for (int i = 0; i < cnt; i++)
            RemoveCellOccupant(cellBuffer[i], obj);
    }

    private void AddFootprintOccupant(IPhysics obj, Vector2Int anchorCell)
    {
        int cnt = GetOccupiedCells(anchorCell, obj.Size, cellBuffer);
        for (int i = 0; i < cnt; i++)
            AddCellOccupant(cellBuffer[i], obj);
    }

    private bool IsStaticWall(IPhysics obj, Vector2Int anchorCell)
    {
        int cnt = GetOccupiedCells(anchorCell, obj.Size, cellBuffer);
        for (int i = 0; i < cnt; i++)
        {
            if (OverlapCheckHorizontal(obj, CellToWorld(cellBuffer[i]), 0.4f))
                return true;
        }
        return false;
    }

    private bool OverlapCheckHorizontal(IPhysics obj, Vector2 point, float radius)
    {
        int cnt = ZPhysics2D.OverlapCircleNonAlloc(
            point, radius, overlapBuffer,
            obj != null ? obj.WallLayer : (LayerMask)~0,
            obj != null ? obj.ZCol.ZMin + obj.StepUpTolerance + EPSILON : float.MinValue,
            obj != null ? obj.ZCol.ZMax                             : float.MaxValue);

        for (int i = 0; i < cnt; i++)
        {
            Transform t = overlapBuffer[i].transform;
            if (obj != null && t == obj.ZCol.transform) continue;
            return true;
        }
        return false;
    }

    private Vector2Int DirectionToCellStep(Vector2 dir) => new( dir.x > EPSILON ? 1 : dir.x < -EPSILON ? -1 : 0,
                                                                dir.y > EPSILON ? 1 : dir.y < -EPSILON ? -1 : 0);
    
    private void UpdatePhysicsCellOccupancy(IPhysics obj, Vector2Int previousAnchor)
    {
        if (obj.Mode != MovementMode.Physics) return;

        RemoveFootprintOccupant(obj, previousAnchor);
        Vector2Int newAnchor = WorldCenterToAnchorCell(obj.HPosition, obj.Size);
        AddFootprintOccupant(obj, newAnchor);
    }

    private Vector2 AnchorCellToWorldCenter(Vector2Int anchorCell, Vector2Int size)
    {
        Vector2 anchorWorld = CellToWorld(anchorCell);
        Vector2 offset      = 0.5f * gridSize * (Vector2)(size - Vector2Int.one);
        return anchorWorld + offset;
    }

    private Vector2Int WorldCenterToAnchorCell(Vector2 worldCenter, Vector2Int size)
    {
        Vector2 offset    = 0.5f * gridSize * (Vector2)(size - Vector2Int.one);
        Vector2 anchorPos = worldCenter - offset;
        return new Vector2Int(
            Mathf.RoundToInt(anchorPos.x / gridSize),
            Mathf.RoundToInt(anchorPos.y / gridSize));
    }

    private int GetOccupiedCells(Vector2Int anchorCell, Vector2Int size, Vector2Int[] results)
    {
        int count = 0;
        for (int x = 0; x < size.x; x++) for (int y = 0; y < size.y; y++)
        {
            results[count++] = anchorCell + new Vector2Int(x, y);
            if (count >= results.Length) return results.Length;
        }
        return count;
    }

    private Vector2 CellToWorld(Vector2Int cell) => new(cell.x * gridSize, cell.y * gridSize);
#endregion

    private void Update() // Interpolate rendering
    {
        foreach (var obj in AllPhysicsEntitys)
            obj.DecayBodyPartOffset(renderDecaySpeed, snapDecaySpeed);
    }
}
