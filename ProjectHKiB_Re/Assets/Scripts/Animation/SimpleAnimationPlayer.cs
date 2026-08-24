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
    public bool syncDirection = false;
    [NaughtyAttributes.ShowIf("syncDirection")]
    public SimpleAnimationPlayer[] playersToSyncDirection;
    public bool syncAnimation = false;
    [NaughtyAttributes.ShowIf("syncAnimation")]
    public SimpleAnimationPlayer[] playersToSyncAnimation;

    [Header("Event Handlers")]
    public Dictionary<string, Action> animationEvents = new();

    [System.Serializable]
    public class StringEvent : UnityEvent<string> { }
    public StringEvent onCustomEventTriggered;

    [SerializeField] private SpriteResolver _spriteResolver;
    [SerializeField] private SpriteRenderer _spriteRenderer;
    protected Sequence _currentSequence;
    protected List<string> _reservedClips;
    private SimpleAnimationClip _currentClip;
    public string CurrentAnimationName => _currentClip.clipName;
    protected int _loop = 0;
    [field: SerializeField][field: NaughtyAttributes.ReadOnly] public EnumManager.AnimDir CurrentAnimDir { get; private set; } = EnumManager.AnimDir.D;
    public bool IsFirstLoopEnded => _loop > 0;

    protected void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        _reservedClips = new(10);
        if (playOnAwake)
        {
            if (!string.IsNullOrEmpty(overrideClipName))
                PlayInternal(overrideClipName);
            else if (animationData && !string.IsNullOrEmpty(animationData.defaultClipName))
                PlayInternal(animationData.defaultClipName);
        }
    }

    public void Play(string clipName)
    {
        ClearReservation();
        PlayInternal(clipName);
    }

    private void PlayInternal(string clipName)
    {
        if (animationData == null) return;

        if (clipName == "Stop") { Stop(); return; }

        SimpleAnimationClip clip = animationData.GetClip(clipName);

        for (int i = 0; i < playersToSyncAnimation.Length; i++)
            playersToSyncAnimation[i].PlayInternal(clipName);

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

    public void Reserve(string clipName)
    {
        // 아직 아무것도 재생하지 않았으면 예약할 "다음"이 없다 — 그냥 지금 튼다.
        // (예전엔 _currentClip이 null인 채로 들어오면 여기서 그대로 터졌다.)
        if (_currentClip == null) { PlayInternal(clipName); return; }

        if (!_currentClip.isLoop && IsFirstLoopEnded && _reservedClips.Count < 1) { PlayInternal(clipName); return; }
        if (_reservedClips.Count < _reservedClips.Capacity)
            _reservedClips.Add(clipName);
    }

    public void ClearReservation()
    {
        _reservedClips?.Clear();
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

        _currentSequence.AppendCallback(ManageLoop);

        if (clip.maxPlaySeconds > 0)
            _currentSequence.InsertCallback(clip.maxPlaySeconds, Stop);

        if (clip.isLoop)
            _currentSequence.SetLoops(-1);
    }

    public void ManageLoop()
    {
        _loop++;
        if (IsFirstLoopEnded && _reservedClips.Count > 0)
        {
            PlayInternal(_reservedClips[0]);
            _reservedClips.RemoveAt(0);
        }
    }

    public void Stop()
    {
        for (int i = 0; i < playersToSyncAnimation.Length; i++)
            playersToSyncAnimation[i].Stop();
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Kill();
        if (disableWhenStop) _spriteRenderer.enabled = false;
    }

    public void Pause()
    {
        for (int i = 0; i < playersToSyncAnimation.Length; i++)
            playersToSyncAnimation[i].Pause();
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Pause();
    }

    public void Resume()
    {
        for (int i = 0; i < playersToSyncAnimation.Length; i++)
            playersToSyncAnimation[i].Resume();
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Play();
    }

    public void SetDirection(EnumManager.AnimDir animDir)
    {
        CurrentAnimDir = animDir;

        if (_currentClip != null && _currentClip.resetWhenDirectionChange)
        {
            Stop();
            PlayInternal(_currentClip.clipName);
        }

        if (syncDirection)
            for (int i = 0; i < playersToSyncDirection.Length; i++)
                playersToSyncDirection[i].SetDirection(animDir);
    }

    protected void ApplyFrame(SimpleAnimationClip clip, int frameIndex)
    {
        var frame = clip.frames[frameIndex];

        string categoryKey;
        if (clip.categoryKeys.Count < 1)
            categoryKey = clip.clipName;
        else if (!clip.categoryKeys.ContainsKey(CurrentAnimDir))
            categoryKey = clip.categoryKeys.Values.ToList()[0];
        else
            categoryKey = clip.categoryKeys[CurrentAnimDir];

        _spriteResolver.SetCategoryAndLabel(categoryKey, frame.labelKey);
        if (_spriteRenderer) _spriteRenderer.enabled = true;

        if (!string.IsNullOrEmpty(frame.triggerEventName))
        {
            if (animationEvents.ContainsKey(frame.triggerEventName))
            {
                animationEvents[frame.triggerEventName]?.Invoke();
            }
            onCustomEventTriggered?.Invoke(frame.triggerEventName);
        }
    }

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