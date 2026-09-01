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

    // "현실일 것" / "꿈일 것" 같은 조건. 예전에는 이걸 표현할 방법이 없어 진행도 플래그로 대신했는데
    // (EVT-003의 dood == 2가 그랬다), 진행도가 더 올라가면 그 근사가 깨져 이벤트가 조용히 안 뜬다 -
    // 실제로 EVT-006에서 죽고 현실로 돌아온 뒤(dood == 3) 노트를 읽어도 꿈으로 못 돌아가는 버그가 났다.
    // 맵을 직접 보는 조건이 있어야 "몇 번을 오가든" 성립한다.
    [SerializeField] private WorldRequirement _worldRequirement = WorldRequirement.Any;

    // 진행 중인 이벤트를 끊고 시작할지. 사망 복귀처럼 "하던 걸 무르고 끼어들어야" 하는 이벤트만 켠다.
    // 평상시엔 꺼두는 게 맞다 — EventManager의 재진입 가드가 트리거 중복으로 이벤트가 처음부터
    // 되감기는 사고를 잡아주는데, 이걸 켜면 그 보호가 사라진다.
    [SerializeField] private bool _interruptRunningEvent;
    [SerializeField] private string[] _requiredClueIds;

    public bool CanTrigger()
        => EvaluatePrerequisites().Succeeded;

    private GameEventExecutionResult EvaluatePrerequisites()
    {
        if (!_event)
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.MissingEventAsset,
                "GameStateEvent에 시작할 EventSO가 연결되지 않았습니다.");

        if (!GameManager.instance)
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.MissingGameManager,
                "GameManager 인스턴스가 없습니다.");

        if (!MatchesWorld())
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.WrongWorld,
                $"현재 맵이 요구 세계 '{_worldRequirement}'와 일치하지 않습니다.");

        EventManager eventManager = GameManager.instance.eventManager;
        if (!eventManager)
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.MissingEventManager,
                "GameManager에 EventManager가 연결되지 않았습니다.");

        if (_preconditions != null && _preconditions.Length > 0)
        {
            for (int i = 0; i < _preconditions.Length; i++)
            {
                EventFlagCondition condition = _preconditions[i];
                if (condition == null || !condition.flag) continue;

                if (eventManager.HasEventFlag(condition.flag, condition.value)) continue;

                string currentValue = eventManager.TryGetEventFlag(condition.flag, out int value)
                    ? value.ToString()
                    : "미설정";
                return GameEventExecutionResult.Rejected(
                    GameEventRejectReason.FlagMismatch,
                    $"플래그 '{condition.flag.name}' 값이 {currentValue}입니다(필요: {condition.value}).");
            }
        }

        if (_requiredClueIds == null || _requiredClueIds.Length == 0)
            return GameEventExecutionResult.Success();

        RouteModule route = RouteModule.Instance;
        if (route == null || route.Progress == null)
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.MissingRouteProgress,
                "필수 단서를 확인할 RouteModule.Progress가 없습니다.");

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

            if (!acquired)
                return GameEventExecutionResult.Rejected(
                    GameEventRejectReason.MissingClue,
                    $"필수 단서 '{clueId}'를 획득하지 않았습니다.");
        }

        return GameEventExecutionResult.Success();
    }

    // 지금 열려 있는 맵이 이 이벤트가 요구하는 세계인지. 맵 정보를 아직 모르면(로드 중 등)
    // 막지 않는다 - 조건을 아는 시점에 다시 판정되므로, 여기서 막아 이벤트를 통째로 잃는 것보다 낫다.
    private bool MatchesWorld()
    {
        if (_worldRequirement == WorldRequirement.Any) return true;

        MapManager mapManager = GameManager.instance != null ? GameManager.instance.mapManager : null;
        if (mapManager == null || mapManager.CurrentMapData == null) return true;

        return _worldRequirement == WorldRequirement.RealWorld
            ? mapManager.IsRealWorld
            : !mapManager.IsRealWorld;
    }

    public override GameEventExecutionResult TryTriggerEvent()
    {
        GameEventExecutionResult prerequisites = EvaluatePrerequisites();
        if (!prerequisites.Succeeded) return prerequisites;

        EventManager eventManager = GameManager.instance.eventManager;
        if (!eventManager.TryStartEvent(_event, _manualTargets, _interruptRunningEvent, out string rejectionDetail))
            return GameEventExecutionResult.Rejected(
                GameEventRejectReason.EventAlreadyRunning,
                rejectionDetail);

        return GameEventExecutionResult.Success();
    }

    // 기존 직접 호출부는 유지하고, 실제 실행은 결과 API 한 곳으로 모은다.
    public override void TriggerEvent()
    {
        GameEventExecutionResult result = TryTriggerEvent();
        EventDiagnostics.LogDirectGameEventRejection(this, result);
    }
}
