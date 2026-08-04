using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// mapAddressableID(문자열) → MapDataSO 조회용 레지스트리.
///
/// 세이브에는 맵을 식별할 수 있는 문자열(= MapDataSO.mapAddressableID)만 남기고, 로드할 때
/// 그 문자열로 실제 MapDataSO를 되찾아 MapManager.LoadMap()에 넘긴다. 문자열만으로 에셋을
/// 얻으려면 어딘가에 "ID → 에셋" 대응표가 있어야 하는데(AssetDatabase는 에디터 전용이라
/// 빌드에서 못 쓴다), 그 대응표가 이 에셋이다.
///
/// [왜 Resources / Addressable이 아닌가]
///   - Resources 폴더: 폴더 전체가 항상 빌드에 포함돼 비용이 크다.
///   - MapDataSO 자체를 Addressable로: 조회가 비동기가 되어 세이브 복원 흐름이 한 겹 복잡해지고,
///     Addressable 그룹이 비어버리는 사고에 맵 데이터까지 함께 노출된다.
///   이 레지스트리는 직접 참조라 빌드에 확실히 포함되고, 조회도 동기 O(1)이다.
///
/// [사용법] 에셋을 하나 만들고 인스펙터의 Collect All 버튼을 누르면 프로젝트의 MapDataSO를
///          전부 자동 수집한다. 수집 후 Validate로 Addressable 등록 상태까지 점검할 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "MapDataRegistry", menuName = "Event/MapDataRegistry")]
public class MapDataRegistrySO : ScriptableObject
{
    [SerializeField] private List<MapDataSO> maps = new();

    public IReadOnlyList<MapDataSO> Maps => maps;

    private Dictionary<string, MapDataSO> _lookup;

    /// <summary>mapAddressableID로 MapDataSO를 찾는다. 없으면 null.</summary>
    public MapDataSO Find(string mapAddressableID)
    {
        if (string.IsNullOrEmpty(mapAddressableID)) return null;

        EnsureLookup();
        return _lookup.TryGetValue(mapAddressableID, out MapDataSO mapData) ? mapData : null;
    }

    private void EnsureLookup()
    {
        // 에디터에서 Collect All로 목록이 바뀌면 캐시를 다시 만들어야 한다.
        // 개수 비교만으로도 이 용도에는 충분하다(런타임 중 목록이 바뀌지 않으므로).
        if (_lookup != null && _lookup.Count == CountValidEntries()) return;

        _lookup = new Dictionary<string, MapDataSO>();
        for (int i = 0; i < maps.Count; i++)
        {
            MapDataSO mapData = maps[i];
            if (mapData == null || string.IsNullOrEmpty(mapData.mapAddressableID)) continue;

            if (_lookup.ContainsKey(mapData.mapAddressableID))
            {
                Debug.LogWarning($"[MapDataRegistry] mapAddressableID가 중복입니다: '{mapData.mapAddressableID}' " +
                                 $"({_lookup[mapData.mapAddressableID].name} / {mapData.name}). 먼저 등록된 쪽을 씁니다.");
                continue;
            }

            _lookup[mapData.mapAddressableID] = mapData;
        }
    }

    private int CountValidEntries()
    {
        int count = 0;
        var seen = new HashSet<string>();
        for (int i = 0; i < maps.Count; i++)
        {
            MapDataSO mapData = maps[i];
            if (mapData == null || string.IsNullOrEmpty(mapData.mapAddressableID)) continue;
            if (seen.Add(mapData.mapAddressableID)) count++;
        }
        return count;
    }

#if UNITY_EDITOR
    // 버튼은 MapDataRegistrySOEditor가 그린다 — NaughtyAttributes의 Button 속성은 전용
    // CustomEditor가 있으면 그려지지 않으므로(NaughtyInspector가 덮이므로) 여기 붙이지 않는다.
    /// <summary>프로젝트 안의 MapDataSO를 전부 찾아 목록을 다시 채운다.</summary>
    public void CollectAll()
    {
        maps.Clear();

        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(MapDataSO)}");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            MapDataSO mapData = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDataSO>(path);
            if (mapData != null) maps.Add(mapData);
        }

        _lookup = null;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[MapDataRegistry] MapDataSO {maps.Count}개를 수집했습니다.");
    }
#endif
}
