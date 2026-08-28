using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// 트리거가 이벤트를 실행할 때 전달하는 공통 정보입니다.
/// 감지 방식과 무관하게 대상, 콜라이더, 공격 정보를 같은 형태로 제공합니다.
/// </summary>
public sealed class EventTriggerContext
{
    public EventTriggerBase Trigger { get; }
    public GameObject Target { get; }
    public Collider2D TargetCollider { get; }
    public EventAttackContext Attack { get; }
    public bool IsAttack => Attack != null;

    /// <summary>
    /// 트리거 실행 시점의 감지 정보를 보관합니다.
    /// 사용하지 않는 정보는 null로 전달할 수 있습니다.
    /// </summary>
    public EventTriggerContext(
        EventTriggerBase trigger,
        GameObject target = null,
        Collider2D targetCollider = null,
        EventAttackContext attack = null)
    {
        Trigger = trigger;
        Target = target;
        TargetCollider = targetCollider;
        Attack = attack;
    }
}

/// <summary>
/// 모든 이벤트 트리거의 실행 정책을 담당하는 공통 기반 클래스입니다.
/// 구체 클래스는 감지만 수행하고 실제 실행, 쿨타임, Chunk 판정은 이 클래스에 위임합니다.
/// </summary>
public abstract class EventTriggerBase : MonoBehaviour
{
    [Tooltip("발동할 이벤트입니다. 비워 두면 같은 오브젝트와 부모에서 자동으로 찾습니다.")]
    [SerializeField, FormerlySerializedAs("gameEvent"), FormerlySerializedAs("_event")]
    private GameEvent _gameEvent;

    [Tooltip("이 트리거가 속한 Chunk입니다. 비워 두면 부모에서 자동으로 찾습니다.")]
    [SerializeField, FormerlySerializedAs("chunk")]
    private ChunkData _chunk;

    [Tooltip("한 번 발동한 뒤 다시 발동할 수 있을 때까지의 시간입니다.")]
    [SerializeField, Min(0f), FormerlySerializedAs("cooldown"), FormerlySerializedAs("_cooltime")]
    private float _cooldown;

    [Tooltip("켜면 ResetTrigger를 호출하기 전까지 한 번만 발동합니다.")]
    [SerializeField, FormerlySerializedAs("triggerOnce")]
    private bool _triggerOnce;

    private float _nextTriggerTime;
    private bool _hasTriggered;

    public event Action<EventTriggerContext> Triggered;
    public EventTriggerContext LastContext { get; private set; }
    public GameEvent GameEvent => _gameEvent;

    /// <summary>
    /// 누락된 이벤트와 Chunk 참조를 같은 오브젝트 또는 부모에서 보완합니다.
    /// 구체 트리거가 Awake를 재정의하면 반드시 base.Awake를 호출해야 합니다.
    /// </summary>
    protected virtual void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// 외부 시스템이 감지 없이 트리거를 직접 실행할 때 사용합니다.
    /// 공통 실행 제한을 그대로 적용하고 성공 여부를 반환합니다.
    /// </summary>
    public bool TriggerManually(GameObject target = null)
    {
        return TryTrigger(new EventTriggerContext(this, target));
    }

    /// <summary>
    /// 1회 실행 상태와 쿨타임을 초기화합니다.
    /// 재사용 가능한 트리거를 명시적으로 다시 무장할 때 사용합니다.
    /// </summary>
    [NaughtyAttributes.Button("Reset Trigger")]
    public void ResetTrigger()
    {
        _hasTriggered = false;
        _nextTriggerTime = 0f;
    }

    /// <summary>
    /// ChunkData의 자동 배선에서 소유 Chunk를 지정합니다.
    /// 런타임과 에디터 양쪽에서 같은 활성 조건을 사용하게 합니다.
    /// </summary>
    public void SetChunkData(ChunkData chunkData)
    {
        _chunk = chunkData;
    }

    /// <summary>
    /// 이벤트 생성 도구가 트리거와 GameEvent를 명시적으로 연결할 때 사용합니다.
    /// 참조가 없을 때의 자동 탐색도 계속 지원합니다.
    /// </summary>
    public void SetGameEvent(GameEvent gameEvent)
    {
        _gameEvent = gameEvent;
    }

    /// <summary>
    /// 모든 공통 제한을 확인한 뒤 이벤트와 후속 콜백을 한 번 실행합니다.
    /// 구체 트리거는 감지에 성공했을 때 이 메서드만 호출합니다.
    /// </summary>
    protected bool TryTrigger(EventTriggerContext context)
    {
        if (!isActiveAndEnabled || !IsAvailableInCurrentChunk()) return false;
        if (_triggerOnce && _hasTriggered) return false;
        if (Time.time < _nextTriggerTime) return false;

        _hasTriggered = true;
        _nextTriggerTime = Time.time + _cooldown;
        LastContext = context ?? new EventTriggerContext(this);

        Triggered?.Invoke(LastContext);
        if (_gameEvent) _gameEvent.TriggerEvent();
        return true;
    }

    /// <summary>
    /// Chunk가 없거나 현재 활성 상태인지 확인합니다.
    /// 모든 감지 방식이 동일한 Chunk 정책을 사용하도록 공통화합니다.
    /// </summary>
    protected bool IsAvailableInCurrentChunk()
    {
        return !_chunk || _chunk.Active;
    }

    /// <summary>
    /// 이벤트와 Chunk 참조를 안전한 범위에서 자동으로 찾습니다.
    /// 씬을 넘는 참조는 만들지 않고 현재 계층만 검사합니다.
    /// </summary>
    private void ResolveReferences()
    {
        if (!_gameEvent) _gameEvent = GetComponent<GameEvent>();
        if (!_gameEvent) _gameEvent = GetComponentInParent<GameEvent>();
        if (!_chunk) _chunk = GetComponentInParent<ChunkData>();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 인스펙터 편집 중 자동 참조를 보완하고 잘못된 구성을 즉시 알립니다.
    /// UnityEditor 의존 없이 런타임 빌드에서도 같은 스크립트를 사용할 수 있습니다.
    /// </summary>
    protected virtual void OnValidate()
    {
        ResolveReferences();
        if (!_gameEvent)
            Debug.LogWarning("발동할 GameEvent를 찾을 수 없습니다.", this);
    }
#endif
}
