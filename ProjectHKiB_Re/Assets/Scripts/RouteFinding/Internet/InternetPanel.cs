using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

namespace RouteFinding.Internet
{
    // 인터넷 창 — 맵을 밟지 않고도 단서를 얻는 두 번째 경로(Internet_System_Plan.md).
    // 지도/도감/노트와 완전히 같은 창 패턴이다: 이 GO 자체는 항상 활성이고 내부 패널(_panelGO)만
    // 토글되며, 같은 GO의 Window가 IWindowContent 구현을 찾아 여닫기를 위임한다. 창 관리(ESC 닫기,
    // 겹쳐 열기, 게임 시간 정지)는 UIManager의 창 스택이 담당한다.
    //
    // 좌측: 사이트 → 게시글 목록. 잠긴 사이트는 회색 "??? (잠김)"으로만 보이고(무엇이 잠겼는지는
    //       노출하지 않는다), 잠긴 게시글은 아예 목록에 나오지 않는다 — 스포일러 방지.
    // 우측: 선택한 게시글의 본문(InternetPostView).
    //
    // "열람 = 획득"이 1차 규칙이라(기획서 3.5장) 게시글을 클릭하는 순간 InternetModule.OpenPost가
    // 단서를 획득시킨다. 그 뒤의 파급(도감 등록·지도 공개)은 기존 단서 배관이 처리하므로 이 패널은
    // 도감도 지도도 알 필요가 없다.
    public class InternetPanel : MonoBehaviour, IWindowContent
    {
        [Header("폰트")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("레이아웃")]
        [SerializeField] private float _drawerWidth = 220f;
        [SerializeField] private float _topBarHeight = 12f;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(패널 전체 바깥 배경)")]
        [SerializeField] private Color _rootBgColor = new(0.04f, 0.05f, 0.09f, 0.96f);
        [Tooltip("패널 전체 바깥 배경 이미지 — 지정하면 도트풍 이미지로 대체, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _rootBgSprite;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(상단바 + 좌측 목록 영역이 같이 씀)")]
        [SerializeField] private Color _drawerBgColor = new(0.07f, 0.09f, 0.14f, 0.97f);
        [Tooltip("상단바 + 좌측 목록 배경 이미지 — 두 영역이 같이 씀, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _drawerBgSprite;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(우측 본문 영역)")]
        [SerializeField] private Color _cardBgColor = new(0.06f, 0.07f, 0.11f, 0.90f);
        [Tooltip("우측 본문 배경 이미지 — 지정하면 도트풍 이미지로 대체, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _cardBgSprite;

        [Header("목록 스타일")]
        [SerializeField] private Color _colSiteRow = new(0.14f, 0.17f, 0.24f);
        [SerializeField] private Color _colPostRow = new(0.10f, 0.12f, 0.17f);
        [SerializeField] private Color _colSelected = new(0.25f, 0.43f, 0.78f, 0.55f);
        [SerializeField] private Color _colLocked = new(0.40f, 0.42f, 0.46f);
        [SerializeField] private float _rowFontSize = 7f;
        [SerializeField] private float _siteRowHeight = 14f;
        [SerializeField] private float _postRowHeight = 12f;

        [Header("행 템플릿 (선택 — 비워두면 위 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _siteRowTemplate;
        [SerializeField] private GameObject _postRowTemplate;

        // [신설, 2026-08-16 — 기획서 4단계] 상단바 검색(기획서 4장 화면 구성의 "주소창/검색어" 자리).
        // 주소를 직접 치는 브라우저 흉내가 아니라 "지금 가진 글 안에서 찾는" 검색이다 — 아직 발견하지
        // 못한 사이트/게시글은 검색으로도 나오지 않는다(잠금 평가를 그대로 통과시킨다). 스포일러 방지.
        // 상단바 요소도 목록 스타일과 같은 수준으로 인스펙터에서 손볼 수 있게 열어둔다 — 프리팹으로
        // 구워 직접 편집하는 길과 별개로, 색만 바꾸고 싶을 때 프리팹을 열 필요가 없게.
        [Header("상단바")]
        [SerializeField] private Color _navButtonColor = new(0.20f, 0.28f, 0.42f);
        [SerializeField] private Color _closeButtonColor = new(0.42f, 0.10f, 0.10f);
        [SerializeField] private float _navButtonWidth = 26f;
        [SerializeField] private float _topBarFontSize = 7f;

        [Header("검색")]
        [SerializeField] private float _searchFieldWidth = 110f;
        [SerializeField] private Color _searchFieldColor = new(0.03f, 0.04f, 0.06f);
        [SerializeField] private string _searchPlaceholder = "검색";

        // [신설, 2026-08-16 — 기획서 4단계 "접속 연출"] 게시글을 고르면 본문이 즉시 바뀌는 대신 짧은
        // "접속 중" 화면을 거친다. 게임이 멈춘 창(pausesGame) 위에서 도는 연출이라 반드시 실시간
        // (unscaled)으로 센다 — Time.timeScale이 0이라 WaitForSeconds는 영원히 안 끝난다.
        [Header("접속 연출")]
        [Tooltip("게시글을 열 때 '접속 중' 화면을 보여주는 시간(초, 실시간). 0이면 연출 없이 즉시 표시")]
        [SerializeField] private float _connectDuration = 0.35f;
        [SerializeField] private Color _connectOverlayColor = new(0.03f, 0.04f, 0.07f, 0.96f);
        [SerializeField] private Color _connectTextColor = new(0.55f, 0.95f, 0.65f);

        [Header("프리팹 (선택 — 비워두면 런타임 자동 생성)")]
        [SerializeField] private GameObject _panelPrefab;

        private GameObject _panelGO;
        private RectTransform _listContentRT;
        private ScrollRect _listScroll;
        private InternetPostView _postView;
        private TextMeshProUGUI _closeLabelTmp;
        private InputManager _inputManager;

        private UiRowPool _siteRowPool;
        private UiRowPool _postRowPool;

        // 펼쳐 놓은 사이트. 처음 열 때는 잠기지 않은 사이트를 전부 펼쳐 둔다(RefreshList의 첫 호출) —
        // 사이트가 몇 개 안 되는 1차 콘텐츠 규모에서는 접혀 있는 편이 오히려 불편하다.
        private readonly HashSet<string> _expandedSiteIds = new();
        private bool _expandedInitialized;
        private string _selectedPostId = "";

        private TMP_InputField _searchField;
        private string _searchQuery = "";

        private GameObject _connectOverlayGO;
        private TextMeshProUGUI _connectTmp;
        private Coroutine _connectRoutine;
        private InternetPost _pendingPost;              // 연출이 끝나면 보여줄 글(중단 시 즉시 표시용)
        private List<ClueData> _pendingAcquired;

        private void Awake()
        {
            // 닫기 버튼 라벨(ToggleKeyLabel)이 실제 바인딩 키를 쓰려면 BuildUI보다 먼저 잡아야 한다.
            _inputManager = FindObjectOfType<InputManager>();

            var rt = GetComponent<RectTransform>();
            if (rt != null) StretchFull(rt);
            BuildUI();

            // UI_TOGGLE 액션맵은 모드 전환과 무관하게 항상 켜져 있다(MapViewer/CodexPanel과 동일).
            if (_inputManager != null) _inputManager.onOpenInternet += HandleOpenInternetInput;
        }

        private void Start()
        {
            InternetModule.Instance.OnInternetChanged += RefreshList;
            RefreshList();
            _panelGO.SetActive(false);
        }

        private void OnDestroy()
        {
            if (InternetModule.Instance != null) InternetModule.Instance.OnInternetChanged -= RefreshList;
            if (_inputManager != null) _inputManager.onOpenInternet -= HandleOpenInternetInput;
        }

        private void HandleOpenInternetInput(InputAction.CallbackContext context)
        {
            if (context.performed) Toggle();
        }

        // "닫기 [Q]" 라벨용. _inputManager를 찾았더라도 그쪽 Awake가 아직 안 돌았으면 inputs가 null이다
        // (스크립트 실행 순서는 보장되지 않는다) — 이 패널은 Awake에서 UI를 만들면서 이 값을 읽으므로
        // 실제로 그 상황을 밟는다. 그때는 기본 키 표기로 대체하고, 창을 열 때 RefreshCloseLabel()이
        // 실제 바인딩으로 다시 채운다.
        private string ToggleKeyLabel
        {
            get
            {
                var inputs = _inputManager != null ? _inputManager.inputs : null;
                var action = inputs?.UI_TOGGLE.OpenInternet;
                return action != null ? action.GetBindingDisplayString() : "Q";
            }
        }

        // 위 주석 참고 — 패널을 만든 시점에 바인딩을 못 읽었을 수 있으므로 열 때마다 한 번 맞춘다.
        private void RefreshCloseLabel()
        {
            if (_closeLabelTmp != null) _closeLabelTmp.text = $"닫기 [{ToggleKeyLabel}]";
        }

        // ─── Public API ──────────────────────────────────────────

        // UIManager.windows에 등록할 이름 — 씬의 GO 이름(InternetWindow)과 달라도 되지만,
        // 이 상수와 UIManager 리스트의 name이 한 글자라도 어긋나면 창이 조용히 안 열린다.
        public const string WindowName = "Internet";

        public void Open() => UI?.OpenWindow(WindowName);
        public void Close() => UI?.CloseWindow(WindowName);
        public void Toggle() => UI?.ToggleWindow(WindowName);

        private static UIManager UI => GameManager.instance == null ? null : GameManager.instance.UIManager;

        // ─── IWindowContent ──────────────────────────────────────

        // 어디서든 열 수 있지만 이동 중에는 못 연다 — 지도/도감과 같은 규칙(기획서 8장 b 기본안).
        public bool CanOpenWindow
        {
            get
            {
                if (RouteModule.Instance != null && !RouteModule.Instance.CanOpenMap)
                {
                    Debug.LogWarning("[InternetPanel] 이동 중에는 인터넷을 열 수 없습니다.");
                    return false;
                }
                return true;
            }
        }

        public void OpenWindowContent()
        {
            // 창이 닫혀 있는 동안 단서를 얻거나 시간이 흘러 새로 열린 사이트/게시글이 있을 수 있다.
            RefreshList();
            RefreshCloseLabel();
            _panelGO.SetActive(true);
            if (_listScroll != null) _listScroll.verticalNormalizedPosition = 1f;
            _inputManager?.MENUMode();
        }

        public void CloseWindowContent()
        {
            // 첨부 소리를 재생하던 중이면 명시적으로 멈춘다(CodexPanel과 같은 이유 — 그쪽 주석 참고).
            _postView?.StopAudio();
            // 접속 연출 도중에 닫혔다면 보여주려던 글을 지금 확정해둔다(StopConnectRoutine 주석 참고).
            StopConnectRoutine(applyPending: true);
            _panelGO.SetActive(false);
            _inputManager?.PLAYMode();
        }

        // Editor 스크립트에서 프리팹 저장 시 접근.
        public GameObject GetPanelGO() => _panelGO;

        // ─── 목록 갱신 ───────────────────────────────────────────

        private void RefreshList()
        {
            if (_listContentRT == null) return;
            EnsurePools();

            var module = InternetModule.Instance;
            if (module == null) return;

            if (!_expandedInitialized)
            {
                foreach (var site in module.Sites)
                    if (module.IsSiteUnlocked(site)) _expandedSiteIds.Add(site.id);
                _expandedInitialized = true;
            }

            bool searching = !string.IsNullOrWhiteSpace(_searchQuery);
            int shownPosts = 0;

            foreach (var site in module.Sites)
            {
                if (site == null) continue;

                bool unlocked = module.IsSiteUnlocked(site);

                if (searching)
                {
                    // 검색 중에는 잠긴 사이트를 아예 뺀다 — 결과 사이에 낀 "??? (잠김)" 행은 노이즈일 뿐
                    // 누를 수도 없다. 그리고 접힘 상태를 무시하고 항상 펼쳐서 보여준다: 검색은 "찾은 것을
                    // 바로 보여주는" 동작인데 한 번 더 펼치게 하면 결과를 못 찾은 것처럼 보인다.
                    if (!unlocked) continue;

                    var matches = CollectMatchingPosts(site, module);
                    if (matches.Count == 0) continue;

                    PopulateSiteRow(_siteRowPool.Get(_listContentRT), site, true, module, searching: true);
                    foreach (var post in matches)
                    {
                        PopulatePostRow(_postRowPool.Get(_listContentRT), post, module);
                        shownPosts++;
                    }
                    continue;
                }

                PopulateSiteRow(_siteRowPool.Get(_listContentRT), site, unlocked, module, searching: false);

                if (!unlocked || !_expandedSiteIds.Contains(site.id) || site.posts == null) continue;

                foreach (var post in site.posts)
                {
                    // 잠긴 게시글은 회색으로도 보여주지 않는다 — 제목 자체가 스포일러가 될 수 있다.
                    if (post == null || !module.IsPostUnlocked(site, post)) continue;
                    PopulatePostRow(_postRowPool.Get(_listContentRT), post, module);
                }
            }

            if (searching && shownPosts == 0)
                PopulateMessageRow(_postRowPool.Get(_listContentRT), "검색 결과가 없습니다.");

            _siteRowPool.EndPass();
            _postRowPool.EndPass();
        }

        // 지금 열람 가능한 게시글 중 검색어에 걸리는 것만. 잠금 평가를 그대로 통과시키므로 아직
        // 발견하지 못한 글은 검색으로도 새어 나오지 않는다.
        private List<InternetPost> CollectMatchingPosts(InternetSite site, InternetModule module)
        {
            var result = new List<InternetPost>();
            if (site.posts == null) return result;

            foreach (var post in site.posts)
            {
                if (post == null || !module.IsPostUnlocked(site, post)) continue;
                if (!PostMatches(post, _searchQuery)) continue;
                result.Add(post);
            }
            return result;
        }

        // 제목·본문·작성자·댓글을 부분 문자열(대소문자 무시)로 훑는다 — 도감 검색
        // (CodexFilterService.Search)과 같은 수준의 단순 매칭이고, 오타 허용 등은 범위 밖이다.
        // 댓글까지 대상에 넣은 이유: 인터넷 콘텐츠에서는 본문보다 댓글에 결정적인 한마디가 오는 일이
        // 많아, 댓글을 빼면 "분명히 읽었는데 검색으로 못 찾는" 글이 생긴다.
        private static bool PostMatches(InternetPost post, string query)
        {
            var q = query.Trim();
            if (Contains(post.title, q) || Contains(post.body, q) || Contains(post.author, q)) return true;

            if (post.comments != null)
                foreach (var c in post.comments)
                    if (c != null && (Contains(c.text, q) || Contains(c.author, q))) return true;

            return false;
        }

        private static bool Contains(string source, string query) =>
            !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        // 검색 결과가 없을 때만 쓰는 안내 행 — 게시글 행 템플릿을 그대로 빌려 쓰되 누를 수 없게 한다.
        private void PopulateMessageRow(GameObject row, string message)
        {
            var tmp = row.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = "  " + message;
                tmp.color = _colLocked;
            }

            var img = row.GetComponent<Image>();
            if (img != null) img.color = _colPostRow;

            var btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.interactable = false;
            }
        }

        private void PopulateSiteRow(GameObject row, InternetSite site, bool unlocked, InternetModule module, bool searching)
        {
            var tmp = row.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                if (!unlocked)
                {
                    tmp.text = "??? (잠김)";
                    tmp.color = _colLocked;
                }
                else
                {
                    bool expanded = searching || _expandedSiteIds.Contains(site.id);
                    int unread = module.UnreadCount(site);
                    string badge = unread > 0 ? $"  <color=#8FE3A0>({unread})</color>" : "";
                    tmp.text = $"{(expanded ? "▾" : "▸")} {site.name}{badge}";
                    tmp.color = Color.white;
                }
            }

            var icon = row.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                var sprite = unlocked && !string.IsNullOrWhiteSpace(site.iconAddress)
                    ? ClueAttachmentService.LoadSprite(site.iconAddress)
                    : null;
                icon.gameObject.SetActive(sprite != null);
                if (sprite != null)
                {
                    icon.sprite = sprite;
                    icon.color = Color.white;
                }
            }

            var btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                // 검색 중에는 사이트 행이 접기/펼치기 버튼이 아니라 결과를 묶는 머리글이다 — 눌러도
                // 항상 펼쳐진 채라 화면이 안 바뀌므로, 아예 못 누르게 해서 "눌렀는데 아무 일도 안 남"을 막는다.
                btn.interactable = unlocked && !searching;
                if (unlocked && !searching)
                {
                    string siteId = site.id;
                    btn.onClick.AddListener(() =>
                    {
                        if (!_expandedSiteIds.Remove(siteId)) _expandedSiteIds.Add(siteId);
                        RefreshList();
                    });
                }
            }
        }

        private void PopulatePostRow(GameObject row, InternetPost post, InternetModule module)
        {
            var tmp = row.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                bool isNew = !module.IsRead(post.id);
                tmp.text = isNew ? $"  · {post.title}  <color=#8FE3A0>NEW</color>" : $"  · {post.title}";
                tmp.color = isNew ? Color.white : new Color(0.78f, 0.80f, 0.84f);
            }

            var img = row.GetComponent<Image>();
            if (img != null) img.color = post.id == _selectedPostId ? _colSelected : _colPostRow;

            var btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                // 같은 풀에서 나온 행이 직전에 "검색 결과 없음" 안내로 쓰였다면 interactable이 꺼진 채다.
                btn.interactable = true;
                var captured = post;
                btn.onClick.AddListener(() => SelectPost(captured));
            }
        }

        // 게시글 선택 = 열람 = 획득(1차 규칙). OpenPost가 읽음 처리와 단서 획득을 함께 하고,
        // 새로 얻은 단서만 돌려주므로 그걸 그대로 본문 뷰에 넘겨 "(획득!)" 표시에 쓴다.
        private void SelectPost(InternetPost post)
        {
            _selectedPostId = post.id;

            // 획득은 연출과 무관하게 클릭 즉시 확정한다 — 연출 도중에 창을 닫아도 "글을 열었는데
            // 단서를 못 얻는" 일이 없어야 하기 때문(연출은 표시 지연일 뿐 게임 상태가 아니다).
            var acquired = InternetModule.Instance.OpenPost(post);
            ShowPostWithConnect(post, acquired);

            // OpenPost가 상태를 바꿨다면 OnInternetChanged로 이미 목록이 다시 그려졌지만,
            // 이미 읽은 글을 다시 누른 경우엔 이벤트가 없다 — 선택 하이라이트를 위해 한 번 더 그린다.
            RefreshList();
        }

        // ─── 접속 연출 (기획서 4단계) ─────────────────────────────
        // 게시글을 고르면 본문이 곧바로 바뀌는 대신 짧게 "접속 중" 화면을 거친다. 순수 연출이라
        // 게임 상태는 이미 SelectPost에서 확정돼 있고, 창을 닫거나 다른 글을 누르면 즉시 중단된다.

        private void ShowPostWithConnect(InternetPost post, List<ClueData> acquired)
        {
            // 진행 중이던 연출은 버린다(applyPending: false) — 바로 아래에서 새 글을 표시하므로
            // 직전 글을 한 번 그려봐야 같은 프레임에 덮인다(불필요한 레이아웃 재계산).
            StopConnectRoutine(applyPending: false);

            if (_connectDuration <= 0f || _connectOverlayGO == null || !isActiveAndEnabled)
            {
                _postView?.ShowPost(post, acquired);
                return;
            }

            _connectRoutine = StartCoroutine(ConnectRoutine(post, acquired));
        }

        private IEnumerator ConnectRoutine(InternetPost post, List<ClueData> acquired)
        {
            _pendingPost = post;
            _pendingAcquired = acquired;

            _connectOverlayGO.SetActive(true);
            _connectOverlayGO.transform.SetAsLastSibling();

            // 창이 열려 있는 동안 게임 시간은 멈춰 있다(Window.pausesGame) — Time.timeScale이 0이라
            // WaitForSeconds는 영원히 안 끝난다. 반드시 실시간(unscaled)으로 세야 한다.
            float elapsed = 0f;
            int step = 0;
            while (elapsed < _connectDuration)
            {
                if (_connectTmp != null)
                {
                    string dots = new string('.', step % 3 + 1);
                    _connectTmp.text = $"접속 중{dots}\n<size=70%>{GlitchLine(step)}</size>";
                }
                step++;
                yield return new WaitForSecondsRealtime(0.06f);
                elapsed += 0.06f;
            }

            _postView?.ShowPost(post, acquired);
            _connectOverlayGO.SetActive(false);
            _pendingPost = null;
            _pendingAcquired = null;
            _connectRoutine = null;
        }

        // 접속 중 흘러가는 노이즈 한 줄 — 별도 에셋 없이 글자만으로 만드는 연출이라 폰트(neodgm)에
        // 확실히 있는 ASCII 기호만 쓴다. 한글 폰트에 없는 문자를 쓰면 네모(tofu)로 뜬다.
        private static string GlitchLine(int step)
        {
            const string Charset = "01#/\\|-_*+=<>[]";
            var sb = new System.Text.StringBuilder(16);
            for (int i = 0; i < 16; i++)
                sb.Append(Charset[(step * 7 + i * 13) % Charset.Length]);
            return sb.ToString();
        }

        // applyPending: 연출이 끝나기 전에 중단됐을 때, 보여주려던 글을 곧바로(연출 없이) 표시할지.
        // 창을 닫는 경우엔 true여야 한다 — 아니면 다음에 열었을 때 목록 하이라이트는 새 글을 가리키는데
        // 본문만 이전 글로 남는다.
        private void StopConnectRoutine(bool applyPending)
        {
            if (_connectRoutine != null)
            {
                StopCoroutine(_connectRoutine);
                _connectRoutine = null;
            }

            if (applyPending && _pendingPost != null) _postView?.ShowPost(_pendingPost, _pendingAcquired);
            _pendingPost = null;
            _pendingAcquired = null;

            _connectOverlayGO?.SetActive(false);
        }

        // ─── 사이트/게시글 행 템플릿 ──────────────────────────────

        private void EnsurePools()
        {
            _siteRowPool ??= new UiRowPool(_siteRowTemplate, () => BuildRowTemplate("SiteRow", _siteRowHeight, _colSiteRow, true));
            _postRowPool ??= new UiRowPool(_postRowTemplate, () => BuildRowTemplate("PostRow", _postRowHeight, _colPostRow, false));
        }

        private GameObject BuildRowTemplate(string name, float height, Color bg, bool withIcon)
        {
            var rowRT = NewRect(null, name);
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var img = AddImg(rowRT, bg);
            var btn = rowRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var hlg = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(3, 3, 0, 0);
            hlg.spacing = 3f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            if (withIcon)
            {
                var iconRT = NewRect(rowRT, "Icon");
                var iconLe = iconRT.gameObject.AddComponent<LayoutElement>();
                iconLe.preferredWidth = height - 4f;
                iconLe.flexibleWidth = 0f;
                iconRT.gameObject.AddComponent<Image>().preserveAspect = true;
            }

            var textRT = NewRect(rowRT, "Text");
            textRT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            rowRT.gameObject.SetActive(false);
            return rowRT.gameObject;
        }

        // ─── UI 구축 ─────────────────────────────────────────────
        // CodexPanel.BuildUI와 같은 3갈래(씬에 있던 것 재사용 / 프리팹 인스턴스화 / 런타임 생성).
        // 재사용 판정 기준은 **패널의 골격**이 있는가다(좌측 목록 = SiteList). 그 안에 무엇이 더 붙었는지는
        // 여기서 따지지 않는다.
        //
        // [2026-08-16] 한 번 ConnectOverlay(4단계에서 추가된 요소)로 올렸다가 되돌렸다. 마커를 최신 요소로
        // 올리면 그 전에 구운 프리팹이 통째로 구버전 판정을 받아 파괴 후 재생성되는데, 그러면 프리팹에서
        // 손봐둔 배경·색·배치가 전부 날아간다 — UI를 프리팹으로 디자인하는 워크플로우 자체가 막힌다.
        // 대신 MapViewer가 쓰는 방식을 따른다: 껍데기는 프리팹 재사용을 허용하고, **그 프리팹에 아직 없는
        // 요소만 골라 제자리에 보강한다**(EnsureTopBarExtras / EnsureConnectOverlay,
        // MapViewer.RebuildSimpleDropdownInPlace·BuildGraphArea와 같은 성격).
        //
        // 그래서 앞으로 UI 요소를 추가할 때 손댈 곳은 이 상수가 아니라 아래 Ensure~ 계열이다.
        private const string CurrentUiMarker = "SiteList";

        private void BuildUI()
        {
            var existing = transform.Find("InternetPanelRoot");
            if (existing != null)
            {
                if (FindDeepTransform(existing, CurrentUiMarker) != null)
                {
                    Debug.Log("[InternetPanel] BuildUI: 씬에 있던 기존 InternetPanelRoot를 재사용합니다.");
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.Log($"[InternetPanel] BuildUI: 기존 InternetPanelRoot에 {CurrentUiMarker}가 없어 구버전으로 판단, 파괴 후 재생성합니다.");
                // 활성 상태의 TMP를 그냥 Destroy하면 같은 프레임의 레이아웃 리빌드가 파괴 중인
                // 서브메시를 건드릴 수 있다 — 먼저 끈다(CodexPanel과 같은 이유).
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            if (_panelPrefab != null)
            {
                if (FindDeepTransform(_panelPrefab.transform, CurrentUiMarker) != null)
                {
                    Debug.Log($"[InternetPanel] BuildUI: 지정된 프리팹({_panelPrefab.name})을 인스턴스화합니다.");
                    _panelGO = Instantiate(_panelPrefab, transform, false);
                    _panelGO.name = "InternetPanelRoot";
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.LogWarning($"[InternetPanel] BuildUI: 지정된 프리팹({_panelPrefab.name})에 {CurrentUiMarker}가 없어 구버전으로 판단, 런타임 생성으로 대체합니다. 프리팹을 다시 생성해주세요.");
            }

            Debug.Log(_panelPrefab == null
                ? "[InternetPanel] BuildUI: _panelPrefab이 비어 있어 런타임으로 새로 생성합니다."
                : "[InternetPanel] BuildUI: (위 경고 참고) 런타임으로 새로 생성합니다.");

            _panelGO = new GameObject("InternetPanelRoot");
            _panelGO.transform.SetParent(transform, false);
            var root = _panelGO.AddComponent<RectTransform>();
            StretchFull(root);
            PanelBackground.Apply(root, _rootBgColor, _rootBgSprite);

            BuildTopBar(root);
            BuildDrawer(root);
            BuildCard(root);
        }

        private void FinalizePanel(RectTransform rt)
        {
            if (rt != null) StretchFull(rt);
            BindRefsFromHierarchy(rt);
        }

        // Instantiate는 private 필드 값과 런타임에 AddListener한 콜백을 보존하지 않으므로 전부 다시 연결한다.
        private void BindRefsFromHierarchy(RectTransform root)
        {
            PanelBackground.Apply(root, _rootBgColor, _rootBgSprite);
            PanelBackground.Apply(FindDeepTransform(root, "TopBar") as RectTransform, _drawerBgColor, _drawerBgSprite);
            PanelBackground.Apply(FindDeepTransform(root, "Drawer") as RectTransform, _drawerBgColor, _drawerBgSprite);
            PanelBackground.Apply(FindDeepTransform(root, "Card") as RectTransform, _cardBgColor, _cardBgSprite);

            var listTF = FindDeepTransform(root, "SiteList");
            _listScroll = listTF != null ? listTF.GetComponent<ScrollRect>() : null;
            _listContentRT = FindDeepTransform(listTF, "Content") as RectTransform;

            var cardTF = FindDeepTransform(root, "Card");
            _postView = cardTF != null ? cardTF.GetComponent<InternetPostView>() : null;
            if (_postView != null)
            {
                _postView.Bind((RectTransform)cardTF);
                _postView.OnMapRefClicked += HandleMapRefClicked;
            }

            // ── (1) 프리팹에 이미 있는 요소: 참조를 잡고 콜백만 다시 잇는다.
            //        Instantiate는 런타임에 AddListener한 콜백을 보존하지 않는다.
            var searchTF = FindDeepTransform(root, "SearchField");
            _searchField = searchTF != null ? searchTF.GetComponent<TMP_InputField>() : null;
            if (_searchField != null)
            {
                // 프리팹에 구워진 검색어가 남아 있으면 목록이 처음부터 걸러진 채로 보인다.
                _searchField.SetTextWithoutNotify("");
                _searchQuery = "";
                _searchField.onValueChanged.AddListener(HandleSearchChanged);
            }

            var overlayTF = FindDeepTransform(root, "ConnectOverlay");
            if (overlayTF != null)
            {
                _connectOverlayGO = overlayTF.gameObject;
                _connectTmp = overlayTF.Find("Text")?.GetComponent<TextMeshProUGUI>();
                // 프리팹을 구울 때 켜져 있었을 수 있다 — 항상 꺼진 상태에서 시작한다.
                _connectOverlayGO.SetActive(false);
            }

            FindDeepTransform(root, "BtnGoToMap")?.GetComponent<Button>()?.onClick.AddListener(GoToMap);
            FindDeepTransform(root, "BtnGoToCodex")?.GetComponent<Button>()?.onClick.AddListener(GoToCodex);
            FindDeepTransform(root, "BtnGoToNote")?.GetComponent<Button>()?.onClick.AddListener(GoToNote);

            var closeTF = FindDeepTransform(root, "BtnClose");
            closeTF?.GetComponent<Button>()?.onClick.AddListener(Close);
            // 프리팹에 구워진 라벨은 만들 당시의 키 표기 그대로다 — 여기서 참조만 잡아두고
            // 실제 텍스트는 창을 열 때 RefreshCloseLabel()이 현재 바인딩으로 다시 채운다.
            _closeLabelTmp = closeTF != null ? closeTF.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;

            // ── (2) 그 프리팹에 아직 없는 요소만 제자리에 보강한다. 반드시 (1) 다음이어야 한다 —
            //        보강 경로는 만들면서 콜백까지 같이 달기 때문에, 순서가 바뀌면 (1)이 같은 콜백을
            //        한 번 더 달아 버튼 한 번 클릭에 두 번 반응하게 된다.
            EnsureTopBarExtras(FindDeepTransform(root, "TopBar") as RectTransform);
            EnsureConnectOverlay(FindDeepTransform(root, "Card") as RectTransform);
        }

        // ─── 프리팹 보강 — "없는 것만 제자리에 만들어 넣기" ────────────
        // 예전에 구운 프리팹에 새 UI 요소가 없다고 패널 전체를 구버전으로 몰면, 프리팹에서 손봐둔
        // 디자인이 통째로 날아간다. 그래서 껍데기(배경·배치)는 프리팹 것을 그대로 쓰고 빠진 요소만
        // 채워 넣는다 — MapViewer가 GraphArea/드롭다운에 쓰는 방식과 같다.
        //
        // 앞으로 상단바에 요소를 추가할 때는 BuildTopBar와 여기 두 곳에 같이 넣어야 한다.
        // (BuildTopBar = 맨 처음부터 만드는 길, 여기 = 이미 구운 프리팹을 따라잡는 길)

        private void EnsureTopBarExtras(RectTransform topBar)
        {
            if (topBar == null) return;

            if (topBar.Find("SearchField") == null) BuildSearchField(topBar);
            if (topBar.Find("BtnGoToMap") == null)
                MakeTopBarButton(topBar, "BtnGoToMap", "지도", _navButtonWidth, _navButtonColor, GoToMap);
            if (topBar.Find("BtnGoToCodex") == null)
                MakeTopBarButton(topBar, "BtnGoToCodex", "도감", _navButtonWidth, _navButtonColor, GoToCodex);
            if (topBar.Find("BtnGoToNote") == null)
                MakeTopBarButton(topBar, "BtnGoToNote", "노트", _navButtonWidth, _navButtonColor, GoToNote);

            // 새로 만든 것은 목록 끝에 붙으므로 닫기 버튼이 가운데 끼어 버린다 — 닫기는 항상 오른쪽 끝.
            // (그 앞 요소들끼리의 순서는 프리팹에서 바꿔둔 대로 존중한다.)
            topBar.Find("BtnClose")?.SetAsLastSibling();
        }

        private void EnsureConnectOverlay(RectTransform card)
        {
            if (card == null || _connectOverlayGO != null) return;
            BuildConnectOverlay(card);
        }

        private void BuildTopBar(RectTransform root)
        {
            var topBar = NewRect(root, "TopBar");
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = Vector2.one;
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, _topBarHeight);
            topBar.anchoredPosition = Vector2.zero;
            PanelBackground.Apply(topBar, _drawerBgColor, _drawerBgSprite);

            var hlg = topBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 3, 3);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var titleRT = NewRect(topBar, "Title");
            titleRT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var titleTmp = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) titleTmp.font = _font;
            titleTmp.text = "인터넷";
            titleTmp.fontSize = 8f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;

            BuildSearchField(topBar);

            // 상호 이동 — 인터넷에서 알아낸 것을 곧바로 지도/도감/노트에서 이어보게 한다(기획서 4단계).
            // MapViewer.GoToNote와 같은 방식으로 상대 패널을 필드로 들지 않고 씬에서 찾는다 —
            // 인터넷은 그 셋의 존재를 몰라도 되게 유지한다.
            MakeTopBarButton(topBar, "BtnGoToMap", "지도", _navButtonWidth, _navButtonColor, GoToMap);
            MakeTopBarButton(topBar, "BtnGoToCodex", "도감", _navButtonWidth, _navButtonColor, GoToCodex);
            MakeTopBarButton(topBar, "BtnGoToNote", "노트", _navButtonWidth, _navButtonColor, GoToNote);

            var closeBtnRT = MakeTopBarButton(topBar, "BtnClose", $"닫기 [{ToggleKeyLabel}]", 34f,
                _closeButtonColor, Close);
            _closeLabelTmp = closeBtnRT.Find("Text")?.GetComponent<TextMeshProUGUI>();
        }

        private RectTransform MakeTopBarButton(RectTransform parent, string id, string label, float width, Color color, Action onClick)
        {
            var rt = NewRect(parent, id);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0f;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = AddImg(rt, color);
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = label;
            tmp.fontSize = _topBarFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;

            return rt;
        }

        // 기획서 4장의 "주소창" 자리에 놓이는 검색창. NoteBoardWindow.BuildSaveRow와 같은
        // TMP_InputField 구성(뷰포트 + 본문 + 플레이스홀더)이다.
        private void BuildSearchField(RectTransform parent)
        {
            var fieldRT = NewRect(parent, "SearchField");
            var le = fieldRT.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = _searchFieldWidth;
            le.flexibleWidth = 0f;
            AddImg(fieldRT, _searchFieldColor);

            var textAreaRT = NewRect(fieldRT, "TextArea");
            StretchFull(textAreaRT);
            textAreaRT.offsetMin = new Vector2(4f, 1f);
            textAreaRT.offsetMax = new Vector2(-4f, -1f);
            textAreaRT.gameObject.AddComponent<RectMask2D>();

            var textRT = NewRect(textAreaRT, "Text");
            StretchFull(textRT);
            var textTmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) textTmp.font = _font;
            textTmp.fontSize = _topBarFontSize;
            textTmp.color = Color.white;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholderRT = NewRect(textAreaRT, "Placeholder");
            StretchFull(placeholderRT);
            var placeholderTmp = placeholderRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) placeholderTmp.font = _font;
            placeholderTmp.text = _searchPlaceholder;
            placeholderTmp.fontSize = _topBarFontSize;
            placeholderTmp.fontStyle = FontStyles.Italic;
            placeholderTmp.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;

            _searchField = fieldRT.gameObject.AddComponent<TMP_InputField>();
            _searchField.textViewport = textAreaRT;
            _searchField.textComponent = textTmp;
            _searchField.placeholder = placeholderTmp;
            _searchField.onValueChanged.AddListener(HandleSearchChanged);
        }

        private void HandleSearchChanged(string value)
        {
            _searchQuery = value ?? "";
            RefreshList();
        }

        // ─── 상호 이동 (지도 / 도감 / 노트) ───────────────────────

        private void GoToMap()
        {
            var mapViewer = FindObjectOfType<MapView.MapViewer>();
            if (mapViewer == null)
            {
                Debug.LogWarning("[InternetPanel] 씬에서 MapViewer를 찾을 수 없습니다.");
                return;
            }
            Close();
            mapViewer.Open();
        }

        private void GoToCodex()
        {
            var codexPanel = FindObjectOfType<Codex.CodexPanel>();
            if (codexPanel == null)
            {
                Debug.LogWarning("[InternetPanel] 씬에서 CodexPanel을 찾을 수 없습니다.");
                return;
            }
            Close();
            codexPanel.Open();
        }

        private void GoToNote()
        {
            var notePanel = FindObjectOfType<Note.NotePanel>();
            if (notePanel == null)
            {
                Debug.LogWarning("[InternetPanel] 씬에서 NotePanel을 찾을 수 없습니다.");
                return;
            }
            Close();
            notePanel.Open();
        }

        private void BuildDrawer(RectTransform root)
        {
            var drawer = NewRect(root, "Drawer");
            drawer.anchorMin = Vector2.zero;
            drawer.anchorMax = new Vector2(0f, 1f);
            drawer.pivot = new Vector2(0f, 0.5f);
            drawer.sizeDelta = new Vector2(_drawerWidth, -_topBarHeight);
            drawer.anchoredPosition = new Vector2(0f, -_topBarHeight * 0.5f);
            PanelBackground.Apply(drawer, _drawerBgColor, _drawerBgSprite);

            var listRT = NewRect(drawer, "SiteList");
            StretchFull(listRT);

            _listScroll = listRT.gameObject.AddComponent<ScrollRect>();
            _listScroll.horizontal = false;
            _listScroll.vertical = true;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScroll.scrollSensitivity = 8f;

            var vp = NewRect(listRT, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            _listScroll.viewport = vp;

            var content = NewRect(vp, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            _listScroll.content = content;
            _listContentRT = content;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            // 아래 여유 패딩은 도감 트리와 같은 이유 — 실제 필요 높이보다 계산이 짧게 나와도
            // 이 여유분 안에서 흡수되어 항상 맨 아래까지 스크롤된다.
            vlg.padding = new RectOffset(2, 2, 2, 200);
            vlg.spacing = 1f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        private void BuildCard(RectTransform root)
        {
            var card = NewRect(root, "Card");
            card.anchorMin = Vector2.zero;
            card.anchorMax = Vector2.one;
            card.offsetMin = new Vector2(_drawerWidth, 0f);
            card.offsetMax = new Vector2(0f, -_topBarHeight);
            PanelBackground.Apply(card, _cardBgColor, _cardBgSprite);

            _postView = card.gameObject.AddComponent<InternetPostView>();
            _postView.Init(card, _font);
            _postView.OnMapRefClicked += HandleMapRefClicked;

            BuildConnectOverlay(card);
        }

        // 본문 영역만 덮는 "접속 중" 오버레이 — 좌측 목록은 계속 보이고 누를 수 있어야 하므로
        // 패널 전체가 아니라 Card 안에 둔다. 배경 Image는 raycastTarget을 켠 채로 둬서(기본값)
        // 연출 도중 본문의 재생/지도 버튼이 눌리는 것을 막는다.
        private void BuildConnectOverlay(RectTransform card)
        {
            var overlay = NewRect(card, "ConnectOverlay");
            StretchFull(overlay);
            AddImg(overlay, _connectOverlayColor);

            var txtRT = NewRect(overlay, "Text");
            StretchFull(txtRT);
            _connectTmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) _connectTmp.font = _font;
            _connectTmp.text = "접속 중.";
            _connectTmp.fontSize = 8f;
            _connectTmp.color = _connectTextColor;
            _connectTmp.alignment = TextAlignmentOptions.Center;
            _connectTmp.verticalAlignment = VerticalAlignmentOptions.Middle;

            _connectOverlayGO = overlay.gameObject;
            _connectOverlayGO.SetActive(false);
        }

        // 본문의 맵 첨부에서 "지도"를 누르면 인터넷을 닫고 지도를 열어 그 맵으로 시점을 옮긴다 —
        // "인터넷에서 장소를 알아내고 지도가 열린다"의 마지막 한 걸음. CodexPanel.HandleMapRefClicked와
        // 같은 방식(상대 패널을 직접 참조하지 않고 씬에서 찾는다).
        private void HandleMapRefClicked(string mapGuid)
        {
            if (string.IsNullOrEmpty(mapGuid)) return;

            var mapViewer = FindObjectOfType<MapView.MapViewer>();
            if (mapViewer == null)
            {
                Debug.LogWarning("[InternetPanel] 씬에서 MapViewer를 찾을 수 없습니다.");
                return;
            }
            Close();
            mapViewer.OpenFocusedOn(mapGuid);
        }

        // ─── UI 헬퍼 ─────────────────────────────────────────────

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Image AddImg(RectTransform rt, Color col)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = col;
            return img;
        }

        private static Transform FindDeepTransform(Transform parent, string childName)
        {
            if (parent == null) return null;
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                var found = FindDeepTransform(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
