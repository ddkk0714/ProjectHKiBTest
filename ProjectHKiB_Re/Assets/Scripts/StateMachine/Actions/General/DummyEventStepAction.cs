using UnityEngine;
namespace StateMachine
{
    // 아직 구현되지 않은 연출/기믹 자리를 채우는 더미.
    //
    // 해몽 UI, 패링, 탄막 반사, 그로기, 각성 게이지, 날개 절단 홀딩처럼 실제 콘텐츠가 필요한
    // 단계를 이걸로 대신 세워두면, 콘텐츠가 없어도 이벤트 전체를 끝까지 배선하고 플레이로
    // 흐름을 확인할 수 있다. 나중에 진짜 액션으로 갈아끼우면 된다.
    //
    // completeBoolName을 채우면 그 커스텀 bool을 true로 만든다 — 다음 State로 넘어가는 전이를
    // CustomBoolDecision으로 걸어두면 더미 단계도 "완료되면 진행"하는 모양이 그대로 유지된다.
    [System.Serializable]
    public class DummyEventStepAction : StateAction
    {
        // 로그에 찍힐 이름. "해몽 UI", "각성 게이지 100%" 처럼 무엇이 빠졌는지 알아볼 수 있게 쓸 것.
        public string label = "(이름 없는 더미 단계)";
        public string completeBoolName;

        public override void Act(StateController stateController)
        {
            // 이벤트 시작 후 경과(unscaled)를 같이 찍는다. 단계 사이에 어디서 시간이 새는지는
            // 로그가 찍힌 순서만 봐서는 알 수 없어서, 연출 타이밍을 볼 때 이 숫자가 근거가 된다.
            string elapsed = stateController is EventManager eventManager
                ? $" [+{Time.unscaledTime - eventManager.EventStartedAtUnscaled:0.00}s]"
                : "";
            string completion = string.IsNullOrEmpty(completeBoolName)
                ? "완료 신호를 기다립니다."
                : $"'{completeBoolName}'를 true로 설정해 즉시 완료 처리합니다.";
            Debug.LogWarning($"[더미 연출]{elapsed} {label} — 아직 구현되지 않은 단계입니다. {completion}");

            if (!string.IsNullOrEmpty(completeBoolName))
                stateController.SetBoolParameterTrue(completeBoolName);
        }
    }
}
