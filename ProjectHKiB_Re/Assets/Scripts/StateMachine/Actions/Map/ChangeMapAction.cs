using UnityEngine;
namespace StateMachine
{
    // 이벤트 연출 도중 맵을 갈아끼운다 — 꿈↔현실 전환, 사망 시 강제 복귀 등.
    // 이 액션이 생기기 전까지 MapManager.LoadMap()을 부르는 곳은 RouteFindingMapBridge/SaveModule/
    // MapChangeManager뿐이라, 상태 기계에서는 맵을 바꿀 방법이 아예 없었다.
    //
    // 플레이어 배치는 하지 않는다 — MapStartPosPlacer가 OnMapLoaded를 구독해 대신 처리하므로
    // 목적지 맵에 MapStartPos가 하나는 있어야 한다(Is Default Entry를 켜두면 모든 진입에 대응).
    //
    // 로드는 Addressables 비동기다. 이 액션이 반환된 시점에는 아직 새 맵이 떠 있지 않으므로,
    // 전환 완료를 기다려야 하는 연출은 다음 State를 두고 MapLoadedDecision으로 받을 것.
    [System.Serializable]
    public class ChangeMapAction : StateAction
    {
        // 직접 참조가 우선. 씬 프리팹에 굽기 곤란한 자리에서는 비워두고 아래 둘로 찾게 한다.
        public MapDataSO mapData;
        public MapDataRegistrySO registry;
        public string mapAddressableID;

        public override void Act(StateController stateController)
        {
            MapDataSO target = mapData;
            if (!target && registry) target = registry.Find(mapAddressableID);

            if (!target)
            {
                Debug.LogError($"ERROR: ChangeMapAction - 목적지 맵을 찾을 수 없습니다 (mapAddressableID: '{mapAddressableID}').");
                return;
            }

            GameManager.instance.mapManager.LoadMap(target);
        }
    }
}
