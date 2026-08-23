# Composable Attack

기존 `AttackableModule`/`Damager`의 단일 공격 상태를 확장하지 않고, StateMachine이 독립 공격 인스턴스를 시작하고 조회하는 새 시스템이다. 기존 `DamageDataSO`, `IAttackable`, `IDamagable` 피해 계약만 재사용한다.

## 설정

1. 공격할 `StateController` 오브젝트에 `CombatAttackModule`을 추가한다.
2. `Create > Scriptable Objects > Attack > Composable Attack`에서 공격 정의를 만든다.
3. State의 `EnterActions` 또는 `actionSequence`에 `StartComposableAttackAction`을 넣는다.
4. 공격자 이동이 필요하면 `MoveCombatOwnerAction`을 `UpdateActions`에 둔다. 실제 이동은 `IPhysics` 또는 `INavigationAgent`가 담당한다.
5. State asset에서 Scene 오브젝트를 지정하려면 그 오브젝트에 `CombatSceneAnchor`를 붙이고 고유 ID를 준 뒤 Position Source를 `SceneAnchor`로 설정한다.

## 위치의 의미

- `Self`: 공격자 위치
- `Player`: `GameManager.player`의 현재 위치
- `CurrentTarget`: `ITargetable.CurrentTarget`
- `World`: 고정 월드 좌표
- `SceneAnchor`: ID로 등록된 Scene Transform의 현재 위치
- `Follow`: 켜면 공격 중 매 프레임 실제 Transform을 다시 읽고, 끄면 시작/재조준 시점 위치를 캡처한다.

## 동시 공격

`slot`은 공격 그룹 이름이다. 같은 slot에서도 `cancelExistingInSlot`이 꺼져 있으면 여러 공격이 동시에 실행된다. 예를 들어 `LeftMissiles`, `RightMissiles`, `FloorAOE`를 서로 독립적으로 시작·취소·조회할 수 있다.

## 공격체

- `Area`: 정의에 지정한 Motion을 사용한다.
- `Bullet`: Linear motion과 명중 종료를 사용한다.
- `Missile`: Homing motion과 명중 종료를 사용한다.

실행 오브젝트는 공격자의 자식이 아닌 Scene root에 생성된다. 표시 프리팹은 실행 오브젝트의 자식이며, 선택적으로 `CombatAreaVisual`을 붙이면 정의된 범위 크기와 예고 진행률이 적용된다.

공격자 오브젝트가 비활성화된 뒤에도 이미 발사된 공격체는 기본적으로 계속 움직인다. 풀 반환 시 공격도 함께 정리해야 하는 엔티티는 `CombatAttackModule.cancelAttacksOnDisable`을 켠다. 공격자가 파괴되면 남은 공격은 안전하게 취소된다.

## 범위 판정

`IsPositionInComposableAttackDecision`은 Player/Self/CurrentTarget/World/SceneAnchor가 공격 범위 안인지 검사한다. 코드에서는 `ICombatAttackModule.Contains(slot, transform)`으로 필드의 특정 오브젝트를 직접 검사할 수 있다.
