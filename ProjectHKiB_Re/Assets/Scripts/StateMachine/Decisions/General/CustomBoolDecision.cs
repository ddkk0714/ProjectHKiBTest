using UnityEngine;
namespace StateMachine
{
    [System.Serializable]
    public class CustomBoolDecision : StateDecision
    {
        // 이벤트 체인 샘플도 전투 담당과 합의한 신호명을 그대로 저작할 수 있어야 한다.
        // 직렬화 호환성을 유지하면서 C# 저작 코드에서도 object initializer로 지정할 수 있게 공개한다.
        [SerializeField] public string boolName;
        public override bool Decide(StateController stateController)
        {
            return stateController.GetBoolParameter(boolName);
        }
    }
}
