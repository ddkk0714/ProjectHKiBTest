using System;
using System.Collections.Generic;
using UnityEngine;

// 인터넷 시스템의 단일 소유자 — 정적 데이터(Resources/internet.json)와 진행 상태(읽은 게시글)를
// 함께 들고 있다. MapGraph(데이터)와 RouteProgressState(상태)가 나뉘어 있는 것과 달리 한 클래스로
// 합친 이유는, 인터넷 데이터가 "사이트 → 게시글" 2단 구조뿐이고 상태도 읽음 여부 하나라서
// 클래스를 둘로 나눌 만큼의 무게가 없기 때문이다. 커지면 그때 나눈다.
//
// CodexModule/NoteModule과 같은 자동 생성 싱글턴이다(MapGraph처럼 씬에 배치할 필요 없음).
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API]
//
// ▸ 접근: InternetModule.Instance
//
// ▸ 조회
//   Sites                      : 전체 사이트 목록(잠긴 것 포함 — 표시 여부는 IsSiteUnlocked로 판정)
//   GetSite(id) / GetPost(id)  : ID 조회
//   IsSiteUnlocked(site) / IsPostUnlocked(site, post) : 지금 접속/열람 가능한지(잠금 조건 평가)
//   IsRead(postId) / UnreadCount(site) : 읽음 상태 — 목록의 NEW 배지에 쓴다
//   OnInternetChanged          : 목록/읽음 상태가 바뀔 때 발행(UI 갱신 신호)
//
// ▸ 게시글 열람 — 인터넷 UI(InternetPanel)가 호출하는 유일한 상태 변경 훅
//     var newClues = InternetModule.Instance.OpenPost(post);
//   읽음 처리 + grantClueIds 획득(RouteProgressState.AcquireClueById 경유)을 한 번에 처리하고,
//   "이번 열람으로 새로 얻은 단서" 목록을 돌려준다(획득 연출용 — 이미 갖고 있던 단서는 빠진다).
//   단서 획득 이후의 파급(도감 등록·지도 공개·NEW 배지)은 기존 배관이 알아서 처리한다.
//
// ▸ 세이브: IEventSaveProvider 구현체다 — SaveModule이 "read_<postId>" 플래그로 읽음 상태만
//   저장한다. 획득한 단서는 RouteModule provider가 이미 저장하므로 여기서 중복 저장하지 않는다.
// ════════════════════════════════════════════════════════════════
public class InternetModule : MonoBehaviour, IEventSaveProvider
{
    private static InternetModule _instance;
    private static bool _isQuitting; // 종료 중 재생성 방지 — CodexModule과 같은 이유(그쪽 주석 참고)

    public static InternetModule Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying && !_isQuitting)
            {
                _instance = FindObjectOfType<InternetModule>();
                if (_instance == null)
                    _instance = new GameObject(nameof(InternetModule)).AddComponent<InternetModule>();
            }
            return _instance;
        }
    }

    private void OnApplicationQuit() => _isQuitting = true;

    // Resources 상대 경로. clues.json/map_database.json과 같은 폴더(Assets/Scripts/RouteFinding/Resources)에 둔다.
    [SerializeField] private string _databasePath = "internet";

    private InternetSite[] _sites = Array.Empty<InternetSite>();
    private readonly Dictionary<string, InternetSite> _siteById = new();
    private readonly Dictionary<string, InternetPost> _postById = new();

    // 읽은 게시글 — 세이브 왕복 대상("read_<postId>").
    private readonly HashSet<string> _readPostIds = new();

    public IReadOnlyList<InternetSite> Sites => _sites;

    // 사이트/게시글 목록이나 읽음 상태가 바뀌었으니 다시 그리라는 신호(CodexModule.OnCodexChanged와 같은 역할).
    public event Action OnInternetChanged;

    private bool _loaded;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[InternetModule] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
        EnsureLoaded();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ─── 데이터 로드 ─────────────────────────────────────────────

    // Awake보다 먼저 Instance로 접근당할 수 있어(자동 생성 싱글턴은 접근 시점에 만들어진다)
    // 조회 API마다 이 가드를 통과시킨다 — MapGraph처럼 "Awake에서만 로드"로 두면 씬 배치 순서에
    // 따라 빈 목록을 보게 된다.
    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var asset = Resources.Load<TextAsset>(_databasePath);
        if (asset == null)
        {
            Debug.LogWarning($"[InternetModule] 인터넷 데이터베이스를 찾을 수 없습니다: Resources/{_databasePath}");
            return;
        }

        var db = JsonUtility.FromJson<InternetDatabase>(asset.text);
        _sites = db?.sites ?? Array.Empty<InternetSite>();

        int postCount = 0;
        foreach (var site in _sites)
        {
            if (site == null || string.IsNullOrEmpty(site.id)) continue;
            _siteById[site.id] = site;
            if (site.posts == null) continue;

            foreach (var post in site.posts)
            {
                if (post == null || string.IsNullOrEmpty(post.id)) continue;
                if (_postById.ContainsKey(post.id))
                {
                    // ID가 겹치면 읽음 플래그("read_<postId>")도 같이 겹쳐 한쪽을 읽으면 다른 쪽도
                    // 읽은 것이 된다 — 조용히 덮어쓰지 않고 반드시 알린다.
                    Debug.LogWarning($"[InternetModule] 게시글 ID 중복: {post.id} — 뒤에 온 항목을 무시합니다.");
                    continue;
                }
                _postById[post.id] = post;
                postCount++;
            }
        }

        Debug.Log($"[InternetModule] 로드 완료 — 사이트 {_sites.Length}개, 게시글 {postCount}개");
    }

    public InternetSite GetSite(string siteId)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(siteId) && _siteById.TryGetValue(siteId, out var s) ? s : null;
    }

    public InternetPost GetPost(string postId)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(postId) && _postById.TryGetValue(postId, out var p) ? p : null;
    }

    // ─── 잠금 조건 평가 ──────────────────────────────────────────

    public bool IsSiteUnlocked(InternetSite site) => site != null && IsUnlocked(site.unlock);

    // 게시글은 자기 조건 + 소속 사이트 조건을 함께 만족해야 열람할 수 있다 —
    // 사이트가 잠겨 있는데 그 안의 글만 열리는 상태를 막는다.
    public bool IsPostUnlocked(InternetSite site, InternetPost post)
        => post != null && IsSiteUnlocked(site) && IsUnlocked(post.unlock);

    public bool IsUnlocked(InternetUnlockCondition cond)
    {
        if (cond == null || cond.IsEmpty) return true;

        var progress = RouteModule.Instance != null ? RouteModule.Instance.Progress : null;

        if (cond.requiredClueIds != null)
        {
            foreach (var clueId in cond.requiredClueIds)
            {
                if (string.IsNullOrEmpty(clueId)) continue;
                if (progress == null || !progress.IsClueAcquired(clueId)) return false;
            }
        }

        if (cond.requiredEventKeys != null)
        {
            foreach (var key in cond.requiredEventKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                // "mapGuid:eventKey" — RouteProgressState가 내부적으로 쓰는 것과 같은 합성 형식이라
                // 여기서 쪼개서 넘긴다. 콜론이 없으면 잘못 쓴 데이터이므로 잠긴 것으로 취급한다.
                int sep = key.IndexOf(':');
                if (sep <= 0 || sep == key.Length - 1)
                {
                    Debug.LogWarning($"[InternetModule] requiredEventKeys 형식 오류(\"mapGuid:eventKey\"여야 함): {key}");
                    return false;
                }
                if (progress == null || !progress.HasEventFlag(key.Substring(0, sep), key.Substring(sep + 1))) return false;
            }
        }

        if (cond.minGameTime > 0f)
        {
            var timeManager = GameManager.instance != null ? GameManager.instance.timeManager : null;
            if (timeManager == null || timeManager.GameTime < cond.minGameTime) return false;
        }

        return true;
    }

    // ─── 읽음 상태 ───────────────────────────────────────────────

    public bool IsRead(string postId) => !string.IsNullOrEmpty(postId) && _readPostIds.Contains(postId);

    // 목록의 "NEW" 배지용 — 지금 열람 가능한데 아직 안 읽은 게시글 수.
    public int UnreadCount(InternetSite site)
    {
        if (site?.posts == null) return 0;

        int count = 0;
        foreach (var post in site.posts)
            if (IsPostUnlocked(site, post) && !IsRead(post.id)) count++;
        return count;
    }

    // ─── 열람 = 획득 (기획서 3.5장 1차 규칙) ─────────────────────

    // 게시글을 연다. 읽음 처리 + grantClueIds 획득을 하고, 이번 열람으로 "새로" 얻은 단서만 돌려준다.
    // 이미 가진 단서(맵 방문으로 먼저 얻었을 수도 있다 — 기획서 9장 리스크)는 목록에서 빠지므로,
    // 호출자는 반환값이 비어 있으면 획득 연출을 건너뛰면 된다.
    public List<ClueData> OpenPost(InternetPost post)
    {
        var acquired = new List<ClueData>();
        if (post == null) return acquired;

        bool changed = _readPostIds.Add(post.id);

        var progress = RouteModule.Instance != null ? RouteModule.Instance.Progress : null;
        var graph = MapGraph.Instance;
        if (progress != null && post.grantClueIds != null)
        {
            foreach (var clueId in post.grantClueIds)
            {
                if (!progress.AcquireClueById(clueId)) continue;
                var clue = graph != null ? graph.GetClue(clueId) : null;
                if (clue != null) acquired.Add(clue);
                changed = true;
            }
        }

        if (changed) OnInternetChanged?.Invoke();
        return acquired;
    }

    // ─── 세이브 연동 (IEventSaveProvider) ────────────────────────
    // 읽음 상태만 담당한다. 획득한 단서는 RouteModule provider의 "clueacq_<clueId>"로 이미
    // 저장·복원되므로 여기서 다시 저장하면 같은 사실을 두 곳에 적어두는 꼴이 된다.

    public string ProviderId => "InternetModule";

    // 읽은 것만 내보낸다(false는 ResetForLoad 이후 기본값과 같아서 저장할 이유가 없다) —
    // 게시글이 수백 개가 돼도 세이브 파일이 안 읽은 글로 불어나지 않는다.
    public Dictionary<string, int> EventFlags
    {
        get
        {
            var dict = new Dictionary<string, int>();
            foreach (var postId in _readPostIds) dict["read_" + postId] = 1;
            return dict;
        }
    }

    public void SetEventFlag(string id, int value)
    {
        if (value == 0 || string.IsNullOrEmpty(id)) return;
        if (id.StartsWith("read_")) _readPostIds.Add(id.Substring("read_".Length));
    }

    // 인터넷은 "통로(맵 클리어)" 개념이 없다 — 빈 사전으로 참여만 한다.
    public Dictionary<string, bool> Passages => new();
    public void SetPassage(string id, bool opened) { }

    public void ResetForLoad()
    {
        _readPostIds.Clear();
        OnInternetChanged?.Invoke();
    }
}
