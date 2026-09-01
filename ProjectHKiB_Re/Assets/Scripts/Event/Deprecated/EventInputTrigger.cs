using System;
using UnityEngine;

/// <summary>
/// 기존 프리팹의 스크립트 GUID를 보존하기 위한 단일 입력 호환 클래스입니다.
/// 직렬화된 옛 입력 값은 에디터에서 InputActionReference로 자동 이전됩니다.
/// </summary>
[Obsolete("새 오브젝트에는 InteractionEventTrigger를 사용하세요.")]
[AddComponentMenu("ProjectHKiB/Event/Legacy/Event Input Trigger")]
public sealed class EventInputTrigger : InteractionEventTrigger
{
    protected override bool SupportsLegacyAreaCollider => true;

    /// <summary>
    /// 예전 EventInputTrigger의 의미를 새로 누른 입력 한 번으로 고정합니다.
    /// 입력을 놓은 뒤 다시 눌러야 재발동할 수 있습니다.
    /// </summary>
    protected override InteractionEventActivation EffectiveActivation => InteractionEventActivation.Press;
}
