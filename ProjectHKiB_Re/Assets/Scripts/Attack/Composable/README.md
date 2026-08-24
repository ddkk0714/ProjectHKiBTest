# Composable Attack

기존 `AttackableModule`/`Damager`의 단일 공격 상태를 확장하지 않고, StateMachine이 독립 공격 인스턴스를 시작하고 조회하는 새 시스템이다. 공격 범위와 피해·연출 정보의 단일 원천은 기존 `DamageDataSO`다.

## 설정

1. 공격할 `StateController` 오브젝트에 `CombatAttackModule`을 추가한다.
2. `Create > Scriptable Objects > Attack > Composable Attack`에서 공격 정의를 만들고 기존 `DamageDataSO`를 연결한다.
3. State의 `EnterActions` 또는 `actionSequence`에 `StartComposableAttackAction`을 넣는다.
4. 공격자 이동은 공격 Action과 분리된 `MoveToPositionAction`을 사용한다. 이 Action은 `Physics and Movement/StateMachine Integration`에서 관리한다.
5. State asset에서 Scene 오브젝트를 지정하려면 그 오브젝트에 `PositionSceneAnchor`를 붙이고 고유 ID를 준 뒤 Position Source를 `SceneAnchor`로 설정한다.

## 위치의 의미

- `Self`: 공격자 위치
- `Player`: `GameManager.player`의 현재 위치
- `CurrentTarget`: `ITargetable.CurrentTarget`
- `World`: 고정 월드 좌표
- `SceneAnchor`: ID로 등록된 Scene Transform의 현재 위치
- `Follow`: 켜면 공격 중 매 프레임 실제 Transform을 다시 읽고, 끄면 시작/재조준 시점 위치를 캡처한다.

위치 설정은 이동 시스템의 `PositionReference`를 공유한다. Source에 따라 필요한 필드만 표시하며, `World`에서는 `worldPosition`, `SceneAnchor`에서는 `sceneAnchorId`가 나타난다. World에서 의미가 없는 `Follow`와 `Offset Space`는 숨긴다.

`Snap Final Position To Grid`를 켜면 Source와 offset을 모두 계산한 최종 XY 좌표를 `Grid Cell Size`와 `Grid Origin`으로 정의한 가장 가까운 그리드 교점에 맞춘다. Z 높이는 그대로 유지된다.

## 동시 공격

`slot`은 공격 그룹 이름이다. 같은 slot에서도 `cancelExistingInSlot`이 꺼져 있으면 여러 공격이 동시에 실행된다. 예를 들어 `LeftMissiles`, `RightMissiles`, `FloorAOE`를 서로 독립적으로 시작·취소·조회할 수 있다.

## 공격자 이동

공격 시스템은 공격자의 이동을 직접 제어하지 않는다. StateMachine에서 별도의 `MoveToPositionAction`을 조합하며, 세부 사용법은 `Physics and Movement/StateMachine Integration/README.md`에서 관리한다.

## DamageData와 4방향

`CombatAttackDefinitionSO`는 별도 범위를 가지지 않는다. 다음 값은 모두 연결된 `DamageDataSO`에서 읽는다.

- `downwardDamageArea.offset/size`: 아래 방향 기준 공격 Box. 선택된 방향으로 회전해 판정한다.
- `downwardDamageArea.pivot`: 선택된 방향으로 함께 회전하며 넉백 방향을 계산하는 월드 원점이 된다.
- `damageLayer`, `damageCoefficient`, `knockBack`: 기존 피해 판정에 그대로 전달한다.
- `initialSound`, `hitSound`, `camShake`: 공격 시작, 명중, 카메라 연출에 사용한다.
- `DLRUDamageEffects`, `attatchParticleToBody`: 선택 방향의 파티클을 독립 공격체에 재생한다.
- `effectAnimationClipName`, `animPlayerNumber`: 독립 공격체의 이펙트 플레이어를 선택하고 재생한다.

`StartComposableAttackAction.directionSource`에서 방향을 선택한다.

- `OwnerAnimationDirection`: `IDirAnimatable.AnimationDirection`을 공격 시작 시 캡처한다. 기존 4방향 공격의 일반적인 설정이다.
- `TowardDestination`: 시작/재조준 시 목표 쪽의 가장 가까운 4방향을 선택한다.
- `MovementDirection`: 총알·미사일의 진행 방향에 따라 4방향을 갱신한다.
- `Down/Left/Right/Up`: 고정 방향이다. 방향과 무관한 공격은 `Down`을 사용하면 원본 데이터를 그대로 쓴다.

## 공격체

- `Area`: 정의에 지정한 Motion을 사용한다.
- `Bullet`: Linear motion과 명중 종료를 사용한다.
- `Missile`: Homing motion과 명중 종료를 사용한다.

공격 정의 Inspector도 실제 Kind와 Motion에서 사용하는 이동 설정만 표시한다. Bullet은 Linear, Missile은 Homing으로 고정되므로 별도 Motion 선택을 숨기고, Stationary/FollowOrigin에서는 속도·가속처럼 사용되지 않는 필드를 표시하지 않는다.

실행 오브젝트는 기본적으로 공격자의 자식이 아닌 Scene root에 생성된다. `CombatAttackDefinitionSO.parentInstanceToOwner`를 켜면 해당 공격 인스턴스만 공격자 `StateController.transform`의 자식으로 생성되어 부모의 이동·회전·활성 상태를 따른다. 표시 프리팹은 실행 오브젝트의 자식이며, 선택적으로 `CombatAreaVisual`을 붙이면 `DamageDataSO.downwardDamageArea`의 방향·offset·size와 예고 진행률이 적용된다.

기존 `Damager`처럼 이펙트 애니메이션을 재생하려면 active prefab에 `CombatAttackEffectPlayer`를 붙이고 `SimpleAnimationPlayer`/`SpriteLibrary` 배열을 연결한다. 실행 시 공격자의 `EffectAnimationData`와 `EffectSpriteLibrary`가 주입되고, DamageData의 `animPlayerNumber`, `effectAnimationClipName`과 선택된 4방향이 적용된다. 효과만 먼저 멈출 때는 `StopComposableAttackEffectAction`을 사용한다.

공격자 오브젝트가 비활성화된 뒤에도 이미 발사된 공격체는 기본적으로 계속 움직인다. 풀 반환 시 공격도 함께 정리해야 하는 엔티티는 `CombatAttackModule.cancelAttacksOnDisable`을 켠다. 공격자가 파괴되면 남은 공격은 안전하게 취소된다.

## 범위 판정

`IsPositionInComposableAttackDecision`은 Player/Self/CurrentTarget/World/SceneAnchor가 공격 범위 안인지 검사한다. 코드에서는 `ICombatAttackModule.Contains(slot, transform)`으로 필드의 특정 오브젝트를 직접 검사할 수 있다.
