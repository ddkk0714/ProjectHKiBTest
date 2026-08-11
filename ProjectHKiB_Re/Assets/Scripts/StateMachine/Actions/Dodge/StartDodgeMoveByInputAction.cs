using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class StartDodgeMoveByInputAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics movable) && stateController.TryGetInterface(out IDodgeable dodgeable))
            {
                Vector3 dir = GameManager.instance.inputManager.MoveInput;
                if (dir != Vector3.zero)
                {
                    // 옛 MovementManagerSO.InitialDodgeMove와 같은 의미다 - 입력 방향으로
                    // InitialDodgeMaxDistance까지 가되, 벽에 막히면 갈 수 있는 데까지만 간다.
                    // 그쪽은 최대거리부터 1씩 줄이며 OverlapCircle로 빈자리를 찾았고, MoveToward는
                    // InstantMove에 위임해 격자 한 칸씩 전진하며 벽·엔티티를 검사한다.
                    float distance = dodgeable.InitialDodgeMaxDistance;
                    movable.MoveToward(stateController.transform.position + dir.normalized * distance, distance);
                }

            }
            else Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}