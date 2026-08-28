using System.Collections.Generic;
using UnityEngine;

public enum AreaEventActivation
{
    Enter,
    Stay,
    Exit
}

/// <summary>
/// 대상의 영역 진입, 체류, 이탈을 감지하는 이벤트 트리거입니다.
/// 물리 상태 비교만 담당하며 입력과 공격 콜백은 다루지 않습니다.
/// </summary>
[AddComponentMenu("ProjectHKiB/Event/Area Event Trigger")]
public class AreaEventTrigger : SpatialEventTriggerBase
{
    [Tooltip("영역 상태 중 어느 시점에 이벤트를 발동할지 결정합니다.")]
    [SerializeField]
    private AreaEventActivation _activation = AreaEventActivation.Enter;

    private readonly Dictionary<int, SpatialTargetRecord> _previousTargets = new();

    /// <summary>
    /// 실제 영역 트리거가 사용할 발동 시점을 반환합니다.
    /// 기존 트리거 호환 클래스는 이 값을 재정의할 수 있습니다.
    /// </summary>
    protected virtual AreaEventActivation EffectiveActivation => _activation;

    /// <summary>
    /// 활성화될 때 이전 프레임 대상을 비워 진입 상태를 정확히 다시 계산합니다.
    /// 비활성화 후 재등장한 대상도 새 진입으로 취급합니다.
    /// </summary>
    private void OnEnable()
    {
        ClearCurrentTargets();
        _previousTargets.Clear();
    }

    /// <summary>
    /// 물리 프레임마다 현재 대상과 직전 대상을 비교해 선택한 영역 사건을 판정합니다.
    /// 한 물리 프레임에는 첫 번째 유효 대상만 실행합니다.
    /// </summary>
    private void FixedUpdate()
    {
        CollectCurrentTargets();
        EvaluateAreaState();
        StoreCurrentTargets();
    }

    /// <summary>
    /// Enter, Stay, Exit 규칙에 맞는 첫 번째 대상을 찾아 공통 실행부로 전달합니다.
    /// Exit은 직전 레코드를 사용해 이탈한 대상 정보도 보존합니다.
    /// </summary>
    private void EvaluateAreaState()
    {
        if (EffectiveActivation == AreaEventActivation.Exit)
        {
            foreach (KeyValuePair<int, SpatialTargetRecord> pair in _previousTargets)
            {
                if (!CurrentTargets.ContainsKey(pair.Key) && TriggerRecord(pair.Value)) return;
            }

            return;
        }

        foreach (KeyValuePair<int, SpatialTargetRecord> pair in CurrentTargets)
        {
            bool isCandidate = EffectiveActivation == AreaEventActivation.Stay ||
                               !_previousTargets.ContainsKey(pair.Key);
            if (isCandidate && TriggerRecord(pair.Value)) return;
        }
    }

    /// <summary>
    /// 감지 레코드를 공통 EventTriggerContext로 변환해 실행합니다.
    /// 실행 제한에 걸리면 false를 반환해 다음 대상을 검사할 수 있습니다.
    /// </summary>
    private bool TriggerRecord(SpatialTargetRecord record)
    {
        return record != null && TryTrigger(new EventTriggerContext(this, record.Target, record.Collider));
    }

    /// <summary>
    /// 현재 대상 목록을 다음 물리 프레임 비교용 사전에 복사합니다.
    /// 레코드는 불변이므로 참조를 안전하게 재사용합니다.
    /// </summary>
    private void StoreCurrentTargets()
    {
        _previousTargets.Clear();
        foreach (KeyValuePair<int, SpatialTargetRecord> pair in CurrentTargets)
            _previousTargets.Add(pair.Key, pair.Value);
    }
}
