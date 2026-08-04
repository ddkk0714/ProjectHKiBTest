using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// MapDataRegistry 인스펙터 — 기본 그리기에 Addressable 등록 검증 버튼을 더한다.
///
/// 맵 씬의 Addressable 등록은 Groups 창(Window → Asset Management → Addressables → Groups)에서
/// 사람이 하고, 이 도구는 어긋난 곳만 찾아 보고한다. 자동으로 고치지 않는 이유는 그룹 구성·주소
/// 정책이 기획 쪽 결정이라서다.
///
/// [이 검증이 필요한 이유]
/// PrototypeMaps 그룹의 엔트리가 실제로 두 번 통째로 비워진 이력이 있다(16e18d41 07-23,
/// d8140aa2 07-31). 등록이 비면 Addressables.LoadSceneAsync가 키를 못 찾아 맵 로드가 통째로
/// 실패하는데, 에디터에서는 아무 경고도 뜨지 않아 발견이 늦다.
/// 또 mapAddressableID는 [NaughtyAttributes.Scene]이 채우므로 씬 "이름"인데, Addressable 주소는
/// 기본값이 전체 경로(Assets/.../TestMap1.unity)라 그대로 두면 서로 다른 키가 되어 매칭되지 않는다.
/// </summary>
[CustomEditor(typeof(MapDataRegistrySO))]
public class MapDataRegistrySOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MapDataRegistrySO registry = (MapDataRegistrySO)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Collect All (프로젝트의 MapDataSO 수집)", GUILayout.Height(24f)))
        {
            registry.CollectAll();
            AssetDatabase.SaveAssetIfDirty(registry);
        }

        if (GUILayout.Button("Addressable 등록 검증", GUILayout.Height(24f)))
            Validate(registry);

        if (GUILayout.Button("RouteFinding 노드 연결 검증", GUILayout.Height(24f)))
            ValidateRouteFindingNodes(registry);
    }

    /// <summary>
    /// map_database.json의 각 노드 sceneName이 실제 MapDataSO와 연결되는지 본다.
    ///
    /// 대응 맵이 없는 노드는 "오류"가 아니라 "아직 미제작"이다 — 경로 그래프는 13개 노드인데
    /// 실제 맵은 2개뿐인 게 현재 콘텐츠 상태다(RouteFindingMapBridge가 이런 노드에서 씬 전환만
    /// 생략한다). 그래서 매칭/미매칭을 나눠서 보여주기만 하고 경고로 올리지 않는다.
    /// 정말 잡고 싶은 건 오타 — 미매칭 목록에 예상 밖의 이름이 있으면 그게 단서다.
    /// </summary>
    private static void ValidateRouteFindingNodes(MapDataRegistrySO registry)
    {
        // Resources.Load 경로로 찾지 않는다 — Resources 폴더 위치(Scripts/RouteFinding/Resources)와
        // MapGraph._mapDatabasePath가 인스펙터에서 덮어써질 수 있어 경로를 추측하면 어긋난다.
        // 에디터 전용 코드이므로 AssetDatabase로 파일을 직접 찾는 게 확실하다.
        TextAsset json = FindMapDatabaseAsset();
        if (json == null)
        {
            Debug.LogError("[MapDataRegistry] map_database.json을 프로젝트에서 찾을 수 없습니다.");
            return;
        }

        MapDatabase db = JsonUtility.FromJson<MapDatabase>(json.text);
        if (db?.maps == null)
        {
            Debug.LogError("[MapDataRegistry] map_database 파싱에 실패했습니다.");
            return;
        }

        StringBuilder linked = new();
        StringBuilder unlinked = new();

        foreach (MapNodeData node in db.maps)
        {
            if (node == null) continue;

            MapDataSO mapData = string.IsNullOrEmpty(node.sceneName) ? null : registry.Find(node.sceneName);
            if (mapData != null) linked.AppendLine($"- {node.nodeName}: '{node.sceneName}' → {mapData.name}");
            else unlinked.AppendLine($"- {node.nodeName}: sceneName='{node.sceneName}'");
        }

        Debug.Log($"[MapDataRegistry] RouteFinding 노드 연결 상태 (총 {db.maps.Length}개)\n" +
                  $"[연결됨]\n{(linked.Length > 0 ? linked.ToString() : "없음\n")}" +
                  $"[대응 맵 없음 — 미제작이면 정상]\n{(unlinked.Length > 0 ? unlinked.ToString() : "없음")}");
    }

    private static void Validate(MapDataRegistrySO registry)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[MapDataRegistry] Addressable 설정을 찾을 수 없습니다. " +
                           "Window → Asset Management → Addressables → Groups에서 초기화하세요.");
            return;
        }

        // 등록된 주소 전체를 모은다. 주소 → (엔트리 경로, 소속 그룹).
        //
        // 읽기 전용 그룹(Built In Data)도 반드시 포함해야 한다 — 이 그룹의 EditorSceneList 엔트리가
        // Build Settings에 등록된 씬들을 "씬 이름"을 주소로 삼아 자동으로 노출하기 때문이다
        // (PlayerDataGroupSchema.IncludeBuildSettingsScenes = 1일 때.
        //  패키지 소스: AddressableAssetEntry.GatherEditorSceneEntries — 주소를
        //  Path.GetFileNameWithoutExtension(경로)로 만든다).
        // 즉 PrototypeMaps 그룹이 비어 있어도 Build Settings에 있는 씬은 이미 로드 가능하다.
        // 이걸 빼고 검사하면 멀쩡한 씬을 "등록 없음"으로 잘못 보고하게 된다.
        Dictionary<string, (string path, string group)> addressToEntry = new();
        List<AddressableAssetEntry> buffer = new();
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null) continue;

            buffer.Clear();
            group.GatherAllAssets(buffer, true, true, false);
            foreach (AddressableAssetEntry entry in buffer)
            {
                if (entry == null || string.IsNullOrEmpty(entry.address)) continue;
                addressToEntry[entry.address] = (entry.AssetPath, group.Name);
            }
        }

        StringBuilder problems = new();
        StringBuilder resolved = new();  // 어느 그룹으로 해결됐는지 — Build Settings 경유인지 구분용
        HashSet<string> seenIDs = new();
        int checkedCount = 0;

        foreach (MapDataSO mapData in registry.Maps)
        {
            if (mapData == null)
            {
                problems.AppendLine("- 목록에 빈(null) 항목이 있습니다. Collect All을 다시 실행하세요.");
                continue;
            }

            checkedCount++;
            string id = mapData.mapAddressableID;

            if (string.IsNullOrEmpty(id))
            {
                problems.AppendLine($"- {mapData.name}: mapAddressableID가 비어 있습니다.");
                continue;
            }

            if (!seenIDs.Add(id))
                problems.AppendLine($"- {mapData.name}: mapAddressableID '{id}'가 다른 MapDataSO와 중복입니다.");

            if (addressToEntry.TryGetValue(id, out var hit))
            {
                resolved.AppendLine($"- {mapData.name}: '{id}' → {hit.group}");
                continue;
            }

            // ID로는 못 찾았다 — 전체 경로로 등록돼 있는지 확인해 원인을 구체적으로 알려준다.
            string pathStyle = FindAddressEndingWithSceneName(addressToEntry, id);
            if (pathStyle != null)
            {
                problems.AppendLine($"- {mapData.name}: Addressable 주소가 '{pathStyle}'로 등록돼 있습니다. " +
                                    $"주소를 '{id}'로 바꿔야 매칭됩니다.");
            }
            else
            {
                problems.AppendLine($"- {mapData.name}: '{id}'로 등록된 Addressable 엔트리가 없습니다. " +
                                    $"Groups 창에서 씬을 추가하고 주소를 '{id}'로 설정하거나, " +
                                    $"Build Settings에 해당 씬을 활성 상태로 넣으세요.");
            }
        }

        if (problems.Length == 0)
        {
            Debug.Log($"[MapDataRegistry] 검증 통과 — MapDataSO {checkedCount}개 모두 Addressable 주소와 일치합니다.\n{resolved}");
            return;
        }

        Debug.LogWarning($"[MapDataRegistry] 검증 실패 (MapDataSO {checkedCount}개 검사)\n{problems}" +
                         (resolved.Length > 0 ? $"\n[정상]\n{resolved}" : ""));
    }

    private static TextAsset FindMapDatabaseAsset()
    {
        foreach (string guid in AssetDatabase.FindAssets("map_database t:TextAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileName(path) != "map_database.json") continue;

            return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        return null;
    }

    // "Assets/Scenes/TestMap/TestMap1.unity" 형태의 주소 중 파일명이 id와 같은 것을 찾는다.
    private static string FindAddressEndingWithSceneName(Dictionary<string, (string path, string group)> addressToEntry, string id)
    {
        foreach (string address in addressToEntry.Keys)
        {
            if (System.IO.Path.GetFileNameWithoutExtension(address) == id) return address;
        }
        return null;
    }
}
