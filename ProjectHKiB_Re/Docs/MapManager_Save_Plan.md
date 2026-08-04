# 세이브 시스템 MapManager 전환 + Addressable 맵 씬 관리 계획

작성일: 2026-08-04 / 기준 커밋: `e2bafc5a`

## 1. 목적

- 세이브 시스템의 맵 전환 기준을 `MapChangeManager`(SceneManager 기반) → **`MapManager`(Addressables 기반)**로 이관
- 맵 씬을 Addressable로 관리하고, 저장된 **맵 Addressable ID**로 `MapDataSO`를 되찾는 경로 확립

## 2. 현재 구조 요약

```
MapDataSO (에셋)
  ├ mapAddressableID : string   ← [NaughtyAttributes.Scene] 씬 선택기
  ├ bgmID
  └ allEntity/AnimInitInfos     ← MapLocalManager가 씬 오브젝트에 주입

MapManager                        MapChangeManager
  Addressables.LoadSceneAsync       SceneManager.LoadSceneAsync
  MapLocalManager.Initialize()      MapStartPos 배치
  navigationManager.RebuildWorld()  OnMapChanged 발행
  완료 통지 없음                     ← 세이브가 현재 여기에 붙어 있음
```

- 세이브는 `SaveModule.cs:645-698`에서 `MapChangeManager`를 직접 참조
- `SaveData.currentMapSceneName`에 씬 이름을 문자열로 저장

## 3. 확인된 문제

### 3-1. Addressable 그룹이 비어 있음

`AssetGroups/PrototypeMaps.asset`의 `m_SerializeEntries: []` — 등록 엔트리 0개.
현 상태로는 `Addressables.LoadSceneAsync("TestMap1")`이 키를 찾지 못하고 실패한다.

이력상 **반복적으로 비워지고 있다**:

| 커밋 | 엔트리 | 주소 형식 |
|---|---|---|
| `b32d8d34` 07-06 | 2 | `Assets/Scenes/TestMap/TestMap1.unity` |
| `ca6a544b` 07-07 | 2 | **`TestMap1`** |
| `16e18d41` 07-23 | **0** | — |
| `3c34b8f0` 07-28 | 2 | `Assets/Scenes/TestMap/TestMap1.unity` |
| `d8140aa2` 07-31 | **0** | — |

### 3-2. 주소 형식 불일치

`mapAddressableID`는 `[NaughtyAttributes.Scene]`이 채우므로 **씬 이름**(`TestMap1`)이다.
그런데 Addressable 등록은 대부분 **전체 경로**로 들어간다. 둘은 Addressables에서 다른 키라 매칭되지 않는다.
→ **주소를 씬 이름으로 통일한다.** (07-07 상태가 정답)

### 3-3. MapManager에 로드 완료 통지가 없음

`LoadMap()`은 Addressables 비동기 콜백 안에서 `CurrentMapData`를 설정할 뿐 외부에 알리지 않는다.
세이브 복원은 "맵 전환 완료 후 저장 좌표로 텔레포트"가 필요하므로 완료 시점을 알아야 한다.

### 3-4. 언로드 완료를 기다리지 않음

`MapManager.cs:25-35` — `UnloadSceneAsync` 콜백만 걸어두고 즉시 `LoadSceneAsync`를 시작해 둘이 경쟁한다.
언로드 콜백이 읽는 `CurrentMapData`도 그 시점엔 이미 새 맵으로 덮여 있어 로그가 어긋난다.
**세이브 복원의 "다른 맵으로 전환" 경로가 정확히 여기를 탄다.**

## 4. 수정 방향

### 4-1. MapManager — 최소 수정 2건

1. **`OnMapLoaded` 이벤트 추가** — 로드가 완전히 끝난 시점(`MapLocalManager` 초기화 + `RebuildWorld` 이후) 발행.
   `MapChangeManager.OnMapChanged`와 같은 목적·같은 이유. 주석으로 명시.
2. **언로드 완료 후 로드하도록 순서 정정** — 로드 본체를 `LoadMapInternal()`로 분리하고,
   언로드가 필요한 경우 그 완료 콜백에서 호출. 언로드 로그용 `MapDataSO`는 미리 캡처.

> 그 외 `MapManager` 동작은 건드리지 않는다. `Oestroy()` 오타(= `OnDestroy` 미호출)는
> 세이브와 무관하므로 이번 범위에서 제외한다.

### 4-2. MapChangeManager — 수정 없음

세이브가 `MapManager`로 옮겨가면 참조만 끊긴다. 클래스 자체는 그대로 둔다.

> `MapChangeManager`만 하던 `MapStartPos` 기반 플레이어 배치는 `MapManager` 경로에 없다.
> 세이브 복원은 저장 좌표로 직접 텔레포트하므로 무관하고, 일반 맵 이동은 현재 어디서도
> 호출되지 않아(프로토타입 단계) **이번 범위 밖**으로 둔다.

### 4-3. MapDataRegistrySO 신설 — ID로 MapDataSO 찾기

```
MapDataRegistrySO
  ├ maps : List<MapDataSO>            (직렬화)
  ├ Find(mapAddressableID) → MapDataSO  런타임 동기 O(1)
  └ [에디터 버튼] CollectAll()          AssetDatabase 스캔으로 자동 수집
```

- **동기 조회**라 복원 흐름이 단순하다(비동기 로드를 한 겹 더 쌓지 않음)
- 에디터 버튼 자동 수집이라 **수동 등록 누락이 없다**
- SO는 직접 참조로 빌드에 포함되므로 **Addressable 그룹이 비는 사고와 독립적**

> `Resources` 폴더 방식은 폴더 전체가 항상 빌드에 포함돼 채택하지 않는다.
> `MapDataSO` 자체를 Addressable로 만드는 방식은 조회가 비동기가 되고 3-1 사고에 같이
> 노출되므로 채택하지 않는다.

### 4-4. Addressable 등록 검증 도구

`MapDataRegistrySO`의 에디터 확장에 검증 버튼을 둔다. **등록 자체는 Groups 창에서 사람이 하고,
도구는 어긋남만 잡아낸다.**

검사 항목:
- `mapAddressableID`가 비어 있는 `MapDataSO`
- 해당 ID로 등록된 Addressable 엔트리 없음 (3-1 재발 감지)
- 전체 경로로 등록돼 있어 ID와 불일치 (3-2 재발 감지)
- 같은 ID를 쓰는 `MapDataSO`가 둘 이상

### 4-5. SaveModule — 참조 교체

| | 기존 | 변경 |
|---|---|---|
| 저장 | `mapChangeManager.currentMap` | `mapManager.CurrentMapData.mapAddressableID` |
| 복원 | `ChangeMap(name)` + `OnMapChanged` | Registry로 `MapDataSO` 조회 → `LoadMap(mapData)` + `OnMapLoaded` |

**`SaveData.currentMapSceneName` 필드명은 유지한다.** Addressable 주소를 씬 이름으로 통일하면
기존에 저장된 값이 그대로 맵 ID가 되어 **세이브 마이그레이션이 불필요**하다. 의미 변경은 주석으로 남긴다.

## 5. 구현 순서

1. `Map/MapManager.cs` — `OnMapLoaded` 이벤트 + 언로드 순서 정정 (§4-1)
2. `Map/MapDataRegistrySO.cs` 신설 (§4-3)
3. `Map/Editor/MapDataRegistrySOEditor.cs` 신설 — 검증 버튼 (§4-4)
4. `Save/New_Save/SaveModule.cs` — 참조 교체 (§4-5)
5. 컴파일 검증 (런타임/에디터 어셈블리)

## 5-1. 구현 및 검증 현황 (2026-08-04)

구현 완료, 인게임 검증 통과.

- 언로드 → 로드 순서: 로그 호출 스택으로 확인
  (`unloaded`는 `MapManager.cs:44 <LoadMap>b__0`, `loaded`는 `MapManager.cs:75 <LoadMapInternal>b__0`)
- 언로드 로그가 나가는 맵 이름을 정확히 출력 (미리 캡처한 `unloadingMap` 덕분)
- 세이브 복원: `TestMap2`에서 로드 → 저장돼 있던 `TestMap1`으로 전환 후 좌표 복원까지 정상

### 계획 대비 정정 사항

**"Addressable 그룹이 비어서 로드가 실패한다"는 §3-1 진단은 틀렸다.**
`Built In Data` 그룹의 `PlayerDataGroupSchema.IncludeBuildSettingsScenes`가 켜져 있으면
(이 프로젝트는 `m_IncludeBuildSettingsScenes: 1`), Build Settings에 활성 등록된 씬이
**씬 이름을 주소로** 자동 노출된다.
(패키지 소스: `AddressableAssetEntry.GatherEditorSceneEntries` — 주소를
`Path.GetFileNameWithoutExtension(경로)`로 생성)

즉 `PrototypeMaps`가 비어 있어도 `Addressables.LoadSceneAsync("TestMap1")`은 해결된다.
그룹이 두 번 비워진 뒤에도 맵 로딩이 멀쩡했던 이유가 이것이다.

이에 맞춰 §4-4 검증 도구도 수정했다 — 처음엔 읽기 전용 그룹(`Built In Data`)을 건너뛰어
정상인 씬을 "등록 없음"으로 오탐했다. 지금은 모든 그룹을 검사하고 **어느 그룹으로
해결됐는지**를 로그에 남긴다.

**결론**: `PrototypeMaps` 등록은 지금 단계에서 필수가 아니다. 번들 분리·원격 업데이트가
필요해질 때 옮기면 되고, 그때 주소를 씬 이름으로 맞추는 것만 잊지 않으면 된다.

## 6. Unity 에디터에서 사람이 해야 할 일

1. ~~Addressables Groups 창에서 맵 씬 재등록~~ → **불필요** (§5-1 정정 참고).
   Build Settings 경유로 이미 해결된다. 나중에 번들로 분리할 때만 하면 되고,
   그때는 **주소를 씬 이름으로 맞추는 것**이 필수다.
2. **`MapDataRegistry` 에셋 생성** 후 `Collect All` 실행, `Addressable 등록 검증` 통과 확인 — 완료
3. **`SaveScene.prefab`의 `SaveModule`에 `Map Data Registry` 연결** — 완료
4. `MapManager`의 `initialMap`에 `Mapdata1` 연결 — 확인 완료
