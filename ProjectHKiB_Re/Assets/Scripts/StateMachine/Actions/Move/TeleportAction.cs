using UnityEngine;
namespace StateMachine
{
    // 대상을 지정 좌표로 옮긴다 — 컷신 중 배치, 기상 지점 배치 등.
    //
    // RealTeleport는 카메라까지 같이 따라붙는 "진짜" 순간이동이고, LogicalTeleport는 위치만
    // 옮긴다(암전 뒤에서 몰래 옮길 때). 암전 중에 옮기면서 카메라가 튀는 게 싫으면 logical을 켤 것.
    [System.Serializable]
    public class TeleportAction : StateAction
    {
        public Vector3 position;
        public bool logical;

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out IPhysics physics))
            {
                Debug.LogError("ERROR: TeleportAction - IPhysics를 찾을 수 없습니다.");
                return;
            }

            if (logical) physics.LogicalTeleport(position);
            else physics.RealTeleport(position);
        }
    }
}
