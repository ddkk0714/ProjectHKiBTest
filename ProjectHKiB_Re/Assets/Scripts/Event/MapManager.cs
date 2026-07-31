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

    public MapDataSO initialMap;
    [NaughtyAttributes.Button] public void LoadMap() => LoadMap(initialMap);

    public void Start()
    {
        LoadMap();
    }

    public void LoadMap(MapDataSO mapData)
    {
        if (clearCurrentScene)
        {
            Addressables.UnloadSceneAsync(currentLoadedScene).Completed += (asyncHandle) =>
            {
                clearCurrentScene = false;
                currentLoadedScene = new SceneInstance();
                Debug.Log("unloaded: " + CurrentMapData.name + " (" + CurrentMapData.mapAddressableID + ")");
            };
        }

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
        };
    }

    public void Oestroy()
    {
        Addressables.Release(currentLoadedScene);
    }
}