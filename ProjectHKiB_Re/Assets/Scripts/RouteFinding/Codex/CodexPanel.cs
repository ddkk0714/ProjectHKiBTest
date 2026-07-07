using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Codex
{
    // 단서 도감 — 완전히 별도의 풀스크린 패널 (지도 패널 안의 탭이 아님).
    // MapViewer와 동일한 패턴: Canvas 직속 자식 GO에 붙이고, 이 GO 자체는 항상 활성 —
    // C키 감지를 위해 Update가 계속 동작하며, 내부 패널(_panelGO)만 Open/Close 토글된다.
    //
    // 1단계: CodexModule(획득한 ClueData 목록의 소유자)을 구독해 실제 단서를 좌측 트리에 반영한다.
    // 2단계: CodexFilterService로 맵/출처/키워드 분류 + 검색을 지원한다.
    // 3단계: CodexUserEntry(유저 자유 메모) 추가/편집/삭제.
    // 코멘트/지도 연동은 이후 단계에서 추가.
    public class CodexPanel : MonoBehaviour
    {
        [Header("조작")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.C;

        [Header("폰트")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("레이아웃")]
        [SerializeField] private float _drawerWidth = 220f;
        [SerializeField] private float _topBarHeight = 24f;
        [SerializeField] private float _searchBarHeight = 44f;
        [SerializeField] private float _newMemoBtnHeight = 20f;
        [SerializeField] private Color _rootBgColor = new(0.04f, 0.04f, 0.08f, 0.96f);
        [SerializeField] private Color _drawerBgColor = new(0.07f, 0.09f, 0.14f, 0.97f);
        [SerializeField] private Color _cardBgColor = new(0.06f, 0.07f, 0.11f, 0.90f);

        [Header("프리팹 (선택 — 비워두면 런타임 자동 생성)")]
        [SerializeField] private GameObject _panelPrefab;

        private GameObject _panelGO;
        private CodexSearchBar _searchBar;
        private CodexDrawerTreeView _drawerView;
        private CodexCardView _cardView;
        private CodexMemoFormView _memoForm;
        private InputManager _inputManager;

        private readonly List<CodexEntry> _allEntries = new();
        private CodexFilterMode _filterMode = CodexFilterMode.ByMap;
        private string _searchQuery = "";

        private void Awake()
        {
            var rt = GetComponent<RectTransform>();
            if (rt != null) StretchFull(rt);
            BuildUI();
            _inputManager = FindObjectOfType<InputManager>();
        }

        private void Start()
        {
            CodexModule.Instance.OnCodexChanged += RefreshTree;
            RefreshTree();
            _panelGO.SetActive(false);
        }

        private void OnDestroy()
        {
            if (CodexModule.Instance != null) CodexModule.Instance.OnCodexChanged -= RefreshTree;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey)) Toggle();
        }

        // ─── Public API ──────────────────────────────────────────

        public void Open()
        {
            // 지도와 동일하게 이동 중에는 도감 열람도 막는다 (0단계 임시 결정 — CLAUDE.md 4-4 참고).
            if (RouteModule.Instance != null && !RouteModule.Instance.CanOpenMap)
            {
                Debug.LogWarning("[CodexPanel] 이동 중에는 도감을 열 수 없습니다.");
                return;
            }
            // 세이브 로드 직후처럼 OnClueAcquired 이벤트를 놓쳤을 경우를 대비해, 열 때마다 전체 재계산.
            CodexModule.Instance.RebuildFromProgress();
            _panelGO.SetActive(true);
            _inputManager?.MENUMode();
        }

        public void Close()
        {
            _panelGO.SetActive(false);
            _inputManager?.PLAYMode();
        }

        public void Toggle()
        {
            if (_panelGO.activeSelf) Close(); else Open();
        }

        // Editor 스크립트(CodexPanelEditor)에서 프리팹 저장 시 접근.
        public GameObject GetPanelGO() => _panelGO;

        // ─── 실제 데이터 연동 ─────────────────────────────────────

        private void RefreshTree()
        {
            _allEntries.Clear();
            foreach (var clue in CodexModule.Instance.AcquiredClues)
                _allEntries.Add(ToEntry(clue));
            foreach (var userEntry in CodexModule.Instance.UserEntries)
                _allEntries.Add(ToEntryFromUser(userEntry));
            ApplyFilter();
        }

        // 검색어 필터링 후, 현재 분류 기준(맵/출처/키워드)으로 그룹핑해서 트리에 반영한다.
        private void ApplyFilter()
        {
            var searched = CodexFilterService.Search(_allEntries, _searchQuery);
            var groups = _filterMode switch
            {
                CodexFilterMode.BySource  => CodexFilterService.GroupBySource(searched),
                CodexFilterMode.ByKeyword => CodexFilterService.GroupByKeyword(searched),
                _                         => CodexFilterService.GroupByMap(searched),
            };
            _drawerView.SetGroups(groups);
        }

        private static CodexEntry ToEntry(ClueData clue)
        {
            var mapNode = !string.IsNullOrEmpty(clue.codexMapGuid) ? MapGraph.Instance?.GetNode(clue.codexMapGuid) : null;
            return new CodexEntry
            {
                title       = clue.name,
                typeLabel   = ClueTypeConfig.GetDisplayName(clue.type),
                timestamp   = clue.timestamp,
                content     = string.IsNullOrEmpty(clue.content) ? clue.description : clue.content,
                source      = clue.source,
                mapCategory = mapNode != null ? mapNode.nodeName : "기타",
                keywords    = clue.keywords,
                userEntryGuid = "",
            };
        }

        // 유저 메모는 타입/시간/출처 개념이 없다 — typeLabel을 빈 문자열로 두면 카드 배지가 숨겨지고
        // CodexFilterService.GroupByKeyword의 자동 타입 키워드 병합도 자연히 건너뛴다(문서 1-3 요구사항).
        private static CodexEntry ToEntryFromUser(CodexUserEntry entry) => new()
        {
            title         = entry.title,
            typeLabel     = "",
            timestamp     = "",
            content       = entry.content,
            source        = "",
            mapCategory   = string.IsNullOrEmpty(entry.mapCategory) ? "기타" : entry.mapCategory,
            keywords      = entry.keywords,
            userEntryGuid = entry.guid,
        };

        // ─── UI 구축 ─────────────────────────────────────────────

        private void BuildUI()
        {
            // 씬 계층에 CodexPanelRoot가 이미 자식으로 배치돼 있으면 재사용.
            // MemoFormOverlay가 없으면 구버전(3단계 도입 이전) — 파괴 후 재생성. (MapViewer의 Toolbar 판정과 동일 패턴)
            var existing = transform.Find("CodexPanelRoot");
            if (existing != null)
            {
                bool existingCurrent = FindDeepTransform(existing, "MemoFormOverlay") != null;
                if (existingCurrent)
                {
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Destroy(existing.gameObject);
            }

            // 프리팹이 지정되어 있으면 인스턴스화 후 참조를 바인딩하고 콜백만 재연결.
            if (_panelPrefab != null)
            {
                bool prefabCurrent = FindDeepTransform(_panelPrefab.transform, "MemoFormOverlay") != null;
                if (prefabCurrent)
                {
                    _panelGO = Instantiate(_panelPrefab, transform, false);
                    _panelGO.name = "CodexPanelRoot";
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
            }

            // ── 프리팹 없음 → 런타임 자동 생성 ──
            _panelGO = new GameObject("CodexPanelRoot");
            _panelGO.transform.SetParent(transform, false);
            var root = _panelGO.AddComponent<RectTransform>();
            StretchFull(root);
            AddImg(root, _rootBgColor);

            BuildTopBar(root);
            BuildDrawer(root);
            BuildCard(root);
            BuildMemoForm(root);
        }

        // 기존/프리팹 패널을 재사용할 때의 공통 마무리: 전체 스트레치 + 참조 바인딩 + 콜백 재연결.
        // Instantiate는 private 필드 값과 런타임에 AddListener한 콜백을 보존하지 않으므로 전부 다시 연결해야 한다.
        private void FinalizePanel(RectTransform rt)
        {
            if (rt != null) StretchFull(rt);
            BindRefsFromHierarchy(rt);
        }

        private void BindRefsFromHierarchy(RectTransform root)
        {
            var searchAreaTF = FindDeepTransform(root, "SearchBarArea");
            _searchBar = searchAreaTF?.GetComponent<CodexSearchBar>();
            if (_searchBar != null)
            {
                _searchBar.Bind((RectTransform)searchAreaTF);
                _searchBar.OnSearchChanged += q => { _searchQuery = q; ApplyFilter(); };
                _searchBar.OnFilterModeChanged += m => { _filterMode = m; ApplyFilter(); };
            }

            FindDeepTransform(root, "NewMemoArea")?.GetComponent<Button>()?.onClick.AddListener(() => _memoForm.ShowForCreate());

            var treeScrollTF = FindDeepTransform(root, "TreeScroll");
            _drawerView = treeScrollTF?.GetComponent<CodexDrawerTreeView>();
            if (_drawerView != null)
            {
                var contentTF = FindDeepTransform(treeScrollTF, "Content");
                _drawerView.Init((RectTransform)contentTF, _font); // 위젯을 새로 만들지 않고 참조만 저장하므로 재호출해도 안전
                _drawerView.OnEntrySelected += OnEntrySelected;
            }

            var cardTF = FindDeepTransform(root, "Card");
            _cardView = cardTF?.GetComponent<CodexCardView>();
            if (_cardView != null)
            {
                _cardView.Bind((RectTransform)cardTF);
                _cardView.OnEditRequested += HandleEditRequested;
                _cardView.OnDeleteRequested += HandleDeleteRequested;
            }

            _memoForm = root.GetComponent<CodexMemoFormView>();
            if (_memoForm != null)
            {
                _memoForm.Bind(root);
                _memoForm.OnSaved += HandleMemoSaved;
                _memoForm.OnDeleteRequested += HandleMemoDeleteRequested;
            }

            FindDeepTransform(root, "BtnClose")?.GetComponent<Button>()?.onClick.AddListener(Close);
        }

        private void BuildTopBar(RectTransform root)
        {
            var topBar = NewRect(root, "TopBar");
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = Vector2.one;
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, _topBarHeight);
            topBar.anchoredPosition = Vector2.zero;
            AddImg(topBar, _drawerBgColor);

            var hlg = topBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 3, 3);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var titleRT = NewRect(topBar, "Title");
            var titleLe = titleRT.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;
            var titleTmp = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) titleTmp.font = _font;
            titleTmp.text = "단서 도감";
            titleTmp.fontSize = 12f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var closeBtnRT = NewRect(topBar, "BtnClose");
            var closeLe = closeBtnRT.gameObject.AddComponent<LayoutElement>();
            closeLe.preferredWidth = 60f;
            closeLe.flexibleWidth = 0f;
            var closeImg = AddImg(closeBtnRT, new Color(0.42f, 0.10f, 0.10f));
            var closeBtn = closeBtnRT.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            closeBtn.transition = Selectable.Transition.None;
            closeBtn.onClick.AddListener(Close);

            var closeTxtRT = NewRect(closeBtnRT, "Text");
            StretchFull(closeTxtRT);
            var closeTmp = closeTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) closeTmp.font = _font;
            closeTmp.text = $"닫기 [{_toggleKey}]";
            closeTmp.fontSize = 8f;
            closeTmp.alignment = TextAlignmentOptions.Center;
            closeTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            closeTmp.color = Color.white;
        }

        private void BuildDrawer(RectTransform root)
        {
            var drawer = NewRect(root, "Drawer");
            drawer.anchorMin = Vector2.zero;
            drawer.anchorMax = new Vector2(0f, 1f);
            drawer.pivot = new Vector2(0f, 0.5f);
            drawer.sizeDelta = new Vector2(_drawerWidth, -_topBarHeight);
            drawer.anchoredPosition = new Vector2(0f, -_topBarHeight * 0.5f);
            AddImg(drawer, _drawerBgColor);

            // 최상단: 검색창 + 분류 기준 버튼 (고정 높이). 그 아래: 스크롤되는 트리 영역.
            var searchArea = NewRect(drawer, "SearchBarArea");
            searchArea.anchorMin = new Vector2(0f, 1f);
            searchArea.anchorMax = Vector2.one;
            searchArea.pivot = new Vector2(0.5f, 1f);
            searchArea.sizeDelta = new Vector2(0f, _searchBarHeight);
            searchArea.anchoredPosition = Vector2.zero;

            _searchBar = searchArea.gameObject.AddComponent<CodexSearchBar>();
            _searchBar.Init(searchArea, _font);
            _searchBar.OnSearchChanged += q => { _searchQuery = q; ApplyFilter(); };
            _searchBar.OnFilterModeChanged += m => { _filterMode = m; ApplyFilter(); };

            // 검색창 바로 아래: "+ 새 메모" 버튼 (고정 높이).
            var newMemoArea = NewRect(drawer, "NewMemoArea");
            newMemoArea.anchorMin = new Vector2(0f, 1f);
            newMemoArea.anchorMax = Vector2.one;
            newMemoArea.pivot = new Vector2(0.5f, 1f);
            newMemoArea.sizeDelta = new Vector2(0f, _newMemoBtnHeight);
            newMemoArea.anchoredPosition = new Vector2(0f, -_searchBarHeight);

            var newMemoImg = AddImg(newMemoArea, new Color(0.14f, 0.28f, 0.16f));
            var newMemoBtn = newMemoArea.gameObject.AddComponent<Button>();
            newMemoBtn.targetGraphic = newMemoImg;
            newMemoBtn.transition = Selectable.Transition.None;
            newMemoBtn.onClick.AddListener(() => _memoForm.ShowForCreate());

            var newMemoTxtRT = NewRect(newMemoArea, "Text");
            StretchFull(newMemoTxtRT);
            var newMemoTmp = newMemoTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) newMemoTmp.font = _font;
            newMemoTmp.text = "+ 새 메모";
            newMemoTmp.fontSize = 8f;
            newMemoTmp.alignment = TextAlignmentOptions.Center;
            newMemoTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            newMemoTmp.color = Color.white;

            var treeScroll = NewRect(drawer, "TreeScroll");
            treeScroll.anchorMin = Vector2.zero;
            treeScroll.anchorMax = Vector2.one;
            treeScroll.offsetMin = Vector2.zero;
            treeScroll.offsetMax = new Vector2(0f, -(_searchBarHeight + _newMemoBtnHeight));

            var scroll = treeScroll.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;

            var vp = NewRect(treeScroll, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vp;

            var content = NewRect(vp, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 1f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _drawerView = treeScroll.gameObject.AddComponent<CodexDrawerTreeView>();
            _drawerView.Init(content, _font);
            _drawerView.OnEntrySelected += OnEntrySelected;
        }

        private void BuildCard(RectTransform root)
        {
            var card = NewRect(root, "Card");
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(1f, 1f);
            card.offsetMin = new Vector2(_drawerWidth, 0f);
            card.offsetMax = new Vector2(0f, -_topBarHeight);
            AddImg(card, _cardBgColor);

            _cardView = card.gameObject.AddComponent<CodexCardView>();
            _cardView.Init(card, _font);
            _cardView.OnEditRequested += HandleEditRequested;
            _cardView.OnDeleteRequested += HandleDeleteRequested;
        }

        private void BuildMemoForm(RectTransform root)
        {
            _memoForm = root.gameObject.AddComponent<CodexMemoFormView>();
            _memoForm.Init(root, _font);
            _memoForm.OnSaved += HandleMemoSaved;
            _memoForm.OnDeleteRequested += HandleMemoDeleteRequested;
        }

        private void OnEntrySelected(CodexEntry entry) => _cardView.ShowEntry(entry);

        // ─── 유저 메모 CRUD 연동 ─────────────────────────────────

        private void HandleEditRequested(CodexEntry entry)
        {
            var userEntry = CodexModule.Instance.UserEntries.FirstOrDefault(u => u.guid == entry.userEntryGuid);
            if (userEntry != null) _memoForm.ShowForEdit(userEntry);
        }

        private void HandleDeleteRequested(CodexEntry entry)
        {
            CodexModule.Instance.RemoveUserEntry(entry.userEntryGuid);
            _cardView.ShowEmpty();
        }

        private void HandleMemoSaved(CodexMemoFormResult r)
        {
            if (string.IsNullOrEmpty(r.guid))
                CodexModule.Instance.AddUserEntry(r.title, r.content, r.mapCategory, r.keywords);
            else
                CodexModule.Instance.UpdateUserEntry(r.guid, r.title, r.content, r.mapCategory, r.keywords);
        }

        private void HandleMemoDeleteRequested(string guid)
        {
            CodexModule.Instance.RemoveUserEntry(guid);
            _cardView.ShowEmpty();
        }

        // ─── UI 헬퍼 ─────────────────────────────────────────────

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
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
