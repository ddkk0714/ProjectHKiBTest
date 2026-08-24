using System;
using EntityControl;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public class MapManager : MonoBehaviour
{
    private bool clearCurrentScene = false;
    private SceneInstance currentLoadedScene;
    public MapDataSO CurrentMapData { get; private set; }

    /// <summary>
    /// 지금 있는 곳이 "현실"인가(MapDataSO.isRealWorld). 맵이 아직 안 떴으면 false —
    /// 즉 확실히 현실이라고 알기 전까지는 꿈으로 본다(단서 열람을 실수로 열어주지 않는 쪽이 안전).
    /// </summary>
    public bool IsRealWorld => CurrentMapData != null && CurrentMapData.isRealWorld;
    public MapLocalManager localManager;
    public NavigationManager navigationManager;

    // 맵 로드가 완전히 끝난 시점(MapLocalManager 초기화·내비게이션 재구축까지 마친 뒤)에 발행.
    // 세이브 로드가 "저장된 맵으로 전환한 뒤 저장된 좌표로 텔레포트"를 하려면 완료 시점을 알아야
    // 하는데, LoadMap()이 Addressables 비동기 콜백이라 완료를 동기적으로 기다릴 방법이 없어서
    // 필요하다. (MapChangeManager.OnMapChanged와 같은 목적)
    public event Action<MapDataSO> OnMapLoaded;

    public MapDataSO initialMap;
    [NaughtyAttributes.Button] public void LoadMap() => LoadMap(initialMap);

    public void Start()
    {
        LoadMap();
    }

    // Addressables로 얹은 게 아니라 "이미 열려 있어서 그대로 쓰기로 한" 맵 씬.
    // 에디터에서 맵 씬을 열어둔 채 플레이를 누른 경우가 여기 해당한다 — 이때 Addressables로 또
    // 얹으면 같은 맵이 두 벌이 되어, 이벤트가 조작하는 사본과 화면에 보이는 사본이 갈린다
    // (퇴장시킨 NPC가 안 사라지는 것처럼 보이던 원인). 핸들을 우리가 들고 있지 않으므로
    // 나중에 맵을 바꿀 때는 Addressables가 아니라 SceneManager로 내려야 한다.
    private Scene _adoptedScene;

    public void LoadMap(MapDataSO mapData)
    {
        // 채택한 씬은 우리 핸들이 아니라서 Addressables로 못 내린다 — SceneManager로 내리고 잇는다.
        if (_adoptedScene.IsValid() && _adoptedScene.isLoaded)
        {
            Scene toUnload = _adoptedScene;
            _adoptedScene = default;
            SceneManager.UnloadSceneAsync(toUnload).completed += _ => LoadMapInternal(mapData);
            return;
        }

        // 예전엔 언로드 완료를 기다리지 않고 곧바로 로드를 시작해 둘이 경쟁했다. 세이브 로드의
        // "다른 맵으로 전환" 경로가 정확히 여기를 타므로 안정성에 직결돼, 언로드가 끝난 뒤에
        // 로드하도록 순서를 잡았다. 언로드 로그에 쓸 맵은 미리 캡처한다 — 콜백이 실행될 시점의
        // CurrentMapData는 이미 새 맵으로 덮여 있을 수 있어 예전엔 로그가 어긋났다. (2026-08-04)
        if (clearCurrentScene)
        {
            MapDataSO unloadingMap = CurrentMapData;
            Addressables.UnloadSceneAsync(currentLoadedScene).Completed += (asyncHandle) =>
            {
                clearCurrentScene = false;
                currentLoadedScene = new SceneInstance();
                if (unloadingMap != null)
                    Debug.Log("unloaded: " + unloadingMap.name + " (" + unloadingMap.mapAddressableID + ")");

                LoadMapInternal(mapData);
            };
            return;
        }

        LoadMapInternal(mapData);
    }

    private void LoadMapInternal(MapDataSO mapData)
    {
        // 이미 열려 있는 씬이면 그대로 채택한다 — 또 얹지 않으므로 사본이 생기지 않고, 로드를
        // 기다리는 시간도 없어 첫 프레임부터 바닥이 있다(예전에 여기서 맵을 내렸다 다시 얹느라
        // 그 사이 플레이어가 허공에서 떨어졌다).
        Scene existing = SceneManager.GetSceneByName(mapData.mapAddressableID);
        if (existing.IsValid() && existing.isLoaded)
        {
            _adoptedScene = existing;
            Debug.Log($"[MapManager] 이미 열려 있던 '{existing.name}' 씬을 그대로 씁니다(중복 로드하지 않음).");
            FinishMapLoad(existing, mapData);
            return;
        }

        Addressables.LoadSceneAsync(mapData.mapAddressableID, LoadSceneMode.Additive).Completed += (asyncHandle) =>
        {
            clearCurrentScene = true;
            currentLoadedScene = asyncHandle.Result;
            FinishMapLoad(currentLoadedScene.Scene, mapData);
        };
    }

    private void FinishMapLoad(Scene scene, MapDataSO mapData)
    {
        CurrentMapData = mapData;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent<MapLocalManager>(out var local))
            {
                local.Initialize();
                localManager = local;
                break;
            }
        }

        navigationManager.RebuildWorld();

        Debug.Log("loaded: " + mapData.name + " (" + mapData.mapAddressableID + ")");

        OnMapLoaded?.Invoke(mapData);

        UnloadStrayScenes(scene);
    }

    // 지금 쓰는 맵도, 시스템 씬도 아닌데 로드돼 있는 씬 — 에디터에서 여러 맵을 열어둔 채 플레이를
    // 누른 잔재다. 그대로 두면 다른 맵의 오브젝트가 겹쳐 보인다. 맵이 준비된 뒤에 치우므로
    // 바닥이 없는 순간이 생기지 않는다. 빌드에서는 해당하는 씬이 없어 아무 일도 하지 않는다.
    private void UnloadStrayScenes(Scene keep)
    {
        Scene active = SceneManager.GetActiveScene();
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene == active || scene == keep) continue;

            Debug.LogWarning($"[MapManager] 쓰이지 않는 씬 '{scene.name}'을 언로드합니다 — " +
                             "에디터에서 열어둔 다른 맵 씬으로 보입니다(오브젝트가 겹쳐 보이는 원인).");
            SceneManager.UnloadSceneAsync(scene);
        }
    }

    // 예전엔 메서드 이름이 Oestroy로 잘못 적혀 있어 Unity 콜백으로 인식되지 않았고, 그 결과
    // 로드한 씬 핸들이 한 번도 해제되지 않았다. clearCurrentScene 가드는 아직 아무 씬도 로드하지
    // 않은 상태에서 기본값 SceneInstance를 해제하려다 경고가 나는 것을 막는다. (2026-08-04)
    private void OnDestroy()
    {
        if (!clearCurrentScene) return;
        Addressables.Release(currentLoadedScene);
    }
}