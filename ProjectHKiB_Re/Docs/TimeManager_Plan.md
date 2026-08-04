# TimeManager 모듈 구현 계획

작성일: 2026-08-04 / 기준 커밋: `d8140aa2`

## 1. 목적

1. **메뉴창을 열었을 때 버프 타이머를 포함한 게임 시간 정지** (1차 목표)
2. **게임 내 경과 시간 축적** — 이후 시간 경과 기반 기능(낮/밤, 이벤트 타이머 등)의 기반 (2차 목표)
3. 배속(Game Speed) 조절 — 슬로우모션 연출·디버그용

**설계 원칙: 가능한 한 단순하게.** 자체 시간 축을 새로 만들지 않고, Unity의 `Time.timeScale`을 단일 진실 공급원으로 삼는다. 프로젝트 코드는 이미 전부 `Time.deltaTime` / `Time.fixedDeltaTime` / `Time.time`(= 스케일된 시간)을 쓰고 있으므로, `timeScale = 0`만으로 게임플레이 전체가 멈춘다. 새 모듈이 할 일은 **"누가 언제 멈췄는지"를 관리하는 것**뿐이다.

---

## 2. 현재 구조 분석

### 2.1 버프 타이머는 DOTween 위에 얹혀 있다

```
BuffableModule.Buff()
  └ BuffInfo.Cooltime (Timer)
      └ Timer.StartTimer()
          └ GameManager.instance.timerManager.StartCooltime()
              └ DOTween.Sequence().AppendInterval(duration).OnComplete(...)
```

DOTween의 기본 `UpdateType.Normal`은 **`Time.timeScale`의 영향을 받는다.**
→ `timeScale = 0`이면 `TimerManager`의 시퀀스가 자동으로 멈추고, `Timer.RemainTime`도 얼어붙는다.
**즉, 버프 정지를 위해 TimerManager를 고칠 필요가 없다.** (`TimerManager.cs:105-109`)

### 2.2 문제는 반대쪽 — UI도 같이 멈춘다

프로젝트 전체에서 `SetUpdate(true)`(= `isIndependentUpdate`) 호출이 **0건**이다.
따라서 `timeScale = 0`을 걸면 대화창 타이핑, 카드 선택 연출, 스탠딩 CG, 오디오 페이드까지 전부 멈춘다.
**이것이 "DOTween도 수정해주기"에 해당하는 실제 작업량이다.**

| 파일 | 트윈 용도 | 정지되어야 하나? |
|---|---|---|
| `Managers/TimerManager.cs` | 버프/쿨타임 | **O** (그대로 두면 됨) |
| `Animation/SimpleAnimationPlayer.cs` | 엔티티 스프라이트 애니메이션 | **O** (그대로) |
| `Attack/AttackAreaIndicatorManager.cs` | 공격 예고 범위 | **O** (그대로) |
| `Event/Triggers/EventTrigger.cs` | 이벤트 트리거 연출 | **O** (그대로) |
| `Dialogue/DialogueModule.cs` | 대사 타이핑(`DOText`), 다음 화살표 | X → `SetUpdate(true)` |
| `UI/CardSelectorParent.cs` | 카드 이동 연출 | X → `SetUpdate(true)` |
| `UI/FaceController.cs` | 초상화 눈/입 | X → `SetUpdate(true)` |
| `UI/StandingCGManager.cs` | 스탠딩 CG 이동/색/회전 | X → `SetUpdate(true)` |
| `Audio/AudioPlayer.cs` | `DOFade` 볼륨 페이드 | X → `SetUpdate(true)` |
| `Graffiti/GraffitiManager.cs` | 그래피티 입력 UI | X → `SetUpdate(true)` |

### 2.3 그 밖에 `timeScale = 0`에 걸리는 것들

| 대상 | 기본 동작 | 조치 |
|---|---|---|
| `Time.deltaTime` / `fixedDeltaTime` / `Time.time` 사용처 (`PhysicsManager`, `NavigationAgentModule`, `NavigationManager`, `BodyComponent` 등) | 자동 정지 | 그대로 (의도한 동작) |
| 코루틴 `WaitForSeconds` | 자동 정지 | 그대로 |
| `ParticleSystem` | 자동 정지 | 그대로 |
| `Animator` (UI 버튼 — `UI/ButtonTemp.cs`가 `Animator.Play`로 hover/pressed 연출) | **자동 정지 → 버튼이 안 움직임** | `updateMode = UnscaledTime` 필요 |
| `AudioSource` 재생 | 영향 없음 | 그대로 |

### 2.4 메뉴를 여는 경로가 두 갈래다

- **`UIManager`**: `OpenWindow()` / `CloseWindow()` → `openedWindows` 스택 관리. 일시정지 메뉴, 대화창 등. (`UI/UIManager.cs`)
- **RouteFinding 패널 3종**: `MapViewer` / `NotePanel` / `CodexPanel`. `UIManager`를 거치지 않고 `InputManager.onOpenMap/Note/Codex` 구독 → 각자 `Toggle()`로 `_panelGO` 활성 토글. (`Scripts/RouteFinding/`)

두 경로 모두 훅이 필요하다.

### 2.5 "Tick Speed"

`ProjectSettings/TimeManager.asset`의 `Fixed Timestep: 0.016` (62.5Hz), `m_TimeScale: 1`.
`GameManager.Awake()`에서 `Application.targetFrameRate = 500`.

배속 변경 시 물리 틱이 실시간 기준으로 일정하게 유지되도록 `Time.fixedDeltaTime`을 `GameSpeed`에 비례해 스케일한다.
**단, 일시정지(`timeScale = 0`) 시에는 `fixedDeltaTime`을 절대 0으로 만들지 않는다** — `FixedUpdate` 루프가 무한 반복에 빠진다.

---

## 3. 설계

### 3.1 핵심 아이디어 — 일시정지 "사유(reason) 집합"

`bool isPaused` 하나로 관리하면 "메뉴 + 대화창"이 겹칠 때 한쪽이 닫히며 다른 쪽까지 재개시켜 버린다.
사유 문자열 `HashSet`을 두고 **비어 있지 않으면 정지**로 판정한다. 카운터보다 디버깅이 쉽고(누가 잡고 있는지 보임), 중복 `Pause` 호출에도 안전하다.

### 3.2 API

```csharp
public class TimeManager : MonoBehaviour
{
    // 정지 사유 상수 — 오타로 인한 "영원히 안 풀리는 정지" 방지
    public const string ReasonMenu   = "Menu";
    public const string ReasonMap    = "Map";
    public const string ReasonNote   = "Note";
    public const string ReasonCodex  = "Codex";

    public bool  IsPaused { get; }        // 사유가 하나라도 있으면 true
    public float GameSpeed { get; }       // 배속 (일시정지와 별개)
    public float GameTime  { get; }       // 일시정지를 제외한 누적 게임 내 경과 시간(초)

    public event Action<bool> OnPauseChanged;

    public void Pause(string reason);
    public void Resume(string reason);
    public void ResumeAll();              // 씬 전환/세이브 로드 시 안전장치
    public void SetGameSpeed(float speed);
    public bool IsPausedBy(string reason);
}
```

### 3.3 내부 동작

```csharp
private void Apply()
{
    Time.timeScale = IsPaused ? 0f : GameSpeed;
    // 정지 중에는 fixedDeltaTime을 건드리지 않는다 (0이 되면 FixedUpdate 무한 루프)
    if (!IsPaused) Time.fixedDeltaTime = _defaultFixedDeltaTime * GameSpeed;
}

private void Update() => GameTime += Time.deltaTime;  // timeScale=0이면 0이 더해짐 → 자동 제외
```

- `_defaultFixedDeltaTime`은 `Awake()`에서 `Time.fixedDeltaTime`을 읽어 저장 (ProjectSettings 값 0.016을 코드에 중복 기입하지 않음).
- `OnDestroy()` / `OnApplicationQuit()`에서 `timeScale`·`fixedDeltaTime` 원복 — **에디터에서 정지 상태로 플레이를 끄면 `timeScale = 0`이 그대로 남아 다음 플레이가 멈춰 보이는 함정**을 막는다.

### 3.4 접근 경로

다른 매니저들과 완전히 동일한 방식을 쓴다 — `GameManager`에 `public TimeManager timeManager;` 필드를 두고,
씬의 매니저 오브젝트에 컴포넌트를 붙인 뒤 인스펙터로 연결한다. 접근은 `GameManager.instance.timeManager`.

자동 부착(`AddComponent`) 같은 예외 처리는 두지 않는다. 관례에서 벗어나면 나중에 "얘만 왜 다르지"가 된다.
호출부(`UIManager`, RouteFinding 패널 3종)는 `timeManager == null`이면 조용히 넘어가므로,
연결 전에도 기존 동작이 깨지지는 않는다(일시정지가 안 걸릴 뿐).

### 3.5 `Window.pausesGame` 플래그

모든 창이 게임을 멈춰야 하는 건 아니다(HUD성 팝업, 토스트 등). `Window`에 `public bool pausesGame = true;`를 추가하고, `UIManager`가 이 플래그를 보고 `Pause`/`Resume`을 건다. 기존 프리팹들은 bool 기본값 `false`로 역직렬화되므로 **각 Window 프리팹에서 체크가 필요하다** (§5 참고).

---

## 4. 구현 순서

### Phase 1 — 코어
1. `Assets/Scripts/Managers/TimeManager.cs` 신규 작성 (§3.2, §3.3)
2. `Assets/Scripts/Managers/GameManager.cs` — `timeManager` 필드 추가 (다른 매니저 필드와 동일)

### Phase 2 — 시간축 분리 (DOTween / Animator)
3. `Dialogue/DialogueModule.cs` — `DOText`, 화살표 트윈에 `SetUpdate(true)`
4. `UI/CardSelectorParent.cs` — 시퀀스 3곳에 `SetUpdate(true)`
5. `UI/FaceController.cs` — `mouthTween`/`sayingTween`/`eyeTween`에 `SetUpdate(true)`
6. `UI/StandingCGManager.cs` — 이동/색/회전/머리 트윈에 `SetUpdate(true)`
7. `Audio/AudioPlayer.cs` — `DOFade`에 `SetUpdate(true)`
8. `Graffiti/GraffitiManager.cs` — 시퀀스에 `SetUpdate(true)`
9. `Managers/TimerManager.cs` — 코드 변경 없음. **"이 시퀀스는 의도적으로 스케일 시간을 쓴다(= 일시정지 시 함께 멈춘다)"는 주석만 추가** — 나중에 누가 무심코 `SetUpdate(true)`를 붙이는 것을 막기 위해.

### Phase 3 — 메뉴 훅 (UIManager 경로)
10. `UI/Window.cs` — `pausesGame` 필드 추가
11. `UI/UIManager.cs` — `OpenWindow`/`CloseWindow`/`CloseAllWindows`에서 `Pause(ReasonMenu + ":" + name)` / `Resume(...)`

### Phase 4 — RouteFinding 패널을 UIManager 창으로 완전 통합

> 처음엔 각 패널이 `TimeManager`를 직접 호출하는 방식으로 넣었으나, "다른 창들과 같은 형식으로"
> 라는 요구에 따라 UIManager의 창 스택에 정식 등록하는 방식으로 교체했다(2026-08-04).

**왜 `Window`를 그냥 붙일 수 없었나**
- 호스트 GO(`RoutePanel`/`NotePanel`/`CluePanel`)는 **항상 활성**이어야 한다 — `RouteModule`,
  `MapGraph`, `NoteModule`, `RouteSpawnManager`, `DeathHandler`가 같이 붙어 있어서
  `Window.Open()`의 `gameObject.SetActive()`로 껐다 켜면 모듈이 죽는다.
- 실제로 토글되는 내부 패널(`_panelGO`)은 **런타임 생성**(private, 비직렬화)이라 인스펙터로 지정 불가.

**해결 — `Window` 자체의 확장 지점 (서브클래스 없음)**

처음엔 `Window`를 상속한 `RouteFindingWindow` 어댑터를 만들었으나, "특수 컴포넌트 말고 그냥
`Window`를 붙일 수 있게" 라는 요구에 따라 `Window` 쪽에 훅을 두는 방식으로 바꿨다(2026-08-04).
같은 GameObject에 `IWindowContent` 구현체가 있으면 `Window`가 여닫기를 그쪽에 위임하고,
없으면 (기존 창 전부) 지금까지처럼 자기 GameObject를 껐다 켠다. 상속도 서브클래스도 필요 없다.

12. `UI/Window.cs` — `IWindowContent` 인터페이스 추가(`CanOpenWindow` / `OpenWindowContent()` /
    `CloseWindowContent()`). `Window`가 `GetComponent<IWindowContent>()`로 찾아 위임.
    `CanOpen` 프로퍼티 추가. `Open()`/`Close()`는 `virtual`이 아니어도 된다.
13. `UI/UIManager.cs` — `OpenWindow`에서 `CanOpen` 체크, `IsWindowOpen()`/`ToggleWindow()` 추가
14. 패널 3종 — `IWindowContent` 구현. `OpenWindowContent()`/`CloseWindowContent()`가 실제 작업,
    `Open()`/`Close()`/`Toggle()`은 UIManager 경유 진입점으로 전환(툴바 `GoToNote`/`GoToCodex`,
    M/V/N 단축키가 전부 이 경로를 탄다). 각 패널에 `WindowName` 상수(`"Map"`/`"Note"`/`"Codex"`).
16. `RouteFinding/UI/ExclusivePanelGroup.cs` **삭제** — UIManager가 비팝업 창을 열 때 기존 창을
    닫으므로 상호 배타가 자동으로 보장된다.
17. `TimeManager`의 `ReasonMap`/`ReasonNote`/`ReasonCodex` 상수 제거 — 이제 UIManager가
    `"Menu:Map"` 식으로 창 이름 기반 사유를 만든다.

> **스코프 고지**: 이 작업은 `Managers` / `UI` / `Dialogue` / `Audio` / `Graffiti` / `RouteFinding` 폴더에 걸쳐 있다. 타임 매니저 기능상 불가피하므로 한 번 고지하고 진행한다.

### Phase 5 — 검증
13. 번들 Roslyn으로 컴파일 검증 (Unity 실행 없이). 기존 에러 `PhysicsManager` CS0172 1건은 무시.
14. 사용자 인게임 확인 (§5)

---

## 5. Unity 에디터에서 사람이 해야 할 일

코드만으로는 끝나지 않는 부분:

0. **`System.unity`의 매니저 오브젝트에 `TimeManager` 컴포넌트 추가 + `GameManager.timeManager`에 연결**
   (`TimerManager` 등 다른 매니저와 동일한 절차. 이걸 안 하면 일시정지가 걸리지 않는다) → **완료**
0-1. **`RoutFinding_Panel` 프리팹의 세 GO에 그냥 `Window` 컴포넌트 추가**
   (`RoutePanel`, `NotePanel`, `CluePanel` — 패널 스크립트와 **같은 GO**여야 `GetComponent`가 찾는다)
   - `Pauses Game` **체크**, `Is Popup` **해제**
0-2. **`UIManager.windows` 리스트에 3줄 추가** — 이름은 반드시 아래와 정확히 일치해야 한다
   (패널 코드의 `WindowName` 상수와 대조):
   | name | window에 넣을 것 |
   |---|---|
   | `Map` | `RoutePanel`의 Window |
   | `Note` | `NotePanel`의 Window |
   | `Codex` | `CluePanel`의 Window |
1. **각 Window 프리팹의 `pausesGame` 체크** — 일시정지 메뉴 등 게임을 멈춰야 하는 창에 체크. (기존 프리팹은 미체크 상태로 로드됨)
2. **UI `Animator`의 Update Mode를 `Unscaled Time`으로 변경** — `ButtonTemp`가 붙은 버튼 Animator들. 안 하면 정지 중 버튼 hover/press 연출이 멈춘다.
3. **인게임 확인 항목**
   - 메뉴를 열면 버프 잔여 시간이 멈추고, 닫으면 이어서 흐르는가
   - 메뉴 UI 애니메이션/대화창 타이핑은 멈추지 않는가
   - 메뉴 → 지도를 겹쳐 열었다가 하나만 닫아도 게임이 재개되지 않는가
   - 에디터 플레이를 정지 상태에서 종료 후 다시 플레이해도 정상인가

---

## 5-1. 구현 현황 (2026-08-04)

Phase 1~5 코드 작업 **완료**. 번들 Roslyn 전체 컴파일 통과 — 남은 에러는 기존 1건
(`PhysicsManager.cs:887` CS0172, Unity 본체에서는 정상 빌드됨)뿐.

계획 대비 추가로 처리한 것:
- `UI/FaceController.cs`의 `BlinkLoop()` 코루틴 — `WaitForSeconds` → `WaitForSecondsRealtime`.
  (스케일 시간이라 일시정지 중 눈 깜빡임이 멈춤)
- `UIManager.OnDestroy()` — 창을 열어둔 채 씬이 바뀔 때 정지 사유가 남는 것을 방지.

남은 것은 §5의 에디터 작업(사람이 해야 하는 부분).

## 6. 이후 확장 여지 (이번엔 구현 안 함)

- `GameTime` 기반 인게임 시계 / 낮·밤 주기
- `GameTime`을 세이브에 포함 (`SaveModule`의 provider 합성 구조에 얹기)
- `SetGameSpeed()`를 이용한 피격 히트스톱 연출
