using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EventSystemTestbed의 실행 버튼을 기능별 접이식 섹션으로 표시한다.
/// NaughtyAttributes의 Button은 메서드 그룹을 지원하지 않아 별도 인스펙터로 묶는다.
/// </summary>
[CustomEditor(typeof(EventSystemTestbed))]
public class EventSystemTestbedEditor : Editor
{
    private readonly struct TestAction
    {
        public readonly string methodName;
        public readonly string label;

        public TestAction(string methodName, string label)
        {
            this.methodName = methodName;
            this.label = label;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("테스트 실행", EditorStyles.boldLabel);

        DrawSection("화면 연출", false,
            new TestAction("FadeOut", "암전"),
            new TestAction("FadeIn", "암전 해제"),
            new TestAction("Noise", "노이즈 3초"),
            new TestAction("NoiseStop", "노이즈 정지"),
            new TestAction("Flash", "흰 섬광"),
            new TestAction("Tear", "화면 찢김 (더미)"),
            new TestAction("ZoomIn", "클로즈업 줌"),
            new TestAction("ZoomOut", "줌 복귀"),
            new TestAction("Shake", "카메라 흔들기"));

        DrawSection("입력 모드", false,
            new TestAction("InputCutscene", "컷신 모드 (조작 잠금)"),
            new TestAction("InputPlay", "플레이 모드 (잠금 해제)"));

        DrawSection("전투 완료 신호", true,
            new TestAction("CompleteEvt004Battle", "EVT-004: 전투 승리 완료 신호"),
            new TestAction("CompleteEvt006Battle", "EVT-006: 전투 승리 완료 신호"));

        DrawSection("진행도 · 맵 · 단서", false,
            new TestAction("SetFlag", "플래그: 지정 값으로 세팅"),
            new TestAction("DumpFlags", "플래그: 전체 덤프"),
            new TestAction("LogCurrentMap", "맵: 현재 맵 로그"),
            new TestAction("ChangeMap", "맵: 지정 맵으로 전환"),
            new TestAction("GrantClue", "단서: 지급 + 도감 열기"),
            new TestAction("DumpClues", "단서: 습득 목록 덤프"),
            new TestAction("DumpLinks", "낙서: 노트 연결 상태 덤프"),
            new TestAction("EvaluateReadings", "낙서: 해몽 강제 실행"));

        DrawSection("기어", false,
            new TestAction("ClearAllGears", "기어: 전부 잃어버리기"));

        DrawSection("사망", false,
            new TestAction("KillPlayer", "플레이어 즉사"));

        DrawSection("EVT-002 실제 이벤트 경로", false,
            new TestAction("SpawnDummyNpcs", "더미 NPC 2명 생성 + 이벤트 타깃 등록"),
            new TestAction("RunEvt002RealPath", "EVT-002 실제 경로 실행 (EventManager 경유)"),
            new TestAction("AdvanceChain", "체인 진행 시도 (조건 통과 이벤트 1개 실행)"),
            new TestAction("DumpProgress", "진행 상태 요약"));

    }

    private void DrawSection(string title, bool defaultExpanded, params TestAction[] actions)
    {
        string preferenceKey = $"EventSystemTestbed.{target.GetInstanceID()}.{title}";
        bool expanded = SessionState.GetBool(preferenceKey, defaultExpanded);
        expanded = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
        SessionState.SetBool(preferenceKey, expanded);

        if (!expanded)
            return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            foreach (TestAction action in actions)
            {
                if (GUILayout.Button(action.label))
                    InvokeTestAction(action.methodName);
            }
        }
    }

    private void InvokeTestAction(string methodName)
    {
        MethodInfo method = typeof(EventSystemTestbed).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (method == null)
        {
            Debug.LogError($"[EventSystemTestbedEditor] 테스트 메서드 '{methodName}'을 찾을 수 없습니다.", target);
            return;
        }

        try
        {
            method.Invoke(target, null);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception, target);
        }
    }
}
