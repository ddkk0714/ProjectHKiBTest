using UnityEngine;

namespace StateMachine
{
    /// <summary>
    /// 이벤트가 이미 진행 중인 상태에서 특정 이벤트 대상 곁의 확인 입력을 기다린다.
    /// 별도 GameEventTrigger를 만들지 않아도, 한 이벤트 체인 안에서 상호작용 단계를 구성할 수 있다.
    /// </summary>
    [System.Serializable]
    public class TargetProximityInputDecision : StateDecision
    {
        public string targetID;
        [Min(0.1f)] public float radius = 1f;
        [EnumDropdown(typeof(EnumManager.InputType))] public EnumManager.InputType inputType = EnumManager.InputType.OnConfirm;

        [System.NonSerialized] private bool _armed;

        public override bool Decide(StateController stateController)
        {
            if (GameManager.instance == null || GameManager.instance.inputManager == null || GameManager.instance.player == null)
                return false;

            bool pressed = GameManager.instance.inputManager.GetInputByEnum(inputType);
            if (!pressed)
            {
                _armed = true;
                return false;
            }

            if (!_armed || !stateController.TryGetInterface(out IEvent @event) || @event.CurrentTargets == null)
                return false;

            if (!@event.CurrentTargets.targetEntities.TryGetValue(targetID, out EventControllableEntity target) ||
                target == null || target.Target == null)
                return false;

            float distanceSqr = (target.Target.transform.position - GameManager.instance.player.transform.position).sqrMagnitude;
            if (distanceSqr > radius * radius)
                return false;

            _armed = false;
            return true;
        }
    }
}
