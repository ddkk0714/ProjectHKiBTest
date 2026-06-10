using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.MapView
{
    // 씬 내 맵 뷰어 창.
    // Canvas 직속 자식 GO에 이 컴포넌트를 붙인다. (BaseWindow와 동일한 계층 구조)
    // 이 GO 자체는 항상 활성 — M키 감지를 위해 Update가 계속 동작.
    // 내부 패널(_panelGO)만 Open/Close 시 토글된다.
    //
    // 의존: MapGraph, RouteManager, DifficultyCalculator, MapPathFinder (씬에 존재해야 함)
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

        private MapNodeView            _selectedDest;
        private PathType               _pathType    = PathType.Shortest;
        private PathResult             _currentPath;
        private readonly List<EmotionColor> _gears  = new();

        private TextMeshProUGUI _pathInfoTMP;
        private TextMeshProUGUI _gearListTMP;

        private Image        _btnShortestImg;
        private Image        _btnMinDiffImg;
        private Image        _btnBalancedImg;
        private GraphPanZoom _graphPanZoom;
        private InputManager _inputManager;

        private readonly Dictionary<EmotionColor, Image> _gearBtnImages = new();

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
            if (RouteManager.Instance != null && RouteManager.Instance.IsTraveling)
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
                nv.OnClicked += OnNodeClicked;
                _nodeViews[node.guid] = nv;
            }
        }

        // ─── 상태 갱신 ───────────────────────────────────────────

        private void Refresh()
        {
            if (MapGraph.Instance == null) return;
            var graph = MapGraph.Instance;
            var gears = _gears.Count > 0 ? _gears.ToArray() : null;

            // 통과 불가 구간을 포함한 경로(IsBlocked)는 선택 불가 — 빨강으로 표시만 하고,
            // 실제 선택 가능한 경로는 AlternativePath.
            bool blocked = _currentPath != null && _currentPath.IsBlocked;
            var selectablePath = blocked ? _currentPath.AlternativePath : _currentPath;
            var blockedPath    = blocked ? _currentPath : null;

            foreach (var kv in _nodeViews)
            {
                var d       = kv.Value.Data;
                bool vis    = graph.IsNodeVisited(d);
                bool clue   = graph.HasNodeClue(d);
                bool start  = d.isStartNode;
                bool sel    = _selectedDest?.Data.guid == d.guid;
                bool onPath = IsNodeOnPath(selectablePath, d.guid);
                bool onBlockedPath = IsNodeOnPath(blockedPath, d.guid);
                kv.Value.SetState(visited: vis, hasClue: clue, isStart: start, isSelected: sel, isOnPath: onPath, isOnBlockedPath: onBlockedPath);
            }

            foreach (var ev in _edgeViews)
            {
                var d        = ev.Data;
                float df     = DifficultyCalculator.Calculate(d, gears);
                bool cleared = graph.IsConnectionCleared(d);
                bool hasClue = graph.HasConnectionClue(d);
                bool onPath  = IsEdgeOnPath(selectablePath, d.guid);
                bool onBlockedPath = IsEdgeOnPath(blockedPath, d.guid);
                bool passable = d.IsPassableWith(gears);
                ev.SetState(cleared: cleared, hasClue: hasClue, isOnPath: onPath, isOnBlockedPath: onBlockedPath,
                    isPassable: passable, requiredGears: d.requiredGears, difficulty: df);
            }

            RefreshPathLabel();
            RefreshPathTypeButtons();
            RefreshClueMarkers();
            RefreshClueList();
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

            foreach (var clueId in MapGraph.Instance.AcquiredClueIds)
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

            var startNode  = GetStartNode();
            var targetNode = MapGraph.Instance.GetNode(clue.targetMapGuid);
            if (startNode != null && targetNode != null && startNode.guid != targetNode.guid)
            {
                var gears = _gears.Count > 0 ? _gears.ToArray() : null;
                AppendRouteInfo(sb, "최단",      PathType.Shortest,      startNode, targetNode, gears);
                AppendRouteInfo(sb, "균형",      PathType.Balanced,      startNode, targetNode, gears);
                AppendRouteInfo(sb, "최소난이도", PathType.MinDifficulty, startNode, targetNode, gears);
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

        private static void AppendRouteInfo(StringBuilder sb, string label, PathType type,
            MapNodeData start, MapNodeData dest, EmotionColor[] gears)
        {
            var result = MapPathFinder.FindPath(start, dest, type, MapGraph.Instance, gears);
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

        private static MapNodeData GetStartNode()
        {
            foreach (var n in MapGraph.Instance.AllNodes)
                if (n.isStartNode) return n;
            return null;
        }

        // 사이드패널의 단서 목록(텍스트)을 갱신한다. 클릭하면 그래프가 해당 맵으로 포커스 이동.
        private void RefreshClueList()
        {
            if (_clueListContent == null || MapGraph.Instance == null) return;

            for (int i = _clueListContent.childCount - 1; i >= 0; i--)
                Destroy(_clueListContent.GetChild(i).gameObject);

            var acquired = MapGraph.Instance.AcquiredClueIds;
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

        private void RecalcPath()
        {
            _currentPath = null;
            if (_selectedDest == null || MapGraph.Instance == null) return;

            var startNode = GetStartNode();
            if (startNode == null || startNode.guid == _selectedDest.Data.guid) return;

            var gears = _gears.Count > 0 ? _gears.ToArray() : null;
            _currentPath = MapPathFinder.FindPath(startNode, _selectedDest.Data, _pathType, MapGraph.Instance, gears);
        }

        private void SetPathType(PathType pt)
        {
            _pathType = pt;
            RecalcPath();
            Refresh();
        }

        private void ToggleGear(EmotionColor ec)
        {
            if (_gears.Contains(ec)) _gears.Remove(ec);
            else _gears.Add(ec);

            if (_gearBtnImages.TryGetValue(ec, out var img))
            {
                var baseColor = EmotionColorConfig.GetColor(ec);
                img.color = _gears.Contains(ec) ? baseColor : baseColor * 0.45f;
            }

            if (_gearListTMP != null)
            {
                if (_gears.Count == 0)
                {
                    _gearListTMP.text = "없음";
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var g in _gears)
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(EmotionColorConfig.GetName(g));
                    }
                    _gearListTMP.text = sb.ToString();
                }
            }

            RecalcPath();
            Refresh();
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
                        sb.Append("[!] 차선 경로에 단서 없는 맵 포함");
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
                    sb.Append("[!] 단서 없는 맵 포함");
            }

            _pathInfoTMP.text = sb.ToString();
        }

        private void RefreshPathTypeButtons()
        {
            if (_btnShortestImg != null) _btnShortestImg.color = _pathType == PathType.Shortest      ? BtnActive : BtnInactive;
            if (_btnBalancedImg != null) _btnBalancedImg.color = _pathType == PathType.Balanced      ? BtnActive : BtnInactive;
            if (_btnMinDiffImg  != null) _btnMinDiffImg.color  = _pathType == PathType.MinDifficulty ? BtnActive : BtnInactive;
        }

        // ─── UI 구축 ─────────────────────────────────────────────

        private void BuildUI()
        {
            // 씬 계층에 MapPanel이 이미 자식으로 배치돼 있으면 재사용
            // BtnBalanced가 없으면 구버전 — 파괴 후 재생성
            var existing = transform.Find("MapPanel");
            if (existing != null)
            {
                bool existingCurrent = FindDeepTransform(existing, "BtnBalanced") != null;

                if (existingCurrent)
                {
                    _panelGO = existing.gameObject;
                    FinalizePanel(_panelGO.GetComponent<RectTransform>());
                    return;
                }
                Destroy(existing.gameObject);
            }

            // 프리팹이 지정되어 있으면 인스턴스화 후 참조를 바인딩하고 콜백만 재연결
            // BtnBalanced 없으면 구버전 취급. GraphArea는 프리팹에서 지워둔 경우(겹침 방지) 자동 생성으로 보강한다.
            if (_panelPrefab != null)
            {
                bool prefabCurrent = FindDeepTransform(_panelPrefab.transform, "BtnBalanced") != null;

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
            side.offsetMax = Vector2.zero;
            AddImg(side, _sidePanelBgColor);
            BuildSidePanel(side, panelW);

            BuildGraphArea(root);
        }

        // 기존/프리팹 패널을 재사용할 때의 공통 마무리: 전체 스트레치, 참조 바인딩, 버튼 콜백 연결.
        // GraphArea가 빠져 있으면(겹침 방지로 지운 경우) 보강 생성한다.
        private void FinalizePanel(RectTransform rt)
        {
            if (rt != null) StretchFull(rt);
            BindRefsFromHierarchy();
            WireButtonCallbacks();
            if (_graphContainer == null) BuildGraphArea(rt);
        }

        // GraphArea(스크롤·줌 가능한 그래프 영역)를 root 아래에 생성한다.
        // 프리팹에 SidePanel만 있고 GraphArea가 없는 경우(겹침 방지를 위해 지워둔 경우) 보강용으로 호출된다.
        private void BuildGraphArea(RectTransform root)
        {
            float panelW = _sidePanelWidth;

            var graphArea = NewRect(root, "GraphArea");
            graphArea.anchorMin = Vector2.zero;
            graphArea.anchorMax = new Vector2(1f, 1f);
            graphArea.offsetMin = new Vector2(_graphAreaMarginLeft, _graphAreaMarginBottom);
            graphArea.offsetMax = new Vector2(-panelW - _graphAreaMarginRight, -_graphAreaMarginTop);
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

            MakeTMP(content, "지  도", 14f, FontStyles.Bold, 18f, TextAlignmentOptions.Center);

            MakeSep(content);

            MakeTMP(content, "경로 방식", 8f, FontStyles.Normal, 10f).color = Gray;
            var pathRow = NewRow(content, 14f);
            pathRow.name = "PathTypeRow";
            var btnSh = MakeBtn(pathRow, "최단",      () => SetPathType(PathType.Shortest),      id: "BtnShortest");
            var btnBl = MakeBtn(pathRow, "균형",      () => SetPathType(PathType.Balanced),      id: "BtnBalanced");
            var btnMd = MakeBtn(pathRow, "최소난이도", () => SetPathType(PathType.MinDifficulty), id: "BtnMinDiff");
            _btnShortestImg = btnSh.GetComponent<Image>();
            _btnBalancedImg = btnBl.GetComponent<Image>();
            _btnMinDiffImg  = btnMd.GetComponent<Image>();

            MakeSep(content);

            MakeTMP(content, "장착 장비 (클릭)", 8f, FontStyles.Normal, 10f).color = Gray;
            _gearListTMP = MakeTMP(content, "없음", 8f, FontStyles.Normal, 10f, id: "GearListLabel");
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
                var row = NewRow(content, 12f);
                AddGearBtn(row, emotionDefs[i].ec, emotionDefs[i].nm);
                if (i + 1 < emotionDefs.Length)
                    AddGearBtn(row, emotionDefs[i + 1].ec, emotionDefs[i + 1].nm);
            }

            MakeSep(content);

            MakeTMP(content, "경로 정보", 8f, FontStyles.Normal, 10f).color = Gray;
            _pathInfoTMP = MakeTMP(content, "노드 클릭 →\n목적지 선택", 8f, FontStyles.Normal, 44f, id: "PathInfoLabel");
            _pathInfoTMP.color = Color.white;
            _pathInfoTMP.enableWordWrapping = true;

            BuildClueListSection(content);

            MakeSep(content);

            MakeTMP(content, "범례", 8f, FontStyles.Normal, 10f).color = Gray;
            MakeLegendRow(content, "단서 없는 맵",  new Color(0.10f, 0.10f, 0.12f));
            MakeLegendRow(content, "단서 있는 맵",  new Color(0.25f, 0.38f, 0.65f));
            MakeLegendRow(content, "방문한 맵",     new Color(0.50f, 0.72f, 1.00f));
            MakeLegendRow(content, "시작 지점",     new Color(0.28f, 0.78f, 0.38f));
            MakeLegendRow(content, "선택/경로",     new Color(1.00f, 0.82f, 0.08f));
            MakeLegendRow(content, "클리어 연결",   new Color(0.32f, 0.80f, 0.45f));
            MakeLegendRow(content, "필수 장비 부족 (통과불가)", new Color(0.55f, 0.18f, 0.55f));
            MakeLegendRow(content, "최선 경로 (장비 부족)",   new Color(0.75f, 0.20f, 0.20f));

            MakeSep(content);

            var closeBtn = MakeBtn(content, $"닫기 [{_toggleKey}]", Close, h: 16f, id: "BtnClose");
            closeBtn.GetComponent<Image>().color = new Color(0.42f, 0.10f, 0.10f);
        }

        // 획득한 단서 목록 섹션. content(SidePanel의 ScrollRect Content) 끝에 추가된다.
        private void BuildClueListSection(RectTransform content)
        {
            MakeSep(content);
            MakeTMP(content, "단서", 8f, FontStyles.Normal, 10f).color = Gray;

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
            var rt = MakeBtn(row, nm, () => ToggleGear(captured), id: "GearBtn_" + ec.ToString());
            var img = rt.GetComponent<Image>();
            img.color = EmotionColorConfig.GetColor(ec) * 0.45f;
            _gearBtnImages[ec] = img;
        }

        private void MakeLegendRow(RectTransform parent, string text, Color dotColor)
        {
            var row = NewRow(parent, 10f);

            var dot = NewRect(row, "Dot");
            dot.gameObject.AddComponent<LayoutElement>().preferredWidth = 8f;
            AddImg(dot, dotColor);

            var lbl = NewRect(row, "Lbl");
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var tmp = lbl.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text              = text;
            tmp.fontSize          = 8f;
            tmp.color             = new Color(0.85f, 0.85f, 0.85f);
            tmp.alignment         = TextAlignmentOptions.Left;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
        }

        // ─── 프리팹 지원 ─────────────────────────────────────────

        // 프리팹 인스턴스에서 이름 기반으로 핵심 참조를 찾아 연결한다.
        private void BindRefsFromHierarchy()
        {
            _graphViewport  = FindDeepChild<RectTransform>("GraphViewport");
            _graphContainer = FindDeepChild<RectTransform>("GraphContainer");
            _labelContainer = FindDeepChild<RectTransform>("LabelContainer");

            var graphAreaTF = FindDeepTransform(_panelGO.transform, "GraphArea");
            if (graphAreaTF != null) _graphPanZoom = graphAreaTF.GetComponent<GraphPanZoom>();
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

            FindDeepTransform(_panelGO.transform, "BtnClose")
                ?.GetComponent<Button>()?.onClick.AddListener(Close);

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
            float h = 0f, string id = null)
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
            tmp.fontSize          = 10f;
            tmp.alignment         = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color             = Color.white;
            return rt;
        }

        private static Sprite GetCircleSprite() => null;
    }
}
