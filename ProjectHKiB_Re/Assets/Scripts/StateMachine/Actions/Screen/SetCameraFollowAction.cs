using UnityEngine;
namespace StateMachine
{
    // 카메라가 따라다닐 대상을 이벤트에 등장하는 엔티티로 바꾼다 — "백발의 눈가를 클로즈업" 같은
    // 연출은 줌만으로는 안 되고 카메라가 그쪽을 봐야 성립한다.
    //
    // 예전엔 카메라가 항상 플레이어만 따라다녀서, CameraZoomAction으로 당겨봐야 플레이어를 확대할
    // 뿐이었다(그래서 클로즈업이 연출로 읽히지 않았다). 이 액션과 짝지어 쓰면 대상 쪽으로 당긴다.
    //
    // 되돌리는 배선을 빼먹으면 카메라가 NPC를 계속 따라다닌다 — 연출이 끝나는 State에
    // returnToDefault를 켠 이 액션을 반드시 함께 넣을 것.
    [System.Serializable]
    public class SetCameraFollowAction : StateAction
    {
        // EventSO.involvedEventTargets에 등록된 대상 ID.
        public string targetID;

        // 켜면 targetID를 무시하고 시작 시점의 기본 대상(보통 플레이어)으로 되돌린다.
        public bool returnToDefault;

        public override void Act(StateController stateController)
        {
            CameraManager camera = CameraManager.instance;
            if (!camera)
            {
                Debug.LogError("ERROR: SetCameraFollowAction - CameraManager가 없습니다.");
                return;
            }

            if (returnToDefault || string.IsNullOrEmpty(targetID))
            {
                camera.SetFollowTarget(null);
                return;
            }

            if (!stateController.TryGetInterface(out IEvent @event))
            {
                Debug.LogError($"ERROR: SetCameraFollowAction - '{stateController.name}'에서 IEvent를 찾을 수 없습니다.");
                return;
            }

            if (@event.CurrentTargets == null)
            {
                Debug.LogError($"ERROR: SetCameraFollowAction - 이벤트 대상 목록이 아직 준비되지 않았습니다 (targetID: '{targetID}').");
                return;
            }

            if (!@event.CurrentTargets.targetEntities.ContainsKey(targetID))
            {
                Debug.LogError($"ERROR: SetCameraFollowAction - 이벤트 대상에 '{targetID}'가 없습니다. " +
                               $"현재 대상: [{string.Join(", ", @event.CurrentTargets.targetEntities.Keys)}]");
                return;
            }

            EventControllableEntity target = @event.CurrentTargets.targetEntities[targetID];
            if (target == null || target.Target == null)
            {
                Debug.LogError($"ERROR: SetCameraFollowAction - '{targetID}'의 Target이 비어 있습니다.");
                return;
            }

            camera.SetFollowTarget(target.Target.transform);
        }
    }
}
