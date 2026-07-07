using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.MapView
{
    // 씬 내 맵 뷰어 창 — 루트파인딩 시스템의 "그리기" 담당.
    // Canvas 직속 자식 GO에 이 컴포넌트를 붙인다. (BaseWindow와 동일한 계층 구조)
    // 이 GO 자체는 항상 활성 — M키 감지를 위해 Update가 계속 동작.
    // 내부 패널(_panelGO)만 Open/Close 시 토글된다.
    //
    // 상태를 직접 소유하지 않는다:
    //   - 장착 장비, 진행 상태(방문/단서/클리어), 경로 규칙 → RouteModule이 소유
    //   - 이 클래스는 모듈의 상태를 읽어 노드·엣지 색상과 패널 텍스트만 갱신한다
    //   - 장비 버튼 클릭 등 입력은 모듈에 위임하고 결과를 다시 그린다
    //
    // 의존: MapGraph(씬 배치 필요), RouteModule(없으면 자동 생성)
    public class MapViewer : MonoBehaviour
    {
        [Header("조작")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.M;

        [Header("폰트")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("그래프 레이아웃")]
        [SerializeField] private float _nodeSize     =  8f;
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
        [SerializeField] private Color _rootBgColor      = new Color(0.04f, 0.04f, 0.08f, 0.94f);
        [SerializeField] private Color _sidePanelBgColor = new Color(0.07f, 0.09f, 0.14f, 0.97f);
        [SerializeField] private Color _graphAreaBgColor = new Color(0.06f, 0.07f, 0.11f, 0.80f);

        [Header("경로 강조 스타일 (노드 선택 시)")]
        [SerializeField] private Color _pathHighlightColor     = new Color(1.00f, 0.82f, 0.08f, 1.00f);
        [SerializeField] private float _edgeThicknessNormal    = 3f;
        [SerializeField] private float _edgeThicknessHighlight = 7f;

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
            var rt = GetComponent<RectTransform>();
            if (rt != null) StretchFull(rt);
            BuildUI();
            var canvas = GetComponentInParent<Canvas>();
            _graphPanZoom?.Init(_graphContainer, _graphViewport, canvas);
            _inputManager = FindObjectOfType<InputManager>();
        }

        private void Start()
        {
            if (MapGraph.Instance != null)
                PopulateGraph();
            _panelGO.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey)) Toggle();
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
            Debug.Log($"[MapViewer] Open() — nodeViews={_nodeViews.Count}  edgeViews={_edgeViews.Count}  MapGraph={MapGraph.Instance != null}");
            if (_nodeViews.Count == 0 && MapGraph.Instance != null)
                PopulateGraph();
            _panelGO.SetActive(true);
            _inputManager?.MENUMode();
            Refresh();
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

                var lblGO = new GameObject($"Lbl_{conn.guid}");
                lblGO.transform.SetParent(_labelContainer, false);
                var lblRT = lblGO.AddComponent<RectTransform>();
                lblRT.anchorMin = lblRT.anchorMax = Vector2.zero;
                lblRT.pivot     = new Vector2(0.5f, 0.5f);
                lblRT.sizeDelta = new Vector2(24f, 10f);
                var lblTMP = lblGO.AddComponent<TextMeshProUGUI>();
                if (_font != null) lblTMP.font = _font;
                lblTMP.fontSize  = 8f;
                lblTMP.alignment = TextAlignmentOptions.Center;
                lblTMP.color     = new Color(1f, 1f, 1f, 0.88f);

                var ev = edgeGO.AddComponent<MapEdgeView>();
                ev.Init(conn, lblTMP);
                ev.SetHighlightStyle(_pathHighlightColor, _edgeThicknessNormal, _edgeThicknessHighlight);
                ev.SetLayout(fp, tp);
                _edgeViews.Add(ev);
            }

            // 노드 (엣지 위 레이어)
            var circleSprite = GetCircleSprite();
            foreach (var node in nodes)
            {
                if (!_nodePositions.TryGetValue(node.guid, out var pos)) continue;

                var nodeGO = new GameObject($"Node_{node.guid}");
                nodeGO.transform.SetParent(_graphContainer, false);
                var nodeRT = nodeGO.AddComponent<RectTransform>();
                nodeRT.anchorMin        = nodeRT.anchorMax = Vector2.zero;
                nodeRT.pivot            = Vector2.one * 0.5f;
                nodeRT.anchoredPosition = pos;
                nodeRT.sizeDelta        = Vector2.one * _nodeSize;

                var img = nodeGO.AddComponent<Image>();
                if (circleSprite != null) img.sprite = circleSprite;
                var btn = nodeGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.transition    = Selectable.Transition.None;

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

                var nv = nodeGO.AddComponent<MapNodeView>();
                nv.Init(node);
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
                var d       = kv.Value.Data;
                bool vis    = progress.IsNodeVisited(d);
                bool clue   = progress.HasNodeClue(d);
                bool start  = d.isStartNode;
                bool sel    = _selectedDest?.Data.guid == d.guid;
                bool onPath = IsNodeOnPath(selectablePath, d.guid);
                bool onBlockedPath = IsNodeOnPath(blockedPath, d.guid);
                bool known = shownNodeGuids.Contains(d.guid);
                kv.Value.SetShown(known);
                kv.Value.SetState(visited: vis, hasClue: clue, isStart: start, isSelected: sel, isOnPath: onPath, isOnBlockedPath: onBlockedPath, known: known);
            }

            foreach (var ev in _edgeViews)
            {
                var d        = ev.Data;
                float df     = DifficultyCalculator.Calculate(d, gears);
                bool cleared = progress.IsConnectionCleared(d);
                bool hasClue = progress.HasConnectionClue(d);
                bool onPath  = IsEdgeOnPath(selectablePath, d.guid);
                bool onBlockedPath = IsEdgeOnPath(blockedPath, d.guid);
                bool passable = d.IsPassableWith(gears);
                ev.SetShown(shownEdgeGuids.Contains(d.guid));
                ev.SetState(cleared: cleared, hasClue: hasClue, isOnPath: onPath, isOnBlockedPath: onBlockedPath,
                    isPassable: passable, requiredGears: d.requiredGears, difficulty: df);
            }

            RefreshPathLabel();
            RefreshPathTypeButtons();
            RefreshNoClueOptionButtons();
            RefreshGearPanel();
            RefreshClueMarkers();
            RefreshClueList();
            RefreshDropdowns();
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
            _tooltipRT.sizeDelta = new Vector2(120f, 60f);
            AddImg(_tooltipRT, new Color(0.05f, 0.05f, 0.08f, 0.95f));

            var txtRT = NewRect(_tooltipRT, "Text");
            StretchFull(txtRT);
            _tooltipTMP = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) _tooltipTMP.font = _font;
            _tooltipTMP.fontSize  = 7f;
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
        private void ShowNodeTooltip(MapNodeView view)
        {
            Debug.Log($"[MapViewer] 노드 호버 진입: {view.Data.nodeName}"); // TODO(임시 진단용): 콘솔에 안 찍히면 EventSystem/InputModule 쪽 문제, 찍히는데 화면에 안 뜨면 아래 표시 로직 문제
            EnsureTooltip();
            var node     = view.Data;
            var progress = RouteModule.Instance.Progress;

            var sb = new StringBuilder();
            sb.Append($"<b>{node.nodeName}</b>\n");
            if (!string.IsNullOrEmpty(node.description)) sb.Append($"{node.description}\n");

            if (node.clueIds != null)
            {
                foreach (var clueId in node.clueIds)
                {
                    if (!progress.IsClueAcquired(clueId)) continue;
                    var clue = MapGraph.Instance.GetClue(clueId);
                    if (clue != null) sb.Append($"[단서] {clue.name}\n");
                }
            }

            _tooltipTMP.text = sb.ToString();

            var nodeRT = (RectTransform)view.transform;
            _tooltipRT.anchoredPosition = nodeRT.anchoredPosition + new Vector2(_nodeSize, 0f);
            _tooltipRT.localScale = Vector3.one / Mathf.Max(_graphPanZoom != null ? _graphPanZoom.Scale : 1f, 0.0001f);
            _tooltipRT.gameObject.SetActive(true);
            _tooltipRT.SetAsLastSibling();
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

        // 사이드패널의 단서 목록(텍스트)을 갱신한다. 클릭하면 그래프가 해당 맵으로 포커스 이동.
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

            var acquired = RouteModule.Instance.Progress.AcquiredClueIds;
            if (acquired.Count == 0)
            {
                MakeTMP(_clueListContent, "없음", 8f, FontStyles.Normal, 12f).color = Gray;
                return;
            }

            foreach (var clueId in acquired)
            {
                var clue = MapGraph.Instance.GetClue(clueId);
                if (clue != null) MakeClueItem(_clueListContent, clue);
            }
        }

        private void MakeClueItem(RectTransform parent, ClueData clue)
        {
            var rt = NewRect(parent, "Clue_" + clue.id);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
            le.flexibleWidth   = 1f;

            var bgImg = AddImg(rt, new Color(0.12f, 0.15f, 0.22f));
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(() => FocusOnClue(clue));

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text  = $"<b>{clue.name}</b>\n{clue.description}";
            tmp.fontSize = 7f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.margin = new Vector4(2f, 1f, 2f, 1f);
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
                var optBtn = MakeBtn(dd.OptionsContent, node.nodeName, () => SelectSimpleDropdownOption(dd, captured), fontSize: 7f);
                var le = optBtn.GetComponent<LayoutElement>();
                le.preferredHeight = 14f;
                if (current != null && node.guid == current.guid) selectedIdx = i;
            }

            dd.SelectedIndex = _knownNodesForDropdown.Count > 0 ? selectedIdx : -1;
            dd.Caption.text  = _knownNodesForDropdown.Count > 0 ? _knownNodesForDropdown[selectedIdx].nodeName : "";
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
            // Toolbar가 없으면 구버전(툴바 도입 이전) — 파괴 후 재생성
            var existing = transform.Find("MapPanel");
            if (existing != null)
            {
                bool existingCurrent = FindDeepTransform(existing, "Toolbar") != null;

                if (existingCurrent)
                {
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Destroy(existing.gameObject);
            }

            // 프리팹이 지정되어 있으면 인스턴스화 후 참조를 바인딩하고 콜백만 재연결
            // Toolbar 없으면 구버전 취급. GraphArea는 프리팹에서 지워둔 경우(겹침 방지) 자동 생성으로 보강한다.
            if (_panelPrefab != null)
            {
                bool prefabCurrent = FindDeepTransform(_panelPrefab.transform, "Toolbar") != null;

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
            AddImg(root, _rootBgColor);

            float panelW = _sidePanelWidth;

            var side = NewRect(root, "SidePanel");
            side.anchorMin = new Vector2(1f, 0f);
            side.anchorMax = Vector2.one;
            side.offsetMin = new Vector2(-panelW, 0f);
            side.offsetMax = new Vector2(0f, -_toolbarHeight);
            AddImg(side, _sidePanelBgColor);
            _sidePanelRT = side;
            BuildSidePanel(side, panelW);
            BuildSidePanelToggleTab(side);

            BuildGraphArea(root);

            // Toolbar를 마지막 자식으로 만들어야 한다 — GraphArea/SidePanel 자체는 툴바 아래 영역에만
            // 앵커돼 있어 겹치지 않지만, 드롭다운 옵션 목록은 툴바 바깥(아래쪽, GraphArea 영역)까지
            // 펼쳐지므로 그보다 늦게 그려지는 형제(=나중에 생성된 GraphArea)에게 가려진다.
            BuildToolbar(root);
        }

        // 기존/프리팹 패널을 재사용할 때의 공통 마무리: 전체 스트레치, 참조 바인딩, 버튼 콜백 연결.
        // GraphArea가 빠져 있으면(겹침 방지로 지운 경우) 보강 생성한다.
        private void FinalizePanel(RectTransform rt)
        {
            if (rt != null) StretchFull(rt);
            BindRefsFromHierarchy();
            WireButtonCallbacks();
            if (_graphContainer == null) BuildGraphArea(rt);
            _toolbarRT?.SetAsLastSibling(); // 드롭다운 목록이 GraphArea에 가리지 않도록 툴바를 항상 맨 위로
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
            AddImg(parent, _graphAreaBgColor);
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
            AddImg(toolbar, _sidePanelBgColor);
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

            // 닫기 버튼 — 툴바 최우측 (사이드패널에서는 제거됨)
            var closeBtn = ToolbarFixedBtn(toolbar, $"닫기 [{_toggleKey}]", Close, "BtnClose", 40f);
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
            var ddImg = AddImg(ddRT, BtnInactive);
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
            captionTMP.fontSize      = 7f;
            captionTMP.color        = Color.white;
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
            list.sizeDelta        = new Vector2(0f, 90f);
            AddImg(list, new Color(0.90f, 0.90f, 0.20f, 1f)); // 배경과 확실히 구분되는 밝은 색
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
            vlg.childControlHeight    = false;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            dd.OptionsContent = content;

            list.gameObject.SetActive(false);

            return dd;
        }

        // 프리팹 재사용 경로에서 이름 기반으로 SimpleDropdown 참조를 재구성한다.
        private SimpleDropdown BindSimpleDropdown(string rootName)
        {
            var rootTF = FindDeepTransform(_panelGO.transform, rootName);
            if (rootTF == null) return null;

            var dd = new SimpleDropdown { Root = (RectTransform)rootTF };
            dd.Caption        = FindDeepTransform(rootTF, "Caption")?.GetComponent<TextMeshProUGUI>();
            var listTF        = FindDeepTransform(rootTF, "OptionsList");
            dd.OptionsList    = listTF as RectTransform;
            dd.OptionsContent = listTF != null ? FindDeepTransform(listTF, "Content") as RectTransform : null;

            var btn = rootTF.GetComponent<Button>();
            btn?.onClick.AddListener(() => ToggleSimpleDropdown(dd));
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

            var img = AddImg(tab, _sidePanelBgColor);
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

        // 획득한 단서 목록 섹션. content(SidePanel의 ScrollRect Content) 끝에 추가된다.
        private void BuildClueListSection(RectTransform content)
        {
            MakeSep(content);
            MakeTMP(content, "단서", 8f, FontStyles.Normal, 12f).color = Gray;

            _clueListContent = NewRect(content, "ClueList");
            var vlg = _clueListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing               = 1f;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = false;
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

            var panelToggleTF = FindDeepTransform(_panelGO.transform, "BtnTogglePanel");
            if (panelToggleTF != null) _toolbarPanelToggleImg = panelToggleTF.GetComponent<Image>();

            _originDropdown = BindSimpleDropdown("OriginDropdown");
            _destDropdown   = BindSimpleDropdown("DestDropdown");

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
