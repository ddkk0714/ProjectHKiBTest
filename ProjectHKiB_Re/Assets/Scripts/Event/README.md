# 역할별 Event Trigger

모든 트리거는 `EventTriggerBase`의 실행 정책(GameEvent, Chunk, Cooldown, Trigger Once)을 공유하고,
감지 원인에 따라 다음 컴포넌트를 사용합니다.

- `AreaEventTrigger`: ZCollider2D 영역의 Enter / Stay / Exit
- `InteractionEventTrigger`: 영역 안의 Press / Hold / Confirm Direction
- `AttackEventTrigger`: `EventAttackSensor`가 전달한 실제 `IDamagable.Damage` 호출
- `EntityDeathEventTrigger`: 로컬 엔티티 또는 플레이어의 `IDamagable.OnDie` 콜백

영역 기반 트리거를 새로 추가하면 `ZCircleCollider2D`와 Unity `CircleCollider2D`가
`RequireComponent`로 함께 구성됩니다. 에디터의 `OnValidate`는 컴포넌트 구성 직후 한 번 더 확인한 뒤
실제 누락만 경고하고, Player 빌드 전 Validator는 `_areaCollider` 참조 누락을 오류로 차단합니다.

기존 `EventInputTrigger`, `EventConfirmDirTrigger`, `EventHoldInputTrigger`, `EventStayTrigger` 에셋은
예전 `_collider2D`에 저장된 일반 `Collider2D`를 호환 경로에서만 계속 사용합니다. 이 예외는 구형 GUID를
유지한 래퍼에만 적용되며, 새 트리거가 요구하는 `ZCollider2D` 계약에는 적용되지 않습니다.

## 상호작용 입력

`InteractionEventTrigger`의 `Input Action`에는 `PlayerAction.inputactions`의 액션 참조를 연결하고,
`Input Process Type`에서 판정 방법을 선택합니다. 런타임에는 `InputManager.GetRuntimeAction`으로 같은 이름의
실제 액션을 읽으므로 액션 맵 Enable/Disable 상태를 그대로 따릅니다.

Hold에는 일반적으로 `InProgress`, 한 번 누르는 상호작용에는 `WasPerformedThisFrame`을 사용합니다.
`EnumManager.InputType`은 새 트리거에서 사용하지 않습니다. 기존 `EventInputTrigger` 계열 프리팹은
에디터 검증 시 직렬화된 옛 값을 대응하는 `InputActionReference`로 한 번 이전합니다.

## 공격 트리거

공격 대상 오브젝트에는 `AttackEventTrigger`를 두고, Damager가 실제로 판정할 전용 콜라이더 오브젝트에
`EventAttackSensor`를 배치합니다. 센서는 실제 HP나 피격 효과를 변경하지 않으며, 공격 필터에 전달할
가상 피해량과 넉백 가능 여부만 계산합니다.

기존 `GameEventTrigger`, `EventStayTrigger`, `EventInputTrigger`, `EventHoldInputTrigger`,
`EventConfirmDirTrigger`는 Scene과 Prefab의 스크립트 GUID 보존용 호환 클래스입니다. 새 오브젝트에는
위 역할별 컴포넌트를 직접 사용합니다.

## 트리거 실행 결과

`EventTriggerBase.Triggered`는 실제 EventSO 시작 완료가 아니라 **공통 제한을 통과한 감지 신호**입니다.
기존 구독자 호환을 위해 이 의미를 유지합니다.

실제 실행 결과는 다음 API에서 확인합니다.

- `LastResult`: 가장 최근 판정 결과
- `Evaluated`: 공통 제한 거부와 GameEvent 실행 결과를 모두 전달하는 이벤트
- `EventTriggerResultStatus`: 감지 거부, 신호 승인, GameEvent 시작, GameEvent 거부, 구독자 억제 구분
- `EventTriggerRejectReason`: 비활성, Chunk 비활성, Trigger Once, 쿨다운, GameEvent 누락 구분
- `GameEventRejectReason`: 월드, 플래그, 단서, EventManager, 진행 중 이벤트 등 실행 거부 구분

`BattleCompletionOnAttack`처럼 감지 신호 자체가 이벤트 진행 조건을 완료하는 구독자는
`EventTriggerContext.SuppressGameEvent()`를 호출합니다. 이렇게 하면 공격 완료 bool은 정상적으로 설정하면서,
같은 `GameStateEvent`를 처음부터 다시 시작하려는 후속 호출만 막을 수 있습니다.

## Unity 로그 진단

Editor와 Development Build에서는 이벤트 진단을 별도 UI 없이 Unity Console과 `Player.log`에 출력합니다.
모든 진단 로그는 `[EventTrace]` 접두사를 사용합니다.

- `[EventTrace][Trigger]`: 감지 승인, 공통 제한 거부, GameEvent 시작/거부
- `[EventTrace][GameEvent]`: 트리거를 거치지 않은 직접 실행의 거부 사유
- `[EventTrace][Event]`: 이벤트 시작, 완료, 중단
- `[EventTrace][State]`: 이벤트 State 전환
- `[EventTrace][Flag]`: EventFlag 설정
- `[EventTrace][Stall]`: 같은 State에서 장시간 대기 중인 이벤트와 전이 정보

Stay 판정이나 쿨다운처럼 반복되는 동일 거부 로그는 트리거별로 중복 억제됩니다. Stall 경고 시간은
`EventManager`의 `Stall Warning Seconds`에서 조절하며 기본값은 10초입니다. Stall 진단은 Decision을
다시 실행하지 않고 마지막 State의 전이 구성과 활성 여부만 출력합니다.
