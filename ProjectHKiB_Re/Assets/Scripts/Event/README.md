# 역할별 Event Trigger

모든 트리거는 `EventTriggerBase`의 실행 정책(GameEvent, Chunk, Cooldown, Trigger Once)을 공유하고,
감지 원인에 따라 다음 컴포넌트를 사용합니다.

- `AreaEventTrigger`: ZCollider2D 영역의 Enter / Stay / Exit
- `InteractionEventTrigger`: 영역 안의 Press / Hold / Confirm Direction
- `AttackEventTrigger`: `EventAttackSensor`가 전달한 실제 `IDamagable.Damage` 호출
- `EntityDeathEventTrigger`: 로컬 엔티티 또는 플레이어의 `IDamagable.OnDie` 콜백

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
