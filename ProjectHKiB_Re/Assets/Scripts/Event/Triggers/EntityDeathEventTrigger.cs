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

    // 켜면 _owner를 무시하고 **플레이어**의 사망을 지켜본다.
    //
    // 사망 복귀 이벤트는 이 오브젝트(트리거 프리팹)가 아니라 플레이어가 죽었을 때 떠야 하는데,
    // 플레이어는 System 씬에 상주하고 트리거 프리팹은 맵 씬에 놓이므로 인스펙터에서 서로를
    // 참조하도록 끌어다 놓을 수가 없다(씬을 넘는 참조는 저장되지 않는다). 그래서 이름으로 찾는
    // 대신 GameManager를 통해 플레이를 시작한 뒤 스스로 묶는다.
    [SerializeField] private bool _watchPlayer;

    private IDamagable _damagable;

    private void Start()
    {
        if (_watchPlayer)
        {
            StartCoroutine(BindPlayerWhenReady());
            return;
        }

        if (!_owner) _owner = GetComponent<InterfaceRegister>();
        if (!_owner || !_owner.TryGetInterface(out _damagable))
        {
            Debug.LogError($"ERROR: EntityDeathEventTrigger - '{name}'에서 IDamagable을 찾을 수 없습니다.");
            return;
        }

        _damagable.OnDie += HandleDeath;
    }

    // 맵 씬이 System 씬보다 먼저 뜰 수도 있어 Start 시점에 플레이어가 없을 수 있다.
    // 준비될 때까지 기다렸다가 묶는다.
    private System.Collections.IEnumerator BindPlayerWhenReady()
    {
        const float timeoutSeconds = 10f;
        float waited = 0f;

        while (waited < timeoutSeconds)
        {
            Player player = GameManager.instance != null ? GameManager.instance.player : null;
            if (player != null && player.TryGetInterface(out _damagable))
            {
                _damagable.OnDie += HandleDeath;
                yield break;
            }

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogError($"ERROR: EntityDeathEventTrigger - '{name}'이 플레이어의 IDamagable을 찾지 못했습니다. " +
                       "사망해도 이 이벤트가 발동하지 않습니다.");
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
