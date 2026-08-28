using System;
using UnityEngine;

/// <summary>
/// 기존 프리팹의 스크립트 GUID를 보존하는 방향 확인 입력 호환 클래스입니다.
/// MathManagerSO와 InputType 대신 InputActionReference와 벡터 내적 판정을 사용합니다.
/// </summary>
[Obsolete("새 오브젝트에는 ConfirmDirection 모드의 InteractionEventTrigger를 사용하세요.")]
[AddComponentMenu("ProjectHKiB/Event/Legacy/Event Confirm Direction Trigger")]
public sealed class EventConfirmDirTrigger : InteractionEventTrigger
{
    /// <summary>
    /// 예전 방향 확인 트리거의 의미를 입력과 방향 조합으로 고정합니다.
    /// 요구 방향과 허용 내적은 기반 클래스 인스펙터에서 설정합니다.
    /// </summary>
    protected override InteractionEventActivation EffectiveActivation => InteractionEventActivation.ConfirmDirection;

    /// <summary>
    /// 옛 방향 확인 트리거에는 InputType 필드가 없었으므로 PLAY/Confirm을 기본 이전 대상으로 사용합니다.
    /// 실제 실행은 이전된 InputActionReference와 InputProcessType만 읽습니다.
    /// </summary>
    protected override string LegacyDefaultActionName => "Confirm";
}
