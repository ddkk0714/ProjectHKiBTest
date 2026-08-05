using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// StateSO 안의 PlayerInputDecision에서 비어 있는 _trigger(InputActionReference)를
/// 옛 _inputType 값에 맞는 InputAction으로 채운다.
///
/// [왜 필요한가]
/// 2026-07-16 "input system simplified"에서 PlayerInputDecision이 EnumManager.InputType 하나로
/// 판정하던 방식(InputManager.GetInputByEnum)에서 InputActionReference를 직접 물고 있는 방식으로
/// 바뀌었다. 그때 데이터 배선은 Delta_Base와 StateTemplates에만 했고 Roza/Lily는 남았다.
/// _trigger가 null이면 Decide()가 항상 false를 돌려주므로(PlayerInputDecision.cs) 그 전이는 죽는다.
/// negate가 걸린 전이는 반대로 항상 통과해서, Idle→Walk는 안 되고 Walk→Idle은 즉시 되는 식으로
/// 캐릭터가 아예 움직이지 못한다.
///
/// [왜 손으로 YAML을 못 고치나]
/// InputActionReference는 .inputactions의 서브에셋이고 그 fileID는 Unity가 임포트할 때 내부 해시로
/// 만든다(.meta의 internalIDToNameTable도 비어 있다). 재현할 수 없으므로 AssetDatabase에게
/// 물어보는 수밖에 없다. 그래서 에디터 도구다.
///
/// [매핑 근거]
/// 아래 표는 InputManager.GetInputByEnum의 switch를 그대로 옮긴 것이다. 그 switch에 없던
/// InputType은 옛 코드에서도 `_ => false`였으므로 일부러 비워 둔다 — 채우면 동작이 바뀐다.
///
/// [쓰는 법]
/// Project 창에서 StateMachineSO / StateSO / 폴더를 고르고
///   State Machine ▸ 입력 배선 검사 (변경 없음)  — 무엇이 바뀔지 먼저 본다
///   State Machine ▸ 입력 배선 (_inputType → InputAction)
/// 이미 _trigger가 채워져 있는 Decision은 건드리지 않는다.
/// </summary>
public static class PlayerInputDecisionWirer
{
    private const string ActionsAssetPath = "Assets/Scripts/PlayerAction.inputactions";
    private const string PlayMap = "PLAY";

    /// <summary>InputProcessType.InProgress. 옛 판정이 전부 inProgress/현재값 기반이었다.</summary>
    private const int InProgress = (int)EnumManager.InputProcessType.InProgress;

    /// <summary>
    /// EnumManager.InputType → PLAY 맵의 액션 이름.
    /// 왼쪽 주석은 InputManager.GetInputByEnum에 있던 원래 판정식이다.
    /// </summary>
    private static readonly Dictionary<EnumManager.InputType, string> ActionOf = new()
    {
        { EnumManager.InputType.OnMove, "Move" },              // Move.ReadValue<Vector2>() != zero
        { EnumManager.InputType.OnSprint, "Sprint" },          // Sprint.inProgress
        { EnumManager.InputType.HasDodge, "Dodge" },           // Dodge.inProgress
        { EnumManager.InputType.HasDInput, "MovePressedD" },   // Move.ReadValue().y < 0
        { EnumManager.InputType.HasLInput, "MovePressedL" },   // Move.ReadValue().x < 0
        { EnumManager.InputType.HasRInput, "MovePressedR" },   // Move.ReadValue().x > 0
        { EnumManager.InputType.HasUInput, "MovePressedU" },   // Move.ReadValue().y > 0
        { EnumManager.InputType.HasAttack, "Attack" },         // Attack.inProgress
        { EnumManager.InputType.HasSkill, "Skill" },           // Skill.inProgress
    };

    [MenuItem("Assets/State Machine/입력 배선 검사 (변경 없음)", true)]
    [MenuItem("Assets/State Machine/입력 배선 (_inputType → InputAction)", true)]
    private static bool Validate() => CollectStates().Count > 0;

    [MenuItem("Assets/State Machine/입력 배선 검사 (변경 없음)")]
    private static void DryRun() => Run(false);

    [MenuItem("Assets/State Machine/입력 배선 (_inputType → InputAction)")]
    private static void Wire() => Run(true);

    private static void Run(bool apply)
    {
        Dictionary<string, InputActionReference> references = LoadPlayActionReferences();
        if (references == null) return;

        List<StateSO> states = CollectStates();
        if (states.Count == 0)
        {
            Debug.LogWarning("[InputWirer] 선택한 항목에서 StateSO를 찾지 못했습니다.");
            return;
        }

        var wired = new List<string>();
        var normalized = new List<string>();
        var skippedFilled = new List<string>();
        var unmapped = new SortedSet<string>();
        var missingAction = new SortedSet<string>();

        foreach (StateSO state in states)
        {
            var serialized = new SerializedObject(state);
            bool touched = false;

            SerializedProperty it = serialized.GetIterator();
            while (it.Next(true))
            {
                if (it.name != "_inputType") continue;

                // PlayerInputDecision인지 확인 — 형제로 _trigger/_type이 같이 있어야 한다.
                string basePath = it.propertyPath.Substring(0, it.propertyPath.Length - "_inputType".Length);
                SerializedProperty trigger = serialized.FindProperty(basePath + "_trigger");
                SerializedProperty type = serialized.FindProperty(basePath + "_type");
                if (trigger == null || type == null) continue;

                // 이미 채워져 있다면 새로 배선하지 않는다. 다만 숨김 사본을 가리키고 있으면
                // 같은 액션의 정본으로 바꿔 준다(액션이 달라지지 않으므로 동작은 그대로다).
                if (trigger.objectReferenceValue != null)
                {
                    if (trigger.objectReferenceValue is not InputActionReference current
                        || !current.hideFlags.HasFlag(HideFlags.HideInHierarchy)
                        || current.action?.actionMap?.name != PlayMap
                        || !references.TryGetValue(current.action.name, out InputActionReference canonical)
                        || canonical == current)
                    {
                        skippedFilled.Add(state.name);
                        continue;
                    }

                    normalized.Add($"{state.name} [{PlayMap}/{current.action.name} 숨김 사본 → 정본]");

                    if (!apply) continue;

                    trigger.objectReferenceValue = canonical; // _type은 건드리지 않는다
                    touched = true;
                    continue;
                }

                var inputType = (EnumManager.InputType)it.enumValueIndex;
                if (!ActionOf.TryGetValue(inputType, out string actionName))
                {
                    // 옛 GetInputByEnum에서도 false를 돌려주던 값이다. 채우면 동작이 바뀌므로 건드리지 않는다.
                    unmapped.Add($"{state.name}: {inputType}");
                    continue;
                }

                if (!references.TryGetValue(actionName, out InputActionReference reference))
                {
                    missingAction.Add($"{PlayMap}/{actionName}");
                    continue;
                }

                wired.Add($"{state.name} [{inputType} → {PlayMap}/{actionName}]");

                if (!apply) continue;

                trigger.objectReferenceValue = reference;
                type.enumValueIndex = InProgress;
                touched = true;
            }

            if (!touched) continue;

            Undo.RecordObject(state, "Wire PlayerInputDecision");
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(state);
        }

        if (apply)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Report(apply, states.Count, wired, normalized, skippedFilled, unmapped, missingAction);
    }

    private static void Report(bool apply, int stateCount, List<string> wired, List<string> normalized,
                               List<string> skippedFilled, SortedSet<string> unmapped,
                               SortedSet<string> missingAction)
    {
        string head = apply ? "배선 완료" : "검사 결과 (변경 없음)";
        Debug.Log($"[InputWirer] {head} — State {stateCount}개 검사, " +
                  $"배선 {wired.Count}건 / 숨김 사본 정본화 {normalized.Count}건 / " +
                  $"그대로 둠 {skippedFilled.Count}건\n" +
                  (wired.Count + normalized.Count > 0
                      ? string.Join("\n", wired.Concat(normalized))
                      : "(대상 없음)"));

        if (unmapped.Count > 0)
            Debug.LogWarning($"[InputWirer] 매핑표에 없는 _inputType이라 비워 둔 Decision {unmapped.Count}건 — " +
                             $"{string.Join(", ", unmapped)}\n" +
                             "이 값들은 옛 InputManager.GetInputByEnum에서도 false를 돌려주던 것이라 " +
                             "임의로 채우면 동작이 바뀝니다. 필요하면 직접 지정하세요.");

        if (missingAction.Count > 0)
            Debug.LogError($"[InputWirer] {ActionsAssetPath}에서 찾지 못한 액션 — {string.Join(", ", missingAction)}");
    }

    /// <summary>
    /// .inputactions 안의 InputActionReference를 전부 나열한다.
    ///
    /// 한 액션에 참조가 여러 개 달려 있는 경우가 있다(옛 임포트가 남긴 잔재로 추정). 실제로
    /// Delta_Base는 PLAY/Move를 6923667502783770647로, 배선 도구가 고른 것은 5892614019233535868로
    /// 서로 다른 서브에셋을 가리키고 있었다. 둘 다 같은 액션으로 해석되므로 동작은 같지만, 한쪽이
    /// 잔재라면 재임포트 때 사라져 참조가 null이 된다. 어느 쪽이 정본인지 눈으로 확인하기 위한 메뉴.
    /// </summary>
    [MenuItem("Assets/State Machine/입력 액션 참조 진단")]
    private static void Diagnose()
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(ActionsAssetPath);
        var byAction = new SortedDictionary<string, List<InputActionReference>>();

        foreach (Object o in all)
        {
            if (o is not InputActionReference reference) continue;

            string key = $"{reference.action?.actionMap?.name ?? "?"}/{reference.action?.name ?? "?"}";
            if (!byAction.TryGetValue(key, out List<InputActionReference> list))
                byAction[key] = list = new List<InputActionReference>();
            list.Add(reference);
        }

        var lines = new List<string>();
        foreach (KeyValuePair<string, List<InputActionReference>> pair in byAction)
        {
            lines.Add($"{pair.Key}  ({pair.Value.Count}개)");
            foreach (InputActionReference reference in pair.Value)
                lines.Add($"    name='{reference.name}'  hideFlags={reference.hideFlags}  " +
                          $"instanceID={reference.GetInstanceID()}  actionId={reference.action?.id}");
        }

        int dup = byAction.Count(p => p.Value.Count > 1);
        Debug.Log($"[InputWirer] '{ActionsAssetPath}' 진단 — 액션 {byAction.Count}종, " +
                  $"참조 {byAction.Sum(p => p.Value.Count)}개, 중복된 액션 {dup}종\n" +
                  string.Join("\n", lines));
    }

    /// <summary>
    /// PLAY 맵의 액션 이름 → InputActionReference 서브에셋.
    ///
    /// 액션 하나당 참조가 반드시 2개씩 나온다. actionId는 같고 hideFlags만 다르다.
    ///   NotEditable                    ← 정본. 인스펙터에서 드래그하면 이게 붙는다.
    ///   HideInHierarchy, NotEditable   ← InputSystem 임포터가 하위호환용으로 남기는 숨김 사본.
    /// 둘 다 같은 액션으로 해석되지만 숨김 사본은 옛 식별자를 살려 두려고 있는 것이라,
    /// 새로 배선할 때 쓰면 안 된다(임포터 버전이 바뀌면 사라질 수 있다).
    /// NotEditable은 양쪽 다 붙으므로 HideInHierarchy 유무로만 갈라야 한다.
    /// </summary>
    private static Dictionary<string, InputActionReference> LoadPlayActionReferences()
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(ActionsAssetPath);
        if (all == null || all.Length == 0)
        {
            Debug.LogError($"[InputWirer] '{ActionsAssetPath}'를 열지 못했습니다.");
            return null;
        }

        var candidates = new Dictionary<string, List<InputActionReference>>();
        foreach (Object o in all)
        {
            // 액션 이름은 맵끼리 겹친다(PLAY/Attack과 GRAFFITI/Attack). 반드시 맵까지 확인한다.
            if (o is not InputActionReference reference) continue;
            if (reference.action == null || reference.action.actionMap?.name != PlayMap) continue;

            if (!candidates.TryGetValue(reference.action.name, out List<InputActionReference> list))
                candidates[reference.action.name] = list = new List<InputActionReference>();
            list.Add(reference);
        }

        var map = new Dictionary<string, InputActionReference>();
        var ambiguous = new List<string>();

        foreach (KeyValuePair<string, List<InputActionReference>> pair in candidates)
        {
            List<InputActionReference> real =
                pair.Value.Where(r => !r.hideFlags.HasFlag(HideFlags.HideInHierarchy)).ToList();
            List<InputActionReference> pick = real.Count > 0 ? real : pair.Value;

            map[pair.Key] = pick[0];

            if (pick.Count > 1)
                ambiguous.Add($"{PlayMap}/{pair.Key} (숨김 아닌 참조가 {pick.Count}개)");
        }

        if (ambiguous.Count > 0)
            Debug.LogWarning($"[InputWirer] 숨김이 아닌 참조가 액션 하나에 둘 이상이라 임의로 골랐습니다 — " +
                             $"{string.Join(", ", ambiguous)}\n" +
                             "'입력 액션 참조 진단'으로 확인하세요.");

        if (map.Count != 0) return map;

        Debug.LogError($"[InputWirer] '{ActionsAssetPath}'에서 {PlayMap} 맵의 InputActionReference를 찾지 못했습니다.");
        return null;
    }

    /// <summary>선택한 것에서 StateSO를 모은다 — StateMachineSO / StateSO / 폴더 모두 받는다.</summary>
    private static List<StateSO> CollectStates()
    {
        var states = new List<StateSO>();
        var known = new HashSet<StateSO>();

        void Add(StateSO s)
        {
            if (s != null && known.Add(s)) states.Add(s);
        }

        void AddFromMachine(StateMachineSO machine)
        {
            Add(machine.initialState);
            if (machine.allStates != null)
                foreach (StateSO s in machine.allStates) Add(s);

            // 새 형식(packed) 기계는 State가 서브에셋으로 들어 있다.
            string path = AssetDatabase.GetAssetPath(machine);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is StateSO sub) Add(sub);
        }

        foreach (Object selected in Selection.objects)
        {
            switch (selected)
            {
                case StateMachineSO machine:
                    AddFromMachine(machine);
                    break;

                case StateSO state:
                    Add(state);
                    break;

                default:
                    string path = AssetDatabase.GetAssetPath(selected);
                    if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) break;

                    foreach (string guid in AssetDatabase.FindAssets("t:StateSO", new[] { path }))
                        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                            if (o is StateSO s) Add(s);
                    break;
            }
        }

        return states;
    }
}
