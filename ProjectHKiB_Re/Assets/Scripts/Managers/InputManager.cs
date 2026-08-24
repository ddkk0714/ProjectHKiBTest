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
        inputs.UI_TOGGLE.Enable(); // 지도/노트/도감/인터넷 토글 — PLAY/MENU/GRAFFITI 사이에서는 계속 켜져 있고, CUTSCENEMode에서만 닫힌다.
        PLAYMode();
    }

    /// <summary>
    /// StateMachine 자산이 들고 있는 InputActionReference는 프로젝트의 원본 입력 자산을
    /// 가리킨다. 실제 플레이 중에는 이 컴포넌트가 만든 <see cref="inputs"/> 인스턴스만
    /// Enable/Disable되므로, 상태 전이에서는 반드시 그 런타임 인스턴스의 같은 이름 액션을
    /// 사용해야 한다.
    /// </summary>
    public InputAction GetRuntimeAction(InputActionReference reference)
    {
        if (reference == null || reference.action == null || inputs == null) return null;
        return inputs.asset.FindAction(reference.action.name, throwIfNotFound: false);
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
            // EventInputTrigger("말 걸기"/상호작용용)가 OnConfirm으로 흔히 배선되는데, 이 switch에
            // 없어 항상 false만 돌려주고 있었다 — 그 트리거는 절대 안 켜지는 채로 조용히 죽어 있었다.
            EnumManager.InputType.OnConfirm => ConfirmInput,
            _ => false,
        };
    }

    public void PLAYMode()
    {
        inputs.PLAY.Enable();
        inputs.MENU.Disable();
        inputs.GRAFFITI.Disable();
        inputs.UI_TOGGLE.Enable();
        ReleaseCutscenePause();
        //Debug.Log("PLAYMode");
    }

    public void MENUMode()
    {
        inputs.PLAY.Disable();
        inputs.MENU.Enable();
        inputs.GRAFFITI.Disable();
        inputs.UI_TOGGLE.Enable();
        // 대화(DialogueStartAction)는 컷신 도중에도 이 메서드를 부른다 — 대사 타이핑
        // 트윈(DOTween, 스케일 시간)이 돌아야 하므로 컷신 정지를 여기서 풀어준다.
        // 다시 잠그는 건 그 다음 State가 SetInputModeAction(Cutscene)으로 한다.
        ReleaseCutscenePause();
        //Debug.Log("MENUMode");
    }

    public void GRAFFITIMode()
    {
        inputs.PLAY.Disable();
        inputs.MENU.Disable();
        inputs.GRAFFITI.Enable();
        inputs.UI_TOGGLE.Enable();
        ReleaseCutscenePause();
        //Debug.Log("GRAFFITIMode");
    }

    // 컷신 전용 — 이동/공격/메뉴/낙서는 물론 UI 토글(지도·노트·도감·인터넷)까지 전부 닫는다.
    // MENUMode로 대신할 수 없는 이유가 이것으로, 강제 연출 도중에는 메뉴를 여는 것도 막아야 한다.
    //
    // 액션맵을 끄는 것만으로는 마지막에 눌려 있던 값이 그대로 남아 캐릭터가 계속 걸어가므로
    // 직접 비운다. 모드에서 빠져나올 때는 PLAYMode()를 부르면 UI 토글까지 같이 복구된다.
    public void CUTSCENEMode() => EnterCutscene(pauseTime: true);

    /// <summary>
    /// 입력만 잠그고 시간은 그대로 흐르는 컷신. 물리가 돌아야 하는 연출에 쓴다.
    ///
    /// 넉백(IPhysics.KnockBack)은 ExForce에 힘을 더할 뿐이고, 실제로 밀어내는 건
    /// PhysicsManager.FixedUpdate다. 그런데 그 FixedUpdate는 매 틱 ExForce를 0으로 비운다 —
    /// Time.timeScale이 0이면 FixedUpdate 자체가 안 돌아 힘이 쌓인 채 아무 일도 일어나지 않는다.
    /// 그래서 넉백·낙하처럼 물리로 표현되는 연출 단계는 이 모드로 감싼다.
    /// </summary>
    public void CUTSCENELIVEMode() => EnterCutscene(pauseTime: false);

    // 두 컷신 모드의 공통 부분 — 조작을 완전히 잠근다.
    //
    // 액션맵을 끄는 것만으로는 부족하다. Walk/Run 스테이트의 발소리·연출은
    // StateController.StartActionSequence(DOTween, 기본 UpdateType.Normal = 스케일 시간)로 도는
    // 반복 시퀀스라, 입력을 막아도 시퀀스 자체는 계속 돈다. 그래서 정지형 컷신은 메뉴 창과 같은
    // 수단(TimeManager.Pause)으로 게임플레이 시간을 통째로 세운다.
    private void EnterCutscene(bool pauseTime)
    {
        inputs.PLAY.Disable();
        inputs.MENU.Disable();
        inputs.GRAFFITI.Disable();
        inputs.UI_TOGGLE.Disable();

        MoveInput = Vector2.zero;
        LastSetMoveInput = Vector2.zero;
        MoveInputPressed = false;
        ConfirmInput = false;

        // 입력을 끊는 것만으로는 이미 실려 있는 속도가 사라지지 않는다. 달려 들어와 컷신이 걸리면
        // 플레이어는 그 속도를 그대로 안고 있다가, 정지형 컷신에서는 마찰이 돌 기회조차 없어
        // (timeScale 0 → FixedUpdate 정지) 컷신이 풀리는 순간 앞으로 튀어나간다. 걸어 들어왔을
        // 때와 컷신 구간의 체감이 달라지는 것도 여기서 온다 — 들어온 속도가 그대로 남기 때문이다.
        StopPlayerMovement();

        if (!pauseTime)
        {
            // 시간이 흐르는 컷신에서는 카메라도 평소대로 돌면 된다.
            ReleaseCutscenePause();
            return;
        }

        GameManager.instance?.timeManager?.Pause(TimeManager.ReasonCutscene);

        // 게임플레이를 멈춘 동안에도 카메라 연출(클로즈업 줌 등)은 흘러야 한다.
        if (CameraManager.instance) CameraManager.instance.SetCutsceneCameraMode(true);
    }

    // 컷신에 들어가는 순간 플레이어를 그 자리에 세운다. 진행 중인 넉백은 건드리지 않는다 —
    // 넉백 연출(CutsceneLive)은 밀려나는 것 자체가 목적이라 여기서 지우면 연출이 사라진다.
    private void StopPlayerMovement()
    {
        Player player = GameManager.instance ? GameManager.instance.player : null;
        if (!player || !player.TryGetInterface(out IPhysics physics) || physics.IsKnockedBack) return;

        physics.HVelocity = Vector2.zero;
        physics.ExForce = Vector3.zero;
        physics.WalkingVel = Vector2.zero;
        physics.IsWalking = false;
        physics.IsSprinting = false;
    }

    // PLAY/MENU/GRAFFITI 중 하나로 전환한다는 것은 "더 이상 순수 컷신 잠금 구간이 아니다"라는
    // 뜻이라 여기서 공통으로 푼다. TimeManager.Resume은 그 사유가 없으면 조용히 no-op이라
    // 컷신이 아니었던 전환(예: 평상시 메뉴 열기)에서 불러도 안전하다.
    private void ReleaseCutscenePause()
    {
        GameManager.instance?.timeManager?.Resume(TimeManager.ReasonCutscene);
        if (CameraManager.instance) CameraManager.instance.SetCutsceneCameraMode(false);
    }

    public void SetInputMode(EnumManager.InputMode mode)
    {
        switch (mode)
        {
            case EnumManager.InputMode.Play: PLAYMode(); break;
            case EnumManager.InputMode.Menu: MENUMode(); break;
            case EnumManager.InputMode.Graffiti: GRAFFITIMode(); break;
            case EnumManager.InputMode.Cutscene: CUTSCENEMode(); break;
            case EnumManager.InputMode.CutsceneLive: CUTSCENELIVEMode(); break;
        }
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
