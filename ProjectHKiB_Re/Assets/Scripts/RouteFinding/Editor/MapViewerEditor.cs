using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RouteFinding.MapView;
using RouteFinding.Editor;

[CustomEditor(typeof(MapViewer))]
public class MapViewerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        GUILayout.Label("── 프리팹 도구 ──", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("MapPanel 프리팹 생성 (플레이 중)", GUILayout.Height(30)))
            GeneratePrefab((MapViewer)target);
        EditorGUI.EndDisabledGroup();

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("플레이 모드에서 맵이 열린 상태일 때 버튼을 눌러 프리팹을 생성하세요.\n생성된 프리팹을 위의 '프리팹' 필드에 할당하면 다음 실행부터 코드 대신 프리팹을 사용합니다.", MessageType.Info);

        // MapViewer.BuildUI()는 _panelPrefab보다 먼저 "씬에 MapPanel이 이미 자식으로 있으면 그대로 씀"
        // 경로가 있다 — MapPanel.prefab 에셋을 Prefab Mode에서 직접 고쳐도, 씬에 이 인스턴스가 남아있으면
        // (연결이 끊긴 복사본이거나, 프리팹 오버라이드가 걸려 있는 경우) 전혀 반영되지 않는다.
        // 이 버튼이 그 씬 인스턴스를 찾아 제거해, 다음 플레이부터 _panelPrefab을 새로 Instantiate하게 한다.
        if (GUILayout.Button("씬에 저장된 MapPanel 제거 (스테일 인스턴스)", GUILayout.Height(24)))
            RemoveStaleSceneInstances();
    }

    [MenuItem("RouteFinding/지도 씬 MapPanel 제거 (스테일 인스턴스)")]
    private static void RemoveStaleSceneInstances()
    {
        var viewers = Resources.FindObjectsOfTypeAll<MapViewer>();
        int removed = 0;
        foreach (var viewer in viewers)
        {
            // 프로젝트 에셋(프리팹 소스) 안에 있는 MapViewer는 제외 — 실제 씬에 배치된 것만 대상.
            if (!viewer.gameObject.scene.IsValid()) continue;

            var existing = viewer.transform.Find("MapPanel");
            if (existing == null) continue;

            Debug.Log($"[MapViewerEditor] 씬 '{viewer.gameObject.scene.name}'의 '{viewer.name}' 밑에 저장돼 있던 스테일 MapPanel을 제거합니다.");
            Object.DestroyImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(viewer.gameObject.scene);
            removed++;
        }

        if (removed == 0)
        {
            EditorUtility.DisplayDialog("정리할 항목 없음", "씬에 저장된 MapPanel 인스턴스가 없습니다.", "확인");
            return;
        }

        EditorUtility.DisplayDialog("완료",
            $"씬에 저장돼 있던 스테일 MapPanel {removed}개를 제거했습니다.\n" +
            "Ctrl+S로 씬을 저장하세요 — 다음 플레이부터 프리팹(또는 코드)으로 새로 생성됩니다.",
            "확인");
    }

    private static void GeneratePrefab(MapViewer viewer)
    {
        var panelGO = viewer.GetPanelGO();
        if (panelGO == null)
        {
            EditorUtility.DisplayDialog("오류",
                "MapPanel GO가 없습니다.\n지도를 한 번 열어(M키) MapPanel이 활성화된 상태에서 시도하세요.",
                "확인");
            return;
        }

        const string saveDir  = "Assets/Scripts/RouteFinding/MapView";
        const string savePath = saveDir + "/MapPanel.prefab";

        System.IO.Directory.CreateDirectory(saveDir);

        // 방어적 정리 — CodexPanelEditor/NotePanelEditor와 동일한 이유(재컴파일 타이밍 문제로 런타임에
        // AddComponent된 타입이 옛 어셈블리 기준으로 붙어있어 "missing script" 컴포넌트로 그대로
        // 구워지는 사고가 실제로 있었다). 저장 직전에 계층 전체를 훑어 missing script 컴포넌트를
        // 자동 제거한다.
        int removedTotal = 0;
        foreach (var t in panelGO.GetComponentsInChildren<Transform>(true))
            removedTotal += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        if (removedTotal > 0)
            Debug.LogWarning($"[MapViewerEditor] 프리팹 저장 전 missing script 컴포넌트 {removedTotal}개를 자동 제거했습니다 " +
                              "(재컴파일 타이밍 문제일 가능성 — 저장 후 플레이 모드를 재시작해 관련 기능이 다시 정상 동작하는지 확인하세요).");

        var prefab = PrefabUtility.SaveAsPrefabAsset(panelGO, savePath, out bool success);
        if (!success || prefab == null)
        {
            EditorUtility.DisplayDialog("오류", "프리팹 저장에 실패했습니다.", "확인");
            return;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 플레이 모드 중엔 지금 필드를 바꿔도 Stop 시 Unity가 되돌리므로(씬 오브젝트의 플레이 중 변경은
        // 전부 롤백됨), 즉시 할당하지 않고 예약해둔다 — 플레이 모드를 종료하면 자동으로 반영된다.
        PanelPrefabAssignHelper.RequestAssign(viewer, "_panelPrefab", savePath);

        EditorUtility.DisplayDialog("완료 (플레이 모드 종료 후 자동 반영)",
            $"MapPanel 프리팹이 저장되었습니다.\n{savePath}\n\n" +
            "플레이 모드 중에는 필드 변경이 Stop 시 되돌려지기 때문에, 지금 당장은 반영되지 않습니다.\n" +
            "플레이 모드를 종료하면 '_panelPrefab' 필드가 자동으로 할당되고 씬이 수정됨으로 표시됩니다 — Ctrl+S로 저장하세요.",
            "확인");

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }
}
