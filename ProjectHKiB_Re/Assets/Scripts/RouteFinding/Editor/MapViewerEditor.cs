using UnityEditor;
using UnityEngine;
using RouteFinding.MapView;

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

        var prefab = PrefabUtility.SaveAsPrefabAsset(panelGO, savePath, out bool success);
        if (!success || prefab == null)
        {
            EditorUtility.DisplayDialog("오류", "프리팹 저장에 실패했습니다.", "확인");
            return;
        }

        // 저장한 프리팹을 _panelPrefab 필드에 자동 할당
        var so = new SerializedObject(viewer);
        so.FindProperty("_panelPrefab").objectReferenceValue = prefab;
        so.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("완료",
            $"MapPanel 프리팹이 저장되었습니다.\n{savePath}\n\n이제 플레이 모드를 종료해도 프리팹이 유지됩니다.",
            "확인");

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }
}
