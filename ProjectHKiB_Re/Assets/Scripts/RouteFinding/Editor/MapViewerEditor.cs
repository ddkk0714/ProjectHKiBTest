using UnityEditor;
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
