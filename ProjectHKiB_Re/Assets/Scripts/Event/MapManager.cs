using System;
using EntityControl;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

/// <summary>
/// Addressables 맵 씬의 로드·초기화·교체 수명주기를 관리한다.
/// 맵이 준비되지 않은 구간에는 프로젝트 물리를 정지해 월드 오브젝트의 선행 낙하를 막는다.
/// </summary>
public class MapManager : MonoBehaviour
{
    private bool clearCurrentScene = false;
    private SceneInstance currentLoadedScene;
    public MapDataSO CurrentMapData { get; private set; }

    public bool IsRealWorld => CurrentMapData != null && CurrentMapData.isRealWorld;
    public MapLocalManager localManager;
    public NavigationManager navigationManager;

    [Tooltip("맵을 비동기로 불러오는 동안 일시 정지할 프로젝트 물리 관리자입니다. 비어 있으면 런타임에 자동으로 찾습니다.")]
    [SerializeField] private PhysicsManager _physicsManager;

    [Tooltip("동시에 진행 중인 맵 로드가 물리를 정지하도록 요청한 횟수입니다.")]
    private int _physicsPauseDepth;

    [Tooltip("첫 맵 로드가 물리를 정지하기 직전의 활성 상태입니다.")]
    private bool _physicsWasEnabledBeforeLoad;

    public event Action<MapDataSO> OnMapLoaded;

    public MapDataSO initialMap;
    [NaughtyAttributes.Button] public void LoadMap() => LoadMap(initialMap);

    public void Start()
    {
        LoadMap();
    }
    private Scene _adoptedScene;

    /// <summary>
    /// 지정한 맵의 Addressables 로드를 시작하고 완료될 때까지 프로젝트 물리를 정지한다.
    /// 유효하지 않은 맵 요청은 현재 상태를 변경하지 않고 오류만 기록한다.
    /// </summary>
    public void LoadMap(MapDataSO mapData)
    {
        if (!mapData || string.IsNullOrWhiteSpace(mapData.mapAddressableID))
        {
            Debug.LogError("[MapManager] Cannot load a map without a MapDataSO and an Addressables scene key.");
            return;
        }
        string targetMapAddressableID = mapData.mapAddressableID;
        SuspendPhysicsForMapLoad();
        SceneInstance previousAddressableScene = currentLoadedScene;
        bool unloadPreviousAddressableScene = clearCurrentScene;
        Scene previousAdoptedScene = _adoptedScene;
        bool unloadPreviousAdoptedScene = previousAdoptedScene.IsValid() && previousAdoptedScene.isLoaded;

        try
        {
            LoadMapInternal(
                targetMapAddressableID,
                previousAddressableScene,
                unloadPreviousAddressableScene,
                previousAdoptedScene,
                unloadPreviousAdoptedScene);
        }
        catch
        {
            ResumePhysicsAfterMapLoad();
            throw;
        }
    }

    private void LoadMapInternal(
        string targetMapAddressableID,
        SceneInstance previousAddressableScene = default,
        bool unloadPreviousAddressableScene = false,
        Scene previousAdoptedScene = default,
        bool unloadPreviousAdoptedScene = false)
    {
        Scene existing = SceneManager.GetSceneByName(targetMapAddressableID);
        if (existing.IsValid() && existing.isLoaded)
        {
            if (unloadPreviousAddressableScene && previousAddressableScene.Scene == existing)
            {
                CompleteMapLoad(existing, targetMapAddressableID);
                return;
            }

            _adoptedScene = existing;
            clearCurrentScene = false;
            currentLoadedScene = default;
            Debug.Log($"[MapManager] 이미 열려 있던 '{existing.name}' 씬을 그대로 씁니다(중복 로드하지 않음).");
            CompleteMapLoad(
                existing,
                targetMapAddressableID,
                previousAddressableScene,
                unloadPreviousAddressableScene,
                previousAdoptedScene,
                unloadPreviousAdoptedScene && previousAdoptedScene != existing);
            return;
        }

        Addressables.LoadSceneAsync(targetMapAddressableID, LoadSceneMode.Additive).Completed += (asyncHandle) =>
        {
            if (asyncHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[MapManager] Failed to load Addressables map scene '{targetMapAddressableID}'. {asyncHandle.OperationException}");
                ResumePhysicsAfterMapLoad();
                return;
            }

            clearCurrentScene = true;
            currentLoadedScene = asyncHandle.Result;
            CompleteMapLoad(
                currentLoadedScene.Scene,
                targetMapAddressableID,
                previousAddressableScene,
                unloadPreviousAddressableScene,
                previousAdoptedScene,
                unloadPreviousAdoptedScene);
        };
    }

    /// <summary>
    /// 맵 초기화와 후속 콜백이 성공하거나 실패해도 로딩용 물리 정지를 반드시 해제한다.
    /// 중첩 로드는 마지막 완료 시점까지 물리 정지를 유지한다.
    /// </summary>
    private void CompleteMapLoad(
        Scene scene,
        string targetMapAddressableID,
        SceneInstance previousAddressableScene = default,
        bool unloadPreviousAddressableScene = false,
        Scene previousAdoptedScene = default,
        bool unloadPreviousAdoptedScene = false)
    {
        try
        {
            FinishMapLoad(
                scene,
                targetMapAddressableID,
                previousAddressableScene,
                unloadPreviousAddressableScene,
                previousAdoptedScene,
                unloadPreviousAdoptedScene);
        }
        finally
        {
            ResumePhysicsAfterMapLoad();
        }
    }

    /// <summary>
    /// Addressables 맵이 준비될 때까지 프로젝트 물리 갱신을 일시 중단한다.
    /// 기존 비활성 상태는 기억해 두어 로드 완료 후 임의로 켜지 않게 한다.
    /// </summary>
    private void SuspendPhysicsForMapLoad()
    {
        if (_physicsPauseDepth == 0)
        {
            if (_physicsManager == null) _physicsManager = FindObjectOfType<PhysicsManager>();
            if (_physicsManager == null)
            {
                Debug.LogWarning("[MapManager] PhysicsManager를 찾을 수 없어 맵 로딩 중 물리를 정지하지 못했습니다.");
            }
            else
            {
                _physicsWasEnabledBeforeLoad = _physicsManager.enable;
                _physicsManager.enable = false;
            }
        }

        _physicsPauseDepth++;
    }

    /// <summary>
    /// 완료된 맵 로드의 물리 정지 요청을 해제한다.
    /// 모든 중첩 로드가 끝나면 로딩 전 활성 상태를 정확히 복원한다.
    /// </summary>
    private void ResumePhysicsAfterMapLoad()
    {
        if (_physicsPauseDepth <= 0) return;

        _physicsPauseDepth--;
        if (_physicsPauseDepth > 0 || _physicsManager == null) return;

        _physicsManager.enable = _physicsWasEnabledBeforeLoad;
    }

    private void FinishMapLoad(
        Scene scene,
        string targetMapAddressableID,
        SceneInstance previousAddressableScene = default,
        bool unloadPreviousAddressableScene = false,
        Scene previousAdoptedScene = default,
        bool unloadPreviousAdoptedScene = false)
    {
        MapLocalManager loadedLocalManager = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent<MapLocalManager>(out var local))
            {
                loadedLocalManager = local;
                break;
            }
        }

        if (loadedLocalManager == null || !loadedLocalManager.mapData)
        {
            Debug.LogError($"[MapManager] Loaded map scene '{targetMapAddressableID}' is missing MapLocalManager.mapData.");
            return;
        }

        CurrentMapData = loadedLocalManager.mapData;
        loadedLocalManager.Initialize();
        localManager = loadedLocalManager;

        navigationManager.RebuildWorld();

        Debug.Log("loaded: " + CurrentMapData.name + " (" + CurrentMapData.mapAddressableID + ")");

        OnMapLoaded?.Invoke(CurrentMapData);

        TryAdvanceMapLoadedTransition();

        ReleasePreviousMap(
            previousAddressableScene,
            unloadPreviousAddressableScene,
            previousAdoptedScene,
            unloadPreviousAdoptedScene);

        UnloadStrayScenes(scene, previousAddressableScene.Scene, previousAdoptedScene);
    }

    private void ReleasePreviousMap(
        SceneInstance previousAddressableScene,
        bool unloadPreviousAddressableScene,
        Scene previousAdoptedScene,
        bool unloadPreviousAdoptedScene)
    {
        if (unloadPreviousAddressableScene && previousAddressableScene.Scene.IsValid())
        {
            string previousMapName = previousAddressableScene.Scene.name;
            Addressables.UnloadSceneAsync(previousAddressableScene).Completed += _ =>
                Debug.Log("unloaded: " + previousMapName);
        }

        if (unloadPreviousAdoptedScene && previousAdoptedScene.IsValid() && previousAdoptedScene.isLoaded)
        {
            if (previousAdoptedScene == _adoptedScene)
                _adoptedScene = default;

            SceneManager.UnloadSceneAsync(previousAdoptedScene);
        }
    }

    private void TryAdvanceMapLoadedTransition()
    {
        EventManager eventManager = GameManager.instance != null ? GameManager.instance.eventManager : null;
        StateSO currentState = eventManager != null ? eventManager.CurrentState : null;
        if (eventManager == null || currentState == null || currentState.transitions == null)
            return;

        eventManager.EnsureTransitionCapacity(currentState.transitions.Length);
        bool hasMapLoadedTransition = false;

        for (int i = 0; i < currentState.transitions.Length; i++)
        {
            StateTransition transition = currentState.transitions[i];
            if (transition == null || transition.activationInput != EnumManager.InputType.None || transition.decisions == null)
                continue;

            for (int j = 0; j < transition.decisions.Length; j++)
            {
                StateMachine.StateDecision decision = transition.decisions[j].Decision;
                if (decision is not StateMachine.MapLoadedDecision)
                    continue;

                hasMapLoadedTransition = true;
                if (!decision.Decide(eventManager))
                    continue;

                eventManager.TransitionConditions[i] = true;
                currentState.CheckDecision(eventManager);
                return;
            }
        }

        if (hasMapLoadedTransition)
        {
            string loadedMapID = CurrentMapData != null ? CurrentMapData.mapAddressableID : "(none)";
            Debug.LogWarning($"[MapManager] MapLoadedDecision did not match the loaded map '{loadedMapID}' in state '{currentState.name}'.");
        }
    }

    private void UnloadStrayScenes(Scene keep, Scene pendingAddressablesUnload, Scene pendingAdoptedUnload)
    {
        Scene active = SceneManager.GetActiveScene();
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene == active || scene == keep ||
                scene == pendingAddressablesUnload || scene == pendingAdoptedUnload) continue;

            Debug.LogWarning($"[MapManager] 쓰이지 않는 씬 '{scene.name}'을 언로드합니다 — " +
                             "에디터에서 열어둔 다른 맵 씬으로 보입니다(오브젝트가 겹쳐 보이는 원인).");
            SceneManager.UnloadSceneAsync(scene);
        }
    }

    /// <summary>
    /// 로딩 도중 관리자가 파괴되면 물리 상태와 Addressables 핸들을 안전하게 정리한다.
    /// 아직 완료되지 않은 로드가 있어도 물리가 영구 비활성 상태로 남지 않게 한다.
    /// </summary>
    private void OnDestroy()
    {
        if (_physicsPauseDepth > 0)
        {
            _physicsPauseDepth = 1;
            ResumePhysicsAfterMapLoad();
        }

        if (!clearCurrentScene) return;
        Addressables.Release(currentLoadedScene);
    }
}
