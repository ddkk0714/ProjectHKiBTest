using UnityEditor;
using UnityEngine;

namespace RouteFinding.Editor
{
    // CodexPanelEditor/NotePanelEditor/MapViewerEditor가 공유하는 헬퍼.
    //
    // 문제: "프리팹 생성" 버튼은 패널 GO가 런타임에만 존재해서 Application.isPlaying일 때만 눌린다.
    // 이 시점에 SerializedObject로 _panelPrefab 필드를 곧바로 할당해도, Unity는 플레이 모드 시작 전부터
    // 씬에 있던 오브젝트의 필드 변경을 Stop 시 전부 되돌린다 — 그래서 "등록됐다"는 다이얼로그까지 봤는데도
    // 다음 플레이에서는 필드가 다시 비어 있고, 런타임 자동 생성 경로로 빠지는 문제가 생겼다.
    //
    // 해결: 즉시 할당하는 대신 EditorPrefs에 "무엇을 할당해야 하는지"만 기록해두고, 실제 할당은
    // [InitializeOnLoad] 정적 생성자(도메인 리로드마다 항상 다시 실행됨 — 플레이 모드 종료도 기본적으로
    // 도메인 리로드를 유발하므로 이 시점에 걸린다) 쪽에서 EditorApplication.delayCall로 한 박자 늦춰
    // "이제 플레이 중이 아님"을 확인한 뒤 수행한다. 도메인 리로드 여부(Enter Play Mode Settings)와
    // 무관하게 동작한다 — 리로드가 있으면 정적 생성자가 다시 실행되며 픽업하고, 없으면 델리게이트가
    // 살아있는 채로 Stop 이후 delayCall이 그대로 실행된다.
    [InitializeOnLoad]
    public static class PanelPrefabAssignHelper
    {
        private const string PrefKeyTargetId  = "RouteFinding_PendingPrefabAssign_TargetId";
        private const string PrefKeyFieldName = "RouteFinding_PendingPrefabAssign_FieldName";
        private const string PrefKeyPrefabPath = "RouteFinding_PendingPrefabAssign_PrefabPath";

        static PanelPrefabAssignHelper()
        {
            EditorApplication.delayCall += TryApplyPending;
        }

        // 플레이 모드 중 호출 — 즉시 할당하지 않고 예약만 해둔다.
        public static void RequestAssign(Object target, string fieldName, string prefabAssetPath)
        {
            var id = GlobalObjectId.GetGlobalObjectIdSlow(target);
            EditorPrefs.SetString(PrefKeyTargetId, id.ToString());
            EditorPrefs.SetString(PrefKeyFieldName, fieldName);
            EditorPrefs.SetString(PrefKeyPrefabPath, prefabAssetPath);
        }

        private static void TryApplyPending()
        {
            if (Application.isPlaying)
            {
                // 아직 플레이 중(예: 스크립트 컴파일로 인한 리로드)이면 되돌려질 것이므로 대기.
                // 다음 도메인 리로드/에디터 업데이트에서 다시 시도된다.
                return;
            }
            if (!EditorPrefs.HasKey(PrefKeyTargetId)) return;

            string idStr      = EditorPrefs.GetString(PrefKeyTargetId);
            string fieldName  = EditorPrefs.GetString(PrefKeyFieldName);
            string prefabPath = EditorPrefs.GetString(PrefKeyPrefabPath);
            ClearPending();

            if (!GlobalObjectId.TryParse(idStr, out var gid)) return;
            var target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (target == null)
            {
                Debug.LogWarning("[PanelPrefabAssignHelper] 대상 오브젝트를 찾을 수 없어 프리팹 필드 할당을 건너뜁니다.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[PanelPrefabAssignHelper] 프리팹 에셋을 찾을 수 없습니다: {prefabPath}");
                return;
            }

            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[PanelPrefabAssignHelper] '{fieldName}' 필드를 찾을 수 없습니다.");
                return;
            }
            prop.objectReferenceValue = prefab;
            so.ApplyModifiedProperties();

            if (target is Component comp)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);

            Debug.Log($"[PanelPrefabAssignHelper] 플레이 모드 종료 후 '{fieldName}' 필드에 프리팹을 자동 할당했습니다: {prefabPath}\n" +
                      "씬이 수정됨 표시되었습니다 — Ctrl+S로 저장해야 다음에도 유지됩니다.");
        }

        private static void ClearPending()
        {
            EditorPrefs.DeleteKey(PrefKeyTargetId);
            EditorPrefs.DeleteKey(PrefKeyFieldName);
            EditorPrefs.DeleteKey(PrefKeyPrefabPath);
        }
    }
}
