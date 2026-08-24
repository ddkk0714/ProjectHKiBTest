using UnityEngine;

// 범위 안에서 지정 입력을 "새로" 눌렀을 때 발동하는 트리거 — "말을 건다/상호작용"류.
//
// [왜 눌림 상태만 보면 안 되나] InputManager.GetInputByEnum이 돌려주는 값은 대부분 눌려 있는
// 동안 계속 참인 래치다(예: OnConfirm → ConfirmInput). 그대로 쓰면 직전 이벤트에서 눌렸거나
// 컷신 동안 누르고 있던 입력이, 컷신이 끝나 액션맵이 다시 켜지는 순간 그대로 참으로 읽혀서
// 다음 이벤트가 즉시 연달아 터진다(EVT-001이 끝나자마자 EVT-002가 발동하던 문제).
//
// 그래서 "한 번 떼는 걸 본 뒤에 다시 눌렀을 때"만 발동하도록 무장(arm) 상태를 둔다. 이렇게 하면
// 넘겨받은 입력으로는 절대 안 켜지고, 플레이어가 실제로 새로 누른 것만 인정된다.
public class EventInputTrigger : GameEventTrigger
{
    [SerializeField] private EnumManager.InputType _inputType;

    // 입력이 떨어진 걸 한 번이라도 본 뒤에야 참이 된다. 시작 시 false라, 처음부터 누르고 있었다면
    // 떼기 전까지는 발동하지 않는다.
    private bool _armed;

    public override void UpdateTrigger()
    {
        bool pressed = GameManager.instance.inputManager.GetInputByEnum(_inputType);

        if (!pressed)
        {
            _armed = true;
            return;
        }

        if (!_armed || !_canTrigger) return;

        // 범위 판정은 실제로 발동시킬 수 있을 때만 한다 — OverlapCollider가 매 FixedUpdate 도는
        // 물리 질의라, 누르지도 않은 프레임까지 돌릴 이유가 없다.
        if (_collider2D.OverlapCollider(_contactFilter, colliders) <= 0) return;

        _armed = false;
        Event.TriggerEvent();
        CoolTime();
    }
}
