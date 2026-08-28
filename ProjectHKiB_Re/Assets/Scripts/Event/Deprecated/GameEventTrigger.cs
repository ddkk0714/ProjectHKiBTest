using System;
using UnityEngine;

/// <summary>
/// 기존 Reliable 공격 트리거가 배치된 Scene의 스크립트 GUID를 보존하는 호환 클래스입니다.
/// 새 오브젝트에는 역할이 명확한 AttackEventTrigger를 직접 사용합니다.
/// </summary>
[Obsolete("새 오브젝트에는 AttackEventTrigger를 사용하세요.")]
[AddComponentMenu("ProjectHKiB/Event/Legacy/Game Event Trigger")]
public sealed class GameEventTrigger : AttackEventTrigger
{
}
