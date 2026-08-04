using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using DG.Tweening;
using TMPro;
using R3;
using UnityEngine.InputSystem;

[RequireComponent(typeof(StateController))]
public class DialogueModule : InterfaceModule, IDialogueable
{
    private static readonly WaitForSeconds _waitForSeconds0_025 = new(0.025f);

    // === UI ===
    public TextMeshProUGUI lineText;
    public TextMeshProUGUI characterName;
    public GameObject choicePanel;
    public RectTransform nextArrowRect;
    public ButtonEnhanced[] choiceButtons;

    [System.Serializable]
    public struct DialogueVariable { public string key; public string value; }
    public DialogueVariable[] initialVariables;

    private Dictionary<string, ReactiveProperty<string>> variableTable;

    // === State Machine Parameter ===
    private Line _currentLine;
    [field: NaughtyAttributes.ReadOnly][SerializeField] private int _subLineIndex;
    [field: NaughtyAttributes.ReadOnly][SerializeField] private bool _isLineEnded;
    [field: NaughtyAttributes.ReadOnly][SerializeField] private int _choicedNum;
    public void NextSubLine() => _subLineIndex++;
    public bool IsLineEnded => _isLineEnded;
    public int ChoicedNum => _choicedNum;

    // === DOTween ===
    public Tweener LinePrintingTweener { get; set; }
    private Tween nextArrowMoveTween;
    private Vector2 nextArrowBasePos;

    public System.Action onExitDialogue { get; set; }

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IDialogueable>(this);
    }

    public void Initialize()
    {
        // DOTween Tweener Initialize (for recycle)
        LinePrintingTweener = null;

        if (nextArrowRect != null)
        {
            nextArrowBasePos = nextArrowRect.anchoredPosition;
            nextArrowRect.gameObject.SetActive(false);
        }

        InitializeVariables();
    }

    public void StartDialogue()
    {
        GameManager.instance.inputManager.MENUMode();
        _subLineIndex = 0;
        choicePanel.SetActive(false);
        GameManager.instance.UIManager.OpenWindow("Dialogue");
    }

    public void ExitDialogue()
    {
        choicePanel.SetActive(false);
        onExitDialogue.Invoke();
    }

    public void StartLine(Line line)
    {
        _isLineEnded = false;
        _currentLine = line;
        _subLineIndex = 0;
        _choicedNum = -1;
        if (_currentLine.isChoice) BindUpdateChoice();
        else BindUpdateLine();

        HideNextArrow();

        // Set Character Name AND Text
        characterName.text = ResolveVariables(_currentLine.characterName);

        PrintCurrentSubLine();
    }

    public void ExitLine()
    {
        _isLineEnded = true;
        choicePanel.SetActive(false);
        if (_currentLine.isChoice) UnBindUpdateChoice();
        else UnBindUpdateLine();
    }

    public void BindUpdateLine() => GameManager.instance.inputManager.onSubmit += UpdateLineBinder;
    public void UnBindUpdateLine() => GameManager.instance.inputManager.onSubmit -= UpdateLineBinder;
    private void UpdateLineBinder(InputAction.CallbackContext context) { if (context.started) UpdateLine(); }
    private void UpdateLine()
    {
        if (LinePrintingTweener != null && LinePrintingTweener.IsActive() && LinePrintingTweener.IsPlaying())
        {
            LinePrintingTweener.Complete();
            return;
        }
        _subLineIndex++;

        if (_currentLine.sublines != null && _subLineIndex < _currentLine.sublines.Length)
        {
            PrintCurrentSubLine();
            return;
        }
        ExitLine();
    }

    public void BindUpdateChoice() => GameManager.instance.inputManager.onSubmit += UpdateChoiceBinder;
    public void UnBindUpdateChoice() => GameManager.instance.inputManager.onSubmit -= UpdateChoiceBinder;
    private void UpdateChoiceBinder(InputAction.CallbackContext context) { if (context.started) UpdateChoice(); }
    private void UpdateChoice()
    {
        if (LinePrintingTweener != null && LinePrintingTweener.IsActive() && LinePrintingTweener.IsPlaying())
        {
            LinePrintingTweener.Complete();
            return;
        }
        _subLineIndex++;

        if (_currentLine.sublines == null || _subLineIndex == _currentLine.sublines.Length)
            StartCoroutine(Choice());

        if (_currentLine.sublines != null && _subLineIndex < _currentLine.sublines.Length)
            PrintCurrentSubLine();
    }

    private IEnumerator Choice()
    {
        yield return _waitForSeconds0_025;
        choicePanel.SetActive(true);
        // Settin Button
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            int choice = i;
            ButtonEnhanced button = choiceButtons[i];

            if (_currentLine.choices != null && i < _currentLine.choices.Length)
            {
                button.gameObject.SetActive(true);
                button.onClick.RemoveAllListeners();

                button.onClick.AddListener(() => ChoiceCallback(choice));
                if (choiceButtons[i].text) choiceButtons[i].text.text = _currentLine.choices[i];
                if (choiceButtons[i].number) choiceButtons[i].number.text = $"{i}";
            }
            else button.gameObject.SetActive(false);
        }

        // Cursor on First Panel
        if (choiceButtons.Length > 0) choiceButtons[0].Select();
    }

    private void ChoiceCallback(int choice)
    {
        _choicedNum = choice;
        ExitLine();
    }

    private void PrintCurrentSubLine()
    {
        string raw = _currentLine.sublines[_subLineIndex];
        string resolved = ResolveVariables(raw);

        lineText.text = "";

        if (LinePrintingTweener != null && LinePrintingTweener.IsActive())
            LinePrintingTweener.Kill();

        // Typing Effect Animation
        // SetUpdate(true): 대화창은 TimeManager가 게임을 멈춘 동안에도 계속 진행돼야 하므로
        // timeScale의 영향을 받지 않는 시간축(unscaled)을 쓴다.
        LinePrintingTweener = lineText
            .DOText(resolved, _currentLine.Duration(_subLineIndex))
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .OnComplete(() => { ShowNextArrow(); });

        LinePrintingTweener?.Play();
    }

    public void ShowNextArrow()
    {
        if (nextArrowRect == null) return;

        // Arrow Animation Removed
        if (nextArrowMoveTween != null && nextArrowMoveTween.IsActive()) nextArrowMoveTween.Kill();

        nextArrowRect.anchoredPosition = nextArrowBasePos;
        nextArrowRect.gameObject.SetActive(true);

        // Shake Animaion
        nextArrowMoveTween = nextArrowRect
            .DOAnchorPosY(nextArrowBasePos.y + 3f, 1.0f)
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetLoops(-1, LoopType.Yoyo);
    }

    public void HideNextArrow()
    {
        if (nextArrowMoveTween != null && nextArrowMoveTween.IsActive())
        {
            nextArrowMoveTween.Kill();
            nextArrowMoveTween = null;
        }

        if (nextArrowRect != null)
        {
            nextArrowRect.anchoredPosition = nextArrowBasePos;
            nextArrowRect.gameObject.SetActive(false);
        }
    }

    #region Variables
    private void InitializeVariables()
    {
        variableTable = new Dictionary<string, ReactiveProperty<string>>();

        if (initialVariables == null) return;

        foreach (var v in initialVariables)
        {
            if (string.IsNullOrEmpty(v.key)) continue;

            variableTable[v.key] = new ReactiveProperty<string>(v.value ?? string.Empty);
        }
    }

    // Can Set Variable From Outside.
    public void SetVariable(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (variableTable == null)
        {
            variableTable = new Dictionary<string, ReactiveProperty<string>>();
        }

        if (!variableTable.TryGetValue(key, out var rp))
        {
            rp = new ReactiveProperty<string>(value ?? string.Empty);
            variableTable[key] = rp;
        }
        else
        {
            rp.Value = value ?? string.Empty;
        }
    }

    public string GetVariable(string key, string defaultValue = "")
    {
        if (variableTable == null) return defaultValue;

        if (variableTable.TryGetValue(key, out var rp) && rp != null)
        {
            return rp.Value ?? defaultValue;
        }

        return defaultValue;
    }

    // Change {KEY} to Text 
    private static readonly Regex variableRegex = new Regex(@"\{([A-Za-z0-9_]+)\}",
    RegexOptions.Compiled);

    // {KEY} RESOLVE
    public string ResolveVariables(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        if (variableTable == null) return input;

        return variableRegex.Replace(input, match =>
        {
            var key = match.Groups[1].Value;

            if (variableTable.TryGetValue(key, out var rp) && rp != null)
            {
                return rp.Value ?? "";
            }

            // If Don't Have
            return match.Value;
        });
    }

    public ReadOnlyReactiveProperty<string> ObserveVariable(string key, string defaultValue = "")
    {
        if (variableTable == null)
        {
            variableTable = new Dictionary<string, ReactiveProperty<string>>();
        }

        if (!variableTable.TryGetValue(key, out var rp) || rp == null)
        {
            // If not Set defaultValue
            rp = new ReactiveProperty<string>(defaultValue);
            variableTable[key] = rp;
        }

        return rp;
    }

    #endregion
}
