using System;
using System.Collections.Generic;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Serializable]
    public class WindowItem
    {
        public string name;
        public Window window;
        public bool useHotkey;
        public EnumManager.InputType hotkey;
    }

    public List<WindowItem> windows;

    public List<WindowItem> openedWindows;

    public int defaultPauseWindowIndex;
    public bool canExit = true;

    // "현실"에서만 열 수 있는 창들(MapDataSO.isRealWorld). 꿈속에서는 단서가 흐릿해 제대로 볼 수
    // 없다는 기획을 그대로 옮긴 것으로, 해몽도 자연히 현실에서만 가능해진다 — 단서를 노트에
    // 늘어놓는 것 자체가 현실에서만 되므로.
    //
    // 각 패널의 CanOpenWindow에 흩어 넣지 않고 여기 한 곳에 둔 이유는, 창을 여는 모든 경로
    // (단축키·Toggle·OpenWindowAction·다른 패널의 링크)가 결국 OpenWindow(WindowItem)로 모이기
    // 때문이다. 규칙이 한 군데 있어야 나중에 창이 늘어도 어긋나지 않는다.
    public string[] realWorldOnlyWindows = { "Clue", "Note", "Map", "Internet" };

    public DialogueModule dialogueModule;

    public void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        //OpenWindow(0);
        GameManager.instance.inputManager.onMenu += OnOpenMenuInput;
        GameManager.instance.inputManager.onMENUCancel += OnCloseWindowInput;
        dialogueModule.onExitDialogue += () => { canExit = true; CloseWindow("Dialogue"); };
    }

    public void OnDestroy()
    {
        // 창을 열어둔 채 씬이 바뀌면 정지 사유가 남아 다음 씬이 멈춘 채로 시작될 수 있다.
        for (int i = 0; i < openedWindows.Count; i++) SetWindowPause(openedWindows[i], false);

        GameManager.instance.inputManager.onMenu -= OnOpenMenuInput;
        GameManager.instance.inputManager.onMENUCancel -= OnCloseWindowInput;
        dialogueModule.onExitDialogue -= () => { canExit = true; CloseWindow("Dialogue"); };
    }

    public void OpenWindow(string name)
    {
        if (windows == null) return;

        WindowItem window = windows.Find((a) => a.name == name);
        // 등록명이 한 글자만 어긋나도 여기서 null이 되어 아무 일 없이 넘어간다. 그러면 "단축키를
        // 눌러도 창이 안 뜬다"만 보이고 원인을 찾기 어려우니 반드시 남긴다
        // (실제로 windows에 "Clue"로 등록해두고 코드는 "Codex"를 찾다가 한 번 겪음, 2026-08-04).
        if (window == null)
        {
            Debug.LogWarning($"[UIManager] '{name}' 이름의 창이 windows 목록에 없습니다. 등록명을 확인하세요.");
            return;
        }

        OpenWindow(window);
    }

    public void OpenWindow(int index)
    {
        if (windows == null) return;
        if (index >= windows.Count) return;
        OpenWindow(windows[index]);
    }

    public bool IsWindowOpen(string name) => openedWindows.Exists(a => a.name == name);

    public void ToggleWindow(string name)
    {
        if (IsWindowOpen(name)) CloseWindow(name);
        else OpenWindow(name);
    }

    /// <summary>이 창이 "현실 전용"인데 지금 꿈속이라 열 수 없는지.</summary>
    public bool IsBlockedByDreamWorld(string windowName)
    {
        if (realWorldOnlyWindows == null || System.Array.IndexOf(realWorldOnlyWindows, windowName) < 0) return false;

        MapManager mapManager = GameManager.instance == null ? null : GameManager.instance.mapManager;
        return mapManager == null || !mapManager.IsRealWorld;
    }

    public void OpenWindow(WindowItem window)
    {
        if (window == null) return;

        if (IsBlockedByDreamWorld(window.name))
        {
            Debug.Log($"[UIManager] '{window.name}' 창은 현실에서만 열 수 있습니다 — 꿈속에서는 단서가 흐릿해 읽을 수 없습니다.");
            return;
        }

        // 창 자신이 지금 열릴 수 없다고 하면(예: 이동 중 지도 열람 금지) 스택도 건드리지 않는다.
        if (!window.window.CanOpen) return;
        if (!window.window.isPopup) CloseWindow();
        //Debug.Log("Window opened: " + window.name);
        window.window.Open();
        openedWindows.Add(window);
        SetWindowPause(window, true);
        InitButton(false);
    }

    // 창마다 별개의 정지 사유를 걸어 TimeManager에 넘긴다. 사유를 이름별로 나누는 이유는
    // 메뉴 위에 팝업을 겹쳐 연 뒤 팝업만 닫았을 때 게임이 재개돼버리는 걸 막기 위해서다.
    private static string PauseReasonOf(WindowItem window) => TimeManager.ReasonMenu + ":" + window.name;

    private void SetWindowPause(WindowItem window, bool pause)
    {
        if (window == null || window.window == null || !window.window.pausesGame) return;
        if (GameManager.instance == null) return;

        TimeManager timeManager = GameManager.instance.timeManager;
        if (timeManager == null) return;

        if (pause) timeManager.Pause(PauseReasonOf(window));
        else timeManager.Resume(PauseReasonOf(window));
    }

    public void CloseWindow()
    {
        if (openedWindows.Count < 1) return;
        openedWindows[^1]?.window.Close();
        bool isClosedWindowPopup = openedWindows[^1].window.isPopup;
        SetWindowPause(openedWindows[^1], false);
        openedWindows.Remove(openedWindows[^1]);
        //Debug.Log($"Window closed, remaining window stack: {openedWindows.Count}");
        if (openedWindows.Count < 1)
            GameManager.instance.inputManager.PLAYMode();
        else
            InitButton(isClosedWindowPopup);
    }

    public void CloseWindow(string name)
    {
        if (windows == null) return;
        WindowItem window = openedWindows.Find((a) => a.name == name);
        if (window != null)
        {
            window.window.Close();
            bool isClosedWindowPopup = window.window.isPopup;
            SetWindowPause(window, false);
            openedWindows.Remove(window);
            //Debug.Log($"Window closed: {window.name}, remaining window stack: {openedWindows.Count}");
            if (openedWindows.Count < 1)
                GameManager.instance.inputManager.PLAYMode();
            else
                InitButton(isClosedWindowPopup);
        }
    }

    public void InitButton(bool fromPopupClose)
    {
        WindowItem window = openedWindows[^1];
        window.window.SelectInitButton(fromPopupClose);
    }

    public void CloseAllWindows()
    {
        while (openedWindows.Count > 0) CloseWindow();
    }

    public void OnExitPressed(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (context.started)
        {
            if (openedWindows.Count < 1)
                OpenWindow(defaultPauseWindowIndex);
            else
                if (canExit) CloseWindow();
        }
    }

    public void OnOpenMenuInput(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (context.started)
        {
            OpenWindow(defaultPauseWindowIndex);
            GameManager.instance.inputManager.MENUMode();
        }
    }

    public void OnCloseWindowInput(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (context.started)
        {
            if (canExit) CloseWindow();
        }
    }

    public void StartDialogue()
    {
        OpenWindow("Dialogue");
        canExit = false;
        dialogueModule.StartDialogue();
    }

    public void ExitDialogue() => dialogueModule.ExitDialogue();
}