using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour, @PlayerAction.IPLAYActions, PlayerAction.IMENUActions, PlayerAction.IUI_TOGGLEActions
{
    public @PlayerAction inputs;
    public Vector2 MoveInput { get; private set; }
    public Vector2 LastSetMoveInput { get; set; }
    public bool MoveInputPressed { get; private set; }
    public bool ConfirmInput { get; private set; }

    private void Awake()
    {
        inputs = new @PlayerAction();
        inputs.PLAY.SetCallbacks(this);
        inputs.MENU.SetCallbacks(this);
        inputs.UI_TOGGLE.SetCallbacks(this);
        inputs.UI_TOGGLE.Enable(); // 지도/노트/도감/인터넷 토글 — PLAY/MENU/GRAFFITI 모드 전환과 무관하게 항상 켜둔다.
        PLAYMode();
    }

    public bool GetInputByEnum(EnumManager.InputType inputType)
    {
        return inputType switch
        {
            EnumManager.InputType.OnMove => !inputs.PLAY.Move.ReadValue<Vector2>().Equals(Vector2.zero),
            EnumManager.InputType.OnSprint => inputs.PLAY.Sprint.inProgress,
            EnumManager.InputType.HasDodge => inputs.PLAY.Dodge.inProgress,
            EnumManager.InputType.HasDInput => inputs.PLAY.Move.ReadValue<Vector2>().y < 0,
            EnumManager.InputType.HasLInput => inputs.PLAY.Move.ReadValue<Vector2>().x < 0,
            EnumManager.InputType.HasRInput => inputs.PLAY.Move.ReadValue<Vector2>().x > 0,
            EnumManager.InputType.HasUInput => inputs.PLAY.Move.ReadValue<Vector2>().y > 0,
            EnumManager.InputType.HasAttack => inputs.PLAY.Attack.inProgress,
            EnumManager.InputType.HasSkill => inputs.PLAY.Skill.inProgress,
            _ => false,
        };
    }

    public void PLAYMode()
    {
        inputs.PLAY.Enable();
        inputs.MENU.Disable();
        inputs.GRAFFITI.Disable();
        //Debug.Log("PLAYMode");
    }

    public void MENUMode()
    {
        inputs.PLAY.Disable();
        inputs.MENU.Enable();
        inputs.GRAFFITI.Disable();
        //Debug.Log("MENUMode");
    }

    public void GRAFFITIMode()
    {
        inputs.PLAY.Disable();
        inputs.MENU.Disable();
        inputs.GRAFFITI.Enable();
        //Debug.Log("GRAFFITIMode");
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        MoveInput = context.ReadValue<Vector2>();
        if (MoveInput.x != 0 || MoveInput.y != 0) LastSetMoveInput = MoveInput;
    }

    public void OnMovePressedD(InputAction.CallbackContext context)
    {
        if (!MoveInputPressed) MoveInputPressed = context.started;
    }

    public void OnMovePressedR(InputAction.CallbackContext context)
    {
        if (!MoveInputPressed) MoveInputPressed = context.started;
    }

    public void OnMovePressedU(InputAction.CallbackContext context)
    {
        if (!MoveInputPressed) MoveInputPressed = context.started;
    }

    public void OnMovePressedL(InputAction.CallbackContext context)
    {
        if (!MoveInputPressed) MoveInputPressed = context.started;
    }

    public void OnSprint(InputAction.CallbackContext context) { }

    public void OnAttack(InputAction.CallbackContext context) { }

    public void OnDodge(InputAction.CallbackContext context) { }

    public void OnGraffitiSystem(InputAction.CallbackContext context) { }

    public void OnConfirm(InputAction.CallbackContext context)
    {
        ConfirmInput = context.performed;
    }

    public void OnSkill(InputAction.CallbackContext context) { }

    public Action<InputAction.CallbackContext> onMenu;
    public void OnMenu(InputAction.CallbackContext context) => onMenu?.Invoke(context);

    public void OnNavigate(InputAction.CallbackContext context) { }

    public Action<InputAction.CallbackContext> onSubmit;
    public void OnSubmit(InputAction.CallbackContext context) => onSubmit?.Invoke(context);

    public Action<InputAction.CallbackContext> onMENUCancel;
    public void OnCancel(InputAction.CallbackContext context) => onMENUCancel?.Invoke(context);

    public void OnPoint(InputAction.CallbackContext context) { }

    public void OnClick(InputAction.CallbackContext context) { }

    public void OnScrollWheel(InputAction.CallbackContext context) { }

    public void OnMiddleClick(InputAction.CallbackContext context) { }

    public void OnRightClick(InputAction.CallbackContext context) { }

    public void OnTrackedDevicePosition(InputAction.CallbackContext context) { }

    public void OnTrackedDeviceOrientation(InputAction.CallbackContext context) { }

    public void OnGraffiti1(InputAction.CallbackContext context) { }

    public void OnGraffiti2(InputAction.CallbackContext context) { }

    public void OnGraffiti3(InputAction.CallbackContext context) { }

    public void OnGraffiti4(InputAction.CallbackContext context) { }

    public void OnGraffiti5(InputAction.CallbackContext context) { }

    // UI_TOGGLE — 지도/노트/도감/인터넷 패널을 여닫는 단축키. 각 패널이 직접 구독해서 Toggle()을 호출한다
    // (MapViewer/NotePanel/CodexPanel/InternetPanel — 이전엔 이 패널들이 각자 Input.GetKeyDown으로 직접 폴링했다).
    public Action<InputAction.CallbackContext> onOpenMap;
    public void OnOpenMap(InputAction.CallbackContext context) => onOpenMap?.Invoke(context);

    public Action<InputAction.CallbackContext> onOpenNote;
    public void OnOpenNote(InputAction.CallbackContext context) => onOpenNote?.Invoke(context);

    public Action<InputAction.CallbackContext> onOpenCodex;
    public void OnOpenCodex(InputAction.CallbackContext context) => onOpenCodex?.Invoke(context);

    public Action<InputAction.CallbackContext> onOpenInternet;
    public void OnOpenInternet(InputAction.CallbackContext context) => onOpenInternet?.Invoke(context);
}
