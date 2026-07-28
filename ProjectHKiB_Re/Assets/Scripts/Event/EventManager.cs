using System;
using System.Collections.Generic;
using UnityEngine;
using AYellowpaper.SerializedCollections;

[Serializable]
public class EventTargets
{
    public EventTargets()
    {
        targetEntities = new();
        targetAnimations = new();
    }
    public SerializedDictionary<string, EventControllableEntity> targetEntities;
    public SerializedDictionary<string, EventControllableAnimation> targetAnimations;
}

public class EventManager : StateController, IEventSaveProvider
{
    public enum TargetSearchType { Player, FromMap, Manual }

    // 스토리/월드 이벤트 플래그의 단일 진실 — 인스펙터에서 저작한 값도, 플레이 중 인스펙터에서
    // 직접 고친 값도, SetEventFlag로 들어온 값도 전부 여기 하나에 모인다. 판정·세이브 모두
    // 이 딕셔너리를 본다.
    public SerializedDictionary<EventFlagSO, int> eventFlags;

    // ─── 세이브에서 읽었지만 에셋을 못 찾은 항목 (2026-07-28) ────────
    // 세이브 파일에는 에셋 참조를 넣을 수 없어 문자열 ID(EventFlagSO.Id = 에셋 GUID)로 저장하는데,
    // 로드할 때 그 ID로 EventFlagSO를 되찾으려면 에셋들이 Resources 폴더 아래 있거나 별도
    // 레지스트리 에셋이 있어야 한다. 대신 이 프로젝트가 가진 성질을 이용한다 — 플래그를 읽는 쪽
    // (EventControllableEntity/Animation.Initialize, SetEventFlagAction)은 **항상** EventFlagSO
    // 참조를 손에 들고 들어온다. 그래서 로드 시점에 에셋을 몰라도 여기 담아뒀다가, 나중에 그
    // 참조를 들고 조회가 들어오는 순간 eventFlags로 승격시키면 된다(TryGetEventFlag 참고).
    private readonly Dictionary<string, int> _pendingFlagsById = new();

    public EventTargets currentTargets;

    public void SetEventFlag(EventFlagSO flag, int value)
    {
        if (flag == null) return;

        eventFlags ??= new();
        eventFlags[flag] = value;
        _pendingFlagsById.Remove(flag.Id);
    }

    // 플래그가 "설정된 적 있고 그 값이 value와 같은가" — 기존 호출부
    // (eventFlags.ContainsKey(flag) && eventFlags[flag] == condition)와 정확히 같은 의미다.
    // 설정된 적 없는 플래그는 value가 0이어도 매치되지 않는다.
    public bool HasEventFlag(EventFlagSO flag, int value)
        => TryGetEventFlag(flag, out int current) && current == value;

    public bool TryGetEventFlag(EventFlagSO flag, out int value)
    {
        value = 0;
        if (flag == null) return false;

        if (eventFlags != null && eventFlags.TryGetValue(flag, out value)) return true;

        // 로드로 들어왔지만 에셋을 몰라 대기 중이던 항목 — 이제 참조를 알았으니 승격시킨다.
        // 조회 경로에서 상태를 바꾸는 게 깔끔하진 않지만, ID → 에셋 역참조 없이 인스펙터 표시까지
        // 정상화하려면 "참조가 들어오는 순간"이 유일한 기회다.
        if (_pendingFlagsById.TryGetValue(flag.Id, out value))
        {
            eventFlags ??= new();
            eventFlags[flag] = value;
            _pendingFlagsById.Remove(flag.Id);
            return true;
        }

        return false;
    }

    // ─── 세이브 연동 (IEventSaveProvider, 2026-07-28) ────────────────
    // SaveModule.eventProvider는 단일 슬롯이라 RouteModule이 이미 차지하고 있었는데, SaveModule이
    // GameManager.instance.eventManager를 직접 붙잡고 EventFlags/ImportFlags라는 별도 API로
    // 특별 취급하던 상태였다(부채로 예고됨, SaveTester.cs 주석 참고). SaveModule이 이제 provider
    // 목록을 합성하므로, EventManager도 RouteModule과 동일하게 이 표준 인터페이스로 들어온다.
    public string ProviderId => "EventManager";

    public Dictionary<string, int> EventFlags
    {
        get
        {
            var snapshot = new Dictionary<string, int>();

            if (eventFlags != null)
            {
                foreach (var kv in eventFlags)
                {
                    if (kv.Key == null) continue;
                    snapshot[kv.Key.Id] = kv.Value;
                }
            }

            // 아직 조회된 적 없어 승격되지 않은 항목도 그대로 다시 저장해야 유실되지 않는다.
            foreach (var kv in _pendingFlagsById)
                snapshot[kv.Key] = kv.Value;

            return snapshot;
        }
    }

    // ResetForLoad()가 채워두는 "이번 로드 시작 시점에 알던 ID→에셋" 캐시 — 로드 중 여러 번 오는
    // SetEventFlag(string,int) 개별 호출마다 eventFlags를 통째로 훑지 않도록 1회만 만들어 재사용한다.
    private Dictionary<string, EventFlagSO> _knownAssetsByIdForLoad;

    public void ResetForLoad()
    {
        _knownAssetsByIdForLoad = new Dictionary<string, EventFlagSO>();
        if (eventFlags != null)
        {
            foreach (var kv in eventFlags)
                if (kv.Key != null) _knownAssetsByIdForLoad[kv.Key.Id] = kv.Key;
        }

        eventFlags ??= new();
        eventFlags.Clear();
        _pendingFlagsById.Clear();
    }

    // IEventSaveProvider 전용 — EventFlagSO 참조 없이 ID만으로 들어오는 로드 경로.
    // 위 SetEventFlag(EventFlagSO, int)(게임플레이용)와는 오버로드로 공존한다.
    public void SetEventFlag(string id, int value)
    {
        if (string.IsNullOrEmpty(id)) return;

        if (_knownAssetsByIdForLoad != null && _knownAssetsByIdForLoad.TryGetValue(id, out var asset) && asset != null)
        {
            eventFlags ??= new();
            eventFlags[asset] = value;
        }
        else
        {
            _pendingFlagsById[id] = value;
        }
    }

    // EventManager는 "통로 개방" 개념이 없다 — RouteModule과 인터페이스를 공유하기 위한 빈 구현.
    public Dictionary<string, bool> Passages => _emptyPassages;
    private static readonly Dictionary<string, bool> _emptyPassages = new();
    public void SetPassage(string id, bool opened) { }

    public void StartEvent(EventSO eventSO, EventTargets manualTargets = null)
    {
        FindTargets(eventSO, manualTargets);
        Initialize(eventSO);
        if (TryGetInterface(out IEvent @event)) @event.CurrentTargets = currentTargets;
    }

    public void FindTargets(EventSO eventSO, EventTargets manualTargets)
    {
        currentTargets = new();
        for (int i = 0; i < eventSO.involvedEventTargets.Length; i++)
        {
            EventTargetSearchInfo target = eventSO.involvedEventTargets[i];
            if (target.targetSearchType == TargetSearchType.Player)
            {
                currentTargets.targetEntities[target.ID] = GameManager.instance.player.GetComponent<EventControllableEntity>();
            }
            else if (target.targetSearchType == TargetSearchType.FromMap)
            {
                MapLocalManager localManager = GameManager.instance.mapManager.localManager;
                if (!localManager) continue;
                EventTargets targets = localManager.allEventTargets;
                if (targets.targetEntities.ContainsKey(target.ID)) currentTargets.targetEntities[target.ID] = targets.targetEntities[target.ID];
                else if (targets.targetAnimations.ContainsKey(target.ID)) currentTargets.targetAnimations[target.ID] = targets.targetAnimations[target.ID];

            }
            else if (target.targetSearchType == TargetSearchType.Manual && manualTargets != null)
            {
                currentTargets.targetEntities[target.ID] = manualTargets.targetEntities[target.ID];
                currentTargets.targetAnimations[target.ID] = manualTargets.targetAnimations[target.ID];
            }
        }
    }

}
