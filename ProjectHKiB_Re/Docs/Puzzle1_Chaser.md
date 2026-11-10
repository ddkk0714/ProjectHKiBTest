# Puzzle1 체이서

`Assets/Scenes/puzzle test/puzzle1.unity`에서 사용하는 추적자 구성.
런타임 C#을 추가하거나 기존 이동 시스템을 수정하지 않고 기존 컴포넌트와 FSM 액션을 조합한다.

## 프리팹

- `Assets/Prefabs/Puzzle/Chaser/Puzzle1_Chaser.prefab`: 적 본체. 기존 Blonde 프리팹의 물리·애니메이션 구성을 재사용한다. 공격 패턴은 사용하지 않는다.
- `Assets/Prefabs/Puzzle/Chaser/Puzzle1_ChaserEncounter.prefab`: 본체와 Puzzle1 범위로 설정한 NavigationManager를 포함한다. Puzzle1에 이 프리팹을 한 번 배치한다.

게임의 기존 System 씬에서 PhysicsManager가 활성화되어 있어야 한다.
`NavigationManager.cs.meta`의 실행 순서는 100이다. `ZCollider2D.Awake`가 바닥과 벽을 등록한 뒤 내비게이션이 경로 노드를 만들도록 하는 설정이며, 런타임 C# 변경은 없다.
체이서 본체만 다른 맵에 배치할 때는 그 맵의 NavigationManager와 바닥/벽 레이어 및 탐색 범위를 연결해야 한다.
Encounter의 탐색 범위는 월드 좌표이므로 프리팹을 옮겨도 자동으로 이동하지 않는다.

## FSM 수정

`Puzzle1_Chaser_FSM.asset`의 **Open Graph View** 또는 `allStates`에서 상태를 편집한다.

| 상태 | 동작 | 다음 상태 |
| --- | --- | --- |
| Chaser_Acquire | 정지, Player 레이어 대상 탐색 | 발견하면 Chase |
| Chaser_Chase | 보행 속도로 지속 추적 | 4초 후 Pause |
| Chaser_Pause | 내비게이션 종료 및 정지 | 1초 후 Burst |
| Chaser_Burst | SprintCoeff를 적용하여 추적 | 2초 후 Recover |
| Chaser_Recover | 내비게이션 종료 및 정지 | 1초 후 Chase |

Chase/Burst 도중 대상을 잃으면 Acquire로 돌아간다. 탐색 반경은 300이며 벽 너머의 Player도 탐색한다.
추적 방향이 바뀌면 기존 내비게이션이 경로를 다시 요청한다.

- **정지·추적 시간**: 각 상태 `transitions`의 해당 Duration 전이 `availableTime`을 수정한다.
- **기본 속도**: `Puzzle1_Chaser_Data.asset`의 `MaxWalkSpeed` (기본 3).
- **빠른 추적 속도**: 같은 에셋의 `SprintCoeff` (기본 1.6, 최대 속도 4.8).
- **더 많은 구간**: State 에셋을 복제하고 `allStates`와 전이의 `trueState`를 연결한다. 추적 진입에는 `SetNavigationBehaviourAction`, 정지 진입/추적 종료에는 `StopNavigationAction`을 사용한다.
- **추적 갱신·접근 거리**: `Puzzle1_Chaser_Navigation.asset`의 `targetUpdateInterval` (0.2초), `stopDistance` (0.7).
- **경로 탐색**: `Puzzle1_Chaser_Profile.asset`에서 반경·경로 갱신 간격 등을 조절한다. 기본은 점프·낙하를 허용하지 않는다.

## 재사용과 범위

기존 `NavigationAgentModule → IPhysics → PhysicsManager` 경로를 사용한다. Transform을 직접 이동시키는 별도 추적 스크립트는 없다.
기존 `StateMachineSO`, `StateSO`, `DetectAndTargetDecision`, `SetNavigationBehaviourAction`, `StopNavigationAction`, `ApplySprintAction`, 애니메이션 액션을 재사용한다.
플레이어 접촉 시 피해·게임오버·맵 이동 등은 이 체이서의 범위에 포함하지 않는다.
지형을 런타임에 바꿀 경우 기존 `NavigationManager.RebuildWorld()`를 호출해야 한다.

## 검증

Unity 2021.3.45f2 배치 모드에서 실제 Puzzle1 씬의 지형, 생성된 Encounter 프리팹, 기존 PhysicsManager를 사용해 18초 동안 실행했다.
게임의 오디오·이벤트 시스템을 제외하고 Player 레이어 콜라이더를 대상으로 사용했다. 대상을 기존 통로 안에서 두 번 옮겨 재추적을 확인했다.

- 경로 노드: 6,528개
- 체이서 총 이동 거리: 13
- 관측 최대 보행 속도: 3, 가속 추적 속도: 4.8
- Chase/Pause/Burst/Recover 상태 순환 확인
- Pause/Recover에서 이동 입력과 목적지 해제 확인
- 최종 경로 상태: Arrived, 실행 오류 없음

결과 원문: `Puzzle1_Chaser_Verification.txt`. 생성·검증에만 사용한 임시 Editor 스크립트는 결과물에서 제거했다.
