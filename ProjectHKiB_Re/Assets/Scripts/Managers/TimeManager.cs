using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * ── TimeManager 사용 가이드 ──────────────────────────────────────────────────
 *
 * 게임 내 시간의 흐름을 한 곳에서 관리한다. 자체 시간 축을 새로 만들지 않고
 * Unity의 Time.timeScale을 단일 진실 공급원으로 쓴다 — 이 프로젝트 코드는 이미
 * 전부 Time.deltaTime / Time.fixedDeltaTime / Time.time(= 스케일된 시간)을 쓰고,
 * 버프 쿨타임(TimerManager)도 DOTween 기본 UpdateType.Normal(스케일 시간) 위에
 * 얹혀 있으므로 timeScale = 0 하나로 게임플레이 전체가 멈춘다.
 *
 * [1] 일시정지
 *   Pause(TimeManager.ReasonMenu)    // 사유를 걸어 정지
 *   Resume(TimeManager.ReasonMenu)   // 그 사유만 해제
 *   ResumeAll()                      // 전부 해제 (씬 전환/세이브 로드 안전장치)
 *
 *   정지 사유를 bool 하나가 아니라 집합으로 관리하는 이유: 메뉴와 지도를 겹쳐
 *   열었을 때 한쪽만 닫아도 게임이 재개되어 버리는 문제를 막기 위해서다.
 *   사유가 하나라도 남아 있으면 계속 정지 상태를 유지한다.
 *
 * [2] 배속
 *   SetGameSpeed(0.5f)               // 슬로우모션. 일시정지와는 별개 축으로 동작
 *   배속을 0으로 만들지 말 것 — 정지는 Pause()로 표현한다.
 *
 * [3] 경과 시간
 *   GameTime                         // 일시정지를 제외한 누적 게임 내 시간(초)
 *
 * [주의] 정지 중에도 계속 움직여야 하는 연출(메뉴 UI, 대화창 타이핑 등)은
 *        DOTween 쪽에서 SetUpdate(true)로, Animator는 Update Mode를
 *        Unscaled Time으로 시간축을 분리해야 한다.
 * ────────────────────────────────────────────────────────────────────────────
 */
public class TimeManager : MonoBehaviour
{
    // 정지 사유 상수. 문자열을 직접 쓰다 오타가 나면 Resume이 먹지 않아
    // "영원히 안 풀리는 정지"가 되므로 되도록 이 상수를 쓴다.
    // UIManager가 관리하는 창들은 여기에 창 이름을 붙여 "Menu:Map" 식으로 사유를 만든다.
    public const string ReasonMenu = "Menu";

    private readonly HashSet<string> _pauseReasons = new();
    private float _defaultFixedDeltaTime;
    private bool _lastAppliedPause;

    public bool IsPaused => _pauseReasons.Count > 0;
    public float GameSpeed { get; private set; } = 1f;

    /// <summary>일시정지 구간을 제외한 누적 게임 내 경과 시간(초).</summary>
    public float GameTime { get; private set; }

    /// <summary>정지 상태가 바뀔 때만 발생. 인자는 바뀐 뒤의 IsPaused.</summary>
    public event Action<bool> OnPauseChanged;

    private void Awake()
    {
        // ProjectSettings의 Fixed Timestep(0.016)을 코드에 중복으로 적어두면
        // 설정을 바꿨을 때 어긋나므로, 시작 시점의 실제 값을 기준으로 삼는다.
        _defaultFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void Update()
    {
        // 정지 중에는 Time.deltaTime이 0이므로 별도 분기 없이 자동으로 제외된다.
        GameTime += Time.deltaTime;
    }

    public bool IsPausedBy(string reason) => _pauseReasons.Contains(reason);

    public void Pause(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return;
        if (!_pauseReasons.Add(reason)) return;
        if (_pauseReasons.Count == 1) Apply();
    }

    public void Resume(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return;
        if (!_pauseReasons.Remove(reason)) return;
        if (_pauseReasons.Count == 0) Apply();
    }

    public void ResumeAll()
    {
        if (_pauseReasons.Count == 0) return;
        _pauseReasons.Clear();
        Apply();
    }

    public void SetGameSpeed(float speed)
    {
        GameSpeed = Mathf.Max(speed, 0.0001f);
        Apply();
    }

    private void Apply()
    {
        bool paused = IsPaused;
        bool changed = paused != _lastAppliedPause;
        _lastAppliedPause = paused;

        Time.timeScale = paused ? 0f : GameSpeed;

        // 배속에 비례해 물리 틱을 스케일하면 슬로우모션 중에도 물리 갱신 빈도가
        // 실시간 기준으로 일정하게 유지된다. 단 정지 중에는 건드리지 않는다 —
        // fixedDeltaTime이 0이 되면 FixedUpdate가 무한 루프에 빠진다.
        if (!paused) Time.fixedDeltaTime = _defaultFixedDeltaTime * GameSpeed;

        if (changed) OnPauseChanged?.Invoke(paused);
    }

    // 에디터에서 정지 상태로 플레이를 끄면 timeScale = 0이 그대로 남아
    // 다음 플레이가 멈춘 것처럼 보이는 함정이 있다. 반드시 원복해 둔다.
    private void OnDestroy() => Restore();
    private void OnApplicationQuit() => Restore();

    private void Restore()
    {
        Time.timeScale = 1f;
        if (_defaultFixedDeltaTime > 0f) Time.fixedDeltaTime = _defaultFixedDeltaTime;
    }
}
