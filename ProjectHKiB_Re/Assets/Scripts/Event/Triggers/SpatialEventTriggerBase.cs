using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum EventTriggerTargetScope
{
    ColliderObject,
    RigidbodyOrColliderObject,
    RootObject
}

public enum EventTriggerNameMatch
{
    Exact,
    Contains,
    StartsWith
}

/// <summary>
/// ZCollider2D 영역을 사용하는 트리거의 감지와 대상 필터를 공통화합니다.
/// 영역 트리거와 상호작용 트리거가 동일한 대상 해석 규칙을 공유합니다.
/// </summary>
public abstract class SpatialEventTriggerBase : EventTriggerBase
{
    [Tooltip("감지할 대상 레이어입니다.")]
    [SerializeField, FormerlySerializedAs("_layerMask")]
    private LayerMask _targetLayers = ~0;

    [Tooltip("비워 두면 대상 이름을 검사하지 않습니다.")]
    [SerializeField]
    private string _requiredTargetName;

    [Tooltip("대상 이름을 비교할 방법입니다.")]
    [SerializeField]
    private EventTriggerNameMatch _targetNameMatch = EventTriggerNameMatch.Exact;

    [Tooltip("대상 이름 비교에서 대소문자를 무시할지 결정합니다.")]
    [SerializeField]
    private bool _ignoreTargetNameCase;

    [Tooltip("영역 판정에 사용할 프로젝트 ZCollider2D입니다.")]
    [SerializeField, FormerlySerializedAs("areaCollider")]
    [NaughtyAttributes.Required]
    private ZCollider2D _areaCollider;

    // 구형 EventInput/Stay 계열은 일반 Collider2D를 _collider2D에 저장했다.
    // 호환 래퍼에서만 이 값을 사용한다. 새 트리거는 Reset과 빌드 Validator가
    // ZCollider2D 구성을 보장하므로, 상속되는 RequireComponent를 사용하지 않는다.
    [SerializeField, HideInInspector, FormerlySerializedAs("_collider2D")]
    private Collider2D _legacyAreaCollider;

    [Tooltip("콜라이더가 감지됐을 때 실제 이벤트 대상으로 사용할 오브젝트 범위입니다.")]
    [SerializeField, FormerlySerializedAs("targetScope")]
    private EventTriggerTargetScope _targetScope = EventTriggerTargetScope.RigidbodyOrColliderObject;

    private readonly List<Collider2D> _overlapResults = new(16);
    private readonly Dictionary<int, SpatialTargetRecord> _currentTargets = new();
    private ContactFilter2D _contactFilter;

#if UNITY_EDITOR
    [NonSerialized]
    private bool _colliderValidationScheduled;
#endif

    protected IReadOnlyDictionary<int, SpatialTargetRecord> CurrentTargets => _currentTargets;
    protected virtual bool SupportsLegacyAreaCollider => false;

    /// <summary>
    /// 감지된 이벤트 대상과 실제 충돌 콜라이더를 함께 보관합니다.
    /// 복수 콜라이더를 가진 대상은 인스턴스 ID 기준으로 하나만 유지됩니다.
    /// </summary>
    protected sealed class SpatialTargetRecord
    {
        public GameObject Target { get; }
        public Collider2D Collider { get; }

        /// <summary>
        /// 대상과 감지에 사용된 콜라이더를 한 레코드로 묶습니다.
        /// 실행 Context 생성 시 두 참조를 그대로 전달합니다.
        /// </summary>
        public SpatialTargetRecord(GameObject target, Collider2D collider)
        {
            Target = target;
            Collider = collider;
        }
    }

    /// <summary>
    /// 공통 참조를 찾고 물리 필터를 현재 레이어 설정으로 초기화합니다.
    /// 이후 물리 프레임마다 같은 필터를 재사용합니다.
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
        if (!_areaCollider) _areaCollider = GetComponent<ZCollider2D>();
        if (SupportsLegacyAreaCollider && !_legacyAreaCollider)
            _legacyAreaCollider = GetComponent<Collider2D>();
        RebuildContactFilter();
    }

    /// <summary>
    /// 현재 ZCollider2D와 겹치는 유효 대상을 다시 수집합니다.
    /// 비활성 Chunk이거나 콜라이더가 없으면 빈 결과를 유지합니다.
    /// </summary>
    protected void CollectCurrentTargets()
    {
        _currentTargets.Clear();
        _overlapResults.Clear();

        if (!IsAvailableInCurrentChunk()) return;

        if (_areaCollider)
            _areaCollider.OverlapCollider(_contactFilter, _overlapResults);
        else if (SupportsLegacyAreaCollider && _legacyAreaCollider)
            _legacyAreaCollider.OverlapCollider(_contactFilter, _overlapResults);
        else
            return;
        for (int i = 0; i < _overlapResults.Count; i++)
        {
            Collider2D candidateCollider = _overlapResults[i];
            if (!candidateCollider) continue;

            GameObject candidate = ResolveTarget(candidateCollider);
            if (!MatchesTarget(candidate)) continue;

            int instanceId = candidate.GetInstanceID();
            if (!_currentTargets.ContainsKey(instanceId))
                _currentTargets.Add(instanceId, new SpatialTargetRecord(candidate, candidateCollider));
        }
    }

    /// <summary>
    /// 비활성화와 재활성화 사이에 남은 영역 대상을 제거합니다.
    /// 다음 물리 프레임 전에 오래된 대상으로 입력이 발동하는 것을 막습니다.
    /// </summary>
    protected void ClearCurrentTargets()
    {
        _currentTargets.Clear();
        _overlapResults.Clear();
    }

    /// <summary>
    /// 대상 레이어와 선택적 이름 필터를 검사합니다.
    /// 영역 및 상호작용 트리거가 같은 필터 의미를 갖게 합니다.
    /// </summary>
    private bool MatchesTarget(GameObject target)
    {
        if (!target || (_targetLayers.value & (1 << target.layer)) == 0) return false;
        if (string.IsNullOrEmpty(_requiredTargetName)) return true;

        StringComparison comparison = _ignoreTargetNameCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return _targetNameMatch switch
        {
            EventTriggerNameMatch.Contains => target.name.IndexOf(_requiredTargetName, comparison) >= 0,
            EventTriggerNameMatch.StartsWith => target.name.StartsWith(_requiredTargetName, comparison),
            _ => string.Equals(target.name, _requiredTargetName, comparison),
        };
    }

    /// <summary>
    /// 설정된 대상 범위에 따라 콜라이더를 대표할 GameObject를 결정합니다.
    /// 복합 콜라이더 캐릭터도 하나의 대상으로 합칠 수 있습니다.
    /// </summary>
    private GameObject ResolveTarget(Collider2D candidate)
    {
        return _targetScope switch
        {
            EventTriggerTargetScope.RootObject => candidate.transform.root.gameObject,
            EventTriggerTargetScope.RigidbodyOrColliderObject => candidate.attachedRigidbody
                ? candidate.attachedRigidbody.gameObject
                : candidate.gameObject,
            _ => candidate.gameObject,
        };
    }

    /// <summary>
    /// 현재 레이어 설정으로 재사용 가능한 ContactFilter2D를 구성합니다.
    /// Trigger Collider도 이벤트 영역 대상으로 포함합니다.
    /// </summary>
    private void RebuildContactFilter()
    {
        _contactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = _targetLayers,
            useTriggers = true
        };
    }

#if UNITY_EDITOR
    /// <summary>
    /// 새 공간 트리거를 직접 추가할 때 기본 원형 감지 영역을 구성합니다.
    /// 구형 호환 래퍼는 기존 Box/Capsule Collider를 유지해야 하므로 자동 생성하지 않습니다.
    /// </summary>
    protected virtual void Reset()
    {
        if (SupportsLegacyAreaCollider)
        {
            _legacyAreaCollider = GetComponent<Collider2D>();
        }
        else
        {
            _areaCollider = GetComponent<ZCollider2D>();
            if (!_areaCollider)
                _areaCollider = UnityEditor.Undo.AddComponent<ZCircleCollider2D>(gameObject);
        }

        RebuildContactFilter();
    }

    /// <summary>
    /// 인스펙터 변경 시 콜라이더 참조와 물리 필터를 즉시 갱신합니다.
    /// 필수 콜라이더가 없으면 편집 단계에서 경고합니다.
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();
        if (!_areaCollider) _areaCollider = GetComponent<ZCollider2D>();
        if (SupportsLegacyAreaCollider && !_legacyAreaCollider)
            _legacyAreaCollider = GetComponent<Collider2D>();
        RebuildContactFilter();

        // AddComponent/스크립트 리로드 중에는 Reset이 붙이는 기본 콜라이더보다
        // OnValidate가 먼저 호출될 수 있다. 즉시 경고하면 정상 구성도 누락처럼 보이므로
        // 에디터가 컴포넌트 구성을 마친 다음 한 번 더 확인한다.
        if (!HasUsableAreaCollider() && !_colliderValidationScheduled)
        {
            _colliderValidationScheduled = true;
            UnityEditor.EditorApplication.delayCall += ValidateAreaColliderAfterEditorUpdate;
        }
    }

    private void ValidateAreaColliderAfterEditorUpdate()
    {
        if (!this) return;

        _colliderValidationScheduled = false;
        if (!_areaCollider) _areaCollider = GetComponent<ZCollider2D>();
        if (SupportsLegacyAreaCollider && !_legacyAreaCollider)
            _legacyAreaCollider = GetComponent<Collider2D>();
        RebuildContactFilter();

        if (!HasUsableAreaCollider())
            Debug.LogWarning("영역 기반 이벤트 트리거에는 ZCollider2D가 필요합니다.", this);
    }

    private bool HasUsableAreaCollider()
    {
        return _areaCollider || SupportsLegacyAreaCollider && _legacyAreaCollider;
    }
#endif
}
