// 범위 안에 "들어오는 순간" 한 번 발동하는 트리거 — "접근하면"류.
//
// [왜 겹침만 보면 안 되나] UpdateTrigger는 FixedUpdate마다 도는데, GameEventTrigger.CoolTime()은
// _cooltime이 0이면 곧바로 다시 발동 가능 상태로 되돌린다. 그래서 겹쳐 있기만 하면 매 물리 틱마다
// 이벤트가 다시 시작됐다 — 한 프레임에 FixedUpdate가 여러 번 몰리면 같은 이벤트가 연속으로
// 두 번 이상 뜬다(EVT-001이 두 번 시작되던 문제).
//
// 그래서 "범위 밖으로 나간 걸 본 뒤에야 다시 발동할 수 있다"는 무장(arm) 상태를 둔다. 들어와 있는
// 동안은 한 번만 발동하고, 나갔다 다시 들어와야 또 발동한다.
public class EventStayTrigger : GameEventTrigger
{
    // 처음엔 무장 상태로 시작한다 — 시작하자마자 범위 안에 있는 배치라면 그 자리에서 발동하는 게
    // "접근하면"의 자연스러운 해석이라서다. 발동 뒤에는 범위를 벗어나야 다시 무장된다.
    private bool _armed = true;

    public override void UpdateTrigger()
    {
        bool inRange = _collider2D.OverlapCollider(_contactFilter, colliders) > 0;

        if (!inRange)
        {
            _armed = true;
            return;
        }

        if (!_armed || !_canTrigger) return;

        _armed = false;
        Event.TriggerEvent();
        CoolTime();
    }
}
