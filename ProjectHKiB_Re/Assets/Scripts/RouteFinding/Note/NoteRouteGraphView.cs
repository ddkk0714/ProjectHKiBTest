using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using RouteFinding.MapView;

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
    //
    // [2026-08-15] 이 컴포넌트는 GraphPanZoom과 같은 GameObject(GraphScroll)에 붙는다 — 그래서 빈 배경
    // 위의 포인터 이벤트를 그쪽과 함께 받을 수 있고, 별도 컴포넌트를 새로 추가하지 않고도(= 이미 만들어둔
    // NotePanel 프리팹을 재사용하는 경로에서 컴포넌트가 없어 조용히 죽는 일 없이) 마퀴 선택을 여기서
    // 직접 구현한다. 좌드래그 = 마퀴 선택, 가운데/오른쪽 드래그(또는 Alt+좌드래그) = 기존 배경 팬.
    public class NoteRouteGraphView : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        // [신설, 2026-07-21] SaveModule이 F5/F9 일반 세이브에서 그래프 위치/펼침 상태를 저장·복원하려면
        // 이 인스턴스에 접근해야 하는데, NoteModule과 달리 씬 오브젝트라 자동 생성 싱글턴으로 만들 순
        // 없다(NoteModule.Instance처럼 없으면 새로 만드는 방식이 아니라, NotePanel이 항상 활성 상태라서
        // Init()이 실행되는 시점에 이미 존재하는 인스턴스를 그냥 참조만 하는 방식). 여러 인스턴스가
        // 동시에 존재할 일은 없다(패널당 하나).
        public static NoteRouteGraphView Instance { get; private set; }

        public event Action<NoteEntry> OnDeleteRequested;

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
        [SerializeField] private GameObject _commentTemplate;
        [SerializeField] private GameObject _attachmentTemplate;

        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colNode = new(0.82f, 0.58f, 0.22f);      // 맵 노드 — 주황색
        [SerializeField] private Color _colCard = new(0.30f, 0.45f, 0.62f);      // 접힌 카드 — 파란색
        [SerializeField] private Color _colClueNode = new(0.36f, 0.28f, 0.58f);  // 펼쳐진 단서 노드 — 보라색(맵 노드와 다른 색)
        [SerializeField] private Color _colEdge = new(0.55f, 0.60f, 0.68f);        // 맵-맵 간선
        [SerializeField] private Color _colClueEdge = new(0.45f, 0.55f, 0.75f);    // 맵-단서 간선
        [SerializeField] private Color _colKeywordEdge = new(0.55f, 0.85f, 0.35f); // 단서-단서(키워드 공유) 간선
        [SerializeField] private Color _colManualLinkEdge = new(0.90f, 0.55f, 0.20f); // [신설] 단서 연동 모드로 사용자가 직접 이은 간선 — 주황
        [SerializeField] private Color _colLinkModeSelected = new(0.95f, 0.90f, 0.25f); // [신설] 단서 연동 모드에서 첫 선택된 단서 강조색 — 노랑
        [SerializeField] private Color _nodeTextColor = Color.black; // NodeBox 안 맵 이름 글자색
        [SerializeField] private float _nodeBoxWidth = 60f;
        [SerializeField] private float _nodeBoxHeight = 16f; // 노드 박스 높이(= 최소 행 높이)
        [SerializeField] private float _nodeStartX = 80f;    // 노드가 처음 생성될 때의 기본 가로 위치의 최소값(가운데 정렬 결과가 이보다 왼쪽이면 이 값을 쓴다)
        // [요청, 2026-08-16] 정가운데 정렬만으로는 여전히 화면 왼쪽에 붙어 보인다 — 맵 노드 박스 옆의
        // 카드 영역(_cardsAreaWidth)까지 한 덩어리로 잡아 가운데를 맞추다 보니 박스 자체는 그만큼
        // 왼쪽으로 치우치기 때문. 그 보정 겸, 노드가 생기는 자리를 이 값만큼 오른쪽으로 민다.
        [SerializeField] private float _routeOriginOffsetX = 100f;
        [SerializeField] private float _nodeSpacingY = 26f;  // 노드가 처음 생성될 때의 기본 세로 간격
        [SerializeField] private float _edgeThickness = 2f;
        // [신설] 맵-맵 체인 간선에만 진행 방향 화살촉을 그린다(맵-단서/단서-단서 간선은 방향 개념이
        // 없어 대상 아님) — 기존 간선(회전된 사각형 라인)에 짧은 사각형 두 개를 "＞" 모양으로 겹쳐
        // 중점에 배치하는 방식, 별도 스프라이트 없이 기존 라인 그리기 방식을 그대로 재사용한다.
        [SerializeField] private float _arrowSize = 5f;      // 화살촉 프롱(prong) 길이
        [SerializeField] private float _arrowSpreadDeg = 25f; // 진행 방향 기준 화살촉이 벌어지는 각도
        [SerializeField] private float _cardsGapX = 6f;      // NodeBox와 그 옆 카드 목록 사이 간격
        [SerializeField] private float _cardsAreaWidth = 90f;
        [SerializeField] private float _cardHeight = 12f;
        [SerializeField] private float _cardSpacing = 2f;
        [SerializeField] private float _cardFontSize = 7f;
        [SerializeField] private float _clueNodeWidth = 90f;      // 펼쳐진 단서 노드 박스 폭
        [SerializeField] private float _clueNodeOffsetX = 20f;    // 단서 노드가 처음 생성될 때 소속 맵 기준 가로 오프셋
        [SerializeField] private float _clueNodeSpacingY = 30f;   // 단서 노드가 처음 생성될 때의 기본 세로 간격

        // [신설] 단서에 달린 코멘트(ClueData.comments) — 기존엔 단서 노드 본문 텍스트 안에 줄로
        // 이어붙였으나, 요청으로 단서 노드 우측 아래에 붙는 별개의 작은 노드(들)로 분리했다.
        [SerializeField] private Color _colCommentNode = new(0.24f, 0.20f, 0.34f); // 단서 노드보다 어두운 보라
        [SerializeField] private float _commentFontSize = 6f;
        [SerializeField] private float _commentAreaWidth = 70f;
        [SerializeField] private float _commentGapX = 6f;  // 단서 노드 우측 모서리로부터의 가로 간격
        [SerializeField] private float _commentGapY = 6f;  // 단서 노드 하단 모서리로부터의 세로 간격
        [SerializeField] private float _commentSpacing = 2f; // 코멘트가 여러 개일 때 세로 간격

        // [신설, 2026-08-11] 첨부물(사진/소리/맵) 노드 — 코멘트 노드가 단서 노드 우측 아래에 붙는 것과
        // 대칭으로 좌측 아래에 붙는다(서로 겹치지 않게 반대편).
        [Header("첨부물 노드 (단서 노드 좌측 아래)")]
        [SerializeField] private Color _colAttachmentNode = new(0.20f, 0.28f, 0.34f); // 코멘트(보라)와 구분되는 청록 계열
        [SerializeField] private float _attachmentFontSize = 6f;
        [SerializeField] private float _attachmentAreaWidth = 70f;
        [SerializeField] private float _attachmentGapX = 6f;
        [SerializeField] private float _attachmentGapY = 6f;
        [SerializeField] private float _attachmentSpacing = 2f;
        [SerializeField] private float _attachmentPreviewHeight = 40f; // 사진 첨부의 미리보기 높이

        // [신설, 2026-08-15] 다중 선택 — 빈 배경을 드래그(마퀴)하거나 Ctrl+클릭으로 여러 노드를 고르고,
        // 그중 하나를 끌면 고른 것 전부가 같은 델타로 함께 움직인다(NoteNodeDragHandle 참고).
        [Header("다중 선택")]
        [SerializeField] private Color _colSelection = new(1f, 0.85f, 0.25f);      // 선택된 노드 테두리
        [SerializeField] private Color _colMarquee = new(0.35f, 0.65f, 1f, 0.22f); // 드래그 중 그려지는 선택 사각형
        [SerializeField] private float _selectionOutlineWidth = 2f;

        // 맵 노드 하나의 시각 상태 — guid로 영속 관리(파괴하지 않음, MapViewer._nodeViews와 동일한 이유).
        private class NodeVisual
        {
            public RectTransform Group;
            public TextMeshProUGUI Label;
            public RectTransform CardsContainer;
            public Image Background; // [신설] 단서 연동 모드에서 선택 강조색을 칠하는 데 사용
            public bool UsedThisPass;
        }

        // 단서 노드 하나의 시각 상태 — clueId로 영속 관리. 카드가 "펼쳐지면" 이 노드가 만들어지고,
        // 다시 접히기 전까지 파괴하지 않는다(드래그 위치 유지 목적, NodeVisual과 동일한 이유).
        private class ClueNodeVisual
        {
            public RectTransform Group;
            public TextMeshProUGUI Text;
            public Button DeleteButton; // [신설] 소속 맵 없이(수동 핀 등) 떠 있는 노드도 여기서 바로 삭제 가능
            public Image Background;    // [신설] 키워드별 색상 구분에 사용
            public RectTransform CommentsContainer; // [신설] 우측 아래에 붙는 코멘트 노드들의 컨테이너
            public RectTransform AttachmentsContainer; // [신설] 좌측 아래에 붙는 첨부물 노드들의 컨테이너
            public bool UsedThisPass;
        }

        private readonly Dictionary<string, NodeVisual> _nodeVisuals = new();
        private readonly List<NodeVisual> _orderedVisuals = new(); // 현재 경로 순서대로 — 맵-맵 간선 연결용

        private readonly Dictionary<string, ClueNodeVisual> _clueVisuals = new(); // key: clueId
        private readonly HashSet<string> _expandedClueIds = new(); // 카드에서 노드로 승격된(클릭한) 단서

        // [신설] 소속 맵이 없어(수동 핀 등) 카드로 돌아갈 자리가 없는 단서 노드의 "접힘" 상태 — 경로연동
        // 단서가 카드↔노드를 토글하는 것과 같은 느낌을 주기 위해, 이쪽은 "간략히(제목만) ↔ 전체 보기"를
        // 토글한다(canCollapse=false 노드 전용, _expandedClueIds와는 별개 개념).
        private readonly HashSet<string> _collapsedStandaloneClueIds = new();

        // 단서 서랍(ClueDrawerView)에서 드래그해 놓은 위치 — PlaceClueAt이 먼저 여기 기록해두면,
        // NoteModule.AddManualPin이 동기적으로 발행하는 OnNoteChanged → SetData → GetOrCreateClueVisual이
        // 이 값을 기본 위치로 소비한다(한 번 쓰고 지워짐). 클릭 순서 문제로 처리 안 된 항목이 남아도
        // 다음에 그 clueId의 노드가 실제로 새로 만들어질 때 한 번만 소비되므로 무해하다.
        private readonly Dictionary<string, Vector2> _pendingCluePositions = new();

        // 이번 SetData 패스에 실제로 화면에 보이는 단서 노드 — 간선 계산용(맵-단서, 단서-단서 둘 다).
        // mapGuid는 소속 맵이 있으면 그 guid, 없으면(도감 수동 핀 등) null.
        private readonly Dictionary<string, (ClueNodeVisual visual, string mapGuid)> _visibleClueVisuals = new();

        // [신설, 2026-08-15] 다중 선택/그룹 이동 대상 — 맵 노드와 단서 노드는 만들어지는 경로도 크기
        // 규칙도 다르지만(고정 크기 vs ContentSizeFitter), 선택과 이동 입장에서는 "Group 하나 + 클램프에
        // 필요한 여백"일 뿐이라 여기서 한 자료구조로 합쳐 다룬다. Group(RectTransform)을 키로 쓰는 이유는
        // 드래그 핸들이 들고 있는 것도, 마퀴가 위치·크기를 읽는 것도 결국 그 Group이기 때문.
        private class SelectableNode
        {
            public RectTransform Group;
            public Outline Outline;    // 선택 강조 테두리 — 꺼둔 채로 만들어 두고 선택 시에만 켠다
            public float Width;        // 마퀴 교차 판정용 폭
            public float FixedHeight;  // 0이면 내용에 따라 높이가 변하는 노드(= Group.rect.height를 읽는다)
            public float RightMargin;  // 드래그 클램프 시 오른쪽에 남겨야 하는 폭(카드 영역까지 포함)
        }

        private readonly Dictionary<RectTransform, SelectableNode> _selectables = new();
        private readonly HashSet<RectTransform> _selected = new();

        private RectTransform _marqueeRT;
        private bool _marqueeActive;
        private Vector2 _marqueeStart;

        // 맨 처음 한 번은 경로가 없어도 작업 공간 가운데로 시점을 맞춘다 — SetData 참고.
        private bool _viewCentered;

        private UiRowPool _edgePool;
        private UiRowPool _arrowPool;
        private UiRowPool _cardPool;
        private UiRowPool _commentPool;
        private UiRowPool _attachmentPool;

        // 첨부 소리 재생 — 도감 카드와 같은 헬퍼를 공유한다(ClueAttachmentAudioPlayer 참고).
        private ClueAttachmentAudioPlayer _audio;

        // [요청, 2026-07-21] 노드를 드래그하는 동안 배경 팬(GraphPanZoom)이 동시에 움직이지 않도록 —
        // NotePanel.BuildGraphPanZoom이 이 컴포넌트와 GraphPanZoom을 같은 GameObject(GraphScroll)에
        // 붙이므로 GetComponent로 바로 찾을 수 있다. 없으면(팬·줌 없이 쓰는 경우) 그냥 null로 무시된다.
        private GraphPanZoom _panZoom;

        public void Init(RectTransform scrollContent, TMP_FontAsset font)
        {
            Instance = this;
            _content = scrollContent;
            _font = font;
            _canvas = scrollContent.GetComponentInParent<Canvas>();
            _panZoom = GetComponent<GraphPanZoom>();

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
            // 위에서 자식을 전부 파괴했으므로 선택/마퀴가 들고 있던 RectTransform 참조도 같이 버린다.
            _selectables.Clear();
            _selected.Clear();
            _marqueeRT = null;
            _marqueeActive = false;
            _viewCentered = false;

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
            _arrowPool = new UiRowPool(null, BuildArrowProngTemplate);
            _cardPool = new UiRowPool(_cardTemplate, BuildCardTemplate);
            _commentPool = new UiRowPool(_commentTemplate, BuildCommentTemplate);
            _attachmentPool = new UiRowPool(_attachmentTemplate, BuildAttachmentTemplate);
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
            int routeNodeCount = hasRoute ? route.Nodes.Count : 0;

            // [요청, 2026-08-15] 경로 체인의 기본 배치 원점 — 예전엔 무조건 좌상단(_nodeStartX, -4)이라
            // 넓어진 작업 공간의 한쪽 구석에 처박혔다. 이제 공간(_content = GraphContainer, 고정 크기)의
            // 정가운데에 체인이 오도록 원점을 계산한다. 이미 만들어진 노드는 사용자가 옮겨둔 위치를
            // 그대로 쓰므로(GetOrCreateNodeVisual은 기존 것을 찾으면 위치를 안 건드린다) 영향이 없다.
            Vector2 chainOrigin = ComputeRouteOrigin(routeNodeCount);
            int createdMapNodes = 0;

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

                    if (!_nodeVisuals.ContainsKey(node.guid)) createdMapNodes++;
                    var visual = GetOrCreateNodeVisual(node.guid, chainOrigin + new Vector2(0f, -i * _nodeSpacingY));
                    visual.UsedThisPass = true;
                    visual.Group.gameObject.SetActive(true);
                    visual.Label.text  = $"{node.nodeName}\n<size=70%>{(i == 0 ? "출발" : i == route.Nodes.Count - 1 ? "목적지" : "경로")}</size>";
                    visual.Label.color = _nodeTextColor;
                    // [신설, 2026-07-21] 단서 연동 모드에서 맵 노드도 연동 대상이 될 수 있어, 첫 번째로
                    // 선택해둔 것이 이 맵 노드라면 단서 노드와 동일한 강조색으로 표시한다.
                    if (visual.Background != null)
                        visual.Background.color = _linkModeSelectedId == node.guid ? _colLinkModeSelected : _colNode;

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
            // 체인 아래쪽에 이어 붙인다 — 체인 원점이 가운데로 옮겨졌으므로 그 기준도 같이 따라간다.
            float othersBaseY = chainOrigin.y - (Mathf.Max(_orderedVisuals.Count, 1) * _nodeSpacingY + _clueNodeSpacingY);
            // 세로로만 쌓으면 개수가 늘었을 때 작업 공간 아래로 넘어가 손이 닿지 않는 자리에 생긴다
            // (드래그·팬 둘 다 공간 안으로 제한돼 있다) — 바닥에 닿으면 오른쪽에 새 열로 이어 쌓는다.
            float spaceH = _content != null ? _content.rect.height : 0f;
            int perColumn = Mathf.Max(1, Mathf.FloorToInt((spaceH + othersBaseY) / _clueNodeSpacingY));
            for (int i = 0; i < others.Count; i++)
            {
                var e = others[i];
                var defaultPos = new Vector2(
                    chainOrigin.x + (i / perColumn) * (_clueNodeWidth + 12f),
                    othersBaseY - (i % perColumn) * _clueNodeSpacingY);
                var clueVisual = GetOrCreateClueVisual(e.clueId, defaultPos);
                clueVisual.UsedThisPass = true;
                clueVisual.Group.gameObject.SetActive(true);
                PopulateClueNode(clueVisual, e, canCollapse: false, isCompact: _collapsedStandaloneClueIds.Contains(e.clueId));
                _visibleClueVisuals[e.clueId] = (clueVisual, null);
            }

            foreach (var v in _clueVisuals.Values)
                if (!v.UsedThisPass) v.Group.gameObject.SetActive(false);

            // 노드 옆 카드를 전부 다 쓴 뒤에 한 번만 정리.
            _cardPool.EndPass();
            // 여러 단서 노드의 CommentsContainer에 나눠 붙인 코멘트 노드도 전부 다 쓴 뒤 한 번만 정리
            // (카드 풀과 동일한 이유 — 여러 컨테이너가 하나의 전역 풀을 공유).
            _commentPool.EndPass();
            _attachmentPool.EndPass(); // 첨부물 노드도 같은 이유로 전역 풀 하나를 여러 컨테이너가 공유

            // 이번 패스에서 꺼진(카드로 접힌 등) 노드는 선택에서도 빼준다 — 안 그러면 보이지도 않는 노드가
            // 그룹 이동에 딸려 다닌다.
            PruneSelection();

            RelayoutEdges();

            if (!hasRoute && others.Count == 0)
                BuildEmptyHint();

            UpdateContentSize();

            // [요청, 2026-08-15] 경로를 새로 반영한 직후에는 그 체인이 화면 정가운데 보이게 시점을 옮긴다.
            // "체인 전체가 이번에 처음 만들어졌을 때"로만 제한하는 이유 — 경로 일부만 바뀌어 노드 하나가
            // 새로 생기는 경우까지 시점을 끌어당기면, 사용자가 맞춰둔 팬·줌이 예고 없이 튄다.
            // 맨 처음 한 번(_viewCentered)은 경로가 없어도 맞춘다 — 작업 공간이 뷰포트보다 훨씬 커서
            // 팬 위치 기본값(좌하단 구석)으로 두면 가운데 놓인 노드가 화면 밖에 있게 된다.
            bool freshChain = createdMapNodes > 0 && createdMapNodes == routeNodeCount;
            if (freshChain || !_viewCentered)
                _viewCentered |= FocusOnRoute(chainOrigin, routeNodeCount);
        }

        // 경로 체인이 작업 공간(_content = GraphContainer, 고정 크기) 정가운데 오도록 첫 노드의 좌상단
        // 좌표를 계산한다. 노드 좌표계는 _graphArea 좌상단 기준(y는 아래로 갈수록 음수)이고, _graphArea는
        // _content의 위쪽 모서리에 앵커돼 있으므로 여기서 나온 y를 그대로 쓰면 된다.
        private Vector2 ComputeRouteOrigin(int nodeCount)
        {
            float spaceW = _content != null ? _content.rect.width : 0f;
            float spaceH = _content != null ? _content.rect.height : 0f;
            float blockW = RouteBlockWidth;
            float chainH = Mathf.Max(0, nodeCount - 1) * _nodeSpacingY + _nodeBoxHeight;

            float x = Mathf.Max(_nodeStartX, (spaceW - blockW) * 0.5f) + _routeOriginOffsetX;
            float y = -Mathf.Max(4f, (spaceH - chainH) * 0.5f);
            return new Vector2(x, y);
        }

        // 맵 노드 박스 + 그 옆에 붙는 카드 목록까지가 체인 한 줄의 실제 가로 폭이다(가운데 정렬 기준).
        private float RouteBlockWidth => _nodeBoxWidth + _cardsGapX + _cardsAreaWidth;

        // 실제로 시점을 옮겼으면 true — GraphPanZoom.Init(NotePanel.Awake)이 아직 안 끝났으면 조용히
        // 아무 일도 안 하므로, 호출부가 "다음 기회에 다시 시도"할 수 있도록 결과를 돌려준다.
        private bool FocusOnRoute(Vector2 origin, int nodeCount)
        {
            if (_panZoom == null || _content == null) return false;

            float chainH = Mathf.Max(0, nodeCount - 1) * _nodeSpacingY + _nodeBoxHeight;
            // 시점은 _routeOriginOffsetX를 빼고 잡는다 — 시점까지 같이 밀면 노드가 화면에서는 제자리라
            // "오른쪽으로 옮겼다"가 눈에 보이지 않는다. 시점은 공간 정가운데에 두고 노드만 민다.
            var center = new Vector2(
                origin.x - _routeOriginOffsetX + RouteBlockWidth * 0.5f,
                origin.y - chainH * 0.5f);
            // GraphPanZoom.FocusOn은 _content 좌하단(pivot=(0,0)) 기준 좌표를 받는데, 노드 좌표는
            // 좌상단 기준(y ≤ 0)이라 세로만 변환해준다.
            _panZoom.FocusOn(new Vector2(center.x, _content.rect.height + center.y));
            return true;
        }

        // 단서 서랍(ClueDrawerView)에서 드래그해 놓았을 때 NotePanel이 호출한다. screenPosition은
        // 드래그가 끝난 지점의 스크린 좌표(PointerEventData.position) — 그래프 영역 밖이면 조용히
        // 무시한다(드롭 취소와 동일하게 처리).
        public void PlaceClueAt(string clueId, Vector2 screenPosition)
        {
            if (string.IsNullOrEmpty(clueId) || _graphArea == null || _content == null) return;

            var cam = GetEventCamera();
            // [버그 수정, 2026-07-21] 판정 기준을 _graphArea에서 _content로 바꿨다 — _graphArea는
            // ComputeGraphHeight()가 "지금 있는 노드들을 담을 만큼만" 매번 다시 맞추는 영역이라, 노드가
            // 몇 개 없으면 세로로 아주 좁아진다. 그 상태에서 화면 아래쪽 빈 공간에 놓으면 실제로는 눈에
            // 보이는 그래프 패널 안인데도 "그래프 밖"으로 오판정돼 조용히 무시됐다 — 노드가 몰려 있는
            // 좁은 띠(대략 화면 중앙 부근)에 놓을 때만 성공하는 것처럼 보인 이유. _content(GraphContainer)
            // 는 BuildGraphPanZoom이 준 고정 크기(900x300)라 패널 어디에 놓아도 넉넉히 포함한다 —
            // 판정 범위만 바꾸는 것이고, 실제 배치 좌표 계산은 여전히 아래에서 _graphArea 기준으로 한다
            // (노드 anchoredPosition이 그 좌표계를 쓰므로, 판정 범위와 좌표계는 별개로 둬야 한다).
            // [2026-08-15] 판정 기준을 _content에서 그 부모(GraphViewport)로 한 번 더 좁혔다 — 작업 공간을
            // 크게 키우면서 _content가 화면 밖(우측 단서 서랍 아래까지)으로 한참 뻗게 됐고, 그 상태에서
            // _content 기준으로 판정하면 "서랍 위에 도로 놓아 취소"하려는 드롭까지 배치로 처리된다.
            // GraphViewport는 RectMask2D로 실제로 보이는 그래프 영역과 정확히 같아서, 위 문단이 말하는
            // "눈에 보이는 패널 안이면 성공" 조건을 그대로 표현한다.
            var dropBounds = (_content.parent as RectTransform) ?? _content;
            if (!RectTransformUtility.RectangleContainsScreenPoint(dropBounds, screenPosition, cam)) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_graphArea, screenPosition, cam, out var localPos);

            // [버그 수정, 2026-07-21] ScreenPointToLocalPointInRectangle이 반환하는 좌표는 _graphArea의
            // "피벗" 기준(_graphArea.pivot=(0.5,1) — 가로 중앙)인데, 단서/맵 노드는 전부 anchorMin/Max=
            // (0,1)(= _graphArea의 좌상단)을 기준으로 anchoredPosition을 쓴다. 이 둘을 그대로 같다고
            // 취급해 대입하고 있었던 게 "서랍에서 단서를 놓으면 클릭 위치와 무관하게 좌측 끝(또는 그
            // 바깥)에 생성되는" 버그의 원인 — 피벗 기준 좌표를 좌상단(앵커) 기준으로 변환해줘야 한다.
            // (참고: 세로는 _graphArea.pivot.y=1이 자식 anchor.y=1과 이미 일치해 보정이 필요 없다.)
            localPos.x += _graphArea.rect.width * _graphArea.pivot.x;

            bool alreadyPinned = NoteModule.Instance.IsPinned(clueId);

            // 이미 노트에 있고 그래프 위에 노드로도 떠 있으면(재배치) 곧바로 옮기고 끝 — 새로 핀할 필요가 없다.
            // [버그 수정, 2026-08-17] 판정에 "노트에 핀돼 있고 지금 실제로 보이는 노드"라는 조건을 더했다.
            // _clueVisuals는 사용자가 드래그해둔 위치를 지키려고 노드를 파괴하지 않고 SetActive(false)로만
            // 재우는 영속 딕셔너리(클래스 상단 주석 참고)라, 한 번 펼쳤던 단서를 노트에서 지우면 항목만
            // 사라지고 딕셔너리에는 꺼진 노드가 그대로 남는다. 예전 조건("노드가 있으면")은 그 꺼진 노드에
            // 걸려, 서랍에서 다시 끌어다 놓아도 보이지 않는 노드의 좌표만 바꾸고 조용히 끝났다 —
            // "지운 단서는 서랍에서 다시 못 꺼낸다"의 원인. 카드로 접어둔 단서(노드는 꺼져 있고 핀은 살아
            // 있음)도 같은 이유로 여기서 걸리면 안 되고, 아래 alreadyPinned 경로가 다시 펼쳐줘야 한다.
            if (alreadyPinned
                && _clueVisuals.TryGetValue(clueId, out var existingVisual)
                && existingVisual.Group.gameObject.activeSelf)
            {
                existingVisual.Group.anchoredPosition = localPos;
                HandleNodeMoved();
                return;
            }

            if (!alreadyPinned && !NoteModule.Instance.CanEdit)
            {
                Debug.LogWarning("[NoteRouteGraphView] 이동 중에는 서랍에서 단서를 배치할 수 없습니다.");
                return;
            }

            // GetOrCreateClueVisual이 이 위치를 기본 위치로 쓰도록 먼저 기록해둔다. 아직 핀 안 된
            // 단서면 AddManualPin이 OnNoteChanged를 동기 발행해 SetData가 바로 이어서 도니, 그
            // 안에서 곧바로 소비된다. 이미 핀은 돼 있지만 현재 경로의 맵 카드로 접혀 있던 단서라면
            // (예: 도감에서 핀만 해두고 아직 그래프에 펼친 적 없는 경우) 강제로 펼쳐서 보여준다.
            _pendingCluePositions[clueId] = localPos;
            _expandedClueIds.Add(clueId);

            if (alreadyPinned)
                SetData(_lastRoute, _lastEntries);
            else
                NoteModule.Instance.AddManualPin(clueId);
        }

        // 저장한 루트(보드) 저장 시 NotePanel이 호출 — 넘겨준 clueId 중 지금 그래프 위에 노드로 떠 있는
        // 것만 위치를 담아 반환한다. 아직 카드로 접혀 있거나(만들어진 적 없음) 존재하지 않으면 건너뛴다.
        // [2026-07-21 확장] 원래는 수동 핀 clueId만 넘겨받는 용도였으나, 경로연동 단서를 펼쳐서 옮긴
        // 위치도 저장하기로 하면서 GetPlacedClueIds()(지금 노드로 떠 있는 전부)를 넘기는 호출도 지원한다
        // — 이 메서드 자체는 "받은 목록의 현재 위치를 담아 돌려준다"는 동작만 하므로 변경 없음.
        public List<CluePositionEntry> ExportCluePositions(IEnumerable<string> clueIds)
        {
            var result = new List<CluePositionEntry>();
            if (clueIds == null) return result;
            foreach (var clueId in clueIds)
            {
                if (!_clueVisuals.TryGetValue(clueId, out var visual)) continue;
                var pos = visual.Group.anchoredPosition;
                result.Add(new CluePositionEntry { clueId = clueId, x = pos.x, y = pos.y });
            }
            return result;
        }

        // 보드 불러오기 직후 NotePanel이 호출한다. 이미 그래프에 노드로 떠 있으면 즉시 옮기고, 아직
        // 핀되지 않아 다음 SetData 재생성 때야 만들어질 단서는 PlaceClueAt과 동일한 방식으로
        // _pendingCluePositions에 큐잉해 GetOrCreateClueVisual이 생성 시점에 소비하도록 한다.
        public void ApplySavedPositions(IReadOnlyList<CluePositionEntry> positions)
        {
            if (positions == null) return;
            foreach (var p in positions)
            {
                var pos = new Vector2(p.x, p.y);
                if (_clueVisuals.TryGetValue(p.clueId, out var visual))
                    visual.Group.anchoredPosition = pos;
                else
                    _pendingCluePositions[p.clueId] = pos;
            }
        }

        // [신설, 2026-07-21] 저장한 루트(보드) 저장 시 NotePanel이 호출 — 지금 노드로 떠 있는 단서 전부
        // (수동 핀 + 카드에서 펼쳐진 경로연동 단서 구분 없이)의 clueId 목록. ExportCluePositions에
        // 그대로 넘기면 "지금 보이는 노드 전부"의 위치를 저장할 수 있다.
        public IEnumerable<string> GetPlacedClueIds() => _clueVisuals.Keys;

        // [신설, 2026-07-21] 저장한 루트(보드) 저장 시 NotePanel이 호출 — 지금 "카드가 아니라 노드로
        // 펼쳐져" 있는 경로연동 단서 clueId 목록(수동 핀 단서는 애초에 항상 노드라 여기 안 들어간다).
        public IEnumerable<string> GetExpandedClueIds() => _expandedClueIds;

        // [신설, 2026-07-21] 보드 불러오기 시 NotePanel이 ApplySavedPositions/RebuildRouteLinkedEntries
        // 보다 먼저 호출해야 한다 — 그래야 저장돼 있던 "펼쳐짐" 상태가 먼저 반영돼, 뒤이은 SetData 재생성
        // 때 그 경로연동 단서들이 카드가 아니라 노드로 만들어지고, ApplySavedPositions가 넘겨주는 위치가
        // 적용될 자리가 생긴다.
        public void ApplyExpandedClueIds(IEnumerable<string> clueIds)
        {
            _expandedClueIds.Clear();
            if (clueIds != null)
                foreach (var id in clueIds) _expandedClueIds.Add(id);
        }

        private Camera GetEventCamera() =>
            _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;

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

        private NodeVisual GetOrCreateNodeVisual(string guid, Vector2 defaultPos)
        {
            if (_nodeVisuals.TryGetValue(guid, out var existing)) return existing;

            var group = NewRect(_graphArea, "NodeGroup_" + guid);
            group.anchorMin = group.anchorMax = new Vector2(0f, 1f);
            group.pivot     = new Vector2(0f, 1f);
            group.anchoredPosition = defaultPos;

            var box = _nodeBoxTemplate != null ? Instantiate(_nodeBoxTemplate, group, false) : BuildNodeBoxDefault(group);
            box.name = "NodeBox";
            var boxRT = (RectTransform)box.transform;
            boxRT.anchorMin = boxRT.anchorMax = new Vector2(0f, 1f);
            boxRT.pivot     = new Vector2(0f, 1f);
            boxRT.anchoredPosition = Vector2.zero;
            boxRT.sizeDelta = new Vector2(_nodeBoxWidth, _nodeBoxHeight);

            var label = box.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            var background = box.GetComponent<Image>(); // [신설] 단서 연동 모드 선택 강조용

            var drag = box.GetComponent<NoteNodeDragHandle>();
            if (drag == null) drag = box.gameObject.AddComponent<NoteNodeDragHandle>();
            drag.Group          = group;
            drag.Bounds         = _graphArea;
            drag.RightMargin    = RouteBlockWidth;
            drag.OnMoved        = HandleNodeMoved;
            drag.PanZoom        = _panZoom;
            drag.ResolveTargets = CollectDragTargets; // 다중 선택 상태면 고른 노드 전부가 같이 움직인다
            drag.SetCanvas(_canvas);

            RegisterSelectable(group, background, _nodeBoxWidth, _nodeBoxHeight, RouteBlockWidth);

            // [신설, 2026-07-21] 요청 — 단서 연동을 단서-단서뿐 아니라 단서-맵 노드 간에도 가능하게 한다.
            // 맵 노드는 원래 클릭 인터랙션이 없었으므로(드래그만 있었음) 새로 추가 — 링크 모드가 아닐 때는
            // 그냥 아무 동작도 하지 않는다(접기/펼치기 같은 대체 동작 자체가 없음).
            var toggle = box.GetComponent<NoteClickToToggle>();
            if (toggle == null) toggle = box.gameObject.AddComponent<NoteClickToToggle>();
            toggle.OnClick = () =>
            {
                if (!LinkModeActive) return;
                HandleLinkModeNodeClicked(guid);
            };
            // [신설, 2026-08-15] Ctrl+클릭은 링크 모드/접기 같은 원래 동작 대신 선택 토글로만 쓴다.
            toggle.OnCtrlClick = () => ToggleSelection(group);

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

            var visual = new NodeVisual { Group = group, Label = label, CardsContainer = cardsContainer, Background = background };
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
            if (_clueVisuals.TryGetValue(clueId, out var existing))
            {
                // [버그 수정, 2026-08-17] 예전엔 여기서 그냥 돌려줘, 노드가 한 번이라도 만들어진 적 있는
                // 단서는 서랍에서 다시 끌어다 놓아도 큐에 넣어둔 드롭 위치가 영영 소비되지 않았다 —
                // 지웠다 다시 놓은 단서가 놓은 자리가 아니라 예전 자리에서 되살아나는 원인.
                if (_pendingCluePositions.TryGetValue(clueId, out var pendingForExisting))
                {
                    existing.Group.anchoredPosition = pendingForExisting;
                    _pendingCluePositions.Remove(clueId);
                }
                return existing;
            }

            // 서랍에서 방금 드래그해 놓은 위치가 있으면 그 자리를 기본 위치로 쓴다(PlaceClueAt 참고).
            if (_pendingCluePositions.TryGetValue(clueId, out var pending))
            {
                defaultPos = pending;
                _pendingCluePositions.Remove(clueId);
            }

            var group = NewRect(_graphArea, "ClueNode_" + clueId);
            group.anchorMin = group.anchorMax = new Vector2(0f, 1f);
            group.pivot     = new Vector2(0f, 1f);
            group.anchoredPosition = defaultPos;
            group.sizeDelta = new Vector2(_clueNodeWidth, 0f);
            var background = AddImg(group, _colClueNode);

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

            // [신설] 노드 형태여도(카드로 접기 전이라도) 바로 지울 수 있는 삭제 버튼 — Button이 직접
            // IPointerClickHandler를 구현하므로 아래 NoteClickToToggle(배경 클릭 = 접기/펼치기)과
            // 간섭하지 않는다(카드의 BtnDelete와 동일한 이유, PopulateCard 참고).
            var deleteBtnRT = NewRect(group, "DeleteBtn");
            var deleteLe = deleteBtnRT.gameObject.AddComponent<LayoutElement>();
            deleteLe.preferredHeight = 10f;
            deleteLe.flexibleWidth = 1f;
            var deleteImg = AddImg(deleteBtnRT, new Color(0.42f, 0.10f, 0.10f));
            var deleteBtn = deleteBtnRT.gameObject.AddComponent<Button>();
            deleteBtn.targetGraphic = deleteImg;
            deleteBtn.transition = Selectable.Transition.None;
            var deleteTxtRT = NewRect(deleteBtnRT, "Text");
            StretchFull(deleteTxtRT);
            var deleteTmp = deleteTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) deleteTmp.font = _font;
            deleteTmp.text = "삭제";
            deleteTmp.fontSize = _cardFontSize;
            deleteTmp.alignment = TextAlignmentOptions.Center;
            deleteTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            deleteTmp.color = Color.white;

            var drag = group.gameObject.AddComponent<NoteNodeDragHandle>();
            drag.Group          = group;
            drag.Bounds         = _graphArea;
            drag.RightMargin    = _clueNodeWidth;
            drag.OnMoved        = HandleNodeMoved;
            drag.PanZoom        = _panZoom;
            drag.ResolveTargets = CollectDragTargets;
            drag.SetCanvas(_canvas);

            // 단서 노드는 ContentSizeFitter로 높이가 바뀌므로 FixedHeight를 0으로 둬 실시간 높이를 읽게 한다.
            RegisterSelectable(group, background, _clueNodeWidth, 0f, _clueNodeWidth);

            var clueToggle = group.gameObject.AddComponent<NoteClickToToggle>();
            // OnClick(접기/펼치기·링크 모드)은 PopulateClueNode가 패스마다 다시 배선하지만, Ctrl+클릭은
            // 노드가 무슨 상태든 늘 선택 토글이라 여기서 한 번만 걸어두면 된다.
            clueToggle.OnCtrlClick = () => ToggleSelection(group);

            // [신설] 코멘트 노드 컨테이너 — group의 VerticalLayoutGroup(Text/DeleteBtn을 세로로 쌓는 용도)
            // 대상에서 LayoutElement.ignoreLayout으로 제외하고, 대신 group 자신의 우측 하단 모서리에
            // 앵커(anchorMin=anchorMax=(1,0))해 별개의 작은 노드처럼 붙는다 — group의 높이가
            // ContentSizeFitter로 바뀌어도 앵커 특성상 자동으로 그 모서리를 따라간다.
            var commentsContainer = NewRect(group, "Comments");
            commentsContainer.anchorMin = commentsContainer.anchorMax = new Vector2(1f, 0f);
            commentsContainer.pivot     = new Vector2(0f, 1f);
            commentsContainer.anchoredPosition = new Vector2(_commentGapX, -_commentGapY);
            commentsContainer.sizeDelta = new Vector2(_commentAreaWidth, 0f);

            var commentsIgnore = commentsContainer.gameObject.AddComponent<LayoutElement>();
            commentsIgnore.ignoreLayout = true;

            var comVlg = commentsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            comVlg.spacing = _commentSpacing;
            comVlg.childControlWidth     = true;
            comVlg.childControlHeight    = true;
            comVlg.childForceExpandWidth  = true;
            comVlg.childForceExpandHeight = false;

            var comCsf = commentsContainer.gameObject.AddComponent<ContentSizeFitter>();
            comCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // [신설, 2026-08-11] 첨부물 컨테이너 — 코멘트가 우측 아래로 뻗는 것과 대칭으로 좌측 아래로
            // 뻗는다(anchor를 좌하단 (0,0), pivot을 우상단 (1,1)로 잡아 왼쪽·아래 방향으로 자란다).
            // 서로 반대편이라 코멘트가 많은 단서에서도 겹치지 않는다.
            var attachmentsContainer = NewRect(group, "Attachments");
            attachmentsContainer.anchorMin = attachmentsContainer.anchorMax = new Vector2(0f, 0f);
            attachmentsContainer.pivot     = new Vector2(1f, 1f);
            attachmentsContainer.anchoredPosition = new Vector2(-_attachmentGapX, -_attachmentGapY);
            attachmentsContainer.sizeDelta = new Vector2(_attachmentAreaWidth, 0f);

            var attachmentsIgnore = attachmentsContainer.gameObject.AddComponent<LayoutElement>();
            attachmentsIgnore.ignoreLayout = true;

            var attVlg = attachmentsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            attVlg.spacing = _attachmentSpacing;
            attVlg.childControlWidth     = true;
            attVlg.childControlHeight    = true;
            attVlg.childForceExpandWidth  = true;
            attVlg.childForceExpandHeight = false;

            var attCsf = attachmentsContainer.gameObject.AddComponent<ContentSizeFitter>();
            attCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var visual = new ClueNodeVisual
            {
                Group = group,
                Text = tmp,
                DeleteButton = deleteBtn,
                Background = background,
                CommentsContainer = commentsContainer,
                AttachmentsContainer = attachmentsContainer,
            };
            _clueVisuals[clueId] = visual;
            return visual;
        }

        // canCollapse: 이 단서가 현재 경로의 어떤 맵과 연관돼(카드로 돌아갈 자리가 있어) 클릭으로 다시
        // 접을 수 있는지 — 도감/서랍 수동 핀 등 소속 맵이 없는 것은 카드로 돌아갈 곳이 없으므로 false로
        // 호출된다. isCompact: canCollapse=false인 노드 전용 — true면 제목 한 줄만, false면 설명/코멘트까지
        // 전체 표시(경로연동 카드↔노드 토글과 같은 느낌을 주는 "간략히/전체 보기" 토글, 아래 참고).
        private void PopulateClueNode(ClueNodeVisual visual, NoteEntry entry, bool canCollapse, bool isCompact = false)
        {
            // [2026-07-21] MapGraph(clues.json)뿐 아니라 유저가 노트에서 직접 만든 단서(CodexUserEntry)도
            // 다뤄야 해서 NoteClueResolver로 통일했다 — 상세 이유는 그 파일 주석 참고.
            var resolved = NoteClueResolver.Resolve(entry.clueId);

            var sb = new StringBuilder();
            sb.Append($"<b>{(resolved.HasValue ? resolved.Value.Name : entry.clueId)}</b> <size=70%>[{ReasonLabel(entry.reason)}]</size>");
            if (!isCompact && resolved.HasValue && !string.IsNullOrEmpty(resolved.Value.Description))
                sb.Append($"\n{resolved.Value.Description}");
            visual.Text.text = sb.ToString();

            if (visual.Background != null)
            {
                // [요청, 2026-07-21] 단서 연동 모드에서 첫 번째로 선택해둔 단서는 별도 강조색으로 표시 —
                // 두 번째 단서를 고르기 전까지 "지금 뭘 골라뒀는지" 한눈에 보이게 한다.
                if (!string.IsNullOrEmpty(_linkModeSelectedId) && _linkModeSelectedId == entry.clueId)
                {
                    visual.Background.color = _colLinkModeSelected;
                }
                else
                {
                    // 다른 키워드의 단서를 그래프에서 색으로 구분 — 키워드가 여러 개면 배열의 첫 번째
                    // (작성자가 적어둔 순서상 "주 키워드")를 기준으로 삼는다. 키워드가 없으면 기본색.
                    var primaryKeyword = resolved?.Keywords?.FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));
                    visual.Background.color = string.IsNullOrEmpty(primaryKeyword)
                        ? _colClueNode
                        : ColorForKeyword(primaryKeyword.Trim());
                }
            }

            // [요청, 2026-07-21] 코멘트(comments)를 본문 텍스트에 줄로 이어붙이던 것을, 노드 우측 아래에
            // 붙는 별개의 작은 노드들로 교체 — 접힌(isCompact) 상태에서는 기존과 동일하게 아예 안 보인다.
            if (visual.CommentsContainer != null)
            {
                var comments = resolved?.Comments;
                bool showComments = !isCompact && comments != null && comments.Length > 0;
                visual.CommentsContainer.gameObject.SetActive(showComments);
                if (showComments)
                {
                    foreach (var c in comments)
                    {
                        if (string.IsNullOrEmpty(c.text)) continue;
                        PopulateCommentNode(_commentPool.Get(visual.CommentsContainer), c);
                    }
                }
            }

            // [신설, 2026-08-11] 첨부물(사진/소리/맵)을 좌측 아래에 붙는 작은 노드로 표시 — 도감 카드의
            // "첨부" 영역과 같은 내용을 노트에서도 볼 수 있게 한 것. 코멘트와 마찬가지로 접힌(isCompact)
            // 상태에서는 감춘다.
            if (visual.AttachmentsContainer != null)
            {
                var attachments = resolved?.Attachments;
                bool showAttachments = !isCompact && attachments != null && attachments.Length > 0;
                visual.AttachmentsContainer.gameObject.SetActive(showAttachments);
                if (showAttachments)
                {
                    foreach (var a in attachments)
                    {
                        if (a == null) continue;
                        PopulateAttachmentNode(_attachmentPool.Get(visual.AttachmentsContainer), a);
                    }
                }
            }

            // [신설] 노드 형태로 떠 있는 동안에도(카드로 접기 전이라도) 바로 삭제할 수 있다 — 소속 맵이
            // 없는(canCollapse=false) 노드는 애초에 카드로 못 돌아가므로 이게 유일한 삭제 경로다.
            if (visual.DeleteButton != null)
            {
                visual.DeleteButton.onClick.RemoveAllListeners();
                visual.DeleteButton.onClick.AddListener(() => OnDeleteRequested?.Invoke(entry));
            }

            var toggle = visual.Group.GetComponent<NoteClickToToggle>();
            if (toggle != null)
            {
                toggle.OnClick = () =>
                {
                    // [신설, 2026-07-21] 단서 연동 모드에서는 클릭이 접기/펼치기가 아니라 "연동 대상 선택"
                    // 으로 의미가 바뀐다 — HandleLinkModeNodeClicked 참고.
                    if (LinkModeActive)
                    {
                        HandleLinkModeNodeClicked(entry.clueId);
                        return;
                    }

                    if (canCollapse)
                    {
                        _expandedClueIds.Remove(entry.clueId);
                        SetData(_lastRoute, _lastEntries);
                    }
                    else
                    {
                        // 소속 맵이 없어 카드로 못 돌아가는 노드 — 대신 "간략히 보기(제목만)"와 "전체 보기"를
                        // 토글한다. 경로연동 단서가 카드↔노드로 접고 펼 수 있는 것과 같은 사용성을 준다.
                        if (!_collapsedStandaloneClueIds.Remove(entry.clueId))
                            _collapsedStandaloneClueIds.Add(entry.clueId);
                        SetData(_lastRoute, _lastEntries);
                    }
                };
            }
        }

        // ─── 단서 연동 모드 — 마우스로 두 개를 순서대로 클릭해 수동으로 "연관" 간선을 잇는다 ────
        // [2026-07-21 확장] 처음엔 단서-단서만 대상이었으나, 요청으로 단서-맵 노드 간에도 연동할 수
        // 있게 넓혔다 — id는 clueId일 수도, 맵 노드 guid일 수도 있다(NoteModule.ClueLinks 입장에선
        // 그냥 문자열 쌍이라 구분할 필요가 없음). 실제 연결 정보 저장은 NoteModule.ClueLinks가 담당
        // (간선 렌더링은 RelayoutEdges의 세 번째 루프 — TryGetLinkableAnchor가 단서/맵 노드 둘 다 조회).

        public bool LinkModeActive { get; private set; }
        private string _linkModeSelectedId;

        // NotePanel의 "단서 연동" 토글 버튼이 호출한다.
        public void SetLinkMode(bool active)
        {
            LinkModeActive = active;
            _linkModeSelectedId = null;
            SetData(_lastRoute, _lastEntries);
        }

        // 단서 노드(PopulateClueNode)와 맵 노드(GetOrCreateNodeVisual) 양쪽의 클릭 핸들러가 공통으로
        // 호출한다 — id가 clueId인지 맵 노드 guid인지는 이 메서드 입장에서 구분할 필요가 없다.
        private void HandleLinkModeNodeClicked(string id)
        {
            if (string.IsNullOrEmpty(_linkModeSelectedId))
            {
                _linkModeSelectedId = id; // 첫 번째 선택 — 강조색으로 표시
                SetData(_lastRoute, _lastEntries);
                return;
            }

            if (_linkModeSelectedId == id)
            {
                _linkModeSelectedId = null; // 같은 걸 다시 클릭 = 선택 취소
                SetData(_lastRoute, _lastEntries);
                return;
            }

            // 두 번째 선택 — 연결/해제 토글(NoteModule.OnNoteChanged가 다시 SetData를 유발해 간선이 반영됨).
            NoteModule.Instance?.ToggleClueLink(_linkModeSelectedId, id);
            _linkModeSelectedId = null;
        }

        // ─── 다중 선택 (빈 배경 마퀴 드래그 / Ctrl+클릭) + 그룹 이동 ─────────────
        // [신설, 2026-08-15, 요청] 노드를 하나씩만 옮길 수 있어 배치를 정리하는 비용이 컸다. 이제
        //   - 빈 배경을 좌드래그하면 사각형 안에 걸친 노드가 전부 선택되고(Ctrl을 누른 채 드래그하면 추가 선택),
        //   - Ctrl+클릭으로 노드를 하나씩 넣고 뺄 수 있으며,
        //   - 선택된 노드 중 하나를 끌면 선택된 전부가 같은 델타로 함께 움직인다.
        // 배경 팬은 가운데/오른쪽 버튼 드래그 또는 Alt+좌드래그로 옮겼다 — 좌드래그가 마퀴에 쓰이므로.
        // 선택 강조는 노드 배경 Image에 Outline을 하나씩 달아두고 켜고 끄는 방식이다(배경색 자체를 바꾸면
        // 키워드별 색 구분·링크 모드 강조색과 서로 덮어써서 어느 쪽도 믿을 수 없게 된다).

        private void RegisterSelectable(RectTransform group, Image background, float width, float fixedHeight, float rightMargin)
        {
            if (group == null) return;

            Outline outline = null;
            if (background != null)
            {
                outline = background.GetComponent<Outline>();
                if (outline == null) outline = background.gameObject.AddComponent<Outline>();
                outline.effectColor    = _colSelection;
                outline.effectDistance = new Vector2(_selectionOutlineWidth, -_selectionOutlineWidth);
                outline.useGraphicAlpha = false;
                outline.enabled = false;
            }

            _selectables[group] = new SelectableNode
            {
                Group = group,
                Outline = outline,
                Width = width,
                FixedHeight = fixedHeight,
                RightMargin = rightMargin,
            };
        }

        private void ToggleSelection(RectTransform group)
        {
            if (group == null) return;
            if (!_selected.Remove(group)) _selected.Add(group);
            ApplySelectionVisual(group);
        }

        private void ClearSelection()
        {
            if (_selected.Count == 0) return;
            var previous = _selected.ToList();
            _selected.Clear();
            foreach (var g in previous) ApplySelectionVisual(g);
        }

        // 카드로 접히거나 경로에서 빠져 화면에서 사라진 노드는 선택에서도 빼준다 — 안 그러면 보이지도
        // 않는 노드가 그룹 이동에 딸려 다닌다.
        private void PruneSelection()
        {
            if (_selected.Count == 0) return;
            var stale = _selected.Where(g => g == null || !g.gameObject.activeSelf).ToList();
            foreach (var g in stale)
            {
                _selected.Remove(g);
                ApplySelectionVisual(g);
            }
        }

        private void ApplySelectionVisual(RectTransform group)
        {
            if (group == null) return;
            if (_selectables.TryGetValue(group, out var node) && node.Outline != null)
                node.Outline.enabled = _selected.Contains(group);
        }

        // NoteNodeDragHandle이 드래그를 시작할 때 호출한다 — 끌린 노드가 현재 선택에 포함돼 있으면
        // 선택된 전부를, 아니면 그 노드 하나만 돌려준다(선택 밖의 노드를 끌었다고 선택을 지우지는 않는다.
        // 여러 개를 골라둔 채 다른 노드를 잠깐 옮기는 일이 흔해서, 그때마다 선택이 날아가면 더 번거롭다).
        public List<NoteDragTarget> CollectDragTargets(RectTransform group)
        {
            var targets = new List<NoteDragTarget>();
            if (group == null) return targets;

            if (_selected.Count > 1 && _selected.Contains(group))
            {
                foreach (var g in _selected)
                    if (g != null && g.gameObject.activeSelf) targets.Add(MakeDragTarget(g));
            }
            else
            {
                targets.Add(MakeDragTarget(group));
            }
            return targets;
        }

        private NoteDragTarget MakeDragTarget(RectTransform group)
        {
            float rightMargin = 0f;
            float height = _nodeBoxHeight;
            if (_selectables.TryGetValue(group, out var node))
            {
                rightMargin = node.RightMargin;
                height = node.FixedHeight > 0f ? node.FixedHeight : node.Group.rect.height;
            }

            // 작업 공간(_content) 아래로 빠져나가지 못하게 하한을 같이 넘긴다 — 팬 범위가 콘텐츠 기준으로
            // 제한돼 있어(GraphPanZoom.ConfigureBounds), 공간 밖으로 내려간 노드는 다시 볼 방법이 없다.
            float spaceH = _content != null ? _content.rect.height : 0f;
            return new NoteDragTarget
            {
                Group = group,
                RightMargin = rightMargin,
                MinY = Mathf.Min(0f, -(spaceH - height)),
            };
        }

        // ─── 마퀴(선택 사각형) — 이 컴포넌트가 GraphScroll의 포인터 이벤트를 GraphPanZoom과 같이 받는다 ──

        public static bool IsCtrlHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool IsAltHeld() => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        // GraphPanZoom과 동일한 이유 — PointerDown을 수락해야 BeginDrag가 이 오브젝트로 전달된다.
        public void OnPointerDown(PointerEventData e) { }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_graphArea == null) return;
            // 가운데/오른쪽 드래그와 Alt+좌드래그는 예전처럼 배경 팬으로 남겨둔다(GraphPanZoom이 그대로 처리).
            if (e.button != PointerEventData.InputButton.Left || IsAltHeld()) return;

            _marqueeActive = true;
            if (!IsCtrlHeld()) ClearSelection(); // Ctrl을 누른 채 시작하면 기존 선택에 더한다
            _marqueeStart = ScreenToGraph(e.position);

            // 마퀴가 도는 동안은 배경이 같이 밀리면 안 된다 — NoteNodeDragHandle과 동일한 이중 잠금.
            if (_panZoom != null)
            {
                _panZoom.SuppressDrag = true;
                _panZoom.LockPosition();
            }

            DrawMarquee(_marqueeStart, _marqueeStart);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_marqueeActive) return;
            DrawMarquee(_marqueeStart, ScreenToGraph(e.position));
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;

            SelectWithin(MakeRect(_marqueeStart, ScreenToGraph(e.position)));
            if (_marqueeRT != null) _marqueeRT.gameObject.SetActive(false);

            if (_panZoom != null)
            {
                _panZoom.SuppressDrag = false;
                _panZoom.UnlockPosition();
            }
        }

        // 빈 배경을 그냥 클릭하면 선택 해제 — 드래그를 동반한 클릭은 위 마퀴가 이미 처리했다.
        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging || e.button != PointerEventData.InputButton.Left) return;
            if (IsCtrlHeld()) return;
            ClearSelection();
        }

        // 스크린 좌표 → 노드들이 쓰는 좌표계(_graphArea 좌상단 기준). PlaceClueAt과 동일한 피벗 보정이
        // 필요하다 — 자세한 이유는 그쪽 주석 참고.
        private Vector2 ScreenToGraph(Vector2 screenPosition)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _graphArea, screenPosition, GetEventCamera(), out var local);
            local.x += _graphArea.rect.width * _graphArea.pivot.x;
            return local;
        }

        private static Rect MakeRect(Vector2 a, Vector2 b) => Rect.MinMaxRect(
            Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        private void DrawMarquee(Vector2 a, Vector2 b)
        {
            EnsureMarquee();
            var r = MakeRect(a, b);
            _marqueeRT.anchoredPosition = new Vector2(r.xMin, r.yMax); // pivot=(0,1) — 좌상단 기준
            _marqueeRT.sizeDelta = new Vector2(r.width, r.height);
            _marqueeRT.gameObject.SetActive(true);
            _marqueeRT.SetAsLastSibling(); // 노드·간선보다 위에 그린다
        }

        private void EnsureMarquee()
        {
            if (_marqueeRT != null) return;

            _marqueeRT = NewRect(_graphArea, "SelectionMarquee");
            _marqueeRT.anchorMin = _marqueeRT.anchorMax = new Vector2(0f, 1f);
            _marqueeRT.pivot     = new Vector2(0f, 1f);
            // 간선/코멘트 노드와 동일한 이유 — 순수 장식이라 클릭을 가로채면 안 된다.
            AddImg(_marqueeRT, _colMarquee).raycastTarget = false;
            _marqueeRT.gameObject.SetActive(false);
        }

        private void SelectWithin(Rect area)
        {
            foreach (var kv in _selectables)
            {
                var node = kv.Value;
                if (node.Group == null || !node.Group.gameObject.activeSelf) continue;

                float h = node.FixedHeight > 0f ? node.FixedHeight : node.Group.rect.height;
                var pos = node.Group.anchoredPosition;
                // 노드 좌표는 아래로 갈수록 y가 음수 — Rect의 yMin(아래쪽)은 pos.y - h가 된다.
                var nodeRect = new Rect(pos.x, pos.y - h, node.Width, h);
                if (!area.Overlaps(nodeRect)) continue;

                if (_selected.Add(node.Group)) ApplySelectionVisual(node.Group);
            }
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
                DrawArrowHead(a, b, _colEdge); // 진행 방향(a→b) 표시 — 맵-맵 체인 간선만 대상
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
            var resolvedCache = new Dictionary<string, ResolvedClue?>();
            DreamReadingModule dreamReadings = DreamReadingModule.Instance;
            ResolvedClue? ResolveCached(string id)
            {
                if (!resolvedCache.TryGetValue(id, out var r)) resolvedCache[id] = r = NoteClueResolver.Resolve(id);
                return r;
            }
            for (int i = 0; i < visibleIds.Count; i++)
            {
                // Dream-reading clues are intentionally excluded from automatic keyword edges.
                // The manual-link pass below remains available for resolving a dream reading.
                if (dreamReadings != null && dreamReadings.IsDreamReadingClue(visibleIds[i])) continue;

                var resolvedA = ResolveCached(visibleIds[i]);
                for (int j = i + 1; j < visibleIds.Count; j++)
                {
                    if (dreamReadings != null && dreamReadings.IsDreamReadingClue(visibleIds[j])) continue;

                    var resolvedB = ResolveCached(visibleIds[j]);
                    if (!CodexFilterService.SharesKeyword(resolvedA?.Keywords, resolvedB?.Keywords)) continue;

                    var rt = PrepEdge(_colKeywordEdge);
                    var groupA = _visibleClueVisuals[visibleIds[i]].visual.Group;
                    var groupB = _visibleClueVisuals[visibleIds[j]].visual.Group;
                    Vector2 a = ClueNodeAnchor(groupA);
                    Vector2 b = ClueNodeAnchor(groupB);
                    SetEdgeLayout(rt, a, b);
                }
            }

            // [신설, 2026-07-21] 단서 연동 모드에서 사용자가 직접 이어둔 "연관" 간선 — 자동 키워드 공유
            // 간선(_colKeywordEdge)과 구분되는 별도 색. [2026-07-21 확장] 단서-단서뿐 아니라 단서-맵
            // 노드 조합도 대상이라, 양 끝을 TryGetLinkableAnchor로 조회한다(단서 노드/맵 노드 어느
            // 쪽이든 지금 화면에 보이는 것만). 둘 중 하나라도 안 보이면(카드로 접힌 상태 등) 건너뛴다.
            if (NoteModule.Instance != null)
            {
                foreach (var (idA, idB) in NoteModule.Instance.ClueLinks)
                {
                    if (!TryGetLinkableAnchor(idA, out var a) || !TryGetLinkableAnchor(idB, out var b)) continue;

                    var rt = PrepEdge(_colManualLinkEdge);
                    SetEdgeLayout(rt, a, b);
                }
            }

            _edgePool.EndPass();
            _arrowPool.EndPass();

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

        // [신설, 2026-07-21] 단서 연동 모드 간선용 — id가 단서 노드인지 맵 노드인지 몰라도(둘 다 될 수
        // 있으므로) 지금 화면에 보이는 쪽을 찾아 앵커 좌표를 계산해준다. 어느 쪽에도 없으면(카드로
        // 접혀 있거나 애초에 이 경로/노트에 없는 id) false.
        private bool TryGetLinkableAnchor(string id, out Vector2 anchor)
        {
            if (_visibleClueVisuals.TryGetValue(id, out var clueVisual))
            {
                anchor = ClueNodeAnchor(clueVisual.visual.Group);
                return true;
            }
            // _nodeVisuals는 _clueVisuals와 마찬가지로 파괴하지 않고 Show/Hide만 하는 영속 딕셔너리라,
            // 지금 경로에 없는 맵도 (꺼진 채로) 남아 있다 — 꺼진 노드로 간선을 그으면 허공에 선이
            // 남으므로 실제로 보이는 것만 앵커로 인정한다.
            if (_nodeVisuals.TryGetValue(id, out var nodeVisual) && nodeVisual.Group.gameObject.activeSelf)
            {
                anchor = NodeAnchor(nodeVisual.Group, _nodeBoxWidth, _nodeBoxHeight);
                return true;
            }
            anchor = default;
            return false;
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
            // [버그 수정, 2026-07-21] 간선은 순수 장식이라 클릭 대상일 이유가 없는데 raycastTarget 기본값이
            // true라 노드 경계 근처를 클릭하면 이 선이 먼저 raycast를 가로챌 수 있었다 — 노드 자체엔
            // NoteNodeDragHandle이 있지만 간선/화살표엔 아무 핸들러가 없어서, 이렇게 가로채진 클릭은
            // GetEventHandler가 부모를 계속 타고 올라가다 결국 GraphScroll의 GraphPanZoom까지 도달해
            // "노드를 드래그하려 했는데 배경 전체가 팬되는" 증상으로 이어졌다.
            AddImg(rt, _colEdge).raycastTarget = false;
            rt.sizeDelta = new Vector2(10f, _edgeThickness);

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // 간선 중점에 진행 방향(from→to)을 가리키는 "＞" 모양 화살촉을 그린다 — 짧은 사각형 프롱
        // 두 개를 진행 방향 기준으로 대칭으로 벌려서 겹치는, 별도 스프라이트 없는 방식.
        private void DrawArrowHead(Vector2 from, Vector2 to, Color color)
        {
            Vector2 dir = to - from;
            if (dir.sqrMagnitude < 0.0001f) return;

            float dirAngleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Vector2 tip = from + dir * 0.5f; // 간선 중점 — 양 끝 노드 박스에 가려지지 않는 지점

            PrepArrowProng(tip, dirAngleDeg + _arrowSpreadDeg, color);
            PrepArrowProng(tip, dirAngleDeg - _arrowSpreadDeg, color);
        }

        // 화살촉 프롱 하나 — pivot을 뾰족한 끝(오른쪽 중앙)에 둬서, anchoredPosition을 화살촉 끝점(tip)에
        // 그대로 대입하면 몸통이 그 반대 방향(angleDeg 기준 뒤쪽)으로 뻗어나가는 방식.
        private void PrepArrowProng(Vector2 tip, float angleDeg, Color color)
        {
            var go = _arrowPool.Get(_graphArea);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(_arrowSize, _edgeThickness);
            rt.anchoredPosition = tip;
            rt.localRotation    = Quaternion.Euler(0f, 0f, angleDeg);

            var img = go.GetComponent<Image>();
            if (img != null) img.color = color;
        }

        private GameObject BuildArrowProngTemplate()
        {
            var rt = NewRect(null, "ArrowProng");
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(1f, 0.5f);
            AddImg(rt, _colEdge).raycastTarget = false; // BuildEdgeTemplate과 동일한 이유 — 순수 장식, 클릭 대상 아님
            rt.sizeDelta = new Vector2(_arrowSize, _edgeThickness);

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ─── 콘텐츠 크기 — 그래프(프리폼) 높이만큼 내부 레이아웃(_graphArea/_otherArea)을 갱신 ─
        //
        // [버그 수정, 2026-07-21] 예전(ScrollRect 시절)에는 스크롤 가능 범위를 알려주기 위해 _content
        // (=GraphContainer, GraphPanZoom이 팬·줌으로 옮기는 그 대상)의 sizeDelta.y까지 여기서 같이
        // 키웠는데, GraphPanZoom으로 바꾼 뒤에도 이 코드가 그대로 남아있었던 게 "노드를 위/아래로
        // 드래그하면 배경 전체가 같이 세로로 밀리는" 버그의 진짜 원인이었다 — _content는 pivot=(0,0)
        // (좌하단 고정)인데, _graphArea는 그 _content의 "위쪽 모서리"에 앵커돼 있다(anchorMin/Max=
        // (0,1)/(1,1)). _content의 세로 크기가 커질수록 좌하단은 고정된 채 위쪽 모서리만 위로 밀려나므로,
        // 거기 앵커된 _graphArea(와 그 안의 모든 노드)가 그 모서리를 따라 통째로 위로 밀려 올라갔던
        // 것 — 좌우는 sizeDelta.x를 건드리지 않아 멀쩡했던 것과 정확히 대칭되는 원인. GraphPanZoom은
        // ScrollRect와 달리 팬 범위에 제한이 없어(선택적 클램프만 있음, GraphPanZoom.ConfigureBounds)
        // _content 크기를 그래프 내용에 맞춰 정밀하게 키울 필요 자체가 없으므로, 아예 건드리지 않는다
        // (BuildGraphPanZoom에서 준 고정 크기 그대로 유지 — 클램프는 그 고정 크기 기준으로 동작).
        private void UpdateContentSize()
        {
            float graphHeight = ComputeGraphHeight();
            _graphArea.sizeDelta = new Vector2(_graphArea.sizeDelta.x, graphHeight);
            _otherArea.anchoredPosition = new Vector2(0f, -graphHeight);
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
            var resolved = NoteClueResolver.Resolve(entry.clueId);
            string title = resolved.HasValue ? resolved.Value.Name : entry.clueId;

            var tmp = cardGO.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = $"{title}  [{ReasonLabel(entry.reason)}]";
                tmp.color = Color.white;
            }

            var delBtn = cardGO.transform.Find("BtnDelete")?.GetComponent<Button>();
            if (delBtn != null)
            {
                delBtn.onClick.RemoveAllListeners();
                delBtn.onClick.AddListener(() => OnDeleteRequested?.Invoke(entry));
            }

            // 카드 자신(빈 배경/텍스트 부분) 클릭 시 단서 노드로 승격 — 버튼 영역은 Button이 직접
            // IPointerClickHandler를 구현하므로 이 핸들러와 간섭하지 않는다.
            var toggle = cardGO.GetComponent<NoteClickToToggle>();
            if (toggle == null) toggle = cardGO.AddComponent<NoteClickToToggle>();
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

        // ─── 코멘트 노드 (풀링) — 단서 노드 우측 아래에 붙는 별개의 작은 노드 ──────

        private void PopulateCommentNode(GameObject rowGO, CodexComment c)
        {
            var tmp = rowGO.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = string.IsNullOrEmpty(c.createdAt)
                    ? $"<b>{c.author}</b> {c.text}"
                    : $"<b>{c.author}</b> <size=80%>{c.createdAt}</size>\n{c.text}";
        }

        private GameObject BuildCommentTemplate()
        {
            var rt = NewRect(null, "CommentNode");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            // 코멘트 노드는 순수 출력용(클릭 핸들러 없음) — 다른 노드를 드래그해 근처로 옮겼을 때 이 위에
            // 겹치면 raycastTarget=true 기본값 탓에 클릭을 가로챌 수 있어(BuildEdgeTemplate과 동일한 이유) 끈다.
            AddImg(rt, _colCommentNode).raycastTarget = false;

            var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(3, 3, 2, 2);
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            var csf = rt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var txtRT = NewRect(rt, "Text");
            var txtLe = txtRT.gameObject.AddComponent<LayoutElement>();
            txtLe.flexibleWidth = 1f;
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _commentFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false; // 배경 Image와 동일한 이유

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ─── 첨부물 노드 (풀링) — 단서 노드 좌측 아래에 붙는 작은 노드 ──────────
        // 도감 카드(CodexCardView)의 "첨부" 행과 같은 데이터를 노트 문맥에 맞게 다시 그린 것이다.
        // 사진은 미리보기, 소리는 재생 버튼, 맵은 아이콘+이름(누르면 지도로 이동).

        private void PopulateAttachmentNode(GameObject rowGO, ClueAttachment a)
        {
            var iconImg    = rowGO.transform.Find("Head/Icon")?.GetComponent<Image>();
            var labelTmp   = rowGO.transform.Find("Head/Text")?.GetComponent<TextMeshProUGUI>();
            var btnTF      = rowGO.transform.Find("Head/BtnAction");
            var btn        = btnTF != null ? btnTF.GetComponent<Button>() : null;
            var btnLabel   = btnTF != null ? btnTF.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
            var previewTF  = rowGO.transform.Find("Preview");
            var previewImg = previewTF != null ? previewTF.GetComponent<Image>() : null;

            btn?.onClick.RemoveAllListeners();

            string label = ClueAttachmentService.ResolveLabel(a);
            var icon = ClueAttachmentService.ResolveIcon(a);
            bool showBtn = false, showPreview = false, missing = false;

            switch (a.kind)
            {
                case ClueAttachmentKind.Image:
                {
                    var sprite = ClueAttachmentService.LoadSprite(a.address);
                    missing = sprite == null;
                    if (!missing && previewImg != null)
                    {
                        previewImg.sprite = sprite;
                        previewImg.color = Color.white;
                        showPreview = true;
                    }
                    break;
                }
                case ClueAttachmentKind.Audio:
                {
                    var clip = ClueAttachmentService.LoadAudio(a.address);
                    missing = clip == null;
                    showBtn = !missing;
                    if (showBtn)
                    {
                        if (btnLabel != null) btnLabel.text = PlayLabel;
                        btn?.onClick.AddListener(() => ToggleAttachmentAudio(clip, btnLabel));
                    }
                    break;
                }
                case ClueAttachmentKind.MapRef:
                {
                    var node = ClueAttachmentService.ResolveMapNode(a);
                    missing = node == null;
                    showBtn = !missing;
                    if (showBtn)
                    {
                        if (btnLabel != null) btnLabel.text = "지도";
                        string guid = a.mapGuid;
                        btn?.onClick.AddListener(() => GoToMap(guid));
                    }
                    break;
                }
            }

            if (labelTmp != null)
            {
                string kindTag = ClueAttachmentConfig.GetDisplayName(a.kind);
                labelTmp.text = missing
                    ? $"[{kindTag}] {label} <color=#C86A6A>(파일 없음)</color>"
                    : $"[{kindTag}] {label}";
            }

            if (iconImg != null)
            {
                iconImg.gameObject.SetActive(icon != null);
                if (icon != null)
                {
                    iconImg.sprite = icon;
                    iconImg.color = Color.white;
                }
            }
            btnTF?.gameObject.SetActive(showBtn);
            previewTF?.gameObject.SetActive(showPreview);
        }

        private const string PlayLabel = "▶";
        private const string StopLabel = "■";

        private void ToggleAttachmentAudio(AudioClip clip, TextMeshProUGUI btnLabel)
        {
            if (_audio == null) _audio = ClueAttachmentAudioPlayer.AttachTo(gameObject);
            _audio.Toggle(clip, playing =>
            {
                if (btnLabel != null) btnLabel.text = playing ? StopLabel : PlayLabel;
            });
        }

        // 맵 첨부의 "지도" 버튼 — 노트를 닫고 지도를 열어 그 맵으로 시점을 옮긴다.
        // 도감 카드(CodexPanel.HandleMapRefClicked)와 같은 동작이고, 패널을 직접 참조하지 않고
        // 씬에서 찾는 것도 MapViewer.GoToNote/GoToCodex와 같은 기존 패턴 그대로다.
        private void GoToMap(string mapGuid)
        {
            if (string.IsNullOrEmpty(mapGuid)) return;

            var mapViewer = FindObjectOfType<MapView.MapViewer>();
            if (mapViewer == null)
            {
                Debug.LogWarning("[NoteRouteGraphView] 씬에서 MapViewer를 찾을 수 없습니다.");
                return;
            }
            FindObjectOfType<NotePanel>()?.Close();
            mapViewer.OpenFocusedOn(mapGuid);
        }

        private GameObject BuildAttachmentTemplate()
        {
            var rt = NewRect(null, "AttachmentNode");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            // 코멘트 노드와 같은 이유로 배경/텍스트의 레이캐스트를 끈다 — 다만 재생·지도 버튼은 눌려야
            // 하므로 버튼 쪽 Image만 raycastTarget을 켜 둔다(아래).
            AddImg(rt, _colAttachmentNode).raycastTarget = false;

            var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(3, 3, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            var csf = rt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = NewRect(rt, "Head");
            var headLe = head.gameObject.AddComponent<LayoutElement>();
            headLe.preferredHeight = 10f;
            headLe.flexibleWidth = 1f;
            var hlg = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f;
            hlg.childControlWidth     = true;
            hlg.childControlHeight    = true;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var iconRT = NewRect(head, "Icon");
            var iconLe = iconRT.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 8f;
            iconLe.flexibleWidth = 0f;
            var iconImg = iconRT.gameObject.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            var txtRT = NewRect(head, "Text");
            var txtLe = txtRT.gameObject.AddComponent<LayoutElement>();
            txtLe.flexibleWidth = 1f;
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _attachmentFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;

            var btnRT = NewRect(head, "BtnAction");
            var btnLe = btnRT.gameObject.AddComponent<LayoutElement>();
            btnLe.preferredWidth = 16f;
            btnLe.flexibleWidth = 0f;
            var btnImg = AddImg(btnRT, new Color(0.25f, 0.42f, 0.72f)); // 버튼은 눌려야 하므로 raycastTarget 유지
            var btn = btnRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.transition = Selectable.Transition.None;

            var btnTxtRT = NewRect(btnRT, "Text");
            StretchFull(btnTxtRT);
            var btnTmp = btnTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) btnTmp.font = _font;
            btnTmp.fontSize = _attachmentFontSize;
            btnTmp.alignment = TextAlignmentOptions.Center;
            btnTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            btnTmp.color = Color.white;
            btnTmp.raycastTarget = false;

            var previewRT = NewRect(rt, "Preview");
            var previewLe = previewRT.gameObject.AddComponent<LayoutElement>();
            previewLe.preferredHeight = _attachmentPreviewHeight;
            previewLe.flexibleWidth = 1f;
            var previewImg = previewRT.gameObject.AddComponent<Image>();
            previewImg.preserveAspect = true;
            previewImg.raycastTarget = false;

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // [요청, 2026-07-21] 키워드별로 단서 노드 배경색을 구분 — 키워드 집합이 콘텐츠에 따라 계속
        // 늘어나므로 수동 색상표 대신, 문자열 해시로 색상(Hue)을 결정적으로 뽑아낸다. 같은 키워드는
        // 항상 같은 색이 나오고, 별도 데이터/설정 없이 새 키워드가 추가돼도 바로 동작한다.
        private static Color ColorForKeyword(string keyword)
        {
            unchecked
            {
                int hash = 0;
                foreach (char c in keyword) hash = hash * 31 + c;
                float hue = (hash % 360 + 360) % 360 / 360f;
                return Color.HSVToRGB(hue, 0.45f, 0.75f);
            }
        }

        // ─── 범례(NotePanel 상단) ─────────────────────────────────
        // [신설, 2026-08-17] 노드/간선 색이 무슨 뜻인지 화면에서 바로 알 수 있게, 실제로 그리는 데 쓰는
        // 색 필드를 그대로 넘겨준다 — 범례를 NotePanel에서 색을 따로 적어 만들면 인스펙터에서 색을
        // 바꿨을 때 그래프와 범례가 어긋나므로, 색의 단일 출처는 항상 이 컴포넌트다.
        public struct LegendItem
        {
            public string Label;
            public Color Color;
            public bool IsEdge; // true = 선(가는 막대) 견본, false = 노드(네모) 견본
        }

        public List<LegendItem> GetLegendItems() => new()
        {
            new LegendItem { Label = "맵",              Color = _colNode },
            new LegendItem { Label = "단서 카드",        Color = _colCard },
            // 단서 노드는 고정색이 아니라 "주 키워드(keywords[0]) 문자열 해시 → Hue"로 자동 배색된다
            // (ColorForKeyword). 견본은 키워드가 없을 때 쓰는 기본색이고, 라벨로 그 규칙을 알린다.
            new LegendItem { Label = "단서(색=주 키워드)", Color = _colClueNode },
            new LegendItem { Label = "코멘트",           Color = _colCommentNode },
            new LegendItem { Label = "첨부",             Color = _colAttachmentNode },
            new LegendItem { Label = "연동 선택",         Color = _colLinkModeSelected },

            new LegendItem { Label = "경로",             Color = _colEdge,           IsEdge = true },
            new LegendItem { Label = "맵-단서",          Color = _colClueEdge,       IsEdge = true },
            new LegendItem { Label = "키워드 자동",       Color = _colKeywordEdge,    IsEdge = true },
            new LegendItem { Label = "직접 연동",         Color = _colManualLinkEdge, IsEdge = true },
        };

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

    // NodeBox/단서 노드 드래그 전용 — Group(부모)의 anchoredPosition을 옮기고, 옮길 때마다 콜백으로
    // 알려 간선을 다시 그리게 한다. GraphPanZoom과 동일한 이유로 ScreenPointToLocalPointInRectangle을
    // 써서 카메라/캔버스 렌더모드와 무관하게 정확한 좌표 변환을 보장한다(단순 delta/scaleFactor는
    // 이 프로젝트의 카메라 설정에서 오차가 났던 전례가 있음).
    // [버그 수정, 2026-07-21] 원래 NoteRouteGraphView 안의 private 중첩 클래스였는데, 유니티가 private
    // 중첩 MonoBehaviour를 프리팹에 제대로 직렬화하지 못해("The referenced script... is missing!")
    // 노트에 실제 노드가 떠 있는 상태로 "NotePanel 프리팹 생성"을 누르면 저장이 막히는 문제가 있을 수
    // 있었다(Codex의 KeywordsValue와 동일한 유형 — CLAUDE.md 참고) — 최상위 public 클래스로 분리해 예방.
    public class NoteNodeDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Group;
        public RectTransform Bounds;   // 좌표 변환 기준 + 드래그 가능 범위(= GraphArea)
        public float RightMargin;      // Bounds 오른쪽에서 남겨둘 여유(이 안으로만 드래그 허용)
        public Action OnMoved;
        // [신설, 2026-08-15] 다중 선택 지원 — 드래그 시작 시 "이번에 같이 움직여야 할 노드"를 뷰에 물어본다.
        // 비워두면(null) 예전처럼 자기 Group 하나만 움직인다.
        public Func<RectTransform, List<NoteDragTarget>> ResolveTargets;
        // [요청, 2026-07-21] 노드를 드래그하는 동안 배경 팬(GraphPanZoom)이 동시에 움직이는 문제 방지용 —
        // 드래그 시작~종료 구간에만 배경 패닝을 잠근다. null이면(팬·줌 없이 쓰는 그래프) 그냥 무시된다.
        public GraphPanZoom PanZoom;

        private Camera _worldCam;
        private Vector2 _dragStartLocal;

        // 이번 드래그로 같이 움직이는 노드들과 각자의 시작 위치 — 단일 선택이면 원소가 하나뿐이라
        // 예전 동작과 완전히 같다.
        private readonly List<NoteDragTarget> _targets = new();
        private readonly List<Vector2> _startAnchors = new();

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

            _targets.Clear();
            _startAnchors.Clear();
            var resolved = ResolveTargets?.Invoke(Group);
            if (resolved != null && resolved.Count > 0) _targets.AddRange(resolved);
            else _targets.Add(new NoteDragTarget { Group = Group, RightMargin = RightMargin, MinY = float.NegativeInfinity });
            foreach (var t in _targets) _startAnchors.Add(t.Group != null ? t.Group.anchoredPosition : Vector2.zero);

            if (PanZoom != null)
            {
                PanZoom.SuppressDrag = true;
                PanZoom.LockPosition(); // 이중 안전장치 — GraphPanZoom.LockPosition 주석 참고
            }
        }

        public void OnDrag(PointerEventData e)
        {
            if (Group == null || Bounds == null || _targets.Count == 0) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Bounds, e.position, _worldCam, out Vector2 cur);
            Vector2 delta = cur - _dragStartLocal;

            // 여러 개를 같이 옮길 때는 노드마다 따로 클램프하면 서로의 상대 위치가 뭉개진다 — 대신
            // "모든 노드가 허용 범위 안에 남는 델타"로 한 번만 제한해 무리 전체가 같은 만큼 움직이게 한다.
            float dxMin = float.NegativeInfinity, dxMax = float.PositiveInfinity;
            float dyMin = float.NegativeInfinity, dyMax = float.PositiveInfinity;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Group == null) continue;
                var start = _startAnchors[i];
                float maxX = Mathf.Max(0f, Bounds.rect.width - _targets[i].RightMargin);
                dxMin = Mathf.Max(dxMin, 0f - start.x);
                dxMax = Mathf.Min(dxMax, maxX - start.x);
                dyMax = Mathf.Min(dyMax, 0f - start.y);           // 그래프 영역 위 경계(0) 밖으로는 못 나가게
                dyMin = Mathf.Max(dyMin, _targets[i].MinY - start.y); // 작업 공간 아래로도 못 빠져나가게
            }
            if (dxMin > dxMax) dxMax = dxMin;
            if (dyMin > dyMax) dyMin = dyMax;
            delta.x = Mathf.Clamp(delta.x, dxMin, dxMax);
            delta.y = Mathf.Clamp(delta.y, dyMin, dyMax);

            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Group == null) continue;
                _targets[i].Group.anchoredPosition = _startAnchors[i] + delta;
            }
            OnMoved?.Invoke();
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (PanZoom != null)
            {
                PanZoom.SuppressDrag = false;
                PanZoom.UnlockPosition();
            }
        }
    }

    // 카드/단서 노드 클릭 전용 — 드래그(NoteNodeDragHandle)와 별개 인터페이스라 같은 오브젝트에 붙여도
    // 컴포넌트 자체는 간섭하지 않지만, "누른 대상"과 "드래그 대상"이 같은 오브젝트일 때는 uGUI가
    // 드래그가 있었다고 해서 클릭을 자동으로 무효화해주지 않는다(그 무효화 로직은 "누른 대상 != 드래그
    // 대상"일 때만 동작함) — 펼쳐진 단서 노드처럼 두 컴포넌트가 한 오브젝트에 같이 붙는 경우, 아래
    // OnPointerClick에서 e.dragging을 직접 확인해 걸러줘야 한다(아래 두 번째 주석 참고). NoteNodeDragHandle과
    // 같은 이유로 최상위 public 클래스다.
    //
    // [버그 수정, 2026-07-21 (1)] IPointerDownHandler를 반드시 같이 구현해야 한다 — 안 그러면 EventSystem이
    // "누른(press) 대상"을 찾을 때 이 오브젝트엔 IPointerDownHandler가 없어 부모를 계속 타고 올라가다
    // 결국 GraphScroll의 GraphPanZoom(배경 팬용으로 빈 IPointerDownHandler를 갖고 있음)까지 도달해
    // 그쪽을 pointerPress로 캡처해버린다. 그러면 뗄 때(release) 찾는 클릭 대상(이 카드/노드 자신)과
    // 눌렀을 때 캡처된 대상(배경)이 서로 달라 pointerClickHandler가 아예 실행되지 않는다 — 삭제 버튼
    // 등 Button 컴포넌트는 IPointerDownHandler를 자체 구현해서 이 문제가 없었다(GraphPanZoom/
    // NoteNodeDragHandle의 빈 OnPointerDown과 정확히 같은 이유·해결책).
    //
    // [버그 수정, 2026-07-21 (2)] 펼쳐진 노드는 드래그와 이 클릭 토글이 같은 오브젝트에 같이 붙어있어서
    // (NoteNodeDragHandle+NoteClickToToggle, GetOrCreateClueVisual 참고), "누른 대상 == 드래그 대상"인
    // 경우가 되어 (1)에서 설명한 uGUI의 드래그→클릭 무효화 로직이 동작하지 않는다 — 드래그해서 노드를
    // 옮긴 뒤 손을 떼는 순간 OnPointerClick도 같이 발동해 그 노드가 접혀버렸다. 더 심각한 건, 이 클릭이
    // OnEndDrag보다 먼저 실행되는데(uGUI 이벤트 순서: PointerUp → Click → EndDrag) 클릭 핸들러가
    // SetData()를 불러 이 노드(드래그 중이던 바로 그 오브젝트)를 SetActive(false)로 꺼버리므로,
    // 뒤이어 실행돼야 할 OnEndDrag 자체가 통째로 호출되지 않아 GraphPanZoom.UnlockPosition()이 영원히
    // 안 불리고 배경 팬·줌이 완전히 멈춰버리는 사고로 이어졌다 — 사용자 제안대로, 드래그가 있었던
    // 클릭(e.dragging)은 아예 토글하지 않는 것으로 근본 차단한다(GraphPanZoom.LateUpdate의 방어적
    // 자동 해제도 같이 추가 — 혹시 비슷한 경우가 또 생겨도 마우스를 놓으면 무조건 풀리게).
    public class NoteClickToToggle : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public Action OnClick;
        // [신설, 2026-08-15] Ctrl+클릭 — 다중 선택 토글. 배선돼 있으면 Ctrl을 누른 클릭은 OnClick(접기/
        // 펼치기, 링크 모드 선택)까지 가지 않고 여기서 끝난다.
        public Action OnCtrlClick;

        public void OnPointerDown(PointerEventData e) { }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return; // 드래그를 동반한 클릭은 토글하지 않는다 — 위 (2) 참고
            if (OnCtrlClick != null && NoteRouteGraphView.IsCtrlHeld())
            {
                OnCtrlClick();
                return;
            }
            OnClick?.Invoke();
        }
    }

    // 드래그로 같이 움직일 노드 하나 — NoteRouteGraphView가 선택 상태를 보고 채워 NoteNodeDragHandle에
    // 넘긴다. 각 노드마다 폭(RightMargin)과 아래 한계(MinY)가 달라서 위치만으로는 클램프할 수 없다.
    public struct NoteDragTarget
    {
        public RectTransform Group;
        public float RightMargin;
        public float MinY;
    }
}
