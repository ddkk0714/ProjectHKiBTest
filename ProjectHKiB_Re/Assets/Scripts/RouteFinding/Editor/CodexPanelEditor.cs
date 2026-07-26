using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using RouteFinding.Codex;
using RouteFinding.Editor;

[CustomEditor(typeof(CodexPanel))]
public class CodexPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        GUILayout.Label("── 프리팹 도구 ──", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("CodexPanel 프리팹 생성 (플레이 중)", GUILayout.Height(30)))
            GeneratePrefab((CodexPanel)target);
        EditorGUI.EndDisabledGroup();

        // 플레이 모드와 무관하게 항상 가능 — 이미 디스크에 저장된 CodexPanel.prefab 파일 자체에
        // (예전 재컴파일 타이밍 문제로) missing script 컴포넌트가 구워져 있는 경우, 위 "프리팹 생성"
        // 버튼을 다시 눌러 덮어쓰기 전까지는 이 버튼으로 그 파일을 직접 열어 정리할 수 있다.
        if (GUILayout.Button("기존 프리팹 파일 정리 (missing script 제거)", GUILayout.Height(24)))
            CleanupExistingPrefabAsset();

        // CodexPanel.BuildUI()는 _panelPrefab보다 먼저 "씬에 CodexPanelRoot가 이미 자식으로 있으면
        // 그걸 그대로 재사용"하는 경로가 있다 — 프리팹 파일을 아무리 고쳐도 씬에 이 스테일 인스턴스가
        // 남아있으면 그쪽이 우선이라 전혀 반영되지 않는다. 이 버튼이 그 씬 인스턴스를 찾아 제거한다.
        if (GUILayout.Button("씬에 저장된 CodexPanelRoot 제거 (스테일 인스턴스)", GUILayout.Height(24)))
            RemoveStaleSceneInstances();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서 도감이 열린 상태일 때 버튼을 눌러 프리팹을 생성하세요.\n생성된 프리팹을 위의 '프리팹' 필드에 할당하면 다음 실행부터 코드 대신 프리팹을 사용합니다.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "이 버튼은 CodexPanelRoot 전체(하위의 Drawer/Card 안에 있는 CodexDrawerTreeView·CodexCardView " +
                "컴포넌트의 행 템플릿 필드 — _categoryTemplate/_rowTemplate/_suggestionRowTemplate/_commentRowTemplate " +
                "포함)를 지금 이 순간 상태 그대로 저장합니다.\n\n" +
                "행/카드 템플릿 프리팹을 새로 만들어서 위 필드들에 할당했다면, 그 할당 '이후'에 이 버튼을 " +
                "다시 눌러야 프리팹 파일에 반영됩니다 — 필드만 바꾸고 이 버튼을 안 누르면, 플레이 모드를 " +
                "종료하는 순간 그 할당은 저장된 데가 없어 그냥 사라집니다(CodexPanelRoot 자체가 플레이 중에만 " +
                "존재하는 오브젝트라서). 순서: ① 행 커스터마이징 + 템플릿 필드 할당 → ② 이 버튼 클릭 → " +
                "③ 플레이 모드 종료 → ④ Ctrl+S로 씬 저장.",
                MessageType.Warning);
        }
    }

    private static void GeneratePrefab(CodexPanel panel)
    {
        var panelGO = panel.GetPanelGO();
        if (panelGO == null)
        {
            EditorUtility.DisplayDialog("오류",
                "CodexPanelRoot GO가 없습니다.\n도감을 한 번 열어(C키) CodexPanelRoot가 활성화된 상태에서 시도하세요.",
                "확인");
            return;
        }

        const string saveDir  = "Assets/Scripts/RouteFinding/Codex";
        const string savePath = saveDir + "/CodexPanel.prefab";

        System.IO.Directory.CreateDirectory(saveDir);

        // TreeScroll/Viewport/Content 밑에는 CodexDrawerTreeView가 UiRowPool로 관리하는 카테고리/행
        // GameObject가 지금 이 순간 몇 개 그려져 있는 상태 그대로 들어있다 — panelGO를 그대로 저장하면
        // 이 "실행 중이던 스냅샷" 행들이 프리팹에 그대로 구워지고, 다음 실행에서 프리팹을 재사용하면
        // CodexDrawerTreeView.Init()이 새 UiRowPool을 빈 목록으로 시작해 그 구워진 행들을 전혀 모른 채
        // SetGroups()가 또 새 행을 만들어 겹쳐 쌓인다(중복 표시 오류의 원인). panelGO 원본(플레이 중인
        // 실제 씬 오브젝트, UiRowPool이 참조를 들고 있음)을 직접 건드리면 그 참조가 끊겨 다음 Get() 호출이
        // 죽은 오브젝트를 재사용하려다 예외를 낼 위험이 있으므로, 반드시 복사본에서만 비운다.
        var tempCopy = Instantiate(panelGO);
        tempCopy.name = panelGO.name;
        var contentTF = tempCopy.transform.Find("Drawer/TreeScroll/Viewport/Content");
        if (contentTF != null)
        {
            for (int i = contentTF.childCount - 1; i >= 0; i--)
                DestroyImmediate(contentTF.GetChild(i).gameObject);
        }
        else
        {
            Debug.LogWarning("[CodexPanelEditor] TreeScroll/Viewport/Content 경로를 찾지 못했습니다 — 구조가 바뀌었다면 이 경로도 갱신해야 합니다.");
        }

        // 방어적 정리 — 스크립트를 고친 직후(재컴파일이 아직 안 끝난 시점) 이 버튼을 누르면, 런타임에
        // AddComponent된 타입이 옛 어셈블리 기준으로 붙어있어 "missing script" 컴포넌트로 그대로
        // 구워지는 사고가 실제로 여러 번 반복됐다(KeywordsValue의 CodexKeywordLinkHandler가 그 사례).
        // 재컴파일 타이밍은 이 코드가 통제할 수 없으니, 저장 직전에 계층 전체에서 missing script
        // 컴포넌트를 훑어 지워 저장 자체가 실패하거나 깨진 채로 구워지는 일이 없게 한다.
        int removedTotal = 0;
        foreach (var t in tempCopy.GetComponentsInChildren<Transform>(true))
            removedTotal += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        if (removedTotal > 0)
            Debug.LogWarning($"[CodexPanelEditor] 프리팹 저장 전 missing script 컴포넌트 {removedTotal}개를 자동 제거했습니다 " +
                              "(재컴파일 타이밍 문제로 런타임에 붙은 컴포넌트가 옛 타입으로 남아있었을 가능성 — " +
                              "저장 후 플레이 모드를 재시작해 관련 기능이 다시 정상 동작하는지 확인하세요).");

        var prefab = PrefabUtility.SaveAsPrefabAsset(tempCopy, savePath, out bool success);
        DestroyImmediate(tempCopy);
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
            $"CodexPanel 프리팹이 저장되었습니다.\n{savePath}\n\n" +
            "플레이 모드 중에는 필드 변경이 Stop 시 되돌려지기 때문에, 지금 당장은 반영되지 않습니다.\n" +
            "플레이 모드를 종료하면 '_panelPrefab' 필드가 자동으로 할당되고 씬이 수정됨으로 표시됩니다 — Ctrl+S로 저장하세요.",
            "확인");

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    // 플레이 모드 없이 디스크의 CodexPanel.prefab 파일 자체를 직접 열어(PrefabUtility.LoadPrefabContents)
    // missing script 컴포넌트를 지우고 다시 저장한다 — GeneratePrefab의 정리 로직은 "다음에 다시 저장할
    // 때"만 적용돼서 이미 파일에 구워진 옛 corruption(KeywordsValue의 CodexKeywordLinkHandler 사례처럼
    // 재컴파일 타이밍 문제로 붙었던 컴포넌트)은 소급 반영이 안 됐다 — 이 메뉴/버튼이 그 파일을 직접 고친다.
    [MenuItem("RouteFinding/도감 프리팹 정리 (missing script 제거)")]
    private static void CleanupExistingPrefabAsset()
    {
        const string path = "Assets/Scripts/RouteFinding/Codex/CodexPanel.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            EditorUtility.DisplayDialog("오류", $"프리팹을 찾을 수 없습니다: {path}", "확인");
            return;
        }

        var contentsRoot = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int removedScripts = 0;
            foreach (var t in contentsRoot.GetComponentsInChildren<Transform>(true))
                removedScripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            // 이전에 GeneratePrefab이 이 정리를 하기 전에 저장된 파일이라면, 실행 중이던 스냅샷
            // 카테고리/행이 Content 밑에 그대로 구워져 있을 수도 있다 — 같이 비운다.
            var contentTF = contentsRoot.transform.Find("Drawer/TreeScroll/Viewport/Content");
            int removedRows = 0;
            if (contentTF != null)
            {
                removedRows = contentTF.childCount;
                for (int i = contentTF.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(contentTF.GetChild(i).gameObject);
            }

            // "다 펼쳐도 맨 아래까지 스크롤 안 되는" 문제의 근본 원인은 결국 못 고쳤다(CLAUDE.md 미해결
            // 버그 참고, childControlHeight를 true로 바꿔봤다가 오히려 패널이 눌리는 더 큰 회귀가 나서
            // 되돌림) — 대신 CodexPanel.BuildDrawer()에서 Content의 아래쪽 패딩을 크게(400) 잡아
            // 스크롤 여유 공간으로 우회했다. 이미 저장된 프리팹 파일은 코드를 고쳐도 반영이 안 되므로
            // (VerticalLayoutGroup 설정이 파일에 직렬화됨) 여기서 같이 맞춰준다.
            bool fixedPadding = false;
            if (contentTF != null)
            {
                var vlg = contentTF.GetComponent<VerticalLayoutGroup>();
                if (vlg != null && vlg.padding.bottom < 400)
                {
                    var padding = vlg.padding;
                    padding.bottom = 400;
                    vlg.padding = padding;
                    fixedPadding = true;
                }
            }

            if (removedScripts == 0 && removedRows == 0 && !fixedPadding)
            {
                EditorUtility.DisplayDialog("정리할 항목 없음", "missing script나 구워진 행, 스크롤 여유 패딩 부족 문제가 없습니다.", "확인");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(contentsRoot, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("완료",
                $"missing script 컴포넌트 {removedScripts}개, 구워진 행 {removedRows}개를 제거하고" +
                (fixedPadding ? ", Content 아래쪽 여유 패딩을 400으로 늘려" : "") +
                $" 저장했습니다.\n{path}",
                "확인");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contentsRoot);
        }
    }

    // CodexPanel.BuildUI()의 최우선 재사용 경로("씬에 CodexPanelRoot가 이미 자식으로 있으면 그대로 씀")가
    // 찾는 대상을 직접 제거한다 — 이 오브젝트는 원래 플레이 중에만 존재해야 하는데(HelpBox 문구 참고),
    // 과거 어느 시점에 씬 파일 자체에 저장돼버려서 프리팹/코드를 아무리 고쳐도 계속 그 스테일 상태가
    // 재사용되고 있었다(MapViewer의 MapPanel.prefab 배치 인스턴스와 같은 유형의 함정). 제거하고 나면
    // 다음 플레이부터 BuildUI()가 _panelPrefab(이미 정리된 상태) 또는 런타임 코드로 새로 만든다.
    [MenuItem("RouteFinding/도감 씬 CodexPanelRoot 제거 (스테일 인스턴스)")]
    private static void RemoveStaleSceneInstances()
    {
        var panels = Resources.FindObjectsOfTypeAll<CodexPanel>();
        int removed = 0;
        foreach (var panel in panels)
        {
            // 프로젝트 에셋(프리팹 소스) 안에 있는 CodexPanel은 제외 — 실제 씬에 배치된 것만 대상.
            if (!panel.gameObject.scene.IsValid()) continue;

            var existing = panel.transform.Find("CodexPanelRoot");
            if (existing == null) continue;

            Debug.Log($"[CodexPanelEditor] 씬 '{panel.gameObject.scene.name}'의 '{panel.name}' 밑에 저장돼 있던 스테일 CodexPanelRoot를 제거합니다.");
            Object.DestroyImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            removed++;
        }

        if (removed == 0)
        {
            EditorUtility.DisplayDialog("정리할 항목 없음", "씬에 저장된 CodexPanelRoot 인스턴스가 없습니다.", "확인");
            return;
        }

        EditorUtility.DisplayDialog("완료",
            $"씬에 저장돼 있던 스테일 CodexPanelRoot {removed}개를 제거했습니다.\n" +
            "Ctrl+S로 씬을 저장하세요 — 다음 플레이부터 프리팹(또는 코드)으로 새로 생성됩니다.",
            "확인");
    }
}
