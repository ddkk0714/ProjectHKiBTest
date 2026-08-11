using UnityEngine;
namespace StateMachine
{
    /// <summary>
    /// NPC·적의 공격 돌진. 사거리(attackMoveMaxRange) 안의 대상 쪽으로 붙는다.
    ///
    /// 옛 MovementManagerSO.AttackMove 자리다. 그쪽은 MovePoint를 직접 옮기는 격자 이동이었고,
    /// 지금은 IPhysics.MoveToward가 physManager.InstantMove에 위임해 격자 한 칸씩 전진하며
    /// 벽·엔티티를 검사한다 — 막히면 갈 수 있는 데까지만 가고 멈춘다. 이동 자체는 논리 위치만
    /// 옮기고 몸통이 따라붙으므로(interpolate) 순간이동처럼 보이지 않는다.
    ///
    /// 걷던 속도를 끊는 건 여기가 아니라 SetAttackDataAction이 한다 — 모든 공격 State가 그걸
    /// EnterActions에 갖고 있고, 이 액션은 actionSequence 중간에 불릴 수 있어 시점이 늦다.
    /// </summary>
    [System.Serializable]
    public class AttackMoveGeneralAction : StateAction
    {
        [SerializeField] private TargetingManagerSO targetingManager;
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IAttackable attackable)
            && stateController.TryGetInterface(out IPhysics movable)
            && stateController.TryGetInterface(out IDirAnimatable animatable)
            && stateController.TryGetInterface(out ITargetable targetable))
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
                int moveRadius = attackData.attackMoveMaxRange;
                Transform thisTransform = stateController.transform;
                Transform target;

                // 이미 잡아 둔 대상이 있으면 그대로 쓰고, 없을 때만 새로 찾는다.
                // 사거리 안에 아무도 없으면 돌진 자체를 하지 않는다(제자리 공격).
                if (!targetable.CurrentTarget)
                {
                    target = targetingManager.PositianalTarget(thisTransform.position, moveRadius, targetable.TargetLayers, targetable.CurrentTarget);
                    targetable.CurrentTarget = target;
                    if (!target) return;
                }
                Debug.DrawLine(thisTransform.position, targetable.CurrentTarget.position, Color.blue, 0.4f);
                animatable.SetAnimationDirection(targetable.CurrentTarget.position - thisTransform.position);
                movable.MoveToward(targetable.CurrentTarget.position, moveRadius);
            }
            else
                Debug.LogError("ERROR: Interface Not Found!!!");

        }
    }
}