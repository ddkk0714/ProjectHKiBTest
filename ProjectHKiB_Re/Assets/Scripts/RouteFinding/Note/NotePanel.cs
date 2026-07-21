using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RouteFinding.MapView;

namespace RouteFinding.Note
{
    // 노트 — 도감(CodexPanel)과 같은 패턴으로 완전히 별도의 풀스크린 패널.
    // Canvas 직속 자식 GO에 붙이고, 이 GO 자체는 항상 활성 — V키 감지를 위해 Update가 계속 동작하며,
    // 내부 패널(_panelGO)만 Open/Close 토글된다.
    //
    // 지도/도감과의 핵심 차이(NoteSystem_기획서.md "UI 진입 / 영속성" 절 참고):
    //   - Open()이 RouteModule.CanOpenMap을 체크하지 않는다 — 이동 중에도 항상 열람 가능
    //     (다중 목적지 계획 실행 중 "몇 번째 구간인지" 확인하는 용도, 4단계부터 의미가 커짐).
    //   - 대신 편집(항목 삭제 등)은 NoteModule.CanEdit(=!IsTraveling)으로 잠근다 — NoteModule이 직접 판단.
    //
    // 1단계: 노트 표시(경로 체인 + 맵별 카드) + 개별 삭제. 도감 연동 수동 핀(2단계)까지는
    // NoteRouteGraphView(좌측)가 담당. (규칙 3 "미획득 후보 자동 노출"은 2026-07-14 요청으로 제거됨.)
    //
    // 1단계 재설계(2026-07-14, 사용자 제공 UI 목업 기준): 항목을 플랫 리스트가 아니라
    // "선택된 경로가 지나는 맵 체인 + 그 맵에 연관된 단서 카드"로 그린다 — NoteRouteGraphView 참고.
    //
    // [2026-07-21, 요청으로 교체] 우측에 있던 다중 목적지 이동 계획(RoutePlanEditorView)을 완전히
    // 제거하고 "단서 서랍"(ClueDrawerView)으로 대체했다 — 현재 획득한 모든 단서를 검색/키워드 필터로
    // 훑어보고, 드래그해서 좌측 단서 그래프에 직접 배치할 수 있다. 드롭 처리 자체는
    // NoteRouteGraphView.PlaceClueAt이 담당 — NotePanel.HandleClueDropped가 그 다리 역할.
    public class NotePanel : MonoBehaviour
    {
        [Header("조작")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.V;

        [Header("폰트")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("레이아웃")]
        [SerializeField] private float _topBarHeight = 12f;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(패널 전체 바깥 배경)")]
        [SerializeField] private Color _rootBgColor = new(0.04f, 0.04f, 0.08f, 0.96f);
        [Tooltip("패널 전체 바깥 배경 이미지 — 지정하면 도트풍 이미지로 대체(9슬라이스 테두리 있는 스프라이트도 지원), 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _rootBgSprite;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(상단바 + 좌측 그래프 영역 + 우측 단서 서랍이 같이 씀, 서랍은 살짝 어둡게 보정된 색)")]
        [SerializeField] private Color _listBgColor = new(0.07f, 0.09f, 0.14f, 0.97f);
        [Tooltip("상단바 + 그래프 영역 + 단서 서랍(+ 서랍 여닫이 탭) 배경 이미지 — 전부 이 스프라이트를 같이 씀, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _listBgSprite;

        // [2026-07-21, 요청으로 교체] 비율(anchorMax.x) 기반이던 서랍 폭을 MapViewer의 사이드패널과 같은
        // 픽셀 폭 기반으로 바꿨다 — 접었을 때 고정폭(_drawerCollapsedWidth)만 남기는 개념 자체가 비율로는
        // 표현하기 어려워서(비율은 항상 화면 크기에 상대적이라 "접었을 때 12px" 같은 절대값을 못 담는다).
        [SerializeField] private float _drawerAreaWidth = 140f;      // 서랍이 열렸을 때의 폭
        [SerializeField] private float _drawerCollapsedWidth = 12f;  // 접었을 때 남는 재오픈용 탭 폭

        [Header("프리팹 (선택 — 비워두면 런타임 자동 생성)")]
        [SerializeField] private GameObject _panelPrefab;

        private GameObject _panelGO;
        private NoteRouteGraphView _graphView;
        private ClueDrawerView _clueDrawerView;
        private NoteBoardWindow _boardWindow;
        private ClueKeywordFilterWindow _keywordFilterWindow;
        private NoteClueCreateWindow _clueCreateWindow;
        private InputManager _inputManager;
        private Image _linkModeBtnImg; // "단서 연동" 토글 버튼 — 활성 상태를 색으로 표시

        // [2026-07-21, 신설] 좌측 단서 그래프 팬·줌(MapViewer.GraphPanZoom 재사용) + 우측 서랍 접기/펼치기.
        private RectTransform _graphAreaRT;     // GraphScroll 자체 — 서랍 폭에 맞춰 offsetMax를 조정
        private RectTransform _graphViewportRT; // GraphPanZoom 클리핑 기준
        private RectTransform _graphContainerRT; // GraphPanZoom 대상 — NoteRouteGraphView의 Content로도 그대로 쓰임
        private GraphPanZoom _graphPanZoom;

        private RectTransform _drawerAreaRT;      // ClueDrawerScroll 자체 — 열림/접힘 폭 조정 대상
        private RectTransform _drawerViewportRT;  // 접었을 때 비활성화할 스크롤 뷰포트(재오픈 탭은 별개라 계속 보임)
        private TextMeshProUGUI _drawerToggleArrowTMP;
        private bool _drawerOpen = true;

        private void Awake()
        {
            var rt = GetComponent<RectTransform>();
            if (rt != null) StretchFull(rt);
            BuildUI();
            // MapViewer.Awake와 동일한 이유·순서 — BuildUI()가 어느 경로(재사용/프리팹/런타임 생성)를
            // 탔든 이 시점엔 _graphContainerRT/_graphViewportRT가 채워져 있으므로 여기서 한 번만 Init.
            var canvas = GetComponentInParent<Canvas>();
            _graphPanZoom?.Init(_graphContainerRT, _graphViewportRT, canvas);
            _inputManager = FindObjectOfType<InputManager>();
        }

        private void Start()
        {
            NoteModule.Instance.OnNoteChanged += Refresh;
            if (RouteModule.Instance != null) RouteModule.Instance.OnRouteSelected += HandleRouteSelected;
            Refresh();
            _panelGO.SetActive(false);
        }

        private void OnDestroy()
        {
            if (NoteModule.Instance != null) NoteModule.Instance.OnNoteChanged -= Refresh;
            if (RouteModule.Instance != null) RouteModule.Instance.OnRouteSelected -= HandleRouteSelected;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey)) Toggle();
        }

        // ─── Public API ──────────────────────────────────────────

        // 지도/도감과 달리 이동 중에도 항상 열 수 있다 — CanOpenMap 체크를 의도적으로 하지 않는다.
        public void Open()
        {
            ExclusivePanelGroup.NotifyOpening(this, Close); // 지도/도감 등 다른 패널이 열려 있으면 먼저 닫는다
            Refresh();
            _panelGO.SetActive(true);
            _inputManager?.MENUMode();
        }

        public void Close()
        {
            ExclusivePanelGroup.NotifyClosing(this);
            _panelGO.SetActive(false);
            _inputManager?.PLAYMode();
        }

        public void Toggle()
        {
            if (_panelGO.activeSelf) Close(); else Open();
        }

        // Editor 스크립트(NotePanelEditor)에서 프리팹 저장 시 접근.
        public GameObject GetPanelGO() => _panelGO;

        // ─── 데이터 연동 ──────────────────────────────────────────

        private void Refresh()
        {
            var route = RouteModule.Instance?.SelectedRoute;
            Debug.Log($"[NotePanel] Refresh() — route={(route == null ? "null" : $"valid={route.IsValid}")}, entries={NoteModule.Instance.Entries.Count}, _graphView={(_graphView != null)}");
            _graphView.SetData(route, NoteModule.Instance.Entries);
            _clueDrawerView.Refresh();
        }

        private void HandleRouteSelected(PathResult _) => Refresh();

        // 이동 중 잠금은 NoteModule.RemoveEntry 내부에서 판단·경고한다 — 여기서는 그대로 위임만 한다.
        private void HandleDeleteRequested(NoteEntry entry) => NoteModule.Instance.RemoveEntry(entry.clueId);

        // 단서 서랍(우측)에서 드래그가 끝났을 때의 다리 역할 — 실제로 그래프 영역 위에 놓였는지,
        // 어디에 배치할지는 NoteRouteGraphView.PlaceClueAt이 전부 판단한다(NotePanel은 중계만).
        private void HandleClueDropped(string clueId, Vector2 screenPosition) =>
            _graphView.PlaceClueAt(clueId, screenPosition);

        // ─── 저장한 루트(보드) — 상단 툴바 "저장한 루트" 창 오케스트레이션 ─────
        // 실제 저장/삭제/목록 보관은 NoteModule.SavedBoards가 담당하고, 여기서는 "지금 화면 상태"를
        // 모으거나(저장) "저장된 스냅샷을 화면에 반영"(불러오기)하는 조립만 한다.

        private void OpenBoardWindow() => _boardWindow?.Show(NoteModule.Instance.SavedBoards);

        private void HandleBoardSaveRequested(string name)
        {
            var routeGuids = new List<string>();
            var route = RouteModule.Instance?.SelectedRoute;
            if (route != null && route.IsValid)
                foreach (var node in route.Nodes) routeGuids.Add(node.guid);

            var manualClueIds = NoteModule.Instance.Entries
                .Where(e => e.reason == NotePinReason.ManualPin)
                .Select(e => e.clueId)
                .ToList();

            // [요청, 2026-07-21] 수동 핀뿐 아니라 지금 노드로 펼쳐져 있는 경로연동 단서까지 포함해 위치를
            // 저장한다 — 안 그러면 사용자가 직접 옮겨둔 경로연동 단서 위치가 불러올 때 기본 위치로 리셋됐다.
            var positions = _graphView.ExportCluePositions(_graphView.GetPlacedClueIds());
            var expandedClueIds = _graphView.GetExpandedClueIds().ToList();

            NoteModule.Instance.SaveBoard(name, routeGuids, manualClueIds, positions, expandedClueIds);
            _boardWindow?.Refresh(NoteModule.Instance.SavedBoards);
        }

        private void HandleBoardLoadRequested(string boardId)
        {
            var board = NoteModule.Instance.GetBoard(boardId);
            if (board == null) return;
            if (!NoteModule.Instance.CanEdit)
            {
                Debug.LogWarning("[NotePanel] 이동 중에는 저장한 루트를 불러올 수 없습니다.");
                return;
            }

            // 순서 중요:
            // 1) 펼침 상태부터 복원해야 — 아래 RebuildRouteLinkedEntries가 유발하는 SetData 재생성 때
            //    저장돼 있던 경로연동 단서가 카드가 아니라 노드로 만들어진다(안 그러면 카드로 남아
            //    2)에서 큐잉해둔 위치가 적용될 자리가 없다).
            // 2) 그래프 위치를 경로/핀 반영보다 먼저 큐잉해둬야, 뒤이은 호출들이 유발하는 SetData
            //    재생성 시점에 새로 만들어지는 단서 노드가 저장된 위치를 바로 집어간다
            //    (NoteRouteGraphView.PlaceClueAt과 동일한 큐잉 순서).
            _graphView.ApplyExpandedClueIds(board.expandedClueIds);
            _graphView.ApplySavedPositions(board.cluePositions);

            RouteModule.Instance?.ImportSelectedRoute(board.routeNodeGuids);
            NoteModule.Instance.RebuildRouteLinkedEntries(RouteModule.Instance?.SelectedRoute);
            NoteModule.Instance.ApplyManualPins(board);
            NoteModule.Instance.ImportClueLinks(board.clueLinks); // 단서 연동 관계도 같이 복원

            _boardWindow?.Hide();
        }

        private void HandleBoardDeleteRequested(string boardId)
        {
            NoteModule.Instance.DeleteBoard(boardId);
            _boardWindow?.Refresh(NoteModule.Instance.SavedBoards);
        }

        // ─── 단서 서랍 키워드 필터 창 오케스트레이션 ───────────────
        // 필터 상태(_activeKeywords) 자체는 ClueDrawerView가 소유 — 여기서는 창을 열고/갱신하는
        // 다리 역할만 한다(HandleClueDropped와 같은 성격의 중계).

        private void HandleFilterButtonClicked() =>
            _keywordFilterWindow?.Show(_clueDrawerView.ComputeAllKeywords(), _clueDrawerView.ActiveKeywords);

        private void HandleFilterKeywordToggled(string keyword)
        {
            _clueDrawerView.ToggleKeyword(keyword);
            _keywordFilterWindow?.Refresh(_clueDrawerView.ComputeAllKeywords(), _clueDrawerView.ActiveKeywords);
        }

        private void HandleFilterClearAllRequested()
        {
            _clueDrawerView.ClearActiveKeywords();
            _keywordFilterWindow?.Refresh(_clueDrawerView.ComputeAllKeywords(), _clueDrawerView.ActiveKeywords);
        }

        // ─── 단서 생성 — 상단 툴바 "단서 생성" 창 오케스트레이션 ─────────────
        // [신설, 2026-07-21] 노트 안에서 유저가 직접 새 단서를 만들 수 있다 — 생성하면 도감
        // (CodexModule.AddUserEntry)에도 자동 등록되고, 노트에도 곧바로 수동 핀으로 붙는다.

        private void OpenClueCreateWindow() => _clueCreateWindow?.Show();

        private void HandleClueCreateRequested(string title, string content, string[] keywords)
        {
            if (CodexModule.Instance == null || NoteModule.Instance == null) return;

            var entry = CodexModule.Instance.AddUserEntry(title, content, "", keywords);
            NoteModule.Instance.AddManualPin(entry.guid);
        }

        // ─── 단서 연동 모드 — 상단 툴바 "단서 연동" 토글 오케스트레이션 ─────────
        // [신설, 2026-07-21] 실제 연동 상태 관리·클릭 처리는 전부 NoteRouteGraphView(그래프 쪽 사정을
        // 잘 아는 쪽)가 하고, 여기서는 토글 버튼 색만 그 상태에 맞춰 갱신한다.

        private void ToggleLinkMode()
        {
            _graphView.SetLinkMode(!_graphView.LinkModeActive);
            RefreshLinkModeButton();
        }

        private void RefreshLinkModeButton()
        {
            if (_linkModeBtnImg == null || _graphView == null) return;
            _linkModeBtnImg.color = _graphView.LinkModeActive
                ? new Color(0.25f, 0.55f, 0.30f)
                : new Color(0.20f, 0.28f, 0.42f);
        }

        // ─── UI 구축 ─────────────────────────────────────────────

        private void BuildUI()
        {
            // 씬 계층에 NotePanelRoot가 이미 자식으로 배치돼 있으면 재사용 (CodexPanel.BuildUI와 동일한 패턴).
            // ClueCreateOverlay(2026-07-21, "단서 생성" 창 + "단서 연동" 모드 신설로 가장 최근에 추가된
            // 요소)가 없으면 구버전으로 보고 파괴 후 재생성 — "판정 기준은 항상 가장 최근에 추가된 요소여야
            // 그 이전 요소까지 전부 갖췄다는 게 보장된다"(MapViewer의 BtnSelectRoute 마커 갱신과 동일한
            // 이유로, 이번에도 DrawerToggleTab에서 ClueCreateOverlay로 교체).
            var existing = transform.Find("NotePanelRoot");
            if (existing != null)
            {
                bool existingCurrent = FindDeepTransform(existing, "ClueCreateOverlay") != null;
                if (existingCurrent)
                {
                    Debug.Log("[NotePanel] BuildUI: 씬에 있던 기존 NotePanelRoot를 재사용합니다.");
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.Log("[NotePanel] BuildUI: 기존 NotePanelRoot에 ClueCreateOverlay가 없어 구버전으로 판단, 파괴 후 재생성합니다.");
                // CodexDrawerTreeView.Clear와 동일한 이유로 먼저 비활성화한 뒤 Destroy.
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            // 프리팹이 지정되어 있으면 인스턴스화 후 참조를 바인딩하고 콜백만 재연결.
            if (_panelPrefab != null)
            {
                bool prefabCurrent = FindDeepTransform(_panelPrefab.transform, "ClueCreateOverlay") != null;
                if (prefabCurrent)
                {
                    Debug.Log($"[NotePanel] BuildUI: 지정된 프리팹({_panelPrefab.name})을 인스턴스화합니다.");
                    _panelGO = Instantiate(_panelPrefab, transform, false);
                    _panelGO.name = "NotePanelRoot";
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.LogWarning($"[NotePanel] BuildUI: 지정된 프리팹({_panelPrefab.name})에 ClueCreateOverlay가 없어 구버전으로 판단, 런타임 생성으로 대체합니다. 프리팹을 다시 생성해주세요.");
            }

            // ── 프리팹 없음 → 런타임 자동 생성 ──
            Debug.Log(_panelPrefab == null
                ? "[NotePanel] BuildUI: _panelPrefab이 비어 있어 런타임으로 새로 생성합니다."
                : "[NotePanel] BuildUI: (위 경고 참고) 런타임으로 새로 생성합니다.");
            _panelGO = new GameObject("NotePanelRoot");
            _panelGO.transform.SetParent(transform, false);
            var root = _panelGO.AddComponent<RectTransform>();
            StretchFull(root);
            PanelBackground.Apply(root, _rootBgColor, _rootBgSprite);

            BuildTopBar(root);
            BuildList(root);
            BuildBoardWindow(root);
            BuildKeywordFilterWindow(root);
            BuildClueCreateWindow(root);
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
            PanelBackground.Apply(FindDeepTransform(root, "TopBar") as RectTransform, _listBgColor, _listBgSprite);

            FindDeepTransform(root, "BtnClose")?.GetComponent<Button>()?.onClick.AddListener(Close);

            var graphScrollTF = FindDeepTransform(root, "GraphScroll");
            _graphAreaRT = graphScrollTF as RectTransform;
            PanelBackground.Apply(_graphAreaRT, _listBgColor, _listBgSprite);
            _graphPanZoom = graphScrollTF?.GetComponent<GraphPanZoom>();
            _graphPanZoom?.ConfigureBounds(true, 120f); // BuildGraphPanZoom과 동일 — 재사용 경로에서도 팬 범위 제한 적용
            _graphView = graphScrollTF?.GetComponent<NoteRouteGraphView>();
            if (_graphView != null)
            {
                _graphViewportRT = FindDeepTransform(graphScrollTF, "GraphViewport") as RectTransform;
                _graphContainerRT = FindDeepTransform(graphScrollTF, "GraphContainer") as RectTransform;
                if (_graphContainerRT != null)
                    _graphView.Init(_graphContainerRT, _font); // 위젯을 새로 만들지 않고 참조만 저장하므로 재호출해도 안전
                _graphView.OnDeleteRequested += HandleDeleteRequested;
            }

            var drawerScrollTF = FindDeepTransform(root, "ClueDrawerScroll");
            _drawerAreaRT = drawerScrollTF as RectTransform;
            var drawerColor = new Color(_listBgColor.r * 0.85f, _listBgColor.g * 0.85f, _listBgColor.b * 0.85f, _listBgColor.a);
            PanelBackground.Apply(_drawerAreaRT, drawerColor, _listBgSprite);
            _clueDrawerView = drawerScrollTF?.GetComponent<ClueDrawerView>();
            if (_clueDrawerView != null)
            {
                _drawerViewportRT = FindDeepTransform(drawerScrollTF, "Viewport") as RectTransform;
                var contentTF = FindDeepTransform(drawerScrollTF, "Content");
                _clueDrawerView.Init((RectTransform)contentTF, _font);
                _clueDrawerView.OnClueDropped += HandleClueDropped;
                _clueDrawerView.OnFilterButtonClicked += HandleFilterButtonClicked;
            }

            var drawerTabTF = FindDeepTransform(root, "DrawerToggleTab");
            PanelBackground.Apply(drawerTabTF as RectTransform, drawerColor, _listBgSprite);
            drawerTabTF?.GetComponent<Button>()?.onClick.AddListener(() => SetDrawerOpen(!_drawerOpen));
            _drawerToggleArrowTMP = drawerTabTF?.GetComponentInChildren<TextMeshProUGUI>();
            UpdateDrawerLayout(); // 재사용 경로에서도 현재 _drawerOpen 상태(기본 true)에 맞춰 폭/Viewport 활성 상태를 강제

            FindDeepTransform(root, "BtnBoardWindow")?.GetComponent<Button>()?.onClick.AddListener(OpenBoardWindow);

            _boardWindow = root.GetComponent<NoteBoardWindow>();
            if (_boardWindow != null)
            {
                _boardWindow.Bind(root, _font);
                _boardWindow.OnSaveRequested += HandleBoardSaveRequested;
                _boardWindow.OnLoadRequested += HandleBoardLoadRequested;
                _boardWindow.OnDeleteRequested += HandleBoardDeleteRequested;
            }

            _keywordFilterWindow = root.GetComponent<ClueKeywordFilterWindow>();
            if (_keywordFilterWindow != null)
            {
                _keywordFilterWindow.Bind(root, _font);
                _keywordFilterWindow.OnKeywordToggled += HandleFilterKeywordToggled;
                _keywordFilterWindow.OnClearAllRequested += HandleFilterClearAllRequested;
            }

            FindDeepTransform(root, "BtnClueCreate")?.GetComponent<Button>()?.onClick.AddListener(OpenClueCreateWindow);

            var linkModeBtnTF = FindDeepTransform(root, "BtnLinkMode");
            linkModeBtnTF?.GetComponent<Button>()?.onClick.AddListener(ToggleLinkMode);
            _linkModeBtnImg = linkModeBtnTF?.GetComponent<Image>();
            RefreshLinkModeButton();

            _clueCreateWindow = root.GetComponent<NoteClueCreateWindow>();
            if (_clueCreateWindow != null)
            {
                _clueCreateWindow.Bind(root, _font);
                _clueCreateWindow.OnCreateRequested += HandleClueCreateRequested;
            }
        }

        private void BuildTopBar(RectTransform root)
        {
            var topBar = NewRect(root, "TopBar");
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = Vector2.one;
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, _topBarHeight);
            topBar.anchoredPosition = Vector2.zero;
            PanelBackground.Apply(topBar, _listBgColor, _listBgSprite);

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
            titleTmp.text = "노트";
            titleTmp.fontSize = 8f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var boardBtnRT = NewRect(topBar, "BtnBoardWindow");
            var boardLe = boardBtnRT.gameObject.AddComponent<LayoutElement>();
            boardLe.preferredWidth = 56f;
            boardLe.flexibleWidth = 0f;
            var boardImg = AddImg(boardBtnRT, new Color(0.20f, 0.28f, 0.42f));
            var boardBtn = boardBtnRT.gameObject.AddComponent<Button>();
            boardBtn.targetGraphic = boardImg;
            boardBtn.transition = Selectable.Transition.None;
            boardBtn.onClick.AddListener(OpenBoardWindow);

            var boardTxtRT = NewRect(boardBtnRT, "Text");
            StretchFull(boardTxtRT);
            var boardTmp = boardTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) boardTmp.font = _font;
            boardTmp.text = "저장한 루트";
            boardTmp.fontSize = 7f;
            boardTmp.alignment = TextAlignmentOptions.Center;
            boardTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            boardTmp.color = Color.white;

            var createBtnRT = NewRect(topBar, "BtnClueCreate");
            var createLe = createBtnRT.gameObject.AddComponent<LayoutElement>();
            createLe.preferredWidth = 48f;
            createLe.flexibleWidth = 0f;
            var createImg = AddImg(createBtnRT, new Color(0.20f, 0.28f, 0.42f));
            var createBtn = createBtnRT.gameObject.AddComponent<Button>();
            createBtn.targetGraphic = createImg;
            createBtn.transition = Selectable.Transition.None;
            createBtn.onClick.AddListener(OpenClueCreateWindow);

            var createTxtRT = NewRect(createBtnRT, "Text");
            StretchFull(createTxtRT);
            var createTmp = createTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) createTmp.font = _font;
            createTmp.text = "단서 생성";
            createTmp.fontSize = 7f;
            createTmp.alignment = TextAlignmentOptions.Center;
            createTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            createTmp.color = Color.white;

            var linkBtnRT = NewRect(topBar, "BtnLinkMode");
            var linkLe = linkBtnRT.gameObject.AddComponent<LayoutElement>();
            linkLe.preferredWidth = 48f;
            linkLe.flexibleWidth = 0f;
            _linkModeBtnImg = AddImg(linkBtnRT, new Color(0.20f, 0.28f, 0.42f));
            var linkBtn = linkBtnRT.gameObject.AddComponent<Button>();
            linkBtn.targetGraphic = _linkModeBtnImg;
            linkBtn.transition = Selectable.Transition.None;
            linkBtn.onClick.AddListener(ToggleLinkMode);

            var linkTxtRT = NewRect(linkBtnRT, "Text");
            StretchFull(linkTxtRT);
            var linkTmp = linkTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) linkTmp.font = _font;
            linkTmp.text = "단서 연동";
            linkTmp.fontSize = 7f;
            linkTmp.alignment = TextAlignmentOptions.Center;
            linkTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            linkTmp.color = Color.white;

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
            closeTmp.text = $"닫기 [{_toggleKey}]";
            closeTmp.fontSize = 7f;
            closeTmp.alignment = TextAlignmentOptions.Center;
            closeTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            closeTmp.color = Color.white;
        }

        // 좌측: 노트 항목(경로 체인 + 카드, NoteRouteGraphView) — [2026-07-21, 요청으로 추가] 맵(MapViewer)
        // 처럼 휠 줌 + 좌클릭 드래그 패닝 가능. 우측: 단서 서랍(ClueDrawerView) — 현재 획득한 단서를
        // 검색/키워드 필터로 훑어보고 드래그해서 좌측 그래프에 배치하며, [2026-07-21, 요청으로 추가]
        // 맵 사이드패널처럼 접고 펼 수 있다.
        private void BuildList(RectTransform root)
        {
            float drawerW = _drawerOpen ? _drawerAreaWidth : _drawerCollapsedWidth;

            var graphArea = NewRect(root, "GraphScroll");
            graphArea.anchorMin = Vector2.zero;
            graphArea.anchorMax = Vector2.one;
            graphArea.offsetMin = Vector2.zero;
            graphArea.offsetMax = new Vector2(-drawerW, -_topBarHeight);
            _graphAreaRT = graphArea;
            BuildGraphPanZoom(graphArea);

            var drawerColor = new Color(_listBgColor.r * 0.85f, _listBgColor.g * 0.85f, _listBgColor.b * 0.85f, _listBgColor.a);

            var drawerArea = NewRect(root, "ClueDrawerScroll");
            drawerArea.anchorMin = new Vector2(1f, 0f);
            drawerArea.anchorMax = Vector2.one;
            drawerArea.offsetMin = new Vector2(-drawerW, 0f);
            drawerArea.offsetMax = new Vector2(0f, -_topBarHeight);
            PanelBackground.Apply(drawerArea, drawerColor, _listBgSprite);
            _drawerAreaRT = drawerArea;

            _clueDrawerView = BuildScrollingList<ClueDrawerView>(drawerArea, out var drawerContent);
            _drawerViewportRT = (RectTransform)drawerArea.Find("Viewport");
            _clueDrawerView.Init(drawerContent, _font);
            _clueDrawerView.OnClueDropped += HandleClueDropped;
            _clueDrawerView.OnFilterButtonClicked += HandleFilterButtonClicked;

            BuildDrawerToggleTab(drawerArea, drawerColor, _listBgSprite);
            UpdateDrawerLayout();
        }

        // GraphScroll 내부에 지도(MapViewer.BuildScrollGraph)와 동일한 구조로 팬·줌 가능한 그래프 영역을
        // 짓는다 — GraphPanZoom(휠 줌 + 좌클릭 드래그 패닝)을 GraphArea 자체에 붙이고, 그 안에 마스킹용
        // GraphViewport + 실제 이동/확대 대상인 GraphContainer를 둔다. NoteRouteGraphView는 이미 프리폼
        // 배치 방식이라 이 GraphContainer를 그대로 "Content"로 받아 노드를 배치하는 것 외에 달라지는 게
        // 없다(예전엔 ScrollRect가 좌표를 옮겼다면 이제는 GraphPanZoom이 옮긴다는 차이뿐).
        private void BuildGraphPanZoom(RectTransform graphArea)
        {
            PanelBackground.Apply(graphArea, _listBgColor, _listBgSprite);
            _graphPanZoom = graphArea.gameObject.AddComponent<GraphPanZoom>();
            // [요청, 2026-07-21] 지도(MapViewer)와 달리 노트 그래프는 콘텐츠 크기가 고정폭(900x300 시작,
            // 세로만 SetData 때마다 재계산)이라 무제한 팬을 허용하면 빈 허공으로 한없이 밀려날 수 있다 —
            // 이 인스턴스에 한해 팬 범위를 제한한다(MapViewer의 GraphPanZoom은 건드리지 않아 그대로 무제한).
            _graphPanZoom.ConfigureBounds(true, 120f);

            var viewport = NewRect(graphArea, "GraphViewport");
            StretchFull(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            _graphViewportRT = viewport;

            var container = NewRect(viewport, "GraphContainer");
            container.anchorMin = container.anchorMax = Vector2.zero;
            container.pivot     = Vector2.zero;
            // NoteNodeDragHandle이 이 폭(Bounds.rect.width)을 기준으로 노드 드래그 가능 범위를 클램프하고,
            // GraphPanZoom.ConfigureBounds(팬 범위 클램프)도 이 크기를 기준으로 동작한다 — 기본 노드
            // 배치보다 충분히 넉넉한 고정 크기로 두고 이후 절대 건드리지 않는다. [버그 수정, 2026-07-21]
            // 원래는 세로를 NoteRouteGraphView.UpdateContentSize가 SetData 때마다 그래프 높이에 맞춰
            // 다시 계산해 덮어쓰게 했었는데, 이 컨테이너는 pivot=(0,0)이라 세로 크기가 커질 때마다
            // 좌하단은 고정된 채 위쪽 모서리가 밀려 올라가고, 그 모서리에 앵커된 그래프 내용(노드 전체)이
            // 통째로 같이 떠밀려 올라가는 버그(노드를 위/아래로 드래그하면 배경까지 같이 팬되는 것처럼
            // 보임)로 이어졌다 — 자세한 원인은 NoteRouteGraphView.UpdateContentSize 주석 참고. 지금은
            // 고정 크기로 두고 절대 변경하지 않는다.
            container.sizeDelta = new Vector2(900f, 300f);
            _graphContainerRT = container;

            _graphView = graphArea.gameObject.AddComponent<NoteRouteGraphView>();
            _graphView.Init(container, _font);
            _graphView.OnDeleteRequested += HandleDeleteRequested;
        }

        // ─── 우측 단서 서랍 열기/닫기 ───────────────────────────────
        // MapViewer의 사이드패널 접기/펼치기와 동일한 패턴 — 접으면 그래프 영역이 그만큼 넓어지고,
        // 접힌 상태에서도 재오픈용 작은 탭(DrawerToggleTab)은 항상 보인다.

        private void SetDrawerOpen(bool open)
        {
            _drawerOpen = open;
            UpdateDrawerLayout();
            if (_drawerToggleArrowTMP != null) _drawerToggleArrowTMP.text = open ? "◀" : "▶";
        }

        private void UpdateDrawerLayout()
        {
            float w = _drawerOpen ? _drawerAreaWidth : _drawerCollapsedWidth;
            if (_drawerAreaRT != null) _drawerAreaRT.offsetMin = new Vector2(-w, 0f);
            if (_graphAreaRT != null) _graphAreaRT.offsetMax = new Vector2(-w, -_topBarHeight);
            _drawerViewportRT?.gameObject.SetActive(_drawerOpen);
        }

        // 서랍 좌측 가장자리에 항상 떠 있는 재오픈/접기 탭 — MapViewer.BuildSidePanelToggleTab과 동일한
        // 이유로 Viewport(스크롤뷰)와 별개 오브젝트에 둬서, 서랍이 접혀 Viewport가 비활성화돼도 계속
        // 보이고 클릭할 수 있다.
        private void BuildDrawerToggleTab(RectTransform parent, Color bgColor, Sprite bgSprite)
        {
            var tab = NewRect(parent, "DrawerToggleTab");
            tab.anchorMin = new Vector2(0f, 0.5f);
            tab.anchorMax = new Vector2(0f, 0.5f);
            tab.pivot     = new Vector2(1f, 0.5f);
            tab.sizeDelta = new Vector2(12f, 28f);
            tab.anchoredPosition = Vector2.zero;

            var img = PanelBackground.Apply(tab, bgColor, bgSprite);
            var btn = tab.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SetDrawerOpen(!_drawerOpen));

            var lblRT = NewRect(tab, "Arrow");
            StretchFull(lblRT);
            var tmp = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = 8f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;
            tmp.text = "◀";
            _drawerToggleArrowTMP = tmp;
        }

        // 상단 툴바 "저장한 루트" 버튼이 여는 모달 창 — CodexPanel의 BuildMemoForm과 동일한 패턴으로
        // 패널 루트 자체에 컴포넌트를 붙이고, 그 안에서 자기 완결적인 오버레이 자식(BoardWindowOverlay)을 짓는다.
        private void BuildBoardWindow(RectTransform root)
        {
            _boardWindow = root.gameObject.AddComponent<NoteBoardWindow>();
            _boardWindow.Init(root, _font);
            _boardWindow.OnSaveRequested += HandleBoardSaveRequested;
            _boardWindow.OnLoadRequested += HandleBoardLoadRequested;
            _boardWindow.OnDeleteRequested += HandleBoardDeleteRequested;
        }

        // 단서 서랍(우측)의 "필터" 버튼이 여는 키워드 다중 선택 창 — BuildBoardWindow와 동일한 이유로
        // 패널 루트에 직접 컴포넌트를 붙인다(ClueKeywordFilterWindow.cs 상단 주석 참고 — 서랍의
        // ScrollRect 안에 두면 마스크에 잘리고, NotePanel을 닫아도 같이 안 꺼지는 문제가 있었다).
        private void BuildKeywordFilterWindow(RectTransform root)
        {
            _keywordFilterWindow = root.gameObject.AddComponent<ClueKeywordFilterWindow>();
            _keywordFilterWindow.Init(root, _font);
            _keywordFilterWindow.OnKeywordToggled += HandleFilterKeywordToggled;
            _keywordFilterWindow.OnClearAllRequested += HandleFilterClearAllRequested;
        }

        // 상단 툴바 "단서 생성" 버튼이 여는 입력 창 — BuildBoardWindow/BuildKeywordFilterWindow와 동일한
        // 이유로 패널 루트에 직접 컴포넌트를 붙인다.
        private void BuildClueCreateWindow(RectTransform root)
        {
            _clueCreateWindow = root.gameObject.AddComponent<NoteClueCreateWindow>();
            _clueCreateWindow.Init(root, _font);
            _clueCreateWindow.OnCreateRequested += HandleClueCreateRequested;
        }

        // GraphScroll/ClueDrawerScroll이 공유하는 뼈대(ScrollRect+Viewport+Content)를 만들고,
        // 그 Content 위에 T 컴포넌트를 붙여 반환한다.
        private static T BuildScrollingList<T>(RectTransform area, out RectTransform content) where T : Component
        {
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;

            var vp = NewRect(area, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vp;

            content = NewRect(vp, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            return area.gameObject.AddComponent<T>();
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
