using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 씬의 New Game / Continue / Options 버튼을 기존 기능에 연결한다.
/// New Game은 게임 씬을 그냥 열고, Continue는 게임 씬을 연 뒤 SaveModule.StartLoad로 슬롯을 불러온다
/// (SaveTester의 F9 불러오기와 같은 경로). Options는 프로젝트에 옵션 창이 아직 없어 연결하지 않는다.
/// </summary>
public class TitleScreen : MonoBehaviour
{
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button continueButton;

    [SerializeField] private string gameSceneName = "System";
    [SerializeField] private int saveSlot = 0;

    // Continue가 비활성일 때 글자 색. Button의 색 전환은 선택 막대에만 걸려 있어 글자는 따로 흐리게 한다.
    [SerializeField] private Color disabledTextColor = new(1f, 1f, 1f, 0.5f);

    // 메뉴 효과음. 게임의 AudioManager는 System 씬에 있어 타이틀에서는 쓸 수 없으므로 AudioSource로 직접 낸다.
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip moveClip;
    [SerializeField] private AudioClip confirmClip;
    [SerializeField] private AudioSource bgmSource;

    // 화면·BGM 페이드 시간(초). 들어올 때 페이드 인, 버튼을 누르면 페이드 아웃.
    [SerializeField] private float fadeSec = 0.5f;

    private static int s_pendingLoadSlot = -1;

    // 타이틀이 떠 있는 동안 게임 씬을 미리 읽어 둔다. 버튼을 누르면 켜기만 해서 동기 로드의 멈춤을 줄인다.
    private AsyncOperation _preload;
    private GameObject _lastSelected;
    private bool _starting;

    private void Start()
    {
        _preload = SceneManager.LoadSceneAsync(gameSceneName);
        _preload.allowSceneActivation = false;

        ScreenEffectManager.Instance.FadeToBlack(0f);
        ScreenEffectManager.Instance.FadeFromBlack(fadeSec);
        float bgmVolume = bgmSource.volume;
        bgmSource.volume = 0f;
        bgmSource.Play();
        bgmSource.DOFade(bgmVolume, fadeSec);

        newGameButton.onClick.AddListener(NewGame);
        continueButton.onClick.AddListener(Continue);

        bool hasSave = SaveModule.HasSave(saveSlot);
        continueButton.interactable = hasSave;
        if (!hasSave)
        {
            TextMeshProUGUI label = continueButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = disabledTextColor;
        }

        // RPG Maker처럼 세이브가 있으면 커서를 Continue에 둔다.
        EventSystem.current.SetSelectedGameObject((hasSave ? continueButton : newGameButton).gameObject);
        _lastSelected = EventSystem.current.currentSelectedGameObject;
    }

    // 커서(선택된 버튼)가 바뀌면 이동음.
    private void Update()
    {
        GameObject selected = EventSystem.current.currentSelectedGameObject;

        // 버튼 밖을 마우스로 누르면 EventSystem이 선택을 비워(Deselect On Background Click) 키보드 커서가 사라진다.
        // 비는 즉시 마지막 버튼을 다시 잡아 키보드로 항상 이어서 조작할 수 있게 한다.
        if (selected == null && _lastSelected != null)
        {
            EventSystem.current.SetSelectedGameObject(_lastSelected);
            return;
        }
        if (selected == _lastSelected) return;
        _lastSelected = selected;
        if (selected != null && !_starting) sfxSource.PlayOneShot(moveClip);
    }

    public void NewGame()
    {
        if (_starting) return;
        s_pendingLoadSlot = -1;
        StartGame();
    }

    public void Continue()
    {
        if (_starting) return;
        s_pendingLoadSlot = saveSlot;
        SceneManager.sceneLoaded += OnGameSceneLoaded;
        StartGame();
    }

    // 화면·BGM을 페이드 아웃하고(선택음도 끝까지 들리게) 다 끝나면 게임 씬을 켠다.
    // 화면은 검게 남고, 맵 로드가 끝나면 MapManager가 페이드 인한다.
    private void StartGame()
    {
        _starting = true;
        sfxSource.PlayOneShot(confirmClip);
        ScreenEffectManager.Instance.FadeToBlack(fadeSec);
        bgmSource.DOFade(0f, fadeSec);
        Invoke(nameof(ActivateGameScene), Mathf.Max(fadeSec, confirmClip.length));
    }

    private void ActivateGameScene() => _preload.allowSceneActivation = true;

    // BGM 페이드 아웃과 씬 전환이 같은 시각에 끝나 AudioSource가 먼저 사라지면 DOTween이 "대상 없음" 경고를 남긴다.
    private void OnDestroy() => bgmSource.DOKill();

    // 이 오브젝트는 씬 전환으로 사라지므로 이후 처리는 static으로 둔다.
    private static void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        MapManager mapManager = GameManager.instance.mapManager;

        // 첫 맵(스토리 맵)을 저장된 맵으로 바꿔 둔다. 안 그러면 Continue 때도 스토리 대사가 재생된다.
        // sceneLoaded는 MapManager.Start(첫 맵 로드)보다 먼저 불리므로 여기서 바꾸면 반영된다.
        SaveModule saveModule = FindFirstObjectByType<SaveModule>();
        SaveSlotData save = SaveModule.PeekSave(s_pendingLoadSlot);
        MapDataSO savedMap = saveModule != null && saveModule.MapDataRegistry != null && save != null
            ? saveModule.MapDataRegistry.Find(save.currentMapSceneName)
            : null;
        if (savedMap != null) mapManager.initialMap = savedMap;

        // 첫 맵 로드가 끝난 뒤에 불러와야 한다. 먼저 불러오면 뒤늦게 끝난 첫 맵 로드가 복원한 상태를 덮는다.
        mapManager.OnMapLoaded += LoadPendingSlotOnce;
    }

    private static void LoadPendingSlotOnce(MapDataSO _)
    {
        GameManager gm = GameManager.instance;
        gm.mapManager.OnMapLoaded -= LoadPendingSlotOnce;
        if (s_pendingLoadSlot < 0) return;

        SaveModule saveModule = FindFirstObjectByType<SaveModule>();
        if (saveModule == null)
        {
            Debug.LogError($"[TitleScreen] '{SceneManager.GetActiveScene().name}' 씬에 SaveModule이 없어 Continue를 처리할 수 없습니다.");
            return;
        }

        saveModule.StartLoad(s_pendingLoadSlot, gm.inventoryManager, gm.gearManager, gm.player, RouteModule.Instance);
        s_pendingLoadSlot = -1;
    }
}
