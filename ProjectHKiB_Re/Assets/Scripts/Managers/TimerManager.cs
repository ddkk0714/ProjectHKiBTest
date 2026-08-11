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

    // 인스펙터/디버그 뷰가 플레이 시작 전이나 GameManager가 아직 안 뜬 시점에 읽을 수 있어
    // null 가드를 둔다. 예전엔 여기서 바로 NullReferenceException이 났다.
    public float ElapsedTime
    {
        get
        {
            if (GameManager.instance == null || GameManager.instance.timerManager == null) return 0f;
            return GameManager.instance.timerManager.GetElapsedTime(GetHashCode());
        }
    }

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

        // 여기 시퀀스는 의도적으로 DOTween 기본 UpdateType.Normal(= Time.timeScale의 영향을
        // 받는 스케일 시간)을 쓴다. 버프 쿨타임을 비롯한 이 매니저의 모든 타이머가 그 덕분에
        // TimeManager.Pause()로 게임을 멈추면 함께 멈추고, RemainTime도 얼어붙는다.
        // → SetUpdate(true)를 붙이면 메뉴를 열어둔 채로 버프가 계속 닳게 되니 붙이지 말 것.
        Sequence sequence = DOTween.Sequence();
        sequence.AppendInterval(duration);

        // 자연 만료한 타이머도 목록에서 뺀다. 예전엔 CancelTimer로만 지워서, 만료된 뒤 다시
        // 시작되지 않는 타이머의 항목이 영영 남았다(Permanent 버프, 스택이 남은 Unstack 버프 등).
        // defaultAutoKill이 켜져 있어 시퀀스는 이미 죽은 뒤라 죽은 참조만 쌓였고, 그걸 읽는
        // GetElapsedTime은 safeMode 경고까지 냈다.
        //
        // 콜백보다 먼저 지운다 — 콜백 안에서 같은 Timer가 StartTimer로 다시 시작하는 경우가
        // 있는데(버프 재적용 등), 나중에 지우면 그 새 항목을 지워버린다.
        sequence.OnComplete(() =>
        {
            _cooltimes.Remove(ID);
            timerEnded?.Invoke();
        });

        _cooltimes[ID] = sequence;
    }

    // ElapsedDelay()는 "경과 시간"이 아니라 SetDelay()로 설정한 딜레이 값을 반환하는 API다(DOTween 문서:
    // "Returns the eventual elapsed delay set for this tween"). 여기 시퀀스들은 딜레이를 준 적이 없어
    // 항상 0을 반환했고, 그 결과 Timer.RemainTime(= Time - ElapsedTime)이 절대 줄어들지 않고 항상
    // 전체 시간을 보고하는 버그가 있었다 — 실제 만료(OnComplete 콜백)는 DOTween 내부 재생 시계로
    // 별개로 동작해 정상 발동했지만, RemainTime을 읽는 모든 코드(세이브 시스템 등)는 항상 "풀타임
    // 남음"으로 잘못 봤다. Elapsed(includeLoops:true)가 실제 경과 시간을 반환하는 올바른 API다.
    public float GetElapsedTime(int ID) => _cooltimes.ContainsKey(ID) ? _cooltimes[ID].Elapsed(true) : 0f;

    public void CancelTimer(int ID)
    {
        if (!_cooltimes.ContainsKey(ID)) return;
        _cooltimes[ID]?.Kill();
        _cooltimes.Remove(ID);
    }
}