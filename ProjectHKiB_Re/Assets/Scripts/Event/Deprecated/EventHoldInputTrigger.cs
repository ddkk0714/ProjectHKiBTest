using System;
using UnityEngine;

/// <summary>
/// 기존 프리팹의 스크립트 GUID와 홀드 시간을 보존하는 호환 클래스입니다.
/// 입력 판정은 InteractionEventTrigger의 InputActionReference 경로를 사용합니다.
/// </summary>
[Obsolete("새 오브젝트에는 Hold 모드의 InteractionEventTrigger를 사용하세요.")]
[AddComponentMenu("ProjectHKiB/Event/Legacy/Event Hold Input Trigger")]
public sealed class EventHoldInputTrigger : InteractionEventTrigger
{
    /// <summary>
    /// 예전 EventHoldInputTrigger의 의미를 홀드 입력으로 고정합니다.
    /// 누적 시간과 진행률은 기반 클래스가 관리합니다.
    /// </summary>
    protected override InteractionEventActivation EffectiveActivation => InteractionEventActivation.Hold;
}
