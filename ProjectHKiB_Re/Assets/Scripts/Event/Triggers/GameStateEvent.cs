using System;
using UnityEngine;

// 이벤트 상태 기계(EventSO)를 시작시키는 씬 오브젝트.
//
// [사전 조건] 기획서의 "사전 조건"(예: dood == 1)을 여기서 판정한다. 트리거는 조건을 모르고
// 그냥 발동하므로, 게이팅을 이 자리에 두지 않으면 진행도와 무관하게 아무 때나 이벤트가 터진다.
// preconditions가 비어 있으면 항상 통과하므로 기존 배선은 그대로 동작한다.
//
// 조건은 EventManager.HasEventFlag와 같은 의미론이다 — "설정된 적 있고 값이 같은가". 한 번도
// 세팅되지 않은 플래그는 value가 0이어도 통과하지 않으니, 진행도 플래그의 시작값은
// MapDataSO.initialEventFlags로 채워둘 것.
public class GameStateEvent : GameEvent
{
    [Serializable]
    public class EventFlagCondition
    {
        public EventFlagSO flag;
        public int value;
    }

    [SerializeField] private EventSO _event;
    [SerializeField] private EventTargets _manualTargets;
    [SerializeField] private EventFlagCondition[] _preconditions;
    [SerializeField] private string[] _requiredClueIds;

    public bool CanTrigger()
    {
        if (_preconditions != null && _preconditions.Length > 0)
        {
            EventManager eventManager = GameManager.instance.eventManager;
            if (!eventManager) return false;

            for (int i = 0; i < _preconditions.Length; i++)
            {
                EventFlagCondition condition = _preconditions[i];
                if (condition == null || !condition.flag) continue;

                if (!eventManager.HasEventFlag(condition.flag, condition.value)) return false;
            }
        }

        if (_requiredClueIds == null || _requiredClueIds.Length == 0) return true;

        RouteModule route = RouteModule.Instance;
        if (route == null || route.Progress == null) return false;

        foreach (string clueId in _requiredClueIds)
        {
            if (string.IsNullOrWhiteSpace(clueId)) continue;

            bool acquired = false;
            foreach (string acquiredId in route.Progress.AcquiredClueIds)
            {
                if (acquiredId != clueId) continue;
                acquired = true;
                break;
            }

            if (!acquired) return false;
        }

        return true;
    }

    // start event by enabling controller update
    public override void TriggerEvent()
    {
        if (!CanTrigger()) return;

        GameManager.instance.eventManager.StartEvent(_event, _manualTargets);
    }
}
