using UnityEngine;

namespace StateMachine
{
    // 감정 벡터 축의 역치 활성 상태를 읽는 Decision — IsGroggyDecision/IsRunawayDecision과
    // 동일한 패턴으로, State 에셋의 transitions에 직접 꽂아 전용 State로 분기시킨다.
    // axis 필드로 4축(PositiveX=황홀, NegativeX=파멸, PositiveY=광기, NegativeY=잠) 전부 재사용
    // 가능한 제네릭 Decision — 새 캐릭터에 감정 State를 추가할 때 이 Decision 하나로 축만
    // 바꿔가며 진입 조건을 건다.
    //
    // 현재 배선 지점(2026-07-26): Enemy_Rusher_SleepState/EcstasyState + BackstepTarget/
    // FollowTarget/Skill01·02FrontState(잠·황홀 대상: axis 3·0), Delta_Base_Sleep/Ecstasy_
    // KeepState + Delta_Base_Idle/Walk/RunState(진입 조건). 새 캐릭터에 추가할 때는
    // Delta_Base의 Start→Keep→End 3단계 패턴을 참고할 것 — EmotionVectorModule.cs 상단
    // [외부 모듈 연동 API] 참고.
    [System.Serializable]
    public class EmotionAxisActiveDecision : StateDecision
    {
        [SerializeField] private EmotionAxis axis;

        public override bool Decide(StateController stateController)
        {
            EmotionVectorModule module = stateController.GetComponent<EmotionVectorModule>();
            if (module == null) return false;

            return module.IsAxisBehaviorActive(axis);
        }
    }
}
