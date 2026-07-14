using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Note
{
    // 노트 본문 좌측 — 선택된 경로가 지나는 맵들을 실제 노드-간선 그래프로 그린다.
    //
    // 2026-07-14 재작업(사용자 요청): 세로로 쌓이는 텍스트 목록("↓" 문자로 이은 화살표)이 아니라,
    // MapViewer의 그래프 뷰처럼 노드(박스)를 실제 간선(선)으로 잇는 방식으로 변경 — 노드는 마우스로
    // 자유롭게 옮길 수 있고, 옮기면 그 노드에 연결된 간선이 즉시 따라와 다시 이어진다.
    //
    // 2026-07-14 추가(사용자 요청): 맵 옆에 붙는 카드(단서)를 클릭하면 그 카드가 "단서 노드"(맵 노드와
    // 다른 색)로 승격되어 그래프 위에 독립적으로 표시된다 — 다시 클릭하면 카드로 되돌아간다. 단서 노드는
    // 이름/설명/코멘트(ClueData.comments)를 전부 보여주고, 소속 맵과 간선으로 연결되며, 키워드
    // (ClueData.keywords)가 하나라도 겹치는 다른 단서 노드와도 간선으로 연결된다. 도감에서 수동으로 핀한
    // (현재 경로의 어떤 맵과도 연관되지 않는) 단서는 애초에 "카드"로 표시될 자리가 없으므로 처음부터
    // 항상 단서 노드로 표시된다.
    //
    // 노드는 MapViewer._nodeViews와 같은 이유로 "한 번 만들면 파괴하지 않고 Show/Hide만" 하는 영속
    // 딕셔너리(_nodeVisuals/_clueVisuals, guid·clueId 기준)로 관리한다 — 그래야 사용자가 드래그로 옮긴
    // 위치가 노트를 새로고침해도 유지된다. 반면 간선·카드는 그 자체로 상태가 없으므로(매번 다시 계산)
    // 기존처럼 UiRowPool로 재사용한다.
    public class NoteRouteGraphView : MonoBehaviour
    {
        public event Action<NoteEntry> OnDeleteRequested;
        public event Action<NoteEntry> OnAddToPlanRequested; // clue.targetMapGuid를 계획 목적지로 추가해달라는 요청

        private RectTransform _content;
        private RectTransform _graphArea; // 프리폼 노드/간선 전용 하위 컨테이너
        private RectTransform _otherArea; // 빈 안내문 전용 하위 컨테이너
        private Canvas _canvas;
        private TMP_FontAsset _font;
        private readonly List<GameObject> _miscSpawned = new(); // EmptyHint 전용(많아야 1개)

        private PathResult _lastRoute;                    // 단서 노드 클릭(펼침/접힘) 후 자체 재갱신에 사용
        private IReadOnlyList<NoteEntry> _lastEntries;

        [Header("행 템플릿 (선택 — 비워두면 아래 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _nodeBoxTemplate;
        [SerializeField] private GameObject _edgeTemplate;
        [SerializeField] private GameObject _cardTemplate;

        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colNode = new(0.82f, 0.58f, 0.22f);      // 맵 노드 — 주황색
        [SerializeField] private Color _colCard = new(0.30f, 0.45f, 0.62f);      // 접힌 카드 — 파란색
        [SerializeField] private Color _colClueNode = new(0.36f, 0.28f, 0.58f);  // 펼쳐진 단서 노드 — 보라색(맵 노드와 다른 색)
        [SerializeField] private Color _colEdge = new(0.55f, 0.60f, 0.68f);        // 맵-맵 간선
        [SerializeField] private Color _colClueEdge = new(0.45f, 0.55f, 0.75f);    // 맵-단서 간선
        [SerializeField] private Color _colKeywordEdge = new(0.55f, 0.85f, 0.35f); // 단서-단서(키워드 공유) 간선
        [SerializeField] private Color _nodeTextColor = Color.black; // NodeBox 안 맵 이름 글자색
        [SerializeField] private float _nodeBoxWidth = 60f;
        [SerializeField] private float _nodeBoxHeight = 16f; // 노드 박스 높이(= 최소 행 높이)
        [SerializeField] private float _nodeStartX = 80f;    // 노드가 처음 생성될 때의 기본 가로 위치(우측으로 밀고 싶으면 키움)
        [SerializeField] private float _nodeSpacingY = 26f;  // 노드가 처음 생성될 때의 기본 세로 간격
        [SerializeField] private float _edgeThickness = 2f;
        [SerializeField] private float _cardsGapX = 6f;      // NodeBox와 그 옆 카드 목록 사이 간격
        [SerializeField] private float _cardsAreaWidth = 90f;
        [SerializeField] private float _cardHeight = 12f;
        [SerializeField] private float _cardSpacing = 2f;
        [SerializeField] private float _cardFontSize = 7f;
        [SerializeField] private float _clueNodeWidth = 90f;      // 펼쳐진 단서 노드 박스 폭
        [SerializeField] private float _clueNodeOffsetX = 20f;    // 단서 노드가 처음 생성될 때 소속 맵 기준 가로 오프셋
        [SerializeField] private float _clueNodeSpacingY = 30f;   // 단서 노드가 처음 생성될 때의 기본 세로 간격

        // 맵 노드 하나의 시각 상태 — guid로 영속 관리(파괴하지 않음, MapViewer._nodeViews와 동일한 이유).
        private class NodeVisual
        {
            public RectTransform Group;
            public TextMeshProUGUI Label;
            public RectTransform CardsContainer;
            public bool UsedThisPass;
        }

        // 단서 노드 하나의 시각 상태 — clueId로 영속 관리. 카드가 "펼쳐지면" 이 노드가 만들어지고,
        // 다시 접히기 전까지 파괴하지 않는다(드래그 위치 유지 목적, NodeVisual과 동일한 이유).
        private class ClueNodeVisual
        {
            public RectTransform Group;
            public TextMeshProUGUI Text;
            public bool UsedThisPass;
        }

        // NodeBox/단서 노드 드래그 전용 — Group(부모)의 anchoredPosition을 옮기고, 옮길 때마다 콜백으로
        // 알려 간선을 다시 그리게 한다. GraphPanZoom과 동일한 이유로 ScreenPointToLocalPointInRectangle을
        // 써서 카메라/캔버스 렌더모드와 무관하게 정확한 좌표 변환을 보장한다(단순 delta/scaleFactor는
        // 이 프로젝트의 카메라 설정에서 오차가 났던 전례가 있음).
        private class NodeDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
        {
            public RectTransform Group;
            public RectTransform Bounds;   // 좌표 변환 기준 + 드래그 가능 범위(= GraphArea)
            public float RightMargin;      // Bounds 오른쪽에서 남겨둘 여유(이 안으로만 드래그 허용)
            public Action OnMoved;

            private Camera _worldCam;
            private Vector2 _dragStartLocal;
            private Vector2 _dragStartAnchor;

            public void SetCanvas(Canvas canvas)
            {
                _worldCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera : null;
            }

            // PointerDown을 수락해야 BeginDrag가 이 핸들러로 전달된다(GraphPanZoom과 동일한 이유).
            public void OnPointerDown(PointerEventData e) { }

            public void OnBeginDrag(PointerEventData e)
            {
                if (Group == null || Bounds == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(Bounds, e.position, _worldCam, out _dragStartLocal);
                _dragStartAnchor = Group.anchoredPosition;
            }

            public void OnDrag(PointerEventData e)
            {
                if (Group == null || Bounds == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(Bounds, e.position, _worldCam, out Vector2 cur);
                Vector2 pos = _dragStartAnchor + (cur - _dragStartLocal);

                float maxX = Mathf.Max(0f, Bounds.rect.width - RightMargin);
                pos.x = Mathf.Clamp(pos.x, 0f, maxX);
                pos.y = Mathf.Min(pos.y, 0f); // 그래프 영역 위 경계(0) 밖으로는 못 나가게

                Group.anchoredPosition = pos;
                OnMoved?.Invoke();
            }
        }

        // 카드/단서 노드 클릭 전용 — 드래그(NodeDragHandle)와 별개 인터페이스라 같은 오브젝트에 붙여도
        // 서로 간섭하지 않는다(드래그가 발생하면 uGUI가 클릭으로 치지 않는다).
        private class ClickToToggle : MonoBehaviour, IPointerClickHandler
        {
            public Action OnClick;
            public void OnPointerClick(PointerEventData e) => OnClick?.Invoke();
        }

        private readonly Dictionary<string, NodeVisual> _nodeVisuals = new();
        private readonly List<NodeVisual> _orderedVisuals = new(); // 현재 경로 순서대로 — 맵-맵 간선 연결용

        private readonly Dictionary<string, ClueNodeVisual> _clueVisuals = new(); // key: clueId
        private readonly HashSet<string> _expandedClueIds = new(); // 카드에서 노드로 승격된(클릭한) 단서

        // 이번 SetData 패스에 실제로 화면에 보이는 단서 노드 — 간선 계산용(맵-단서, 단서-단서 둘 다).
        // mapGuid는 소속 맵이 있으면 그 guid, 없으면(도감 수동 핀 등) null.
        private readonly Dictionary<string, (ClueNodeVisual visual, string mapGuid)> _visibleClueVisuals = new();

        private UiRowPool _edgePool;
        private UiRowPool _cardPool;

        public void Init(RectTransform scrollContent, TMP_FontAsset font)
        {
            _content = scrollContent;
            _font = font;
            _canvas = scrollContent.GetComponentInParent<Canvas>();

            // NotePanel.BuildScrollingList가 공용으로 붙이는 VerticalLayoutGroup/ContentSizeFitter는
            // RoutePlanEditorView(세로 목록)에는 필요하지만, 이 뷰는 이제 프리폼 배치라 매 프레임 자식
            // 위치를 되돌려버려 오히려 방해가 된다 — 이 뷰에서만 제거한다(공용 헬퍼 자체는 안 건드림).
            var vlg = _content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) Destroy(vlg);
            var csf = _content.GetComponent<ContentSizeFitter>();
            if (csf != null) Destroy(csf);

            // 프리팹을 재사용/재인스턴스화하면, 저장 시점에 이미 만들어져 있던 GraphArea/OtherArea(그리고
            // 그 안의 노드/간선/카드 클론)가 Content에 그대로 남아있을 수 있다 — 이 컴포넌트의 런타임 상태
            // (_graphArea/_otherArea 필드, _nodeVisuals/_clueVisuals, UiRowPool의 내부 풀 목록)는
            // 직렬화되지 않으므로 새 인스턴스는 그 baked-in 자식들의 존재를 전혀 모른 채 또 하나씩 새로
            // 만들어 중복이 생겼다(GraphArea/OtherArea/NewPlanBtn 2개씩 생기는 문제의 원인). Init 시점에
            // Content의 기존 자식을 전부 정리하고 항상 빈 상태에서 다시 짓는다 — 어차피 SetData()가 매번
            // 현재 상태로 다시 채우므로 이전 내용을 보존할 이유가 없다.
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            _nodeVisuals.Clear();
            _orderedVisuals.Clear();
            _clueVisuals.Clear();

            _graphArea = NewRect(_content, "GraphArea");
            _graphArea.anchorMin = new Vector2(0f, 1f);
            _graphArea.anchorMax = new Vector2(1f, 1f);
            _graphArea.pivot     = new Vector2(0.5f, 1f);
            _graphArea.anchoredPosition = Vector2.zero;

            _otherArea = NewRect(_content, "OtherArea");
            _otherArea.anchorMin = new Vector2(0f, 1f);
            _otherArea.anchorMax = new Vector2(1f, 1f);
            _otherArea.pivot     = new Vector2(0.5f, 1f);

            var ovlg = _otherArea.gameObject.AddComponent<VerticalLayoutGroup>();
            ovlg.padding = new RectOffset(0, 0, 4, 4);
            ovlg.spacing = 2f;
            ovlg.childControlWidth     = true;
            ovlg.childControlHeight    = false;
            ovlg.childForceExpandWidth  = true;
            ovlg.childForceExpandHeight = false;

            var ocsf = _otherArea.gameObject.AddComponent<ContentSizeFitter>();
            ocsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _edgePool = new UiRowPool(_edgeTemplate, BuildEdgeTemplate);
            _cardPool = new UiRowPool(_cardTemplate, BuildCardTemplate);
        }

        public void SetData(PathResult route, IReadOnlyList<NoteEntry> entries)
        {
            _lastRoute = route;
            _lastEntries = entries;

            ClearMisc();

            var graph = MapGraph.Instance;
            var matchedClueIds = new HashSet<string>();
            _orderedVisuals.Clear();
            _visibleClueVisuals.Clear();
            foreach (var v in _nodeVisuals.Values) v.UsedThisPass = false;
            foreach (var v in _clueVisuals.Values) v.UsedThisPass = false;

            bool hasRoute = route != null && route.IsValid && graph != null;
            if (hasRoute)
            {
                for (int i = 0; i < route.Nodes.Count; i++)
                {
                    var node = route.Nodes[i];
                    var nodeEntries = entries.Where(e =>
                    {
                        var clue = graph.GetClue(e.clueId);
                        return clue != null && clue.codexMapGuid == node.guid;
                    }).ToList();
                    foreach (var e in nodeEntries) matchedClueIds.Add(e.clueId);

                    var visual = GetOrCreateNodeVisual(node.guid, i);
                    visual.UsedThisPass = true;
                    visual.Group.gameObject.SetActive(true);
                    visual.Label.text  = $"{node.nodeName}\n<size=70%>{(i == 0 ? "출발" : i == route.Nodes.Count - 1 ? "목적지" : "경로")}</size>";
                    visual.Label.color = _nodeTextColor;

                    // 카드에서 노드로 승격된(클릭한) 단서는 그래프 위 독립 노드로, 나머지는 예전처럼
                    // 맵 옆 접힌 카드로 표시한다.
                    int expandedIndex = 0;
                    foreach (var e in nodeEntries)
                    {
                        if (_expandedClueIds.Contains(e.clueId))
                        {
                            var defaultPos = visual.Group.anchoredPosition +
                                new Vector2(_nodeBoxWidth + _clueNodeOffsetX, -expandedIndex * _clueNodeSpacingY);
                            var clueVisual = GetOrCreateClueVisual(e.clueId, defaultPos);
                            clueVisual.UsedThisPass = true;
                            clueVisual.Group.gameObject.SetActive(true);
                            PopulateClueNode(clueVisual, e, canCollapse: true);
                            _visibleClueVisuals[e.clueId] = (clueVisual, node.guid);
                            expandedIndex++;
                        }
                        else
                        {
                            PopulateCard(_cardPool.Get(visual.CardsContainer), e);
                        }
                    }

                    _orderedVisuals.Add(visual);
                }
            }

            foreach (var v in _nodeVisuals.Values)
                if (!v.UsedThisPass) v.Group.gameObject.SetActive(false);

            // 현재 경로의 어떤 맵과도 연관되지 않은 항목(도감에서 수동으로 핀한 단서 등)은 애초에
            // "카드"로 붙을 자리가 없으므로 처음부터 항상 단서 노드로 표시한다.
            var others = entries.Where(e => !matchedClueIds.Contains(e.clueId)).ToList();
            float othersBaseY = -(Mathf.Max(_orderedVisuals.Count, 1) * _nodeSpacingY + _clueNodeSpacingY + 4f);
            for (int i = 0; i < others.Count; i++)
            {
                var e = others[i];
                var defaultPos = new Vector2(_nodeStartX, othersBaseY - i * _clueNodeSpacingY);
                var clueVisual = GetOrCreateClueVisual(e.clueId, defaultPos);
                clueVisual.UsedThisPass = true;
                clueVisual.Group.gameObject.SetActive(true);
                PopulateClueNode(clueVisual, e, canCollapse: false);
                _visibleClueVisuals[e.clueId] = (clueVisual, null);
            }

            foreach (var v in _clueVisuals.Values)
                if (!v.UsedThisPass) v.Group.gameObject.SetActive(false);

            // 노드 옆 카드를 전부 다 쓴 뒤에 한 번만 정리.
            _cardPool.EndPass();

            RelayoutEdges();

            if (!hasRoute && others.Count == 0)
                BuildEmptyHint();

            UpdateContentSize();
        }

        private void ClearMisc()
        {
            // CodexDrawerTreeView.Clear와 동일한 이유로 SetActive(false) 후 Destroy —
            // 활성 상태의 TMP를 그냥 Destroy하면 같은 프레임 ScrollRect 리빌드가 파괴 중인 서브메시
            // 머티리얼에 접근해 MissingReferenceException을 던질 수 있다.
            foreach (var go in _miscSpawned)
            {
                go.SetActive(false);
                Destroy(go);
            }
            _miscSpawned.Clear();
        }

        // ─── 맵 노드 (영속 — guid로 관리, 파괴하지 않음) ─────────────

        private NodeVisual GetOrCreateNodeVisual(string guid, int indexForDefaultLayout)
        {
            if (_nodeVisuals.TryGetValue(guid, out var existing)) return existing;

            var group = NewRect(_graphArea, "NodeGroup_" + guid);
            group.anchorMin = group.anchorMax = new Vector2(0f, 1f);
            group.pivot     = new Vector2(0f, 1f);
            group.anchoredPosition = new Vector2(_nodeStartX, -(indexForDefaultLayout * _nodeSpacingY + 4f));

            var box = _nodeBoxTemplate != null ? Instantiate(_nodeBoxTemplate, group, false) : BuildNodeBoxDefault(group);
            box.name = "NodeBox";
            var boxRT = (RectTransform)box.transform;
            boxRT.anchorMin = boxRT.anchorMax = new Vector2(0f, 1f);
            boxRT.pivot     = new Vector2(0f, 1f);
            boxRT.anchoredPosition = Vector2.zero;
            boxRT.sizeDelta = new Vector2(_nodeBoxWidth, _nodeBoxHeight);

            var label = box.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();

            var drag = box.GetComponent<NodeDragHandle>();
            if (drag == null) drag = box.gameObject.AddComponent<NodeDragHandle>();
            drag.Group       = group;
            drag.Bounds       = _graphArea;
            drag.RightMargin  = _nodeBoxWidth + _cardsGapX + _cardsAreaWidth;
            drag.OnMoved      = HandleNodeMoved;
            drag.SetCanvas(_canvas);

            var cardsContainer = NewRect(group, "Cards");
            cardsContainer.anchorMin = cardsContainer.anchorMax = new Vector2(0f, 1f);
            cardsContainer.pivot     = new Vector2(0f, 1f);
            cardsContainer.anchoredPosition = new Vector2(_nodeBoxWidth + _cardsGapX, 0f);
            cardsContainer.sizeDelta = new Vector2(_cardsAreaWidth, 0f);

            var cvlg = cardsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            cvlg.spacing = _cardSpacing;
            cvlg.childControlWidth     = true;
            cvlg.childControlHeight    = true;
            cvlg.childForceExpandWidth  = true;
            cvlg.childForceExpandHeight = false;

            var ccsf = cardsContainer.gameObject.AddComponent<ContentSizeFitter>();
            ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var visual = new NodeVisual { Group = group, Label = label, CardsContainer = cardsContainer };
            _nodeVisuals[guid] = visual;
            return visual;
        }

        private void HandleNodeMoved()
        {
            RelayoutEdges();
            UpdateContentSize();
        }

        private GameObject BuildNodeBoxDefault(Transform parent)
        {
            var rt = NewRect(parent, "NodeBox");
            AddImg(rt, _colNode);

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize   = _cardFontSize;
            tmp.fontStyle  = FontStyles.Bold;
            tmp.color      = _nodeTextColor;
            tmp.alignment  = TextAlignmentOptions.Midline;

            return rt.gameObject;
        }

        // ─── 단서 노드 (영속 — clueId로 관리, 파괴하지 않음) ─────────

        private ClueNodeVisual GetOrCreateClueVisual(string clueId, Vector2 defaultPos)
        {
            if (_clueVisuals.TryGetValue(clueId, out var existing)) return existing;

            var group = NewRect(_graphArea, "ClueNode_" + clueId);
            group.anchorMin = group.anchorMax = new Vector2(0f, 1f);
            group.pivot     = new Vector2(0f, 1f);
            group.anchoredPosition = defaultPos;
            group.sizeDelta = new Vector2(_clueNodeWidth, 0f);
            AddImg(group, _colClueNode);

            var vlg = group.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 3, 3);
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            var groupCsf = group.gameObject.AddComponent<ContentSizeFitter>();
            groupCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var txtRT = NewRect(group, "Text");
            var txtLe = txtRT.gameObject.AddComponent<LayoutElement>();
            txtLe.flexibleWidth = 1f;
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _cardFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;

            var drag = group.gameObject.AddComponent<NodeDragHandle>();
            drag.Group       = group;
            drag.Bounds      = _graphArea;
            drag.RightMargin = _clueNodeWidth;
            drag.OnMoved     = HandleNodeMoved;
            drag.SetCanvas(_canvas);

            group.gameObject.AddComponent<ClickToToggle>();

            var visual = new ClueNodeVisual { Group = group, Text = tmp };
            _clueVisuals[clueId] = visual;
            return visual;
        }

        // canCollapse: 이 단서가 현재 경로의 어떤 맵과 연관돼(카드로 돌아갈 자리가 있어) 클릭으로 다시
        // 접을 수 있는지 — 도감 수동 핀 등 소속 맵이 없는 것은 접을 곳이 없으므로 false로 호출된다.
        private void PopulateClueNode(ClueNodeVisual visual, NoteEntry entry, bool canCollapse)
        {
            var clue = MapGraph.Instance?.GetClue(entry.clueId);

            var sb = new StringBuilder();
            sb.Append($"<b>{(clue != null ? clue.name : entry.clueId)}</b> <size=70%>[{ReasonLabel(entry.reason)}]</size>");
            if (clue != null && !string.IsNullOrEmpty(clue.description))
                sb.Append($"\n{clue.description}");
            if (clue != null)
            {
                foreach (var c in clue.comments)
                {
                    if (string.IsNullOrEmpty(c.text)) continue;
                    sb.Append($"\n<size=80%><i>{c.author}: {c.text}</i></size>");
                }
            }
            visual.Text.text = sb.ToString();

            var toggle = visual.Group.GetComponent<ClickToToggle>();
            if (toggle != null)
            {
                toggle.OnClick = canCollapse
                    ? () => { _expandedClueIds.Remove(entry.clueId); SetData(_lastRoute, _lastEntries); }
                    : null;
            }
        }

        private static bool SharesKeyword(ClueData a, ClueData b)
        {
            if (a?.keywords == null || b?.keywords == null) return false;
            foreach (var k1 in a.keywords)
            {
                if (string.IsNullOrWhiteSpace(k1)) continue;
                foreach (var k2 in b.keywords)
                {
                    if (string.Equals(k1.Trim(), k2.Trim(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        // ─── 간선 (상태 없음 — 매번 현재 위치로 다시 계산, 풀링) ─────
        //   1) 맵-맵 체인  2) 맵-단서 노드  3) 단서-단서(키워드 공유)

        private void RelayoutEdges()
        {
            for (int i = 0; i < _orderedVisuals.Count - 1; i++)
            {
                var rt = PrepEdge(_colEdge);
                Vector2 a = NodeAnchor(_orderedVisuals[i].Group, _nodeBoxWidth, _nodeBoxHeight);
                Vector2 b = NodeAnchor(_orderedVisuals[i + 1].Group, _nodeBoxWidth, _nodeBoxHeight);
                SetEdgeLayout(rt, a, b);
            }

            foreach (var kv in _visibleClueVisuals)
            {
                var (clueVisual, mapGuid) = kv.Value;
                if (mapGuid == null || !_nodeVisuals.TryGetValue(mapGuid, out var mapVisual)) continue;

                var rt = PrepEdge(_colClueEdge);
                Vector2 a = NodeAnchor(mapVisual.Group, _nodeBoxWidth, _nodeBoxHeight);
                Vector2 b = ClueNodeAnchor(clueVisual.Group);
                SetEdgeLayout(rt, a, b);
            }

            var visibleIds = _visibleClueVisuals.Keys.ToList();
            var graph = MapGraph.Instance;
            for (int i = 0; i < visibleIds.Count; i++)
            {
                var clueA = graph?.GetClue(visibleIds[i]);
                for (int j = i + 1; j < visibleIds.Count; j++)
                {
                    var clueB = graph?.GetClue(visibleIds[j]);
                    if (!SharesKeyword(clueA, clueB)) continue;

                    var rt = PrepEdge(_colKeywordEdge);
                    var groupA = _visibleClueVisuals[visibleIds[i]].visual.Group;
                    var groupB = _visibleClueVisuals[visibleIds[j]].visual.Group;
                    Vector2 a = ClueNodeAnchor(groupA);
                    Vector2 b = ClueNodeAnchor(groupB);
                    SetEdgeLayout(rt, a, b);
                }
            }

            _edgePool.EndPass();

            // UiRowPool.Get()이 매번 SetAsLastSibling()으로 간선을 형제 목록 맨 뒤로 보내(= 나중에 그려짐)
            // 노드보다 위에 그려지고 있었다 — 노드를 다시 맨 뒤로 보내 항상 간선 위에 그려지게 한다.
            foreach (var v in _orderedVisuals)
                v.Group.SetAsLastSibling();
            foreach (var kv in _visibleClueVisuals)
                kv.Value.visual.Group.SetAsLastSibling();
        }

        // 풀에서 간선 하나를 받아 앵커/피벗과 색을 지정해 반환 — 위치·회전·길이는 SetEdgeLayout이 따로 맡는다.
        private RectTransform PrepEdge(Color color)
        {
            var edgeGO = _edgePool.Get(_graphArea);
            var rt = (RectTransform)edgeGO.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);

            var img = edgeGO.GetComponent<Image>();
            if (img != null) img.color = color;

            return rt;
        }

        // 맵 노드는 고정 크기(_nodeBoxWidth/_nodeBoxHeight)라 레이아웃 재계산이 필요 없다.
        private Vector2 NodeAnchor(RectTransform group, float width, float height) =>
            group.anchoredPosition + new Vector2(width * 0.5f, -height * 0.5f);

        // 단서 노드는 ContentSizeFitter로 내용에 따라 높이가 바뀌므로, rect.height를 읽기 "전"에
        // 레이아웃을 먼저 확정해야 한다 — 인자 평가 순서상 호출부에서 미리 rect.height를 읽어
        // 넘기면 이 메서드 안의 리빌드보다 먼저 평가돼 갱신 전 값을 읽는 버그가 생기므로, 반드시
        // 이 메서드 안에서 리빌드 → 읽기 순서로 처리한다.
        private Vector2 ClueNodeAnchor(RectTransform group)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(group);
            return group.anchoredPosition + new Vector2(_clueNodeWidth * 0.5f, -group.rect.height * 0.5f);
        }

        private void SetEdgeLayout(RectTransform rt, Vector2 from, Vector2 to)
        {
            Vector2 dir = to - from;
            Vector2 mid = from + dir * 0.5f;
            rt.anchoredPosition = mid;
            rt.sizeDelta        = new Vector2(Mathf.Max(dir.magnitude, 0.01f), _edgeThickness);
            rt.localRotation    = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        private GameObject BuildEdgeTemplate()
        {
            var rt = NewRect(null, "Edge");
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            AddImg(rt, _colEdge);
            rt.sizeDelta = new Vector2(10f, _edgeThickness);

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ─── 콘텐츠 크기 — 그래프(프리폼) 높이 + 빈 안내문 높이를 합쳐 스크롤 영역에 반영 ─

        private void UpdateContentSize()
        {
            float graphHeight = ComputeGraphHeight();
            _graphArea.sizeDelta = new Vector2(_graphArea.sizeDelta.x, graphHeight);
            _otherArea.anchoredPosition = new Vector2(0f, -graphHeight);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_otherArea);
            float otherHeight = _otherArea.rect.height;

            _content.sizeDelta = new Vector2(_content.sizeDelta.x, graphHeight + otherHeight);
        }

        private float ComputeGraphHeight()
        {
            float maxBottom = 0f;
            foreach (var v in _orderedVisuals)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(v.CardsContainer);
                float cardsH = v.CardsContainer.rect.height;
                float bottom = -v.Group.anchoredPosition.y + Mathf.Max(_nodeBoxHeight, cardsH);
                maxBottom = Mathf.Max(maxBottom, bottom);
            }
            foreach (var kv in _visibleClueVisuals)
            {
                var group = kv.Value.visual.Group;
                LayoutRebuilder.ForceRebuildLayoutImmediate(group);
                float bottom = -group.anchoredPosition.y + group.rect.height;
                maxBottom = Mathf.Max(maxBottom, bottom);
            }
            return maxBottom + 8f;
        }

        // ─── 빈 안내문 (많아야 1개 — 그냥 매번 새로 그림) ────────────

        private void BuildEmptyHint()
        {
            var hintRT = NewRect(_otherArea, "EmptyHint");
            var le = hintRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
            le.flexibleWidth = 1f;
            var tmp = MakeLabel(hintRT, "Text", "노트가 비어 있습니다.\n경로를 선택하면 연관된 단서가 자동으로 표시됩니다.", 7f,
                new Color(0.7f, 0.7f, 0.7f));
            tmp.alignment = TextAlignmentOptions.Center;
            _miscSpawned.Add(hintRT.gameObject);
        }

        // ─── 카드 (풀링) — 맵 노드 옆에 접혀서 표시. 클릭하면 단서 노드로 승격된다 ─

        private void PopulateCard(GameObject cardGO, NoteEntry entry)
        {
            var clue = MapGraph.Instance?.GetClue(entry.clueId);
            string title = clue != null ? clue.name : entry.clueId;

            var tmp = cardGO.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = $"{title}  [{ReasonLabel(entry.reason)}]";
                tmp.color = Color.white;
            }

            // 목적지 성격을 가진(targetMapGuid가 있는) 단서에만 계획 편입 버튼을 보여준다
            // (NoteSystem_기획서.md "핵심 기능" — "targetMapGuid가 있는 것을 골라 계획에 추가").
            bool canAddToPlan = clue != null && !string.IsNullOrEmpty(clue.targetMapGuid);
            var planBtnTF = cardGO.transform.Find("BtnPlan");
            if (planBtnTF != null)
            {
                planBtnTF.gameObject.SetActive(canAddToPlan);
                var planBtn = planBtnTF.GetComponent<Button>();
                if (planBtn != null)
                {
                    planBtn.onClick.RemoveAllListeners();
                    planBtn.onClick.AddListener(() => OnAddToPlanRequested?.Invoke(entry));
                }
            }

            var delBtn = cardGO.transform.Find("BtnDelete")?.GetComponent<Button>();
            if (delBtn != null)
            {
                delBtn.onClick.RemoveAllListeners();
                delBtn.onClick.AddListener(() => OnDeleteRequested?.Invoke(entry));
            }

            // 카드 자신(빈 배경/텍스트 부분) 클릭 시 단서 노드로 승격 — 버튼 영역은 Button이 직접
            // IPointerClickHandler를 구현하므로 이 핸들러와 간섭하지 않는다.
            var toggle = cardGO.GetComponent<ClickToToggle>();
            if (toggle == null) toggle = cardGO.AddComponent<ClickToToggle>();
            toggle.OnClick = () => { _expandedClueIds.Add(entry.clueId); SetData(_lastRoute, _lastEntries); };
        }

        private GameObject BuildCardTemplate()
        {
            var cardRT = NewRect(null, "Card");
            var le = cardRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = _cardHeight;
            le.flexibleWidth = 1f;
            AddImg(cardRT, _colCard);

            var hlg = cardRT.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(3, 3, 1, 1);
            hlg.spacing = 3f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var textRT = NewRect(cardRT, "Text");
            var textLe = textRT.gameObject.AddComponent<LayoutElement>();
            textLe.flexibleWidth = 1f;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _cardFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            var planBtnRT = NewRect(cardRT, "BtnPlan");
            var planLe = planBtnRT.gameObject.AddComponent<LayoutElement>();
            planLe.preferredWidth = 24f;
            planLe.flexibleWidth = 0f;
            var planImg = AddImg(planBtnRT, new Color(0.15f, 0.32f, 0.19f));
            var planBtn = planBtnRT.gameObject.AddComponent<Button>();
            planBtn.targetGraphic = planImg;
            planBtn.transition = Selectable.Transition.None;
            var planTxtRT = NewRect(planBtnRT, "Text");
            StretchFull(planTxtRT);
            var planTmp = planTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) planTmp.font = _font;
            planTmp.text = "계획";
            planTmp.fontSize = _cardFontSize;
            planTmp.alignment = TextAlignmentOptions.Center;
            planTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            planTmp.color = Color.white;

            var delBtnRT = NewRect(cardRT, "BtnDelete");
            var delLe = delBtnRT.gameObject.AddComponent<LayoutElement>();
            delLe.preferredWidth = 24f;
            delLe.flexibleWidth = 0f;
            var delImg = AddImg(delBtnRT, new Color(0.42f, 0.10f, 0.10f));
            var delBtn = delBtnRT.gameObject.AddComponent<Button>();
            delBtn.targetGraphic = delImg;
            delBtn.transition = Selectable.Transition.None;
            var delTxtRT = NewRect(delBtnRT, "Text");
            StretchFull(delTxtRT);
            var delTmp = delTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) delTmp.font = _font;
            delTmp.text = "삭제";
            delTmp.fontSize = _cardFontSize;
            delTmp.alignment = TextAlignmentOptions.Center;
            delTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            delTmp.color = Color.white;

            cardRT.gameObject.SetActive(false);
            return cardRT.gameObject;
        }

        private static string ReasonLabel(NotePinReason reason) => reason switch
        {
            NotePinReason.RouteLinked => "경로연동",
            NotePinReason.ManualPin => "수동핀",
            _ => "",
        };

        private TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float fontSize, Color color)
        {
            var rt = NewRect(parent, name);
            StretchFull(rt);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            return tmp;
        }

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
    }
}
