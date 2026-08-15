using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

namespace RouteFinding.Codex
{
    // 단서 도감 — 완전히 별도의 풀스크린 패널 (지도 패널 안의 탭이 아님).
    // MapViewer와 동일한 패턴: 이 GO 자체는 항상 활성이고 내부 패널(_panelGO)만 토글된다.
    // 같은 GO의 Window가 IWindowContent 구현을 찾아 여닫기를 위임하며, 창 관리는 UIManager의
    // 창 스택이 담당한다(WindowName = "Clue"). N키는 InputManager.onOpenCodex → Toggle() 경유.
    //
    // 1단계: CodexModule(획득한 ClueData 목록의 소유자)을 구독해 실제 단서를 좌측 트리에 반영한다.
    // 2단계: CodexFilterService로 맵/출처/키워드 분류 + 검색을 지원한다.
    // 3단계: CodexUserEntry(유저 자유 메모) 추가/편집/삭제.
    // 코멘트/지도 연동은 이후 단계에서 추가.
    public class CodexPanel : MonoBehaviour, IWindowContent
    {
        [Header("폰트")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("레이아웃")]
        [SerializeField] private float _drawerWidth = 220f;
        [SerializeField] private float _topBarHeight = 12f;
        // 6-1(진행률 표시)·6-5(정렬 버튼 행)로 검색바 영역에 행이 2개 늘어난 만큼(34f → 62f) 높였다.
        [SerializeField] private float _searchBarHeight = 62f;
        [SerializeField] private float _newMemoBtnHeight = 12f;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(패널 전체 바깥 배경)")]
        [SerializeField] private Color _rootBgColor = new(0.04f, 0.04f, 0.08f, 0.96f);
        [Tooltip("패널 전체 바깥 배경 이미지 — 지정하면 도트풍 이미지로 대체(9슬라이스 테두리 있는 스프라이트도 지원), 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _rootBgSprite;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(상단바 + 좌측 드로어/트리 영역이 같이 씀)")]
        [SerializeField] private Color _drawerBgColor = new(0.07f, 0.09f, 0.14f, 0.97f);
        [Tooltip("상단바 + 좌측 드로어(트리) 배경 이미지 — 두 영역이 이 스프라이트를 같이 씀, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _drawerBgSprite;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(우측 상세 카드 영역)")]
        [SerializeField] private Color _cardBgColor = new(0.06f, 0.07f, 0.11f, 0.90f);
        [Tooltip("우측 상세 카드 배경 이미지 — 지정하면 도트풍 이미지로 대체, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _cardBgSprite;

        [Header("프리팹 (선택 — 비워두면 런타임 자동 생성)")]
        [SerializeField] private GameObject _panelPrefab;

        private GameObject _panelGO;
        private CodexSearchBar _searchBar;
        private CodexDrawerTreeView _drawerView;
        private CodexCardView _cardView;
        private CodexMemoFormView _memoForm;
        private InputManager _inputManager;

        private readonly List<CodexEntry> _allEntries = new();
        private List<CodexEntry> _placeholderEntries = new(); // 6-2단계 "???" 슬롯 — 맵별 그룹핑에서만 섞임
        private CodexFilterMode _filterMode = CodexFilterMode.ByMap;
        private CodexSortOrder _sortOrder = CodexSortOrder.Alphabetical; // 6-5단계
        private string _searchQuery = "";

        private void Awake()
        {
            // BuildUI()가 닫기 버튼 라벨(ToggleKeyLabel)을 실제 바인딩 키로 채우려면 그 전에
            // _inputManager가 준비돼 있어야 한다 — 그래서 BuildUI()보다 먼저 할당한다.
            _inputManager = FindObjectOfType<InputManager>();

            var rt = GetComponent<RectTransform>();
            if (rt != null) StretchFull(rt);
            BuildUI();

            // UI_TOGGLE 액션맵은 모드 전환과 무관하게 항상 켜져 있다(MapViewer.Awake와 동일).
            if (_inputManager != null) _inputManager.onOpenCodex += HandleOpenCodexInput;
        }

        // "닫기 [N]" 같은 UI 라벨용 — MapViewer.ToggleKeyLabel과 동일한 이유로 필드 대신 실시간 조회.
        private string ToggleKeyLabel
            => _inputManager != null ? _inputManager.inputs.UI_TOGGLE.OpenCodex.GetBindingDisplayString() : "N";

        private void Start()
        {
            CodexModule.Instance.OnCodexChanged += RefreshTree;
            // [버그 수정, 2026-08-17] 카드의 "노트에 핀" 버튼 상태를 노트 쪽 변경에 직접 물린다.
            // 예전엔 창을 열 때(OpenWindowContent)만 다시 맞췄는데, 그건 "도감을 닫고 → 노트에서 지우고 →
            // 도감을 다시 연다"는 순서에서만 성립한다. 도감이 열려 있는 채로 노트 항목이 지워지는 경로
            // (다른 창에서 지우기, 저장한 루트 불러오기, 세이브 로드 등)에서는 버튼이 "노트에 핀됨"
            // (interactable=false)으로 굳은 채 남아, 한 번 지운 단서를 도감에서 다시 꺼낼 수 없었다.
            // 여닫는 타이밍과 무관하게 항상 실제 핀 상태를 따라가도록 이벤트로 갱신한다.
            NoteModule.Instance.OnNoteChanged += HandleNoteChanged;
            RefreshTree();
            _panelGO.SetActive(false);
        }

        private void OnDestroy()
        {
            if (CodexModule.Instance != null) CodexModule.Instance.OnCodexChanged -= RefreshTree;
            if (NoteModule.Instance != null) NoteModule.Instance.OnNoteChanged -= HandleNoteChanged;
            if (_inputManager != null) _inputManager.onOpenCodex -= HandleOpenCodexInput;
        }

        // 노트 내용이 바뀌면 지금 카드에 떠 있는 항목의 핀 버튼만 가볍게 다시 맞춘다
        // (카드 전체 재생성 없음 — CodexCardView.RefreshPinState 주석 참고).
        private void HandleNoteChanged() => _cardView?.RefreshPinState();

        private void HandleOpenCodexInput(InputAction.CallbackContext context)
        {
            if (context.performed) Toggle();
        }

        // ─── Public API ──────────────────────────────────────────

        // UIManager의 windows 리스트에 등록할 이름. 같은 GO에 붙인 Window 컴포넌트가 이 창의 실체이고,
        // Window는 아래 IWindowContent 구현을 찾아 여닫기를 위임한다.
        // 씬의 GO 이름(ClueWindow)과 UIManager.windows 등록명에 맞춰 "Clue"다 — 클래스 이름은
        // CodexPanel이지만 창 이름은 Clue이니 헷갈리지 말 것. 이 상수와 UIManager 리스트의 name이
        // 한 글자라도 어긋나면 창이 조용히 안 열린다.
        public const string WindowName = "Clue";

        // ─── UIManager 경유 진입점 ────────────────────────────────
        // 외부에서 부르는 API는 전부 UIManager를 거친다(MapViewer와 동일한 패턴).
        // 실제 작업은 OpenPanel/ClosePanel이고, 거기서 다시 UIManager를 부르면 무한 재귀가 된다.

        public void Open() => UI?.OpenWindow(WindowName);
        public void Close() => UI?.CloseWindow(WindowName);
        public void Toggle() => UI?.ToggleWindow(WindowName);

        private static UIManager UI => GameManager.instance == null ? null : GameManager.instance.UIManager;

        // ─── IWindowContent — Window가 호출하는 실제 작업 ─────────

        // 지도와 동일하게 이동 중에는 도감 열람도 막는다 (0단계 임시 결정 — CLAUDE.md 4-4 참고).
        public bool CanOpenWindow
        {
            get
            {
                if (RouteModule.Instance != null && !RouteModule.Instance.CanOpenMap)
                {
                    Debug.LogWarning("[CodexPanel] 이동 중에는 도감을 열 수 없습니다.");
                    return false;
                }
                return true;
            }
        }

        public void OpenWindowContent()
        {
            // 세이브 로드 직후처럼 OnClueAcquired 이벤트를 놓쳤을 경우를 대비해, 열 때마다 전체 재계산.
            CodexModule.Instance.RebuildFromProgress();
            // [버그 수정, 2026-07-21] 도감이 닫혀 있는 동안 노트에서 이 카드가 보여주던 단서의 핀이
            // 풀렸을 수 있다(CodexCardView.RefreshPinState 주석 참고) — 열 때마다 다시 맞춘다.
            _cardView?.RefreshPinState();
            _panelGO.SetActive(true);
            _drawerView?.ResetScroll(); // 열 때마다 이전에 내려서 보던 스크롤 위치를 맨 위로 초기화
            _inputManager?.MENUMode();
        }

        public void CloseWindowContent()
        {
            // 첨부 소리를 재생하던 중이면 창을 닫는 순간 명시적으로 멈춘다. 패널 비활성화만으로도
            // AudioSource는 멈추지만, 그 경로로는 재생 감시 코루틴이 그냥 죽어버려서 버튼 라벨이
            // "■ 정지"인 채로 굳는다 — 다시 열었을 때 눌러도 아무 소리가 안 나는 것처럼 보인다.
            _cardView?.StopAudio();
            _panelGO.SetActive(false);
            _inputManager?.PLAYMode();
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

            _placeholderEntries = BuildUnacquiredPlaceholders();

            // 6-1단계 — "획득 N / 전체 M". 전체는 정식 단서 수만 센다(유저 메모 제외).
            _searchBar.SetProgress(CodexModule.Instance.AcquiredClues.Count, MapGraph.Instance?.AllClues.Count ?? 0);

            ApplyFilter();
        }

        // 검색어 필터링 후, 현재 분류 기준(맵/출처/키워드)으로 그룹핑해서 트리에 반영한다.
        // "???" 빈칸 슬롯(_placeholderEntries)은 맵별 그룹핑에서만 섞어 넣는다 — 슬롯 개수 자체가
        // MapNodeData.clueIds 기준이라 출처/키워드 분류에는 맞지 않는다(Clue_System.md 6-2).
        private void ApplyFilter()
        {
            var source = _filterMode == CodexFilterMode.ByMap
                ? _allEntries.Concat(_placeholderEntries).ToList()
                : _allEntries;

            var searched = CodexFilterService.Search(source, _searchQuery);
            var groups = _filterMode switch
            {
                CodexFilterMode.BySource  => CodexFilterService.GroupBySource(searched),
                CodexFilterMode.ByKeyword => CodexFilterService.GroupByKeyword(searched),
                _                         => CodexFilterService.GroupByMap(searched),
            };

            // 6-5단계 — 그룹 내부 항목 정렬. "획득 최신순"은 RouteProgressState.AcquisitionOrder
            // (clueId → 순번)를 조회용 Dictionary로 한 번 만들어 넘긴다.
            var progress = RouteModule.Instance?.Progress;
            Dictionary<string, int> acquisitionRank = null;
            if (progress != null)
            {
                acquisitionRank = new Dictionary<string, int>();
                var order = progress.AcquisitionOrder;
                for (int i = 0; i < order.Count; i++) acquisitionRank[order[i]] = i;
            }
            CodexFilterService.SortEntries(groups, _sortOrder, acquisitionRank);

            _drawerView.SetGroups(groups);
        }

        // 6-2단계(Clue_System.md) — 알려진(프론티어 포함) 맵 중 아직 다 못 찾은 단서가 있는 맵마다,
        // 그 맵의 MapNodeData.clueIds 중 미획득분 개수만큼 "??? (미발견)" 슬롯을 만든다. "알려진 맵" 판정은
        // 노트의 미획득 후보 노출(NoteSystem_기획서.md 규칙 3)과 완전히 동일한 기준(KnownMapService)을 쓴다.
        private List<CodexEntry> BuildUnacquiredPlaceholders()
        {
            var result = new List<CodexEntry>();
            var graph = MapGraph.Instance;
            var progress = RouteModule.Instance?.Progress;
            if (graph == null || progress == null) return result;

            var known = KnownMapService.ComputeKnownNodeGuids(graph, progress);
            foreach (var node in graph.AllNodes)
            {
                if (!known.Contains(node.guid) || node.clueIds == null) continue;

                int missing = KnownMapService.CountUnacquiredClues(node, progress);
                for (int i = 0; i < missing; i++)
                {
                    result.Add(new CodexEntry
                    {
                        title         = "??? (미발견)",
                        typeLabel     = "",
                        timestamp     = "",
                        content       = "아직 발견하지 못한 단서입니다.",
                        source        = "",
                        mapCategory   = node.nodeName,
                        keywords      = Array.Empty<string>(),
                        userEntryGuid = "",
                        clueId        = "",
                        comments      = Array.Empty<CodexComment>(),
                        isPlaceholder = true,
                    });
                }
            }
            return result;
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
                clueId      = clue.id,
                comments    = clue.comments ?? Array.Empty<CodexComment>(),
                attachments = clue.attachments ?? Array.Empty<ClueAttachment>(),
                isNew       = CodexModule.Instance.IsClueNew(clue.id),
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
            comments      = entry.comments ?? Array.Empty<CodexComment>(),
        };

        // ─── UI 구축 ─────────────────────────────────────────────

        private void BuildUI()
        {
            // 씬 계층에 CodexPanelRoot가 이미 자식으로 배치돼 있으면 재사용.
            // MemoFormOverlay가 없으면 구버전(3단계 도입 이전) — 파괴 후 재생성. (MapViewer의 Toolbar 판정과 동일 패턴)
            var existing = transform.Find("CodexPanelRoot");
            if (existing != null)
            {
                // SortRow(6-5단계, 가장 최근에 추가된 요소)까지 있어야 "최신" — 판정 기준은
                // 항상 최근 추가 요소여야 그 이전 요소(CommentsSection 등) 전부 갖췄다는 게 보장된다.
                bool existingCurrent = FindDeepTransform(existing, "SortRow") != null;
                if (existingCurrent)
                {
                    Debug.Log("[CodexPanel] BuildUI: 씬에 있던 기존 CodexPanelRoot를 재사용합니다.");
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.Log("[CodexPanel] BuildUI: 기존 CodexPanelRoot에 SortRow가 없어 구버전으로 판단, 파괴 후 재생성합니다.");
                // 먼저 비활성화한 뒤 Destroy — 구버전 패널 안의 TMP 텍스트를 활성 상태로 그냥 Destroy하면,
                // 같은 프레임에 ScrollRect.LateUpdate가 강제하는 CanvasUpdateRegistry 리빌드가 이미 파괴 중인
                // TMP의 서브메시(폴백 폰트) 머티리얼에 접근하려다 MissingReferenceException을 던질 수 있다.
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            // 프리팹이 지정되어 있으면 인스턴스화 후 참조를 바인딩하고 콜백만 재연결.
            if (_panelPrefab != null)
            {
                bool prefabCurrent = FindDeepTransform(_panelPrefab.transform, "SortRow") != null;
                if (prefabCurrent)
                {
                    Debug.Log($"[CodexPanel] BuildUI: 지정된 프리팹({_panelPrefab.name})을 인스턴스화합니다.");
                    _panelGO = Instantiate(_panelPrefab, transform, false);
                    _panelGO.name = "CodexPanelRoot";
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.LogWarning($"[CodexPanel] BuildUI: 지정된 프리팹({_panelPrefab.name})에 SortRow가 없어 구버전으로 판단, 런타임 생성으로 대체합니다. 프리팹을 다시 생성해주세요.");
            }

            // ── 프리팹 없음 → 런타임 자동 생성 ──
            Debug.Log(_panelPrefab == null
                ? "[CodexPanel] BuildUI: _panelPrefab이 비어 있어 런타임으로 새로 생성합니다."
                : "[CodexPanel] BuildUI: (위 경고 참고) 런타임으로 새로 생성합니다.");
            _panelGO = new GameObject("CodexPanelRoot");
            _panelGO.transform.SetParent(transform, false);
            var root = _panelGO.AddComponent<RectTransform>();
            StretchFull(root);
            PanelBackground.Apply(root, _rootBgColor, _rootBgSprite);

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
            // 프리팹/씬 재사용 경로 — 배경 스프라이트 인스펙터 값을 바꿔도 반영되도록 여기서도 적용.
            PanelBackground.Apply(root, _rootBgColor, _rootBgSprite);
            PanelBackground.Apply(FindDeepTransform(root, "TopBar") as RectTransform, _drawerBgColor, _drawerBgSprite);
            PanelBackground.Apply(FindDeepTransform(root, "Drawer") as RectTransform, _drawerBgColor, _drawerBgSprite);
            PanelBackground.Apply(FindDeepTransform(root, "Card") as RectTransform, _cardBgColor, _cardBgSprite);

            var searchAreaTF = FindDeepTransform(root, "SearchBarArea");
            _searchBar = searchAreaTF?.GetComponent<CodexSearchBar>();
            if (_searchBar != null)
            {
                _searchBar.Bind((RectTransform)searchAreaTF);
                _searchBar.OnSearchChanged += q => { _searchQuery = q; ApplyFilter(); };
                _searchBar.OnFilterModeChanged += m => { _filterMode = m; ApplyFilter(); };
                _searchBar.OnSortOrderChanged += o => { _sortOrder = o; ApplyFilter(); };
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
                _cardView.OnPinRequested += HandlePinRequested;
                _cardView.OnSuggestionAddRequested += HandleSuggestionAddRequested;
                _cardView.OnKeywordClicked += HandleKeywordClicked;
                _cardView.OnMapRefClicked += HandleMapRefClicked;
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
            var titleLe = titleRT.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;
            var titleTmp = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) titleTmp.font = _font;
            titleTmp.text = "단서 도감";
            titleTmp.fontSize = 8f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var closeBtnRT = NewRect(topBar, "BtnClose");
            var closeLe = closeBtnRT.gameObject.AddComponent<LayoutElement>();
            closeLe.preferredWidth = 34f;
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
            closeTmp.text = $"닫기 [{ToggleKeyLabel}]";
            closeTmp.fontSize = 7f;
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
            PanelBackground.Apply(drawer, _drawerBgColor, _drawerBgSprite);

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
            _searchBar.OnSortOrderChanged += o => { _sortOrder = o; ApplyFilter(); };

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
            newMemoTmp.fontSize = 7f;
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
            // 아래쪽 패딩을 크게 잡아 스크롤 여유 공간을 넉넉히 확보 — 근본 원인(트리를 다 펼쳤을 때
            // 실제 필요한 높이보다 ContentSizeFitter 계산이 짧게 나오는 문제, 위 CLAUDE.md 미해결 버그
            // 참고)을 해결하는 대신, 그보다 훨씬 큰 여유값을 더해 항상 맨 아래까지 스크롤되도록 우회한다.
            // 계산이 짧아도 이 여유분 안에서 흡수되면 되므로 실질적으로 체감되는 문제가 사라진다.
            vlg.padding = new RectOffset(2, 2, 2, 400);
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
            PanelBackground.Apply(card, _cardBgColor, _cardBgSprite);

            _cardView = card.gameObject.AddComponent<CodexCardView>();
            _cardView.Init(card, _font);
            _cardView.OnEditRequested += HandleEditRequested;
            _cardView.OnDeleteRequested += HandleDeleteRequested;
            _cardView.OnPinRequested += HandlePinRequested;
            _cardView.OnSuggestionAddRequested += HandleSuggestionAddRequested;
            _cardView.OnKeywordClicked += HandleKeywordClicked;
            _cardView.OnMapRefClicked += HandleMapRefClicked;
        }

        private void BuildMemoForm(RectTransform root)
        {
            _memoForm = root.gameObject.AddComponent<CodexMemoFormView>();
            _memoForm.Init(root, _font);
            _memoForm.OnSaved += HandleMemoSaved;
            _memoForm.OnDeleteRequested += HandleMemoDeleteRequested;
        }

        private void OnEntrySelected(CodexEntry entry)
        {
            // 6-3단계 — 카드로 연 시점에 "NEW" 후보에서 제거한다. MarkClueViewed는 리프레시 이벤트를
            // 일부러 쏘지 않으므로(이유는 CodexModule 쪽 주석 참고) 트리의 NEW 배지는 다음 자연스러운
            // 갱신 때 사라진다 — 지금 클릭한 이 카드는 그대로 정상 표시된다.
            CodexModule.Instance.MarkClueViewed(entry.clueId);
            _cardView.ShowEntry(entry);
        }

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

        // ─── 6-4단계: 키워드 태그 교차 탐색 ─────────────────────────

        // 카드에서 키워드 태그를 클릭하면 분류 기준을 키워드별로 전환하고 해당 그룹으로 스크롤한다.
        // SetFilterModeExternally는 이미 같은 모드면 아무 것도 안 하므로(SetMode의 기존 가드), 이미
        // 키워드별 분류 중이었다면 이 호출은 그냥 스킵되고 아래 FocusCategory만 실행된다.
        private void HandleKeywordClicked(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return;
            _searchBar.SetFilterModeExternally(CodexFilterMode.ByKeyword);
            _drawerView.FocusCategory(keyword);
        }

        // ─── 첨부물(맵 참조) 연동 ─────────────────────────────────

        // 카드의 맵 첨부에서 "지도" 버튼을 누르면 도감을 닫고 지도를 열어 그 맵으로 시점을 옮긴다.
        // MapViewer가 "도감" 툴바 버튼에서 쓰는 것과 정확히 반대 방향의 이동으로, 같은 패턴을 따른다
        // (상대 패널을 직접 참조하지 않고 씬에서 찾는다).
        private void HandleMapRefClicked(string mapGuid)
        {
            if (string.IsNullOrEmpty(mapGuid)) return;

            var mapViewer = FindObjectOfType<MapView.MapViewer>();
            if (mapViewer == null)
            {
                Debug.LogWarning("[CodexPanel] 씬에서 MapViewer를 찾을 수 없습니다.");
                return;
            }
            Close();
            mapViewer.OpenFocusedOn(mapGuid);
        }

        // ─── 노트 연동 (2단계, 노트 편입 규칙 2 — 도감 → 노트 수동 핀) ─────

        private void HandlePinRequested(CodexEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.clueId)) return;
            if (!NoteModule.Instance.AddManualPin(entry.clueId)) return;

            // ShowEntry가 끝에서 추천 목록을 접으므로(카드 전환 시 이전 추천을 지우기 위함),
            // 반드시 ShowSuggestions보다 먼저 호출해야 한다 — 순서를 바꾸면 추천이 그려지자마자 숨겨진다.
            _cardView.ShowEntry(entry); // 핀 버튼 상태("노트에 핀됨")를 다시 그린다
            var suggestions = FindRelatedByKeyword(entry).FindAll(s => !NoteModule.Instance.IsPinned(s.clueId));
            _cardView.ShowSuggestions(suggestions);
        }

        private void HandleSuggestionAddRequested(CodexEntry suggestion)
        {
            if (suggestion == null || string.IsNullOrEmpty(suggestion.clueId)) return;
            NoteModule.Instance.AddManualPin(suggestion.clueId);

            // 방금 추가된 항목만 추천 목록에서 제외하고 다시 그린다 — 나머지 후보는 그대로 유지.
            var remaining = FindRelatedByKeyword(_currentPinnedEntry).FindAll(s => !NoteModule.Instance.IsPinned(s.clueId));
            _cardView.ShowSuggestions(remaining);
        }

        // 마지막으로 핀된(그래서 지금 추천 목록의 기준이 되는) 항목 — HandleSuggestionAddRequested가
        // "추가" 클릭 후 추천 목록을 다시 계산할 때 기준 항목을 다시 알아야 해서 보관해둔다.
        private CodexEntry _currentPinnedEntry;

        // 같은 키워드(ClueData.keywords ∪ 타입 표시 이름)를 공유하는 다른 정식 단서를 찾는다.
        // CodexFilterService.GroupByKeyword를 그대로 재사용 — NoteSystem_기획서.md 규칙 2에 명시된 그대로.
        private List<CodexEntry> FindRelatedByKeyword(CodexEntry entry)
        {
            if (entry == null) return new List<CodexEntry>();
            _currentPinnedEntry = entry;

            var entryKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry.keywords != null)
                foreach (var kw in entry.keywords)
                    if (!string.IsNullOrWhiteSpace(kw)) entryKeywords.Add(kw.Trim());
            if (!string.IsNullOrEmpty(entry.typeLabel)) entryKeywords.Add(entry.typeLabel);

            var related = new List<CodexEntry>();
            var seenClueIds = new HashSet<string>();
            foreach (var group in CodexFilterService.GroupByKeyword(_allEntries))
            {
                if (!entryKeywords.Contains(group.category)) continue;
                foreach (var other in group.entries)
                {
                    if (other == entry || string.IsNullOrEmpty(other.clueId)) continue; // 유저 메모는 핀 대상 아님
                    if (!seenClueIds.Add(other.clueId)) continue;
                    related.Add(other);
                }
            }
            return related;
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
