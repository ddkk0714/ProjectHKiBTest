using UnityEngine;
namespace StateMachine
{
    /// <summary>
    /// 플레이어의 공격 돌진. AttackMoveGeneralAction과 이동 수단은 같고, 어디로 갈지 고르는
    /// 규칙만 다르다 — 이동 입력이 있으면 "그 방향", 없으면 "주변 대상"을 우선한다.
    ///
    /// 옛 MovementManagerSO.AttackMove 자리다. 지금은 IPhysics.MoveToward가 physManager.InstantMove에
    /// 위임해 격자 한 칸씩 전진하며 벽·엔티티를 검사한다 — 막히면 갈 수 있는 데까지만 간다.
    /// 걷던 속도를 끊는 건 SetAttackDataAction이 EnterActions에서 이미 해 둔다.
    ///
    /// 분기 네 갈래(위에서부터 우선순위):
    ///   이동 입력 있음 + 자동조준 → 입력 방향 부채꼴에서 찾은 대상까지 (maxRange)
    ///   이동 입력 있음           → 대상 없이 입력 방향으로 (maxRange)
    ///   입력 없음 + 기존 대상 유효 → 그 대상까지 (maxRange)
    ///   입력 없음 + 새 대상 없음   → 마지막 바라보던 방향으로 minRange만큼만
    /// </summary>
    [System.Serializable]
    public class AttackMovePlayerAction : StateAction
    {
        public TargetingManagerSO targetingManager;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IAttackable attackable)
            && stateController.TryGetInterface(out IPhysics movable)
            && stateController.TryGetInterface(out IDirAnimatable animatable)
            && stateController.TryGetInterface(out ITargetable targetable)
            && stateController.TryGetInterface(out IFootstep footstep))
            {
                if (attackable.AttackDatas.Equals(null))
                {
                    Debug.LogError("ERROR: AttackDatas is missing!!!"); return;
                }
                if (attackable.AttackDatas.Length - 1 < attackable.AttackNumber)
                {
                    Debug.LogError("ERROR: AttackData[" + attackable.AttackNumber + "] is missing !!!"); return;
                }

                AttackDataSO attackData = attackable.AttackDatas[attackable.AttackNumber];
                int moveRadius;
                bool autoTarget = attackData.isAutoTarget;
                Transform thisTransform = stateController.transform;
                Transform target;

                // 이동 입력이 있으면 플레이어가 방향을 정한 것으로 본다 — 그쪽으로 돌진한다.
                if (!GameManager.instance.inputManager.MoveInput.Equals(Vector2.zero))
                {
                    moveRadius = attackData.attackMoveMaxRange;

                    // 입력 방향을 애니메이션 4방향으로 제한한 뒤, 그 방향 부채꼴에서만 대상을 찾는다.
                    // 못 찾으면 CurrentTarget을 비워 다음 공격이 낡은 대상을 물고 가지 않게 한다.
                    if (autoTarget)
                    {
                        target = targetingManager.DirectionalTarget(thisTransform.position, moveRadius, animatable.GetAnimationRestrictedDirection(GameManager.instance.inputManager.MoveInput), targetable.TargetLayers, targetable.CurrentTarget);
                        if (target)
                        {
                            targetable.CurrentTarget = target;
                            movable.MoveToward(target.position, moveRadius);
                            return;
                        }
                        targetable.CurrentTarget = null;
                    }

                    movable.MoveToward(thisTransform.position + (Vector3)animatable.GetAnimationRestrictedDirection(GameManager.instance.inputManager.MoveInput) * moveRadius, moveRadius);
                }
                // 이동 입력이 없으면 방향을 대상이 정한다.
                else
                {
                    moveRadius = attackData.attackMoveMaxRange;

                    // 이미 잡고 있던 대상이 아직 사거리 안이면 그대로 유지한다 —
                    // 연타 중에 대상이 다른 적으로 튀지 않게 하는 쪽이 조작감이 좋다.
                    if (autoTarget && targetable.CurrentTarget && targetingManager.CheckCurrentTargetDistance(targetable.CurrentTarget, thisTransform.position, moveRadius))
                    {
                        Debug.DrawLine(thisTransform.position, targetable.CurrentTarget.position, Color.blue, 0.4f);
                        animatable.SetAnimationDirection(targetable.CurrentTarget.position - thisTransform.position);
                        movable.MoveToward(targetable.CurrentTarget.position, moveRadius);
                        return;
                    }

                    target = targetingManager.PositianalTarget(thisTransform.position, moveRadius, targetable.TargetLayers, targetable.CurrentTarget);
                    if (target)
                    {
                        animatable.SetAnimationDirection(target.position - thisTransform.position);
                        movable.MoveToward(target.position, moveRadius);
                        targetable.CurrentTarget = target;
                        return;
                    }
                    // 입력도 대상도 없는 허공 휘두르기. 여기만 maxRange가 아니라 minRange를 쓴다 —
                    // 갈 곳이 정해지지 않았는데 최대 사거리로 튀어나가면 제자리 공격이 안 된다.
                    targetable.CurrentTarget = null;
                    moveRadius = attackData.attackMoveMinRange;
                    movable.MoveToward(thisTransform.position + (Vector3)animatable.LastSetAnimationDir4 * moveRadius, moveRadius);
                }
                footstep.PlayFootstepAudio(default);
            }
            else
                Debug.LogError("ERROR: Interface Not Found!!!");

        }
    }
}