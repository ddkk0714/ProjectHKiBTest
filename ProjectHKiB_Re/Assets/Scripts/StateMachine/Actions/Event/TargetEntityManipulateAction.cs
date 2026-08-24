using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class TargetEntityManipulateAction : StateAction
    {
        public string targetID;
        [SerializeReference, SubclassSelector] public StateAction targetAction;
        // 예전엔 어느 쪽이 틀어져도 "Interface Not Found!!!" 하나만 찍혀서, 실제로는 대상 ID를 못 찾은
        // 것인데도 인터페이스 문제로 오해하기 쉬웠다. 원인별로 갈라서 무엇을 고쳐야 하는지까지 남긴다.
        public override void Act(StateController stateController)
        {
            // 안쪽 액션을 아직 안 고른 빈칸은 "할 일 없음"으로 본다 — 여기서 터지면 같은 State의
            // 뒤쪽 액션들까지 통째로 실행되지 않는다(StateSO의 액션 목록과 같은 사정).
            if (targetAction == null) return;

            if (!stateController.TryGetInterface(out IEvent @event))
            {
                Debug.LogError($"ERROR: TargetEntityManipulateAction - '{stateController.name}'에서 IEvent를 찾을 수 없습니다 " +
                               "(이벤트를 돌리는 오브젝트에 EventModule이 붙어 있어야 합니다).");
                return;
            }

            // 이벤트가 StartEvent를 거치지 않고 돌거나(직접 Initialize 등) 대상 검색 전에 액션이
            // 실행되면 여기가 비어 있다. 터뜨리는 대신 무엇이 없는지 알리고 넘어간다.
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