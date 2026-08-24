using UnityEngine;

// 지정 입력을 일정 시간 "누르고 있어야" 발동하는 트리거.
// EVT-006의 "날개 앞으로 이동해 인터랙션 키를 눌러 잘라낸다(홀딩)"가 이 모양이다.
//
// EventInputTrigger는 누른 순간 바로 발동하므로 홀딩을 표현할 수 없다. 여기서는 범위 안에 있고
// 입력이 유지되는 동안 시간을 모으고, 도중에 손을 떼거나 범위를 벗어나면 처음부터 다시 센다.
public class EventHoldInputTrigger : GameEventTrigger
{
    [SerializeField] private EnumManager.InputType _inputType;
    [SerializeField][Min(0f)] private float _holdTime = 1f;

    // 지금까지 모인 홀딩 시간 — UI 게이지를 붙이고 싶으면 이 비율을 읽으면 된다.
    public float HoldProgress => _holdTime <= 0f ? 1f : Mathf.Clamp01(_heldTime / _holdTime);

    private float _heldTime;

    public override void UpdateTrigger()
    {
        int length = _collider2D.OverlapCollider(_contactFilter, colliders);
        bool inRange = length > 0;
        bool holding = inRange && GameManager.instance.inputManager.GetInputByEnum(_inputType);

        if (!holding)
        {
            _heldTime = 0f;
            return;
        }

        if (!_canTrigger) return;

        // 컷신 중 게임이 멈춰도 홀딩은 흘러야 하므로 unscaled를 쓴다.
        _heldTime += Time.unscaledDeltaTime;
        if (_heldTime < _holdTime) return;

        _heldTime = 0f;
        Event.TriggerEvent();
        CoolTime();
    }
}
