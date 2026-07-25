using UnityEngine;

// 감정 벡터 축의 역치 활성 상태를 읽는 StateDecisionSO — 기존 IsGroggyDecision/IsRunawayDecision과
// 동일한 패턴(NewState/Groggy&Runaway)으로, 적 State 에셋의 transitions에 직접 꽂아 전용 State로 분기시킨다.
[CreateAssetMenu(fileName = "EmotionAxisActiveDecision", menuName = "State Machine/Decision/Debuff/EmotionAxisActiveDecision")]
public class EmotionAxisActiveDecision : StateDecisionSO
{
    [SerializeField] private EmotionAxis axis;

    public override bool Decide(StateController stateController)
    {
        EmotionVectorModule module = stateController.GetComponent<EmotionVectorModule>();
        if (module == null) return false;

        return module.IsAxisBehaviorActive(axis);
    }
}
