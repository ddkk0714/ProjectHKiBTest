
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.Events;
using DG.Tweening;
using System.Linq;

public class SimpleAnimationPlayer : MonoBehaviour
{
    [Header("Data Reference")]
    public SimpleAnimationDataSO animationData;

    [Header("Settings")]
    public bool playOnAwake = true;
    public string overrideClipName;
    public bool disableWhenStop = false;

    [Header("Event Handlers")]
    public Dictionary<string, Action> animationEvents = new();
    
    [System.Serializable]
    public class StringEvent : UnityEvent<string> { }
    public StringEvent onCustomEventTriggered;

    [SerializeField] private SpriteResolver _spriteResolver;
    protected Sequence _currentSequence;
    private SimpleAnimationClip _currentClip;
    protected int _loop = 0;
    [SerializeField] [NaughtyAttributes.ReadOnly] private EnumManager.AnimDir _currentAnimDir = EnumManager.AnimDir.D;

    protected void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (playOnAwake)
        {
            if(!string.IsNullOrEmpty(overrideClipName))
                Play(overrideClipName);
            else if(!string.IsNullOrEmpty(animationData.defaultClipName))
                Play(animationData.defaultClipName);
        }
    }

    public void Play(string clipName)
    {
        if (animationData == null) return;
        SimpleAnimationClip clip = animationData.GetClip(clipName);

        if (clip != null)
        {
            _currentClip = clip;
            _loop = 0;
            PlayClip(clip);
        }
        else
        {
            Debug.LogWarning($"Clip '{clipName}' not found.");
        }
    }

    public void Play(SimpleAnimationClip clip)
    {
        _loop = 0;
        PlayClip(clip);
    }

    protected void PlayClip(SimpleAnimationClip clip)
    {
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Kill();
        
        if (clip.frames.Count == 0) return;

        _currentSequence = DOTween.Sequence();
        float baseFrameDuration = 1f * clip.tickSeconds;

        for (int i = 0; i < clip.frames.Count; i++)
        {
            int frameIndex = i;
            AnimationFrame frame = clip.frames[i];
            float duration = baseFrameDuration * frame.durationModifier;

            _currentSequence.AppendCallback(() => ApplyFrame(clip, frameIndex));

            _currentSequence.AppendInterval(duration);
        }

        _currentSequence.AppendCallback(() => _loop++);

        if (clip.maxPlaySeconds > 0)
            _currentSequence.InsertCallback(clip.maxPlaySeconds, Stop);

        if (clip.isLoop)
            _currentSequence.SetLoops(-1);
    }

    public void Stop()
    {
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Kill();
        if (disableWhenStop) gameObject.SetActive(false);
    }

    public void Pause()
    {
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Pause();
    }

    public void Resume()
    {
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Play();
    }

    public void SetDirection(EnumManager.AnimDir animDir) 
    {
        Stop();
        _currentAnimDir = animDir;
        Play(_currentClip.clipName);
    }

    protected void ApplyFrame(SimpleAnimationClip clip, int frameIndex)
    {
        var frame = clip.frames[frameIndex];

        string categoryKey;
        if (clip.categoryKeys.Count < 1) 
            categoryKey = clip.clipName;
        else if (!clip.categoryKeys.ContainsKey(_currentAnimDir)) 
            categoryKey = clip.categoryKeys.Values.ToList()[0];
        else 
            categoryKey = clip.categoryKeys[_currentAnimDir];
        
        _spriteResolver.SetCategoryAndLabel(categoryKey, frame.labelKey);

        // 이벤트 트리거
        if (!string.IsNullOrEmpty(frame.triggerEventName))
        {
            if (animationEvents.ContainsKey(frame.triggerEventName))
            {
                animationEvents[frame.triggerEventName]?.Invoke();
            }
            onCustomEventTriggered?.Invoke(frame.triggerEventName);
        }
    }

    public bool IsFirstLoopEnded() => _loop > 0;

    public void RegisterEvent(string eventName, Action callback)
    {
        if (!animationEvents.ContainsKey(eventName))
            animationEvents[eventName] = callback;
        else
            animationEvents[eventName] += callback;
    }

    public void UnregisterEvent(string eventName, Action callback)
    {
        if (animationEvents.ContainsKey(eventName))
            animationEvents[eventName] -= callback;
    }

    private void OnDestroy()
    {
        _currentSequence?.Kill();
    }
}