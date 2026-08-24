using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;

[CreateAssetMenu(fileName = "Event", menuName = "Event/MapData")]
public class MapDataSO : ScriptableObject
{
    [NaughtyAttributes.Scene]
    public string mapAddressableID;

    // 이 맵이 "현실"인가. 꿈속에서는 단서가 흐릿해 제대로 볼 수 없다는 기획을 코드로 옮긴 것으로,
    // 도감·노트·지도·인터넷 창은 이 값이 켜진 맵에서만 열린다(UIManager.realWorldOnlyWindows).
    // 해몽도 자연히 현실에서만 가능해진다 — 단서를 노트에 늘어놓는 것 자체가 현실에서만 되므로.
    public bool isRealWorld;

    // 이 맵에 "처음" 들어올 때 보장할 이벤트 플래그 기본값.
    // MapLocalManager.Initialize()가 아직 설정된 적 없는 것만 채운다(EventManager.EnsureEventFlag).
    // 진행도 플래그(dood 등)의 시작값을 여기 넣어두지 않으면 `== 0` 조건이 성립하지 않는다.
    public SerializedDictionary<EventFlagSO, int> initialEventFlags;
    public string bgmID;
    public SerializedDictionary<string, List<EntityInitializeInfo>> allEntityInitInfos;
    public SerializedDictionary<string, List<AnimationInitializeInfo>> allAnimInitInfos;
}