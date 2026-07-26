#if UNITY_EDITOR
using NaughtyAttributes.Editor;
using UnityEditor;
using UnityEngine;

// EmotionVectorTable_Default 에셋을 선택했을 때 인스펙터에서 바로 부호 검증 리포트를 돌릴 수 있게
// 상단 메뉴(Emotion/...) 대신 이 에셋의 인스펙터에 버튼으로 옮김. NaughtyInspector를 상속해
// 기존 ReorderableList 등 NaughtyAttributes 드로잉은 그대로 유지하고 버튼만 추가한다.
[CustomEditor(typeof(EmotionVectorTableSO))]
public class EmotionVectorTableSOEditor : NaughtyInspector
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        EditorGUILayout.Space();
        if (GUILayout.Button("Vector Stat Comparison Report"))
        {
            EmotionVectorStatComparisonReport.Run();
        }
    }
}
#endif
