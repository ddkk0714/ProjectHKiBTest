# Reliable Triggers

기존 `Event/Triggers`를 수정하지 않고 교체 테스트할 수 있는 단일 트리거 체계입니다.

## 일반 영역 트리거

1. 트리거 오브젝트에 `ZCollider2D`와 `ReliableGameEventTrigger`를 추가합니다.
2. `Attack Only`를 끄고 `Activation`에서 Enter, Stay, Exit, Input, Confirm Direction 중 하나를 고릅니다.
3. `Layers`, `Required Name`, `Target Scope`로 대상을 제한합니다.
4. 기존 `GameSimpleEvent` 또는 `GameStateEvent`를 `Game Event`에 연결합니다. 같은 오브젝트나 부모에 있으면 자동으로 찾습니다.

Input 또는 Confirm Direction을 고른 경우 `Input Action`에 `StateTransition.trigger`와 같은 `InputActionReference`를 연결하고, `Input Process Type`에서 `PlayerInputDecision`과 동일한 판정 방식을 고릅니다. 이 트리거는 액션을 임의로 Enable/Disable하지 않으므로 기존 `InputManager`의 액션 맵 전환을 그대로 따릅니다.

일반 감지는 `FixedUpdate`마다 `ZCollider2D.OverlapCollider` 결과를 비교합니다. 그래서 씬 시작부터 겹친 대상, 순간이동한 대상, 비활성화 후 다시 나타난 대상도 Enter/Exit 상태로 정리되며 프로젝트의 커스텀 Z 높이 판정도 유지됩니다. 입력 조건만 `Update`에서 읽어 `WasPerformedThisFrame` 같은 한 프레임 신호가 물리 프레임 사이에서 사라지는 일을 줄입니다.

## 공격 트리거

1. `ReliableGameEventTrigger`의 `Attack Only`를 켭니다.
2. 공격 판정 전용 자식 오브젝트를 만들고 `Collider2D`와 `ReliableEventAttackSensor`를 추가합니다.
3. 이 자식 오브젝트의 레이어를 해당 `DamageDataSO.damageLayer`에 포함되는 레이어로 설정합니다.
4. `Layers`와 공통 `Required Name`은 공격자를 검사합니다. `Attack/Required Attacker Name`을 사용하면 Scene을 넘는 공격자를 이름으로 제한할 수 있고, Exact/Contains/Starts With 및 대소문자 무시 여부를 선택할 수 있습니다. 런타임 프리팹의 `(Clone)` 접미사를 허용하려면 Starts With가 편리합니다.
5. 같은 Scene의 특정 인스턴스만 허용해야 할 때만 `Required Attacker` GameObject를 사용합니다. 예상 피해량 범위와 넉백 필요/금지도 `Attack`에서 설정할 수 있습니다.

공격 센서는 일반 접촉이나 Trigger 메시지로 공격을 추측하지 않습니다. 기존 `Damager`가 실제 공격 영역을 계산한 뒤 호출하는 `IDamagable.Damage(DamageDataSO, IAttackable, Vector3)`만 받습니다. 센서는 실제 HP나 피격 이펙트를 변경하지 않으며, 필터의 피해량은 센서에 설정한 가상 방어력/저항 기준의 비치명타 예상값입니다.

실행 직전의 대상과 공격 정보는 `ReliableGameEventTrigger.LastContext` 또는 `Triggered` C# 이벤트에서 읽을 수 있습니다.
