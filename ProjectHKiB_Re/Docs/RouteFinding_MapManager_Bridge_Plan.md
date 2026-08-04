# 지도·노트·단서 시스템 ↔ MapManager 연결 계획

작성일: 2026-08-04 / 기준 브랜치: `TimeManager_0804`

## 1. 현재 상태

두 시스템이 **완전히 분리**돼 있다. RouteFinding 폴더 전체에서 `MapManager` / `MapDataSO` 참조가 **0건**이다.

| | RouteFinding | MapManager |
|---|---|---|
| 맵 식별자 | `MapNodeData.guid` (`"map-001"`) | `MapDataSO.mapAddressableID` (`"TestMap1"`) |
| 데이터 원본 | `Resources/map_database.json` | `MapDataSO` 에셋 |
| 역할 | 추상 그래프 — 경로 탐색·난이도·단서 | 실제 씬 로드 |
| 세이브 필드 | `currentLocationGuid` | `currentMapSceneName` |

### 이미 준비된 연결 고리

`MapNodeData.sceneName` — 주석에 `"씬별 로드 시 사용할 씬 이름"`이라 적혀 있고,
`map_database.json`의 13개 노드에 전부 값이 채워져 있다(`"Home"`, `"ForestEntrance"`, `"Lakeside"` …).

**그런데 이 필드를 읽는 런타임 코드가 없다.** 에디터 창에서 입력만 받고 소비되지 않는다.

### 값이 서로 다른 세계다

| | 값 |
|---|---|
| `MapNodeData.sceneName` | `Home`, `ForestEntrance`, `ForestCabin`, `Lakeside`, `Destination1` … (13개) |
| 실제 존재하는 맵 | `TestMap1`, `TestMap2` (`MapDataSO` 2개) |

RouteFinding 쪽 씬 이름은 **기획용 플레이스홀더**이고, 대응하는 씬·`MapDataSO`가 아직 없다.
이 간극을 어떻게 메울지가 이 작업의 첫 결정이다.

## 2. 설계 방향

### 2-1. 다리는 RouteFinding 폴더 **밖**에 둔다

RouteFinding은 지금까지 외부 시스템을 직접 참조하지 않는 구조를 유지해 왔다
(`MapViewer`가 노트의 존재를 모르게 하고 씬에서 찾는 방식, `RouteProgressState`가 `SaveSlotData`를
직접 참조하지 않고 `IEventSaveProvider`로 우회하는 방식).

따라서 연결 코드는 `Assets/Scripts/Map/RouteFindingMapBridge.cs`에 두고,
**이 클래스만 양쪽을 안다.** RouteFinding과 MapManager는 서로를 계속 모른다.

### 2-2. 식별자 — `sceneName`에 맵 Addressable ID를 그대로 쓴다

```
MapNodeData.sceneName  ==  MapDataSO.mapAddressableID
```

- `sceneName` 필드의 **원래 의도가 정확히 그것**이라 새로운 결합이 아니다
- `MapDataRegistrySO.Find(id)`가 이미 있으므로 추가 자산이 필요 없다
- 별도 매핑 테이블을 두는 방식보다 유지보수 지점이 하나 적다

> 대안(검토 후 채택 안 함): ① 노드 guid ↔ mapAddressableID 매핑 SO를 따로 둠 — 자산이 하나 늘고
> 두 곳을 같이 고쳐야 함. ② `MapDataSO`에 노드 guid 필드를 추가 — 실제 맵이 그래프 노드를 알아야
> 해서 의존 방향이 거꾸로다.

### 2-3. 양방향 연결

```
[정방향] 노드 도달 → 실제 맵 로드
  RouteModule.OnNodeArrived(node)
      → MapDataRegistry.Find(node.sceneName)
      → MapManager.LoadMap(mapData)

[역방향] 맵 로드 완료 → 그래프 위치 동기화
  MapManager.OnMapLoaded(mapData)
      → MapGraph에서 sceneName == mapAddressableID 인 노드 탐색
      → RouteModule.ImportCurrentLocation(node.guid)
      → Progress.MarkNodeVisited(node)
```

**역방향이 필요한 이유**: 단서 공개와 이벤트 플래그가 `RouteModule.CurrentLocation` 기준으로
동작한다(`SetRouteEventFlagAction`은 mapGuid를 비워두면 `CurrentLocation`을 자동 사용). 실제 로드된
맵과 `CurrentLocation`이 어긋나면 **단서가 엉뚱한 맵에 기록된다.**

**루프 방지**: 정방향으로 이미 `_currentLocation`이 갱신된 뒤 역방향이 같은 값을 다시 쓰는 것은
무해하지만, 값이 같으면 조기 반환하도록 가드를 둔다.

## 3. 구현 순서

### Phase 0 — 콘텐츠 정합 (사람 작업, 선행 필수)

두 방법 중 하나를 골라야 한다.

- **(A) 프로토타입 범위로 축소** — `map_database.json`의 노드 몇 개(집·목적지 등)만
  `sceneName`을 `TestMap1` / `TestMap2`로 바꿔 실제 맵과 연결한다.
  → 지금 바로 동작 확인 가능. **권장 / 채택됨**

  > 계획 초안에는 "나머지는 빈 값으로 둔다"고 적었으나, 구현 시 **나머지 11개의 기존
  > `sceneName`(`ForestCabin`, `Lakeside` 등)을 그대로 두기로 바꿨다.** 콘텐츠 설계 정보라
  > 지우면 손실이고, 다리가 "대응 맵 없음 → 씬 전환만 생략"으로 처리하므로 남겨둬도 무해하다.
  > 대신 검증 도구가 매칭/미매칭을 나눠 보여준다.
- **(B) 맵 에셋 전부 생성** — 13개 노드에 대응하는 씬 + `MapDataSO`를 만든다.
  → 콘텐츠 작업량이 커서 지금 단계에는 과함

`sceneName`이 비어 있는 노드는 **씬 전환 없이 그래프 이동만** 하도록 처리한다(§4-1).

### Phase 1 — 다리 컴포넌트

1. `Map/RouteFindingMapBridge.cs` 신규
   - `MapDataRegistrySO` 참조 + `MapManager` 참조
   - `RouteModule.OnNodeArrived` 구독 → 정방향
   - `MapManager.OnMapLoaded` 구독 → 역방향
   - `sceneName`이 비었거나 Registry에서 못 찾으면 **경고 후 씬 전환만 생략**(그래프 이동은 유지)

### Phase 2 — 조회 보강

2. `MapDataRegistrySO`에 역방향 조회가 필요한지 확인
   - 역방향은 `MapGraph`에서 노드를 찾는 것이라 Registry가 아니라 `MapGraph` 쪽 헬퍼가 맞다
   - `MapGraph.FindNodeBySceneName(id)` 추가 (RouteFinding 폴더 안이지만 외부 타입을 참조하지
     않는 순수 조회라 §2-1 원칙에 어긋나지 않는다)

### Phase 3 — 세이브 정합

3. `currentLocationGuid`(그래프)와 `currentMapSceneName`(실제 맵)을 **둘 다 유지**한다.
   - 이동 중에는 둘이 정상적으로 어긋날 수 있어(그래프상 노드 도달 ≠ 씬 로드 완료) 한쪽에서
     다른 쪽을 유도하면 정보가 손실된다
   - 대신 로드 직후 **불일치 경고**를 남겨 데이터 문제를 조기에 드러낸다

### Phase 4 — 검증 도구 확장

4. `MapDataRegistrySOEditor`의 검증에 항목 추가
   - `map_database.json`의 각 노드 `sceneName`이 실제 `MapDataSO`와 매칭되는지
   - 매칭 안 되는 노드 목록 보고 (Phase 0에서 의도적으로 비워둔 것과 오타를 구분할 수 있게)

## 4. 결정이 필요한 사항

### 4-1. `sceneName`이 빈 노드의 동작

경로 탐색은 그래프 전체를 쓰는데 실제 맵은 일부만 존재한다. 도달했지만 씬이 없는 노드에서:

- **(가) 씬 전환 생략, 그래프 이동만** — 프로토타입 진행에 지장 없음. **권장**
- (나) 이동 자체를 막음 — 경로 탐색 결과가 비어버려 지금 단계에선 테스트가 어려움

### 4-2. 전투와의 순서

`OnNodeArrived`는 이미 "전투 통과 후" 시점이다. 그런데 실제 전투는 로드된 씬 안에서 벌어져야
하므로, 정상 흐름은 **맵 로드 → 전투 → 도달** 순이어야 한다. 지금은 전투 연동 자체가 미완이라
(`WaveCombatBridge` TODO) 당장은 `OnNodeArrived` 훅으로 충분하지만, 전투를 붙일 때
**로드 훅을 `OnNodeArrived` 이전 시점으로 옮겨야 한다.**

이번 작업 범위는 `OnNodeArrived` 기준으로 잡고, 이 제약을 코드 주석에 남긴다.

## 4-3. 구현 및 검증 현황 (2026-08-04)

구현 완료, 인게임 검증 통과.

- 검증 도구: 13개 노드 중 2개(집→`TestMap1`, 숲 입구→`TestMap2`) 연결, 나머지 11개는 미제작으로 정상 분류
- 정방향: `[RouteModule] 도달 → 숲 입구` → `unloaded: Mapdata1` → `loaded: Mapdata2` 확인
- 역방향: 정방향으로 `CurrentLocation`이 이미 갱신된 경우라 조용히 빠짐(의도한 동작)
- 덤으로 M키 → `Window.Open()` → `MapViewer.OpenWindowContent()` 스택이 찍혀 UIManager 창 통합도 함께 확인됨

### 남은 콘텐츠 작업

`TestMap2` 씬에 `fromScene = TestMap1`인 `MapStartPos`가 없어 맵 전환 후 플레이어가 이전 좌표에
남는다(`MapStartPosPlacer`가 경고로 알림). 코드 문제가 아니라 씬 설정 공백이다.

## 5. 범위 밖

- 실제 전투 연동 (`WaveCombatBridge` TODO)
- 맵별 씬·`MapDataSO` 콘텐츠 제작
- `MapStartPos` 배치 규칙 — 이미 `MapStartPosPlacer`로 처리됨
