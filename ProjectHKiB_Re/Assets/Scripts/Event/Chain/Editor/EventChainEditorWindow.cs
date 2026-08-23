using System;
using System.Collections.Generic;
using System.Linq;
using StateMachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// EventChainSO 하나를 편집하고, "빌드"로 실제 EventSO/StateSO/트리거 프리팹(런타임이 도는 형태)을
/// 만들어내는 개발자 창. RouteFinding의 맵 DB 편집기(MapDatabaseEditorWindow)와 같은 자리 —
/// 데이터는 SO(이쪽은 JSON 대신 ScriptableObject) 하나에 모으고, 이 창이 목록+상세 GUI로 편집한다.
///
/// [빌드가 하는 일과 안 하는 일] EventChainSO의 각 EventDefinition을 StateSO 사슬 하나로 펼쳐서
/// Assets/Scripts/Event/Test/Generated/에 저장한다. 실행 경로(EventManager/GameStateEvent/
/// StateController)는 그대로다 — 이 창은 그 경로가 먹는 "데이터"만 다르게 만드는 저작 도구다.
///
/// [재빌드해도 씬 참조가 안 끊긴다] 같은 이름의 에셋이 이미 있으면 새로 만들지 않고 필드만
/// 덮어쓴다(GUID 보존). 프리팹도 PrefabUtility.LoadPrefabContents로 기존 파일을 직접 고친다 —
/// 지우고 다시 만들면 매번 새 GUID가 나와 씬에 이미 꽂아둔 트리거 참조가 다시 비게 된다.
/// </summary>
public class EventChainEditorWindow : EditorWindow
{
    private const string OutputFolder = "Assets/Scripts/Event/Test/Generated";
    private const string FlagFolder = "Assets/ScriptableObjects/EventFlags";
    private const float DialogueTimeout = 15f;

    // 샘플 데이터(EVT-001~004)가 쓰는 대상 ID/단서 ID. 실제 콘텐츠를 만들 때는 이 창에서
    // targets/enterActions를 직접 편집하면 되고, 이 상수들은 "샘플 채우기" 전용이다.
    private const string PlayerTargetID = "Player";
    private const string NpcATargetID = "NPC_A";
    private const string NpcBTargetID = "NPC_B";
    // clues.json 전용 더미 항목(dummy-clue-*) — 실제 맵/이벤트키에 매이지 않고 순수하게
    // 이벤트 체인 + 해몽 판정을 테스트하기 위한 것. reading_dummy_test(DreamReadings.asset)의
    // requiredClueIds와 짝을 맞춰야 한다.
    private const string DummyClueWingId = "dummy-clue-wing";
    private const string DummyClueEyeId = "dummy-clue-eye";

    // 플레이어 body 스프라이트가 쓰는 정렬 레이어. 더미 NPC도 같은 값이라야 캐릭터와 같은 깊이에 선다.
    private const string EntitySortingLayer = "Entity";

    [SerializeField] private EventChainSO _chain;
    private SerializedObject _so;
    private SerializedProperty _eventsProp;
    private int _selectedEvent = -1;
    private Vector2 _eventListScroll;
    private Vector2 _detailScroll;

    [MenuItem("Tools/Event/이벤트 체인 편집기")]
    public static void Open()
    {
        var window = GetWindow<EventChainEditorWindow>("이벤트 체인 편집기");
        window.minSize = new Vector2(780f, 480f);
        window.Show();
    }

    private void OnEnable()
    {
        if (_chain == null) return;
        _so = new SerializedObject(_chain);
        _eventsProp = _so.FindProperty("events");
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (_chain == null)
        {
            EditorGUILayout.HelpBox("편집할 EventChainSO 에셋을 선택하거나 새로 만드세요.", MessageType.Info);
            return;
        }

        _so.Update();
        EditorGUILayout.BeginHorizontal();
        DrawEventList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
        _so.ApplyModifiedProperties();
    }

    // ─── 툴바 ────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        var newChain = (EventChainSO)EditorGUILayout.ObjectField(_chain, typeof(EventChainSO), false, GUILayout.Width(240f));
        if (newChain != _chain)
        {
            _chain = newChain;
            _so = _chain != null ? new SerializedObject(_chain) : null;
            _eventsProp = _so?.FindProperty("events");
            _selectedEvent = -1;
        }

        if (GUILayout.Button("새 체인 만들기", EditorStyles.toolbarButton, GUILayout.Width(110f)))
            CreateNewChain();

        GUILayout.FlexibleSpace();

        GUI.enabled = _chain != null;
        if (_chain != null)
        {
            // 로그가 너무 빨리 지나가 흐름을 못 볼 때 올려서 빌드한다. 확인이 끝나면 1로 되돌릴 것.
            //
            // 툴바는 OnGUI에서 _so.Update()보다 먼저 그려지므로, 여기서 SerializedProperty를 고치면
            // 바로 뒤의 Update()가 에셋 값으로 되돌려 입력이 씹힌다 — 대상 객체에 직접 쓴다.
            EditorGUILayout.LabelField("연출 배속", GUILayout.Width(56f));
            float multiplier = EditorGUILayout.FloatField(_chain.stepTimeoutMultiplier, GUILayout.Width(40f));
            if (!Mathf.Approximately(multiplier, _chain.stepTimeoutMultiplier))
            {
                Undo.RecordObject(_chain, "연출 배속 변경");
                _chain.stepTimeoutMultiplier = Mathf.Max(0.1f, multiplier);
                EditorUtility.SetDirty(_chain);
            }
        }

        if (GUILayout.Button("샘플(EVT-001~004) 채우기", EditorStyles.toolbarButton, GUILayout.Width(190f)))
            FillSampleChain();

        if (GUILayout.Button("빌드", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            BuildChain();

        if (GUILayout.Button("씬에 데모 배치", EditorStyles.toolbarButton, GUILayout.Width(110f)))
            PlaceDemoInScene();
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();
    }

    private void CreateNewChain()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "새 이벤트 체인", "EventChain", "asset", "저장할 위치를 고르세요.", OutputFolder);
        if (string.IsNullOrEmpty(path)) return;

        EnsureFolderFor(path);
        var asset = ScriptableObject.CreateInstance<EventChainSO>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        _chain = asset;
        _so = new SerializedObject(_chain);
        _eventsProp = _so.FindProperty("events");
        _selectedEvent = -1;
    }

    // ─── 목록(왼쪽) ──────────────────────────────────────────────

    private void DrawEventList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(220f));
        EditorGUILayout.LabelField("이벤트", EditorStyles.boldLabel);

        _eventListScroll = EditorGUILayout.BeginScrollView(_eventListScroll, GUILayout.ExpandHeight(true));
        for (int i = 0; i < _eventsProp.arraySize; i++)
        {
            SerializedProperty entry = _eventsProp.GetArrayElementAtIndex(i);
            string id = entry.FindPropertyRelative("eventId").stringValue;
            string name = entry.FindPropertyRelative("eventName").stringValue;
            string label = string.IsNullOrEmpty(id) ? $"(이름 없음 #{i})"
                : string.IsNullOrEmpty(name) ? id : $"{id} · {name}";

            GUI.backgroundColor = i == _selectedEvent ? new Color(0.45f, 0.65f, 1f) : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(24f)))
                _selectedEvent = i;
            GUI.backgroundColor = Color.white;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 이벤트 추가"))
        {
            _eventsProp.InsertArrayElementAtIndex(_eventsProp.arraySize);
            SerializedProperty added = _eventsProp.GetArrayElementAtIndex(_eventsProp.arraySize - 1);
            // InsertArrayElementAtIndex가 직전 원소를 복제하는 Unity 특성이 있어, 새로 넣는 값이면
            // 반드시 여기서 필드를 전부 명시적으로 비워야 이전 이벤트 데이터가 안 딸려온다.
            added.FindPropertyRelative("eventId").stringValue = $"EVT-{_eventsProp.arraySize:000}";
            added.FindPropertyRelative("eventName").stringValue = "";
            added.FindPropertyRelative("purpose").stringValue = "";
            added.FindPropertyRelative("startTriggerDesc").stringValue = "";
            added.FindPropertyRelative("preconditionDesc").stringValue = "";
            added.FindPropertyRelative("interruptCondition").stringValue = "";
            added.FindPropertyRelative("retryPolicy").stringValue = "";
            added.FindPropertyRelative("linkedEvents").stringValue = "";
            added.FindPropertyRelative("narrativeContent").stringValue = "";
            added.FindPropertyRelative("targets").ClearArray();
            added.FindPropertyRelative("preconditions").ClearArray();
            added.FindPropertyRelative("triggerKind").enumValueIndex = (int)EventTriggerKind.None;
            added.FindPropertyRelative("triggerRadius").floatValue = 1.5f;
            added.FindPropertyRelative("triggerInputType").enumValueIndex = (int)EnumManager.InputType.OnConfirm;
            added.FindPropertyRelative("steps").ClearArray();
            _selectedEvent = _eventsProp.arraySize - 1;
        }
        GUI.enabled = _selectedEvent >= 0 && _selectedEvent < _eventsProp.arraySize;
        if (GUILayout.Button("- 삭제", GUILayout.Width(60f)))
        {
            _eventsProp.DeleteArrayElementAtIndex(_selectedEvent);
            _selectedEvent = -1;
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // ─── 상세(오른쪽) ────────────────────────────────────────────

    private void DrawDetail()
    {
        EditorGUILayout.BeginVertical();

        if (_selectedEvent < 0 || _selectedEvent >= _eventsProp.arraySize)
        {
            EditorGUILayout.HelpBox("왼쪽에서 이벤트를 선택하세요.", MessageType.None);
            EditorGUILayout.EndVertical();
            return;
        }

        SerializedProperty eventProp = _eventsProp.GetArrayElementAtIndex(_selectedEvent);
        string eventId = eventProp.FindPropertyRelative("eventId").stringValue;

        if (IsSampleEventId(eventId))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button($"'{eventId}' 샘플 값으로 되돌리기", GUILayout.Width(220f)))
                ResetSelectedEventToSample(eventId);
            EditorGUILayout.EndHorizontal();
        }

        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

        DrawPlanningSection(eventProp);
        EditorGUILayout.Space(10f);
        DrawImplementationSection(eventProp);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private static bool IsSampleEventId(string eventId) =>
        eventId is "Dummy_EVT001" or "Dummy_EVT002" or "Dummy_EVT003" or "Dummy_EVT004";

    // "샘플로 채우기"가 통째로 4개를 다 덮어쓰는 것과 달리, 실수로 값을 고쳤을 때 그 이벤트
    // 하나만 원래대로 되돌리는 안전장치 — 다른 이벤트에 이미 해 둔 편집은 건드리지 않는다.
    private void ResetSelectedEventToSample(string eventId)
    {
        EventFlagSO dood = LoadFlag("Dood");
        EventFlagSO emotionMerge = LoadFlag("FLAG_EMOTION_MERGE");

        EventDefinition sample = eventId switch
        {
            "Dummy_EVT001" => BuildEvt001Sample(dood),
            "Dummy_EVT002" => BuildEvt002Sample(dood, EnsureBossMachine()),
            "Dummy_EVT003" => BuildEvt003Sample(dood),
            "Dummy_EVT004" => BuildEvt004Sample(dood, emotionMerge),
            _ => null,
        };
        if (sample == null) return;

        _chain.events[_selectedEvent] = sample;
        EditorUtility.SetDirty(_chain);
        AssetDatabase.SaveAssets();
        _so.Update();

        Debug.Log($"[EventChainEditorWindow] '{eventId}'를 샘플 값으로 되돌렸습니다. '빌드'를 눌러야 실행 가능한 에셋에도 반영됩니다.");
    }

    // 낙서세계 기획서 "4. 이벤트 정보" 표와 같은 순서·같은 라벨로 그린다 — 기획서 원본은
    // 저장소에서 추적하지 않으므로, 이 구간이 사실상 그 문서를 대신하는 살아있는 원본이 된다.
    private void DrawPlanningSection(SerializedProperty eventProp)
    {
        EditorGUILayout.LabelField("기획 정보", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("eventId"), new GUIContent("이벤트 ID"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("eventName"), new GUIContent("이벤트 이름"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("purpose"), new GUIContent("목적"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("startTriggerDesc"), new GUIContent("시작 트리거"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("preconditionDesc"), new GUIContent("사전 조건"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("interruptCondition"), new GUIContent("중단 조건"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("retryPolicy"), new GUIContent("재실행 정책"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("linkedEvents"), new GUIContent("연결 대상"));
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("narrativeContent"), new GUIContent("연출 및 내용"));

        EditorGUILayout.EndVertical();
    }

    // 위 서술(사전 조건 등)을 실제로 게임이 판정·실행하는 데이터. "빌드"가 이 구간만 읽는다.
    private void DrawImplementationSection(SerializedProperty eventProp)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("구현 (빌드 시 실제 이벤트로 변환됨)", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("그래프로 보기", GUILayout.Width(110f)))
            OpenGraphViewForSelectedEvent(eventProp.FindPropertyRelative("eventId").stringValue);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginVertical(GUI.skin.box);

        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("preconditions"), new GUIContent("발동 조건 (플래그)"), true);
        EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("targets"), new GUIContent("이벤트 대상"), true);

        EditorGUILayout.Space(4f);
        SerializedProperty triggerKindProp = eventProp.FindPropertyRelative("triggerKind");
        EditorGUILayout.PropertyField(triggerKindProp, new GUIContent("씬 트리거"));
        if ((EventTriggerKind)triggerKindProp.enumValueIndex != EventTriggerKind.None)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("triggerRadius"), new GUIContent("반경"));
            if ((EventTriggerKind)triggerKindProp.enumValueIndex == EventTriggerKind.Input)
                EditorGUILayout.PropertyField(eventProp.FindPropertyRelative("triggerInputType"), new GUIContent("발동 입력"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("단계 (순서대로 진행, 분기 없음)", EditorStyles.boldLabel);
        DrawSteps(eventProp.FindPropertyRelative("steps"));

        EditorGUILayout.EndVertical();
    }

    // 이 창이 만드는 EventSO는 StateMachineSO를 그대로 상속한다 — 캐릭터 상태 기계와 같은 엔진으로
    // 돈다. 다만 우리는 그 그래프(StateMachineGraph, NodeGraphProcessor 노드+와이어)를 손으로
    // 그리지 않고 allStates/transitions를 코드로 직접 채우기 때문에, 처음 만든 그래프는 노드가
    // 하나도 없는 빈 캔버스로 보인다("Open Graph View를 눌러도 아무것도 안 보이는" 이유가 이거다) —
    // 그래프 노드와 allStates 데이터는 별개 표현이고, 보통은 그래프에서 노드를 만들면 거기서
    // allStates가 채워지는 반대 방향으로 동기화된다.
    //
    // StateMachinePacker.BuildGraph가 "packed" 상태 기계(코드/변환으로 채워진 flat 데이터)를 노드로
    // 시각화하는 로직을 이미 갖고 있어 그대로 재사용한다 — 매번 눌러서 최신 상태로 다시 그린다.
    private void OpenGraphViewForSelectedEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
        {
            Debug.LogWarning("[EventChainEditorWindow] 이벤트 ID가 비어 있습니다.");
            return;
        }

        string path = $"{OutputFolder}/{eventId}.asset";
        EventSO evt = AssetDatabase.LoadAssetAtPath<EventSO>(path);
        if (evt == null)
        {
            Debug.LogWarning($"[EventChainEditorWindow] '{path}'가 아직 없습니다 — 먼저 '빌드'를 눌러 이 이벤트를 실제 에셋으로 만드세요.");
            return;
        }

        // 재빌드마다 다시 그리는 것이므로, 전에 만들어둔 그래프 서브에셋이 있으면 먼저 정리한다 —
        // 안 지우면 빈 그래프(예: 인스펙터 버튼을 먼저 눌러본 경우)가 같은 파일 안에 고아로 남는다.
        if (evt.graph != null)
        {
            AssetDatabase.RemoveObjectFromAsset(evt.graph);
            UnityEngine.Object.DestroyImmediate(evt.graph, true);
            evt.graph = null;
        }

        StateMachinePacker.BuildGraph(evt);
        AssetDatabase.SaveAssets();
        evt.OpenGraphView();
    }

    private void DrawSteps(SerializedProperty stepsProp)
    {
        for (int i = 0; i < stepsProp.arraySize; i++)
        {
            SerializedProperty stepProp = stepsProp.GetArrayElementAtIndex(i);
            SerializedProperty labelProp = stepProp.FindPropertyRelative("label");
            string header = $"{i}. " + (string.IsNullOrEmpty(labelProp.stringValue) ? "(라벨 없음)" : labelProp.stringValue);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();
            stepProp.isExpanded = EditorGUILayout.Foldout(stepProp.isExpanded, header, true);
            GUILayout.FlexibleSpace();

            GUI.enabled = i > 0;
            if (GUILayout.Button("↑", GUILayout.Width(24f))) stepsProp.MoveArrayElement(i, i - 1);
            GUI.enabled = i < stepsProp.arraySize - 1;
            if (GUILayout.Button("↓", GUILayout.Width(24f))) stepsProp.MoveArrayElement(i, i + 1);
            GUI.enabled = true;

            if (GUILayout.Button("삭제", GUILayout.Width(44f)))
            {
                stepsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return; // 목록을 건드렸으니 이번 프레임은 여기서 멈춘다 — 다음 Repaint가 새로 그린다.
            }
            EditorGUILayout.EndHorizontal();

            if (stepProp.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(labelProp);
                SerializedProperty enterActions = stepProp.FindPropertyRelative("enterActions");
                EditorGUILayout.PropertyField(enterActions, new GUIContent("진입 액션"), true);

                // 각 액션의 waitAfter는 접힌 상태에선 안 보이므로, 이 단계가 최소 몇 초짜리인지 요약해 준다.
                float totalWait = SumWaits(enterActions);
                if (totalWait > 0f)
                    EditorGUILayout.LabelField(" ", $"액션 사이 대기 합계 {totalWait:0.##}초 " +
                                                    "(대기마다 State가 쪼개진다)", EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                // 애니메이션은 이 단계에서 가장 자주 채우는 칸이라 한 번에 추가할 수 있게 해 둔다.
                // 클립 이름을 비워 두면 아무 일도 하지 않으므로, 클립이 없는 지금 미리 배선해 놔도 안전하다.
                if (GUILayout.Button("+ 애니메이션 빈칸", GUILayout.Width(130f)))
                    AppendStepAction(enterActions, new TargetEntityManipulateAction { targetAction = new PlayAnimationAction() });

                // 액션 없이 대기만 하는 칸. "여기서 1초 쉰다"를 표현하는 가장 단순한 방법이다.
                if (GUILayout.Button("+ 대기만", GUILayout.Width(80f)))
                    AppendStepAction(enterActions, null, 1f);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("advanceWhenAny"), new GUIContent("다음 단계로 (OR)"), true);
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("timeoutSeconds"), new GUIContent("타임아웃(초, 0=없음)"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("+ 단계 추가"))
        {
            stepsProp.InsertArrayElementAtIndex(stepsProp.arraySize);
            SerializedProperty added = stepsProp.GetArrayElementAtIndex(stepsProp.arraySize - 1);
            added.FindPropertyRelative("label").stringValue = "";
            added.FindPropertyRelative("enterActions").ClearArray();
            added.FindPropertyRelative("advanceWhenAny").ClearArray();
            added.FindPropertyRelative("timeoutSeconds").floatValue = 0f;
            added.isExpanded = true;
        }
    }

    // ─── 빌드 — EventChainSO → 실행 가능한 EventSO/StateSO/트리거 ─

    private void BuildChain()
    {
        if (_chain == null) return;

        EnsureFolderFor($"{OutputFolder}/_");
        for (int i = 0; i < _chain.events.Count; i++)
            BuildEventDefinition(_chain.events[i], Mathf.Max(0.1f, _chain.stepTimeoutMultiplier));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[EventChainEditorWindow] 빌드 완료 — {OutputFolder}에 이벤트 {_chain.events.Count}개를 생성/갱신했습니다.");
    }

    private static void BuildEventDefinition(EventDefinition def, float timeoutMultiplier)
    {
        if (def.steps == null || def.steps.Count == 0)
        {
            Debug.LogWarning($"[EventChainEditorWindow] '{def.eventId}'에 단계가 없어 건너뜁니다.");
            return;
        }

        // 단계 하나가 State 하나로 곧장 가지 않는다 — 진입 액션에 걸린 waitAfter마다 State를 쪼갠다.
        // 쪼갠 조각은 "그 시간이 지나면 다음 조각으로" 넘어가는 것 말고는 아무 조건도 없는 대기 State다.
        var slots = new List<BuildSlot>();
        for (int i = 0; i < def.steps.Count; i++)
        {
            EventStepData step = def.steps[i];
            List<(List<StateAction> actions, float waitAfter)> segments = SplitStepByWaits(step);

            StateDecision[] stepAdvance = (step.advanceWhenAny ?? Array.Empty<StateDecision>())
                .Where(d => d != null).ToArray();
            float stepTimeout = step.timeoutSeconds;
            // 배속은 "조건을 기다리다 지쳐 넘어가는 안전장치"를 늘려 로그를 읽게 하려는 것이므로
            // 의도한 연출 간격(대기)에는 걸지 않는다.
            bool scaleStepTimeout = true;

            // 단계의 **마지막** 액션 뒤에 대기를 걸면 액션이 하나도 없는 꼬리 조각이 생긴다.
            // 그 단계에 진행 조건도 타임아웃도 없으면 꼬리 조각은 빠져나갈 길이 없어 영원히 멈춘다 —
            // 이 경우 그 대기를 단계 자신의 타임아웃으로 삼는 게 저작자가 의도한 바다
            // ("이 액션들 실행하고 N초 뒤 다음 단계로").
            bool hasEmptyTail = segments.Count > 1 && segments[^1].actions.Count == 0;
            if (hasEmptyTail && stepAdvance.Length == 0 && stepTimeout <= 0f)
            {
                stepTimeout = segments[^2].waitAfter;
                scaleStepTimeout = false;
                segments.RemoveAt(segments.Count - 1);
            }

            for (int k = 0; k < segments.Count; k++)
            {
                bool isLastSegment = k == segments.Count - 1;
                // 쪼개지지 않은 단계는 예전과 같은 이름을 그대로 쓴다(불필요한 에셋 이름 변경 방지).
                string name = k == 0 ? $"{def.eventId}_S{i}" : $"{def.eventId}_S{i}w{k}";
                slots.Add(new BuildSlot
                {
                    name = name,
                    label = isLastSegment ? step.label : $"{step.label} (대기 {k})",
                    actions = segments[k].actions,
                    // 단계의 진행 조건은 마지막 조각에만 붙는다 — 중간 조각은 순수한 대기다.
                    advance = isLastSegment ? stepAdvance : Array.Empty<StateDecision>(),
                    timeout = isLastSegment ? stepTimeout : segments[k].waitAfter,
                    scaleTimeout = isLastSegment && scaleStepTimeout,
                });
            }
        }

        var states = new List<StateSO>();
        for (int i = 0; i < slots.Count; i++)
            states.Add(NewOrOverwriteState(slots[i].name));

        // MarkUnscaledTimeAction이 쓰는 커스텀 int 이름들. 미리 선언해 두지 않으면 실행 중
        // StateController.SetIntParameter가 "Generated missing variable" 경고를 매번 찍는다.
        var markKeys = new List<string>();

        for (int i = 0; i < slots.Count; i++)
        {
            BuildSlot slot = slots[i];
            StateSO state = states[i];

            state.EnterActions = slot.actions.ToArray();
            state.UpdateActions = Array.Empty<StateAction>();
            state.ExitActions = Array.Empty<StateAction>();
            state.actionSequence = Array.Empty<ActionSequence>();
            state.additionalTransitions = Array.Empty<StateTransition>();

            bool isLast = i == slots.Count - 1;
            if (isLast)
            {
                state.transitions = Array.Empty<StateTransition>();
                state.useTimer = false;
                EditorUtility.SetDirty(state);
                continue;
            }

            StateSO next = states[i + 1];
            var transitions = new List<StateTransition>();
            for (int d = 0; d < slot.advance.Length; d++)
                if (slot.advance[d] != null) transitions.Add(Transition($"조건 {d}", slot.advance[d], next));

            // 타임아웃은 unscaled 시간으로 잰다. StateSO의 useTimer/TimerDecision은 TimerManager를
            // 타는데 그쪽이 스케일 시간이라(버프 쿨타임이 메뉴와 함께 멈춰야 하므로 의도된 설계),
            // 컷신이 TimeManager.Pause로 게임을 멈추는 순간 이벤트 타이머까지 같이 얼어붙어
            // 그 단계에서 영영 안 넘어간다 — 컷신이 스스로를 가둔다.
            // 액션 사이의 대기도 같은 이유로 이 방식을 그대로 쓴다.
            state.useTimer = false;
            if (slot.timeout > 0f)
            {
                string markKey = $"{slot.name}_mark";
                markKeys.Add(markKey);
                state.EnterActions = state.EnterActions
                    .Prepend<StateAction>(new MarkUnscaledTimeAction { key = markKey })
                    .ToArray();
                float seconds = slot.scaleTimeout ? slot.timeout * timeoutMultiplier : slot.timeout;
                transitions.Add(Transition(slot.scaleTimeout ? "타임아웃" : "대기",
                    new UnscaledTimeElapsedDecision { key = markKey, seconds = seconds }, next));
            }

            if (transitions.Count == 0)
                Debug.LogWarning($"[EventChainEditorWindow] '{def.eventId}'의 '{slot.label}'에 진행 조건이 없어 여기서 영원히 멈춥니다 " +
                                 "— 다음 단계로 (OR)에 조건을 넣거나, 타임아웃 또는 마지막 액션의 Wait After를 채우세요.");

            state.transitions = transitions.ToArray();
            EditorUtility.SetDirty(state);
        }

        EventSO evt = NewOrOverwriteEvent(def.eventId);
        evt.allStates = states;
        evt.initialState = states[0];
        evt.involvedEventTargets = def.targets ?? Array.Empty<EventTargetSearchInfo>();
        DeclareMarkVariables(evt, markKeys);
        evt.UpdateStateMachine();
        EditorUtility.SetDirty(evt);

        CreateOrOverwriteTriggerPrefab($"{def.eventId}_Trigger", evt, def);
    }

    // 진입 액션 배열 끝에 항목 하나를 붙인다. 배열 원소가 SerializeReference 그 자체가 아니라
    // action 필드를 품은 클래스라, 원소가 아니라 그 안쪽 필드에 넣어야 한다.
    private static void AppendStepAction(SerializedProperty enterActions, StateAction action, float waitAfter = 0f)
    {
        int index = enterActions.arraySize;
        enterActions.InsertArrayElementAtIndex(index);
        SerializedProperty element = enterActions.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("action").managedReferenceValue = action;
        element.FindPropertyRelative("waitAfter").floatValue = waitAfter;
    }

    private static float SumWaits(SerializedProperty enterActions)
    {
        float total = 0f;
        for (int i = 0; i < enterActions.arraySize; i++)
            total += enterActions.GetArrayElementAtIndex(i).FindPropertyRelative("waitAfter").floatValue;
        return total;
    }

    // 액션만 담긴 목록을 단계 액션(대기 0)으로 감싼다 — 조건에 따라 목록을 만들어 쓰는 샘플용.
    private static EventStepAction[] ToStepActions(IEnumerable<StateAction> actions)
        => actions.Select(a => (EventStepAction)a).ToArray();

    // 빌드가 만들 State 하나 분량 — 단계 그 자체이거나, 단계를 waitAfter로 쪼갠 조각이다.
    private class BuildSlot
    {
        public string name;
        public string label;
        public List<StateAction> actions;
        public StateDecision[] advance;
        public float timeout;
        public bool scaleTimeout;
    }

    // 진입 액션들을 waitAfter가 걸린 지점에서 끊어 세그먼트로 나눈다.
    // 대기가 하나도 없으면 세그먼트 하나(= 예전과 똑같은 결과)만 나온다.
    private static List<(List<StateAction> actions, float waitAfter)> SplitStepByWaits(EventStepData step)
    {
        var segments = new List<(List<StateAction>, float)>();
        var current = new List<StateAction>();

        foreach (EventStepAction entry in step.enterActions ?? Array.Empty<EventStepAction>())
        {
            if (entry == null) continue;
            // 액션을 아직 안 고른 빈칸이어도 waitAfter만 쓰면 "그냥 이만큼 쉰다"가 된다.
            if (entry.action != null) current.Add(entry.action);
            if (entry.waitAfter <= 0f) continue;

            segments.Add((current, entry.waitAfter));
            current = new List<StateAction>();
        }

        // 마지막 세그먼트 — 여기에 단계의 진행 조건과 타임아웃이 붙는다.
        segments.Add((current, 0f));
        return segments;
    }

    // 타임아웃 표식용 커스텀 int를 상태 기계에 미리 선언해 둔다. 없어도 SetIntParameter가 그 자리에서
    // 만들어 주긴 하지만, 그때마다 "Generated missing variable" 경고가 찍혀 콘솔이 시끄러워진다.
    //
    // StateController.Initialize가 이 객체를 참조로 그대로 물어가므로(StateController.cs의
    // "HAVE TO FIX THIS NOT TO DEEP REFERENCE CUSTOMVARS!!!" 주석 참고), 여기 담아두면 런타임이
    // 같은 딕셔너리를 쓴다.
    private static void DeclareMarkVariables(EventSO evt, List<string> markKeys)
    {
        evt.customVariables ??= new CustomVariableSets();
        evt.customVariables.intVariables ??= new AYellowpaper.SerializedCollections.SerializedDictionary<string, CustomVariable<int>>();

        // 값은 항상 0으로 되돌린다. StateController.customVariables는 이 SO의 객체를 참조로 물어가서
        // (StateController.Initialize의 경고 참고) 플레이 중 찍힌 시각 표식이 에셋에 그대로 눌러
        // 붙는다 — 남겨두면 diff만 지저분해지고, 무엇보다 "지난 판의 시각"이 에셋에 남는다.
        foreach (string key in markKeys)
            evt.customVariables.intVariables[key] = new CustomVariable<int>();
    }

    private static StateTransition Transition(string name, StateDecision decision, StateSO trueState)
    {
        return new StateTransition
        {
            name = name,
            decisions = new[] { new StateTransition.DecisionSet { Decision = decision } },
            trueState = trueState,
        };
    }

    // 같은 경로에 이미 있으면 그 에셋을 그대로 재사용(필드만 뒤에서 덮어씀)한다 — GUID가 안 바뀌어야
    // 씬에 이미 꽂아둔 트리거/이벤트 참조가 재빌드 후에도 살아있다.
    private static StateSO NewOrOverwriteState(string name)
    {
        string path = $"{OutputFolder}/{name}.asset";
        StateSO existing = AssetDatabase.LoadAssetAtPath<StateSO>(path);
        if (existing != null) return existing;

        StateSO created = ScriptableObject.CreateInstance<StateSO>();
        created.name = name;
        AssetDatabase.CreateAsset(created, path);
        return created;
    }

    private static EventSO NewOrOverwriteEvent(string eventId)
    {
        string path = $"{OutputFolder}/{eventId}.asset";
        EventSO existing = AssetDatabase.LoadAssetAtPath<EventSO>(path);
        if (existing != null) return existing;

        EventSO created = ScriptableObject.CreateInstance<EventSO>();
        created.name = eventId;
        created.customVariables = new CustomVariableSets();
        AssetDatabase.CreateAsset(created, path);
        return created;
    }

    // 사전 조건 게이팅까지 확인하려면 GameStateEvent 인스턴스가 필요하다. 그 필드는 private
    // [SerializeField]라 런타임에는 못 채우므로 에디터에서 SerializedObject로 채워 프리팹으로 굽는다.
    //
    // 기존 프리팹이 있으면 LoadPrefabContents로 그 파일을 직접 고친다 — 지우고 새로 만들면 매번
    // 새 GUID가 나와서, 씬에 이미 드래그해둔 트리거 참조가 재빌드할 때마다 다시 비어버린다.
    //
    // triggerKind가 None이 아니면 콜라이더(ZCircleCollider2D)와 GameEventTrigger(EventStayTrigger/
    // EventInputTrigger)까지 이 프리팹에 직접 붙이고 서로 배선까지 끝낸다 — 씬에 드래그해 위치와
    // 반경만 맞추면 그걸로 끝이다. 예전엔 "빌드"가 GameStateEvent만 만들어서, 콜라이더를 붙이고
    // GameStateEvent._trigger에 손으로 연결하는 걸 매번 씬에서 따로 해야 했다.
    private static void CreateOrOverwriteTriggerPrefab(string prefabName, EventSO evt, EventDefinition def)
    {
        string path = $"{OutputFolder}/{prefabName}.prefab";
        bool isNew = AssetDatabase.LoadAssetAtPath<GameObject>(path) == null;

        GameObject root = isNew ? new GameObject(prefabName) : PrefabUtility.LoadPrefabContents(path);
        GameStateEvent gameStateEvent = root.GetComponent<GameStateEvent>();
        if (gameStateEvent == null) gameStateEvent = root.AddComponent<GameStateEvent>();

        var serialized = new SerializedObject(gameStateEvent);
        serialized.FindProperty("_event").objectReferenceValue = evt;

        GameStateEvent.EventFlagCondition[] conditions = def.preconditions ?? Array.Empty<GameStateEvent.EventFlagCondition>();
        SerializedProperty preconditionsProp = serialized.FindProperty("_preconditions");
        List<GameStateEvent.EventFlagCondition> valid = conditions.Where(c => c != null && c.flag != null).ToList();
        preconditionsProp.arraySize = valid.Count;
        for (int i = 0; i < valid.Count; i++)
        {
            SerializedProperty element = preconditionsProp.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("flag").objectReferenceValue = valid[i].flag;
            element.FindPropertyRelative("value").intValue = valid[i].value;
        }

        GameEventTrigger trigger = ConfigureSceneTrigger(root, def);
        serialized.FindProperty("_trigger").objectReferenceValue = trigger;

        serialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        if (isNew) UnityEngine.Object.DestroyImmediate(root);
        else PrefabUtility.UnloadPrefabContents(root);
    }

    // triggerKind에 맞는 콜라이더+트리거 컴포넌트를 만들고(없으면 추가, 있으면 재사용) 반경/입력
    // 종류를 채운다. None이면 대신 예전 컴포넌트(트리거 종류를 바꿔 더는 안 쓰게 된 것)를 정리한다.
    // 반환값이 null이면 GameStateEvent._trigger도 비워진다 — 그러면 EventSystemTestbed처럼
    // 코드로 TriggerEvent()를 직접 불러야 발동한다.
    private static GameEventTrigger ConfigureSceneTrigger(GameObject root, EventDefinition def)
    {
        // 트리거 종류를 바꿨을 수 있으니, 지금 쓰지 않는 GameEventTrigger는 콜라이더와 함께 지운다.
        foreach (GameEventTrigger old in root.GetComponents<GameEventTrigger>())
        {
            bool keep = def.triggerKind == EventTriggerKind.Stay && old is EventStayTrigger
                     || def.triggerKind == EventTriggerKind.Input && old is EventInputTrigger;
            if (!keep) UnityEngine.Object.DestroyImmediate(old, true);
        }

        if (def.triggerKind == EventTriggerKind.None)
        {
            ZCircleCollider2D staleCollider = root.GetComponent<ZCircleCollider2D>();
            if (staleCollider != null) UnityEngine.Object.DestroyImmediate(staleCollider, true);
            CircleCollider2D staleCircle = root.GetComponent<CircleCollider2D>();
            if (staleCircle != null) UnityEngine.Object.DestroyImmediate(staleCircle, true);
            return null;
        }

        // [RequireComponent] 덕에 CircleCollider2D가 같이 붙는다. 반경/isTrigger는 ZCircleCollider2D의
        // Radius/IsTrigger 프로퍼티가 아니라 CircleCollider2D에 직접 쓴다 — 그 프로퍼티들은 Awake()에서만
        // 채워지는 _col 필드를 쓰는데, 에디터에서 프리팹을 굽는 이 시점엔 Awake()가 돌지 않아 NRE가 난다.
        ZCircleCollider2D zCollider = root.GetComponent<ZCircleCollider2D>();
        if (zCollider == null) zCollider = root.AddComponent<ZCircleCollider2D>();

        CircleCollider2D circle = root.GetComponent<CircleCollider2D>();
        if (circle == null) circle = root.AddComponent<CircleCollider2D>();
        circle.isTrigger = true;
        circle.radius = def.triggerRadius;

        // Z 높이(이 프로젝트는 Z가 높이축이다)가 어긋나면 ZCollider2D.OverlapsZ가 걸러버려서
        // 평면상으로는 겹쳐도 트리거가 조용히 안 켜진다. 데모용이라 넉넉하게 잡아 그 함정을 피한다.
        zCollider.zCenter = 0f;
        zCollider.height = 4f;

        GameEventTrigger trigger = def.triggerKind == EventTriggerKind.Stay
            ? root.GetComponent<EventStayTrigger>() as GameEventTrigger ?? root.AddComponent<EventStayTrigger>()
            : root.GetComponent<EventInputTrigger>() as GameEventTrigger ?? root.AddComponent<EventInputTrigger>();

        var serializedTrigger = new SerializedObject(trigger);
        serializedTrigger.FindProperty("_collider2D").objectReferenceValue = zCollider;
        // 플레이어 레이어만 걸러야 한다 — 비워두면 아무 것도 안 걸려 영원히 발동하지 않는다.
        serializedTrigger.FindProperty("_layerMask").intValue = LayerMask.GetMask("Player");
        if (def.triggerKind == EventTriggerKind.Input)
            serializedTrigger.FindProperty("_inputType").enumValueIndex = (int)def.triggerInputType;
        serializedTrigger.ApplyModifiedPropertiesWithoutUndo();

        return trigger;
    }

    private static void EnsureFolderFor(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(OutputFolder)) return;
        AssetDatabase.CreateFolder("Assets/Scripts/Event/Test", "Generated");
    }

    // ─── 씬에 데모 배치 ─────────────────────────────────────────────
    // "체인 진행 시도" 버튼으로 코드가 대신 TriggerEvent()를 불러주던 것과 달리, 여기서는
    // 진짜 콜라이더가 있는 NPC와 트리거를 현재 열려 있는 씬에 놓는다 — 플레이어가 실제로
    // 걸어서 접근하면 dood가 바뀌고 대화가 뜨고 다음 이벤트로 이어지는 걸 그대로 볼 수 있다.
    //
    // NPC는 아트가 없어 흰 정사각형 스프라이트(EnsureDummyBoxSprite, 절차 생성 — 아트 의존 0)에
    // 색만 입혀 구분한다. StateController + EventControllableEntity만 붙인다 — 이 체인의 액션들
    // (ChangeStateMachineAction/SetEntityActiveAction/TargetEntitiesManipulateAction)은 물리·애니메이션
    // 없이도 동작하고, 대화는 EventManager 자신의 StateController에서 도니까(DialogueModule이 그
    // 오브젝트에 붙어 있다, System 씬 참고) NPC 쪽엔 필요 없다.
    private void PlaceDemoInScene()
    {
        // 더미 NPC는 맵 씬 안에 있어야 이벤트 대상(FromMap)으로 등록된다 — 런타임에 EventManager는
        // "로드된 맵 씬"의 MapLocalManager만 보기 때문이다. 그래서 활성 씬이 아니라 **열려 있는 씬 중
        // MapLocalManager가 있는 씬**을 골라 거기에 놓는다(System을 활성 씬으로 두고 맵 씬을 같이
        // 열어두는 게 흔한 작업 방식이라, 활성 씬만 보고 막으면 쓸데없이 걸리적거린다).
        MapLocalManager[] locals = UnityEngine.Object.FindObjectsOfType<MapLocalManager>();
        if (locals.Length == 0)
        {
            EditorUtility.DisplayDialog("맵 씬을 열어 주세요",
                "지금 열려 있는 씬 중에 MapLocalManager가 있는 맵 씬이 없습니다.\n\n" +
                "더미 NPC는 맵 씬 안에 있어야 이벤트 대상(FromMap)으로 등록됩니다.\n" +
                "맵 씬(예: Assets/Scenes/TestMap/TestMap1.unity)을 함께 열고 다시 누르세요.\n" +
                "활성 씬은 System이어도 됩니다.\n\n" +
                "플레이는 그대로 System 씬에서 시작하면 됩니다 — 맵은 MapManager가 얹습니다.",
                "확인");
            return;
        }

        // 맵 씬이 여러 개 열려 있으면 MapManager.initialMap(플레이 시작 시 실제로 로드되는 맵)과
        // 짝이 맞는 것을 고른다. 못 고르면 첫 번째를 쓰되 어느 씬에 놓았는지 로그로 남긴다.
        MapLocalManager mapLocal = locals[0];
        MapManager mapManager = UnityEngine.Object.FindObjectOfType<MapManager>();
        if (locals.Length > 1 && mapManager != null && mapManager.initialMap != null)
        {
            MapLocalManager matched = locals.FirstOrDefault(m => m.mapData == mapManager.initialMap);
            if (matched != null) mapLocal = matched;
        }
        Scene mapScene = mapLocal.gameObject.scene;

        EnsureFolderFor($"{OutputFolder}/_");
        Sprite box = EnsureDummyBoxSprite();

        // 플레이어에서 넉넉히 떨어뜨린다 — EVT-001이 반경 3짜리 Stay 트리거라, 가까이 놓으면
        // 플레이 시작하자마자 발동해서 정작 보고 싶은 "다가가니까 터진다"를 못 본다.
        //
        // 플레이어는 보통 System 씬에 있고 배치는 맵 씬에 하므로, 다른 씬의 플레이어를 기준으로
        // 삼으면 맵 밖에 놓이기 쉽다. 같은 씬에 있을 때만 기준으로 쓰고 아니면 원점을 쓴다.
        Vector3 center = Vector3.zero;
        Player player = UnityEngine.Object.FindObjectOfType<Player>();
        if (player != null && player.gameObject.scene == mapScene)
            center = player.transform.position + new Vector3(8f, 0f, 0f);

        // 금발=노란빛, 백발=흰빛 — 실제 캐릭터가 들어오기 전까지 구분용.
        EnsureDummyNpc(NpcATargetID, "DummyNPC_NPC_A", box, new Color(0.85f, 0.75f, 0.25f), center + new Vector3(-0.75f, 0f, 0f), mapScene);
        EnsureDummyNpc(NpcBTargetID, "DummyNPC_NPC_B", box, new Color(0.92f, 0.92f, 0.92f), center + new Vector3(0.75f, 0f, 0f), mapScene);

        // 트리거끼리 정확히 겹쳐 놓으면 씬 뷰에서 하나씩 집어 옮기기가 어렵다 — 조금씩 어긋나게 둔다.
        // EVT-001/002는 꿈 맵에 둔다(금발/백발과 만나는 곳).
        EnsureTriggerInstance("Dummy_EVT001_Trigger", center, mapScene);
        EnsureTriggerInstance("Dummy_EVT002_Trigger", center + new Vector3(0f, -1.2f, 0f), mapScene);

        // EVT-004(전투)는 기획서상 "중앙 집터로 재진입" — 꿈에서 벌어지므로 꿈 맵에 둔다.
        // EVT-003이 해몽을 마치고 꿈으로 되돌려 보내면 그때 이 트리거가 받는다.
        EnsureTriggerInstance("Dummy_EVT004_Trigger", center + new Vector3(0f, -2.4f, 0f), mapScene);

        // EVT-003은 **현실 맵**에 둔다 — "현실의 방 침대 옆 책상 위에 놓인 노트와 상호작용".
        // 눈에 보이는 책상 프롭을 하나 세우고 트리거를 그 자리에 겹쳐 둬서, 뭘 향해 확인 키를
        // 눌러야 하는지 알 수 있게 한다.
        MapLocalManager realLocal = locals.FirstOrDefault(m => m.mapData != null && m.mapData.isRealWorld);
        Scene realScene = realLocal != null ? realLocal.gameObject.scene : default;
        if (realLocal != null)
        {
            Vector3 deskPos = Vector3.zero;
            EnsureDummyProp("DummyProp_Desk", box, new Color(0.55f, 0.38f, 0.22f), deskPos, realScene);
            EnsureTriggerInstance("Dummy_EVT003_Trigger", deskPos, realScene);
            EditorSceneManager.MarkSceneDirty(realScene);
            Debug.Log($"[EventChainEditorWindow] 현실 맵 '{realScene.name}' 원점에 책상 프롭(DummyProp_Desk)과 " +
                      "EVT-003 트리거를 겹쳐 배치했습니다. 이 씬도 같이 저장하세요.");
        }
        else
        {
            Debug.LogWarning("[EventChainEditorWindow] isRealWorld가 켜진 맵 씬이 열려 있지 않아 EVT-003(노트) 배치를 " +
                             "건너뛰었습니다 — 현실 맵 씬(TestMap2)도 함께 열고 다시 누르세요. " +
                             "그대로 두면 현실로 넘어간 뒤 해몽을 시작할 트리거가 없습니다.");
        }

        WriteExitInitInfos(mapLocal);

        mapLocal.AutoFindEventTargets();
        EditorUtility.SetDirty(mapLocal);
        Debug.Log($"[EventChainEditorWindow] '{mapScene.name}'의 MapLocalManager.allEventTargets에 " +
                  $"더미 NPC를 등록했습니다: [{string.Join(", ", mapLocal.allEventTargets.targetEntities.Keys)}]");

        var expected = new Dictionary<string, Scene>
        {
            ["DummyNPC_NPC_A"] = mapScene,
            ["DummyNPC_NPC_B"] = mapScene,
            ["Dummy_EVT001_Trigger"] = mapScene,
            ["Dummy_EVT002_Trigger"] = mapScene,
            ["Dummy_EVT004_Trigger"] = mapScene,
            ["Dummy_EVT003_Trigger"] = realScene,
            ["DummyProp_Desk"] = realScene,
        };
        WarnAboutStraysInOtherScenes(expected);

        EditorSceneManager.MarkSceneDirty(mapScene);
        Debug.Log($"[EventChainEditorWindow] 꿈 맵 '{mapScene.name}' {center} 근처에 더미 NPC 2개 + EVT-001/002 트리거를 " +
                  "배치했습니다(Hierarchy에서 DummyNPC_* 를 더블클릭하면 그 위치로 이동합니다). Ctrl+S로 저장하세요.\n" +
                  "플레이는 System 씬에서 시작해야 합니다(GameManager/EventManager/대화 패널이 거기 있고, " +
                  "맵 씬은 MapManager가 Addressables로 얹습니다). 다시 눌러도 같은 오브젝트를 재사용합니다.");
    }

    // 금발(NPC_A)은 EVT-001에서 퇴장하는데, 그건 씬이 살아 있는 동안만 유지된다 — 꿈 맵을 다시
    // 로드하면(현실에 갔다 돌아오면) 씬이 새로 깔려 되살아난다. 그래서 "이 진행도에서는 없는 상태"를
    // 맵 복원 정보(MapDataSO.allEntityInitInfos)에 남긴다.
    //
    // dood 값마다 항목을 따로 넣는 이유: EventManager.HasEventFlag가 정확히 일치하는 값만 참이라,
    // "1 이상"을 한 항목으로 표현할 수 없다. 퇴장 이후에 나올 수 있는 값들을 나열해 둔다.
    private static void WriteExitInitInfos(MapLocalManager mapLocal)
    {
        if (mapLocal.mapData == null) return;

        EventFlagSO dood = LoadFlag("Dood");
        if (dood == null) return;

        mapLocal.mapData.allEntityInitInfos ??= new AYellowpaper.SerializedCollections.SerializedDictionary<string, List<EntityInitializeInfo>>();
        if (!mapLocal.mapData.allEntityInitInfos.TryGetValue(NpcATargetID, out List<EntityInitializeInfo> infos) || infos == null)
        {
            infos = new List<EntityInitializeInfo>();
            mapLocal.mapData.allEntityInitInfos[NpcATargetID] = infos;
        }

        for (int doodValue = 1; doodValue <= 5; doodValue++)
        {
            int existing = infos.FindIndex(a => a.eventFlag == dood && a.eventFlagCondition == doodValue);
            var info = new EntityInitializeInfo
            {
                eventFlag = dood,
                eventFlagCondition = doodValue,
                startsInactive = true,
            };

            if (existing >= 0) infos[existing] = info;
            else infos.Add(info);
        }

        EditorUtility.SetDirty(mapLocal.mapData);
        Debug.Log($"[EventChainEditorWindow] '{mapLocal.mapData.name}'에 금발(NPC_A) 퇴장 복원 정보를 " +
                  "기록했습니다(dood 1~5에서 비활성) — 현실에 갔다 돌아와도 다시 나타나지 않습니다.");
    }

    // 이벤트 대상이 아니라 "여기서 상호작용하라"를 보여주기만 하는 눈에 보이는 표식.
    // StateController/EventControllableEntity를 붙이지 않는다 — 붙이면 MapLocalManager의
    // 이벤트 대상 목록에 쓸데없이 끼어든다.
    private static GameObject EnsureDummyProp(string objectName, Sprite sprite, Color tint, Vector3 position, Scene scene)
    {
        GameObject go = FindInScene(objectName, scene);
        if (go == null)
        {
            go = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "더미 프롭 생성");
        }
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sortingLayerName = EntitySortingLayer;

        EditorUtility.SetDirty(go);
        return go;
    }

    private static GameObject EnsureDummyNpc(string entityId, string objectName, Sprite sprite, Color tint, Vector3 position, Scene scene)
    {
        GameObject go = FindInScene(objectName, scene);
        if (go == null)
        {
            go = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "더미 NPC 생성");
        }
        go.transform.position = position;

        SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        // 정렬 레이어를 안 정하면 기본값 "Default"로 들어가는데, 그건 목록 맨 앞이라 가장 먼저
        // 그려진다 = 맵 바닥("Bottom")보다도 뒤 → 맵에서 아예 안 보인다.
        // 플레이어 body 스프라이트와 같은 "Entity"로 맞춰야 캐릭터와 같은 깊이에 선다.
        renderer.sortingLayerName = EntitySortingLayer;

        StateController controller = go.GetComponent<StateController>();
        if (controller == null) controller = go.AddComponent<StateController>();

        EventControllableEntity controllable = go.GetComponent<EventControllableEntity>();
        if (controllable == null) controllable = go.AddComponent<EventControllableEntity>();
        controllable.ID = entityId;
        controllable.Target = controller;

        EditorUtility.SetDirty(go);
        return go;
    }

    // 트리거 프리팹을 씬 인스턴스로 놓는다 — PrefabUtility.InstantiatePrefab을 써야 "빌드"로 프리팹
    // 내용이 갱신될 때 씬의 인스턴스도 같이 따라간다(일반 Instantiate는 그 연결이 끊긴다).
    private static void EnsureTriggerInstance(string prefabName, Vector3 position, Scene scene)
    {
        string path = $"{OutputFolder}/{prefabName}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"[EventChainEditorWindow] '{path}'가 아직 없습니다 — 먼저 '빌드'를 누르세요.");
            return;
        }

        GameObject existing = FindInScene(prefabName, scene);
        if (existing != null)
        {
            existing.transform.position = position;
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = prefabName;
        instance.transform.position = position;
        Undo.RegisterCreatedObjectUndo(instance, "더미 트리거 배치");
    }

    // GameObject.Find는 열려 있는 모든 씬을 뒤진다 — System 씬에 잘못 놔둔 예전 오브젝트를 집어
    // 그걸 옮겨버리면, 정작 맵 씬엔 아무것도 안 생기고 같은 문제가 반복된다. 씬을 좁혀서 찾는다.
    private static GameObject FindInScene(string name, Scene scene)
    {
        if (!scene.IsValid()) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }

    // 다른 씬(주로 System)에 같은 이름으로 남아 있는 예전 배치본을 알린다 — 그대로 두면 트리거가
    // 두 벌 돌아 이벤트가 두 번 발동하거나, 어느 쪽이 도는지 헷갈린다.
    // 데모 오브젝트는 꿈/현실 두 씬에 나눠 놓이므로, "어느 씬에 있어야 하는가"까지 알아야 잔여물을
    // 정확히 짚을 수 있다. 예전 배치에서 씬이 바뀐 것(EVT-004가 현실 → 꿈으로 옮겨진 것 등)이
    // 그대로 남아 있으면 트리거가 두 벌 돌아 이벤트가 겹쳐 터진다.
    private static void WarnAboutStraysInOtherScenes(Dictionary<string, Scene> expectedScenes)
    {
        var strays = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!expectedScenes.TryGetValue(root.name, out Scene expected)) continue;
                if (expected.IsValid() && scene == expected) continue;
                strays.Add($"{scene.name}/{root.name}");
            }
        }

        if (strays.Count > 0)
            Debug.LogWarning("[EventChainEditorWindow] 있어야 할 씬이 아닌 곳에 데모 오브젝트가 남아 있습니다 — 지우세요: " +
                             string.Join(", ", strays));
    }

    // 흰 정사각형 하나만 만들어 두고 NPC마다 색(tint)만 다르게 입힌다 — 별도 아트 없이 구분한다.
    private static Sprite EnsureDummyBoxSprite()
    {
        string path = $"{OutputFolder}/DummyBoxSprite.png";
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        const int size = 16;
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = size;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ─── 샘플 데이터(EVT-001~004) 채우기 ───────────────────────────
    // EventDummyGenerator.cs(폐기)가 하드코딩하던 것을 데이터로 옮긴 것. 빈 체인에서 시작하는
    // 대신 참고용으로 눌러볼 수 있게 남겨뒀다 — 다음부터 이 창이 실제 이벤트 저작 경로다.

    private void FillSampleChain()
    {
        if (_chain == null) return;

        EnsureFolderFor($"{OutputFolder}/_");
        EventFlagSO dood = LoadFlag("Dood");
        EventFlagSO emotionMerge = LoadFlag("FLAG_EMOTION_MERGE");
        StateMachineSO bossMachine = EnsureBossMachine();

        _chain.events = new List<EventDefinition>
        {
            BuildEvt001Sample(dood),
            BuildEvt002Sample(dood, bossMachine),
            BuildEvt003Sample(dood),
            BuildEvt004Sample(dood, emotionMerge),
        };

        EditorUtility.SetDirty(_chain);
        AssetDatabase.SaveAssets();
        _so.Update();
        _selectedEvent = 0;

        Debug.Log("[EventChainEditorWindow] 샘플 데이터(EVT-001~004)를 채웠습니다. '빌드'를 눌러 실행 가능한 이벤트로 만드세요.");
    }

    private static EventTargetSearchInfo[] DefaultTargets() => new[]
    {
        new EventTargetSearchInfo { ID = PlayerTargetID, targetSearchType = EventManager.TargetSearchType.Player },
        new EventTargetSearchInfo { ID = NpcATargetID, targetSearchType = EventManager.TargetSearchType.FromMap },
        new EventTargetSearchInfo { ID = NpcBTargetID, targetSearchType = EventManager.TargetSearchType.FromMap },
    };

    private static EventDefinition BuildEvt001Sample(EventFlagSO dood)
    {
        var def = new EventDefinition
        {
            eventId = "Dummy_EVT001",
            eventName = "금발 아이의 가위질",
            purpose = "메인 서사 진행",
            startTriggerDesc = "플레이어가 두 아이 근처(접근범위 미정)로 접근",
            preconditionDesc = "dood == 0",
            interruptCondition = "없음(강제 컷신 연출, 플레이어 조작 불가)",
            retryPolicy = "게임 중 최초 1회만 실행",
            linkedEvents = "EVT-002",
            narrativeContent =
                "금발이 백발의 트윈테일 끝을 가위로 자름. 이때 화면에 기괴한 가위질 소리(ASMR)가 크게 " +
                "증폭되며 노이즈 발생. 잘려 나간 머리카락이 바닥에서 새의 날개처럼 파르르 떨다가 가루가 " +
                "되어 소멸함. [잘려 나간 머리카락] 몇 개가 남아 습득할 수 있는 [단서]로 기능. 연출 종료 후 " +
                "dood = 1로 변경. 금발 아이의 퇴장",
            targets = DefaultTargets(),
            preconditions = dood != null
                ? new[] { new GameStateEvent.EventFlagCondition { flag = dood, value = 0 } }
                : Array.Empty<GameStateEvent.EventFlagCondition>(),
            // "플레이어가 두 아이 근처(접근범위 미정)로 접근" — 근접하면 자동 발동.
            triggerKind = EventTriggerKind.Stay,
            triggerRadius = 3f,
        };

        def.steps.Add(new EventStepData
        {
            label = "가위질",
            enterActions = new EventStepAction[]
            {
                new SetInputModeAction { mode = EnumManager.InputMode.Cutscene },
                new DummyEventStepAction { label = "EVT-001 S0 금발이 백발의 트윈테일을 자름 (애니메이션·사운드 없음)" },
                // [애니메이션 빈칸] 금발의 가위질. 클립이 생기면 animationName만 채우면 된다 —
                // 비어 있는 동안은 아무 일도 하지 않는다(PlayAnimationAction 주석 참고).
                new TargetEntityManipulateAction { targetID = NpcATargetID, targetAction = new PlayAnimationAction { animationName = "" } },
                // [애니메이션 빈칸] 머리카락이 잘리는 백발 쪽 반응.
                new TargetEntityManipulateAction { targetID = NpcBTargetID, targetAction = new PlayAnimationAction { animationName = "" } },
                new ScreenNoiseAction { intensity = 0.5f, duration = 1.5f },
                new CameraShakeAction(),
            },
            timeoutSeconds = 1.5f,
        });

        def.steps.Add(new EventStepData
        {
            label = "단서 획득",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-001 S1 머리카락 파티클 (아트 없음) + 단서 획득" },
                // 꿈속에서는 단서를 볼 수 없다(현실 전용 창) — 조용히 획득만 하고 도감은 열지 않는다.
                new AcquireClueAction { clueId = DummyClueWingId, openCodexImmediately = false },
            },
            timeoutSeconds = 1.5f,
        });

        var endActions = new List<StateAction>
        {
            // [애니메이션 빈칸] 퇴장 동작. 비활성화보다 먼저 둔다 — 꺼진 뒤에는 재생할 수 없다.
            new TargetEntityManipulateAction { targetID = NpcATargetID, targetAction = new PlayAnimationAction { animationName = "" } },
            new TargetEntityManipulateAction { targetID = NpcATargetID, targetAction = new SetEntityActiveAction { active = false } },
            new DummyEventStepAction { label = "EVT-001 S2 금발 퇴장 — dood = 1" },
        };
        if (dood != null) endActions.Add(new SetEventFlagAction { flag = dood, flagValue = 1 });
        endActions.Add(new SetInputModeAction { mode = EnumManager.InputMode.Play });
        def.steps.Add(new EventStepData { label = "금발 퇴장", enterActions = ToStepActions(endActions) });

        return def;
    }

    private static EventDefinition BuildEvt002Sample(EventFlagSO dood, StateMachineSO bossMachine)
    {
        var def = new EventDefinition
        {
            eventId = "Dummy_EVT002",
            eventName = "백발의 보스화",
            purpose = "메인 서사 진행, 장르적 공포 유도",
            startTriggerDesc = "백발의 아이에게 말을 건다",
            preconditionDesc = "dood == 1",
            interruptCondition = "없음(강제 기상까지 이어지는 연속 연출)",
            retryPolicy = "게임 중 최초 1회만 실행",
            linkedEvents = "EVT-003 (기획서 원문은 미기재 — 2026-08-22 확정)",
            narrativeContent =
                "카메라가 백발의 눈가를 화면 전체에 2D 일러스트로 클로즈업. 눈동자 내부가 TV 노이즈 " +
                "화면처럼 지지직거림. 백발이 기괴한 소리를 내며 괴로워하다가, 트윈테일 방향에서 거대하고 " +
                "날카로운 흰색 낙서 형태의 머리카락(날개)들이 폭발하듯 뻗어 나옴. 플레이어는 이 충격으로 " +
                "인해 화면 외곽의 검은 심연 공간으로 강제 넉백(밀려남)됨. [강제 기상 연출]: 넉백과 동시에 " +
                "화면이 거친 노이즈와 함께 암전되며, 플레이어는 현실 세계의 침실 침대 위에서 번쩍 눈을 " +
                "뜨며 깨어남. 깨어남과 동시에 dood = 2로 변경.\n" +
                "(기획자노트 — 꿈-현실 2안 대신 자동 세이브로 대체 가능하다는 의견 있었으나 2026-08-22 " +
                "원안(2안) 유지로 확정. Docs/Map_Event_Implementation_Plan.md 참고)",
            targets = DefaultTargets(),
            preconditions = dood != null
                ? new[] { new GameStateEvent.EventFlagCondition { flag = dood, value = 1 } }
                : Array.Empty<GameStateEvent.EventFlagCondition>(),
            // "백발의 아이에게 말을 건다" — 근처에서 확인 입력을 눌러야 발동.
            triggerKind = EventTriggerKind.Input,
            triggerRadius = 1.5f,
        };

        def.steps.Add(new EventStepData
        {
            label = "대화 시작",
            enterActions = new EventStepAction[]
            {
                new SetInputModeAction { mode = EnumManager.InputMode.Cutscene },
                new DummyEventStepAction { label = "EVT-002 S0 대화 시작 — Submit 키로 대사를 넘기세요" },
                new DialogueStartAction(),
                // [애니메이션 빈칸] 대화 중 백발의 동작.
                new TargetEntityManipulateAction { targetID = NpcBTargetID, targetAction = new PlayAnimationAction { animationName = "" } },
                new DialogueShowLineAction { line = DummyLine("백발", "…뭐야, 왜 그런 눈으로 봐?") },
            },
            advanceWhenAny = new StateDecision[] { new DialogueLineEndedDecision() },
            timeoutSeconds = DialogueTimeout,
        });

        def.steps.Add(new EventStepData
        {
            label = "대사 2",
            enterActions = new EventStepAction[] { new DialogueShowLineAction { line = DummyLine("백발", "…머리카락이, 자꾸 자라나.") } },
            advanceWhenAny = new StateDecision[] { new DialogueLineEndedDecision() },
            timeoutSeconds = DialogueTimeout,
        });

        def.steps.Add(new EventStepData
        {
            label = "대화 종료",
            enterActions = new EventStepAction[] { new DialogueExitAction() },
            advanceWhenAny = new StateDecision[] { new DialogueEndedDecision() },
            timeoutSeconds = DialogueTimeout,
        });

        def.steps.Add(new EventStepData
        {
            label = "클로즈업",
            enterActions = new EventStepAction[]
            {
                // 대화를 닫으면 UIManager가 입력을 PLAY로 되돌린다 — 여기서 다시 컷신으로 잠근다.
                new SetInputModeAction { mode = EnumManager.InputMode.Cutscene },
                new DummyEventStepAction { label = "EVT-002 S1 눈가 클로즈업" },
                // 줌만 걸면 플레이어를 확대할 뿐이다 — 카메라를 백발 쪽으로 돌려야 클로즈업이 된다.
                new SetCameraFollowAction { targetID = NpcBTargetID },
                new CameraZoomAction { sizeMultiplier = 0.25f, blendTime = 0.5f, warnMissingIllustration = true },
                new ScreenNoiseAction { intensity = 0.6f, duration = 2.4f, tiling = 24f },
            },
            timeoutSeconds = 2.4f,
        });

        def.steps.Add(new EventStepData
        {
            label = "보스화",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-002 S2 백발 보스화" },
                // [애니메이션 빈칸] 보스화 변신 동작. 상태 기계 교체(아래)는 겉모습을 바꾸지 않으므로
                // 변신 연출은 여기서 따로 틀어야 한다.
                new TargetEntityManipulateAction { targetID = NpcBTargetID, targetAction = new PlayAnimationAction { animationName = "" } },
                // 금발(NPC_A)은 EVT-001에서 이미 퇴장(비활성)했으므로 대상에서 뺀다 — 넣어두면
                // 꺼진 오브젝트에 상태 기계를 걸려다 건너뛰며 경고만 남는다.
                // (여러 NPC를 한 번에 바꿔야 하는 연출이면 targetIDs에 나열하면 된다.)
                new TargetEntitiesManipulateAction
                {
                    targetIDs = new[] { NpcBTargetID },
                    targetAction = new ChangeStateMachineAction { stateMachine = bossMachine },
                },
                new CameraShakeAction { strength = 2f },
                new ScreenFlashAction { color = Color.white, duration = 0.3f },
            },
            timeoutSeconds = 1f,
        });

        // 넉백은 "시작 → 유지 → 종료" 3단이다. 플레이어의 상태 기계(Delta_Base_StateMachine_test)에는
        // KnockbackState가 없어 상태 기계가 애니메이션을 되돌려주지 못하므로, 이벤트가 직접 몬다.
        // (Roza/Lily 기계에는 KnockbackState가 있어 그쪽은 자동으로도 동작한다.)
        def.steps.Add(new EventStepData
        {
            label = "넉백 — 시작 + 유지",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-002 S3 플레이어 넉백" },
                // 넉백은 물리로 밀어내는 연출이라 시간이 흘러야 한다 — 정지형 컷신(Cutscene)이면
                // FixedUpdate가 안 돌아 힘만 쌓이고 아무 일도 일어나지 않는다.
                new SetInputModeAction { mode = EnumManager.InputMode.CutsceneLive },
                // 밀려나는 건 플레이어이므로 카메라를 되돌린다 — 되돌리지 않으면 백발만 계속 비춘다.
                new SetCameraFollowAction { returnToDefault = true },
                new CameraZoomAction { returnToOriginal = true, blendTime = 0.3f },
                new TargetEntityManipulateAction { targetID = PlayerTargetID, targetAction = new KnockBackAction() },
                // 지금 있는 클립은 반복 하나뿐이라(Default_AnimationBase의 Knockback: isLoop) 이걸
                // 그대로 틀어 두면 밀려나는 내내 유지되고, 아래 "넉백 — 종료" 단계가 걷어낸다.
                new TargetEntityManipulateAction { targetID = PlayerTargetID, targetAction = new PlayAnimationAction { animationName = "Knockback" } },
                // [애니메이션 빈칸] 시작 동작을 따로 만들면 위를 그 이름으로 바꾸고, 이어서 유지할
                // 반복 동작을 여기에 예약하면 된다(시작 클립이 끝나는 순간 자동으로 넘어간다).
                new TargetEntityManipulateAction { targetID = PlayerTargetID, targetAction = new ReserveAnimationAction { animationName = "" } },
                new ScreenNoiseAction { stop = true },
            },
            // 밀려나는 시간은 가속·마찰에 따라 달라지므로 초로 못 박지 않고 실제로 멈출 때까지 기다린다.
            // 타임아웃은 어딘가 걸려 영영 안 멈추는 경우의 안전장치다.
            advanceWhenAny = new StateDecision[] { new KnockbackEndedDecision { targetID = PlayerTargetID } },
            timeoutSeconds = 3f,
        });

        def.steps.Add(new EventStepData
        {
            label = "넉백 — 종료",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-002 S3b 넉백 종료 — 자세를 되돌린다" },
                // 유지 동작이 반복 클립이면 스스로 끝나지 않으므로 여기서 반드시 덮어써야 한다.
                // 종료 동작(일어나기 등)이 생기면 이 이름을 그걸로 바꾸고, 그 뒤에 Idle을 예약하면 된다.
                // 넉백이 끝나자마자 암전으로 넘어가면 쓰러진 걸 볼 새가 없다 — 잠깐 두고 간다.
                new EventStepAction
                {
                    action = new TargetEntityManipulateAction { targetID = PlayerTargetID, targetAction = new PlayAnimationAction { animationName = "Idle" } },
                    waitAfter = 0.6f,
                },
            },
            timeoutSeconds = 0f,
        });

        def.steps.Add(new EventStepData
        {
            label = "암전",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-002 S4 암전" },
                // 밀려나는 연출이 끝났으니 다시 정지형 컷신으로 — 여기서부터는 물리가 돌 필요가 없다.
                new SetInputModeAction { mode = EnumManager.InputMode.Cutscene },
                // 줌/추적 대상 복귀는 앞 단계(넉백)에서 이미 했다 — 여기서 또 하면 가상 카메라
                // 우선순위가 한 번 더 뒤집혀 불필요한 블렌딩이 끼어든다.
                new ScreenFadeAction { targetColor = new Color(0f, 0f, 0f, 1f), duration = 1f },
            },
            advanceWhenAny = new StateDecision[] { new ScreenEffectEndedDecision() },
        });

        // 완전히 어두워진 상태로 잠깐 머무른다. 맵 로딩이 바로 이어지면 "정신을 잃었다"는 간격이
        // 사라져 장면이 튄다. 액션 없이 대기만 하는 칸이라 이렇게 waitAfter만 적으면 된다.
        def.steps.Add(new EventStepData
        {
            label = "암전 유지",
            enterActions = new EventStepAction[] { new EventStepAction { waitAfter = 0.8f } },
        });

        // 현실 침실 맵이 아직 없어 Mapdata2(TestMap2)를 대역으로 쓴다 — isRealWorld가 켜져 있어
        // 여기로 넘어가야 도감/노트가 열리고 EVT-003의 해몽이 가능해진다.
        MapDataSO realWorldMap = FindRealWorldMap();
        def.steps.Add(new EventStepData
        {
            label = "맵 전환",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-002 S5 현실로 이동" },
                new ChangeMapAction { mapData = realWorldMap },
            },
            advanceWhenAny = new StateDecision[]
            {
                new MapLoadedDecision { mapAddressableID = realWorldMap != null ? realWorldMap.mapAddressableID : "" },
            },
        });

        var wakeActions = new List<StateAction>
        {
            new DummyEventStepAction { label = "EVT-002 S6 기상 — dood = 2" },
            new ScreenFadeAction { targetColor = new Color(0f, 0f, 0f, 0f), duration = 1f },
            // EVT-003이 노트를 열었을 때 두 단서가 이미 다 있도록 여기서 두 번째 더미 단서를 지급한다 —
            // 그래야 플레이어가 노트에서 잇기만 하면 되고, 테스트베드로 따로 넣어줄 필요가 없다.
            new AcquireClueAction { clueId = DummyClueEyeId, openCodexImmediately = false },
        };
        if (dood != null) wakeActions.Add(new SetEventFlagAction { flag = dood, flagValue = 2 });
        wakeActions.Add(new SetInputModeAction { mode = EnumManager.InputMode.Play });
        def.steps.Add(new EventStepData { label = "기상", enterActions = ToStepActions(wakeActions) });

        return def;
    }

    private static EventDefinition BuildEvt003Sample(EventFlagSO dood)
    {
        var def = new EventDefinition
        {
            eventId = "Dummy_EVT003",
            eventName = "해몽",
            purpose = "플레이 흐름 환기, 공략서 변환 플레이로 전환, 전투 대비",
            startTriggerDesc = "현실의 방 침대 옆 책상 위에 놓인 '노트(공략서)'와 상호작용.",
            preconditionDesc = "현실일 것 (GameStateEvent는 맵이 아니라 플래그만 판정하므로, 구현에서는 " +
                "dood == 2를 대리 조건으로 쓴다 — EVT-002가 끝나야 dood가 2가 되고 그 시점엔 항상 현실에 " +
                "있으므로 근사가 맞아떨어진다. 맵 자체를 보는 조건이 생기면 이 근사를 걷어낼 것.)",
            interruptCondition = "플레이어가 노트를 덮거나 UI를 닫을 때",
            retryPolicy = "해몽 완료 전까지 반복 확인 가능. 해몽 완료 후에는 완성된 규칙 상시 표기",
            linkedEvents = "메뉴 UI",
            narrativeContent =
                "현실의 책상에 지금까지 꿈에서 얻었던 미해몽 [단서]들이 흩어져 있음. 꿈속에서보다 단서들은 " +
                "비교적 선명한 모양과 뜻을 가진 것으로 바뀌어 있음. 노트를 열고 꿈속 단서들을 배열해 " +
                "'해몽'함. 해몽 후 유저가 규칙을 알게 되고 시스템 규칙도 추가됨. 이 때, 시스템 규칙이 " +
                "명확하게 이해되어도 되지만 다시 꿈에서 해몽된 단서를 확인한다면 흐릿하게 보일 것. " +
                "[단서 1 해몽]: 떨리는 날개 그림 해석 → '보스가 날개로 내리칠 때 패링한 후 [가위] " +
                "인터랙션을 할 것.' 시스템 해금(FLAG_USE_SCISSORS = True). [단서 2 해몽]: 노이즈가 낀 눈 " +
                "그림 해석 → '슬픔+분노 = [공포], 기쁨+분노 = [각성]' 감정 합성 공식 해금 " +
                "(FLAG_EMOTION_MERGE = True). 플레이어가 침대에 다시 누워 잠들기를 선택하면 변형된 꿈의 " +
                "세계 [검은 낙서의 심연] 맵 중앙에서 눈을 뜨며 복귀",
            targets = DefaultTargets(),
            preconditions = dood != null
                ? new[] { new GameStateEvent.EventFlagCondition { flag = dood, value = 2 } }
                : Array.Empty<GameStateEvent.EventFlagCondition>(),
            // "책상 위에 놓인 '노트(공략서)'와 상호작용" — 근처에서 확인 입력을 눌러야 발동.
            triggerKind = EventTriggerKind.Input,
            triggerRadius = 1.5f,
        };

        def.steps.Add(new EventStepData
        {
            label = "노트 열기 — 해몽 대기",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-003 S0 현실의 책상 — 노트를 편다" },
                new OpenWindowAction { windowName = "Note" },
                new DummyEventStepAction { label = "더미 단서 두 개(EVT-001/002가 이미 지급)가 노트 서랍에 있습니다 — '단서 연동' 모드로 둘을 이으면 해몽이 성립합니다" },
            },
            advanceWhenAny = new StateDecision[] { new DreamReadingResolvedDecision() },
        });

        // 해몽이 성립해도 곧바로 꿈으로 보내지 않는다 — 무엇이 해금됐는지 노트를 펼쳐 둔 채로
        // 확인할 시간을 준다. 실제 해금(플래그 세팅)은 DreamReadingModule이 이미 끝냈고,
        // 여기서는 안내만 남긴다(규칙 표시 UI가 생기면 이 자리를 그걸로 갈아끼우면 된다).
        def.steps.Add(new EventStepData
        {
            label = "규칙 해금 안내 — 노트를 닫을 때까지 대기",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-003 S1 해몽 성립 — 시스템 규칙이 해금되었습니다 (FLAG_USE_SCISSORS / FLAG_EMOTION_MERGE)" },
                new DummyEventStepAction { label = "노트를 닫으면 꿈으로 돌아갑니다" },
            },
            // 플레이어가 직접 노트를 덮는 것을 기다린다 — 기획서의 "중단 조건: 플레이어가 노트를
            // 덮거나 UI를 닫을 때"가 여기서 다음 단계로 넘어가는 신호가 된다.
            advanceWhenAny = new StateDecision[] { new WindowClosedDecision { windowName = "Note" } },
        });

        // 기획서의 "침대에 다시 누워 잠들기를 선택하면 꿈의 세계 맵 중앙에서 눈을 뜨며 복귀".
        // 침대 상호작용은 아직 없어 노트를 닫는 시점을 그 자리로 삼았다.
        MapDataSO dreamMap = FindDreamMap();
        def.steps.Add(new EventStepData
        {
            label = "꿈으로 복귀",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-003 S2 노트를 덮었습니다 — 꿈으로 돌아갑니다" },
                new SetInputModeAction { mode = EnumManager.InputMode.Cutscene },
                new ScreenFadeAction { targetColor = new Color(0f, 0f, 0f, 1f), duration = 0.6f },
                new ChangeMapAction { mapData = dreamMap },
            },
            advanceWhenAny = new StateDecision[]
            {
                new MapLoadedDecision { mapAddressableID = dreamMap != null ? dreamMap.mapAddressableID : "" },
            },
        });

        def.steps.Add(new EventStepData
        {
            label = "꿈에서 기상",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-003 S3 꿈으로 복귀 완료 — 중앙 집터로 가면 EVT-004가 시작됩니다" },
                new ScreenFadeAction { targetColor = new Color(0f, 0f, 0f, 0f), duration = 0.6f },
                new SetInputModeAction { mode = EnumManager.InputMode.Play },
            },
        });

        return def;
    }

    private static EventDefinition BuildEvt004Sample(EventFlagSO dood, EventFlagSO emotionMerge)
    {
        var conditions = new List<GameStateEvent.EventFlagCondition>();
        if (dood != null) conditions.Add(new GameStateEvent.EventFlagCondition { flag = dood, value = 2 });
        if (emotionMerge != null) conditions.Add(new GameStateEvent.EventFlagCondition { flag = emotionMerge, value = 1 });

        var def = new EventDefinition
        {
            eventId = "Dummy_EVT004",
            eventName = "분노한 금발과 전투",
            purpose = "감정 합성 및 패링 매커니즘이 있는 퍼즐 풀이로 긴장감 부여",
            startTriggerDesc = "중앙 집터로 재진입하기",
            preconditionDesc = "Dood == 2, 현실에서 꿈 해몽 완료 후 꿈 복귀",
            interruptCondition =
                "플레이어 캐릭터 사망 시 → 소름 돋는 사운드(불협화음?)와 함께 [EVT-003(현실의 방)]으로 " +
                "강제 복귀 리스폰 (2026-08-22: 침대 복귀는 미구현으로 보류, 현실의 방 강제 복귀만 우선 구현)",
            retryPolicy = "클리어 전까지 실패 시 계속 재시작",
            linkedEvents = "EVT-005",
            narrativeContent =
                "플레이어가 현실→꿈으로 재진입하면 주변은 완전 검은색. 밀려났던 방향 그대로 쭉 나아가보면 " +
                "하얀 낙서 공간이 나옴, 집터. 근처로 가면 이벤트 시작. 화면 전체가 붉은색 낙서로 뒤덮이며 " +
                "금발이 붉은색 [분노] 탄막 폭격을 시작함(기괴한 사운드 노이즈 배경음 재생). 플레이어는 " +
                "본인의 감정을 [분노(적색)]로 전환해 탄막을 튕겨 낼 수 있음(패링). 이를 주변의 백발이 " +
                "폭주해 뻗친 머리카락(날개?)에 반사해 금발의 분노를 폭주시켜야 함. 폭주 패턴 시, 플레이어는 " +
                "[공포(슬픔+분노)]를 조합하여 공포 상태로 탄막을 받아쳐 보스를 그로기 상태로 만듦. 그로기 " +
                "상태의 보스에게 다가가 [평안(기쁨+즐거움)] 파동을 주입하면 금발이 진정하며 대화 가능한 " +
                "NPC로 변경.\n" +
                "(전투 기믹 자체는 별도 담당 — 이 이벤트는 진입 지점까지만 구현한다. " +
                "Docs/Map_Event_Implementation_Plan.md 참고)",
            targets = DefaultTargets(),
            preconditions = conditions.ToArray(),
            // "중앙 집터로 재진입하기" — 근접하면 자동 발동.
            triggerKind = EventTriggerKind.Stay,
            triggerRadius = 3f,
        };

        def.steps.Add(new EventStepData
        {
            label = "전투 진입",
            enterActions = new EventStepAction[]
            {
                new DummyEventStepAction { label = "EVT-004 진입 — 여기부터 전투. 전투 기믹(패링/반사/그로기/각성)은 별도 담당" },
            },
        });

        return def;
    }

    private static StateMachineSO EnsureBossMachine()
    {
        string machinePath = $"{OutputFolder}/Dummy_BossStateMachine.asset";
        StateMachineSO existing = AssetDatabase.LoadAssetAtPath<StateMachineSO>(machinePath);
        if (existing != null) return existing;

        StateSO bossState = NewOrOverwriteState("Dummy_BossState");
        bossState.EnterActions = new StateAction[] { new DummyEventStepAction { label = "보스화된 NPC가 보스 상태로 진입" } };
        bossState.UpdateActions = Array.Empty<StateAction>();
        bossState.ExitActions = Array.Empty<StateAction>();
        bossState.transitions = Array.Empty<StateTransition>();
        bossState.additionalTransitions = Array.Empty<StateTransition>();
        bossState.actionSequence = Array.Empty<ActionSequence>();
        EditorUtility.SetDirty(bossState);

        StateMachineSO machine = ScriptableObject.CreateInstance<StateMachineSO>();
        machine.name = "Dummy_BossStateMachine";
        machine.customVariables = new CustomVariableSets();
        machine.allStates = new List<StateSO> { bossState };
        machine.initialState = bossState;
        AssetDatabase.CreateAsset(machine, machinePath);
        machine.UpdateStateMachine();
        EditorUtility.SetDirty(machine);
        return machine;
    }

    private static Line DummyLine(string speaker, string text) => new Line
    {
        name = speaker,
        characterName = speaker,
        sublines = new[] { text },
        choices = Array.Empty<string>(),
    };

    // 꿈 맵 — 플레이가 시작되는 맵(MapManager.initialMap)을 우선 쓰고, 씬에 MapManager가 없으면
    // isRealWorld가 꺼진 첫 MapDataSO로 떨어진다.
    private static MapDataSO FindDreamMap()
    {
        MapManager mapManager = UnityEngine.Object.FindObjectOfType<MapManager>();
        if (mapManager != null && mapManager.initialMap != null && !mapManager.initialMap.isRealWorld)
            return mapManager.initialMap;

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(MapDataSO)}"))
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (map != null && !map.isRealWorld) return map;
        }

        Debug.LogWarning("[EventChainEditorWindow] 꿈 맵(isRealWorld가 꺼진 MapDataSO)을 찾지 못했습니다 — " +
                         "EVT-003의 복귀 단계가 비어 있게 됩니다.");
        return null;
    }

    // isRealWorld가 켜진 MapDataSO를 프로젝트에서 찾는다. 여러 개면 첫 번째 — 지금은 하나뿐이고,
    // 진짜 "현실 침실" 맵이 생기면 그쪽에 플래그를 옮기면 이 코드는 그대로 따라간다.
    private static MapDataSO FindRealWorldMap()
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(MapDataSO)}"))
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (map != null && map.isRealWorld) return map;
        }

        Debug.LogWarning("[EventChainEditorWindow] isRealWorld가 켜진 MapDataSO가 없습니다 — EVT-002의 " +
                         "현실 이동 단계가 비어 있게 됩니다(도감/노트가 계속 잠겨 EVT-003을 진행할 수 없습니다).");
        return null;
    }

    private static EventFlagSO LoadFlag(string name)
    {
        var flag = AssetDatabase.LoadAssetAtPath<EventFlagSO>($"{FlagFolder}/{name}.asset");
        if (flag == null)
            Debug.LogWarning($"[EventChainEditorWindow] {FlagFolder}/{name}.asset을 찾지 못했습니다 — 관련 조건/갱신이 빠집니다.");
        return flag;
    }
}
