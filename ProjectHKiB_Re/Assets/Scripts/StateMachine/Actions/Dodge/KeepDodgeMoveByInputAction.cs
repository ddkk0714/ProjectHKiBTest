using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class KeepDodgeMoveByInputAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IPhysics movable) && stateController.TryGetInterface(out IDodgeable dodgeable))
            {
                // 옛 MovementManagerSO.WalkMove(.., BaseDodgeSpeed, ..) 자리. 그쪽은 MovePoint를
                // 직접 옮기는 격자 이동이었지만 지금 물리는 속도를 적분하므로, 매 프레임 회피 속도로
                // 속도를 세워 준다(마찰이 깎는 만큼 다시 채워진다). 벽 처리는 물리 쪽이 한다.
                //
                // 옛 인자에 있던 dodgeable.KeepDodgeWallLayer는 쓰지 않는다 - 새 물리는 obj.WallLayer
                // 하나로 판정해서, 회피 중에만 다른 레이어를 통과시키려면 그쪽을 손봐야 한다.
                Vector2 dir = GameManager.instance.inputManager.MoveInput;
                movable.HVelocity = dir == Vector2.zero
                    ? Vector2.zero
                    : dir.normalized * dodgeable.BaseDodgeSpeed;
            }
            else Debug.LogError("ERROR: Interface Not Found!!!");
        }
    }
}