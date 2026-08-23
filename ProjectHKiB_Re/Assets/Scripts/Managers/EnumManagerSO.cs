
using UnityEngine;
public class EnumManager
{
    public enum AnimDir
    {
        D, R, L, U
    }

    public enum InputType
    {
        OnMove,
        OnSprint,
        OnAttack,
        OnDodge,
        HasDodge,
        HasDInput,
        HasLInput,
        HasRInput,
        HasUInput,
        OnConfirm,
        None,
        OnSubmit,
        OnSkill,
        OnGraffiti,
        OnGraffitiMoveDown,
        OnGraffitiMoveLeft,
        OnGraffitiMoveRight,
        OnGraffitiMoveUp,
        OnGraffitiAttack,
        OnGraffitiSkill,
        OnGraffitiCancel,
        OnGraffitiReset,
        HasAttack,
        HasSkill
    }

    public enum CompareType
    {
        SameAs,
        BiggerThan,
        BiggerOrSameAs,
        SmallerThan,
        SmallerOrSameAs,
        NotSame
    }

    // 입력 "종류"(InputType)가 아니라 입력 "모드" — InputManager의 액션맵 묶음 전환용.
    // Cutscene은 PLAY/MENU/GRAFFITI에 더해 UI 토글(지도·노트·도감·인터넷)까지 닫는다.
    public enum InputMode
    {
        Play,
        Menu,
        Graffiti,
        Cutscene,

        // 입력만 잠그고 시간은 그대로 흐르는 컷신. 물리가 돌아야 하는 연출(넉백·낙하·이동)에 쓴다 —
        // Cutscene은 Time.timeScale을 0으로 만들어 FixedUpdate를 멈추므로 그런 연출이 아예 일어나지 않는다.
        CutsceneLive,
    }

    public enum InputActionType
    {
        Performed,
        Started,
        Canceled,
    }

    public enum InputProcessType
    {
        InProgress,
        Triggered,
        Enabled,
        WasPerformedThisFrame,
        WasPressedThisFrame,
        WasReleasedThisFrame
    }
}
