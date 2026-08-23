# EVT-004 / 005 / 006 구현 인수인계

작성 2026-08-24 · 브랜치 `EventSystem_0823` · 기준 커밋 `22dd7fb4`

이 문서 하나만 읽고 EVT-004~006 작업을 시작할 수 있게 쓴 것이다.
EVT-001~003은 구현·플레이 확인이 끝났고, **EVT-004는 진입 스텁 1단계만** 있다. 005/006은 없다.

---

## 1. 먼저 알아야 할 것 — 이벤트는 코드가 아니라 에디터에서 만든다

`EventDummyGenerator.cs`처럼 C#으로 이벤트를 하드코딩하던 방식은 **폐기됐다.** 지금 구조는:

```
EventChain.asset (저작 데이터)
   └ Tools ▸ Event ▸ 이벤트 체인 편집기 에서 편집
        └ [빌드] 버튼
             └ EventSO + StateSO 여러 개 + 트리거 프리팹 (실행 가능한 에셋) 생성
```

- 저작 데이터: `Assets/Scripts/Event/Test/Generated/EventChain.asset` (`EventChainSO`)
- 산출물: 같은 폴더의 `Dummy_EVT00N*.asset`, `Dummy_EVT00N_Trigger.prefab`
- 편집기: `Assets/Scripts/Event/Chain/Editor/EventChainEditorWindow.cs`
- 데이터 정의: `Assets/Scripts/Event/Chain/EventChainSO.cs`

**런타임 실행 경로(`EventManager` / `GameStateEvent` / `StateController`)는 건드리지 않는다.**
빌드는 그 경로가 원래 먹던 형태(StateSO 체인)를 대신 만들어 줄 뿐이다.

### 두 가지 저작 방법

| 방법 | 언제 |
| :---- | :---- |
| 편집기 창에서 직접 입력 | 값 조정, 액션 추가 — 일상적인 작업 |
| `BuildEvt00NSample()` C# 함수에 적고 "샘플 채우기" | 처음부터 짜거나 통째로 되돌릴 때 |

EVT-001~004는 후자로 초기값이 들어 있다(`EventChainEditorWindow.cs`의 `BuildEvt001Sample` 등).
**EVT-005/006은 `BuildEvt005Sample`/`BuildEvt006Sample`을 새로 만들고 `FillSamples`/`FillSelectedSample`의
분기에 등록하는 방식을 권장한다** — 004까지 그렇게 돼 있어 일관되고, 되돌리기가 쉽다.

### 데이터 모양

```csharp
EventDefinition {
    // 기획 정보 — 기획서 "4. 이벤트 정보" 표와 1:1 (사람이 읽는 칸)
    eventId, eventName, purpose, startTriggerDesc, preconditionDesc,
    interruptCondition, retryPolicy, linkedEvents, narrativeContent

    // 구현 — 빌드가 실제로 읽는 칸
    targets            // EventTargetSearchInfo[]  Player / FromMap / Manual
    preconditions      // EventFlagCondition[]     비면 무조건 발동
    triggerKind        // None / Stay / Input
    triggerRadius, triggerInputType
    steps              // List<EventStepData>
}

EventStepData {
    label
    enterActions       // EventStepAction[] { StateAction action; float waitAfter; }
    advanceWhenAny     // StateDecision[]  하나라도 참이면 다음 단계 (OR)
    timeoutSeconds     // 0이면 없음
}
```

**분기는 없다.** 단계 N → N+1 순차 진행뿐이고, 마지막 단계에서 끝난다.
분기가 필요하면 `EventStepData`에 "다음 단계 인덱스" 필드를 추가해야 한다(아직 필요했던 적 없음).

### 액션 사이 대기 (`waitAfter`)

빌드가 대기 지점에서 State를 쪼갠다. 조각은 `{eventId}_S{i}w{k}`로 생기고,
쪼개지지 않은 단계는 예전 이름 `{eventId}_S{i}` 그대로다.

- 단계의 `advanceWhenAny`/`timeoutSeconds`는 **마지막 조각에만** 붙는다
- **마지막 액션 뒤의 `waitAfter`**는 (그 단계에 진행 조건도 타임아웃도 없으면) 단계 타임아웃으로 승격된다
- `action`을 비우고 `waitAfter`만 넣으면 "여기서 N초 쉰다" 칸 (편집기의 `+ 대기만` 버튼)
- `stepTimeoutMultiplier`(연출 배속)는 **대기에는 안 걸린다** — 조건 대기 타임아웃에만 걸린다

---

## 2. 지금 상태

| 이벤트 | 상태 |
| :---- | :---- |
| EVT-001 금발의 가위질 | 완료 (Stay 트리거, 단서 획득, dood=1) |
| EVT-002 백발 대화 → 보스화 → 넉백 → 암전 → 현실 이동 | 완료 (dood=2) |
| EVT-003 현실의 노트 해몽 | 완료 (해몽 성립 → 시스템 해금 로그 → 노트 닫으면 꿈 복귀) |
| **EVT-004 분노한 금발과 전투** | **진입 스텁 1단계만** — `BuildEvt004Sample` |
| **EVT-005 진정한 금발 대화 / 가위 지급** | **없음** |
| **EVT-006 백발과의 전투 / 최종 탈출** | **없음** |

### 전투 기믹은 범위 밖

사용자 지시(2026-08-22): **"전투 기믹은 다른 곳에서 맡을 거니까 냅두고"**.
EVT-004/006에서 만들 것은 **전투 진입까지의 연출과, 전투 종료 후의 후처리**다.
패링·반사·그로기·각성 게이지·날개 절단 홀딩은 만들지 말 것.
전투 구간은 `DummyEventStepAction`으로 자리만 잡고, 종료 조건도 더미(타임아웃/커스텀 bool)로 둔다.

---

## 3. 기획서 원문 (`낙서세계(2).md` — 저장소 밖, gitignore)

### EVT-004 / 분노한 금발과 전투
- **목적** 감정 합성 및 패링 매커니즘이 있는 퍼즐 풀이로 긴장감 부여
- **시작 트리거** 중앙 집터로 재진입하기
- **사전 조건** Dood == 2, 현실에서 꿈 해몽 완료 후 꿈 복귀
- **중단 조건** 플레이어 사망 → 소름 돋는 사운드와 함께 EVT-003(현실의 방)으로 강제 복귀 리스폰
- **재실행 정책** 클리어 전까지 실패 시 계속 재시작
- **연결 대상** EVT-005
- **연출** 현실→꿈 재진입 시 주변이 완전 검은색. **밀려났던 방향 그대로** 쭉 나아가면 하얀 낙서 공간(집터)이
  나옴. 근처로 가면 이벤트 시작. 화면 전체가 붉은 낙서로 뒤덮이며 금발이 [분노] 탄막 폭격 시작
  (기괴한 노이즈 배경음). 플레이어는 감정을 [분노]로 전환해 패링 → 백발의 뻗친 머리카락에 반사해
  금발의 분노를 폭주시킴 → 폭주 패턴에서 [공포(슬픔+분노)]로 받아쳐 그로기 → 그로기 상태에 다가가
  [평안(기쁨+즐거움)] 파동 주입 → 금발이 진정, 대화 가능한 NPC로 변경

### EVT-005 / 진정한 금발 대화, 머리카락을 없애라~!
- **목적** 메인 시나리오 이해
- **시작 트리거** 전투 후 금발이 대화 가능 상태에서 말 걸기
- **사전 조건** Dood == 2
- **중단 조건** 이탈
- **재실행 정책** 처음부터 대사 출력
- **연결 대상** EVT-006
- **연출** 대화를 시도하고 머리카락을 일단 어떻게든 해야 한다고 설득. 금발이 인정하고 새 장비 **'가위'**를
  넘겨줌. **가위 획득 즉시 dood = 3**

### EVT-006 / 백발과의 전투
- **목적** 전투 및 딜레마적 상황의 재미 제공
- **시작 트리거** 백발과 대화 시도
- **사전 조건** Dood == 3
- **중단 조건** 백발 HP 0 또는 플레이어 사망 → EVT-003(현실의 방)으로 강제 복귀 패널티
- **재실행 정책** 성공 시 최종 종료
- **연결 대상** 맵 탈출 오브젝트
- **연출** 머리가 날개 형태로 변하며 살짝 공중에 뜬 백발이 중앙에서 흑백 탄막을 뿜으며 거대 날개로 바닥을
  내려침. 타이밍 맞춰 감정 전환으로 경직 → 날개 앞으로 이동해 인터랙션 키 → '서걱' 날개 절단(홀딩) →
  날개가 잘릴 때마다 본체 HP 감소, 틈새로 본체가 드러남 → HP 0 전에 틈새 조준선 방향으로
  [각성(기쁨+분노)] 파동을 투사해 각성 게이지 100% 달성
- **최종 탈출** 각성 성공 시 감정 [즐거움] 지급 → 맵 중앙에 거대한 문 생성 → [즐거움] 상태에서 문에
  [가위] 인터랙션 → 화면이 종이처럼 반으로 찢어지며 데모 종료

---

## 4. 쓸 수 있는 재료

### 이벤트 플래그 (`Assets/ScriptableObjects/EventFlags/`)
`Dood.asset` · `FLAG_EMOTION_MERGE.asset` · `FLAG_USE_SCISSORS.asset`

`FLAG_USE_SCISSORS`는 EVT-003 해몽에서 해금되도록 기획돼 있으나 **지금은 안내 로그만 띄우는 상태**다
(사용자 지시: "연관하면 바로 꿈으로 들어가는 게 아니라 시스템 해금(안내 로그만 띄워 일단)").
EVT-005의 가위 지급과 어떻게 맞물릴지는 **확인이 필요한 지점**이다 — 9절 참고.

> 사전 조건은 `EventManager.HasEventFlag` 의미론이다 — **"설정된 적 있고 값이 같은가"**.
> 한 번도 세팅 안 된 플래그는 값이 0이어도 통과하지 않는다. 시작값은 `MapDataSO.initialEventFlags`로 채운다.

### 이벤트 전용 액션
`SetEventFlagAction` `SetRouteEventFlagAction` `AcquireClueAction` `GrantGearAction(GearDataSO)`
`GrantItemAction(ItemDataSO, count)` `SetEntityActiveAction` `ReviveEntityAction`
`ChangeStateMachineAction` `TargetEntityManipulateAction(targetID, 안쪽 액션)`
`TargetEntitiesManipulateAction` `TargetAnimationPlayAction` `SaveInitInfoAction`

### 연출 액션
`ScreenFadeAction` `ScreenNoiseAction` `ScreenFlashAction` `ScreenTearAction`
`CameraZoomAction` `CameraShakeAction` `SetCameraFollowAction`
`PlayAudioOneShotAction` `PlayParticleOneShotAction` `SetSpriteTintAction`
`PlayAnimationAction` `ReserveAnimationAction`
`SetInputModeAction` `ChangeMapAction` `TeleportAction` `KnockBackAction` `OpenWindowAction`
`DialogueStartAction` `DialogueShowLineAction` `DialogueExitAction`
`MarkUnscaledTimeAction` `DummyEventStepAction`

> `ScreenTearAction`은 EVT-006 최종 탈출("화면이 종이처럼 반으로 찢어지며")에 그대로 쓰라고 만든 것이다.

### 판정
`DialogueEndedDecision` `DialogueLineEndedDecision` `DialogueChoosedDecision`
`MapLoadedDecision` `ScreenEffectEndedDecision` `WindowClosedDecision`
`DreamReadingResolvedDecision` `KnockbackEndedDecision` `AnimationEndedDecision`
`CustomBoolDecision` `CustomIntDecision` `UnscaledTimeElapsedDecision` `TimerDecision`
`PlayerInputDecision` `RandomDecision`

### 트리거
- `EventStayTrigger` — 반경 안에 들어오면 발동. 무장(arm) 방식이라 **나갔다 다시 들어와야** 재발동
- `EventInputTrigger` — 반경 안에서 지정 입력. **release→press**를 요구(눌린 채로 연속 발동 방지)
- `EventHoldInputTrigger` · `EntityDeathEventTrigger` — 있으나 자동 배선 대상 아님

빌드가 `triggerKind`에 따라 `ZCircleCollider2D`(radius, height=4, isTrigger, LayerMask "Player")까지 붙여
프리팹을 만든다. 씬에는 그 프리팹을 드래그해 놓기만 하면 된다.

---

## 5. 반드시 지켜야 하는 규칙 (전부 실제로 데인 것들)

### 1. 판정에는 대상 개념이 없다 — `targetID`를 반드시 채울 것
이벤트 상태 기계는 **EventManager 위에서** 돈다. 그래서 판정에 넘어오는 `stateController`는
연출 대상이 아니라 EventManager다. 액션은 `TargetEntityManipulateAction`으로 대상을 감싸지만
**판정에는 그런 감싸개가 없다.**

`KnockbackEndedDecision`이 이 문제로 즉시 통과해 넉백 애니메이션이 한 프레임 만에 사라졌다.
지금은 `targetID` 필드가 있다. **엔티티 상태를 보는 판정을 새로 만들면 같은 구조로 만들 것.**

### 2. 컷신 모드 두 가지를 구분할 것

| 모드 | timeScale | 언제 |
| :---- | :---- | :---- |
| `Cutscene` | **0** | 일반 연출. 물리가 필요 없을 때 |
| `CutsceneLive` | 1 | **물리가 돌아야 하는 연출**(넉백, 낙하) |

`Cutscene`에서는 FixedUpdate가 안 돈다. 넉백을 여기서 부르면 힘만 쌓이고 아무 일도 안 일어난다.

### 3. 단계 타임아웃은 unscaled 시간으로
`StateSO.useTimer`/`TimerDecision`은 `TimerManager`(DOTween 스케일 시간)를 탄다.
컷신이 timeScale을 0으로 만드는 순간 **그 타이머도 같이 얼어 이벤트가 스스로를 가둔다.**
그래서 빌드는 `MarkUnscaledTimeAction` + `UnscaledTimeElapsedDecision` 쌍을 쓴다. **직접 만들지 말 것.**

### 4. 커스텀 변수는 SO 에셋과 같은 객체다
`StateController.customVariables`는 `StateMachineSO.customVariables`를 **참조로** 물어간다
(코드에 `HAVE TO FIX THIS NOT TO DEEP REFERENCE CUSTOMVARS!!!` 경고가 있다).
**플레이 중 쓴 값이 에셋에 그대로 남는다.**

여기서 이번 세션 최대의 버그가 나왔다 — `Initialize`가 `ResetStateMachine`(= 초기 State 진입 액션 실행)을
**먼저** 부르고 `customVariables`를 **나중에** 대입해서, 진입 시 찍은 시각 표식이 버려지고
읽는 쪽은 **지난 플레이에서 남은 값**을 봤다. 단계 대기가 13.9초로 늘어난 원인.
지금은 순서를 고쳤고 빌드 때 표식을 0으로 리셋한다.

**"플레이할 때마다 값이 달라진다"류 증상은 이 지속성부터 의심할 것.**

### 5. 대상은 `Initialize`보다 먼저 넘어간다
`EventManager.StartEvent`가 `FindTargets` → `CurrentTargets` 대입 → `Initialize` 순서다.
`Initialize`가 첫 State의 진입 액션을 그 자리에서 실행하기 때문. 이 순서를 되돌리지 말 것.

### 6. 비활성 대상에 상태 기계를 갈아끼우지 말 것
꺼진 GameObject에 `ChangeStateMachineAction`을 쓰면 `StartCoroutine`이 터진다. 가드는 있지만
**퇴장 연출은 비활성화보다 먼저** 배선할 것(꺼진 뒤에는 애니메이션도 못 튼다).

### 7. 더미 NPC의 정렬 레이어는 `Entity`
`Default`면 맵의 `Bottom` 뒤로 들어가 안 보인다. 순서는 `Default → Bottom → Effect → Wall → Entity → Top`.

### 8. 도감/노트/지도/인터넷은 **현실 맵에서만** 열린다
`UIManager.realWorldOnlyWindows`가 `MapDataSO.isRealWorld`로 막는다.
꿈에서 단서를 획득할 때는 `AcquireClueAction { openCodexImmediately = false }`로 조용히 받게 할 것.

### 9. 반복 애니메이션은 스스로 안 끝난다
플레이어 상태 기계(`Delta_Base_StateMachine_test`)에는 **Knockback 상태가 없다**(Roza/Lily에만 있다).
그래서 이벤트가 튼 반복 클립은 이벤트가 직접 걷어내야 한다 — 넉백을 시작/유지/종료 3단으로 나눈 이유.
`ReserveAnimationAction`은 지금 클립이 한 바퀴 끝나면 이어서 틀 클립을 예약한다(시작 클립 길이를 몰라도 됨).

### 10. 유니티 없이 컴파일 확인 가능
번들 Roslyn + 직접 만든 rsp로 돌린다. `reference_unity_compile_check` 메모리 참고.
지금 기준 **에러 0건**이며, `PhysicsManager`의 CS0172 1건은 기존 에러라 무시한다.

---

## 6. EVT-004 구현 시 유의점

### 진입 연출의 핵심은 "밀려났던 방향"
기획서: *"밀려났던 방향 그대로 쭉 나아가보면 하얀 낙서 공간이 나옴"*.
EVT-002가 플레이어를 넉백시킨 방향과 EVT-004 집터의 배치가 **이어져야** 한다.
지금 EVT-002의 넉백 방향은 `KnockbackDirectionMode.Backward`(대상 기준 반대편)이라
NPC_B의 위치에 따라 결정된다 — 집터를 그 연장선에 놓거나, 방향을 `Fixed`로 못 박아야 한다.
**어느 쪽으로 갈지는 확인이 필요한 지점이다(9절).**

### "주변은 완전 검은색"
`ScreenFadeAction`으로 암전을 유지한 채 맵을 띄우고, 집터 근처에서 걷히는 식이 자연스럽다.
꿈 복귀는 EVT-003 마지막(노트를 닫으면 꿈으로)에서 이미 `ChangeMapAction` + `MapLoadedDecision`으로 돌고 있으니
그 뒤를 잇는다.

### 사망 시 EVT-003 강제 복귀
사용자 지시(2026-08-22): **침대는 미구현이므로 "현실의 방" 복귀만 우선**.
`EntityDeathEventTrigger` + `ChangeMapAction`(현실 맵)으로 배선하되, 지금 현실 맵은
**`TestMap2`가 대역**이다(`MapDataSO.isRealWorld`가 켜진 맵을 `FindRealWorldMap()`이 찾는다).

### 전투 구간
`DummyEventStepAction`으로 자리만 잡고 `CustomBoolDecision`(예: `EVT004_BattleCleared`)으로 빠져나가게 둔다.
그래야 나중에 전투 담당이 그 bool만 세워주면 이벤트가 이어진다.

---

## 7. EVT-005 구현 시 유의점

가장 단순하다 — 대화 + 지급 + 플래그.

1. `EventInputTrigger`(OnConfirm), 사전 조건 `Dood == 2`
2. `SetInputModeAction(Cutscene)` → `DialogueStartAction` → `DialogueShowLineAction` 여러 줄
   - 줄마다 단계를 나누고 `DialogueLineEndedDecision`으로 넘긴다(EVT-002 S0 패턴 그대로)
   - **타임아웃 안전장치를 반드시 걸 것** — 대화 모듈이 씬에 없으면 영영 안 넘어간다(`DialogueTimeout = 15f`)
3. `GrantGearAction { gear = 가위 GearDataSO }`
   - **가위 GearDataSO가 아직 없다.** 기존 기어는 `Assets/Resources/Items/Gears/`에
     Default / Delta / Hadaka / Lily / Roza / Training 6종뿐이다. 새로 만들어야 한다
4. `SetEventFlagAction { flag = Dood, flagValue = 3 }` — 기획서상 **가위 획득 즉시**
5. `DialogueExitAction` + `SetInputModeAction(Play)`

**재실행 정책이 "처음부터 대사 출력"**이므로 트리거를 일회성으로 막지 말 것.
다만 dood가 3이 된 뒤에는 사전 조건(`Dood == 2`)이 자연히 안 맞아 다시 안 뜬다 —
그게 의도인지는 확인이 필요하다(9절).

---

## 8. EVT-006 구현 시 유의점

- 트리거: 백발과 대화 시도(`EventInputTrigger`, OnConfirm), 사전 조건 `Dood == 3`
- 전투 구간은 EVT-004와 같이 더미 + `CustomBoolDecision`
- **최종 탈출**이 이 이벤트의 진짜 구현 대상이다:
  - 각성 성공 → 감정 [즐거움] 지급 (감정 시스템은 EmotionVector 모듈로 이미 있음)
  - 맵 중앙에 문 생성 → `SetEntityActiveAction`으로 미리 꺼둔 문을 켜는 방식이 가장 단순
  - 문에 [가위] 인터랙션 → `EventInputTrigger` + 사전 조건(`FLAG_USE_SCISSORS`)
  - `ScreenTearAction` → 데모 종료
- 사망/HP 0 → EVT-003 복귀는 EVT-004와 같은 배선

---

## 9. 확인이 필요한 지점 (진행 전 사용자에게 물을 것)

1. **EVT-004 집터 위치와 넉백 방향의 연결** — 기획서의 "밀려났던 방향 그대로"를 살리려면
   EVT-002의 넉백 방향을 `Fixed`로 못 박을지, 아니면 집터를 NPC_B 반대편에 배치할지
2. **가위 `GearDataSO`를 새로 만들지, 기존 기어를 대역으로 쓸지** — 아트 리소스가 없는 상태다
3. **`FLAG_USE_SCISSORS` 해금 시점** — 기획서는 EVT-003 해몽에서 해금인데 지금은 안내 로그뿐이다.
   EVT-005의 가위 지급과 둘 다 필요한지, 하나로 합칠지
4. **EVT-005 재실행** — dood가 3이 되면 사전 조건이 안 맞아 다시 안 뜬다. "처음부터 대사 출력"이
   재대화를 뜻하는지, 실패 시 재시작만 뜻하는지
5. **전투 클리어 신호의 이름** — `CustomBoolDecision`으로 둘 때 전투 담당과 맞춰야 할 bool 이름

---

## 10. 작업 순서 권장

1. 유니티에서 **Tools ▸ Event ▸ 이벤트 체인 편집기** → **[빌드]** 한 번
   (직전 커밋에서 고아 State 3개를 지웠으므로 에셋과 데이터를 다시 맞춘다)
2. 9절을 사용자에게 확인
3. EVT-005부터 (가장 단순하고 의존이 적다) → EVT-004 → EVT-006
4. 각 이벤트마다 `BuildEvt00NSample()` 작성 → "샘플 채우기" → "빌드" → 플레이 확인
5. `Docs/Map_Event_Dev_Plan.md`에 검증 결과를 이어 쓸 것 (이 저장소의 관례다)

### 커밋 관례
- 메시지는 짧게: `20260824 이벤트 005/006 구현` 정도. 상세 이유는 코드 주석과 이 문서에
- **커밋 개수도 적게.** 후속 작업은 새 커밋을 만들지 말고 아직 PR에 안 넣은 같은 영역 커밋에 흡수
- 커밋 전 무관한 파일이 딸려가는지 확인: `neodgm.asset`(폰트 아틀라스), `*_StateMachine_packed.asset`
  (줄바꿈만 바뀜), `System.unity`의 카메라 뷰포트 — 전부 실제로 딸려왔던 것들이다
- `Generated/` 폴더의 시각 표식(`*_mark`) 값이 0이 아니면 플레이 잔재다

---

## 11. 참고 문서

| 문서 | 내용 |
| :---- | :---- |
| `Docs/Map_Event_Dev_Plan.md` | 구현 중 만난 문제와 원인·수정 기록 (이번 세션 전량) |
| `Docs/Map_Event_Implementation_Plan.md` | 이벤트별 검증(완료) 조건 |
| `낙서세계(2).md` | 기획서 원본 — **저장소 밖, gitignore** |

세 문서 모두 저장소에서 추적되지 않는다. 기획 정보의 살아있는 원본은
**`EventChain.asset`의 기획 정보 칸**(eventName ~ narrativeContent)이다 — 새 이벤트를 만들 때
그 칸들을 반드시 채울 것.
