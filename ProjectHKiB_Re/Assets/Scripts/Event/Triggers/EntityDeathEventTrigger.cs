using UnityEngine;

// 이 오브젝트가 죽는 순간 지정한 이벤트를 발동시킨다 — EVT-004/006의 "플레이어 사망 시 강제 복귀"
// 같은 중단 조건을 이벤트로 표현할 수 있게 해주는 다리.
//
// 다른 트리거들(EventStayTrigger 등)과 달리 GameEventTrigger를 상속하지 않는다. 그쪽은 콜라이더
// 겹침을 매 FixedUpdate 검사하는 구조라 콜라이더가 반드시 있어야 하는데, 사망은 물리와 무관한
// 순수 콜백 사건이기 때문이다.
//
// [무엇을 하지 않는가] 죽은 뒤 무엇을 할지는 정하지 않는다. 되살릴지, 어느 맵으로 보낼지,
// 진행도를 어디까지 되돌릴지는 전부 연결된 이벤트(EventSO)가 결정한다. 리스폰 정책을 코드에
// 박지 않으려고 일부러 이렇게 갈랐다.
//
// ※ DamagableModule.Die()는 이 콜백을 부른 "뒤"가 아니라 gameObject.SetActive(false) "뒤"에
//   OnDie를 부른다. 즉 이벤트가 시작될 때 이 오브젝트는 이미 꺼져 있다. 되살리려면 이벤트 쪽에서
//   ReviveEntityAction을 쓸 것.
public class EntityDeathEventTrigger : MonoBehaviour
{
    [SerializeField] private GameEvent _event;
    [SerializeField] private InterfaceRegister _owner;

    private IDamagable _damagable;

    private void Start()
    {
        if (!_owner) _owner = GetComponent<InterfaceRegister>();
        if (!_owner || !_owner.TryGetInterface(out _damagable))
        {
            Debug.LogError($"ERROR: EntityDeathEventTrigger - '{name}'에서 IDamagable을 찾을 수 없습니다.");
            return;
        }

        _damagable.OnDie += HandleDeath;
    }

    private void OnDestroy()
    {
        if (_damagable != null) _damagable.OnDie -= HandleDeath;
    }

    private void HandleDeath()
    {
        if (!_event)
        {
            Debug.LogWarning($"[EntityDeathEventTrigger] '{name}'이 죽었지만 연결된 이벤트가 없습니다.");
            return;
        }

        _event.TriggerEvent();
    }
}
