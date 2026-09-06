using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using StateMachine;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 이벤트 연출 액션(화면 노이즈·흔들림·섬광·넉백)에 효과음 SO를 배선합니다.
///
/// 이벤트 체인 원본과 **생성된 StateSO 양쪽**에 모두 씁니다. SerializeReference는 에셋마다
/// 값을 따로 직렬화하므로 체인만 고치면 이미 구워진 Generated 에셋에는 반영되지 않고,
/// 반대로 Generated만 고치면 다음 빌드 때 날아간다. 둘 다 써야 재빌드 전후로 살아남는다.
/// </summary>
public static class EventEffectAudioSetup
{
    private const string ImpactSourceFolder = "Assets/Audio/EventEffects/Impact";
    private const string ImpactOutputFolder = "Assets/ScriptableObjects/AudioDatas/EventEffects/Impact";
    private const string NoiseOutputFolder = "Assets/ScriptableObjects/AudioDatas/EventEffects/ScreenNoise";
    private const string GeneratedFolder = "Assets/Scripts/Event/Test/Generated";
    private const string SfxTypePath = "Assets/ScriptableObjects/Enums/AudioTypes/SFX.asset";

    // EVT-001 "가위질"의 노이즈는 1.5초 뒤 페이드 없이 곧장 꺼진다(ScreenEffectManager.NoiseRoutine).
    // 그 순간에 쿵이 떨어지도록 흔들림 효과음만 지연시킨다. 같은 단계의 액션은 전부 waitAfter가 0이라
    // 이벤트 체인을 고치지 않고 타이밍을 주려면 이 방법뿐이다.
    private const float ScissorShakeDelay = 1.5f;

    private readonly struct CueAsset
    {
        public readonly string ClipPrefix;
        public readonly string AssetName;
        public readonly string SourceFolder;
        public readonly string OutputFolder;

        public CueAsset(string clipPrefix, string assetName, string sourceFolder, string outputFolder)
        {
            ClipPrefix = clipPrefix;
            AssetName = assetName;
            SourceFolder = sourceFolder;
            OutputFolder = outputFolder;
        }
    }

    private static readonly CueAsset[] ImpactCues =
    {
        new("SFX_EVT_Shake_Boom_", "SFX_EVT_Shake_BoomAudio", ImpactSourceFolder, ImpactOutputFolder),
        new("SFX_EVT_Flash_Sting_", "SFX_EVT_Flash_StingAudio", ImpactSourceFolder, ImpactOutputFolder),
        new("SFX_EVT_Knockback_Heavy_", "SFX_EVT_Knockback_HeavyAudio", ImpactSourceFolder, ImpactOutputFolder),
    };

    /// <summary>
    /// SO 빌드 → 배선 → 검증을 한 번에. 배치모드에서 -executeMethod 로 부르기 위한 진입점이다.
    /// 배선은 SO가 이미 있어야 하므로 순서를 바꾸면 안 된다.
    /// </summary>
    [MenuItem("Tools/Audio/Event Effects/Run Full Setup")]
    public static void RunFullSetup()
    {
        ScreenNoiseAudioSetup.BuildAudioDataAssets();
        BuildImpactAudioAssets();
        BindAll();
        ValidateAll();
    }

    // ── SO 빌드 ──────────────────────────────────────────────────────

    [MenuItem("Tools/Audio/Event Effects/Build Impact AudioDataSO")]
    public static void BuildImpactAudioAssets()
    {
        AudioTypeSO sfxType = AssetDatabase.LoadAssetAtPath<AudioTypeSO>(SfxTypePath);
        if (sfxType == null)
        {
            Debug.LogError($"[EventEffectAudioSetup] SFX AudioTypeSO를 찾을 수 없습니다: {SfxTypePath}");
            return;
        }

        EnsureFolder(ImpactSourceFolder);
        EnsureFolder(ImpactOutputFolder);

        int built = 0;
        foreach (CueAsset cue in ImpactCues)
        {
            AudioClip[] clips = FindClips(cue.SourceFolder, cue.ClipPrefix);
            if (clips.Length == 0)
            {
                Debug.LogWarning($"[EventEffectAudioSetup] '{cue.ClipPrefix}*.wav' 음원을 찾지 못해 건너뜁니다.");
                continue;
            }

            string assetPath = $"{cue.OutputFolder}/{cue.AssetName}.asset";
            AudioDataSO audioData = AssetDatabase.LoadAssetAtPath<AudioDataSO>(assetPath);
            if (audioData == null)
            {
                audioData = ScriptableObject.CreateInstance<AudioDataSO>();
                AssetDatabase.CreateAsset(audioData, assetPath);
            }
            else
            {
                Undo.RecordObject(audioData, "Update Impact Audio Data");
            }

            audioData.type = sfxType;
            audioData.audioClips = clips;
            EditorUtility.SetDirty(audioData);
            built++;
            Debug.Log($"[EventEffectAudioSetup] {cue.AssetName}: {clips.Length}개 클립 연결", audioData);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[EventEffectAudioSetup] 임팩트 AudioDataSO {built}/{ImpactCues.Length}개 생성 또는 갱신 완료.");
    }

    // ── 배선 ────────────────────────────────────────────────────────

    /// <summary>액션 하나가 어떤 큐를 받아야 하는지 판단한다. null이면 배선하지 않는다.</summary>
    private static bool TryResolveCue(StateAction action, string eventId, out AudioDataSO cue, out float delay)
    {
        cue = null;
        delay = 0f;
        string id = eventId ?? string.Empty;
        bool evt001 = id.IndexOf("EVT001", StringComparison.OrdinalIgnoreCase) >= 0;
        bool evt002 = id.IndexOf("EVT002", StringComparison.OrdinalIgnoreCase) >= 0;
        bool death = id.IndexOf("DEATH", StringComparison.OrdinalIgnoreCase) >= 0;

        switch (action)
        {
            case StateMachine.ScreenNoiseAction noise when !noise.stop:
                if (death && Mathf.Approximately(noise.duration, 0.6f)) cue = LoadNoise("SFX_EVT_ScreenNoise_DeathAudio");
                else if (evt002 && Mathf.Approximately(noise.duration, 2.4f)) cue = LoadNoise("SFX_EVT_ScreenNoise_EyeAudio");
                else if (evt001 && Mathf.Approximately(noise.duration, 1.5f)) cue = LoadNoise("SFX_EVT_ScreenNoise_CutAudio");
                return cue != null;

            // EVT-001은 노이즈가 끝나는 순간의 흔들림에 쿵을 붙인다.
            case StateMachine.CameraShakeAction when evt001:
                cue = LoadImpact("SFX_EVT_Shake_BoomAudio");
                delay = ScissorShakeDelay;
                return cue != null;

            // 섬광은 EVT-002 보스화(0.3초)에만 붙인다.
            // EVT-004/006/DEATH의 섬광은 다른 연출이라 무음으로 둔다.
            case StateMachine.ScreenFlashAction flash when evt002 && Mathf.Approximately(flash.duration, 0.3f):
                cue = LoadImpact("SFX_EVT_Flash_StingAudio");
                return cue != null;

            // EVT-002는 앞선 카메라 흔들림이 아니라 실제 플레이어 넉백 순간에 합성음을 낸다.
            case StateMachine.KnockBackAction when evt002:
                cue = LoadImpact("SFX_EVT_Knockback_HeavyAudio");
                return cue != null;

            default:
                return false;
        }
    }

    [MenuItem("Tools/Audio/Event Effects/Bind Event Effect Audio (All)")]
    public static void BindAll()
    {
        int chainCount = BindChains();
        int generatedCount = BindGeneratedStates();
        AssetDatabase.SaveAssets();
        Debug.Log($"[EventEffectAudioSetup] 배선 완료 — 이벤트 체인 {chainCount}건, 생성 StateSO {generatedCount}건. " +
                  "체인을 다시 빌드해도 값이 유지됩니다.");
    }

    private static int BindChains()
    {
        int bound = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:EventChainSO"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EventChainSO chain = AssetDatabase.LoadAssetAtPath<EventChainSO>(path);
            if (chain == null || chain.events == null) continue;

            bool changed = false;
            foreach (EventDefinition definition in chain.events)
            {
                if (definition?.steps == null) continue;
                foreach (EventStepData step in definition.steps)
                {
                    if (step?.enterActions == null) continue;
                    foreach (EventStepAction entry in step.enterActions)
                        ApplyRecursive(entry?.action, definition.eventId, ref changed, ref bound);
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(chain);
                Debug.Log($"[EventEffectAudioSetup] 체인 배선: {path}", chain);
            }
        }
        return bound;
    }

    private static int BindGeneratedStates()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedFolder)) return 0;

        int bound = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:StateSO", new[] { GeneratedFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            StateSO state = AssetDatabase.LoadAssetAtPath<StateSO>(path);
            if (state?.EnterActions == null) continue;

            // 생성 규칙이 "{eventId}_S{i}" / "{eventId}_S{i}w{k}" 이므로 이름에서 이벤트를 되찾는다.
            string assetName = Path.GetFileNameWithoutExtension(path);
            int cut = assetName.LastIndexOf("_S", StringComparison.Ordinal);
            string eventId = cut > 0 ? assetName.Substring(0, cut) : assetName;

            bool changed = false;
            foreach (StateAction action in state.EnterActions)
                ApplyRecursive(action, eventId, ref changed, ref bound);

            if (changed)
            {
                EditorUtility.SetDirty(state);
                Debug.Log($"[EventEffectAudioSetup] 생성 State 배선: {path}", state);
            }
        }
        return bound;
    }

    /// <summary>
    /// 중첩 액션까지 따라 내려간다. 넉백은 TargetEntityManipulateAction 안에 들어 있어
    /// 최상위만 훑으면 영원히 못 찾는다.
    /// </summary>
    private static void ApplyRecursive(StateAction action, string eventId, ref bool changed, ref int bound)
    {
        if (action == null) return;

        if (TryResolveCue(action, eventId, out AudioDataSO cue, out float delay) && ApplyCue(action, cue, delay))
        {
            changed = true;
            bound++;
        }

        foreach (FieldInfo field in action.GetType()
                     .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (typeof(StateAction).IsAssignableFrom(field.FieldType))
            {
                ApplyRecursive(field.GetValue(action) as StateAction, eventId, ref changed, ref bound);
            }
            else if (typeof(IEnumerable<StateAction>).IsAssignableFrom(field.FieldType))
            {
                if (field.GetValue(action) is IEnumerable<StateAction> children)
                    foreach (StateAction child in children)
                        ApplyRecursive(child, eventId, ref changed, ref bound);
            }
        }
    }

    /// <summary>액션의 audioCue 필드를 찾아 값을 넣는다. 이미 같은 값이면 건드리지 않는다.</summary>
    private static bool ApplyCue(StateAction action, AudioDataSO cue, float delay)
    {
        FieldInfo field = action.GetType().GetField("audioCue",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(EffectAudioCue)) return false;

        if (field.GetValue(action) is not EffectAudioCue existing)
        {
            existing = new EffectAudioCue();
            field.SetValue(action, existing);
        }
        else if (existing.AudioData == cue && Mathf.Approximately(existing.Delay, delay))
        {
            return false;
        }

        existing.Configure(cue, 1f, delay);
        return true;
    }

    // ── 검증 ────────────────────────────────────────────────────────

    [MenuItem("Tools/Audio/Event Effects/Validate Event Effect Audio")]
    public static void ValidateAll()
    {
        bool ok = true;
        foreach (CueAsset cue in ImpactCues)
            ok &= ValidateOne($"{cue.OutputFolder}/{cue.AssetName}.asset");

        foreach (string name in new[]
                 {
                     "SFX_EVT_ScreenNoise_CutAudio", "SFX_EVT_ScreenNoise_EyeAudio", "SFX_EVT_ScreenNoise_DeathAudio",
                 })
            ok &= ValidateOne($"{NoiseOutputFolder}/{name}.asset");

        Debug.Log(ok
            ? "[EventEffectAudioSetup] 이벤트 연출 효과음 SO 검증 통과."
            : "[EventEffectAudioSetup] 검증 실패 — 위 오류를 확인하세요.");
    }

    private static bool ValidateOne(string assetPath)
    {
        AudioDataSO data = AssetDatabase.LoadAssetAtPath<AudioDataSO>(assetPath);
        if (data == null)
        {
            Debug.LogError($"[EventEffectAudioSetup] AudioDataSO 누락: {assetPath}");
            return false;
        }

        bool ok = true;
        if (data.type == null || !data.type.playOneShot)
        {
            Debug.LogError($"[EventEffectAudioSetup] 원샷 SFX 타입이 아닙니다: {assetPath}", data);
            ok = false;
        }

        if (data.audioClips == null || data.audioClips.Length == 0 || data.audioClips.Any(c => c == null))
        {
            Debug.LogError($"[EventEffectAudioSetup] 유효한 AudioClip이 없습니다: {assetPath}", data);
            ok = false;
        }

        return ok;
    }

    // ── 공통 ────────────────────────────────────────────────────────

    private static AudioDataSO LoadImpact(string assetName)
        => AssetDatabase.LoadAssetAtPath<AudioDataSO>($"{ImpactOutputFolder}/{assetName}.asset");

    private static AudioDataSO LoadNoise(string assetName)
        => AssetDatabase.LoadAssetAtPath<AudioDataSO>($"{NoiseOutputFolder}/{assetName}.asset");

    private static AudioClip[] FindClips(string folder, string prefix)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return Array.Empty<AudioClip>();

        var clips = new List<AudioClip>();
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) clips.Add(clip);
        }

        return clips.OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
