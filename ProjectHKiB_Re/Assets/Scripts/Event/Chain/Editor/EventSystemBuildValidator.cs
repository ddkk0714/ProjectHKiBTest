using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StateMachine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 이벤트 체인과 씬/프리팹 직렬화를 검사해, 에디터에서는 지나가고 Player에서만 멈추는
/// 구성을 이벤트 생성 및 Player 빌드 전에 차단한다.
/// </summary>
public sealed class EventSystemBuildValidator : IPreprocessBuildWithReport
{
    private const string LogPrefix = "[EventSystemValidator]";
    private const string GeneratedOutputFolder = "Assets/Scripts/Event/Test/Generated";
    private const string BattleCompletionScriptGuid = "436dfc51986348bca782a4310ff9bfe8";
    private const string AreaEventTriggerScriptGuid = "8ec6f09a0f2a4d42ad4194a502f375f5";
    private const string InteractionEventTriggerScriptGuid = "924ed9c8213840adb03a2aa8b5bf4609";
    private const string LegacyEventInputTriggerScriptGuid = "408a66b6050dc6144b8bb196f869b1d9";
    private const string LegacyEventConfirmTriggerScriptGuid = "2787a60fa2dc8e04bb37078366f5d8a9";
    private const string LegacyEventHoldTriggerScriptGuid = "16a864a9f2c690ea710661024b45a9d5";
    private const string LegacyEventStayTriggerScriptGuid = "5efdfa0e28e3f1342a5aa5445311a732";

    private static readonly Regex ObjectHeaderRegex = new Regex(
        @"^--- !u!(?<type>\d+) &(?<id>-?\d+)", RegexOptions.Compiled);

    private static readonly Regex GameObjectReferenceRegex = new Regex(
        @"^\s*m_GameObject:\s*\{fileID:\s*(?<id>-?\d+)", RegexOptions.Compiled);

    private static readonly Regex ScriptGuidRegex = new Regex(
        @"^\s*m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*(?<guid>[0-9a-fA-F]{32}),\s*type:\s*3\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex MissingScriptRegex = new Regex(
        @"^\s*m_Script:\s*\{fileID:\s*0(?:,|\s*\})", RegexOptions.Compiled);

    private static readonly Regex RequiredEventGuidRegex = new Regex(
        @"^\s*_requiredEvent:\s*\{fileID:\s*\d+,\s*guid:\s*(?<guid>[0-9a-fA-F]{32}),\s*type:\s*2\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex CompletionBoolNameRegex = new Regex(
        @"^\s*_completionBoolName:\s*(?<key>.*)$", RegexOptions.Compiled);

    private static readonly Regex AreaColliderReferenceRegex = new Regex(
        @"^\s*_areaCollider:\s*\{fileID:\s*(?<id>-?\d+)", RegexOptions.Compiled);

    private static readonly Regex LegacyAreaColliderReferenceRegex = new Regex(
        @"^\s*_collider2D:\s*\{fileID:\s*(?<id>-?\d+)", RegexOptions.Compiled);

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        ValidationReport validation = ValidateProject();
        validation.Log();

        if (validation.HasErrors)
            throw new BuildFailedException(
                $"이벤트 시스템 검증에서 {validation.Errors.Count}개의 오류를 발견해 Player 빌드를 중단합니다.");
    }

    [MenuItem("Tools/Event/Validate Event System")]
    public static void ValidateFromMenu()
    {
        ValidationReport validation = ValidateProject();
        validation.Log();

        if (validation.HasErrors && Application.isBatchMode)
            throw new BuildFailedException(
                $"Event system validation failed with {validation.Errors.Count} error(s).");

        if (!validation.HasErrors)
            Debug.Log($"{LogPrefix} 검증 완료 — 오류가 없습니다. 경고 {validation.Warnings.Count}개.");
    }

    /// <summary>
    /// EventChainEditorWindow가 Generated 에셋을 수정하기 전에 호출하는 빠른 검증이다.
    /// 프로젝트 전체 씬 검사는 Player 빌드 또는 메뉴 검증에서 수행한다.
    /// </summary>
    public static bool ValidateChainBeforeGeneration(EventChainSO chain)
    {
        var report = new ValidationReport();
        ValidateChain(chain, AssetDatabase.GetAssetPath(chain), report);
        report.Log();
        return !report.HasErrors;
    }

    private static ValidationReport ValidateProject()
    {
        var report = new ValidationReport();

        // Scene/Prefab에서 실제 외부 완료 제공자를 먼저 수집해야 체인 계약과 대조할 수 있다.
        var completionProviders = new List<CompletionProviderBinding>();
        ValidateSerializedAssets("t:Scene", report, completionProviders);
        ValidateSerializedAssets("t:Prefab", report, completionProviders);

        foreach (string guid in AssetDatabase.FindAssets("t:EventChainSO"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EventChainSO chain = AssetDatabase.LoadAssetAtPath<EventChainSO>(path);
            ValidateChain(chain, path, report, completionProviders, true);
        }
        return report;
    }

    private static void ValidateChain(
        EventChainSO chain,
        string path,
        ValidationReport report,
        IReadOnlyList<CompletionProviderBinding> completionProviders = null,
        bool validateSceneProviders = false)
    {
        string assetPath = string.IsNullOrEmpty(path) ? "(저장되지 않은 EventChainSO)" : path;
        if (!chain)
        {
            report.Error($"{assetPath}: EventChainSO를 불러올 수 없습니다.");
            return;
        }

        if (chain.events == null || chain.events.Count == 0)
        {
            report.Error($"{assetPath}: 이벤트가 하나도 없습니다.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

        for (int eventIndex = 0; eventIndex < chain.events.Count; eventIndex++)
        {
            EventDefinition definition = chain.events[eventIndex];
            string location = $"{assetPath} / events[{eventIndex}]";
            if (definition == null)
            {
                report.Error($"{location}: EventDefinition이 null입니다.");
                continue;
            }

            string eventId = definition.eventId == null ? string.Empty : definition.eventId.Trim();
            if (eventId.Length == 0)
            {
                report.Error($"{location}: 이벤트 ID가 비어 있습니다.");
            }
            else
            {
                location += $" ({eventId})";
                if (!ids.Add(eventId))
                    report.Error($"{location}: 같은 이벤트 ID가 중복되었습니다.");
                if (eventId.IndexOfAny(invalidFileNameChars) >= 0)
                    report.Error($"{location}: 이벤트 ID에 파일명으로 사용할 수 없는 문자가 있습니다.");
            }

            if (definition.steps == null || definition.steps.Count == 0)
            {
                report.Error($"{location}: 단계가 없어 EventSO를 생성할 수 없습니다.");
                continue;
            }

            ValidateTargetIds(definition, location, report);
            HashSet<string> internallyCompletedBoolKeys = CollectInternallyCompletedBoolKeys(definition);
            string generatedEventGuid = string.Empty;
            if (validateSceneProviders && eventId.Length > 0)
            {
                string generatedPath = $"{GeneratedOutputFolder}/{eventId}.asset";
                generatedEventGuid = AssetDatabase.AssetPathToGUID(generatedPath);
                if (string.IsNullOrEmpty(generatedEventGuid))
                    report.Error($"{location}: 생성 EventSO '{generatedPath}'가 없습니다. 체인을 다시 빌드해야 합니다.");
            }

            for (int stepIndex = 0; stepIndex < definition.steps.Count; stepIndex++)
            {
                EventStepData step = definition.steps[stepIndex];
                string stepLocation = $"{location} / steps[{stepIndex}]";
                if (step == null)
                {
                    report.Error($"{stepLocation}: EventStepData가 null입니다.");
                    continue;
                }

                ValidateStepEntries(step, stepLocation, report);
                ValidateExternalCompletionRequirements(
                    step,
                    stepLocation,
                    internallyCompletedBoolKeys,
                    generatedEventGuid,
                    completionProviders,
                    validateSceneProviders,
                    report);

                bool isLastStep = stepIndex == definition.steps.Count - 1;
                if (!isLastStep && !HasExitPath(step))
                {
                    report.Error(
                        $"{stepLocation}: 진행 조건, 타임아웃, 마지막 Wait After가 모두 없어 여기서 영구 정지합니다.");
                }
            }

            if (definition.triggerKind == EventTriggerKind.Input && !definition.triggerInputAction)
            {
                // 생성기가 PLAY/Confirm을 자동 탐색하므로 즉시 오류로 막지는 않는다. 프로젝트에서
                // 기본 액션을 찾지 못하면 생성기 자체가 별도 오류를 출력한다.
                report.Warning($"{location}: Input 트리거가 기본 PLAY/Confirm InputAction 자동 탐색에 의존합니다.");
            }
        }
    }

    private static void ValidateTargetIds(EventDefinition definition, string location, ValidationReport report)
    {
        if (definition.targets == null) return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < definition.targets.Length; i++)
        {
            EventTargetSearchInfo target = definition.targets[i];
            if (target == null)
            {
                report.Error($"{location} / targets[{i}]: 대상 정보가 null입니다.");
                continue;
            }

            string id = target.ID == null ? string.Empty : target.ID.Trim();
            if (id.Length == 0)
                report.Error($"{location} / targets[{i}]: Target ID가 비어 있습니다.");
            else if (!ids.Add(id))
                report.Error($"{location}: Target ID '{id}'가 중복되었습니다.");
        }
    }

    private static void ValidateStepEntries(EventStepData step, string location, ValidationReport report)
    {
        if (step.enterActions != null)
        {
            for (int i = 0; i < step.enterActions.Length; i++)
            {
                EventStepAction entry = step.enterActions[i];
                if (entry == null)
                    report.Error($"{location} / enterActions[{i}]: 액션 항목이 null입니다.");
                else if (entry.action == null)
                    report.Warning($"{location} / enterActions[{i}]: StateAction이 비어 있어 실행 시 건너뜁니다.");
            }
        }

        if (step.advanceWhenAny == null) return;
        for (int i = 0; i < step.advanceWhenAny.Length; i++)
        {
            if (step.advanceWhenAny[i] == null)
                report.Warning($"{location} / advanceWhenAny[{i}]: StateDecision이 비어 있어 생성 시 제외됩니다.");
        }
    }

    private static HashSet<string> CollectInternallyCompletedBoolKeys(EventDefinition definition)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (definition.steps == null) return keys;

        foreach (EventStepData step in definition.steps)
        {
            if (step?.enterActions == null) continue;
            foreach (EventStepAction entry in step.enterActions)
            {
                if (entry?.action is not SetCustomBoolAction action || !action.value ||
                    string.IsNullOrWhiteSpace(action.boolName)) continue;
                keys.Add(action.boolName.Trim());
            }
        }

        return keys;
    }

    private static void ValidateExternalCompletionRequirements(
        EventStepData step,
        string location,
        HashSet<string> internallyCompletedBoolKeys,
        string generatedEventGuid,
        IReadOnlyList<CompletionProviderBinding> completionProviders,
        bool validateSceneProviders,
        ValidationReport report)
    {
        var awaitedBoolKeys = new HashSet<string>(StringComparer.Ordinal);
        if (step.advanceWhenAny != null)
        {
            foreach (StateDecision decision in step.advanceWhenAny)
            {
                if (decision is CustomBoolDecision boolDecision &&
                    !string.IsNullOrWhiteSpace(boolDecision.boolName))
                    awaitedBoolKeys.Add(boolDecision.boolName.Trim());
            }
        }

        var contractKeys = new HashSet<string>(StringComparer.Ordinal);
        ExternalCompletionRequirement[] requirements = step.externalCompletionRequirements ??
                                                       Array.Empty<ExternalCompletionRequirement>();
        for (int i = 0; i < requirements.Length; i++)
        {
            ExternalCompletionRequirement requirement = requirements[i];
            string requirementLocation = $"{location} / externalCompletionRequirements[{i}]";
            if (requirement == null)
            {
                report.Error($"{requirementLocation}: 외부 완료 계약이 null입니다.");
                continue;
            }

            string key = requirement.key == null ? string.Empty : requirement.key.Trim();
            if (key.Length == 0)
            {
                report.Error($"{requirementLocation}: 완료 bool 키가 비어 있습니다.");
                continue;
            }

            if (!contractKeys.Add(key))
                report.Error($"{requirementLocation}: 외부 완료 계약 키 '{key}'가 중복되었습니다.");
            if (!awaitedBoolKeys.Contains(key))
                report.Error($"{requirementLocation}: '{key}'를 기다리는 CustomBoolDecision이 이 단계에 없습니다.");
            if (step.timeoutSeconds <= 0f && !requirement.allowInfiniteWait)
                report.Error($"{requirementLocation}: 타임아웃 없는 외부 대기는 allowInfiniteWait 승인이 필요합니다.");

            if (!validateSceneProviders || string.IsNullOrEmpty(generatedEventGuid)) continue;

            CompletionProviderBinding provider = completionProviders?.FirstOrDefault(candidate =>
                string.Equals(candidate.EventGuid, generatedEventGuid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Key, key, StringComparison.Ordinal) &&
                candidate.Provider == requirement.provider);

            if (provider != null) continue;

            string message = $"{requirementLocation}: EventSO GUID {generatedEventGuid}의 '{key}'를 제공하는 " +
                             $"{requirement.provider} 컴포넌트가 Scene/Prefab에 없습니다.";
            if (requirement.isRequired) report.Error(message);
            else report.Warning(message);
        }

        foreach (string awaitedKey in awaitedBoolKeys)
        {
            if (internallyCompletedBoolKeys.Contains(awaitedKey) || contractKeys.Contains(awaitedKey)) continue;
            report.Error($"{location}: Custom Bool '{awaitedKey}'는 체인 내부에서 true로 설정되지 않으며 " +
                         "외부 완료 계약도 없습니다.");
        }
    }

    private static bool HasExitPath(EventStepData step)
    {
        bool hasDecision = step.advanceWhenAny != null && step.advanceWhenAny.Any(decision => decision != null);
        if (hasDecision || step.timeoutSeconds > 0f) return true;

        if (step.enterActions == null || step.enterActions.Length == 0) return false;
        EventStepAction lastAction = step.enterActions[step.enterActions.Length - 1];
        return lastAction != null && lastAction.waitAfter > 0f;
    }

    private static void ValidateSerializedAssets(
        string filter,
        ValidationReport report,
        List<CompletionProviderBinding> completionProviders)
    {
        foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            ValidateSerializedAsset(path, report, completionProviders);
        }
    }

    /// <summary>
    /// 씬을 열고 다시 저장하지 않고 YAML을 읽는다. Missing Script가 있어도 씬을 변경하지 않으며,
    /// GUID가 사라진 컴포넌트까지 검출할 수 있다.
    /// </summary>
    private static void ValidateSerializedAsset(
        string path,
        ValidationReport report,
        List<CompletionProviderBinding> completionProviders)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception exception)
        {
            report.Warning($"{path}: 직렬화 파일을 읽지 못했습니다 ({exception.Message}).");
            return;
        }

        var gameObjectNames = CollectGameObjectNames(lines);
        HashSet<long> colliderGameObjects = CollectCollider2DGameObjects(lines);
        for (int start = 0; start < lines.Length; start++)
        {
            Match header = ObjectHeaderRegex.Match(lines[start]);
            if (!header.Success || header.Groups["type"].Value != "114") continue;

            int end = FindSectionEnd(lines, start + 1);
            long gameObjectId = 0;
            int scriptLine = -1;
            string scriptGuid = null;
            bool explicitMissingScript = false;
            string requiredEventGuid = null;
            string completionBoolName = null;
            long areaColliderId = 0;
            long legacyAreaColliderId = 0;
            bool hasEventComponentSignature = false;

            for (int lineIndex = start + 1; lineIndex < end; lineIndex++)
            {
                Match gameObjectMatch = GameObjectReferenceRegex.Match(lines[lineIndex]);
                if (gameObjectMatch.Success)
                    long.TryParse(gameObjectMatch.Groups["id"].Value, out gameObjectId);

                Match scriptMatch = ScriptGuidRegex.Match(lines[lineIndex]);
                if (scriptMatch.Success)
                {
                    scriptLine = lineIndex + 1;
                    scriptGuid = scriptMatch.Groups["guid"].Value;
                }
                else if (MissingScriptRegex.IsMatch(lines[lineIndex]))
                {
                    scriptLine = lineIndex + 1;
                    explicitMissingScript = true;
                }

                Match requiredEventMatch = RequiredEventGuidRegex.Match(lines[lineIndex]);
                if (requiredEventMatch.Success)
                {
                    requiredEventGuid = requiredEventMatch.Groups["guid"].Value;
                    hasEventComponentSignature = true;
                }

                Match completionBoolMatch = CompletionBoolNameRegex.Match(lines[lineIndex]);
                if (completionBoolMatch.Success)
                {
                    completionBoolName = UnquoteYamlScalar(completionBoolMatch.Groups["key"].Value);
                    hasEventComponentSignature = true;
                }

                if (lines[lineIndex].StartsWith("  _gameEvent:", StringComparison.Ordinal))
                    hasEventComponentSignature = true;

                Match areaColliderMatch = AreaColliderReferenceRegex.Match(lines[lineIndex]);
                if (areaColliderMatch.Success)
                    long.TryParse(areaColliderMatch.Groups["id"].Value, out areaColliderId);

                Match legacyAreaColliderMatch = LegacyAreaColliderReferenceRegex.Match(lines[lineIndex]);
                if (legacyAreaColliderMatch.Success)
                    long.TryParse(legacyAreaColliderMatch.Groups["id"].Value, out legacyAreaColliderId);
            }

            if (!explicitMissingScript && string.IsNullOrEmpty(scriptGuid)) continue;

            bool unresolvedGuid = !string.IsNullOrEmpty(scriptGuid) &&
                                  string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(scriptGuid));
            string objectName = gameObjectNames.TryGetValue(gameObjectId, out string foundName)
                ? foundName
                : $"GameObject fileID {gameObjectId}";

            if (explicitMissingScript || unresolvedGuid)
            {
                // 이 Validator는 이벤트 시스템 전용 빌드 차단기다. 프로젝트 전체의 오래된
                // UI/테스트 Missing Script까지 차단하지 않고 이벤트 형태의 컴포넌트만 검사한다.
                if (hasEventComponentSignature ||
                    string.Equals(scriptGuid, BattleCompletionScriptGuid, StringComparison.OrdinalIgnoreCase))
                {
                    string guidText = explicitMissingScript ? "(직접 누락: fileID 0)" : scriptGuid;
                    report.Error($"{path}:{scriptLine} / {objectName}: Missing event Script GUID {guidText}.");
                }
            }
            else if (string.Equals(scriptGuid, BattleCompletionScriptGuid, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(requiredEventGuid))
                    report.Error($"{path}:{scriptLine} / {objectName}: BattleCompletionOnAttack의 Required Event가 비어 있습니다.");
                if (string.IsNullOrWhiteSpace(completionBoolName))
                    report.Error($"{path}:{scriptLine} / {objectName}: BattleCompletionOnAttack의 Completion Bool Name이 비어 있습니다.");

                if (!string.IsNullOrWhiteSpace(requiredEventGuid) && !string.IsNullOrWhiteSpace(completionBoolName))
                {
                    completionProviders.Add(new CompletionProviderBinding(
                        requiredEventGuid,
                        completionBoolName,
                        ExternalCompletionProviderKind.AttackTrigger,
                        path,
                        objectName));
                }
            }
            else if (IsNewSpatialTriggerScript(scriptGuid) && areaColliderId == 0)
            {
                report.Error(
                    $"{path}:{scriptLine} / {objectName}: Spatial event trigger has no ZCollider2D reference.");
            }
            else if (IsLegacySpatialTriggerScript(scriptGuid) &&
                     areaColliderId == 0 && legacyAreaColliderId == 0 &&
                     !colliderGameObjects.Contains(gameObjectId))
            {
                report.Error(
                    $"{path}:{scriptLine} / {objectName}: Legacy spatial event trigger has no collider reference.");
            }

            start = end - 1;
        }
    }

    private static bool IsNewSpatialTriggerScript(string scriptGuid)
    {
        return string.Equals(scriptGuid, AreaEventTriggerScriptGuid, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scriptGuid, InteractionEventTriggerScriptGuid, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacySpatialTriggerScript(string scriptGuid)
    {
        return string.Equals(scriptGuid, LegacyEventInputTriggerScriptGuid, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scriptGuid, LegacyEventConfirmTriggerScriptGuid, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scriptGuid, LegacyEventHoldTriggerScriptGuid, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scriptGuid, LegacyEventStayTriggerScriptGuid, StringComparison.OrdinalIgnoreCase);
    }

    private static string UnquoteYamlScalar(string value)
    {
        string trimmed = value == null ? string.Empty : value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            return trimmed.Substring(1, trimmed.Length - 2);
        return trimmed;
    }

    private static Dictionary<long, string> CollectGameObjectNames(string[] lines)
    {
        var names = new Dictionary<long, string>();
        for (int start = 0; start < lines.Length; start++)
        {
            Match header = ObjectHeaderRegex.Match(lines[start]);
            if (!header.Success || header.Groups["type"].Value != "1") continue;
            if (!long.TryParse(header.Groups["id"].Value, out long objectId)) continue;

            int end = FindSectionEnd(lines, start + 1);
            for (int lineIndex = start + 1; lineIndex < end; lineIndex++)
            {
                const string namePrefix = "  m_Name: ";
                if (!lines[lineIndex].StartsWith(namePrefix, StringComparison.Ordinal)) continue;
                names[objectId] = lines[lineIndex].Substring(namePrefix.Length);
                break;
            }

            start = end - 1;
        }

        return names;
    }

    private static HashSet<long> CollectCollider2DGameObjects(string[] lines)
    {
        // Unity YAML class IDs: Circle, Polygon, Box, Composite, Edge, Capsule, Tilemap Collider2D.
        var colliderTypeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "58", "60", "61", "66", "68", "70", "19719996"
        };
        var gameObjectIds = new HashSet<long>();

        for (int start = 0; start < lines.Length; start++)
        {
            Match header = ObjectHeaderRegex.Match(lines[start]);
            if (!header.Success || !colliderTypeIds.Contains(header.Groups["type"].Value)) continue;

            int end = FindSectionEnd(lines, start + 1);
            for (int lineIndex = start + 1; lineIndex < end; lineIndex++)
            {
                Match gameObjectMatch = GameObjectReferenceRegex.Match(lines[lineIndex]);
                if (!gameObjectMatch.Success) continue;
                if (long.TryParse(gameObjectMatch.Groups["id"].Value, out long gameObjectId))
                    gameObjectIds.Add(gameObjectId);
                break;
            }

            start = end - 1;
        }

        return gameObjectIds;
    }

    private static int FindSectionEnd(string[] lines, int start)
    {
        for (int i = start; i < lines.Length; i++)
        {
            if (ObjectHeaderRegex.IsMatch(lines[i])) return i;
        }

        return lines.Length;
    }

    private sealed class CompletionProviderBinding
    {
        public string EventGuid { get; }
        public string Key { get; }
        public ExternalCompletionProviderKind Provider { get; }
        public string AssetPath { get; }
        public string ObjectName { get; }

        public CompletionProviderBinding(
            string eventGuid,
            string key,
            ExternalCompletionProviderKind provider,
            string assetPath,
            string objectName)
        {
            EventGuid = eventGuid;
            Key = key;
            Provider = provider;
            AssetPath = assetPath;
            ObjectName = objectName;
        }
    }

    private sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool HasErrors => Errors.Count > 0;

        public void Error(string message) => Errors.Add(message);
        public void Warning(string message) => Warnings.Add(message);

        public void Log()
        {
            foreach (string error in Errors)
                Debug.LogError($"{LogPrefix} {error}");
            foreach (string warning in Warnings)
                Debug.LogWarning($"{LogPrefix} {warning}");
        }
    }
}
