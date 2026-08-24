using UnityEngine;
namespace StateMachine
{
    // MarkUnscaledTimeAction이 찍어둔 시각으로부터 지정 시간이 지났는지 — Time.timeScale과 무관하게 흐른다.
    // 컷신처럼 게임플레이를 멈춘 채로도 진행돼야 하는 연출 단계의 타임아웃에 쓴다(TimerDecision은
    // 스케일 시간이라 그 상황에서 멈춘다 — MarkUnscaledTimeAction 주석 참고).
    [System.Serializable]
    public class UnscaledTimeElapsedDecision : StateDecision
    {
        public string key = "unscaledMark";
        [Min(0f)] public float seconds = 1f;

        public override bool Decide(StateController stateController)
        {
            // 표식이 없으면(액션 배선을 빠뜨렸으면) 0이 나온다 — 그 경우 즉시 참이 되어 단계가
            // 그냥 넘어간다. 멈춰서 이벤트를 가두는 것보다 낫다.
            int markMs = stateController.GetIntParameter(key);
            float elapsedMs = Time.unscaledTime * 1000f - markMs;

            // 경과가 음수라면 표식이 "미래"에 찍혀 있다는 뜻이고, 그건 이번 진입에서 찍힌 값이
            // 아니라 지난 플레이에서 에셋에 눌러 붙은 값이다(customVariables가 SO를 참조로
            // 물어가기 때문 — StateController.Initialize 주석 참고). 그대로 두면 그 시각이
            // 돌아올 때까지 몇십 초를 기다리므로, 낡은 값으로 보고 통과시킨다.
            if (elapsedMs < 0f) return true;

            return elapsedMs >= seconds * 1000f;
        }
    }
}
