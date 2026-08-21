using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RouteFinding.Internet;
using RouteFinding.Editor;

// CodexPanelEditor와 같은 프리팹 워크플로우를 인터넷 창에도 제공한다 — 플레이 중에 만들어진
// InternetPanelRoot를 프리팹으로 구워두면, 다음 실행부터는 코드 생성 대신 그 프리팹을 인스턴스화하므로
// Prefab Mode에서 디자인을 자유롭게 손볼 수 있다(InternetPanel.BuildUI의 3갈래 참고).
[CustomEditor(typeof(InternetPanel))]
public class InternetPanelEditor : Editor
{
    private const string SaveDir  = "Assets/Scripts/RouteFinding/Internet";
    private const string SavePath = SaveDir + "/InternetPanel.prefab";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        GUILayout.Label("── 프리팹 도구 ──", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("InternetPanel 프리팹 생성 (플레이 중)", GUILayout.Height(30)))
            GeneratePrefab((InternetPanel)target);
        EditorGUI.EndDisabledGroup();

        // BuildUI()는 _panelPrefab보다 "씬에 이미 있는 InternetPanelRoot"를 우선 재사용한다 —
        // 그 스테일 인스턴스가 씬 파일에 저장돼 있으면 프리팹/코드를 고쳐도 반영되지 않는다.
        if (GUILayout.Button("씬에 저장된 InternetPanelRoot 제거 (스테일 인스턴스)", GUILayout.Height(24)))
            RemoveStaleSceneInstances();

        EditorGUILayout.HelpBox(
            Application.isPlaying
                ? "지금 이 순간의 InternetPanelRoot 상태를 그대로 저장합니다. 행 템플릿 필드(_siteRowTemplate 등)를 " +
                  "새로 할당했다면 그 '이후'에 눌러야 프리팹에 반영됩니다.\n" +
                  "순서: ① 커스터마이징 → ② 이 버튼 → ③ 플레이 모드 종료 → ④ Ctrl+S로 씬 저장."
                : "플레이 모드에서 인터넷 창을 한 번 연 뒤(Q) 버튼을 눌러 프리팹을 생성하세요.",
            Application.isPlaying ? MessageType.Warning : MessageType.Info);
    }

    private static void GeneratePrefab(InternetPanel panel)
    {
        var panelGO = panel.GetPanelGO();
        if (panelGO == null)
        {
            EditorUtility.DisplayDialog("오류",
                "InternetPanelRoot GO가 없습니다.\n인터넷 창을 한 번 열어(Q) 활성화된 상태에서 시도하세요.", "확인");
            return;
        }

        System.IO.Directory.CreateDirectory(SaveDir);

        // 목록 Content 밑에는 UiRowPool이 지금 그려둔 사이트/게시글 행이 들어 있다 — 그대로 구우면
        // 다음 실행에서 새 풀이 그 행들을 모른 채 또 만들어 겹쳐 쌓인다(CodexPanelEditor와 같은 함정).
        var tempCopy = Instantiate(panelGO);
        tempCopy.name = panelGO.name;
        var contentTF = tempCopy.transform.Find("Drawer/SiteList/Viewport/Content");
        if (contentTF != null)
        {
            for (int i = contentTF.childCount - 1; i >= 0; i--)
                DestroyImmediate(contentTF.GetChild(i).gameObject);
        }
        else Debug.LogWarning("[InternetPanelEditor] Drawer/SiteList/Viewport/Content 경로를 찾지 못했습니다 — 구조가 바뀌었다면 이 경로도 갱신해야 합니다.");

        // 본문 쪽 첨부/단서/댓글 행도 마찬가지로 런타임 스냅샷이다.
        foreach (var sectionName in new[] { "PostAttachments", "PostClues", "PostComments" })
        {
            var sectionTF = FindDeep(tempCopy.transform, sectionName);
            if (sectionTF == null) continue;
            for (int i = sectionTF.childCount - 1; i >= 0; i--)
            {
                var child = sectionTF.GetChild(i);
                // 정적으로 만든 구분선/헤더는 남기고 풀이 만든 행만 지운다.
                if (child.name == "Sep" || child.name.EndsWith("Header")) continue;
                DestroyImmediate(child.gameObject);
            }
        }

        int removed = 0;
        foreach (var t in tempCopy.GetComponentsInChildren<Transform>(true))
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        if (removed > 0)
            Debug.LogWarning($"[InternetPanelEditor] 프리팹 저장 전 missing script 컴포넌트 {removed}개를 자동 제거했습니다.");

        var prefab = PrefabUtility.SaveAsPrefabAsset(tempCopy, SavePath, out bool success);
        DestroyImmediate(tempCopy);
        if (!success || prefab == null)
        {
            EditorUtility.DisplayDialog("오류", "프리팹 저장에 실패했습니다.", "확인");
            return;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 플레이 중 필드 변경은 Stop 시 되돌려지므로 예약만 해둔다(CodexPanelEditor와 동일).
        PanelPrefabAssignHelper.RequestAssign(panel, "_panelPrefab", SavePath);

        EditorUtility.DisplayDialog("완료 (플레이 모드 종료 후 자동 반영)",
            $"InternetPanel 프리팹이 저장되었습니다.\n{SavePath}\n\n" +
            "플레이 모드를 종료하면 '_panelPrefab' 필드가 자동 할당됩니다 — Ctrl+S로 씬을 저장하세요.", "확인");

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    [MenuItem("RouteFinding/인터넷 씬 InternetPanelRoot 제거 (스테일 인스턴스)")]
    private static void RemoveStaleSceneInstances()
    {
        var panels = Resources.FindObjectsOfTypeAll<InternetPanel>();
        int removed = 0;
        foreach (var panel in panels)
        {
            if (!panel.gameObject.scene.IsValid()) continue; // 프리팹 에셋 안의 것은 제외

            var existing = panel.transform.Find("InternetPanelRoot");
            if (existing == null) continue;

            Debug.Log($"[InternetPanelEditor] 씬 '{panel.gameObject.scene.name}'의 '{panel.name}' 밑에 저장돼 있던 스테일 InternetPanelRoot를 제거합니다.");
            Object.DestroyImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            removed++;
        }

        if (removed == 0)
        {
            EditorUtility.DisplayDialog("정리할 항목 없음", "씬에 저장된 InternetPanelRoot 인스턴스가 없습니다.", "확인");
            return;
        }

        EditorUtility.DisplayDialog("완료",
            $"씬에 저장돼 있던 스테일 InternetPanelRoot {removed}개를 제거했습니다.\nCtrl+S로 씬을 저장하세요.", "확인");
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
