namespace StateMachine
{
    // 지정한 해몽이 성립했는지. EVT-003이 "플레이어가 노트에서 단서를 이어 규칙을 깨달을 때까지"
    // 기다리는 데 쓴다.
    //
    // 해몽 판정과 플래그 해금은 DreamReadingModule이 알아서 한다 — 이벤트는 결과만 본다.
    // readingId를 비워두면 "아무거나 하나라도 풀렸는가"가 된다.
    [System.Serializable]
    public class DreamReadingResolvedDecision : StateDecision
    {
        public string readingId;

        public override bool Decide(StateController stateController)
        {
            DreamReadingModule module = DreamReadingModule.Instance;
            if (module == null) return false;

            return string.IsNullOrEmpty(readingId)
                ? module.ResolvedIds.Count > 0
                : module.IsResolved(readingId);
        }
    }
}
