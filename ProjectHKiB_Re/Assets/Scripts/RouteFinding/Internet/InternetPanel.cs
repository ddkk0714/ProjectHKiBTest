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

            foreach (var site in module.Sites)
            {
                if (site == null) continue;

                bool unlocked = module.IsSiteUnlocked(site);
                PopulateSiteRow(_siteRowPool.Get(_listContentRT), site, unlocked, module);

                if (!unlocked || !_expandedSiteIds.Contains(site.id) || site.posts == null) continue;

                foreach (var post in site.posts)
                {
                    // 잠긴 게시글은 회색으로도 보여주지 않는다 — 제목 자체가 스포일러가 될 수 있다.
                    if (post == null || !module.IsPostUnlocked(site, post)) continue;
                    PopulatePostRow(_postRowPool.Get(_listContentRT), post, module);
                }
            }

            _siteRowPool.EndPass();
            _postRowPool.EndPass();
        }

        private void PopulateSiteRow(GameObject row, InternetSite site, bool unlocked, InternetModule module)
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
                    bool expanded = _expandedSiteIds.Contains(site.id);
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
                btn.interactable = unlocked;
                if (unlocked)
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
                var captured = post;
                btn.onClick.AddListener(() => SelectPost(captured));
            }
        }

        // 게시글 선택 = 열람 = 획득(1차 규칙). OpenPost가 읽음 처리와 단서 획득을 함께 하고,
        // 새로 얻은 단서만 돌려주므로 그걸 그대로 본문 뷰에 넘겨 "(획득!)" 표시에 쓴다.
        private void SelectPost(InternetPost post)
        {
            _selectedPostId = post.id;

            var acquired = InternetModule.Instance.OpenPost(post);
            _postView?.ShowPost(post, acquired);

            // OpenPost가 상태를 바꿨다면 OnInternetChanged로 이미 목록이 다시 그려졌지만,
            // 이미 읽은 글을 다시 누른 경우엔 이벤트가 없다 — 선택 하이라이트를 위해 한 번 더 그린다.
            RefreshList();
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
        // 재사용 판정은 "가장 최근에 추가된 요소가 있는가"로 한다 — 지금은 SiteList가 그 기준이다.

        private void BuildUI()
        {
            var existing = transform.Find("InternetPanelRoot");
            if (existing != null)
            {
                if (FindDeepTransform(existing, "SiteList") != null)
                {
                    Debug.Log("[InternetPanel] BuildUI: 씬에 있던 기존 InternetPanelRoot를 재사용합니다.");
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.Log("[InternetPanel] BuildUI: 기존 InternetPanelRoot에 SiteList가 없어 구버전으로 판단, 파괴 후 재생성합니다.");
                // 활성 상태의 TMP를 그냥 Destroy하면 같은 프레임의 레이아웃 리빌드가 파괴 중인
                // 서브메시를 건드릴 수 있다 — 먼저 끈다(CodexPanel과 같은 이유).
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            if (_panelPrefab != null)
            {
                if (FindDeepTransform(_panelPrefab.transform, "SiteList") != null)
                {
                    Debug.Log($"[InternetPanel] BuildUI: 지정된 프리팹({_panelPrefab.name})을 인스턴스화합니다.");
                    _panelGO = Instantiate(_panelPrefab, transform, false);
                    _panelGO.name = "InternetPanelRoot";
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.LogWarning($"[InternetPanel] BuildUI: 지정된 프리팹({_panelPrefab.name})에 SiteList가 없어 구버전으로 판단, 런타임 생성으로 대체합니다.");
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

            var closeTF = FindDeepTransform(root, "BtnClose");
            closeTF?.GetComponent<Button>()?.onClick.AddListener(Close);
            // 프리팹에 구워진 라벨은 만들 당시의 키 표기 그대로다 — 여기서 참조만 잡아두고
            // 실제 텍스트는 창을 열 때 RefreshCloseLabel()이 현재 바인딩으로 다시 채운다.
            _closeLabelTmp = closeTF != null ? closeTF.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
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

            var closeBtnRT = NewRect(topBar, "BtnClose");
            var closeLe = closeBtnRT.gameObject.AddComponent<LayoutElement>();
            closeLe.preferredWidth = 34f;
            closeLe.flexibleWidth = 0f;
            var closeBtn = closeBtnRT.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = AddImg(closeBtnRT, new Color(0.42f, 0.10f, 0.10f));
            closeBtn.transition = Selectable.Transition.None;
            closeBtn.onClick.AddListener(Close);

            var closeTxtRT = NewRect(closeBtnRT, "Text");
            StretchFull(closeTxtRT);
            var closeTmp = closeTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) closeTmp.font = _font;
            closeTmp.text = $"닫기 [{ToggleKeyLabel}]";
            closeTmp.fontSize = 7f;
            closeTmp.alignment = TextAlignmentOptions.Center;
            closeTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            closeTmp.color = Color.white;
            _closeLabelTmp = closeTmp;
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
