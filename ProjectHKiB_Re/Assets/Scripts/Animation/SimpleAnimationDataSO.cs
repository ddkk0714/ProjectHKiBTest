using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;
using UnityEngine.U2D.Animation;

[CreateAssetMenu(fileName = "NewAnimationData", menuName = "Animation/Simple Animation Data")]
public class SimpleAnimationDataSO : ScriptableObject
{
    [Header("Reference for Auto-Generation")]
    public SpriteLibraryAsset sourceLibraryAsset;

    [Header("Animation Clips")]
    public string defaultClipName;
    public List<SimpleAnimationClip> clips = new();

    [NaughtyAttributes.Button]
    public void GenerateClipsFromLibrary()
    {
        if (sourceLibraryAsset == null)
        {
            Debug.LogError("Source Library Asset is missing!");
            return;
        }

        clips.Clear();
        var categories = sourceLibraryAsset.GetCategoryNames();

        foreach (var category in categories)
        {
            SimpleAnimationClip newClip = new() { clipName = category};

            EnumManager.AnimDir dir = EnumManager.AnimDir.D;

            if      (category.EndsWith("U")) {dir = EnumManager.AnimDir.U; newClip.clipName = newClip.clipName[..^1];}
            else if (category.EndsWith("D")) {dir = EnumManager.AnimDir.D; newClip.clipName = newClip.clipName[..^1];}
            else if (category.EndsWith("L")) {dir = EnumManager.AnimDir.L; newClip.clipName = newClip.clipName[..^1];}
            else if (category.EndsWith("R")) {dir = EnumManager.AnimDir.R; newClip.clipName = newClip.clipName[..^1];}
            newClip.categoryKeys[dir] = category;

            var labels = sourceLibraryAsset.GetCategoryLabelNames(category);
            foreach (var label in labels)
            {
                newClip.frames.Add(new AnimationFrame{ labelKey = label, durationModifier = 1.0f });
            }

            if (clips.Exists(c => c.clipName == newClip.clipName)) 
                clips.Find(c => c.clipName == newClip.clipName).categoryKeys[dir] = category;
            else
                clips.Add(newClip);
        }

        Debug.Log($"Generated {clips.Count} clips from {sourceLibraryAsset.name}");
    }

    public SimpleAnimationClip GetClip(string name)
    {
        return clips.Find(c => c.clipName == name);
    }
}

[System.Serializable]
public class SimpleAnimationClip
{
    public string clipName;
    public SerializedDictionary<EnumManager.AnimDir, string> categoryKeys = new();
    public bool isLoop = true;
    public float tickSeconds = 0.1f;
    public float maxPlaySeconds = -1;

    public List<AnimationFrame> frames = new();
}

[System.Serializable]
public class AnimationFrame
{
    public string labelKey; 
    [Tooltip("(1 = normal, 2 = x2)")]
    public float durationModifier = 1.0f; 
    public string triggerEventName; 
}