using System.Collections;
using UnityEngine;

/// <summary>
/// 지정 엔티티 또는 플레이어의 IDamagable.OnDie 콜백을 이벤트로 변환합니다.
/// 콜라이더를 사용하지 않고 사망 이후의 처리 정책은 연결된 GameEvent에 위임합니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Entity Death Event Trigger")]
public sealed class EntityDeathEventTrigger : EventTriggerBase
{
    [Tooltip("사망을 감시할 엔티티의 인터페이스 등록기입니다. 플레이어 감시 시에는 사용하지 않습니다.")]
    [SerializeField]
    [NaughtyAttributes.HideIf(nameof(_watchPlayer))]
    private InterfaceRegister _owner;

    [Tooltip("켜면 Scene 참조 대신 GameManager를 통해 플레이어의 사망을 감시합니다.")]
    [SerializeField]
    private bool _watchPlayer;

    [Tooltip("플레이어가 준비되기를 기다릴 최대 시간입니다.")]
    [SerializeField, Min(0.1f)]
    [NaughtyAttributes.ShowIf(nameof(_watchPlayer))]
    private float _playerBindTimeout = 10f;

    private IDamagable _damagable;

    /// <summary>
    /// 감시 대상 종류에 따라 로컬 엔티티를 즉시 연결하거나 플레이어 준비를 기다립니다.
    /// 연결 실패 시 이벤트가 조용히 누락되지 않도록 오류를 남깁니다.
    /// </summary>
    private void Start()
    {
        if (_watchPlayer)
        {
            StartCoroutine(BindPlayerWhenReady());
            return;
        }

        BindLocalOwner();
    }

    /// <summary>
    /// 같은 오브젝트의 InterfaceRegister에서 IDamagable을 찾아 사망 콜백을 구독합니다.
    /// 명시된 owner가 있으면 해당 참조를 우선 사용합니다.
    /// </summary>
    private void BindLocalOwner()
    {
        if (!_owner) _owner = GetComponent<InterfaceRegister>();
        if (!_owner || !_owner.TryGetInterface(out _damagable))
        {
            Debug.LogError($"[EntityDeathEventTrigger] '{name}'에서 IDamagable을 찾을 수 없습니다.", this);
            return;
        }

        _damagable.OnDie += HandleDeath;
    }

    /// <summary>
    /// System Scene의 플레이어가 준비될 때까지 unscaled 시간으로 기다린 뒤 사망 콜백을 구독합니다.
    /// 제한 시간 안에 찾지 못하면 명시적인 오류를 남깁니다.
    /// </summary>
    private IEnumerator BindPlayerWhenReady()
    {
        float waited = 0f;
        while (waited < _playerBindTimeout)
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

        Debug.LogError(
            $"[EntityDeathEventTrigger] '{name}'이 플레이어의 IDamagable을 찾지 못했습니다.",
            this);
    }

    /// <summary>
    /// 오브젝트가 파괴될 때 구독한 IDamagable 콜백을 안전하게 해제합니다.
    /// Scene 전환 뒤 파괴된 트리거로 콜백이 전달되는 것을 막습니다.
    /// </summary>
    private void OnDestroy()
    {
        if (_damagable != null) _damagable.OnDie -= HandleDeath;
    }

    /// <summary>
    /// 사망 콜백을 공통 실행 정책에 전달합니다.
    /// 연결된 GameEvent의 부활, 맵 이동, 진행도 정책은 이 클래스가 결정하지 않습니다.
    /// </summary>
    private void HandleDeath()
    {
        TryTrigger(new EventTriggerContext(this, (_damagable as Component)?.gameObject));
    }

#if UNITY_EDITOR
    /// <summary>
    /// 로컬 감시 모드에서 InterfaceRegister 참조를 자동으로 채웁니다.
    /// 플레이어 감시 모드에서는 Scene 간 참조를 만들지 않습니다.
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();
        if (!_watchPlayer && !_owner) _owner = GetComponent<InterfaceRegister>();
    }
#endif
}
