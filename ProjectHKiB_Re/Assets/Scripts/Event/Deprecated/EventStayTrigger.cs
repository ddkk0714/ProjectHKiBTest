using System;
using UnityEngine;

/// <summary>
/// 기존 프리팹의 스크립트 GUID를 보존하기 위한 영역 진입 트리거 호환 클래스입니다.
/// 새 오브젝트에는 AreaEventTrigger를 사용하며 이 클래스도 같은 공통 실행 경로로 동작합니다.
/// </summary>
[Obsolete("새 오브젝트에는 AreaEventTrigger를 사용하세요.")]
[AddComponentMenu("ProjectHKiB/Event/Legacy/Event Stay Trigger")]
public sealed class EventStayTrigger : AreaEventTrigger
{
    /// <summary>
    /// 예전 EventStayTrigger의 의미를 영역 진입 1회로 고정합니다.
    /// 대상이 나갔다 다시 들어오면 다시 발동할 수 있습니다.
    /// </summary>
    protected override AreaEventActivation EffectiveActivation => AreaEventActivation.Enter;
}
