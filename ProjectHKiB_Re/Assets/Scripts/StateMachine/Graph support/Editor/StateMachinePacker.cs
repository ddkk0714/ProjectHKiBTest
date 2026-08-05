using System.Collections.Generic;
using System.Linq;
using GraphProcessor;
using StateMachine;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기존 형식(State를 외부 .asset 파일로 두는)의 StateMachineSO를 새 형식(모든 State를 기계 에셋
/// 안의 서브에셋으로 packing하고 StateMachineGraph를 동봉하는)으로 변환한다.
/// 목표 형태는 Delta_Base_StateMachine_test.asset 이다.
///
/// [손으로 YAML을 고치지 않는 이유]
/// State 하나가 수백 줄이고, 안에 [SerializeReference]로 직렬화된 Action/Decision 블록(RefIds)과
/// 상태끼리 서로를 가리키는 참조가 얽혀 있다. Lily만 해도 24개 상태 6,000여 줄이라 손으로 옮기면
/// 거의 확실히 깨진다. AssetDatabase에 맡기면 직렬화 세부는 Unity가 처리한다.
///
/// [변환은 언제나 무손실이다]
/// State의 내용은 손대지 않고 서브에셋으로 옮기기만 한다. 한때 "이름이 맞는 템플릿으로 State를
/// 통째로 교체하는" 모드가 같이 있었는데(Delta_Base_StateMachine_test와 똑같은 결과를 내려던
/// 것이다), Roza를 그 모드로 변환했더니 Idle/Walk/Run/Transform의 캐릭터 고유 전이가 통째로
/// 사라졌다 — Walk 8개 → 2개, Run 8개 → 2개. 쓸 일이 없는 데다 위험하기만 해서 없앴다.
/// 템플릿은 이제 exposedVariables(노출 변수) 구성을 가져오는 데만 쓴다.
///
/// [무엇을 하는가]
///   1. allStates의 각 외부 StateSO를 기계 에셋 안으로 복제해 넣는다(원본 파일은 건드리지 않는다).
///      allStates에 빠져 있지만 전이로 닿는 State도 따라가서 같이 넣는다 — 새 형식 기계에는
///      외부 State 참조가 남으면 안 되기 때문이다.
///   2. 복제본들 사이의 참조를 다시 엮는다 — initialState, allStates, 각 transition의
///      trueState/falseState가 외부 원본이 아니라 내부 복제본을 가리키게 한다.
///      _commandPairs는 UpdateStateMachine()으로 재생성한다.
///   3. 복제본을 packing 상태로 만들고(isPacked=1, exposedVariables는 템플릿에서 가져옴),
///      Enter/Update/ExitActions의 빈 칸(null)을 걷어낸다 — CompactActions 참고.
///   4. StateMachineGraph를 만들어 InitialStateNode + State별 StateNode를 역할별 레인으로 배치하고,
///      transitions의 trueState/falseState를 따라 edge를 잇는다.
///
/// [주의] 원본 기계 에셋을 직접 바꾸지 않고 "_packed" 사본을 새로 만든다. 변환 결과가 마음에
///        들지 않으면 사본만 지우면 되고, 기존 기계는 그대로 살아 있다.
/// </summary>
public static class StateMachinePacker
{
    // StateNodeView.Enable()이 노드 폭을 400으로 고정한다. 저장되는 rect도 같은 값으로 맞춰야
    // 아래 배치 계산과 실제 창 모습이 어긋나지 않는다(_test의 노드도 width 400이다).
    private const float NodeW = 400f;
    private const float NodeH = 200f;          // 세로는 인스펙터 내용에 따라 뷰가 다시 재는 값 — 시작값만 준다
    private const float ColGap = 160f;         // 노드 사이 가로 여백
    private const float RowGap = 460f;         // 한 레인이 줄바꿈될 때의 세로 여백(packing 안 된 노드도 들어갈 만큼)
    private const float CellW = NodeW + ColGap;
    private const float CellH = NodeH + RowGap;
    private const int MaxPerLane = 10;         // 레인 하나가 한 줄에 담는 최대 노드 수
    private const float LaneGapY = 340f;       // 레인 사이 여백 — 역할 경계가 눈에 보일 만큼 띄운다

    private const string TemplateFolder = "Assets/ScriptableObjects/StateTemplates";
    private const string PlayerPrefix = "Player";
    private const string DefaultExposedPath = "additionalTransitions";
    private const string DefaultExposedLabel = "Additional Transitions";

    private const string EtcRole = "Etc";
    private const string StateSuffix = "State";
    private static readonly string[] StageSuffixes =
        { "Start", "Keep", "End", "Enter", "Exit", "Begin", "Finish", "ing" };

    [MenuItem("Assets/State Machine/새 형식으로 변환 (Pack)", true)]
    private static bool ValidatePack() => Selection.activeObject is StateMachineSO;

    [MenuItem("Assets/State Machine/새 형식으로 변환 (Pack)")]
    private static void Pack()
    {
        if (Selection.activeObject is not StateMachineSO source) return;

        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(sourcePath))
        {
            Debug.LogError("[StateMachinePacker] 에셋 경로를 찾을 수 없습니다.");
            return;
        }

        if (source.allStates == null || source.allStates.Count == 0)
        {
            Debug.LogError($"[StateMachinePacker] '{source.name}'의 allStates가 비어 있습니다.");
            return;
        }

        if (IsAlreadyPacked(source))
        {
            Debug.LogWarning($"[StateMachinePacker] '{source.name}'은 이미 새 형식입니다 — State가 이 에셋 안에 들어 있습니다.");
            return;
        }

        string targetPath = AssetDatabase.GenerateUniqueAssetPath(
            sourcePath.Replace(".asset", "_packed.asset"));

        bool ok = EditorUtility.DisplayDialog(
            "State Machine 변환",
            $"'{source.name}'의 State {source.allStates.Count}개를 새 형식으로 packing합니다.\n" +
            $"결과: {targetPath}\n\n" +
            "State의 내용(전이·액션·decision)은 그대로 옮깁니다 — 무손실입니다.\n" +
            "다만 Enter/Update/ExitActions의 빈 칸(None)은 걷어냅니다 — 실행 시 " +
            "NullReferenceException을 내기만 하는 자리입니다.\n\n" +
            "원본 기계와 State 파일들은 그대로 유지됩니다.",
            "변환", "취소");

        if (!ok) return;

        StateMachineSO packed = CreatePackedCopy(source, targetPath, LoadTemplates());

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (packed != null)
        {
            Selection.activeObject = packed;
            EditorGUIUtility.PingObject(packed);
        }
    }

    /// <summary>State가 이미 기계 에셋 안에 들어 있는지 — 그렇다면 변환할 것이 없다.</summary>
    private static bool IsAlreadyPacked(StateMachineSO source)
    {
        string path = AssetDatabase.GetAssetPath(source);
        List<StateSO> states = source.allStates.Where(s => s != null).ToList();
        return states.Count > 0 && states.All(s => AssetDatabase.GetAssetPath(s) == path);
    }

    private static StateMachineSO CreatePackedCopy(StateMachineSO source, string targetPath,
                                                   List<StateSO> templates)
    {
        // ── 1. 기계 본체 복제 ────────────────────────────────────
        StateMachineSO packed = Object.Instantiate(source);
        packed.name = System.IO.Path.GetFileNameWithoutExtension(targetPath);
        packed.graph = null; // 원본 그래프를 물고 오면 안 된다 — 아래에서 새로 만든다
        AssetDatabase.CreateAsset(packed, targetPath);

        // ── 2. 복제 대상 확정 ───────────────────────────────────
        // allStates + initialState 에서 시작해 전이를 따라 닿는 State를 모두 끌어온다.
        // 새 형식 기계에 외부 State 참조가 남으면 그래프에 노드 없는 전이가 생긴다.
        List<StateSO> ordered = CollectStates(source, out List<StateSO> pulledIn);

        string prefix = CommonPrefix(ordered.Select(s => s.name).ToList());
        int exposedFromTemplate = 0;
        var compacted = new List<string>();

        // 같은 State가 allStates에 두 번 들어 있어도 대응표가 중복을 흡수한다.
        var map = new Dictionary<StateSO, StateSO>();

        foreach (StateSO original in ordered)
        {
            if (map.ContainsKey(original)) continue;

            StateSO copy = Object.Instantiate(original);
            copy.name = original.name;
            copy.isTemplate = false;

            // 템플릿은 내용이 아니라 "무엇을 노출할지"만 가져온다.
            if (ApplyPackingSettings(copy, MatchTemplate(original.name, prefix, templates)))
                exposedFromTemplate++;

            int removed = CompactActions(copy);
            if (removed > 0) compacted.Add($"{original.name}({removed})");

            AssetDatabase.AddObjectToAsset(copy, packed);
            map[original] = copy;
        }

        // ── 3. 참조 재연결 ──────────────────────────────────────
        packed.allStates = ordered.Select(s => map[s]).Distinct().ToList();
        packed.initialState = source.initialState != null ? map[source.initialState] : null;

        // 자기 자신을 가리키는 전이는 Instantiate가 복제본 쪽으로 알아서 돌려놓는다. 이미 내부를
        // 가리키고 있으니 재연결 대상도 아니고 경고 대상도 아니다 — 이 집합으로 걸러낸다.
        var copies = new HashSet<StateSO>(map.Values);

        var unresolved = new HashSet<string>();
        foreach (StateSO copy in map.Values)
        {
            RemapTransitions(copy.transitions, map, copies, copy.name, unresolved);
            RemapTransitions(copy.additionalTransitions, map, copies, copy.name, unresolved);
            EditorUtility.SetDirty(copy);
        }

        if (unresolved.Count > 0)
            Debug.LogWarning($"[StateMachinePacker] '{packed.name}': 이 기계 안에 대응하는 State가 없는 전이가 " +
                             $"있어 외부 참조로 남겨 두었습니다 — {string.Join(", ", unresolved)}");

        // _commandPairs는 allStates를 훑어 다시 만드는 쪽이 안전하다.
        packed.UpdateStateMachine();
        foreach (StateSO copy in map.Values) EditorUtility.SetDirty(copy); // temporaryID가 여기서 바뀐다

        // ── 4. 그래프 생성 ──────────────────────────────────────
        BuildGraph(packed);

        EditorUtility.SetDirty(packed);

        if (compacted.Count > 0)
            Debug.Log($"[StateMachinePacker] '{packed.name}': Enter/Update/ExitActions의 빈 칸(null)을 걷어냈습니다 — " +
                      $"{string.Join(", ", compacted)}. 원본 State 파일에는 그대로 남아 있으니, 원본 기계를 쓰면 " +
                      "해당 State에서 NullReferenceException이 계속 납니다.");

        string extra = pulledIn.Count > 0
            ? $" (allStates에 없었지만 전이로 닿아 함께 넣은 State {pulledIn.Count}개: {string.Join(", ", pulledIn.Select(s => s.name))})"
            : string.Empty;
        Debug.Log($"[StateMachinePacker] '{packed.name}' 생성 완료 — State {map.Count}개를 내부로 packing했습니다. " +
                  $"(노출 설정을 템플릿에서 가져온 State {exposedFromTemplate}개){extra}");
        return packed;
    }

    /// <summary>
    /// 복제할 State 목록. allStates 순서를 그대로 유지하고, 그 뒤에 initialState와
    /// 전이로만 닿는 State를 덧붙인다.
    /// </summary>
    private static List<StateSO> CollectStates(StateMachineSO source, out List<StateSO> pulledIn)
    {
        var ordered = new List<StateSO>();
        var known = new HashSet<StateSO>();

        void Add(StateSO s)
        {
            if (s == null || !known.Add(s)) return;
            ordered.Add(s);
        }

        foreach (StateSO s in source.allStates) Add(s);
        int declared = ordered.Count;
        Add(source.initialState); // allStates에 빠져 있어도 초기 상태는 반드시 안으로 넣는다

        // 전이를 따라가며 닫힘(closure)을 만든다. 새로 들어온 State의 전이도 다시 훑는다.
        for (int i = 0; i < ordered.Count; i++)
            foreach (StateSO next in TargetsOf(ordered[i]))
                Add(next);

        pulledIn = ordered.Skip(declared).ToList();
        return ordered;
    }

    /// <summary>Assets/ScriptableObjects/StateTemplates 아래의 템플릿 State들.</summary>
    private static List<StateSO> LoadTemplates()
    {
        if (!AssetDatabase.IsValidFolder(TemplateFolder)) return new List<StateSO>();

        return AssetDatabase.FindAssets("t:StateSO", new[] { TemplateFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<StateSO>)
            .Where(t => t != null && t.isTemplate)
            .ToList();
    }

    /// <summary>
    /// EnterActions / UpdateActions / ExitActions의 빈 칸(null)을 걷어낸다.
    ///
    /// 인스펙터에서 배열 칸만 늘리고 SubclassSelector를 None으로 둔 자리는 YAML에 `rid: -2`로
    /// 남는다. StateSO의 세 실행 루프에는 null 검사가 없어서(StateSO.UpdateState 등)
    /// 그 State에 들어가는 순간 NullReferenceException이 난다 — Idle처럼 initialState면
    /// 매 프레임 터진다. 실제로 Roza/Lily/Enemy_Rusher의 State 29곳에 이런 칸이 있었다.
    ///
    /// 이 자리의 null은 의도일 수가 없다. 아무 동작도 하지 않으면서 예외만 던지기 때문이다.
    /// 반면 actionSequence의 null은 뜻이 있다(액션 없이 time만큼 쉬는 칸 — Walk/Run의 발소리
    /// 사이 간격이 이것이다). transition의 Action도 null이 "전이할 때 아무것도 안 함"이라는
    /// 정상 값이다. 그래서 이 둘은 건드리지 않는다. 둘 다 호출부가 `?.`로 막혀 있기도 하다.
    ///
    /// 원본 State 파일은 그대로 두므로(packer의 원칙) 이 정리는 packed 사본에만 적용된다.
    /// </summary>
    /// <returns>걷어낸 빈 칸 수</returns>
    private static int CompactActions(StateSO copy)
    {
        int removed = 0;
        copy.EnterActions = Compact(copy.EnterActions, ref removed);
        copy.UpdateActions = Compact(copy.UpdateActions, ref removed);
        copy.ExitActions = Compact(copy.ExitActions, ref removed);
        return removed;

        static StateAction[] Compact(StateAction[] actions, ref int count)
        {
            if (actions == null) return null;

            int nulls = actions.Count(a => a == null);
            if (nulls == 0) return actions;

            count += nulls;
            return actions.Where(a => a != null).ToArray();
        }
    }

    /// <summary>
    /// 복제본의 packing 설정(isPacked / exposedVariables)을 정한다.
    ///
    /// isPacked는 무조건 켠다. 꺼져 있으면 그래프 창이 버벅인다 — StateNodeView가 노드마다
    /// Editor.CreateEditor로 StateSO 인스펙터를 만들어 IMGUIContainer에 넣는데 IMGUI는
    /// 리페인트마다 다시 그려지고, 꺼져 있으면 StateSOEditor가 DrawPropertiesExcluding으로
    /// StateSO 전체(전이·decisions·Action 배열의 SubclassSelector 드롭다운까지)를 그린다.
    /// 노드 수만큼 그 비용이 곱해진다. _test의 State도 전부 isPacked=1이다.
    ///
    /// 무엇을 노출할지는 새로 만들지 않고 이미 있는 장치를 쓴다. 우선순위:
    ///   1. 원본이 이미 노출 목록을 갖고 있으면 그대로 존중한다(Add Variable로 큐레이션한 결과).
    ///   2. StateTemplates에 같은 State의 템플릿이 있으면 그 설정을 가져온다.
    ///      _test의 PlayerIdle/PlayerWalk/PlayerRun이 이 경로로, 템플릿과 똑같이
    ///      Additional Transitions 하나만 노출한다(Transform/TransformStart는 아무것도 노출 안 함).
    ///   3. 둘 다 없으면 Player* 템플릿들이 쓰는 관례를 따라 additionalTransitions만 노출한다.
    ///
    /// 노출 구성을 바꾸고 싶으면 노드에서 Unpack → Add Variable → Pack 하면 되고,
    /// 그 구성을 다음 변환에도 쓰고 싶으면 Make Template을 눌러 템플릿으로 저장하면
    /// 이름이 같은 State에 2번 경로로 자동 적용된다.
    /// </summary>
    /// <returns>템플릿에서 가져왔으면 true</returns>
    private static bool ApplyPackingSettings(StateSO copy, StateSO template)
    {
        copy.isPacked = true;

        if (copy.exposedVariables != null && copy.exposedVariables.Count > 0) return false;

        if (template != null)
        {
            copy.exposedVariables = new List<ExposedVariable>(template.exposedVariables);
            return true;
        }

        copy.exposedVariables = new List<ExposedVariable>
        {
            new() { propertyPath = DefaultExposedPath, displayName = DefaultExposedLabel }
        };
        return false;
    }

    /// <summary>
    /// State 이름으로 템플릿을 찾는다 — 찾은 템플릿은 exposedVariables를 가져오는 데만 쓴다.
    /// 좁은 조건부터 차례로 넓힌다.
    ///   1. 정확히 같은 이름
    ///   2. 기계 공통 접두사와 "State" 꼬리표, 템플릿 쪽 "Player" 접두사를 걷어낸 알맹이끼리
    ///      (Delta_Lily_IdleState ↔ PlayerIdle, Delta_Base_TransformStartState ↔ TransformStart)
    ///   3. 거기서 단계 낱말까지 뗀 알맹이끼리 (Delta_Base_TransformingState ↔ Transform)
    ///
    /// 3번을 마지막에 두는 게 중요하다. 먼저 돌리면 TransformStart가 Transform 템플릿에
    /// 잡혀 버린다. DodgeStart처럼 대응하는 Dodge 템플릿이 없는 이름은 3번에서도 못 찾고
    /// 그대로 남는다 — 의도한 동작이다.
    /// </summary>
    private static StateSO MatchTemplate(string stateName, string prefix, List<StateSO> templates)
    {
        StateSO exact = templates.FirstOrDefault(
            t => string.Equals(t.name, stateName, System.StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 템플릿 이름은 항상 단계 낱말을 남긴 채로 정규화한다. 템플릿 쪽까지 떼면
        // TransformStart도 "Transform"이 되어 Transforming이 어느 쪽에 붙을지 알 수 없어진다.
        return Find(TemplateCore(stateName, prefix, false))
            ?? Find(TemplateCore(stateName, prefix, true));

        StateSO Find(string core) => string.IsNullOrEmpty(core)
            ? null
            : templates.FirstOrDefault(t => string.Equals(
                TemplateCore(t.name, string.Empty, false), core, System.StringComparison.OrdinalIgnoreCase));
    }

    private static string TemplateCore(string name, string prefix, bool stripStage)
    {
        string s = name.StartsWith(prefix) ? name.Substring(prefix.Length) : name;
        if (s.EndsWith(StateSuffix) && s.Length > StateSuffix.Length)
            s = s.Substring(0, s.Length - StateSuffix.Length);
        if (s.StartsWith(PlayerPrefix) && s.Length > PlayerPrefix.Length)
            s = s.Substring(PlayerPrefix.Length);
        if (stripStage) s = StripStage(s);
        return s;
    }

    private static void RemapTransitions(StateTransition[] transitions, Dictionary<StateSO, StateSO> map,
                                         HashSet<StateSO> copies, string ownerName,
                                         HashSet<string> unresolved)
    {
        if (transitions == null) return;

        foreach (StateTransition t in transitions)
        {
            if (t == null) continue;
            t.trueState = Remap(t.trueState);
            t.falseState = Remap(t.falseState);
        }

        StateSO Remap(StateSO target)
        {
            if (target == null) return null;
            if (copies.Contains(target)) return target;          // 이미 내부 복제본을 가리킨다
            if (map.TryGetValue(target, out StateSO copy)) return copy;

            // CollectStates가 전이를 따라 closure를 만들었으니 여기까지 오면 안 된다.
            unresolved.Add($"{ownerName} → {target.name}");
            return target;
        }
    }

    private static void BuildGraph(StateMachineSO packed)
    {
        var graph = ScriptableObject.CreateInstance<StateMachineGraph>();
        graph.name = packed.name + "Editor";
        graph.targetStateMachine = packed;
        AssetDatabase.AddObjectToAsset(graph, packed);
        packed.graph = graph;

        // 역할별로 가로 레인 하나씩 잡아 배치한다. 자동 배치는 어디까지나 시작점이고,
        // 실제 배선 모양은 그래프 창에서 직접 정리하는 걸 전제로 한다.
        List<List<StateSO>> lanes = BuildLanes(packed);

        var initialNode = BaseNode.CreateFromType<InitialStateNode>(Vector2.zero);
        AddNode(graph, initialNode);

        var nodeOf = new Dictionary<StateSO, StateNode>();
        float laneTop = 0f;

        for (int l = 0; l < lanes.Count; l++)
        {
            List<StateSO> lane = lanes[l];
            int rows = Mathf.CeilToInt(lane.Count / (float)MaxPerLane);

            for (int i = 0; i < lane.Count; i++)
            {
                StateSO state = lane[i];
                if (state == null || nodeOf.ContainsKey(state)) continue;

                var node = BaseNode.CreateFromType<StateNode>(
                    new Vector2(i % MaxPerLane * CellW, laneTop + i / MaxPerLane * CellH));

                // CreateFromType은 rect를 100x100으로 잡는다. StateNodeView가 폭을 400으로 고정하니
                // 저장되는 값도 맞춰 둔다 — _test의 노드도 width 400이다.
                node.position.size = new Vector2(NodeW, NodeH);

                // 포트는 Initialize() 안에서 stateSO.transitions를 읽어 만들어진다 — 먼저 붙여야 한다.
                node.stateSO = state;
                AddNode(graph, node);

                // _test처럼 노드 이름을 State 이름으로 박아 둔다. 이름이 같으므로
                // StateNode.SetCustomName의 에셋 rename 분기는 타지 않는다.
                node.SetCustomName(state.name);

                nodeOf[state] = node;
            }

            // InitialStateNode는 초기 상태 레인의 왼쪽에 붙여 진입점이 눈에 띄게 한다.
            if (l == 0) initialNode.position.position = new Vector2(-CellW, laneTop);

            laneTop += rows * CellH + LaneGapY;
        }

        if (packed.initialState != null && nodeOf.TryGetValue(packed.initialState, out StateNode firstNode))
            Connect(graph, initialNode, nameof(InitialStateNode.outputTransitions), null, firstNode);

        // 전이를 따라 edge를 잇는다. 포트 identifier 규칙은 StateNode.GetPortsForTransitions와 같다.
        // additionalTransitions는 identifier가 transitions와 겹쳐(둘 다 "T_{i}_...") 구분이 불가능하므로
        // 여기서는 transitions만 잇는다. 나머지는 그래프 창에서 손으로 연결하면 된다.
        foreach (StateSO state in nodeOf.Keys)
        {
            StateNode fromNode = nodeOf[state];
            if (state.transitions == null) continue;

            for (int i = 0; i < state.transitions.Length; i++)
            {
                StateTransition t = state.transitions[i];
                if (t == null) continue;

                if (t.showTrueStatePort && t.trueState != null && nodeOf.TryGetValue(t.trueState, out StateNode trueNode))
                    Connect(graph, fromNode, nameof(StateNode.outputTransitions), $"T_{i}_True", trueNode);

                if (t.showFalseStatePort && t.falseState != null && nodeOf.TryGetValue(t.falseState, out StateNode falseNode))
                    Connect(graph, fromNode, nameof(StateNode.outputTransitions), $"T_{i}_False", falseNode);
            }
        }

        FrameGraph(graph);

        EditorUtility.SetDirty(graph);
    }

    /// <summary>
    /// 그래프 창을 처음 열었을 때 전체가 화면에 들어오도록 시점을 맞춰 둔다.
    /// (_test도 position/scale이 잡혀 있다. 없으면 원점·100%로 열려 노드 하나만 크게 보인다.)
    /// </summary>
    private static void FrameGraph(BaseGraph graph)
    {
        if (graph.nodes.Count == 0) return;

        Rect bounds = graph.nodes[0].position;
        foreach (BaseNode n in graph.nodes)
        {
            bounds.xMin = Mathf.Min(bounds.xMin, n.position.xMin);
            bounds.yMin = Mathf.Min(bounds.yMin, n.position.yMin);
            bounds.xMax = Mathf.Max(bounds.xMax, n.position.xMax);
            bounds.yMax = Mathf.Max(bounds.yMax, n.position.yMax);
        }

        // 대략적인 창 크기 기준. 정확할 필요는 없고 "다 보이는 배율"이면 된다.
        const float ViewW = 1600f, ViewH = 900f;
        float zoom = Mathf.Clamp(
            Mathf.Min(ViewW / Mathf.Max(bounds.width, 1f), ViewH / Mathf.Max(bounds.height, 1f)), 0.15f, 1f);

        graph.scale = new Vector3(zoom, zoom, 1f);
        graph.position = new Vector3(-bounds.xMin * zoom, -bounds.yMin * zoom, 0f);
    }

    /// <summary>
    /// State를 역할별 레인으로 묶는다. 역할은 이름에서 뽑는다 — 이 프로젝트의 State 이름이
    /// "{캐릭터}_{역할}{단계}State" 꼴을 지키고 있어서(Delta_Lily_DodgeStartState,
    /// Delta_Lily_NormalAttack3State, Delta_Lily_Groggy_KeepState …) 꽤 잘 들어맞는다.
    ///
    ///   Delta_Lily_ 처럼 모든 State가 공유하는 접두사를 떼고 → "State" 꼬리표를 떼고
    ///   → 끝의 숫자를 떼고(NormalAttack3 → NormalAttack)
    ///   → 끝의 단계 낱말을 뗀다(DodgeStart → Dodge, Transforming → Transform).
    ///
    /// 레인 순서는 초기 상태로부터의 전이 거리(BFS)를 따르므로, 위에서 아래로 읽으면 대체로
    /// 진행 순서가 된다. 이름 규칙에서 벗어나 혼자 남는 State들은 마지막 Etc 레인으로 모은다.
    /// </summary>
    private static List<List<StateSO>> BuildLanes(StateMachineSO packed)
    {
        List<StateSO> states = packed.allStates.Where(s => s != null).Distinct().ToList();
        if (states.Count == 0) return new List<List<StateSO>>();

        string prefix = CommonPrefix(states.Select(s => s.name).ToList());

        var roles = new Dictionary<string, List<StateSO>>();
        foreach (StateSO s in states)
        {
            string key = RoleOf(s.name, prefix);
            if (!roles.TryGetValue(key, out List<StateSO> list)) roles[key] = list = new List<StateSO>();
            list.Add(s);
        }

        // 다른 역할의 접두사면 그쪽으로 흡수한다(DodgeSkill → Dodge). 긴 쪽부터 처리해야
        // 짧은 역할이 먼저 사라져 버리는 일이 없다.
        foreach (string key in roles.Keys.OrderByDescending(k => k.Length).ToList())
        {
            if (key == EtcRole || !roles.ContainsKey(key)) continue;
            string parent = roles.Keys.FirstOrDefault(k => k != key && k != EtcRole && key.StartsWith(k));
            if (parent == null) continue;

            roles[parent].AddRange(roles[key]);
            roles.Remove(key);
        }

        // 혼자뿐인 역할은 레인 하나를 차지할 만큼의 의미가 없다 — Etc로 모은다.
        var etc = new List<StateSO>();
        foreach (string key in roles.Keys.Where(k => k != EtcRole && roles[k].Count == 1).ToList())
        {
            etc.AddRange(roles[key]);
            roles.Remove(key);
        }
        if (etc.Count > 0)
        {
            if (!roles.ContainsKey(EtcRole)) roles[EtcRole] = new List<StateSO>();
            roles[EtcRole].AddRange(etc);
        }

        Dictionary<StateSO, int> depth = ComputeDepths(packed, states);
        int DepthOf(StateSO s) => depth.TryGetValue(s, out int d) ? d : int.MaxValue;

        var lanes = new List<List<StateSO>>();

        // 초기 상태는 진입점이라 맨 위 전용 레인으로 뽑는다.
        if (packed.initialState != null)
        {
            foreach (List<StateSO> list in roles.Values) list.Remove(packed.initialState);
            lanes.Add(new List<StateSO> { packed.initialState });
        }

        IEnumerable<string> ordered = roles.Keys
            .Where(k => roles[k].Count > 0)
            .OrderBy(k => k == EtcRole ? 1 : 0)             // Etc는 항상 마지막
            .ThenBy(k => roles[k].Min(DepthOf))             // 초기 상태에서 가까운 역할부터
            .ThenBy(k => k);

        foreach (string key in ordered)
            lanes.Add(roles[key].OrderBy(DepthOf).ThenBy(s => s.name).ToList());

        return lanes;
    }

    /// <summary>모든 이름이 공유하는 접두사를 '_' 경계까지만 잘라서 돌려준다.</summary>
    private static string CommonPrefix(List<string> names)
    {
        if (names.Count < 2) return string.Empty;

        string p = names[0];
        foreach (string n in names)
        {
            int i = 0;
            while (i < p.Length && i < n.Length && p[i] == n[i]) i++;
            p = p.Substring(0, i);
        }

        int cut = p.LastIndexOf('_');
        return cut >= 0 ? p.Substring(0, cut + 1) : string.Empty;
    }

    private static string RoleOf(string name, string prefix)
    {
        string s = name.StartsWith(prefix) ? name.Substring(prefix.Length) : name;
        if (s.EndsWith(StateSuffix) && s.Length > StateSuffix.Length)
            s = s.Substring(0, s.Length - StateSuffix.Length);

        s = s.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        s = StripStage(s);

        return string.IsNullOrEmpty(s) ? EtcRole : s;
    }

    /// <summary>끝에 붙은 단계 낱말을 하나 뗀다 (DodgeStart → Dodge, Transforming → Transform).</summary>
    private static string StripStage(string s)
    {
        foreach (string stage in StageSuffixes)
            if (s.Length > stage.Length && s.EndsWith(stage))
                return s.Substring(0, s.Length - stage.Length).TrimEnd('_');

        return s;
    }

    /// <summary>초기 상태로부터 전이를 따라간 거리. 닿지 않는 State는 사전에 들어가지 않는다.</summary>
    private static Dictionary<StateSO, int> ComputeDepths(StateMachineSO packed, List<StateSO> states)
    {
        var known = new HashSet<StateSO>(states);
        var depth = new Dictionary<StateSO, int>();
        var queue = new Queue<StateSO>();

        if (packed.initialState != null && known.Contains(packed.initialState))
        {
            depth[packed.initialState] = 0;
            queue.Enqueue(packed.initialState);
        }

        while (queue.Count > 0)
        {
            StateSO current = queue.Dequeue();
            foreach (StateSO next in TargetsOf(current))
            {
                if (next == null || !known.Contains(next) || depth.ContainsKey(next)) continue;
                depth[next] = depth[current] + 1;
                queue.Enqueue(next);
            }
        }

        return depth;
    }

    private static IEnumerable<StateSO> TargetsOf(StateSO state)
    {
        foreach (StateTransition[] set in new[] { state.transitions, state.additionalTransitions })
        {
            if (set == null) continue;
            foreach (StateTransition t in set)
            {
                if (t == null) continue;
                yield return t.trueState;
                yield return t.falseState;
            }
        }
    }

    /// <summary>
    /// BaseGraph.AddNode의 동작만 그대로 수행한다. StateMachineGraph.AddNode를 쓰면 새 StateSO를
    /// 만들어 allStates에 밀어 넣기 때문에(그래프 창에서 노드를 새로 만드는 경로) 여기서는 쓸 수 없다.
    /// </summary>
    private static void AddNode(BaseGraph graph, BaseNode node)
    {
        graph.nodes.Add(node);
        graph.nodesPerGUID[node.GUID] = node;
        node.Initialize(graph);
    }

    /// <summary>
    /// edge를 만들어 그래프에 직접 넣는다.
    ///
    /// BaseGraph.Connect를 쓰면 안 된다. 그쪽은 양쪽 노드의 OnEdgeConnected → UpdateAllPorts를 태우고,
    /// 그 포트 재생성 연쇄가 방금 만든 edge를 도로 Disconnect해 버리는 경우가 있다(Lily 변환에서
    /// 85개 중 1개가 이렇게 사라졌다). 여기서 필요한 건 직렬화될 edge 레코드뿐이고, 전이 데이터
    /// (trueState/falseState)는 이미 3단계에서 올바르게 맞춰 놨으므로 OnEdgeConnected가 해 주는
    /// 일은 어차피 중복이다. 에셋을 다시 열 때 BaseGraph.OnEnable이 edge를 Deserialize하면서
    /// 포트 연결과 OnEdgeConnected를 정상적으로 다시 태운다.
    /// </summary>
    private static void Connect(BaseGraph graph, BaseNode fromNode, string outputFieldName,
                                string outputIdentifier, StateNode toNode)
    {
        NodePort outputPort = fromNode.GetPort(outputFieldName, outputIdentifier);
        NodePort inputPort = toNode.GetPort(nameof(StateNode.inputState), null);

        if (outputPort == null || inputPort == null)
        {
            Debug.LogWarning($"[StateMachinePacker] 포트를 찾지 못해 연결을 건너뜁니다 — " +
                             $"{fromNode.name}.{outputFieldName}[{outputIdentifier}] → {toNode.name}");
            return;
        }

        graph.edges.Add(SerializableEdge.CreateNewEdge(graph, inputPort, outputPort));
    }
}
