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

    public void LoadMap(MapDataSO mapData)
    {
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
        Addressables.LoadSceneAsync(mapData.mapAddressableID, LoadSceneMode.Additive).Completed += (asyncHandle) =>
        {
            clearCurrentScene = true;
            currentLoadedScene = asyncHandle.Result;
            CurrentMapData = mapData;

            var rootObjects = currentLoadedScene.Scene.GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                if (root.TryGetComponent<MapLocalManager>(out var localManager))
                {
                    localManager.Initialize();
                    this.localManager = localManager;
                    break;
                }
            }

            navigationManager.RebuildWorld();

            Debug.Log("loaded: " + mapData.name + " (" + mapData.mapAddressableID + ")");

            OnMapLoaded?.Invoke(mapData);
        };
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