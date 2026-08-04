using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 창의 내용물이 자기 GameObject가 아니거나(런타임 생성 등), 여닫을 때 준비 작업이 필요한 창이
/// <see cref="Window"/>와 같은 GameObject에 이걸 구현해두면 Window가 알아서 위임한다.
/// 덕분에 그런 창에도 Window 서브클래스 없이 기본 Window 컴포넌트를 그대로 붙일 수 있다.
///
/// [주의] 구현부에서 UIManager.OpenWindow/CloseWindow를 다시 부르면 무한 재귀가 된다.
/// </summary>
public interface IWindowContent
{
    /// <summary>지금 열 수 있는 상태인지. false면 UIManager가 창을 열지 않고 스택에도 안 쌓는다.</summary>
    bool CanOpenWindow { get; }

    /// <summary>창을 실제로 여는 작업(내부 패널 활성화 등).</summary>
    void OpenWindowContent();

    /// <summary>창을 실제로 닫는 작업.</summary>
    void CloseWindowContent();
}

public class Window : MonoBehaviour
{
    public bool isPopup;

    // 이 창이 열려 있는 동안 게임 시간을 멈출지 여부(UIManager가 TimeManager에 전달).
    // 모든 창이 멈춰야 하는 건 아니라서 창별로 켠다 — HUD성 팝업/토스트는 꺼둔다.
    // [주의] 기존 프리팹들은 이 필드가 없던 시절에 직렬화됐으므로 false로 로드된다.
    //        게임을 멈춰야 하는 창은 프리팹에서 직접 체크해야 한다.
    public bool pausesGame;
    public Button initButton;
    [NaughtyAttributes.ReadOnly]public Button lastSelectedButton;

    public enum ButtonSelectMethod { AlwaysInitialize, MaintainWhenPopup, AlwaysMaintain }

    public ButtonSelectMethod buttonSelectMethod;

    public UnityEvent OnWindowShow;
    public UnityEvent OnWindowHide;

    public void SelectInitButton(bool fromPopupClose)
    {
        if (initButton == null) return;
        if (buttonSelectMethod == ButtonSelectMethod.AlwaysMaintain || (buttonSelectMethod == ButtonSelectMethod.MaintainWhenPopup && fromPopupClose))
        {
            if (lastSelectedButton != null && lastSelectedButton.IsActive() && lastSelectedButton.gameObject.activeSelf)
            {
                lastSelectedButton.Select();
                return;
            }
        }
        initButton.Select();
    }

    // 같은 GameObject에 IWindowContent 구현체가 있으면 여닫기를 그쪽에 맡긴다.
    // 없으면(대부분의 창) 그냥 자기 GameObject를 껐다 켠다.
    private IWindowContent _content;
    private bool _contentResolved;

    private IWindowContent Content
    {
        get
        {
            if (!_contentResolved)
            {
                _content = GetComponent<IWindowContent>();
                _contentResolved = true;
            }
            return _content;
        }
    }

    // UIManager가 열기 직전에 묻는다. false면 창을 열지 않고 스택에도 쌓지 않는다.
    // (지도/도감의 "이동 중에는 못 연다" 같은 조건이 여기로 들어온다)
    public bool CanOpen => Content == null || Content.CanOpenWindow;

    public void Open()
    {
        if (Content != null) Content.OpenWindowContent();
        else gameObject.SetActive(true);

        OnWindowShow?.Invoke();
    }

    public void Close()
    {
        if (Content != null) Content.CloseWindowContent();
        else gameObject.SetActive(false);

        OnWindowHide?.Invoke();
    }

    public void SetLastSelectedButton(Button button)
    {
        lastSelectedButton = button;
    }
}