using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 정해진 이름으로 임포트한 화면 노이즈 WAV를 현재 프로젝트의 AudioDataSO 구조로 묶습니다.
/// 이벤트 시스템과 독립적으로 실행할 수 있어 이벤트 체인 수정 중에도 안전하게 준비할 수 있습니다.
/// </summary>
public static class ScreenNoiseAudioSetup
{
    private const string SourceFolder = "Assets/Audio/EventEffects/ScreenNoise";
    private const string OutputFolder = "Assets/ScriptableObjects/AudioDatas/EventEffects/ScreenNoise";
    private const string SfxTypePath = "Assets/ScriptableObjects/Enums/AudioTypes/SFX.asset";

    private readonly struct CueDefinition
    {
        public readonly string ClipPrefix;
        public readonly string AssetName;

        public CueDefinition(string clipPrefix, string assetName)
        {
            ClipPrefix = clipPrefix;
            AssetName = assetName;
        }
    }

    private static readonly CueDefinition[] CueDefinitions =
    {
        new("SFX_EVT_ScreenNoise_Cut_1500_", "SFX_EVT_ScreenNoise_CutAudio"),
        new("SFX_EVT_ScreenNoise_Eye_2400_", "SFX_EVT_ScreenNoise_EyeAudio"),
        new("SFX_EVT_ScreenNoise_Death_0600_", "SFX_EVT_ScreenNoise_DeathAudio"),
    };

    [MenuItem("Tools/Audio/Event Effects/Build Screen Noise AudioDataSO")]
    public static void BuildAudioDataAssets()
    {
        AudioTypeSO sfxType = AssetDatabase.LoadAssetAtPath<AudioTypeSO>(SfxTypePath);
        if (sfxType == null)
        {
            Debug.LogError($"[ScreenNoiseAudioSetup] SFX AudioTypeSO를 찾을 수 없습니다: {SfxTypePath}");
            return;
        }

        EnsureFolder(SourceFolder);
        EnsureFolder(OutputFolder);

        int builtCount = 0;
        foreach (CueDefinition cue in CueDefinitions)
        {
            AudioClip[] clips = FindClips(cue.ClipPrefix);
            if (clips.Length == 0)
            {
                Debug.LogWarning($"[ScreenNoiseAudioSetup] '{cue.ClipPrefix}*.wav' 음원을 찾지 못해 건너뜁니다.");
                continue;
            }

            string assetPath = $"{OutputFolder}/{cue.AssetName}.asset";
            AudioDataSO audioData = AssetDatabase.LoadAssetAtPath<AudioDataSO>(assetPath);
            if (audioData == null)
            {
                audioData = ScriptableObject.CreateInstance<AudioDataSO>();
                AssetDatabase.CreateAsset(audioData, assetPath);
            }
            else
            {
                Undo.RecordObject(audioData, "Update Screen Noise Audio Data");
            }

            audioData.type = sfxType;
            audioData.audioClips = clips;
            EditorUtility.SetDirty(audioData);
            builtCount++;

            Debug.Log($"[ScreenNoiseAudioSetup] {cue.AssetName}: {clips.Length}개 클립 연결", audioData);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ScreenNoiseAudioSetup] 화면 노이즈 AudioDataSO {builtCount}/{CueDefinitions.Length}개 생성 또는 갱신 완료.");
    }

    [MenuItem("Tools/Audio/Event Effects/Bind Screen Noise To Event Chains")]
    public static void BindAudioToEventChains()
    {
        AudioDataSO cut = LoadCue("SFX_EVT_ScreenNoise_CutAudio");
        AudioDataSO eye = LoadCue("SFX_EVT_ScreenNoise_EyeAudio");
        AudioDataSO death = LoadCue("SFX_EVT_ScreenNoise_DeathAudio");
        if (cut == null || eye == null || death == null)
        {
            Debug.LogError("[ScreenNoiseAudioSetup] 화면 노이즈 AudioDataSO 3종을 먼저 빌드해야 합니다.");
            return;
        }

        int boundCount = 0;
        string[] chainGuids = AssetDatabase.FindAssets("t:EventChainSO");
        foreach (string guid in chainGuids)
        {
            string chainPath = AssetDatabase.GUIDToAssetPath(guid);
            EventChainSO chain = AssetDatabase.LoadAssetAtPath<EventChainSO>(chainPath);
            if (chain == null || chain.events == null) continue;

            bool changed = false;
            foreach (EventDefinition definition in chain.events)
            {
                if (definition == null || definition.steps == null) continue;

                foreach (EventStepData step in definition.steps)
                {
                    if (step?.enterActions == null) continue;

                    foreach (EventStepAction entry in step.enterActions)
                    {
                        if (entry?.action is not StateMachine.ScreenNoiseAction noise || noise.stop) continue;

                        AudioDataSO cue = ResolveCue(definition.eventId, noise, cut, eye, death);
                        if (cue == null) continue;

                        if (noise.audioCue != null && noise.audioCue.AudioData == cue) continue;

                        if (!changed) Undo.RecordObject(chain, "Bind Screen Noise Audio");
                        noise.audioCue ??= new EffectAudioCue();
                        noise.audioCue.Configure(cue);
                        changed = true;
                        boundCount++;
                    }
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(chain);
                Debug.Log($"[ScreenNoiseAudioSetup] 화면 노이즈 큐를 배선했습니다: {chainPath}", chain);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ScreenNoiseAudioSetup] 이벤트 체인 화면 노이즈 배선 {boundCount}건 완료. 체인 편집기에서 다시 빌드하세요.");
    }

    [MenuItem("Tools/Audio/Event Effects/Validate Screen Noise Audio", true)]
    private static bool CanValidateAudio()
    {
        return AssetDatabase.IsValidFolder(SourceFolder) || AssetDatabase.IsValidFolder(OutputFolder);
    }

    [MenuItem("Tools/Audio/Event Effects/Validate Screen Noise Audio")]
    public static void ValidateAudio()
    {
        bool isValid = true;
        foreach (CueDefinition cue in CueDefinitions)
        {
            string assetPath = $"{OutputFolder}/{cue.AssetName}.asset";
            AudioDataSO audioData = AssetDatabase.LoadAssetAtPath<AudioDataSO>(assetPath);
            if (audioData == null)
            {
                Debug.LogError($"[ScreenNoiseAudioSetup] AudioDataSO 누락: {assetPath}");
                isValid = false;
                continue;
            }

            if (audioData.type == null || !audioData.type.playOneShot)
            {
                Debug.LogError($"[ScreenNoiseAudioSetup] 원샷 SFX 타입이 아닙니다: {assetPath}", audioData);
                isValid = false;
            }

            if (audioData.audioClips == null || audioData.audioClips.Length == 0 ||
                audioData.audioClips.Any(clip => clip == null))
            {
                Debug.LogError($"[ScreenNoiseAudioSetup] 유효한 AudioClip이 없습니다: {assetPath}", audioData);
                isValid = false;
            }
        }

        if (isValid)
            Debug.Log("[ScreenNoiseAudioSetup] 화면 노이즈 AudioDataSO 검증 통과.");
    }

    private static AudioClip[] FindClips(string prefix)
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SourceFolder });
        var clips = new List<AudioClip>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) clips.Add(clip);
        }

        return clips.OrderBy(clip => clip.name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static AudioDataSO LoadCue(string assetName)
    {
        return AssetDatabase.LoadAssetAtPath<AudioDataSO>($"{OutputFolder}/{assetName}.asset");
    }

    private static AudioDataSO ResolveCue(
        string eventId,
        StateMachine.ScreenNoiseAction noise,
        AudioDataSO cut,
        AudioDataSO eye,
        AudioDataSO death)
    {
        string normalizedId = eventId ?? string.Empty;
        if (normalizedId.IndexOf("DEATH", StringComparison.OrdinalIgnoreCase) >= 0 &&
            Mathf.Approximately(noise.duration, 0.6f))
            return death;

        if (normalizedId.IndexOf("EVT002", StringComparison.OrdinalIgnoreCase) >= 0 &&
            Mathf.Approximately(noise.duration, 2.4f))
            return eye;

        if (normalizedId.IndexOf("EVT001", StringComparison.OrdinalIgnoreCase) >= 0 &&
            Mathf.Approximately(noise.duration, 1.5f))
            return cut;

        return null;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
