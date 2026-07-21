using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 노트 전용 싱글턴 — CodexModule과 같은 패턴으로, 노트에 담긴 NoteEntry 목록의 단일 소유자.
//
// 도감(CodexModule)은 "획득한 단서 전체"를 보여주는 반면, 노트는 그중 실제로 쓸 것만 추려 좌측
// 단서 그래프(NoteRouteGraphView)에 배치한다(NoteSystem_기획서.md). 규칙 1(경로 연동 자동 편입)·
// 규칙 2(도감/단서 서랍에서 수동 핀)와 세이브 연동(7단계)까지 동작한다.
// (2026-07-14: 규칙 3 "미획득 후보 자동 노출"은 요청으로 제거됨 — 노트는 이제 항상 획득한 단서만 다룬다.)
// [2026-07-21] 다중 목적지 이동 계획(4단계, RouteWaypointPlan) 기능은 요청으로 완전히 제거됨 —
// 우측 패널은 그 대신 "단서 서랍"(ClueDrawerView)으로 교체됨. Git 히스토리에 이전 구현이 남아있다.
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 퀘스트/대화 시스템이 특정 단서를 노트에 강제로 편입시키고 싶을 때,
// 또는 다른 UI가 노트 상태를 읽고 싶을 때 사용한다. (일반적인 게임플레이에서는 대부분 유저가
// 직접 UI로 조작하므로, 다른 모듈이 이 API를 호출할 일은 드물다 — 주로 읽기/구독 용도.)
//
// ▸ 접근: NoteModule.Instance (자동 생성 싱글턴)
//
// ▸ 조회
//   Entries                 : 현재 노트에 놓인 항목 전체(경로연동 + 수동핀 통합, 읽기 전용)
//   IsPinned(clueId)        : 특정 단서가 이미 노트에 있는지
//   CanEdit                 : 지금 편집 가능한지(이동 중엔 false — 열람은 별도, NotePanel이 판단)
//   OnNoteChanged           : 노트 내용이 바뀔 때마다 발행(구독해서 UI 갱신 트리거로 사용)
//
// ▸ 단서를 노트에 강제로 편입시키고 싶을 때 (예: 퀘스트 시스템이 "이 단서는 꼭 노트에 있어야 한다")
//     if (NoteModule.Instance.CanEdit) NoteModule.Instance.AddManualPin(clueId);
//   이미 있으면(경로연동이든 수동핀이든) 아무 일도 안 하고 false 반환. clueId는 ClueData.id 또는
//   CodexUserEntry.guid 둘 다 가능(NoteClueResolver가 통일해서 다룸).
//
// ▸ 선택된 경로가 바뀌었을 때 노트를 그에 맞춰 재계산 — 보통 RouteModule.OnRouteSelected 구독으로
//   자동 처리되므로(NoteModule 내부) 외부에서 직접 호출할 일은 거의 없다.
//     NoteModule.Instance.RebuildRouteLinkedEntries(route);
//
// ▸ 단서 간 수동 연동(간선) — 노트 UI의 "단서 연동 모드"가 사용하는 API, 다른 시스템이 프로그램적으로
//   특정 단서 두 개를 미리 이어두고 싶을 때도 사용 가능.
//     NoteModule.Instance.ToggleClueLink(clueIdA, clueIdB);  // 있으면 끊고 없으면 잇는다
//     NoteModule.Instance.AreCluesLinked(clueIdA, clueIdB);  // 조회
//     ClueLinks                                              // 전체 연동 쌍 목록(읽기 전용)
//
// ▸ "저장한 루트"(보드) — 노트 상단 툴바가 사용하는 이름 붙은 스냅샷 저장/불러오기 API.
//   SavedBoards / OnSavedBoardsChanged / SaveBoard(...) / DeleteBoard(boardId) / GetBoard(boardId) /
//   ApplyManualPins(board) — 다른 시스템이 직접 쓸 일은 거의 없고, 세이브 연동(ExportSavedBoards/
//   ImportSavedBoards)은 SaveModule 전용.
//
// ▸ ImportFrom/ExportSavedBoards/ImportSavedBoards/ExportClueLinks/ImportClueLinks는 세이브 시스템
//   전용(SaveModule.SaveEvents()/LoadEvents()가 직접 호출) — 게임플레이 코드에서 직접 호출하지 말 것.
// ════════════════════════════════════════════════════════════════
public class NoteModule : MonoBehaviour
{
    private static NoteModule _instance;
    private static bool _isQuitting; // 종료 중에는 다른 오브젝트의 OnDestroy가 Instance를 건드려도 재생성하지 않는다.

    public static NoteModule Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying && !_isQuitting)
            {
                _instance = FindObjectOfType<NoteModule>();
                if (_instance == null)
                    _instance = new GameObject(nameof(NoteModule)).AddComponent<NoteModule>();
            }
            return _instance;
        }
    }

    // 플레이 모드 종료(에디터) / 앱 종료 시 OnDestroy들보다 먼저 호출되는 것이 보장된다 —
    // CodexModule과 동일한 이유로, 종료 도중 재생성을 막는다.
    private void OnApplicationQuit() => _isQuitting = true;

    private readonly List<NoteEntry> _entries = new();
    public IReadOnlyList<NoteEntry> Entries => _entries;

    // NotePanel 등 UI가 구독 — 노트 목록이 바뀔 때마다 다시 그리라는 신호.
    public event Action OnNoteChanged;

    // 이동 중에는 노트 편집(항목 삭제·핀 등)을 잠근다 — 열람(NotePanel.Open)과는 분리된 권한
    // (NoteSystem_기획서.md "UI 진입 / 영속성" 절의 규칙 — 장비·경로가 이동 중 변경 불가한 것과 같은 이유).
    public bool CanEdit => RouteModule.Instance == null || !RouteModule.Instance.IsTraveling;

    private bool _subscribed;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[NoteModule] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start() => TrySubscribe();

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_subscribed && RouteModule.Instance != null)
        {
            RouteModule.Instance.OnRouteSelected -= HandleRouteSelected;
            if (RouteModule.Instance.Progress != null)
                RouteModule.Instance.Progress.OnClueAcquired -= HandleClueAcquired;
        }
    }

    private void TrySubscribe()
    {
        if (_subscribed || RouteModule.Instance == null || RouteModule.Instance.Progress == null) return;
        RouteModule.Instance.OnRouteSelected += HandleRouteSelected;
        // [요청, 2026-07-21] 경로를 먼저 골라둔 뒤 나중에 단서를 획득하는 순서에서는 OnRouteSelected가
        // 다시 발행되지 않아 RebuildRouteLinkedEntries가 호출될 계기가 없었다 — CodexModule과 같은 패턴으로
        // RouteProgressState.OnClueAcquired(단서를 실제로 획득한 그 순간)도 구독해, 지금 선택된 경로와
        // 연관된 단서면 획득 즉시 노트에 반영되도록 한다.
        RouteModule.Instance.Progress.OnClueAcquired += HandleClueAcquired;
        _subscribed = true;
    }

    private void HandleRouteSelected(PathResult route)
    {
        Debug.Log($"[NoteModule] HandleRouteSelected — route={(route == null ? "null" : $"valid={route.IsValid},nodes={route.Nodes?.Count}")}");
        TrySubscribe(); // RouteModule이 이 컴포넌트보다 늦게 준비된 경우를 대비
        RebuildRouteLinkedEntries(route);
    }

    // RebuildRouteLinkedEntries는 RouteLinked 항목만 통째로 지우고 다시 채우는 멱등 연산이라(수동 핀은
    // 안 건드림), 단서를 새로 하나 획득했을 때도 그냥 다시 호출하는 것으로 충분하다 — 그 단서가 지금
    // 선택된 경로의 맵과 연관 있으면 자동으로 편입되고, 아니면 (route/graph 내부에서 이미 필터링돼) 무시된다.
    private void HandleClueAcquired(ClueData clue)
    {
        TrySubscribe();
        RebuildRouteLinkedEntries(RouteModule.Instance?.SelectedRoute);
    }

    // 규칙 1(경로 연동 자동 편입): 선택된 경로가 지나는 맵들과 연관된, 이미 획득한 단서를 노트에 채운다.
    // "연관"의 기준은 ClueData.codexMapGuid(도감 분류 기준, Clue_System.md 1-1장)가
    // 경로의 노드 목록에 포함되는지 — NoteSystem_기획서.md "노트 편입 규칙 1"에 명시된 그대로.
    public void RebuildRouteLinkedEntries(PathResult route)
    {
        _entries.RemoveAll(e => e.reason == NotePinReason.RouteLinked);

        var progress = RouteModule.Instance != null ? RouteModule.Instance.Progress : null;
        var graph = MapGraph.Instance;
        Debug.Log($"[NoteModule] RebuildRouteLinkedEntries 입력 — route={(route == null ? "null" : $"valid={route.IsValid}")}, progress={(progress != null)}, graph={(graph != null)}, acquiredClues={progress?.AcquiredClueIds?.Count}");
        if (route != null && route.IsValid && progress != null && graph != null)
        {
            var routeNodeGuids = new HashSet<string>();
            foreach (var node in route.Nodes) routeNodeGuids.Add(node.guid);

            foreach (var clueId in progress.AcquiredClueIds)
            {
                var clue = graph.GetClue(clueId);
                if (clue == null || string.IsNullOrEmpty(clue.codexMapGuid)) continue;
                if (!routeNodeGuids.Contains(clue.codexMapGuid)) continue;

                // 이미 다른 경로로(수동 핀 등) 노트에 들어와 있으면 중복 추가하지 않는다.
                bool alreadyPresent = false;
                foreach (var existing in _entries)
                {
                    if (existing.clueId == clueId) { alreadyPresent = true; break; }
                }
                if (alreadyPresent) continue;

                _entries.Add(new NoteEntry
                {
                    clueId = clueId,
                    reason = NotePinReason.RouteLinked,
                });
            }
        }

        int routeLinkedCount = 0;
        foreach (var e in _entries) if (e.reason == NotePinReason.RouteLinked) routeLinkedCount++;
        Debug.Log($"[NoteModule] RebuildRouteLinkedEntries 완료 — RouteLinked 항목 {routeLinkedCount}개, 전체 {_entries.Count}개");
        OnNoteChanged?.Invoke();
    }

    // 1단계: 노트 항목 개별 삭제. 이동 중이면 거부(CanEdit) — 재선택 시 RebuildRouteLinkedEntries가
    // 다시 채울 수 있는 RouteLinked 항목도 동일하게 삭제 대상이다(2단계 이전엔 재선택 전까지는 유지됨).
    public bool RemoveEntry(string clueId)
    {
        if (!CanEdit)
        {
            Debug.LogWarning("[NoteModule] 이동 중에는 노트를 편집할 수 없습니다.");
            return false;
        }

        int removed = _entries.RemoveAll(e => e.clueId == clueId);
        if (removed > 0)
        {
            _clueLinks.RemoveWhere(p => p.a == clueId || p.b == clueId); // 지운 단서가 걸려있던 연동 간선도 같이 정리
            OnNoteChanged?.Invoke();
        }
        return removed > 0;
    }

    // ─── 2단계: 도감 → 노트 수동 핀 ──────────────────────────────

    public bool IsPinned(string clueId) => _entries.Exists(e => e.clueId == clueId);

    // 도감 카드의 "노트에 핀" 액션이 호출한다(CodexPanel 참고 — 획득한 단서에만 핀 버튼이 뜨므로
    // clueId는 항상 이미 획득한 단서를 가리킨다). 이미 핀돼 있거나(경로 연동 포함) 이동 중이면 거부.
    public bool AddManualPin(string clueId)
    {
        if (string.IsNullOrEmpty(clueId)) return false;
        if (!CanEdit)
        {
            Debug.LogWarning("[NoteModule] 이동 중에는 노트를 편집할 수 없습니다.");
            return false;
        }
        if (IsPinned(clueId)) return false;

        _entries.Add(new NoteEntry
        {
            clueId = clueId,
            reason = NotePinReason.ManualPin,
        });
        OnNoteChanged?.Invoke();
        return true;
    }

    // ─── 세이브 연동 (NoteSystem_기획서.md 7단계) ─────────────────
    // SaveModule.SaveEvents()/LoadEvents()가 직접 Instance로 접근해 호출한다(IEventSaveProvider와
    // 별개 경로 — 핀 목록은 Dictionary<string,bool>로 표현 안 되는 구조화 데이터라서).
    public void ImportFrom(List<NoteEntry> entries)
    {
        _entries.Clear();
        if (entries != null) _entries.AddRange(entries);
        OnNoteChanged?.Invoke();
    }

    // ─── 저장한 루트(보드) — 상단 툴바 창에서 이름 붙여 스냅샷 저장/불러오기 ──────
    // 자동 게임 세이브(위 ImportFrom/ExportSavedBoards)와는 별개의 "이름 붙은 다중 슬롯" 개념 —
    // 게임 세이브 한 슬롯 안에 여러 개의 보드가 같이 저장된다(노트 기획서 "저장한 루트" 절 참고).
    private readonly List<NoteSavedBoard> _savedBoards = new();
    public IReadOnlyList<NoteSavedBoard> SavedBoards => _savedBoards;

    // NoteBoardWindow가 구독 — 저장/삭제/불러오기로 목록이 바뀔 때마다 다시 그리라는 신호.
    // 노트 그래프 자체의 갱신 신호(OnNoteChanged)와 분리 — 보드 목록 창만 다시 그리면 되는 경우가 대부분이라서.
    public event Action OnSavedBoardsChanged;

    // 덮어쓰기 개념 없이 항상 새 보드로 추가한다 — 이름이 같아도 별개 스냅샷으로 취급(삭제는 boardId 기준).
    // [2026-07-21 확장] expandedClueIds 매개변수 추가 — 경로연동 단서를 노드로 펼쳐서 옮긴 위치도
    // 같이 저장하려면, "펼쳐진 상태" 자체도 같이 기억해둬야 불러올 때 그 위치가 적용될 노드가 생긴다.
    public NoteSavedBoard SaveBoard(string name, List<string> routeNodeGuids, List<string> manualPinClueIds,
        List<CluePositionEntry> cluePositions, List<string> expandedClueIds)
    {
        var board = new NoteSavedBoard
        {
            boardId = Guid.NewGuid().ToString("N"),
            boardName = string.IsNullOrWhiteSpace(name) ? $"보드 {_savedBoards.Count + 1}" : name,
            routeNodeGuids = routeNodeGuids != null ? new List<string>(routeNodeGuids) : new List<string>(),
            manualPinClueIds = manualPinClueIds != null ? new List<string>(manualPinClueIds) : new List<string>(),
            cluePositions = cluePositions != null ? new List<CluePositionEntry>(cluePositions) : new List<CluePositionEntry>(),
            expandedClueIds = expandedClueIds != null ? new List<string>(expandedClueIds) : new List<string>(),
            // [요청, 2026-07-21] 저장 시점의 단서 연동 관계 전부를 같이 담는다 — 양쪽 종류(경로연동/수동핀)
            // 구분 없이 그냥 지금 상태를 통째로 스냅샷 뜨고, 안 맞는 링크는 불러올 때 그냥 무해하게 무시된다.
            clueLinks = ExportClueLinks(),
        };
        _savedBoards.Add(board);
        OnSavedBoardsChanged?.Invoke();
        return board;
    }

    public bool DeleteBoard(string boardId)
    {
        int removed = _savedBoards.RemoveAll(b => b.boardId == boardId);
        if (removed > 0) OnSavedBoardsChanged?.Invoke();
        return removed > 0;
    }

    public NoteSavedBoard GetBoard(string boardId) => _savedBoards.Find(b => b.boardId == boardId);

    // 보드의 수동 핀 부분만 현재 노트에 반영한다(경로 연동 항목·그래프 배치 위치 복원은 NotePanel이
    // RouteModule.ImportSelectedRoute + RebuildRouteLinkedEntries + NoteRouteGraphView.ApplySavedPositions와
    // 함께 오케스트레이션한다 — 이 메서드는 그중 "핀 목록 교체"만 담당하는 단일 책임 조각).
    public bool ApplyManualPins(NoteSavedBoard board)
    {
        if (board == null) return false;
        if (!CanEdit)
        {
            Debug.LogWarning("[NoteModule] 이동 중에는 노트를 편집할 수 없습니다.");
            return false;
        }

        _entries.RemoveAll(e => e.reason == NotePinReason.ManualPin);
        foreach (var clueId in board.manualPinClueIds)
        {
            if (IsPinned(clueId)) continue;
            _entries.Add(new NoteEntry { clueId = clueId, reason = NotePinReason.ManualPin });
        }
        OnNoteChanged?.Invoke();
        return true;
    }

    // SaveModule이 직접 Instance로 접근해 호출 — noteEntries와 동일한 패턴.
    public List<NoteSavedBoard> ExportSavedBoards() => new(_savedBoards);

    public void ImportSavedBoards(List<NoteSavedBoard> boards)
    {
        _savedBoards.Clear();
        if (boards != null) _savedBoards.AddRange(boards);
        OnSavedBoardsChanged?.Invoke();
    }

    // ─── 단서 연동(수동 "연관 단서" 간선) — NoteRouteGraphView "단서 연동 모드"가 사용 ────────
    // 자동 키워드 공유 간선(NoteRouteGraphView._colKeywordEdge)과 별개로, 사용자가 그래프에서 마우스로
    // 직접 이어둔 관계다. 정규화된(사전순) 쌍으로 저장해 (A,B)/(B,A)를 같은 연결로 취급한다.
    // [요청, 2026-07-21] 세이브 연동 완료 — noteEntries와 동일한 패턴으로 SaveModule이 ExportClueLinks/
    // ImportClueLinks를 직접 호출한다(아래 참고).
    private readonly HashSet<(string a, string b)> _clueLinks = new();
    public IEnumerable<(string a, string b)> ClueLinks => _clueLinks;

    private static (string, string) NormalizeLinkPair(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    // NoteRouteGraphView.HandleLinkModeClueClicked가 두 번째 단서를 클릭한 순간 호출한다.
    // true = 새로 연결됨, false = 있던 연결이 해제됨(같은 쌍을 다시 연동하면 토글).
    public bool ToggleClueLink(string clueIdA, string clueIdB)
    {
        if (string.IsNullOrEmpty(clueIdA) || string.IsNullOrEmpty(clueIdB) || clueIdA == clueIdB) return false;
        if (!CanEdit)
        {
            Debug.LogWarning("[NoteModule] 이동 중에는 단서를 연동할 수 없습니다.");
            return false;
        }

        var pair = NormalizeLinkPair(clueIdA, clueIdB);
        bool linked = !_clueLinks.Remove(pair);
        if (linked) _clueLinks.Add(pair);
        OnNoteChanged?.Invoke();
        return linked;
    }

    public bool AreCluesLinked(string clueIdA, string clueIdB) =>
        _clueLinks.Contains(NormalizeLinkPair(clueIdA, clueIdB));

    // [요청, 2026-07-21] SaveModule이 직접 Instance로 접근해 호출 — noteEntries/noteSavedBoards와
    // 동일한 패턴. ValueTuple은 JsonUtility가 못 다뤄서 NoteClueLink(평범한 클래스)로 변환해 오간다.
    public List<NoteClueLink> ExportClueLinks() =>
        _clueLinks.Select(p => new NoteClueLink { clueIdA = p.a, clueIdB = p.b }).ToList();

    public void ImportClueLinks(List<NoteClueLink> links)
    {
        _clueLinks.Clear();
        if (links != null)
        {
            foreach (var link in links)
            {
                if (string.IsNullOrEmpty(link.clueIdA) || string.IsNullOrEmpty(link.clueIdB)) continue;
                _clueLinks.Add(NormalizeLinkPair(link.clueIdA, link.clueIdB));
            }
        }
        OnNoteChanged?.Invoke();
    }
}
