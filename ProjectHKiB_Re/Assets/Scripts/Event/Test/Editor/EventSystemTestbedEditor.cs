using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EventSystemTestbed의 실행 버튼을 기능별 접이식 섹션으로 표시한다.
/// NaughtyAttributes의 Button은 메서드 그룹을 지원하지 않아 별도 인스펙터로 묶는다.
///
/// [주의] 이 CustomEditor가 기본 인스펙터를 **대체**하므로, 테스트베드에 [Button]을 붙여도
/// 아래 섹션 목록에 이름을 적지 않으면 화면에 나오지 않는다. 실제로 그렇게 조용히 사라진 적이
/// 있어서, 지금은 마지막에 "섹션 미등록" 항목을 자동으로 훑어 남김없이 그린다 -
/// 거기 뜨는 버튼은 곧 "섹션에 넣어 달라"는 신호다.
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

        // 가장 자주 쓰는 섹션이라 기본으로 펼쳐 둔다 - 접혀 있으면 버튼이 아예 없는 걸로 보인다.
        DrawSection("화면 연출", true,
            new TestAction("FadeOut", "암전"),
            new TestAction("FadeIn", "암전 해제"),
            new TestAction("Noise", "노이즈 3초"),
            new TestAction("NoiseStop", "노이즈 정지"),
            new TestAction("Glitch", "글리치"),
            new TestAction("GlitchStop", "글리치 정지"),
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

        DrawUnlistedButtons();
    }

    // 위 섹션 어디에도 이름이 없는 [Button] 메서드를 찾아 그린다.
    // 이 목록은 손으로 관리하는 것이라 새 버튼을 추가하고 등록을 빠뜨리기 쉬운데, 그러면 기본
    // 인스펙터가 대체된 탓에 버튼이 조용히 사라진다. 여기 뜨는 게 있으면 위 섹션에 옮겨 적을 것.
    private void DrawUnlistedButtons()
    {
        var unlisted = new List<(string methodName, string label)>();

        MethodInfo[] methods = typeof(EventSystemTestbed).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (MethodInfo method in methods)
        {
            var button = method.GetCustomAttribute<NaughtyAttributes.ButtonAttribute>();
            if (button == null || _listedMethods.Contains(method.Name)) continue;
            unlisted.Add((method.Name, string.IsNullOrEmpty(button.Text) ? method.Name : button.Text));
        }

        if (unlisted.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "아래 버튼들은 섹션에 등록되지 않았습니다. EventSystemTestbedEditor의 DrawSection 목록에 " +
            "추가하면 알맞은 자리에 표시됩니다.", MessageType.Info);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            foreach ((string methodName, string label) in unlisted)
            {
                if (GUILayout.Button(label))
                    InvokeTestAction(methodName);
            }
        }
    }

    // DrawSection이 지나간 메서드 이름 - DrawUnlistedButtons가 "빠진 것"을 가려내는 데 쓴다.
    private readonly HashSet<string> _listedMethods = new();

    private void DrawSection(string title, bool defaultExpanded, params TestAction[] actions)
    {
        foreach (TestAction action in actions) _listedMethods.Add(action.methodName);

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
