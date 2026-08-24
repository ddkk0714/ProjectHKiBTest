# Position Movement StateMachine Actions

공격 실행과 무관한 위치 기반 이동 Action과 공용 `PositionReference`를 관리한다. Self, Player, CurrentTarget, World, SceneAnchor 및 offset 규칙을 이동과 공격에서 함께 사용한다.

`PositionReference.snapFinalPositionToGrid`를 켜면 Source와 offset을 계산한 최종 XY 위치를 가장 가까운 그리드 교점에 맞춘다. `gridCellSize`로 한 칸 크기, `gridOrigin`으로 기준 원점을 설정하며 Z 높이는 보존한다. Scene Transform을 ID로 참조하려면 같은 폴더의 `PositionSceneAnchor`를 사용한다.

## MoveToPositionAction

- `InterpolatedPhysicsStep`: 목적지를 `duration` 뒤에 도착하도록 PhysicsManager에 이동을 요청한다. 위치를 프레임마다 덮어쓰지 않고 실제 `HVelocity`와 Physics 모드의 벽·엔티티 충돌 파이프라인으로 이동한다.
- `InstantTeleport`: `IPhysics.RealTeleport`로 논리 위치와 표시 Body를 같은 프레임에 옮긴다. 보간하지 않는다.
- `Navigation`: `INavigationAgent.SetDestination`에 목적지를 전달한다.

State의 `[SerializeReference, SubclassSelector]` 메뉴에서는 `Movement/Move To Position`으로 표시된다. NaughtyAttributes의 `ShowIf + AllowNesting`을 사용하므로 Inspector에는 선택한 Mode에 필요한 설정만 나타난다.

- `InterpolatedPhysicsStep`: `duration`
- `InstantTeleport`: `stopHorizontalMovementBeforeTeleport`
- `Navigation`: `forceRepath`

Player, CurrentTarget, SceneAnchor는 Action을 실행할 때마다 실제 Scene 위치를 다시 읽는다. 계속 추적하려면 `UpdateActions`, 한 번만 순간이동하려면 보통 `EnterActions`에 둔다.

`stopHorizontalMovementBeforeTeleport`를 켜면 순간이동 직전에 걷기 상태와 수평 속도를 정지한다. 점프와 낙하에 해당하는 Z 속도는 기존 `IPhysics.StopMove` 규칙대로 유지한다.

`InterpolatedPhysicsStep`은 진행도를 갱신해야 하므로 `UpdateActions`에 둔다. 같은 State asset을 여러 엔티티가 사용해도 시작점, 경과 시간, 완료 여부는 엔티티별로 따로 저장된다. 기존 `speed` 직렬화 값은 `duration`으로 자동 이관된다.

이 이동 요청은 물리 갱신 직전에 목표까지 남은 거리와 시간을 이용해 속도를 계산한다. 따라서 느린 속도도 Grid의 정지 임계값이나 셀 스냅에 잘리지 않으며, 이동 중에는 Physics 모드를 유지한다. State 전환 등으로 UpdateAction 호출이 끊기면 남은 이동 요청은 자동 폐기된다.

## PhysicsMode 변경

`SetPhysicsMovementModeAction`은 `Physics/Set Movement Mode` 메뉴에서 선택한다. Grid, Physics, Static 전환 시 `Mode` 값만 직접 바꾸지 않고 PhysicsManager가 점유 셀과 Grid 상태를 함께 정리한다. `EnterActions`에 두면 한 번 전환하고 이후 자동 모드 판정을 따르며, 상태 동안 특정 모드를 계속 유지하려면 `UpdateActions`에 둔다.

## MoveAlongPositionKeyframesAction

여러 `PositionMovementKeyframe`을 배열 순서대로 실행해 꺾인 경로, 돌진 후 추적, 구간 사이 순간이동 같은 움직임을 구성한다. 이 Action도 `UpdateActions`에 둔다.

각 키프레임은 다음을 가진다.

- `mode`, `destination`
- Interpolated/Navigation 구간의 `duration`
- Navigation 전용 `forceRepath`
- InstantTeleport 전용 `stopHorizontalMovementBeforeTeleport`

`loop`를 켜면 마지막 키프레임 이후 첫 키프레임부터 다시 실행한다. `Follow`가 꺼진 목적지는 해당 키프레임이 시작될 때 캡처하고, 켜진 목적지는 구간 진행 중 Scene의 실제 Transform을 계속 읽는다.

## 임시 Physics Layer 변경

`SetPhysicsLayerOverrideAction`은 `IPhysics.WallLayer`와 `FloorLayer`에 이름 있는 임시 변경을 추가한다. 빠른 이동 중 특정 엔티티 Layer를 통과하려면 State의 `EnterActions`에서 Wall Layer의 `Remove`를 선택하고 제외할 Layer를 지정한 뒤, `ExitActions`의 `ClearPhysicsLayerOverrideAction`에서 같은 slot을 해제한다.

- `Replace`: 현재 마스크 전체를 지정 값으로 교체
- `Add`: 지정 Layer를 현재 마스크에 추가
- `Remove`: 지정 Layer를 현재 마스크에서 제외

서로 다른 slot은 적용 순서대로 합성되므로 여러 기능의 임시 변경을 함께 사용할 수 있다. 같은 slot을 다시 설정하면 기존 값을 교체한다. 모든 임시 변경을 즉시 폐기해야 할 때는 `ClearAllPhysicsLayerOverridesAction`을 사용한다. `PhysicsModule`이 비활성화될 때도 원본 마스크로 자동 복원된다.

벽, 다른 엔티티, 바닥을 포함한 모든 커스텀 물리 충돌을 잠시 끄려면 `DisableAllPhysicsCollisionsAction`을 사용하고, 복구 시점에 `RestoreAllPhysicsCollisionsAction`을 같은 slot으로 실행한다. 비충돌 slot이 하나라도 남아 있는 동안에는 다른 Layer override보다 우선하여 Wall/Floor Layer가 모두 비활성화된다. 따라서 여러 상태나 기능이 각각 비충돌 상태를 요청해도 모든 slot이 복구되기 전에는 충돌이 다시 켜지지 않는다.

동적 엔티티 충돌은 양쪽 엔티티의 Wall Layer가 서로의 Collider GameObject Layer를 허용할 때만 발생한다. 어느 한쪽에서 상대 Layer를 제거하면 Grid 이동, `MoveToward`와 일반 Physics 충돌 모두 상대를 통과한다. Floor Layer 변경은 다음 물리 갱신부터 바닥·천장 탐색에 반영된다.
