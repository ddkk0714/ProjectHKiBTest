using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RouteFinding.Note;
using RouteFinding.Codex;

namespace RouteFinding.MapView
{
    // 씬 내 맵 뷰어 창 — 루트파인딩 시스템의 "그리기" 담당.
    // Canvas 직속 자식 GO에 이 컴포넌트를 붙인다. (BaseWindow와 동일한 계층 구조)
    // 이 GO 자체는 항상 활성 — M키 토글은 InputManager.onOpenMap 구독으로 받는다(2026-07-28,
    // PlayerAction.inputactions의 UI_TOGGLE 액션맵 참고. 이전엔 Update()에서 Input.GetKeyDown 폴링).
    // 내부 패널(_panelGO)만 Open/Close 시 토글된다.
    //
    // 상태를 직접 소유하지 않는다:
    //   - 장착 장비, 진행 상태(방문/단서/클리어), 경로 규칙 → RouteModule이 소유
    //   - 이 클래스는 모듈의 상태를 읽어 노드·엣지 색상과 패널 텍스트만 갱신한다
    //   - 장비 버튼 클릭 등 입력은 모듈에 위임하고 결과를 다시 그린다
    //
    // 의존: MapGraph(씬 배치 필요), RouteModule(없으면 자동 생성)
    //
    // ▸ 선행 이벤트 조건 표시 (2026-07-26 신규, ShowNodeTooltip)
    //   노드 툴팁에서, 그 노드에 딸린 단서 중 아직 못 얻은(ClueData.requiredEventKey가 있고
    //   RouteProgressState.HasEventFlag가 아직 false인) 것이 있으면 "[미공개 단서 N개 - 특정
    //   이벤트 필요]"를 회색으로 보여준다 — 이름/내용은 스포일러라 노출 안 함, 존재/잠김 여부만.
    //   콘텐츠 쪽에서 새 단서에 requiredEventKey를 채우기만 하면 이 표시는 자동으로 따라온다.
    public class MapViewer : MonoBehaviour
    {
        [Header("폰트")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("그래프 레이아웃")]
        [SerializeField] private float _nodeSize     =  8f;
        [SerializeField] private float _nodeHitSizeMultiplier = 2.5f; // 클릭 판정 영역 = 노드 시각 크기 × 이 값 (최소 1배 보장)
        [SerializeField] private float _graphPadding =  8f;
        [SerializeField] private float _graphWidth   = 230f;
        [SerializeField] private float _graphHeight  = 230f;

        [Header("Graph Panel UI 레이아웃 (런타임 자동 생성 시)")]
        [SerializeField] private float _sidePanelWidth = 100f;
        [SerializeField] private float _sidePanelCollapsedWidth = 12f; // 접었을 때 남는 재오픈용 탭 폭
        [SerializeField] private float _toolbarHeight = 18f;
        [SerializeField] private float _graphAreaMarginLeft;
        [SerializeField] private float _graphAreaMarginRight;
        [SerializeField] private float _graphAreaMarginTop;
        [SerializeField] private float _graphAreaMarginBottom;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(패널 전체 바깥 배경)")]
        [SerializeField] private Color _rootBgColor      = new Color(0.04f, 0.04f, 0.08f, 0.94f);
        [Tooltip("패널 전체 바깥 배경 이미지 — 지정하면 도트풍 이미지로 대체(9슬라이스 테두리 있는 스프라이트도 지원), 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _rootBgSprite;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(우측 사이드패널 + 상단 툴바 + 재오픈 탭이 같이 씀)")]
        [SerializeField] private Color _sidePanelBgColor = new Color(0.07f, 0.09f, 0.14f, 0.97f);
        [Tooltip("사이드패널 + 상단 툴바 + 재오픈 탭 배경 이미지 — 셋이 이 스프라이트를 같이 씀, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _sidePanelBgSprite;
        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(그래프/지도가 그려지는 영역)")]
        [SerializeField] private Color _graphAreaBgColor = new Color(0.06f, 0.07f, 0.11f, 0.80f);
        [Tooltip("그래프/지도 영역 배경 이미지 — 지정하면 도트풍 이미지로 대체, 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _graphAreaBgSprite;

        [Header("경로 강조 스타일 (노드 선택 시)")]
        [SerializeField] private Color _pathHighlightColor     = new Color(1.00f, 0.82f, 0.08f, 1.00f);
        [SerializeField] private float _edgeThicknessNormal    = 3f;
        [SerializeField] private float _edgeThicknessHighlight = 7f;

        [Header("드롭다운 스타일 (출발/도착 SimpleDropdown)")]
        [SerializeField] private Color _dropdownBgColor            = new Color(0.17f, 0.21f, 0.30f, 1f); // 드롭다운 본체(캡션 박스) 배경
        [SerializeField] private Color _dropdownOptionsListBgColor = new Color(0.12f, 0.14f, 0.20f, 0.97f); // 펼쳐지는 옵션 목록 배경 — 2026-07-07 진단용으로 넣었던 밝은 노란색을 정상 색으로 교체
        [SerializeField] private Color _dropdownCaptionColor       = Color.white;
        [SerializeField] private float _dropdownCaptionFontSize    = 7f;
        [SerializeField] private float _dropdownOptionFontSize     = 7f;
        [SerializeField] private float _dropdownOptionHeight       = 14f;
        [SerializeField] private float _dropdownOptionsListMaxHeight = 90f;

        [Header("프리팹 (선택 — 비워두면 런타임 자동 생성)")]
        [SerializeField] private GameObject _panelPrefab;

        // ─── 런타임 상태 ─────────────────────────────────────────
        private GameObject    _panelGO;          // Open/Close 토글 대상 (Canvas 아님)
        private RectTransform _graphContainer;
        private RectTransform _graphViewport;
        private RectTransform _labelContainer;

        private readonly Dictionary<string, MapNodeView> _nodeViews = new();
        private readonly List<MapEdgeView>               _edgeViews = new();
        private readonly Dictionary<string, Vector2>     _nodePositions = new();
        private readonly Dictionary<string, ClueMarkerView> _clueMarkerViews = new();

        private RectTransform   _tooltipRT;
        private TextMeshProUGUI _tooltipTMP;
        private RectTransform   _clueListContent;
        private readonly HashSet<string> _expandedClueIds = new(); // 사이드패널 단서 목록에서 펼쳐둔 단서 id (Refresh()로 목록이 재생성돼도 유지)

        // 뷰가 가지는 것은 "화면 표시" 상태뿐 — 목적지 선택, 경로 방식, 탐색 결과 캐시.
        // 장비·진행 상태 같은 공용 상태는 RouteModule이 소유한다.
        private MapNodeView            _selectedDest;
        private MapNodeData            _originNode; // 출발지 — 기본값 MapGraph.StartNode, 상단 툴바 드롭다운으로 변경 가능
        private PathType               _pathType    = PathType.Shortest;
        private PathResult             _currentPath;

        private TextMeshProUGUI _pathInfoTMP;
        private TextMeshProUGUI _gearListTMP;

        private Image        _btnShortestImg;
        private Image        _btnMinDiffImg;
        private Image        _btnBalancedImg;
        private Image        _btnAllowNoClueImg;
        private Image        _btnAvoidNoClueImg;
        private Image        _btnSelectRouteImg;
        private GraphPanZoom _graphPanZoom;
        private InputManager _inputManager;

        private readonly Dictionary<EmotionColor, Image> _gearBtnImages = new();

        // ─── 사이드패널 열기/닫기 ───────────────────────────────────
        private RectTransform   _sidePanelRT;
        private RectTransform   _graphAreaRT;
        private RectTransform   _sidePanelScrollGO; // Viewport — 접혔을 때 비활성화 대상
        private TextMeshProUGUI _sidePanelToggleArrowTMP;
        private Image           _toolbarPanelToggleImg;
        private bool            _sidePanelOpen = true;

        // ─── 상단 툴바 (출발/도착 드롭다운) ─────────────────────────
        // TMP_Dropdown이 동적으로 만드는 중첩 캔버스가 이 프로젝트의 카메라(Cinemachine)/렌더 설정과
        // 맞물려 화면에 안 뜨는 문제가 있어(2026-07-07), 별도 캔버스·Instantiate 없이 기존 UI 계층에
        // 그대로 얹는 자체 드롭다운(SimpleDropdown)으로 교체했다.
        private RectTransform _toolbarRT;
        private SimpleDropdown _originDropdown;
        private SimpleDropdown _destDropdown;
        private readonly List<MapNodeData> _knownNodesForDropdown = new();

        // 캔버스/Instantiate 없이 같은 계층에 얹는 최소 드롭다운. 옵션 목록(OptionsList)은 평소엔
        // 비활성화돼 있다가 클릭 시 SetActive(true)로 펼쳐진다 — TMP_Dropdown처럼 새 GameObject를
        // 복제해서 별도 캔버스에 띄우는 방식이 아니라, 항상 같은 부모 밑에 존재하는 형제 오브젝트다.
        private class SimpleDropdown
        {
            public RectTransform    Root;
            public TextMeshProUGUI  Caption;
            public RectTransform    OptionsList;
            public RectTransform    OptionsContent;
            public int              SelectedIndex = -1;
            public Action<int>      OnSelected;
        }

        // ─── Lifecycle ───────────────────────────────────────────

        private void Awake()
        {
            // BuildUI()가 닫기 버튼 라벨(ToggleKeyLabel)을 실제 바인딩 키로 채우려면 그 전에
            // _inputManager가 준비돼 있어야 한다 — 그래서 BuildUI()보다 먼저 할당한다.
            _inputManager = FindObjectOfType<InputManager>();

            var rt = GetComponent<RectTransform>();
            if (rt != null) StretchFull(rt);
            BuildUI();
            var canvas = GetComponentInParent<Canvas>();
            _graphPanZoom?.Init(_graphContainer, _graphViewport, canvas);

            // [2026-07-28] 레거시 Input.GetKeyDown 폴링 대신 Input System의 UI_TOGGLE 액션맵을
            // 구독한다 — 이 맵은 PLAY/MENU/GRAFFITI 모드 전환과 무관하게 항상 켜져 있어(InputManager
            // 참고) 이전과 동일하게 어느 모드에서든 M키로 토글된다.
            if (_inputManager != null) _inputManager.onOpenMap += HandleOpenMapInput;
        }

        private void OnDestroy()
        {
            if (_inputManager != null) _inputManager.onOpenMap -= HandleOpenMapInput;
        }

        private void HandleOpenMapInput(InputAction.CallbackContext context)
        {
            if (context.performed) Toggle();
        }

        // "닫기 [M]" 같은 UI 라벨용 — 실제 바인딩된 키를 UI_TOGGLE/OpenMap 액션에서 직접 읽어온다.
        // 고정 KeyCode 필드로 따로 들고 있으면 나중에 PlayerAction.inputactions에서 키를 바꿨을 때
        // 라벨이 안 따라가서 실제 입력과 어긋나는 문제가 있었다(2026-07-28) — 그래서 필드 대신 항상
        // 실시간으로 조회한다.
        private string ToggleKeyLabel
            => _inputManager != null ? _inputManager.inputs.UI_TOGGLE.OpenMap.GetBindingDisplayString() : "M";

        private void Start()
        {
            if (MapGraph.Instance != null)
                PopulateGraph();
            _panelGO.SetActive(false);
        }

        // ─── Public API ──────────────────────────────────────────

        public void Open()
        {
            // 기획 규칙(이동 중 지도 열람 불가)의 판단은 모듈의 몫 — 뷰는 묻기만 한다.
            if (!RouteModule.Instance.CanOpenMap)
            {
                Debug.LogWarning("[MapViewer] 이동 중에는 지도를 열 수 없습니다.");
                return;
            }
            ExclusivePanelGroup.NotifyOpening(this, Close); // 노트/도감 등 다른 패널이 열려 있으면 먼저 닫는다
            Debug.Log($"[MapViewer] Open() — nodeViews={_nodeViews.Count}  edgeViews={_edgeViews.Count}  MapGraph={MapGraph.Instance != null}");
            if (_nodeViews.Count == 0 && MapGraph.Instance != null)
                PopulateGraph();
            _panelGO.SetActive(true);
            _inputManager?.MENUMode();
            Refresh();
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

        // "노트로 이동" 툴바 버튼 — 맵을 닫고 씬에 배치된 노트 패널을 연다.
        // (2026-07-14 신설) 노트 패널을 직접 참조하지 않고 씬에서 찾는다 — MapViewer는 노트의 존재를 몰라도 되게 유지.
        // (2026-07-14 추가) "경로 선택"을 누르는 걸 잊어도 노트에 반영되도록, 지금 미리보기 중인 경로를
        // 먼저 자동으로 커밋한다 — SelectCurrentPath()는 커밋할 게 없거나(막힘+대안없음) 이미 같은 경로가
        // 커밋돼 있으면 경고만 남기고 조용히 넘어가므로 여기서 특별히 결과를 검사할 필요는 없다.
        private void GoToNote()
        {
            var notePanel = FindObjectOfType<NotePanel>();
            if (notePanel == null)
            {
                Debug.LogWarning("[MapViewer] 씬에서 NotePanel을 찾을 수 없습니다.");
                return;
            }
            SelectCurrentPath();
            Close();
            notePanel.Open();
        }

        // "도감" 툴바 버튼 — 맵을 닫고 씬에 배치된 도감 패널을 연다 (Clue_System.md 5단계).
        // 노트 이동(GoToNote)과 동일한 패턴: 도감 패널을 직접 참조하지 않고 씬에서 찾는다.
        // 경로 자동 커밋은 하지 않는다 — 도감은 노트와 달리 경로 계획과 무관한 순수 열람 기능이라
        // "노트로 이동"의 SelectCurrentPath() 자동 호출이 여기엔 해당하지 않는다.
        private void GoToCodex()
        {
            var codexPanel = FindObjectOfType<CodexPanel>();
            if (codexPanel == null)
            {
                Debug.LogWarning("[MapViewer] 씬에서 CodexPanel을 찾을 수 없습니다.");
                return;
            }
            Close();
            codexPanel.Open();
        }

        // Editor 스크립트에서 프리팹 저장 시 접근
        public GameObject GetPanelGO() => _panelGO;

        // ─── 그래프 요소 생성 ─────────────────────────────────────

        private void PopulateGraph()
        {
            if (MapGraph.Instance == null)
            {
                ShowGraphWarning("[!] MapGraph 없음\n씬에 MapGraph를 추가하세요.");
                return;
            }

            var nodes = MapGraph.Instance.AllNodes;
            if (nodes.Count == 0)
            {
                ShowGraphWarning("[!] 맵 데이터 없음\nResources/RouteFinding/map_database.json");
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in nodes)
            {
                minX = Mathf.Min(minX, n.graphPosition.x);
                minY = Mathf.Min(minY, n.graphPosition.y);
                maxX = Mathf.Max(maxX, n.graphPosition.x);
                maxY = Mathf.Max(maxY, n.graphPosition.y);
            }
            float spanX = Mathf.Max(maxX - minX, 1f);
            float spanY = Mathf.Max(maxY - minY, 1f);

            float vpW = _graphViewport != null && _graphViewport.rect.width  > 1f ? _graphViewport.rect.width  : _graphWidth;
            float vpH = _graphViewport != null && _graphViewport.rect.height > 1f ? _graphViewport.rect.height : _graphHeight;
            _graphContainer.sizeDelta = new Vector2(vpW, vpH);

            float usableW = vpW - _graphPadding * 2f;
            float usableH = vpH - _graphPadding * 2f;

            _nodePositions.Clear();
            foreach (var n in nodes)
            {
                float cx = _graphPadding + (n.graphPosition.x - minX) / spanX * usableW;
                float cy = _graphPadding + (n.graphPosition.y - minY) / spanY * usableH;
                _nodePositions[n.guid] = new Vector2(cx, cy);
            }

            // 엣지 (노드 아래 레이어)
            foreach (var conn in MapGraph.Instance.AllConnections)
            {
                if (!_nodePositions.TryGetValue(conn.fromGuid, out var fp)) continue;
                if (!_nodePositions.TryGetValue(conn.toGuid,   out var tp)) continue;

                var edgeGO = new GameObject($"Edge_{conn.guid}");
                edgeGO.transform.SetParent(_graphContainer, false);
                var edgeRT = edgeGO.AddComponent<RectTransform>();
                edgeRT.anchorMin = edgeRT.anchorMax = Vector2.zero;
                edgeRT.pivot     = new Vector2(0.5f, 0.5f);
                edgeGO.AddComponent<Image>();

                // 2026-07-14 — 난이도 숫자 표시가 노드로 이동하면서 간선 전용 라벨은 더 이상 만들지 않는다.
                var ev = edgeGO.AddComponent<MapEdgeView>();
                ev.Init(conn);
                ev.SetHighlightStyle(_pathHighlightColor, _edgeThicknessNormal, _edgeThicknessHighlight);
                ev.SetLayout(fp, tp);
                _edgeViews.Add(ev);
            }

            // 노드 (엣지 위 레이어)
            var circleSprite = GetCircleSprite();
            foreach (var node in nodes)
            {
                if (!_nodePositions.TryGetValue(node.guid, out var pos)) continue;

                // 노드 GO 자체는 "클릭 판정 영역"이다 — 실제 눈에 보이는 크기(_nodeSize)보다
                // 넉넉하게 키워서(_nodeHitSizeMultiplier) 마우스로 누르기 쉽게 한다.
                // 눈에 보이는 그래픽은 자식 "Visual"에 원래 크기 그대로 둔다.
                float hitSize = Mathf.Max(_nodeSize, _nodeSize * _nodeHitSizeMultiplier);

                var nodeGO = new GameObject($"Node_{node.guid}");
                nodeGO.transform.SetParent(_graphContainer, false);
                var nodeRT = nodeGO.AddComponent<RectTransform>();
                nodeRT.anchorMin        = nodeRT.anchorMax = Vector2.zero;
                nodeRT.pivot            = Vector2.one * 0.5f;
                nodeRT.anchoredPosition = pos;
                nodeRT.sizeDelta        = Vector2.one * hitSize;

                // 클릭 판정용 배경 — 완전 투명이지만 raycastTarget은 그대로 true라 클릭은 정상 등록된다.
                var hitImg = nodeGO.AddComponent<Image>();
                hitImg.color = new Color(0f, 0f, 0f, 0f);
                var btn = nodeGO.AddComponent<Button>();
                btn.targetGraphic = hitImg;
                btn.transition    = Selectable.Transition.None;

                // 실제 눈에 보이는 노드 그래픽 — 클릭 판정 영역(hitSize)과 무관하게 원래 크기(_nodeSize) 유지.
                var visualGO = new GameObject("Visual");
                visualGO.transform.SetParent(nodeGO.transform, false);
                var visualRT = visualGO.AddComponent<RectTransform>();
                visualRT.anchorMin = visualRT.anchorMax = new Vector2(0.5f, 0.5f);
                visualRT.pivot            = new Vector2(0.5f, 0.5f);
                visualRT.anchoredPosition = Vector2.zero;
                visualRT.sizeDelta        = Vector2.one * _nodeSize;
                var img = visualGO.AddComponent<Image>();
                if (circleSprite != null) img.sprite = circleSprite;
                img.raycastTarget = false; // 클릭 판정은 부모(hitImg)가 담당 — 시각 그래픽은 판정에서 제외

                // 레이블: 노드 아래에 위치 (노드가 작아도 텍스트가 충분히 표시됨)
                var lblGO = new GameObject("Name");
                lblGO.transform.SetParent(nodeGO.transform, false);
                var lblRT = lblGO.AddComponent<RectTransform>();
                lblRT.anchorMin        = new Vector2(0.5f, 0f);
                lblRT.anchorMax        = new Vector2(0.5f, 0f);
                lblRT.pivot            = new Vector2(0.5f, 1f);
                lblRT.sizeDelta        = new Vector2(44f, 14f);
                lblRT.anchoredPosition = Vector2.zero;
                var tmp = lblGO.AddComponent<TextMeshProUGUI>();
                if (_font != null) tmp.font = _font;
                tmp.text               = node.nodeName;
                tmp.fontSize           = 6f;
                tmp.alignment          = TextAlignmentOptions.Center;
                tmp.verticalAlignment  = VerticalAlignmentOptions.Top;
                tmp.color              = Color.white;
                tmp.enableWordWrapping = true;
                tmp.overflowMode       = TextOverflowModes.Overflow;
                var cg = lblGO.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;

                // 2026-07-14 추가 — 난이도/통과 불가 표시가 간선에서 노드로 이동해온 라벨.
                // 노드 좌상단(고정 오프셋)에 배치 — 노드 자체는 팬/줌 시 부모(_graphContainer)와 함께
                // 움직이므로, 엣지 라벨과 달리 매 프레임 위치를 다시 계산할 필요가 없다.
                var diffGO = new GameObject($"Diff_{node.guid}");
                diffGO.transform.SetParent(_graphContainer, false);
                var diffRT = diffGO.AddComponent<RectTransform>();
                diffRT.anchorMin = diffRT.anchorMax = Vector2.zero;
                diffRT.pivot     = new Vector2(0.5f, 0.5f);
                diffRT.sizeDelta = new Vector2(24f, 10f);
                diffRT.anchoredPosition = pos + new Vector2(_nodeSize * 0.6f, _nodeSize * 0.6f);
                var diffTMP = diffGO.AddComponent<TextMeshProUGUI>();
                if (_font != null) diffTMP.font = _font;
                diffTMP.fontSize  = 8f;
                diffTMP.alignment = TextAlignmentOptions.Center;
                diffTMP.color     = new Color(1f, 1f, 1f, 0.88f);
                diffTMP.raycastTarget = false; // 노드 위에 겹쳐 그려지므로 클릭 판정을 가로채면 안 됨

                var nv = nodeGO.AddComponent<MapNodeView>();
                nv.Init(node, img, diffTMP);
                nv.OnClicked    += OnNodeClicked;
                nv.OnHoverEnter += ShowNodeTooltip;
                nv.OnHoverExit  += _ => HideClueTooltip();
                _nodeViews[node.guid] = nv;
            }
        }

        // ─── 상태 갱신 ───────────────────────────────────────────

        private void Refresh()
        {
            if (MapGraph.Instance == null) return;
            _originNode ??= MapGraph.Instance.StartNode; // 출발지 드롭다운 기본값 — 최초 1회만
            var progress = RouteModule.Instance.Progress; // 방문/단서/클리어 상태
            var gears    = RouteModule.Instance.EquippedGearArray;

            // 통과 불가 구간을 포함한 경로(IsBlocked)는 선택 불가 — 빨강으로 표시만 하고,
            // 실제 선택 가능한 경로는 AlternativePath.
            bool blocked = _currentPath != null && _currentPath.IsBlocked;
            var selectablePath = blocked ? _currentPath.AlternativePath : _currentPath;
            var blockedPath    = blocked ? _currentPath : null;

            // 밝혀진(단서 보유) 노드 → 그 노드에 닿은 간선 → 간선 반대편 노드 순으로 "표시 대상"을 계산한다.
            // 밝혀지지 않았고 밝혀진 노드와 맞닿은 간선도 없는 노드/간선은 화면에서 완전히 숨긴다.
            var revealedNodeGuids = new HashSet<string>();
            foreach (var kv in _nodeViews)
                if (kv.Value.Data.isStartNode || progress.HasNodeClue(kv.Value.Data))
                    revealedNodeGuids.Add(kv.Key);

            var shownEdgeGuids = new HashSet<string>();
            var shownNodeGuids = new HashSet<string>(revealedNodeGuids);
            foreach (var ev in _edgeViews)
            {
                var d = ev.Data;
                if (!revealedNodeGuids.Contains(d.fromGuid) && !revealedNodeGuids.Contains(d.toGuid)) continue;
                shownEdgeGuids.Add(d.guid);
                shownNodeGuids.Add(d.fromGuid);
                shownNodeGuids.Add(d.toGuid);
            }

            // 툴바 출발/도착 드롭다운에 올릴 후보 — 화면에 보이는(known) 노드만.
            _knownNodesForDropdown.Clear();
            foreach (var kv in _nodeViews)
                if (shownNodeGuids.Contains(kv.Key)) _knownNodesForDropdown.Add(kv.Value.Data);

            foreach (var kv in _nodeViews)
            {
                var d        = kv.Value.Data;
                bool vis     = progress.IsNodeVisited(d);
                bool clue    = progress.HasNodeClue(d);
                bool start   = d.isStartNode;
                bool sel     = _selectedDest?.Data.guid == d.guid;
                bool onPath  = IsNodeOnPath(selectablePath, d.guid);
                bool onBlockedPath = IsNodeOnPath(blockedPath, d.guid);
                bool known   = shownNodeGuids.Contains(d.guid);
                // 2026-07-14 — 난이도/통과 불가 판정이 연결에서 맵으로 이동.
                float df       = DifficultyCalculator.Calculate(d, gears);
                bool cleared   = progress.IsNodeCleared(d);
                bool passable  = d.IsPassableWith(gears);
                kv.Value.SetShown(known);
                kv.Value.SetState(visited: vis, hasClue: clue, isStart: start, isSelected: sel, isOnPath: onPath, isOnBlockedPath: onBlockedPath, known: known,
                    isPassable: passable, requiredGears: d.requiredGears, difficulty: df, cleared: cleared);
            }

            foreach (var ev in _edgeViews)
            {
                var d        = ev.Data;
                bool hasClue = progress.HasConnectionClue(d);
                bool onPath  = IsEdgeOnPath(selectablePath, d.guid);
                bool onBlockedPath = IsEdgeOnPath(blockedPath, d.guid);
                ev.SetShown(shownEdgeGuids.Contains(d.guid));
                ev.SetState(hasClue: hasClue, isOnPath: onPath, isOnBlockedPath: onBlockedPath);
            }

            RefreshPathLabel();
            RefreshPathTypeButtons();
            RefreshNoClueOptionButtons();
            RefreshSelectRouteButton();
            RefreshGearPanel();
            RefreshClueMarkers();
            RefreshClueList();
            RefreshDropdowns();
        }

        // "경로 선택" 버튼 색상 — 지금 미리보기 중인 경로(_currentPath, 막혀 있으면 AlternativePath)가
        // 이미 RouteModule에 커밋된 경로와 같으면 초록(BtnActive), 아니면 회색(BtnInactive).
        private void RefreshSelectRouteButton()
        {
            if (_btnSelectRouteImg == null) return;
            _btnSelectRouteImg.color = IsCurrentPathSelected() ? BtnActive : BtnInactive;
        }

        private bool IsCurrentPathSelected()
        {
            var selected = RouteModule.Instance?.SelectedRoute;
            if (selected == null || _currentPath == null) return false;

            var toCompare = _currentPath.IsBlocked ? _currentPath.AlternativePath : _currentPath;
            if (toCompare == null || selected.Nodes.Count != toCompare.Nodes.Count) return false;

            for (int i = 0; i < selected.Nodes.Count; i++)
                if (selected.Nodes[i].guid != toCompare.Nodes[i].guid) return false;
            return true;
        }

        private static bool IsNodeOnPath(PathResult path, string guid)
        {
            if (path == null || !path.IsValid) return false;
            foreach (var n in path.Nodes)
                if (n.guid == guid) return true;
            return false;
        }

        private static bool IsEdgeOnPath(PathResult path, string guid)
        {
            if (path == null || !path.IsValid) return false;
            foreach (var c in path.Connections)
                if (c.guid == guid) return true;
            return false;
        }

        // ─── 단서 마커 / 툴팁 ─────────────────────────────────────
        // 획득한 단서가 가리키는 맵 옆에 마커를 표시하고, 호버 시 그 맵까지의
        // 추천 경로 3종(최단/균형/최소난이도)을 툴팁으로 보여준다.

        private static readonly Color ColClueMarker = new(1f, 0.85f, 0.2f);

        private void RefreshClueMarkers()
        {
            if (MapGraph.Instance == null) return;

            foreach (var clueId in RouteModule.Instance.Progress.AcquiredClueIds)
            {
                if (_clueMarkerViews.ContainsKey(clueId)) continue;
                var clue = MapGraph.Instance.GetClue(clueId);
                if (clue == null || string.IsNullOrEmpty(clue.targetMapGuid)) continue;
                if (!_nodePositions.TryGetValue(clue.targetMapGuid, out var pos)) continue;

                CreateClueMarker(clue, pos);
            }
        }

        private void CreateClueMarker(ClueData clue, Vector2 nodePos)
        {
            int idx = 0;
            foreach (var kv in _clueMarkerViews)
                if (kv.Value.Clue.targetMapGuid == clue.targetMapGuid) idx++;

            var go = new GameObject($"ClueMarker_{clue.id}");
            go.transform.SetParent(_graphContainer, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * (_nodeSize * 0.6f);
            rt.anchoredPosition = nodePos + new Vector2(_nodeSize * 0.7f, _nodeSize * 0.7f + idx * (_nodeSize * 0.7f));

            var img = go.AddComponent<Image>();
            img.color = ColClueMarker;

            var marker = go.AddComponent<ClueMarkerView>();
            marker.Init(clue);
            marker.OnHoverEnter += ShowClueTooltip;
            marker.OnHoverExit  += _ => HideClueTooltip();

            _clueMarkerViews[clue.id] = marker;
        }

        private void EnsureTooltip()
        {
            if (_tooltipRT != null) return;

            var go = new GameObject("ClueTooltip");
            go.transform.SetParent(_graphContainer, false);
            _tooltipRT = go.AddComponent<RectTransform>();
            _tooltipRT.pivot     = new Vector2(0f, 0f);
            _tooltipRT.sizeDelta = new Vector2(80f, 50f);
            AddImg(_tooltipRT, new Color(0.05f, 0.05f, 0.08f, 0.95f));

            var txtRT = NewRect(_tooltipRT, "Text");
            StretchFull(txtRT);
            _tooltipTMP = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) _tooltipTMP.font = _font;
            _tooltipTMP.fontSize  = 6f;
            _tooltipTMP.color     = Color.white;
            _tooltipTMP.alignment = TextAlignmentOptions.TopLeft;
            _tooltipTMP.enableWordWrapping = true;

            go.SetActive(false);
        }

        private void ShowClueTooltip(ClueMarkerView marker)
        {
            EnsureTooltip();
            var clue = marker.Clue;

            var sb = new StringBuilder();
            sb.Append($"<b>{clue.name}</b>\n{clue.description}\n");

            var startNode  = MapGraph.Instance.StartNode;
            var targetNode = MapGraph.Instance.GetNode(clue.targetMapGuid);
            if (startNode != null && targetNode != null && startNode.guid != targetNode.guid)
            {
                AppendRouteInfo(sb, "최단",      PathType.Shortest,      targetNode);
                AppendRouteInfo(sb, "균형",      PathType.Balanced,      targetNode);
                AppendRouteInfo(sb, "최소난이도", PathType.MinDifficulty, targetNode);
            }

            _tooltipTMP.text = sb.ToString();

            var markerRT = (RectTransform)marker.transform;
            _tooltipRT.anchoredPosition = markerRT.anchoredPosition + new Vector2(_nodeSize, 0f);
            _tooltipRT.localScale = Vector3.one / Mathf.Max(_graphPanZoom != null ? _graphPanZoom.Scale : 1f, 0.0001f);
            _tooltipRT.gameObject.SetActive(true);
            _tooltipRT.SetAsLastSibling();
        }

        private void HideClueTooltip()
        {
            if (_tooltipRT != null) _tooltipRT.gameObject.SetActive(false);
        }

        // 화면에 보이는(known) 노드에 호버 시 맵 정보 + 이 맵에서 획득한 단서를 보여준다.
        // 툴팁은 노드 위치가 아니라 마우스 커서 바로 오른쪽 위에 뜬다(ShowClueTooltip은 기존대로 마커 기준 유지).
        private void ShowNodeTooltip(MapNodeView view)
        {
            EnsureTooltip();
            var node     = view.Data;
            var progress = RouteModule.Instance.Progress;

            var sb = new StringBuilder();
            sb.Append($"<b>{node.nodeName}</b>\n");
            if (!string.IsNullOrEmpty(node.description)) sb.Append($"{node.description}\n");

            if (node.clueIds != null)
            {
                int lockedCount = 0;
                foreach (var clueId in node.clueIds)
                {
                    if (progress.IsClueAcquired(clueId))
                    {
                        var clue = MapGraph.Instance.GetClue(clueId);
                        if (clue != null) sb.Append($"[단서] {clue.name}\n");
                        continue;
                    }

                    // 방문은 했지만 아직 획득 못 한 단서 — requiredEventKey가 있는 것만 "선행 이벤트
                    // 필요"로 표시한다(내용/이름은 스포일러라 안 보여줌, 존재와 잠김 상태만 알림).
                    var lockedClue = MapGraph.Instance.GetClue(clueId);
                    if (lockedClue != null && !string.IsNullOrEmpty(lockedClue.requiredEventKey))
                        lockedCount++;
                }
                if (lockedCount > 0)
                    sb.Append($"<color=#888888>[미공개 단서 {lockedCount}개 - 특정 이벤트 필요]</color>\n");
            }

            _tooltipTMP.text = sb.ToString();

            PositionTooltipAtMouse();
            _tooltipRT.gameObject.SetActive(true);
            _tooltipRT.SetAsLastSibling();
        }

        // 현재 마우스 스크린 좌표를 그래프 컨테이너 로컬 좌표로 변환해 툴팁을 그 자리에 놓는다.
        // _tooltipRT의 pivot이 (0,0)이라 anchoredPosition이 곧 툴팁의 좌하단 모서리 — 그 지점을
        // 마우스보다 살짝 오른쪽·위로 두면 툴팁 박스 전체가 커서의 오른쪽 위로 펼쳐진다.
        // 오프셋은 화면 픽셀 기준으로 고정되도록 현재 줌 배율로 나눠서 그래프 컨테이너 로컬 단위로 변환한다.
        private void PositionTooltipAtMouse()
        {
            if (_graphContainer == null) return;

            var canvas = GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_graphContainer, Input.mousePosition, cam, out var local))
                return;

            float scale = Mathf.Max(_graphPanZoom != null ? _graphPanZoom.Scale : 1f, 0.0001f);
            // 기존 (+10,+10)에서 좌측 100px·아래 50px만큼 이동 요청 반영.
            const float offsetX = 10f - 120f; // -90f
            const float offsetY = 10f - 120f;  // -40f
            _tooltipRT.anchoredPosition = local + new Vector2(offsetX, offsetY) / scale;
            _tooltipRT.localScale = Vector3.one / scale;
        }

        // 단서 툴팁에 표시할 추천 경로 한 줄 요약. 탐색 자체는 모듈(현재 장비·진행 상태 기준)에 위임.
        private static void AppendRouteInfo(StringBuilder sb, string label, PathType type, MapNodeData dest)
        {
            var result = RouteModule.Instance.FindPathFromStart(dest, type);
            if (!result.IsValid) { sb.Append($"{label}: 경로 없음\n"); return; }

            if (result.IsBlocked)
            {
                var alt = result.AlternativePath;
                if (alt != null && alt.IsValid)
                    sb.Append($"{label}: [!]차단 → 차선 {alt.Nodes.Count - 1}구간/{alt.TotalDifficulty:F0}\n");
                else
                    sb.Append($"{label}: [!]차단, 차선 없음\n");
            }
            else
            {
                sb.Append($"{label}: {result.Nodes.Count - 1}구간 / 난이도 {result.TotalDifficulty:F0}\n");
            }
        }

        // 사이드패널의 단서 목록을 갱신한다 — 2026-07-14: 전체 획득 단서가 아니라 **가장 최근에 선택된
        // 맵 노드(_selectedDest, 그래프 클릭 또는 도착 드롭다운으로 갱신됨)와 연관된 단서만** 보여주도록
        // 범위를 좁혔다. "연관"의 기준은 ShowNodeTooltip이 이미 쓰던 것과 동일 — 그 맵의 clueIds 중
        // 획득된 것만. 항목이 여러 개면 이름만 접어두고 클릭해야 본문(세부 항목)이 펼쳐진다(아코디언).
        private void RefreshClueList()
        {
            if (_clueListContent == null || MapGraph.Instance == null) return;

            // 먼저 비활성화한 뒤 Destroy — TMP 오브젝트를 활성 상태로 그냥 Destroy하면, 같은 프레임에
            // ScrollRect.LateUpdate가 강제하는 CanvasUpdateRegistry 리빌드가 이미 파괴 중인 TMP의
            // 서브메시(폴백 폰트) 머티리얼에 접근하려다 MissingReferenceException을 던지는 경우가 있다.
            // SetActive(false)는 OnDisable을 즉시 호출해 리빌드 대상에서 그 프레임에 바로 빠지게 한다.
            for (int i = _clueListContent.childCount - 1; i >= 0; i--)
            {
                var child = _clueListContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            var selected = _selectedDest?.Data;
            if (selected == null)
            {
                MakeTMP(_clueListContent, "맵 노드를 선택하세요", 8f, FontStyles.Normal, 12f).color = Gray;
                return;
            }

            var progress = RouteModule.Instance.Progress;
            var related = new List<ClueData>();
            if (selected.clueIds != null)
            {
                foreach (var clueId in selected.clueIds)
                {
                    if (!progress.IsClueAcquired(clueId)) continue;
                    var clue = MapGraph.Instance.GetClue(clueId);
                    if (clue != null) related.Add(clue);
                }
            }

            if (related.Count == 0)
            {
                MakeTMP(_clueListContent, "이 맵에서 획득한 단서 없음", 8f, FontStyles.Normal, 12f).color = Gray;
                return;
            }

            // 1개면 클릭 없이 바로 본문까지 보여주고, 여러 개면 이름만 접어둔다(눌러서 펼침).
            // 펼침 여부는 _expandedClueIds에 저장해둬서, 다른 조작(장비 토글 등)으로 Refresh()가 다시
            // 불려 목록이 통째로 재생성되더라도 사용자가 펼쳐 둔 상태가 갑자기 접히지 않게 한다.
            bool singleItem = related.Count == 1;
            foreach (var clue in related)
                MakeClueListItem(_clueListContent, clue, startExpanded: singleItem || _expandedClueIds.Contains(clue.id));
        }

        // startExpanded=true면 처음부터 이름+본문을 모두 보여주고, false면 이름만 보이다가
        // 클릭할 때마다 펼침/접힘을 토글한다. 클릭 시 기존처럼 그래프도 해당 맵으로 포커스 이동.
        private void MakeClueListItem(RectTransform parent, ClueData clue, bool startExpanded)
        {
            var rt = NewRect(parent, "Clue_" + clue.id);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            var bgImg = AddImg(rt, new Color(0.12f, 0.15f, 0.22f));
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            btn.transition    = Selectable.Transition.None;

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = 7f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.margin = new Vector4(2f, 1f, 2f, 1f);

            bool expanded = startExpanded;
            void ApplyState()
            {
                tmp.text = expanded ? $"<b>{clue.name}</b>\n{clue.description}" : $"▸ <b>{clue.name}</b>";
                le.preferredHeight = expanded ? 28f : 14f;
            }
            ApplyState();

            btn.onClick.AddListener(() =>
            {
                expanded = !expanded;
                if (expanded) _expandedClueIds.Add(clue.id); else _expandedClueIds.Remove(clue.id);
                ApplyState();
                FocusOnClue(clue);
            });
        }

        private void FocusOnClue(ClueData clue)
        {
            if (_graphPanZoom == null || string.IsNullOrEmpty(clue.targetMapGuid)) return;
            if (_nodePositions.TryGetValue(clue.targetMapGuid, out var pos))
                _graphPanZoom.FocusOn(pos);
        }

        // ─── 인터랙션 ─────────────────────────────────────────────

        private void OnNodeClicked(MapNodeView view)
        {
            _selectedDest = (_selectedDest?.Data.guid == view.Data.guid) ? null : view;
            RecalcPath();
            Refresh();
        }

        // 툴바 "원점" 버튼 — 지도 팬/줌을 초기 상태로 되돌리는 것에 더해,
        // 출발/도착 드롭다운도 둘 다 집(StartNode)으로 초기화한다.
        private void ResetToOrigin()
        {
            _graphPanZoom?.ResetView();

            if (MapGraph.Instance == null) return;
            var home = MapGraph.Instance.StartNode;
            if (home == null) return;

            _originNode   = home;
            _selectedDest = _nodeViews.TryGetValue(home.guid, out var view) ? view : null;
            RecalcPath();
            Refresh();
        }

        // 선택된 목적지 기준으로 추천 경로 재계산.
        // 출발지는 툴바 드롭다운으로 바뀔 수 있어 기본(StartNode)이 아닐 수 있다 — RouteModule.FindPath에 직접 전달.
        // 장비·진행 상태 반영은 모듈이 담당하므로 뷰는 출발지·목적지·경로 방식만 전달한다.
        private void RecalcPath()
        {
            _currentPath = null;
            if (_selectedDest == null || MapGraph.Instance == null) return;

            var startNode = _originNode ?? MapGraph.Instance.StartNode;
            if (startNode == null || startNode.guid == _selectedDest.Data.guid) return;

            _currentPath = RouteModule.Instance.FindPath(startNode, _selectedDest.Data, _pathType);
        }

        private void SetPathType(PathType pt)
        {
            _pathType = pt;
            RecalcPath();
            Refresh();
        }

        // "단서 없어도 이동" / "단서 우선 경로" 옵션 전환. 실제 판단·잠금(이동 중 변경 불가)은 모듈이 담당.
        private void SetAvoidNoClueNodes(bool avoid)
        {
            if (!RouteModule.Instance.SetAvoidNoClueNodes(avoid)) return;
            RecalcPath();
            Refresh();
        }

        // "경로 선택" 버튼 (2026-07-14 신설) — 지도는 지금까지 _currentPath를 미리보기만 했을 뿐
        // RouteModule.SelectRoute를 호출한 적이 없어서, 노트의 "경로 연동 자동 편입"(OnRouteSelected 구독)이
        // 지도에서 목적지를 고르는 것만으로는 절대 발동하지 않았다 — 이 버튼이 미리보기를 실제로 커밋한다.
        private void SelectCurrentPath()
        {
            Debug.Log($"[MapViewer] SelectCurrentPath() 호출 — _currentPath={(_currentPath == null ? "null" : $"valid={_currentPath.IsValid},blocked={_currentPath.IsBlocked},nodes={_currentPath.Nodes?.Count}")}");

            if (_currentPath == null || !_currentPath.IsValid)
            {
                Debug.LogWarning("[MapViewer] 선택할 경로가 없습니다.");
                return;
            }

            var toSelect = _currentPath;
            if (toSelect.IsBlocked)
            {
                if (toSelect.AlternativePath == null || !toSelect.AlternativePath.IsValid)
                {
                    Debug.LogWarning("[MapViewer] 통과 불가 구간이 있고 차선 경로도 없어 선택할 수 없습니다.");
                    return;
                }
                toSelect = toSelect.AlternativePath;
            }

            bool ok = RouteModule.Instance.SelectRoute(toSelect);
            Debug.Log($"[MapViewer] RouteModule.SelectRoute 결과 — {ok}, NoteModule.Instance={(NoteModule.Instance != null)}");
        }

        // 장비 버튼 클릭 — 실제 착탈은 모듈에 위임하고(이동 중이면 거부됨),
        // 변경됐을 때만 경로 재계산 + 화면 갱신. 버튼 색상 등은 Refresh→RefreshGearPanel이 처리.
        private void ToggleGear(EmotionColor ec)
        {
            if (!RouteModule.Instance.ToggleGear(ec)) return;
            RecalcPath();
            Refresh();
        }

        // 장비 패널을 모듈의 장비 상태와 동기화한다.
        // 상태의 원본이 모듈에 있으므로, 어떤 경로로 장비가 바뀌어도 화면이 항상 일치한다.
        private void RefreshGearPanel()
        {
            var module = RouteModule.Instance;

            foreach (var kv in _gearBtnImages)
            {
                var baseColor = EmotionColorConfig.GetColor(kv.Key);
                kv.Value.color = module.IsGearEquipped(kv.Key) ? baseColor : baseColor * 0.45f;
            }

            if (_gearListTMP == null) return;

            if (module.EquippedGears.Count == 0)
            {
                _gearListTMP.text = "없음";
                return;
            }

            var sb = new StringBuilder();
            foreach (var g in module.EquippedGears)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(EmotionColorConfig.GetName(g));
            }
            _gearListTMP.text = sb.ToString();
        }

        private void RefreshPathLabel()
        {
            if (_pathInfoTMP == null) return;

            if (_selectedDest == null)
            {
                _pathInfoTMP.text = "노드 클릭 →\n목적지 선택";
                return;
            }
            if (_currentPath == null || !_currentPath.IsValid)
            {
                _pathInfoTMP.text = $"[{_selectedDest.Data.nodeName}]\n경로 없음";
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"목적지: {_selectedDest.Data.nodeName}\n");

            if (_currentPath.IsBlocked)
            {
                sb.Append("[!] 장비 부족으로\n통과 불가 구간 포함\n");
                var alt = _currentPath.AlternativePath;
                if (alt != null && alt.IsValid)
                {
                    sb.Append($"차선: 구간 {alt.Nodes.Count - 1}  난이도 {alt.TotalDifficulty:F0}\n");
                    if (alt.ContainsNoClueNode)
                        sb.Append("[!] 차선 경로에 단서 없는 맵 포함\n");
                    if (alt.NoClueAvoidanceFailed)
                        sb.Append("[!] 단서만 있는 경로 없음\n");
                }
                else
                {
                    sb.Append("대체 경로 없음 — 도달 불가");
                }
            }
            else
            {
                sb.Append($"구간: {_currentPath.Nodes.Count - 1}  난이도: {_currentPath.TotalDifficulty:F0}\n");
                if (_currentPath.ContainsNoClueNode)
                    sb.Append("[!] 단서 없는 맵 포함\n");
                if (_currentPath.NoClueAvoidanceFailed)
                    sb.Append("[!] 단서만 있는 경로 없음\n");
            }

            _pathInfoTMP.text = sb.ToString();
        }

        private void RefreshPathTypeButtons()
        {
            if (_btnShortestImg != null) _btnShortestImg.color = _pathType == PathType.Shortest      ? BtnActive : BtnInactive;
            if (_btnBalancedImg != null) _btnBalancedImg.color = _pathType == PathType.Balanced      ? BtnActive : BtnInactive;
            if (_btnMinDiffImg  != null) _btnMinDiffImg.color  = _pathType == PathType.MinDifficulty ? BtnActive : BtnInactive;
        }

        private void RefreshNoClueOptionButtons()
        {
            bool avoid = RouteModule.Instance.AvoidNoClueNodes;
            if (_btnAllowNoClueImg != null) _btnAllowNoClueImg.color = !avoid ? BtnActive : BtnInactive;
            if (_btnAvoidNoClueImg != null) _btnAvoidNoClueImg.color = avoid  ? BtnActive : BtnInactive;
        }

        // ─── 사이드패널 열기/닫기 ───────────────────────────────────
        // 우측 사이드패널 자체를 접어서 그래프 영역을 넓게 볼 수 있게 한다.
        // 접혀도 재오픈용 작은 탭(SidePanelToggleTab)은 항상 보이도록 스크롤 뷰와 별개로 둔다.

        private void SetSidePanelOpen(bool open)
        {
            _sidePanelOpen = open;
            UpdateSidePanelLayout();
            if (_sidePanelToggleArrowTMP != null) _sidePanelToggleArrowTMP.text = open ? "◀" : "▶";
            if (_toolbarPanelToggleImg   != null) _toolbarPanelToggleImg.color  = open ? BtnActive : BtnInactive;
        }

        private void UpdateSidePanelLayout()
        {
            float w = _sidePanelOpen ? _sidePanelWidth : _sidePanelCollapsedWidth;
            if (_sidePanelRT != null) _sidePanelRT.offsetMin = new Vector2(-w, 0f);
            if (_graphAreaRT != null) _graphAreaRT.offsetMax = new Vector2(-w - _graphAreaMarginRight, -_graphAreaMarginTop - _toolbarHeight);
            _sidePanelScrollGO?.gameObject.SetActive(_sidePanelOpen);
        }

        // ─── 출발/도착 드롭다운 (SimpleDropdown) ──────────────────────

        private void RefreshDropdowns()
        {
            if (_originDropdown == null || _destDropdown == null) return;
            FillSimpleDropdown(_originDropdown, _originNode);
            FillSimpleDropdown(_destDropdown, _selectedDest?.Data);
        }

        // 옵션 버튼들을 다시 만들고, 캡션 텍스트를 현재 선택값으로 갱신한다.
        // 여기서는 OnSelected를 호출하지 않으므로(사용자가 실제로 옵션을 클릭했을 때만 호출됨)
        // TMP_Dropdown 때 필요했던 "이벤트 억제 플래그" 자체가 필요 없다.
        private void FillSimpleDropdown(SimpleDropdown dd, MapNodeData current)
        {
            for (int i = dd.OptionsContent.childCount - 1; i >= 0; i--)
            {
                var child = dd.OptionsContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            int selectedIdx = 0;
            for (int i = 0; i < _knownNodesForDropdown.Count; i++)
            {
                var node = _knownNodesForDropdown[i];
                int captured = i;
                var optBtn = MakeBtn(dd.OptionsContent, node.nodeName, () => SelectSimpleDropdownOption(dd, captured), fontSize: _dropdownOptionFontSize);
                var le = optBtn.GetComponent<LayoutElement>();
                le.preferredHeight = _dropdownOptionHeight;
                if (current != null && node.guid == current.guid) selectedIdx = i;
            }

            dd.SelectedIndex = _knownNodesForDropdown.Count > 0 ? selectedIdx : -1;
            dd.Caption.text  = _knownNodesForDropdown.Count > 0 ? _knownNodesForDropdown[selectedIdx].nodeName : "";

            // [원인 파악, 2026-07-14] Caption은 ddRT(드롭다운 본체)에 앵커-스트레치된 자식이라, ddRT
            // 자체의 크기가 아직 유효하지 않으면(0,0) Caption도 크기 0인 채로 텍스트만 대입되어 화면에
            // 안 보이게 된다. ddRT의 크기는 툴바의 HorizontalLayoutGroup이 계산하는데, 이 리빌드는 다음
            // 레이아웃 패스까지 지연되는 게 기본 동작 — BuildUI()/FinalizePanel()에서 빌드 직후 1회
            // 강제 리빌드를 추가했지만, 옵션 버튼을 매번 파괴·재생성하는 이 함수도 같은 툴바 하위에서
            // 호출되므로 안전하게 한 번 더 강제 리빌드해 Caption이 항상 유효한 크기를 갖도록 보장한다.
            if (_toolbarRT != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_toolbarRT);
        }

        // 드롭다운 본체 클릭 — 열림/닫힘 토글. 다른 쪽 드롭다운이 열려있으면 같이 닫는다(둘 다 열리면 헷갈림).
        private void ToggleSimpleDropdown(SimpleDropdown dd)
        {
            bool opening = !dd.OptionsList.gameObject.activeSelf;
            _originDropdown.OptionsList.gameObject.SetActive(opening && dd == _originDropdown);
            _destDropdown.OptionsList.gameObject.SetActive(opening && dd == _destDropdown);
        }

        // 옵션 클릭 — 선택 확정 + 목록 닫기 + 실제 동작(OnSelected)은 호출부(출발/도착)에 위임.
        private void SelectSimpleDropdownOption(SimpleDropdown dd, int index)
        {
            dd.OptionsList.gameObject.SetActive(false);
            if (index < 0 || index >= _knownNodesForDropdown.Count) return;
            dd.SelectedIndex = index;
            dd.Caption.text  = _knownNodesForDropdown[index].nodeName;
            dd.OnSelected?.Invoke(index);
        }

        // 출발지 드롭다운 변경 — 그래프 클릭으로는 바꿀 수 없는 유일한 수단.
        private void OnOriginDropdownChanged(int index)
        {
            if (index < 0 || index >= _knownNodesForDropdown.Count) return;
            _originNode = _knownNodesForDropdown[index];
            RecalcPath();
            Refresh();
        }

        // 도착지 드롭다운 변경 — 그래프 노드 클릭(OnNodeClicked)과 동일한 목적지 선택 수단이며 서로 값이 동기화된다.
        private void OnDestDropdownChanged(int index)
        {
            if (index < 0 || index >= _knownNodesForDropdown.Count) return;
            var node = _knownNodesForDropdown[index];
            if (_nodeViews.TryGetValue(node.guid, out var view))
            {
                _selectedDest = view;
                RecalcPath();
                Refresh();
            }
        }

        // ─── UI 구축 ─────────────────────────────────────────────

        private void BuildUI()
        {
            // 씬 계층에 MapPanel이 이미 자식으로 배치돼 있으면 재사용
            // BtnGoToCodex가 없으면 구버전(2026-07-20 "도감" 버튼 도입 이전) — 파괴 후 재생성
            // (마커를 예전 요소로 두면, 그 요소는 이미 있던 구버전 저장 프리팹도 "최신"으로 오판정돼
            //  새로 추가된 버튼이 코드에서 생성될 기회 자체가 없어짐 — 항상 가장 최근에 추가된
            //  구조 요소로 갱신해야 한다.)
            var existing = transform.Find("MapPanel");
            if (existing != null)
            {
                bool existingCurrent = FindDeepTransform(existing, "BtnGoToCodex") != null;

                if (existingCurrent)
                {
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                // 먼저 비활성화한 뒤 Destroy — 구버전 패널 안의 TMP 텍스트를 활성 상태로 그냥 Destroy하면
                // MissingReferenceException이 발생할 수 있다 (RefreshClueList 등과 동일 패턴).
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            // 프리팹이 지정되어 있으면 인스턴스화 후 참조를 바인딩하고 콜백만 재연결
            // BtnGoToCodex 없으면 구버전 취급(위 existing 분기와 동일한 이유). GraphArea는 프리팹에서
            // 지워둔 경우(겹침 방지) 자동 생성으로 보강한다.
            if (_panelPrefab != null)
            {
                bool prefabCurrent = FindDeepTransform(_panelPrefab.transform, "BtnGoToCodex") != null;

                if (prefabCurrent)
                {
                    _panelGO = Instantiate(_panelPrefab, transform, false);
                    _panelGO.name = "MapPanel";
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
            }

            // ── 프리팹 없음 → 런타임 자동 생성 ──
            _panelGO = new GameObject("MapPanel");
            _panelGO.transform.SetParent(transform, false);
            var root = _panelGO.AddComponent<RectTransform>();
            StretchFull(root);
            PanelBackground.Apply(root, _rootBgColor, _rootBgSprite);

            float panelW = _sidePanelWidth;

            // GraphArea를 SidePanel보다 먼저 만든다 — SidePanelToggleTab(재오픈용 탭)이 SidePanel의
            // 왼쪽 경계 바깥(GraphArea 쪽)으로 튀어나오게 배치되는데, 나중에 생성된 형제가 화면에서 위에
            // 그려지는 일반 UI 계층 규칙상 GraphArea가 SidePanel보다 늦게 만들어지면 그 튀어나온 탭 부분을
            // 덮어버려 안 보이게 된다(2026-07-14 리포트). GraphArea를 먼저 만들어 SidePanel(과 탭)이 항상
            // 그 위에 그려지도록 순서를 바꿨다.
            BuildGraphArea(root);

            var side = NewRect(root, "SidePanel");
            side.anchorMin = new Vector2(1f, 0f);
            side.anchorMax = Vector2.one;
            side.offsetMin = new Vector2(-panelW, 0f);
            side.offsetMax = new Vector2(0f, -_toolbarHeight);
            PanelBackground.Apply(side, _sidePanelBgColor, _sidePanelBgSprite);
            _sidePanelRT = side;
            BuildSidePanel(side, panelW);
            BuildSidePanelToggleTab(side);

            // Toolbar를 마지막 자식으로 만들어야 한다 — GraphArea/SidePanel 자체는 툴바 아래 영역에만
            // 앵커돼 있어 겹치지 않지만, 드롭다운 옵션 목록은 툴바 바깥(아래쪽, GraphArea 영역)까지
            // 펼쳐지므로 그보다 늦게 그려지는 형제(=나중에 생성된 GraphArea)에게 가려진다.
            BuildToolbar(root);

            // 툴바는 HorizontalLayoutGroup(childControlWidth/Height=true)으로 자식(드롭다운 등) 크기를
            // 계산하는데, 이 리빌드는 기본적으로 다음 레이아웃 패스까지 지연된다(Canvas.willRenderCanvases).
            // Awake 안에서 만들어진 직후 곧바로 Refresh()가 호출되면(Open() 참고) 드롭다운의 Caption(ddRT에
            // 앵커-스트레치된 자식)이 아직 크기 0인 부모를 참조한 상태로 텍스트가 대입돼버려 화면에 반영되지
            // 않는 문제가 있었다 — 여기서 즉시 강제로 레이아웃을 확정해 이 시점부터 rect가 유효하게 한다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_toolbarRT);
        }

        // 기존/프리팹 패널을 재사용할 때의 공통 마무리: 전체 스트레치, 참조 바인딩, 버튼 콜백 연결.
        // GraphArea가 빠져 있으면(겹침 방지로 지운 경우) 보강 생성한다.
        private void FinalizePanel(RectTransform rt)
        {
            if (rt != null) StretchFull(rt);
            BindRefsFromHierarchy();
            WireButtonCallbacks();
            if (_graphContainer == null) BuildGraphArea(rt);

            // [2026-07-14] Toolbar 자체(배경·버튼·라벨 위치 등 정적 디자인)는 프리팹 재사용을 허용해
            // 비주얼 커스터마이징이 가능하게 유지한다. 다만 드롭다운(OriginDropdown/DestDropdown)은 저장
            // 시점 상태가 굳어 캡션이 영구히 깨질 수 있었던 실제 사례가 있어(원인: 프리팹에 저장된 옛
            // 인스턴스가 "Toolbar 있음"만으로 최신 판정돼 재사용되며 MakeSimpleDropdown이 아예 호출 안 됨),
            // 이 둘만큼은 재사용 여부와 무관하게 항상 파괴 후 새로 만든다 — 같은 위치(형제 인덱스)에
            // 다시 끼워 넣으므로 Toolbar 안에서의 배치(라벨 옆)는 프리팹에 있던 그대로 유지된다.
            if (_toolbarRT != null)
            {
                _originDropdown = RebuildSimpleDropdownInPlace(_toolbarRT, "OriginDropdown", 48f, OnOriginDropdownChanged);
                _destDropdown   = RebuildSimpleDropdownInPlace(_toolbarRT, "DestDropdown", 48f, OnDestDropdownChanged);
            }

            // 형제 순서를 GraphArea < SidePanel < Toolbar로 강제한다 — 프리팹/씬에 저장된 순서가 무엇이든
            // SidePanelToggleTab(SidePanel 왼쪽 바깥으로 튀어나오는 탭)과 드롭다운 옵션 목록(Toolbar 아래로
            // 펼쳐짐)이 GraphArea에 가리지 않게 하려면 이 순서가 필요하다(2026-07-14).
            _sidePanelRT?.SetAsLastSibling();
            _toolbarRT?.SetAsLastSibling(); // 드롭다운 목록이 GraphArea/SidePanel에 가리지 않도록 툴바를 항상 맨 위로
            if (_toolbarRT != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_toolbarRT); // 아래 BuildUI() 주석 참고
            UpdateSidePanelLayout();
        }

        // GraphArea(스크롤·줌 가능한 그래프 영역)를 root 아래에 생성한다.
        // 프리팹에 SidePanel만 있고 GraphArea가 없는 경우(겹침 방지를 위해 지워둔 경우) 보강용으로 호출된다.
        private void BuildGraphArea(RectTransform root)
        {
            float panelW = _sidePanelOpen ? _sidePanelWidth : _sidePanelCollapsedWidth;

            var graphArea = NewRect(root, "GraphArea");
            graphArea.anchorMin = Vector2.zero;
            graphArea.anchorMax = new Vector2(1f, 1f);
            graphArea.offsetMin = new Vector2(_graphAreaMarginLeft, _graphAreaMarginBottom);
            graphArea.offsetMax = new Vector2(-panelW - _graphAreaMarginRight, -_graphAreaMarginTop - _toolbarHeight);
            _graphAreaRT = graphArea;
            BuildScrollGraph(graphArea);
        }

        private void BuildScrollGraph(RectTransform parent)
        {
            PanelBackground.Apply(parent, _graphAreaBgColor, _graphAreaBgSprite);
            _graphPanZoom = parent.gameObject.AddComponent<GraphPanZoom>();

            var vp = NewRect(parent, "GraphViewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            _graphViewport = vp;

            _graphContainer = NewRect(vp, "GraphContainer");
            _graphContainer.anchorMin = _graphContainer.anchorMax = Vector2.zero;
            _graphContainer.pivot     = Vector2.zero;

            _labelContainer = NewRect(_graphContainer, "LabelContainer");
            StretchFull(_labelContainer);
        }

        private void ShowGraphWarning(string message)
        {
            if (_graphViewport == null) return;
            var warnGO = new GameObject("GraphWarning");
            warnGO.transform.SetParent(_graphViewport, false);
            var rt = warnGO.AddComponent<RectTransform>();
            StretchFull(rt);
            var tmp = warnGO.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text              = message;
            tmp.fontSize          = 10f;
            tmp.alignment         = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color             = new Color(0.9f, 0.45f, 0.25f);
        }

        // 상단 툴바 — 출발·도착 드롭다운, 지도 원점 복귀, 사이드패널 열기/닫기 토글, 닫기 버튼을 한 줄에 담는다.
        // (2026-07-07 요구사항) 사이드패널과 별개로 그래프 위쪽 전체 폭을 차지한다.
        private void BuildToolbar(RectTransform root)
        {
            var toolbar = NewRect(root, "Toolbar");
            toolbar.anchorMin = new Vector2(0f, 1f);
            toolbar.anchorMax = Vector2.one;
            toolbar.pivot     = new Vector2(0.5f, 1f);
            toolbar.sizeDelta = new Vector2(0f, _toolbarHeight);
            toolbar.anchoredPosition = Vector2.zero;
            PanelBackground.Apply(toolbar, _sidePanelBgColor, _sidePanelBgSprite);
            _toolbarRT = toolbar;

            var hlg = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding               = new RectOffset(3, 3, 2, 2);
            hlg.spacing               = 3f;
            // childControlWidth=false였을 때 버그: LayoutElement.preferredWidth가 자식 실제 크기에 전혀
            // 반영되지 않고(부모의 총 크기 계산에만 쓰임) 새로 생성된 RectTransform 기본 크기(100)를 그대로
            // 써버려서 요소들이 화면 밖으로 밀려나므로, true로 둬야 preferredWidth가 실제로 적용된다.
            hlg.childControlWidth     = true;
            hlg.childControlHeight    = true;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment        = TextAnchor.MiddleLeft;

            ToolbarFixedLabel(toolbar, "출발", 18f);
            _originDropdown = MakeSimpleDropdown(toolbar, "OriginDropdown", 48f);
            _originDropdown.OnSelected = OnOriginDropdownChanged;

            ToolbarFixedLabel(toolbar, "도착", 18f);
            _destDropdown = MakeSimpleDropdown(toolbar, "DestDropdown", 48f);
            _destDropdown.OnSelected = OnDestDropdownChanged;

            ToolbarFixedBtn(toolbar, "원점", ResetToOrigin, "BtnResetView", 24f);

            var panelToggleBtn = ToolbarFixedBtn(toolbar, "패널", () => SetSidePanelOpen(!_sidePanelOpen), "BtnTogglePanel", 24f);
            _toolbarPanelToggleImg = panelToggleBtn.GetComponent<Image>();

            // (2026-07-14) 사이드패널 안에 있던 "경로 선택" 버튼을 여기로 이동 — 사이드패널을 접으면
            // 안 보이던 접근성 문제 해결. 지금 미리보기 중인 경로가 이미 커밋된 상태면 초록(BtnActive류),
            // 아니면 회색(BtnInactive)으로 표시해 "커밋됐는지"를 한눈에 구분한다 (RefreshSelectRouteButton).
            var selectRouteBtn = ToolbarFixedBtn(toolbar, "경로 선택", SelectCurrentPath, "BtnSelectRoute", 32f);
            _btnSelectRouteImg = selectRouteBtn.GetComponent<Image>();

            // "노트로 이동"은 클릭 시 지금 미리보기 중인 경로를 자동으로 먼저 커밋한 뒤 노트를 연다 —
            // "경로 선택"을 누르는 걸 잊어도 노트에 반영되도록 하기 위함(사용자 요청).
            ToolbarFixedBtn(toolbar, "노트로 이동", GoToNote, "BtnGoToNote", 40f);

            // "도감 열기" — 지도 사이드패널에서 도감으로 바로 넘어가는 진입 경로 (Clue_System.md 5단계).
            ToolbarFixedBtn(toolbar, "도감", GoToCodex, "BtnGoToCodex", 28f);

            // 닫기 버튼 — 툴바 최우측 (사이드패널에서는 제거됨)
            var closeBtn = ToolbarFixedBtn(toolbar, $"닫기 [{ToggleKeyLabel}]", Close, "BtnClose", 40f);
            closeBtn.GetComponent<Image>().color = new Color(0.42f, 0.10f, 0.10f);
        }

        // 툴바 전용 — LayoutElement.preferredWidth로 고정폭을 명시한다 (HLG.childControlWidth=true 필요).
        private RectTransform ToolbarFixedBtn(RectTransform toolbar, string label, Action onClick, string id, float width)
        {
            var rt = MakeBtn(toolbar, label, onClick, id: id, fontSize: 8f);
            var le = rt.GetComponent<LayoutElement>();
            le.flexibleWidth  = 0f;
            le.preferredWidth = width;
            return rt;
        }

        private void ToolbarFixedLabel(RectTransform toolbar, string text, float width)
        {
            var tmp = MakeTMP(toolbar, text, 8f, FontStyles.Normal, _toolbarHeight, TextAlignmentOptions.Right);
            tmp.color = Gray;
            var le = tmp.GetComponent<LayoutElement>();
            le.flexibleWidth  = 0f;
            le.preferredWidth = width;
        }

        // 캔버스/Instantiate 없이 같은 UI 계층에 그대로 얹는 자체 드롭다운.
        // 본체(캡션 박스) + 그 밑에 펼쳐지는 옵션 목록(OptionsList, 평소엔 비활성화)으로 구성된다.
        private SimpleDropdown MakeSimpleDropdown(RectTransform parent, string id, float width)
        {
            var dd = new SimpleDropdown();

            var ddRT = NewRect(parent, id);
            var ddLe = ddRT.gameObject.AddComponent<LayoutElement>();
            ddLe.flexibleWidth  = 0f;
            ddLe.preferredWidth = width;
            var ddImg = AddImg(ddRT, _dropdownBgColor);
            var ddBtn = ddRT.gameObject.AddComponent<Button>();
            ddBtn.targetGraphic = ddImg;
            ddBtn.transition    = Selectable.Transition.None;
            ddBtn.onClick.AddListener(() => ToggleSimpleDropdown(dd));
            dd.Root = ddRT;

            var captionRT = NewRect(ddRT, "Caption");
            StretchFull(captionRT);
            captionRT.offsetMin = new Vector2(4f, 1f);
            captionRT.offsetMax = new Vector2(-4f, -1f);
            var captionTMP = captionRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) captionTMP.font = _font;
            captionTMP.fontSize      = _dropdownCaptionFontSize;
            captionTMP.color        = _dropdownCaptionColor;
            captionTMP.alignment    = TextAlignmentOptions.MidlineLeft;
            captionTMP.overflowMode = TextOverflowModes.Ellipsis;
            captionTMP.raycastTarget = false; // 캡션이 드롭다운 전체를 덮으므로, 클릭을 가로챌 필요 없음
            dd.Caption = captionTMP;

            // 옵션 목록 — 드롭다운 본체(ddRT)의 자식으로, 아래로 펼쳐진다. 평소엔 비활성화.
            var list = NewRect(ddRT, "OptionsList");
            list.anchorMin        = new Vector2(0f, 0f);
            list.anchorMax        = new Vector2(1f, 0f);
            list.pivot            = new Vector2(0.5f, 1f);
            list.anchoredPosition = Vector2.zero;
            list.sizeDelta        = new Vector2(0f, _dropdownOptionsListMaxHeight);
            AddImg(list, _dropdownOptionsListBgColor);
            dd.OptionsList = list;

            var scroll = list.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal   = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var vp = NewRect(list, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vp;

            var content = NewRect(vp, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot     = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing               = 1f;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = true; // 옵션 버튼의 LayoutElement.preferredHeight(14f)가 실제로 반영되도록
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            dd.OptionsContent = content;

            list.gameObject.SetActive(false);

            return dd;
        }

        // 프리팹/씬에 재사용된 Toolbar 안에서 기존 드롭다운(있다면)을 파괴하고 같은 자리(형제 인덱스)에
        // 새로 만든다 — Toolbar 자체의 비주얼 커스터마이징(프리팹 편집)은 허용하면서, 드롭다운은 저장
        // 시점 상태가 굳어버리는 문제(2026-07-14 사례, 위 FinalizePanel 주석 참고)를 원천 차단한다.
        private SimpleDropdown RebuildSimpleDropdownInPlace(RectTransform toolbar, string id, float width, Action<int> onSelected)
        {
            var existingTF = toolbar != null ? FindDeepTransform(toolbar, id) : null;
            int siblingIndex = existingTF != null ? existingTF.GetSiblingIndex() : -1;
            if (existingTF != null)
            {
                // 먼저 비활성화한 뒤 Destroy — 활성 상태로 그냥 Destroy하면 같은 프레임에 ScrollRect.LateUpdate가
                // 강제하는 CanvasUpdateRegistry 리빌드가 이미 파괴 중인 TMP(Caption 등)의 서브메시(폴백 폰트)
                // 머티리얼에 접근하려다 MissingReferenceException을 던질 수 있다 (RefreshClueList/FillSimpleDropdown과 동일 패턴).
                existingTF.gameObject.SetActive(false);
                Destroy(existingTF.gameObject);
            }

            var dd = MakeSimpleDropdown(toolbar, id, width);
            if (siblingIndex >= 0) dd.Root.SetSiblingIndex(siblingIndex);
            dd.OnSelected = onSelected;
            return dd;
        }

        private void BuildSidePanel(RectTransform parent, float panelW)
        {
            var scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal        = false;
            scroll.vertical          = true;
            scroll.scrollSensitivity = 8f;
            scroll.movementType      = ScrollRect.MovementType.Elastic;

            var vp = NewRect(parent, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vp;
            _sidePanelScrollGO = vp; // 패널 접었을 때 이 뷰포트만 비활성화 (재오픈 탭은 parent 직속이라 별개)

            var content = NewRect(vp, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot     = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding               = new RectOffset(3, 3, 4, 3);
            vlg.spacing               = 2f;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = false;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;

            MakeTMP(content, "지  도", 14f, FontStyles.Bold, 15f, TextAlignmentOptions.Center);

            MakeSep(content);

            MakeTMP(content, "경로 방식", 8f, FontStyles.Normal, 12f).color = Gray;
            var pathRow = NewRow(content, 20f);
            pathRow.name = "PathTypeRow";
            var btnSh = MakeBtn(pathRow, "최단",      () => SetPathType(PathType.Shortest),      id: "BtnShortest", fontSize: 8f);
            var btnBl = MakeBtn(pathRow, "균형",      () => SetPathType(PathType.Balanced),      id: "BtnBalanced", fontSize: 8f);
            var btnMd = MakeBtn(pathRow, "최소난이도", () => SetPathType(PathType.MinDifficulty), id: "BtnMinDiff", fontSize: 8f);
            _btnShortestImg = btnSh.GetComponent<Image>();
            _btnBalancedImg = btnBl.GetComponent<Image>();
            _btnMinDiffImg  = btnMd.GetComponent<Image>();

            MakeSep(content);

            MakeTMP(content, "단서 없는 맵 경유", 8f, FontStyles.Normal, 12f).color = Gray;
            var clueRow = NewRow(content, 20f);
            clueRow.name = "NoClueOptionRow";
            var btnAllow = MakeBtn(clueRow, "단서 없어도 이동", () => SetAvoidNoClueNodes(false), id: "BtnAllowNoClue", fontSize: 8f);
            var btnAvoid = MakeBtn(clueRow, "단서 우선 경로",   () => SetAvoidNoClueNodes(true),  id: "BtnAvoidNoClue", fontSize: 8f);
            _btnAllowNoClueImg = btnAllow.GetComponent<Image>();
            _btnAvoidNoClueImg = btnAvoid.GetComponent<Image>();

            MakeSep(content);

            MakeTMP(content, "장착 장비 (클릭)", 8f, FontStyles.Normal, 12f).color = Gray;
            _gearListTMP = MakeTMP(content, "없음", 8f, FontStyles.Normal, 12f, id: "GearListLabel");
            _gearListTMP.color = Gray;

            var emotionDefs = new (EmotionColor ec, string nm)[]
            {
                (EmotionColor.SadnessBlue,        "슬픔"),
                (EmotionColor.ExcitementDeepPink, "흥분"),
                (EmotionColor.HappinessYellow,    "행복"),
                (EmotionColor.AngerScarlet,       "분노"),
                (EmotionColor.VoidBlack,          "공허"),
                (EmotionColor.FearDarkRed,        "공포"),
            };

            for (int i = 0; i < emotionDefs.Length; i += 2)
            {
                var row = NewRow(content, 10f);
                AddGearBtn(row, emotionDefs[i].ec, emotionDefs[i].nm);
                if (i + 1 < emotionDefs.Length)
                    AddGearBtn(row, emotionDefs[i + 1].ec, emotionDefs[i + 1].nm);
            }

            MakeSep(content);

            MakeTMP(content, "경로 정보", 8f, FontStyles.Normal, 12f).color = Gray;
            _pathInfoTMP = MakeTMP(content, "노드 클릭 →\n목적지 선택", 8f, FontStyles.Normal, 28f, id: "PathInfoLabel");
            _pathInfoTMP.color = Color.white;
            _pathInfoTMP.enableWordWrapping = false;

            BuildClueListSection(content);
        }

        // 사이드패널 좌측 가장자리에 항상 떠 있는 재오픈/접기 탭. 스크롤뷰(Content)와 별개 오브젝트라
        // 패널이 접혀 Viewport가 비활성화돼도 계속 보이고 클릭할 수 있다.
        private void BuildSidePanelToggleTab(RectTransform parent)
        {
            var tab = NewRect(parent, "SidePanelToggleTab");
            tab.anchorMin = new Vector2(0f, 0.5f);
            tab.anchorMax = new Vector2(0f, 0.5f);
            tab.pivot     = new Vector2(1f, 0.5f);
            tab.sizeDelta = new Vector2(12f, 28f);
            tab.anchoredPosition = Vector2.zero;

            var img = PanelBackground.Apply(tab, _sidePanelBgColor, _sidePanelBgSprite);
            var btn = tab.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(() => SetSidePanelOpen(!_sidePanelOpen));

            var lblRT = NewRect(tab, "Arrow");
            StretchFull(lblRT);
            var tmp = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize          = 8f;
            tmp.alignment         = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color             = Color.white;
            tmp.text              = "◀";
            _sidePanelToggleArrowTMP = tmp;
        }

        // 선택된 맵 노드의 단서 목록 섹션. content(SidePanel의 ScrollRect Content) 끝에 추가된다.
        private void BuildClueListSection(RectTransform content)
        {
            MakeSep(content);
            MakeTMP(content, "선택한 맵의 단서", 8f, FontStyles.Normal, 12f).color = Gray;

            _clueListContent = NewRect(content, "ClueList");
            var csf = _clueListContent.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vlg = _clueListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing               = 1f;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = true; // 단서 항목의 펼침/접힘 높이(LayoutElement.preferredHeight)가 실제로 반영되도록
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
        }

        private void AddGearBtn(RectTransform row, EmotionColor ec, string nm)
        {
            var captured = ec;
            var rt = MakeBtn(row, nm, () => ToggleGear(captured), id: "GearBtn_" + ec.ToString(), fontSize: 8f);
            var img = rt.GetComponent<Image>();
            img.color = EmotionColorConfig.GetColor(ec) * 0.45f;
            _gearBtnImages[ec] = img;
        }

        // ─── 프리팹 지원 ─────────────────────────────────────────

        // 프리팹 인스턴스에서 이름 기반으로 핵심 참조를 찾아 연결한다.
        private void BindRefsFromHierarchy()
        {
            _graphViewport  = FindDeepChild<RectTransform>("GraphViewport");
            _graphContainer = FindDeepChild<RectTransform>("GraphContainer");
            _labelContainer = FindDeepChild<RectTransform>("LabelContainer");

            var graphAreaTF = FindDeepTransform(_panelGO.transform, "GraphArea");
            if (graphAreaTF != null) { _graphPanZoom = graphAreaTF.GetComponent<GraphPanZoom>(); _graphAreaRT = (RectTransform)graphAreaTF; }
            _sidePanelRT     = FindDeepChild<RectTransform>("SidePanel");
            _toolbarRT       = FindDeepChild<RectTransform>("Toolbar");
            _sidePanelScrollGO = FindDeepChild<RectTransform>("Viewport");

            var toggleTabTF = FindDeepTransform(_panelGO.transform, "SidePanelToggleTab");
            if (toggleTabTF != null) _sidePanelToggleArrowTMP = FindDeepTransform(toggleTabTF, "Arrow")?.GetComponent<TextMeshProUGUI>();

            // 프리팹/씬 재사용 경로 — 배경 스프라이트 인스펙터 값을 바꿔도 반영되도록 여기서도 적용.
            PanelBackground.Apply((RectTransform)_panelGO.transform, _rootBgColor, _rootBgSprite);
            PanelBackground.Apply(_sidePanelRT, _sidePanelBgColor, _sidePanelBgSprite);
            PanelBackground.Apply(_toolbarRT, _sidePanelBgColor, _sidePanelBgSprite);
            PanelBackground.Apply(_graphAreaRT, _graphAreaBgColor, _graphAreaBgSprite);
            if (toggleTabTF != null) PanelBackground.Apply((RectTransform)toggleTabTF, _sidePanelBgColor, _sidePanelBgSprite);

            var panelToggleTF = FindDeepTransform(_panelGO.transform, "BtnTogglePanel");
            if (panelToggleTF != null) _toolbarPanelToggleImg = panelToggleTF.GetComponent<Image>();

            // 드롭다운(_originDropdown/_destDropdown)은 여기서 바인딩하지 않는다 — FinalizePanel()이
            // RebuildSimpleDropdownInPlace()로 항상 새로 만들어 대입하므로 이중 작업이라 생략.

            _pathInfoTMP    = FindDeepChild<TextMeshProUGUI>("PathInfoLabel");
            _gearListTMP    = FindDeepChild<TextMeshProUGUI>("GearListLabel");
            _clueListContent = FindDeepChild<RectTransform>("ClueList");
            if (_clueListContent == null)
            {
                var contentTF = FindDeepTransform(_panelGO.transform, "Content");
                if (contentTF != null) BuildClueListSection((RectTransform)contentTF);
            }

            var sh = FindDeepTransform(_panelGO.transform, "BtnShortest");
            if (sh != null) _btnShortestImg = sh.GetComponent<Image>();

            var bl = FindDeepTransform(_panelGO.transform, "BtnBalanced");
            if (bl != null) _btnBalancedImg = bl.GetComponent<Image>();

            var md = FindDeepTransform(_panelGO.transform, "BtnMinDiff");
            if (md != null) _btnMinDiffImg = md.GetComponent<Image>();

            var allowNc = FindDeepTransform(_panelGO.transform, "BtnAllowNoClue");
            if (allowNc != null) _btnAllowNoClueImg = allowNc.GetComponent<Image>();

            var avoidNc = FindDeepTransform(_panelGO.transform, "BtnAvoidNoClue");
            if (avoidNc != null) _btnAvoidNoClueImg = avoidNc.GetComponent<Image>();

            var selectRoute = FindDeepTransform(_panelGO.transform, "BtnSelectRoute");
            if (selectRoute != null) _btnSelectRouteImg = selectRoute.GetComponent<Image>();

            _gearBtnImages.Clear();
            foreach (EmotionColor ec in Enum.GetValues(typeof(EmotionColor)))
            {
                var go = FindDeepTransform(_panelGO.transform, "GearBtn_" + ec.ToString());
                if (go != null) _gearBtnImages[ec] = go.GetComponent<Image>();
            }
        }

        // 프리팹 인스턴스의 버튼에 런타임 콜백을 연결한다.
        private void WireButtonCallbacks()
        {
            FindDeepTransform(_panelGO.transform, "BtnShortest")
                ?.GetComponent<Button>()?.onClick.AddListener(() => SetPathType(PathType.Shortest));

            FindDeepTransform(_panelGO.transform, "BtnBalanced")
                ?.GetComponent<Button>()?.onClick.AddListener(() => SetPathType(PathType.Balanced));

            FindDeepTransform(_panelGO.transform, "BtnMinDiff")
                ?.GetComponent<Button>()?.onClick.AddListener(() => SetPathType(PathType.MinDifficulty));

            FindDeepTransform(_panelGO.transform, "BtnAllowNoClue")
                ?.GetComponent<Button>()?.onClick.AddListener(() => SetAvoidNoClueNodes(false));

            FindDeepTransform(_panelGO.transform, "BtnAvoidNoClue")
                ?.GetComponent<Button>()?.onClick.AddListener(() => SetAvoidNoClueNodes(true));

            FindDeepTransform(_panelGO.transform, "BtnClose")
                ?.GetComponent<Button>()?.onClick.AddListener(Close);

            FindDeepTransform(_panelGO.transform, "BtnResetView")
                ?.GetComponent<Button>()?.onClick.AddListener(ResetToOrigin);

            FindDeepTransform(_panelGO.transform, "BtnTogglePanel")
                ?.GetComponent<Button>()?.onClick.AddListener(() => SetSidePanelOpen(!_sidePanelOpen));

            FindDeepTransform(_panelGO.transform, "BtnGoToNote")
                ?.GetComponent<Button>()?.onClick.AddListener(GoToNote);

            FindDeepTransform(_panelGO.transform, "BtnGoToCodex")
                ?.GetComponent<Button>()?.onClick.AddListener(GoToCodex);

            FindDeepTransform(_panelGO.transform, "BtnSelectRoute")
                ?.GetComponent<Button>()?.onClick.AddListener(SelectCurrentPath);

            FindDeepTransform(_panelGO.transform, "SidePanelToggleTab")
                ?.GetComponent<Button>()?.onClick.AddListener(() => SetSidePanelOpen(!_sidePanelOpen));

            if (_originDropdown != null) _originDropdown.OnSelected = OnOriginDropdownChanged;
            if (_destDropdown   != null) _destDropdown.OnSelected   = OnDestDropdownChanged;

            foreach (EmotionColor ec in Enum.GetValues(typeof(EmotionColor)))
            {
                var captured = ec;
                FindDeepTransform(_panelGO.transform, "GearBtn_" + ec.ToString())
                    ?.GetComponent<Button>()?.onClick.AddListener(() => ToggleGear(captured));
            }
        }

        private T FindDeepChild<T>(string childName) where T : Component
        {
            var t = FindDeepTransform(_panelGO.transform, childName);
            return t != null ? t.GetComponent<T>() : null;
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

        // ─── UI 헬퍼 ─────────────────────────────────────────────

        private static readonly Color Gray        = new(0.55f, 0.60f, 0.65f);
        private static readonly Color BtnActive   = new(0.25f, 0.42f, 0.72f);
        private static readonly Color BtnInactive = new(0.17f, 0.21f, 0.30f);

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

        private TextMeshProUGUI MakeTMP(RectTransform parent, string text,
            float fontSize, FontStyles style, float height,
            TextAlignmentOptions align = TextAlignmentOptions.Left,
            string id = null)
        {
            var goName = id ?? ("Lbl_" + text.Replace(" ", "_"));
            var rt = NewRect(parent, goName);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth   = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text               = text;
            tmp.fontSize           = fontSize;
            tmp.fontStyle          = style;
            tmp.alignment          = align;
            tmp.verticalAlignment  = VerticalAlignmentOptions.Middle;
            tmp.color              = Color.white;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        private static void MakeSep(RectTransform parent)
        {
            var rt = NewRect(parent, "Sep");
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            AddImg(rt, new Color(1f, 1f, 1f, 0.10f));
        }

        private static RectTransform NewRow(RectTransform parent, float height)
        {
            var rt = NewRect(parent, "Row");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth   = 1f;
            var hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 2f;
            hlg.childControlWidth     = true;
            hlg.childControlHeight    = true;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            return rt;
        }

        private RectTransform MakeBtn(RectTransform parent, string label, Action onClick,
            float h = 0f, string id = null, float fontSize = 10f)
        {
            var goName = id ?? ("Btn_" + label);
            var rt = NewRect(parent, goName);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            if (h > 0f) le.preferredHeight = h;
            var bgImg = AddImg(rt, BtnInactive);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lblRT = NewRect(rt, "Text");
            StretchFull(lblRT);
            var tmp = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text              = label;
            tmp.fontSize          = fontSize;
            tmp.alignment         = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color             = Color.white;
            return rt;
        }

        private static Sprite GetCircleSprite() => null;
    }
}
