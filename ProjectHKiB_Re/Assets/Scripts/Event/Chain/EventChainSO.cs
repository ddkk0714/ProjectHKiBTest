using System;
using System.Collections.Generic;
using StateMachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 이벤트 체인 하나(예: EVT-001~EVT-004)를 에셋 하나에 담는 저작 데이터.
///
/// [왜 필요한가] StateMachineSO/StateSO 그래프는 캐릭터 전투용으로 만들어진 범용 도구라,
/// "이벤트 하나 = 순차 단계 여러 개"만 표현하고 싶을 때도 단계마다 StateSO를 따로 만들고
/// 전이·타이머까지 손으로 배선해야 한다(이전엔 EventDummyGenerator.cs가 이 배선을 C# 코드로
/// 하드코딩했다). 이 SO 하나만 편집하면, EventChainEditorWindow의 "빌드" 버튼이 실제
/// EventSO/StateSO/트리거 프리팹(런타임이 실제로 도는 형태)을 자동으로 만들어낸다 — 실행 경로
/// (EventManager/GameStateEvent/StateController)는 하나도 바뀌지 않는다.
///
/// [모양의 한계] 분기 없이 순차 진행만 표현한다(단계 N → N+1, 마지막 단계는 종료).
/// 각 단계는 진입 액션과 "다음으로 넘어가는 조건"(여러 개면 OR)을 가진다. 지금까지 설계한
/// 이벤트(EVT-001~004)가 전부 이 모양이라 분기가 필요했던 적이 없다 — 필요해지면 이 파일의
/// EventStepData에 "다음 단계 인덱스 지정" 필드를 추가하면 된다.
///
/// [기획서와의 관계] EventDefinition의 앞부분(이벤트 이름~연출 및 내용)은 원래 낙서세계 기획서
/// "4. 이벤트 정보" 표 양식을 옮겨 온 것이지만, 필드 자체는 특정 프로젝트에 매인 게 아니라
/// "이벤트 ID/목적/트리거/조건/재실행 정책/연결 대상/연출 내용" 같은 범용 이벤트 기획 항목이다 —
/// 다른 이벤트(다른 프로젝트의 것이든)를 저작할 때도 그대로 쓸 수 있다. 기획서 원본이 저장소에서
/// 추적되지 않는 프로젝트라면, 이 필드들이 사실상 그 문서를 대신하는 살아있는 원본이 된다.
/// 뒤쪽(대상/발동 조건/단계)은 "빌드"가 실제로 읽어서 실행 가능한 이벤트로 바꾸는 구현 데이터다.
/// </summary>
[CreateAssetMenu(fileName = "EventChain", menuName = "Event/Event Chain")]
public class EventChainSO : ScriptableObject
{
    // 빌드할 때 모든 단계의 타임아웃에 곱하는 배수. 로그가 너무 빨리 지나가 흐름을 못 볼 때
    // 2~3으로 올려 빌드하면 전체가 그만큼 느려진다. 실제 연출 속도를 바꾸는 게 아니라 "지금 굽는
    // 결과물"에만 반영되므로, 확인이 끝나면 1로 되돌리고 다시 빌드하면 된다.
    [Min(0.1f)] public float stepTimeoutMultiplier = 1f;

    public List<EventDefinition> events = new();
}

[Serializable]
public class EventDefinition
{
    // ─── 기획 정보 — 낙서세계 기획서 "4. 이벤트 정보" 표와 1:1 대응 ────────────
    // EventSO/StateSO/트리거 프리팹의 파일명 접두사로도 그대로 쓰인다. 예: "Dummy_EVT001".
    public string eventId = "EVT-000";
    public string eventName = "";
    public string purpose = "";                        // 목적
    [TextArea(2, 4)] public string startTriggerDesc = "";     // 시작 트리거
    [TextArea(2, 4)] public string preconditionDesc = "";     // 사전 조건 (서술 — 아래 preconditions는 그걸 실제로 판정하는 데이터)
    [TextArea(2, 4)] public string interruptCondition = "";   // 중단 조건
    public string retryPolicy = "";                     // 재실행 정책
    public string linkedEvents = "";                    // 연결 대상
    [TextArea(4, 16)] public string narrativeContent = "";    // 연출 및 내용

    // ─── 구현 — "빌드"가 읽어서 실제 이벤트로 바꾸는 데이터 ────────────────────
    // 이벤트가 발동하는 대상 검색 방식(Player/FromMap/Manual). EventSO.involvedEventTargets와 동일한 형식.
    public EventTargetSearchInfo[] targets = Array.Empty<EventTargetSearchInfo>();

    // 트리거(GameStateEvent)에 그대로 들어가는 사전 조건. 비어 있으면 무조건 발동.
    // 위 preconditionDesc가 "dood == 1"이라고 서술하면 여기에 그 판정을 실제로 걸어야 발동한다 —
    // 서술은 사람이 읽는 것, 이 배열은 게임이 판정하는 것이라 둘은 손으로 맞춰야 한다.
    public GameStateEvent.EventFlagCondition[] preconditions = Array.Empty<GameStateEvent.EventFlagCondition>();
    public string[] requiredClueIds = Array.Empty<string>();

    // 위 startTriggerDesc(서술)를 실제로 판정하는 콜라이더 트리거. None이면 프리팹에 GameStateEvent만
    // 있고, 씬에서 EventSystemTestbed처럼 코드로 TriggerEvent()를 직접 불러야 한다 — 예전엔 이게
    // 유일한 방법이었다. Stay/Input을 고르면 "빌드"가 콜라이더까지 자동으로 붙여서, 씬에 드래그해
    // 놓기만 하면 실제로 반경 안에 들어오는 순간(Stay) 또는 반경 안에서 확인 키를 누르는 순간(Input)
    // 발동한다. ConfirmDir(방향 확인)은 이 프로젝트 이벤트 어디서도 아직 쓰인 적이 없어 자동 배선
    // 대상에서 뺐다 — 필요해지면 씬에서 손으로 EventConfirmDirTrigger를 붙이면 된다.
    // "현실일 것" / "꿈일 것". 진행도 플래그로 대신하지 말 것 — 진행도가 더 올라가면 근사가 깨져
    // 이벤트가 조용히 안 뜬다(EVT-003이 dood == 2로 대신하다가 정확히 그 사고를 냈다).
    public WorldRequirement worldRequirement = WorldRequirement.Any;

    // 진행 중인 이벤트를 끊고 시작할지. 사망 복귀처럼 "하던 걸 무르고 반드시 끼어들어야" 하는
    // 이벤트만 켠다. 평상시엔 꺼두는 게 맞다 — EventManager의 재진입 가드가 "트리거가 겹쳐
    // 이벤트가 처음부터 되감기는" 사고를 잡아주는데, 이걸 켜면 그 보호가 사라진다.
    public bool interruptRunningEvent;

    public EventTriggerKind triggerKind = EventTriggerKind.None;
    [Min(0.1f)] public float triggerRadius = 1.5f;
    [Tooltip("Interaction 트리거가 읽을 Input System 액션입니다. 비우면 생성 도구가 PLAY/Confirm을 사용합니다.")]
    public InputActionReference triggerInputAction;

    [Tooltip("Interaction 트리거가 InputAction 상태를 판정할 규칙입니다.")]
    [EnumDropdown(typeof(EnumManager.InputProcessType))]
    public EnumManager.InputProcessType triggerInputProcessType = EnumManager.InputProcessType.WasPerformedThisFrame;

    public List<EventStepData> steps = new();
}

// 이벤트가 성립하려면 어느 세계에 있어야 하는가. MapDataSO.isRealWorld를 직접 본다.
public enum WorldRequirement
{
    Any,        // 어디서든
    RealWorld,  // 현실 맵에서만 (해몽/노트처럼 현실 전용인 것)
    Dream,      // 꿈 맵에서만
}

public enum EventTriggerKind
{
    None,   // 콜라이더 없음 — 코드에서 TriggerEvent()를 직접 불러야 한다.
    Stay,   // 반경 안에 들어오면 즉시 발동(EventStayTrigger). "접근하면"류.
    Input,  // 반경 안에서 지정 입력을 누르면 발동(EventInputTrigger). "말을 건다/상호작용"류.

    // 플레이어가 죽는 순간 발동(EntityDeathEventTrigger). 콜라이더를 쓰지 않는다 - 사망은 겹침이
    // 아니라 순수 콜백 사건이라서다. 사망 복귀 이벤트가 이걸 쓴다.
    PlayerDeath,
}

// 진입 액션 하나와, 그 뒤에 쉬는 시간.
//
// waitAfter가 0이면 다음 액션과 같은 프레임에 이어서 실행된다(예전 동작). 0보다 크면 빌드가 그
// 지점에서 State를 쪼개, 그만큼 기다린 뒤 다음 액션으로 넘어간다 — 넉백 뒤 뜸 들이기, 암전 뒤
// 잠깐 두기처럼 "연출 사이의 간격"을 여기서 조절한다.
//
// 대기는 State 타임아웃(unscaled 시간)으로 구현되므로 정지형 컷신에서도 정상적으로 흐른다.
[Serializable]
public class EventStepAction
{
    [SerializeReference, SubclassSelector] public StateAction action;

    [Min(0f)] public float waitAfter;

    // 샘플 코드처럼 액션만 나열해도 되게 해 준다 — 대기가 필요한 자리에서만 waitAfter를 적는다.
    public static implicit operator EventStepAction(StateAction action) => new() { action = action };
}

[Serializable]
public class EventStepData
{
    // 에디터 목록 표시용 라벨. 빌드 시 StateSO 이름(EVT001_S0_라벨)에도 그대로 쓰인다.
    public string label = "";

    public EventStepAction[] enterActions = Array.Empty<EventStepAction>();

    // 이 중 하나라도 참이면 다음 단계로 넘어간다(OR). timeoutSeconds와 함께 쓰면 "실제 조건 OR 타임아웃"이 된다.
    [SerializeReference, SubclassSelector] public StateDecision[] advanceWhenAny = Array.Empty<StateDecision>();

    // 0이면 타임아웃 없음. 0보다 크면 advanceWhenAny가 한 번도 안 맞아도 이 시간 뒤 자동으로 다음 단계로
    // 넘어간다 — 대화 모듈처럼 씬에 없을 수도 있는 시스템을 기다리는 조건에 안전장치로 걸어둔다.
    [Min(0f)] public float timeoutSeconds = 0f;
}
