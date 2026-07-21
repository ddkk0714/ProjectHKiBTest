using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 단서 도감 전용 싱글턴 — RouteModule이 이동/장비/경로를 소유하는 것과 같은 패턴으로,
// 획득한 ClueData 목록(도감에 실제로 보여줄 것)의 단일 소유자 역할을 한다.
//
// RouteProgressState.OnClueAcquired를 구독해 자동 갱신되는 것이 기본 경로이지만,
// 세이브 로드 직후처럼 이벤트가 발행되지 않는 경로도 있으므로(RouteProgressState.ApplyEventFlag 참고),
// RebuildFromProgress()로 언제든 전체 재계산할 수 있게 열어둔다 — CodexPanel이 Open()마다 호출한다.
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 대화/퀘스트/NPC 시스템이 "이 정보는 도감에 자동 등록되는 메모여야 한다"
// 같은 식으로 도감에 항목을 추가하고 싶을 때, 또는 다른 UI가 획득한 단서 목록을 읽고 싶을 때 사용.
//
// ▸ 접근: CodexModule.Instance (자동 생성 싱글턴)
//
// ▸ 조회
//   AcquiredClues            : 지금까지 획득한 정식 단서 전체(ClueData, 읽기 전용)
//   UserEntries               : 유저(또는 다른 시스템)가 직접 만든 자유 메모 전체(CodexUserEntry, 읽기 전용)
//   IsClueNew(clueId)         : "획득했지만 아직 카드로 안 열어본" NEW 상태인지
//   OnCodexChanged            : 도감 내용이 바뀔 때마다 발행(구독해서 UI 갱신 트리거로 사용)
//
// ▸ 유저 메모(자유 항목) 추가 — 노트의 "단서 생성" 기능이 이 API를 그대로 쓴다. 다른 시스템도
//   똑같이 호출해 "정식 ClueData는 아니지만 도감에 기록되는 항목"을 만들 수 있다.
//     var entry = CodexModule.Instance.AddUserEntry(title, content, mapCategory, keywords);
//   반환된 entry.guid를 NoteModule.Instance.AddManualPin(entry.guid)에 넘기면 노트에도 바로 편입된다
//   (NotePanel.HandleClueCreateRequested가 실제로 이렇게 조합해서 쓴다).
//   수정/삭제: UpdateUserEntry(guid, ...) / RemoveUserEntry(guid)
//
// ▸ 세이브 로드 등으로 전체 재계산이 필요할 때(예: RouteProgressState.OnClueAcquired 이벤트를
//   놓쳤을 가능성이 있는 시점)
//     CodexModule.Instance.RebuildFromProgress();
//   RouteProgressState.AcquiredClueIds 기준으로 AcquiredClues를 처음부터 다시 채운다.
//
// ▸ ImportUserEntries는 세이브 시스템 전용(SaveModule이 직접 호출) — 게임플레이 코드에서 직접
//   호출하지 말 것. MarkClueViewed는 카드 UI가 "이 단서를 열람했다" 표시할 때 쓰는 내부용에 가깝다.
// ════════════════════════════════════════════════════════════════
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

    // 6-3단계(Clue_System.md) — "새로 획득했지만 아직 카드로 한 번도 열어보지 않은 단서" 후보 집합.
    // HandleClueAcquired(실시간 획득)에서만 채워진다 — RebuildFromProgress(세이브 로드 등 일괄 재계산)는
    // 예전에 이미 갖고 있던 단서까지 전부 "NEW"로 만들어버리므로 여기 채우지 않는다. 카드로 한 번
    // 열어보면(MarkClueViewed) 제거된다. 세이브 미연동(런타임 전용) — 재시작하면 사라진다, 6-3 문서에
    // 적힌 대로 "별도 시스템 없이 도감 내부에서 닫힌 상태로" 구현하는 선에서 의도적으로 단순화했다.
    private readonly HashSet<string> _unviewedNewClueIds = new();
    public bool IsClueNew(string clueId) => !string.IsNullOrEmpty(clueId) && _unviewedNewClueIds.Contains(clueId);
    // OnCodexChanged를 일부러 발행하지 않는다 — 이 메서드는 트리 행 클릭 콜백(CodexDrawerTreeView)
    // 도중 CodexPanel.OnEntrySelected에서 호출되는데, 여기서 리프레시 이벤트를 쏘면 그 클릭 콜백이
    // 참조하고 있던 CodexEntry 객체(RefreshTree가 매번 새로 만듦)가 곧바로 낡은 참조가 되어, 뒤이어
    // 실행되는 트리의 선택 하이라이트 비교(참조 비교)가 깨진다. NEW 배지는 다음 자연스러운 갱신
    // (다른 단서 획득, 패널 재오픈 등) 때 사라지는 정도로 충분하다고 판단해 단순화했다.
    public void MarkClueViewed(string clueId)
    {
        if (string.IsNullOrEmpty(clueId)) return;
        _unviewedNewClueIds.Remove(clueId);
    }

    // 유저가 도감 안에서 직접 작성한 자유 메모("빈 단서") — 3단계.
    // 세이브 연동(6단계) 완료 — ImportUserEntries 참고.
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
        _unviewedNewClueIds.Add(clue.id);
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

    // ─── 세이브 연동 (6단계) ────────────────────────────────────
    // SaveModule.SaveEvents()/LoadEvents()가 직접 Instance로 접근해 호출한다. 획득 단서 목록
    // (_acquiredClues)은 여기 포함되지 않는다 — RouteProgressState.AcquiredClueIds에서 파생되므로
    // 이미 IEventSaveProvider 경로로 저장되고, 로드 후 RebuildFromProgress()가 다시 채운다.
    public void ImportUserEntries(List<CodexUserEntry> entries)
    {
        _userEntries.Clear();
        if (entries != null) _userEntries.AddRange(entries);
        OnCodexChanged?.Invoke();
    }
}
