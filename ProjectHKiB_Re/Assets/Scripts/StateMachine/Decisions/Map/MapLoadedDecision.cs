namespace StateMachine
{
    // ChangeMapAction이 건 비동기 로드가 끝났는지 본다.
    //
    // OnMapLoaded를 구독하지 않고 MapManager.CurrentMapData를 폴링하는 이유는, 이 클래스가
    // [SerializeReference]로 에셋에 직렬화되는 값 객체라서 구독/해제 수명을 관리할 자리가
    // 없기 때문이다. CurrentMapData는 로드 완료 콜백 안에서 갱신되므로 완료 신호로 충분하다.
    //
    // 주의: 목적지가 지금 있는 맵과 같으면 곧바로 true다. 같은 맵을 다시 로드해 기다리는
    // 연출에는 쓸 수 없다.
    [System.Serializable]
    public class MapLoadedDecision : StateDecision
    {
        // 비워두면 "아무 맵이든 로드가 끝나 있으면 true".
        public string mapAddressableID;

        public override bool Decide(StateController stateController)
        {
            MapManager mapManager = GameManager.instance.mapManager;
            if (!mapManager || !mapManager.CurrentMapData) return false;

            return string.IsNullOrEmpty(mapAddressableID)
                || mapManager.CurrentMapData.mapAddressableID == mapAddressableID;
        }
    }
}
