using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 단서 도감 전용 싱글턴 — RouteModule이 이동/장비/경로를 소유하는 것과 같은 패턴으로,
// 획득한 ClueData 목록(도감에 실제로 보여줄 것)의 단일 소유자 역할을 한다.
//
// RouteProgressState.OnClueAcquired를 구독해 자동 갱신되는 것이 기본 경로이지만,
// 세이브 로드 직후처럼 이벤트가 발행되지 않는 경로도 있으므로(ImportFromSaveData 참고),
// RebuildFromProgress()로 언제든 전체 재계산할 수 있게 열어둔다 — CodexPanel이 Open()마다 호출한다.
public class CodexModule : MonoBehaviour
{
    private static CodexModule _instance;
    private static bool _isQuitting; // 종료 중에는 다른 오브젝트의 OnDestroy가 Instance를 건드려도 재생성하지 않는다.

    public static CodexModule Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying && !_isQuitting)
            {
                _instance = FindObjectOfType<CodexModule>();
                if (_instance == null)
                    _instance = new GameObject(nameof(CodexModule)).AddComponent<CodexModule>();
            }
            return _instance;
        }
    }

    // 플레이 모드 종료(에디터) / 앱 종료 시 OnDestroy들보다 먼저 호출되는 것이 보장된다 — 이후
    // CodexPanel.OnDestroy 등에서 Instance에 접근해도 새 GameObject를 만들지 않도록 막는다.
    // (안 막으면 씬을 닫을 때 "Some objects were not cleaned up when closing the scene" 경고가 뜬다 —
    // OnDestroy 도중 Instance 접근이 이미 파괴된 CodexModule을 새로 스폰해버리기 때문.)
    private void OnApplicationQuit() => _isQuitting = true;

    private readonly List<ClueData> _acquiredClues = new();
    public IReadOnlyList<ClueData> AcquiredClues => _acquiredClues;

    // 유저가 도감 안에서 직접 작성한 자유 메모("빈 단서") — 3단계.
    // 세이브 미연동(6단계 예정) — 현재는 순수 런타임 상태, 재시작하면 사라진다.
    private readonly List<CodexUserEntry> _userEntries = new();
    public IReadOnlyList<CodexUserEntry> UserEntries => _userEntries;

    // CodexPanel 등 UI가 구독 — 획득/메모 목록이 바뀔 때마다 다시 그리라는 신호.
    public event Action OnCodexChanged;

    private bool _subscribed;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[CodexModule] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start() => RebuildFromProgress();

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_subscribed && RouteModule.Instance != null)
            RouteModule.Instance.Progress.OnClueAcquired -= HandleClueAcquired;
    }

    private void TrySubscribe()
    {
        if (_subscribed || RouteModule.Instance == null || RouteModule.Instance.Progress == null) return;
        RouteModule.Instance.Progress.OnClueAcquired += HandleClueAcquired;
        _subscribed = true;
    }

    private void HandleClueAcquired(ClueData clue)
    {
        if (!_acquiredClues.Contains(clue)) _acquiredClues.Add(clue);
        OnCodexChanged?.Invoke();
    }

    // RouteProgressState.AcquiredClueIds 기준으로 전체 재계산.
    // 아직 구독하지 못한 상태(RouteModule/MapGraph가 늦게 준비된 경우)라면 여기서 구독도 함께 시도한다.
    public void RebuildFromProgress()
    {
        TrySubscribe();

        var graph = MapGraph.Instance;
        var progress = RouteModule.Instance != null ? RouteModule.Instance.Progress : null;
        if (graph == null || progress == null) return;

        _acquiredClues.Clear();
        foreach (var clueId in progress.AcquiredClueIds)
        {
            var clue = graph.GetClue(clueId);
            if (clue != null) _acquiredClues.Add(clue);
        }
        OnCodexChanged?.Invoke();
    }

    // ─── 유저 생성 메모 CRUD ────────────────────────────────────

    public CodexUserEntry AddUserEntry(string title, string content, string mapCategory, string[] keywords)
    {
        var entry = new CodexUserEntry
        {
            guid        = Guid.NewGuid().ToString("N"),
            title       = title,
            content     = content,
            mapCategory = mapCategory,
            keywords    = keywords ?? Array.Empty<string>(),
        };
        _userEntries.Add(entry);
        OnCodexChanged?.Invoke();
        return entry;
    }

    public bool UpdateUserEntry(string guid, string title, string content, string mapCategory, string[] keywords)
    {
        var entry = _userEntries.FirstOrDefault(e => e.guid == guid);
        if (entry == null) return false;

        entry.title       = title;
        entry.content     = content;
        entry.mapCategory = mapCategory;
        entry.keywords    = keywords ?? Array.Empty<string>();
        OnCodexChanged?.Invoke();
        return true;
    }

    public bool RemoveUserEntry(string guid)
    {
        var entry = _userEntries.FirstOrDefault(e => e.guid == guid);
        if (entry == null) return false;

        _userEntries.Remove(entry);
        OnCodexChanged?.Invoke();
        return true;
    }
}
