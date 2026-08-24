using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class TargetEntityManipulateAction : StateAction
    {
        public string targetID;
        [SerializeReference, SubclassSelector] public StateAction targetAction;
        public override void Act(StateController stateController)
        {
            if (targetAction == null) return;

            if (!stateController.TryGetInterface(out IEvent @event))
            {
                Debug.LogError($"ERROR: TargetEntityManipulateAction - '{stateController.name}'에서 IEvent를 찾을 수 없습니다 " +
                               "(이벤트를 돌리는 오브젝트에 EventModule이 붙어 있어야 합니다).");
                return;
            }

            if (@event.CurrentTargets == null)
            {
                Debug.LogError($"ERROR: TargetEntityManipulateAction - 이벤트 대상 목록이 아직 준비되지 않았습니다 " +
                               $"(targetID: '{targetID}'). EventManager.StartEvent를 통해 시작했는지 확인하세요.");
                return;
            }

            if (!@event.CurrentTargets.targetEntities.ContainsKey(targetID))
            {
                Debug.LogError($"ERROR: TargetEntityManipulateAction - 이벤트 대상에 '{targetID}'가 없습니다. " +
                               $"현재 대상: [{string.Join(", ", @event.CurrentTargets.targetEntities.Keys)}]. " +
                               "EventSO.involvedEventTargets에 그 ID가 있는지, FromMap이라면 그 ID의 " +
                               "EventControllableEntity가 맵 씬에 있고 MapLocalManager.allEventTargets에 " +
                               "등록(Auto Find Event Targets)돼 있는지 확인하세요.");
                return;
            }

            EventControllableEntity target = @event.CurrentTargets.targetEntities[targetID];
            if (target == null || target.Target == null)
            {
                Debug.LogError($"ERROR: TargetEntityManipulateAction - '{targetID}'의 EventControllableEntity.Target이 비어 있습니다.");
                return;
            }

            targetAction.Act(target.Target);
        }
    }
}