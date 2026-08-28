using System;

/// <summary>
/// 외부 코드가 사용하던 EventTrigger 타입 이름을 보존하는 추상 호환 기반입니다.
/// 새 역할별 트리거는 EventTriggerBase를 직접 상속하며 이 타입에는 감지 책임이 없습니다.
/// </summary>
[Obsolete("새 코드에서는 EventTriggerBase 또는 역할별 트리거를 사용하세요.")]
public abstract class EventTrigger : EventTriggerBase
{
}
