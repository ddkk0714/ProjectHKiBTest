using UnityEditor;
using UnityEngine;
using RouteFinding.Note;
using RouteFinding.Editor;

[CustomEditor(typeof(NotePanel))]
public class NotePanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        GUILayout.Label("── 프리팹 도구 ──", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("NotePanel 프리팹 생성 (플레이 중)", GUILayout.Height(30)))
            GeneratePrefab((NotePanel)target);
        EditorGUI.EndDisabledGroup();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서 노트가 열린 상태일 때 버튼을 눌러 프리팹을 생성하세요.\n생성된 프리팹을 위의 '프리팹' 필드에 할당하면 다음 실행부터 코드 대신 프리팹을 사용합니다.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "이 버튼은 NotePanelRoot 전체(하위의 GraphScroll — GraphPanZoom(휠 줌·드래그 패닝)이 붙어있고 " +
                "그 안의 GraphViewport/GraphContainer에 NoteRouteGraphView가 노드를 그림 — 와 " +
                "ClueDrawerScroll(접기/펼치기용 DrawerToggleTab 포함) 안에 있는 NoteRouteGraphView·" +
                "ClueDrawerView 컴포넌트의 행 템플릿 필드 — _nodeBoxTemplate/_edgeTemplate/" +
                "_cardTemplate/_commentTemplate/_clueRowTemplate 포함, 그리고 NotePanelRoot 자체에 붙은 " +
                "NoteBoardWindow의 BoardWindowOverlay·ClueKeywordFilterWindow의 ClueKeywordFilterOverlay·" +
                "NoteClueCreateWindow의 ClueCreateOverlay)를 지금 이 순간 상태 그대로 저장합니다.\n\n" +
                "행/카드 템플릿 프리팹을 새로 만들어서 위 필드들에 할당했다면, 그 할당 '이후'에 이 버튼을 " +
                "다시 눌러야 프리팹 파일에 반영됩니다 — 필드만 바꾸고 이 버튼을 안 누르면, 플레이 모드를 " +
                "종료하는 순간 그 할당은 저장된 데가 없어 그냥 사라집니다(NotePanelRoot 자체가 플레이 중에만 " +
                "존재하는 오브젝트라서). 순서: ① 행 커스터마이징 + 템플릿 필드 할당 → ② 이 버튼 클릭 → " +
                "③ 플레이 모드 종료 → ④ Ctrl+S로 씬 저장.",
                MessageType.Warning);
        }
    }

    private static void GeneratePrefab(NotePanel panel)
    {
        var panelGO = panel.GetPanelGO();
        if (panelGO == null)
        {
            EditorUtility.DisplayDialog("오류",
                "NotePanelRoot GO가 없습니다.\n노트를 한 번 열어(V키) NotePanelRoot가 활성화된 상태에서 시도하세요.",
                "확인");
            return;
        }

        const string saveDir  = "Assets/Scripts/RouteFinding/Note";
        const string savePath = saveDir + "/NotePanel.prefab";

        // 방어적 정리 — CodexPanelEditor와 동일한 이유(재컴파일 타이밍 문제로 런타임에 AddComponent된
        // 타입이 옛 어셈블리 기준으로 붙어있어 "missing script" 컴포넌트로 그대로 구워지는 사고가
        // 실제로 있었다). 저장 직전에 계층 전체를 훑어 missing script 컴포넌트를 자동 제거한다.
        int removedTotal = 0;
        foreach (var t in panelGO.GetComponentsInChildren<Transform>(true))
            removedTotal += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        if (removedTotal > 0)
            Debug.LogWarning($"[NotePanelEditor] 프리팹 저장 전 missing script 컴포넌트 {removedTotal}개를 자동 제거했습니다 " +
                              "(재컴파일 타이밍 문제일 가능성 — 저장 후 플레이 모드를 재시작해 관련 기능이 다시 정상 동작하는지 확인하세요).");

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
        PanelPrefabAssignHelper.RequestAssign(panel, "_panelPrefab", savePath);

        EditorUtility.DisplayDialog("완료 (플레이 모드 종료 후 자동 반영)",
            $"NotePanel 프리팹이 저장되었습니다.\n{savePath}\n\n" +
            "플레이 모드 중에는 필드 변경이 Stop 시 되돌려지기 때문에, 지금 당장은 반영되지 않습니다.\n" +
            "플레이 모드를 종료하면 '_panelPrefab' 필드가 자동으로 할당되고 씬이 수정됨으로 표시됩니다 — Ctrl+S로 저장하세요.",
            "확인");

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }
}
