using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    // 4단계(2026-07-14, 완료): 우측에 RoutePlanEditorView를 추가해 다중 목적지 이동 계획(핵심 산출물)을
    // 편집·실행한다. 좌측 카드의 "계획에 추가" 버튼(targetMapGuid가 있는 항목)이 우측 계획에 목적지를 담는
    // 다리 역할 — NotePanel.HandleAddToPlanRequested 참고.
    public class NotePanel : MonoBehaviour
    {
        [Header("조작")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.V;

        [Header("폰트")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("레이아웃")]
        [SerializeField] private float _topBarHeight = 12f;
        [SerializeField] private Color _rootBgColor = new(0.04f, 0.04f, 0.08f, 0.96f);
        [SerializeField] private Color _listBgColor = new(0.07f, 0.09f, 0.14f, 0.97f);

        [SerializeField] private float _planAreaWidthRatio = 0.42f; // 우측 계획 편집 영역이 차지하는 비율

        [Header("프리팹 (선택 — 비워두면 런타임 자동 생성)")]
        [SerializeField] private GameObject _panelPrefab;

        private GameObject _panelGO;
        private NoteRouteGraphView _graphView;
        private RoutePlanEditorView _planEditorView;
        private InputManager _inputManager;

        private void Awake()
        {
            var rt = GetComponent<RectTransform>();
            if (rt != null) StretchFull(rt);
            BuildUI();
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
            Refresh();
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

        // Editor 스크립트(NotePanelEditor)에서 프리팹 저장 시 접근.
        public GameObject GetPanelGO() => _panelGO;

        // ─── 데이터 연동 ──────────────────────────────────────────

        private void Refresh()
        {
            var route = RouteModule.Instance?.SelectedRoute;
            Debug.Log($"[NotePanel] Refresh() — route={(route == null ? "null" : $"valid={route.IsValid}")}, entries={NoteModule.Instance.Entries.Count}, _graphView={(_graphView != null)}");
            _graphView.SetData(route, NoteModule.Instance.Entries);
            _planEditorView.Refresh();
        }

        private void HandleRouteSelected(PathResult _) => Refresh();

        // 이동 중 잠금은 NoteModule.RemoveEntry 내부에서 판단·경고한다 — 여기서는 그대로 위임만 한다.
        private void HandleDeleteRequested(NoteEntry entry) => NoteModule.Instance.RemoveEntry(entry.clueId);

        // 4단계: 노트 카드의 "계획에 추가" 액션 — 그 단서의 targetMapGuid를 현재 편집 중인 계획에 담는다.
        private void HandleAddToPlanRequested(NoteEntry entry)
        {
            var clue = MapGraph.Instance?.GetClue(entry.clueId);
            if (clue == null || string.IsNullOrEmpty(clue.targetMapGuid)) return;
            _planEditorView.AddMapToSelectedPlan(clue.targetMapGuid);
        }

        // ─── UI 구축 ─────────────────────────────────────────────

        private void BuildUI()
        {
            // 씬 계층에 NotePanelRoot가 이미 자식으로 배치돼 있으면 재사용 (CodexPanel.BuildUI와 동일한 패턴).
            // GraphArea(2026-07-14, 노드-간선 그래프 재작업으로 가장 최근에 추가된 요소)가 없으면 구버전으로
            // 보고 파괴 후 재생성 — "판정 기준은 항상 가장 최근에 추가된 요소여야 그 이전 요소까지 전부
            // 갖췄다는 게 보장된다"(Clue_System.md 4-5장의 MemoFormOverlay/PinRow 판정과 같은 이유,
            // MapViewer의 BtnSelectRoute 마커 갱신과 동일한 이유로 PlanScroll에서 GraphArea로 교체).
            var existing = transform.Find("NotePanelRoot");
            if (existing != null)
            {
                bool existingCurrent = FindDeepTransform(existing, "GraphArea") != null;
                if (existingCurrent)
                {
                    Debug.Log("[NotePanel] BuildUI: 씬에 있던 기존 NotePanelRoot를 재사용합니다.");
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.Log("[NotePanel] BuildUI: 기존 NotePanelRoot에 GraphArea가 없어 구버전으로 판단, 파괴 후 재생성합니다.");
                // CodexDrawerTreeView.Clear와 동일한 이유로 먼저 비활성화한 뒤 Destroy.
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            // 프리팹이 지정되어 있으면 인스턴스화 후 참조를 바인딩하고 콜백만 재연결.
            if (_panelPrefab != null)
            {
                bool prefabCurrent = FindDeepTransform(_panelPrefab.transform, "GraphArea") != null;
                if (prefabCurrent)
                {
                    Debug.Log($"[NotePanel] BuildUI: 지정된 프리팹({_panelPrefab.name})을 인스턴스화합니다.");
                    _panelGO = Instantiate(_panelPrefab, transform, false);
                    _panelGO.name = "NotePanelRoot";
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Debug.LogWarning($"[NotePanel] BuildUI: 지정된 프리팹({_panelPrefab.name})에 GraphArea가 없어 구버전으로 판단, 런타임 생성으로 대체합니다. 프리팹을 다시 생성해주세요.");
            }

            // ── 프리팹 없음 → 런타임 자동 생성 ──
            Debug.Log(_panelPrefab == null
                ? "[NotePanel] BuildUI: _panelPrefab이 비어 있어 런타임으로 새로 생성합니다."
                : "[NotePanel] BuildUI: (위 경고 참고) 런타임으로 새로 생성합니다.");
            _panelGO = new GameObject("NotePanelRoot");
            _panelGO.transform.SetParent(transform, false);
            var root = _panelGO.AddComponent<RectTransform>();
            StretchFull(root);
            AddImg(root, _rootBgColor);

            BuildTopBar(root);
            BuildList(root);
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
            FindDeepTransform(root, "BtnClose")?.GetComponent<Button>()?.onClick.AddListener(Close);

            var graphScrollTF = FindDeepTransform(root, "GraphScroll");
            _graphView = graphScrollTF?.GetComponent<NoteRouteGraphView>();
            if (_graphView != null)
            {
                var contentTF = FindDeepTransform(graphScrollTF, "Content");
                _graphView.Init((RectTransform)contentTF, _font); // 위젯을 새로 만들지 않고 참조만 저장하므로 재호출해도 안전
                _graphView.OnDeleteRequested += HandleDeleteRequested;
                _graphView.OnAddToPlanRequested += HandleAddToPlanRequested;
            }

            var planScrollTF = FindDeepTransform(root, "PlanScroll");
            _planEditorView = planScrollTF?.GetComponent<RoutePlanEditorView>();
            if (_planEditorView != null)
            {
                var contentTF = FindDeepTransform(planScrollTF, "Content");
                _planEditorView.Init((RectTransform)contentTF, _font);
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
            AddImg(topBar, _listBgColor);

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

        // 좌측: 노트 항목(경로 체인 + 카드, NoteRouteGraphView). 우측: 다중 목적지 이동 계획 편집·실행
        // (RoutePlanEditorView, 4단계 — 이 시스템의 핵심 산출물).
        private void BuildList(RectTransform root)
        {
            var graphArea = NewRect(root, "GraphScroll");
            graphArea.anchorMin = Vector2.zero;
            graphArea.anchorMax = new Vector2(1f - _planAreaWidthRatio, 1f);
            graphArea.offsetMin = Vector2.zero;
            graphArea.offsetMax = new Vector2(0f, -_topBarHeight);
            AddImg(graphArea, _listBgColor);

            _graphView = BuildScrollingList<NoteRouteGraphView>(graphArea, out var graphContent);
            _graphView.Init(graphContent, _font);
            _graphView.OnDeleteRequested += HandleDeleteRequested;
            _graphView.OnAddToPlanRequested += HandleAddToPlanRequested;

            var planArea = NewRect(root, "PlanScroll");
            planArea.anchorMin = new Vector2(1f - _planAreaWidthRatio, 0f);
            planArea.anchorMax = Vector2.one;
            planArea.offsetMin = Vector2.zero;
            planArea.offsetMax = new Vector2(0f, -_topBarHeight);
            AddImg(planArea, new Color(_listBgColor.r * 0.85f, _listBgColor.g * 0.85f, _listBgColor.b * 0.85f, _listBgColor.a));

            _planEditorView = BuildScrollingList<RoutePlanEditorView>(planArea, out var planContent);
            _planEditorView.Init(planContent, _font);
        }

        // GraphScroll/PlanScroll이 공유하는 뼈대(ScrollRect+Viewport+Content)를 만들고,
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
