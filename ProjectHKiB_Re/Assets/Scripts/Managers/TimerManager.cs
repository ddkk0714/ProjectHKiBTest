using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

[Serializable]
public class Timer
{
    public float Time { get; private set; }
    public bool IsCooltimeEnded { get; private set; }

    private TweenCallback _timeEndCallback;

    public float ElapsedTime => GameManager.instance.timerManager.GetElapsedTime(GetHashCode());
    public float RemainTime => Mathf.Max(0f, Time - ElapsedTime);

    public void StartTimer(float cooltime, TweenCallback timerEndCallback = null)
    {
        Time = cooltime;

        if (!IsCooltimeEnded)
            CancelTimer();

        IsCooltimeEnded = false;
        _timeEndCallback = timerEndCallback;
        _timeEndCallback += () => IsCooltimeEnded = true;

        GameManager.instance.timerManager.StartCooltime(GetHashCode(), this, _timeEndCallback);
    }

    /// <summary>
    /// 현재까지 진행된 시간은 유지하고, "총 쿨타임"만 새 값으로 다시 계산
    /// 예) 30초 중 10초 지난 상태에서 newTotalCooltime = 33 이면 남은 시간은 23초
    /// </summary>
    public void RecalculateTotalTime(float newTotalTime)
    {
        if (IsCooltimeEnded)
        {
            Time = newTotalTime;
            return;
        }

        float elapsed = ElapsedTime;
        float newRemain = Mathf.Max(0f, newTotalTime - elapsed);

        CancelTimer();
        Time = newTotalTime;

        if (newRemain <= 0f)
        {
            IsCooltimeEnded = true;
            _timeEndCallback?.Invoke();
            return;
        }

        IsCooltimeEnded = false;
        GameManager.instance.timerManager.StartCooltime(GetHashCode(), _timeEndCallback, newRemain);
    }

    public void ExtendTimer(float time, TweenCallback timerEndCallback = null)
    {
        float elapsedTime = ElapsedTime;
        CancelTimer();
        StartTimer(time + elapsedTime, timerEndCallback);
    }

    public void CancelTimer()
    {
        GameManager.instance.timerManager.CancelTimer(GetHashCode());
        IsCooltimeEnded = true;
    }

    public Timer(Timer cooltime)
    {
        Time = cooltime.Time;
        IsCooltimeEnded = true;
    }

    public Timer(float cooltime)
    {
        Time = cooltime;
        IsCooltimeEnded = true;
    }

    public Timer()
    {
        Time = 0;
        IsCooltimeEnded = true;
    }
}

public class TimerManager : MonoBehaviour
{
    private readonly Dictionary<int, Sequence> _cooltimes = new();

    public void StartCooltime(int ID, Timer timer, TweenCallback timerEnded)
    {
        StartCooltime(ID, timerEnded, timer.Time);
    }

    public void StartCooltime(int ID, TweenCallback timerEnded, float duration)
    {
        CancelTimer(ID);

        Sequence sequence = DOTween.Sequence();
        sequence.AppendInterval(duration);
        sequence.OnComplete(timerEnded);
        _cooltimes[ID] = sequence;
    }

    public float GetElapsedTime(int ID) => _cooltimes.ContainsKey(ID) ? _cooltimes[ID].ElapsedDelay() : 0f;

    public void CancelTimer(int ID)
    {
        if (!_cooltimes.ContainsKey(ID)) return;
        _cooltimes[ID]?.Kill();
        _cooltimes.Remove(ID);
    }
}