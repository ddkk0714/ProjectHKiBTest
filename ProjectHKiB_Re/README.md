# ProjectHKiB_Re 개발 가이드

이 문서는 프로젝트 전반의 구조와 개발 규칙을 빠르게 파악하기 위한 기준 문서다.  
새 기능을 작성하거나 다른 채팅에서 작업을 이어갈 때는 먼저 이 문서를 읽고, 실제 구현과 차이가 의심되는 부분만 다시 확인한다.

## 1. 기본 환경

- Unity 버전: `2021.3.45f2`
- C# 언어 버전: `9.0`
- 일반 게임 스크립트는 별도 asmdef 없이 `Assembly-CSharp`에 포함된다.
- 주요 코드 위치: `Assets/Scripts`
- 프로젝트는 Unity Input System, DOTween, NaughtyAttributes, SerializeReference SubclassSelector, GraphProcessor를 사용한다.
- 전역 접근이 필요한 기존 매니저는 대부분 `GameManager.instance`를 통해 접근한다.

주요 스크립트 폴더:

| 폴더 | 역할 |
|---|---|
| `Interface and Module Support` | 인터페이스 등록 및 모듈 조립 기반 |
| `Entity` | Player, Enemy, Friendly 등 상태 컨트롤러 |
| `StateMachine` | ScriptableObject 기반 상태 머신 |
| `Physics and Movement` | 커스텀 물리, Z 충돌, 이동, 내비게이션 |
| `Managers` | 전역 및 시스템 매니저 |
| `Attack`, `Buff`, `Animation` 등 | 각 기능의 인터페이스와 모듈 |

## 2. 프로젝트의 핵심 구성 방식

엔티티 기능은 일반적으로 다음 네 계층으로 나뉜다.

1. `I{Name}Base`
   - 데이터에 필요한 프로퍼티만 정의한다.
   - `EnemyDataSO` 같은 데이터 ScriptableObject와 런타임 모듈이 함께 구현한다.

2. `I{Name}`
   - `I{Name}Base`, `IInitializable`을 상속한다.
   - 런타임 상태와 동작 메서드를 정의한다.

3. `{Name}Module`
   - `InterfaceModule`을 상속하고 런타임 인터페이스를 구현하는 MonoBehaviour다.
   - 엔티티와 같은 GameObject에 부착한다.

4. Entity Data ScriptableObject
   - `I{Name}Base`를 구현하고 기본 스탯과 참조 데이터를 직렬화한다.
   - `DatabaseManagerSO`가 Data의 값을 런타임 모듈에 복사한다.

예시:

```csharp
public interface IExampleBase
{
    float BaseValue { get; set; }
}

public interface IExample : IExampleBase, IInitializable
{
    void Execute();
}

public class ExampleModule : InterfaceModule, IExample
{
    [field: SerializeField] public float BaseValue { get; set; }

    public override void Register(IInterfaceRegistable owner)
    {
        owner.RegisterInterface<IExample>(this);
    }

    public void Initialize()
    {
        // 런타임 상태 초기화
    }

    public void Execute() { }
}
```

## 3. 인터페이스 및 모듈 사용 규칙

### 등록

`StateController`/`Entity`는 같은 GameObject의 `InterfaceModule`을 수집해 인터페이스 딕셔너리에 등록한다.

```csharp
RegisterModules(transform);
```

중요한 제약:

- `RegisterModules`는 `transform.GetComponents<InterfaceModule>()`을 사용한다.
- 자식 GameObject의 모듈은 자동 등록되지 않는다.
- 하나의 인터페이스 타입에는 하나의 구현만 등록할 수 있다.
- 같은 인터페이스를 두 번 등록하면 나중 구현이 기존 구현을 덮어쓴다.
- 모듈을 새로 추가하면 `Register()`에서 반드시 런타임 인터페이스 타입으로 등록한다.

### 접근

엔티티 기능끼리 통신할 때는 직접 `GetComponent`로 구체 클래스에 결합하기보다 인터페이스를 사용한다.

```csharp
if (stateController.TryGetInterface(out IPhysics physics))
{
    physics.IsWalking = true;
}
```

규칙:

- 선택적 기능은 `TryGetInterface`를 사용한다.
- 반드시 존재해야 하는 기능도 null 가능성을 고려하고 명확한 오류를 남긴다.
- 상태 머신 액션과 결정에서는 구체 Module보다 인터페이스에 의존한다.
- `GetInterface<T>()`는 등록된 인터페이스만 반환한다. 단순히 Component가 붙어 있다고 반환되는 것이 아니다.

### 초기화 순서

Enemy 계열의 표준 초기화 순서는 다음과 같다.

```text
RegisterModules
→ DatabaseManagerSO로 Data 값을 Module에 복사
→ StateMachine 초기화 및 초기 상태 Enter
→ InitializeModules
```

현재 `Enemy.Initialize()`가 이 순서를 사용한다.

주의:

- 초기 상태의 Enter Action은 `InitializeModules()`보다 먼저 실행될 수 있다.
- Enter Action에서 초기화 전 런타임 필드에 접근하는 코드는 피한다.
- 모듈 API가 초기 상태에서 호출될 수 있다면 지연 초기화 또는 안전한 기본값을 제공한다.
- 풀링되는 엔티티는 `InitializeFromPool`과 `OnDisable`에서 이벤트 구독, 타이머, 임시 상태를 정리해야 한다.

### 데이터 주입

공용 스탯은 Prefab의 Module 값보다 Entity Data ScriptableObject를 기준으로 관리한다.

- Data SO에 `I{Name}Base` 구현 추가
- `[field: SerializeField]`로 인터페이스 프로퍼티 직렬화
- `DatabaseManagerSO`에 `SetI{Name}` 복사 메서드 추가
- Entity의 `Initialize()`에서 데이터 복사 호출
- 복사 이후 `InitializeModules()` 호출

배열이나 변경 가능한 컬렉션은 런타임에서 수정될 수 있다면 복사본을 만들어야 한다. `DatabaseManagerSO.SetIAttackable`의 배열 복사 방식을 참고한다.

## 4. 상태 머신 개발 규칙

핵심 파일:

- `Assets/Scripts/StateMachine/StateController.cs`
- `Assets/Scripts/StateMachine/StateMachineSO.cs`
- `Assets/Scripts/StateMachine/StateSO.cs`
- `Assets/Scripts/StateMachine/StateTransition.cs`
- `Assets/Scripts/StateMachine/StateAction.cs`
- `Assets/Scripts/StateMachine/StateDecision.cs`

### 실행 흐름

```text
StateController.Update
→ CurrentState.UpdateState
→ UpdateActions 실행
→ CurrentState.CheckDecision
→ Transition Decision들을 순서대로 평가
→ Action 실행
→ ChangeState
→ 이전 State ExitActions
→ 새 State EnterActions
```

`StateSO`는 다음을 보유한다.

- `EnterActions`
- `UpdateActions`
- `ExitActions`
- `transitions`
- 시간 순서 액션인 `actionSequence`
- 선택적 State Timer

### Action 작성

`StateAction`은 `[System.Serializable]` 일반 클래스이며 ScriptableObject가 아니다.

```csharp
namespace StateMachine
{
    [System.Serializable]
    public class ExampleAction : StateAction
    {
        public override void Act(StateController stateController)
        {
            if (stateController.TryGetInterface(out IExample example))
                example.Execute();
        }
    }
}
```

규칙:

- 상태의 “의도”를 전달하는 짧은 작업으로 유지한다.
- 장시간 유지되는 로직, 재시도, 경로 추종 등은 Module/Manager가 책임진다.
- `UpdateAction`에서 비용이 큰 탐색이나 할당을 매 프레임 실행하지 않는다.
- 공통 액션은 기존 액션과 조합할 수 있도록 작게 만든다.
- 방향, 이동, 공격 등 기능 접근은 인터페이스를 사용한다.

### Decision 작성

`StateDecision` 역시 `[System.Serializable]` 일반 클래스다.

```csharp
namespace StateMachine
{
    [System.Serializable]
    public class ExampleDecision : StateDecision
    {
        public override bool Decide(StateController stateController)
        {
            return stateController.TryGetInterface(out IExample example);
        }
    }
}
```

Transition의 모든 Decision은 AND로 평가된다. 각 Decision에는 `negate`를 지정할 수 있다.

### 입력 전이

- `activationInput == None`인 전이는 매 Update 평가된다.
- Input 전이는 `CommandPair`가 Input System의 `started`, `performed`, `canceled` 이벤트에 바인딩한다.
- 상태나 입력 전이를 편집한 뒤에는 `StateMachineSO.UpdateStateMachine()`을 실행해 `_commandPairs`를 갱신해야 한다.
- 상태 머신 교체 시 기존 Command를 Unbind한 뒤 새 Command를 Bind한다.

### 상태 머신의 현재 제약

- Controller의 `TransitionConditions`, `TransitionSequences`, `Timers`는 각각 10개로 초기화된다.
- State별 transition 수와 timer ID는 사실상 `0~9` 범위를 전제로 한다.
- `availableTime`과 `disableTime`은 상태 진입 시점부터 각각 독립적으로 흐른다.
- 상태 이탈 시 `StopAllCoroutines()`가 호출되므로 해당 Controller에서 상태 머신 외 Coroutine을 실행할 때 주의한다.
- `additionalTransitions`는 Graph 포트 생성에는 사용되지만 현재 런타임의 `ReserveTransitions`, `CheckDecision`에서는 평가되지 않는다.
- `StateMachineSO.customVariables`는 Controller에 깊은 복사 없이 참조가 전달된다. 여러 Controller가 같은 StateMachine asset을 공유할 때 런타임 값 공유 여부를 반드시 확인한다.
- `ActionSequence`는 DOTween Sequence를 사용하며 새 Sequence 시작 시 기존 Sequence를 Kill한다.

## 5. 커스텀 물리와 이동

이 프로젝트는 Rigidbody2D 중심이 아니라 `PhysicsManager`와 `IPhysics` 중심의 커스텀 이동을 사용한다.

핵심 파일:

- `Assets/Scripts/Physics and Movement/PhysicsManager.cs`
- `Assets/Scripts/Physics and Movement/PhysicsModule.cs`
- `Assets/Scripts/Physics and Movement/ZPhysics`

### 좌표 의미

- `HPosition`, `HVelocity`: XY 평면 위치와 속도
- `ZPosition`, `ZVelocity`: 높이축 위치와 속도
- `Position`: `(HPosition.x, HPosition.y, ZPosition)`
- Unity Transform의 z가 논리적인 높이축으로 사용된다.

### FixedUpdate 처리 순서

```text
외력/중력/마찰 및 보행 가속도 계산
→ 수직 물리와 바닥/천장 계산
→ Grid/Physics 모드 결정
→ 셀 점유 재구축
→ 수평 이동과 벽 충돌
→ 엔티티 간 충돌 반복 해결
→ ExForce 초기화 및 LastSetDir 갱신
```

### 이동 모드

- `Grid`: 바닥 위의 안정적인 일반 이동
- `Physics`: 경사, 공중, 넉백, 강한 외력, 엔티티 충돌 상황
- `Static`: 내구력 이하의 충격에는 움직이지 않는 상태

일반 이동을 요청할 때는 Transform을 직접 변경하지 않고 다음 값을 사용한다.

```csharp
physics.IsWalking = true;
physics.WalkingDir = direction;
physics.IsSprinting = true; // 필요한 경우
```

순간이동은 목적에 따라 구분한다.

- `LogicalTeleport`: 커스텀 물리의 논리 위치 변경
- `RealTeleport`: 논리 위치와 표시 위치를 함께 즉시 변경

`ExForce`는 FixedUpdate 마지막에 0으로 초기화된다. 지속적인 힘은 필요한 FixedUpdate마다 다시 적용해야 한다.

### ZCollider2D

Unity Collider2D에 대응하는 Z 래퍼가 필요하다.

- `ZBoxCollider2D`
- `ZCircleCollider2D`
- `ZCapsuleCollider2D`
- `ZPolygonCollider2D`
- `ZEdgeCollider2D`
- `ZCompositeCollider2D`
- `ZTilemapCollider2D`

`ZCollider2D`는 높이, 마찰, 반발, 경사, 계단 정보를 보유한다. Awake에서 `ZPhysics2D` registry에 등록된다.

주의:

- Floor/Wall 레이어의 Collider가 Z 판정에 참여하려면 대응하는 `ZCollider2D`가 있어야 한다.
- 경사는 `useSlopeDU` 또는 `useSlopeRL`과 각 끝점 offset으로 표현한다.
- 계단은 `isStair`를 사용한다.
- `ZPhysics2D` API는 먼저 Physics2D 후보를 얻은 뒤 Z 범위가 겹치는 결과만 남긴다.
- `PhysicsModule.Initialize()` 전에 `ZCol`, `Mass`, `PhysicsManager` 참조가 유효해야 한다.
- `Mass == 0`이면 `InvM = 1 / Mass`가 잘못되므로 데이터에서 양수를 보장한다.

### 레이어 구분

- `FloorLayer`: 바닥 및 천장 표면 검색
- `WallLayer`: 수평 이동을 막는 장애물
- `CanPushLayer`: 밀 수 있는 대상
- Navigation Profile의 `agentLayer`: 근거리 군중 회피 대상

레이어가 0이면 관련 탐색이 동작하지 않을 수 있다. 새 Prefab과 Scene을 만들 때 가장 먼저 확인한다.

## 6. Entity Control 내비게이션 시스템

위치: `Assets/Scripts/Physics and Movement/Entity Control`

기존 `PathFindingManager`와 `PathFindableModule`은 평면 A* 기반의 레거시 시스템이다. 새 AI 이동에는 Entity Control 시스템을 우선 사용한다.

### 구성요소

- `NavigationManager`: 월드 그래프, 경로 요청 큐, 셀 예약
- `NavigationWorld`: Z-aware 노드 생성 및 인접 링크 판정
- `NavigationAgentModule`: 경로 추종, 이탈 복구, 군중 회피
- `NavigationAgentProfile`: 크기, 이동 능력, 재탐색, 군중 설정
- `NavigationBehaviourSO`: 이동 패턴
- StateMachine Action/Decision: 상태와 Navigation Agent 연결

### Scene 설정

1. Scene에 `NavigationManager`를 하나 배치한다.
2. `NavigationWorld`의 `bottomLeft`, `topRight`, `cellSize`, `minZ`, `maxZ`를 설정한다.
3. `floorLayer`와 `wallLayer`를 지정한다.
4. `buildOnAwake`를 사용하거나 지형 변경 후 `RebuildWorld()`를 호출한다.

월드는 각 XY 셀에서 여러 높이의 표면 노드를 만들 수 있다. 같은 XY에 다리 위/아래가 함께 존재할 수 있다.

### Agent 설정

1. Entity와 같은 GameObject에 `NavigationAgentModule`을 추가한다.
2. `NavigationAgentProfile` asset을 생성해 연결한다.
3. `NavigationManager`를 연결한다. 비어 있으면 현재는 `FindObjectOfType`으로 검색한다.
4. 군중 회피를 쓸 경우 Profile의 `agentLayer`를 지정한다.
5. Patrol은 Agent의 `patrolPoints`에 Scene Transform들을 연결한다.

Agent는 `INavigationAgent`로 등록된다.

### 지원 링크

- Walk
- Slope
- Stair
- Jump
- Drop

사용 가능 여부와 비용은 `NavigationAgentProfile`에서 제어한다.

### 지원 Behaviour

- `DirectNavigationBehaviourSO`
- `PatrolNavigationBehaviourSO`
- `WanderNavigationBehaviourSO`
- `ChaseNavigationBehaviourSO`
- `FleeNavigationBehaviourSO`
- `KeepDistanceNavigationBehaviourSO`

Behaviour asset은 상태를 직접 보관하지 않는다. 런타임별 임시 상태는 `NavigationAgentModule`에 보관한다. 공유 ScriptableObject에 엔티티별 런타임 값을 추가하지 않는다.

### 상태 머신 연계

주요 Action:

- `SetNavigationBehaviourAction`
- `NavigateToCurrentTargetAction`
- `NavigateToTransformAction`
- `StopNavigationAction`
- `ClearNavigationDestinationAction`
- `ForceNavigationRepathAction`

주요 Decision:

- `NavigationStatusDecision`
- `NavigationArrivedDecision`
- `NavigationBlockedDecision`
- `NavigationTargetDistanceDecision`

상태 머신은 “어떤 패턴을 사용할지”만 결정한다. 경로 계산, 추종, 회피, 재계획은 Agent가 담당한다.

같은 상태에서 기존 `WalkByPathFindAction` 또는 `WalkByTargetDirAction`과 새 Navigation Agent를 동시에 사용하지 않는다. 여러 시스템이 `IPhysics.WalkingDir`과 `IsWalking`을 덮어쓰게 된다.

### 재탐색 및 군중 처리

Agent는 다음 상황에서 재탐색한다.

- 목적지가 일정 거리 이상 이동
- 현재 경로에서 이탈
- 물리 충격 후 착지
- 일정 시간 이동하지 못함
- 예약된 다음 노드에 오래 진입하지 못함

군중 처리는 다음 순서로 구성된다.

```text
A* 전역 경로
→ 예약된 노드 비용 반영
→ 다음 노드 단기 예약
→ 근거리 Separation/Avoidance
→ PhysicsManager 충돌 해결
```

고정 크기 버퍼가 사용되므로 매우 밀집된 군중에서는 다음 값을 확인한다.

- Agent 이웃 Collider buffer: 32
- Navigation passage cast buffer: 16
- PhysicsManager의 주요 overlap/object/cell buffer: 32

한 엔티티의 `Size`가 매우 크거나 한 셀 주변에 32개 이상이 밀집하는 경우 버퍼 확장 또는 NonAlloc 결과 분할이 필요하다.

## 7. 전역 매니저와 시간 처리

`GameManager.instance`는 다음 매니저를 참조한다.

- Audio
- Particle
- DamageParticle
- AttackAreaIndicator
- Input
- 기존 PathFinding
- Chunk
- Camera
- Timer
- ObjectSpawn
- ObjectDeathCount
- Gear
- Inventory
- UI
- Graffiti
- Event
- Map

새 전역 시스템을 추가할 때:

- Scene 수명과 생성 순서가 명확하면 GameManager에 등록한다.
- 특정 Scene에서만 쓰는 시스템은 명시적 SerializeField 참조를 우선한다.
- `FindObjectOfType`는 초기화용 fallback으로만 사용하고 매 프레임 호출하지 않는다.

`Timer`는 `GameManager.instance.timerManager`에 의존한다. GameManager와 TimerManager가 준비되기 전에 Timer를 시작하면 안 된다.

## 8. 직렬화 및 ScriptableObject 규칙

- 인터페이스 auto-property는 `[field: SerializeField]`를 사용한다.
- StateAction과 StateDecision 같은 다형 일반 클래스는 `[SerializeReference, SubclassSelector]`로 보관한다.
- 공유 설정은 ScriptableObject를 사용한다.
- ScriptableObject에 엔티티별 mutable runtime 상태를 저장하지 않는다.
- Scene Transform 참조는 Project asset인 ScriptableObject에서 지속적으로 참조할 수 없다는 점을 고려한다.
- StateMachine asset과 Behaviour asset은 여러 엔티티가 공유할 수 있다고 가정한다.
- Asset의 배열/리스트를 런타임에서 직접 변경하지 않는다.

## 9. 이벤트 및 수명 관리

- `Awake` 또는 `Initialize`에서 이벤트를 구독했다면 `OnDestroy` 또는 `OnDisable`에서 해제한다.
- Pool 대상은 파괴되지 않고 Disable/Enable될 수 있다.
- Timer, DOTween Sequence, Coroutine, Navigation reservation은 Disable 시 정리한다.
- 재초기화가 가능한 Module의 `Initialize()`는 가능하면 idempotent하게 작성한다.
- 전역 singleton을 참조하는 `OnDestroy`는 종료 순서에서 null일 가능성을 고려한다.

## 10. 성능 규칙

- Update/FixedUpdate에서 `FindObjectOfType`, LINQ, 불필요한 배열 생성, 경로 전체 재탐색을 피한다.
- 물리 탐색은 가능하면 NonAlloc API와 재사용 buffer를 사용한다.
- 여러 Agent의 경로 요청은 Manager queue에서 프레임별로 제한한다.
- 군중 회피는 공간 분할 또는 레이어 필터를 거친 가까운 대상만 계산한다.
- 상태 머신 Decision은 매 프레임 호출될 수 있으므로 가볍게 유지한다.
- 반복 충돌 해결 횟수와 경로 처리 수는 Inspector 설정으로 조절한다.

## 11. 새 기능 구현 체크리스트

새 Entity 기능을 추가할 때:

1. 기존에 같은 역할의 인터페이스/모듈이 있는지 `rg`로 검색한다.
2. 데이터가 필요하면 `I{Name}Base`를 정의한다.
3. 런타임 API는 `I{Name} : I{Name}Base, IInitializable`에 정의한다.
4. `{Name}Module : InterfaceModule`을 작성한다.
5. `Register()`에서 인터페이스를 등록한다.
6. Data SO와 `DatabaseManagerSO`에 데이터 복사 경로를 추가한다.
7. Entity 초기화 순서를 유지한다.
8. 상태 머신에서는 인터페이스 기반 Action/Decision을 작성한다.
9. Disable/Destroy/Pooling 정리 경로를 확인한다.
10. Unity 컴파일과 최소 런타임 Scene 테스트를 수행한다.

새 이동 패턴을 추가할 때:

1. 기존 Behaviour 조합으로 가능한지 먼저 확인한다.
2. 지속 로직은 `NavigationBehaviourSO`, 상태 전환은 StateMachine에 둔다.
3. Behaviour asset에는 런타임 mutable 상태를 두지 않는다.
4. 목적지는 `SetDestination`으로 전달하고 Transform을 직접 이동하지 않는다.
5. Arrived, Blocked, Failed, Displaced 처리 방식을 정의한다.

## 12. 반복 조사 방지를 위한 빠른 검색

PowerShell 기준:

```powershell
# 파일 찾기
rg --files Assets/Scripts

# 인터페이스 구현/사용 위치
rg -n "interface IPhysics|RegisterInterface<IPhysics>|TryGetInterface.*IPhysics" Assets/Scripts -g "*.cs"

# 상태 머신 Action/Decision
rg -n "class .*Action : StateAction|class .*Decision : StateDecision" Assets/Scripts/StateMachine -g "*.cs"

# 초기화 순서
rg -n "RegisterModules|InitializeModules|DatabaseManager" Assets/Scripts/Entity -g "*.cs"

# 물리와 이동
rg -n "MovementMode|WalkingDir|ExForce|LogicalTeleport" "Assets/Scripts/Physics and Movement" -g "*.cs"
```

## 13. 검증 기준

코드 변경 후 최소 확인:

- Unity Console에 새 컴파일 오류가 없는지 확인
- `git status --short`로 예상하지 않은 파일 변경 확인
- `git diff --check`로 공백 오류 확인
- Prefab의 Module, LayerMask, ScriptableObject 참조 누락 확인
- 상태 진입/이탈 시 이벤트·Timer·Coroutine·Navigation 상태 정리 확인
- Grid, Physics, 경사, 낙하, 넉백 상황에서 이동 확인
- 여러 Agent가 좁은 통로에서 정지하거나 진동하지 않는지 확인

현재 새 Entity Control 스크립트는 Unity 2021.3 Roslyn 컴파일러로 `Assembly-CSharp`와 함께 컴파일 검증되었다. 실제 Scene의 Layer와 Collider 구성은 프로젝트 데이터이므로 기능 추가 후 반드시 Play Mode 검증을 수행한다.

## 14. 알려진 기술 부채와 주의점

- 기존 `PathFindingManager`는 Z축과 커스텀 물리를 고려하지 않는 레거시 구현이다.
- `PathFindableModule`과 새 `NavigationAgentModule`을 같은 상태에서 동시에 구동하지 않는다.
- `StateSO.additionalTransitions`는 현재 런타임 전이 평가에 포함되지 않는다.
- StateMachine custom variable은 asset 참조 공유 가능성이 있다.
- StateController의 transition/timer 저장 공간은 10개로 고정되어 있다.
- `StateSO.ExitState`의 `StopAllCoroutines()`는 Controller의 다른 Coroutine에도 영향을 준다.
- 여러 시스템의 NonAlloc buffer가 32개 안팎으로 고정되어 있다.
- `GameManager.instance` 의존 시스템은 Scene 초기화 순서에 민감하다.
- 일부 기존 한글 주석은 인코딩이 깨져 있다. 코드를 수정할 때 파일 전체 인코딩 변환을 섞지 않는다.
- 기존 코드에는 global namespace와 기능별 namespace가 혼재한다. 새 Entity Control 코어는 `EntityControl`, 상태 머신 확장은 `StateMachine` namespace를 사용한다.

이 문서의 설명과 실제 코드가 달라지면 코드를 기준으로 판단하고, 같은 변경 작업에서 이 README도 함께 갱신한다.
