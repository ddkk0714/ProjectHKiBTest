using UnityEngine;
namespace StateMachine
{
    // TargetEntityManipulateAction의 복수 대상판 — 같은 액션을 여러 대상에게 한 번에 먹인다.
    // "NPC 둘이 동시에 보스화" 같은 연출을 액션 하나로 표현하려고 만들었다.
    // 대상 ID 중 이벤트에 등장하지 않는 것이 섞여 있으면 그것만 건너뛰고 나머지는 그대로 진행한다.
    [System.Serializable]
    public class TargetEntitiesManipulateAction : StateAction
    {
        public string[] targetIDs;
        [SerializeReference, SubclassSelector] public StateAction targetAction;

        public override void Act(StateController stateController)
        {
            if (targetAction == null)
            {
                Debug.LogError("ERROR: TargetEntitiesManipulateAction - targetAction이 비어 있습니다.");
                return;
            }

            if (!stateController.TryGetInterface(out IEvent @event))
            {
                Debug.LogError("ERROR: Interface Not Found!!!");
                return;
            }

            if (@event.CurrentTargets == null)
            {
                Debug.LogError("ERROR: TargetEntitiesManipulateAction - 이벤트 대상 목록이 아직 준비되지 않았습니다. " +
                               "EventManager.StartEvent를 통해 시작했는지 확인하세요.");
                return;
            }

            for (int i = 0; i < targetIDs.Length; i++)
            {
                if (!@event.CurrentTargets.targetEntities.ContainsKey(targetIDs[i]))
                {
                    Debug.LogWarning($"[TargetEntitiesManipulateAction] 이벤트 대상에 '{targetIDs[i]}'가 없습니다. 건너뜁니다.");
                    continue;
                }

                targetAction.Act(@event.CurrentTargets.targetEntities[targetIDs[i]].Target);
            }
        }
    }
}
