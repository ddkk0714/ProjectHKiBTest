using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using RouteFinding.Codex;

namespace RouteFinding.Note
{
    // 노트 우측 — "단서 서랍". [2026-07-21, 요청으로 신설] 다중 목적지 이동 계획(RoutePlanEditorView)을
    // 완전히 대체한다.
    //
    // 현재 획득한 모든 단서(CodexModule.AcquiredClues — 도감과 같은 소스, "현재 얻은 단서들")를 검색·
    // 키워드 필터로 훑어보고, 원하는 걸 드래그해서 좌측 단서 그래프(NoteRouteGraphView)에 놓을 수 있다.
    // 이미 그래프에 있거나 노트에 핀돼 있는지와 무관하게 항상 전부 보여준다 — 진짜 서랍처럼, 한 번
    // 꺼내둔 것도 다시 꺼내(재배치) 쓸 수 있다.
    //
    // 검색/키워드 추출은 Codex와 공유하기로 확정된 CodexFilterService/CodexEntry를 그대로 재사용한다
    // (NoteSystem_기획서.md — Note·Codex는 데이터·서비스 레이어만 공유, 모듈·UI는 분리).
    //
    // RoutePlanEditorView와 같은 패턴: Init()만 있고 Bind()가 없다 — 이 뷰의 내용은 Refresh()마다
    // 통째로 다시 그려지는 휘발성 데이터라, 프리팹 재사용 시에도 Init()을 다시 호출해 항상 빈 상태에서
    // 새로 짓는다(NotePanel 참고).
    public class ClueDrawerView : MonoBehaviour
    {
        // (clueId, screenPosition) — 드래그가 그래프 영역 위에서 끝났는지 판단하고 실제로 배치하는 건
        // NoteRouteGraphView.PlaceClueAt의 책임이다. 이 뷰는 "여기서 드래그가 끝났다"는 사실만 전달한다.
        public event Action<string, Vector2> OnClueDropped;

        // [2026-07-21, 요청으로 교체] 검색창 옆 "필터" 버튼 클릭 — 실제 창(ClueKeywordFilterWindow)은
        // NotePanel이 소유(NoteBoardWindow와 같은 이유, ClueKeywordFilterWindow.cs 상단 주석 참고)하므로
        // 여기서는 클릭 사실만 이벤트로 알린다. 필터 상태(_activeKeywords) 자체는 이 뷰가 계속 소유하고
        // Refresh()의 검색 결과 필터링에 그대로 쓴다 — 창은 순수 입력 UI일 뿐이다.
        public event Action OnFilterButtonClicked;

        private RectTransform _content;
        private TMP_FontAsset _font;
        private Canvas _canvas;
        private readonly List<GameObject> _miscSpawned = new();
        private readonly List<ClueData> _acquired = new();
        private List<CodexEntry> _lastAllEntries = new();

        private string _searchQuery = "";
        // 기존 "전체"/키워드 하나만 고르던 배타적 단일 선택을 다중 선택(체크박스, OR 조건)으로 교체.
        private readonly HashSet<string> _activeKeywords = new(StringComparer.OrdinalIgnoreCase);
        private TMP_InputField _searchField;
        private TextMeshProUGUI _filterBtnLabel;

        [Header("행 템플릿 (선택 — 비워두면 아래 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _clueRowTemplate;

        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colClueRow          = new(0.10f, 0.12f, 0.17f);
        [SerializeField] private float _rowFontSize         = 7f;

        private UiRowPool _clueRowPool;

        // NotePanel이 필터 창을 열 때(ComputeAllKeywords) / 상태를 동기화할 때(ActiveKeywords) 읽는다.
        public IReadOnlyCollection<string> ActiveKeywords => _activeKeywords;

        public void Init(RectTransform scrollContent, TMP_FontAsset font)
        {
            _content = scrollContent;
            _font = font;
            _canvas = scrollContent.GetComponentInParent<Canvas>();

            // NoteRouteGraphView.Init/(구)RoutePlanEditorView.Init과 동일한 이유 — 프리팹을 재사용/
            // 재인스턴스화하면 저장 시점에 이미 있던 행 클론이 Content에 그대로 남아있을 수 있는데,
            // 이 컴포넌트의 런타임 추적 상태(UiRowPool 내부 풀 목록)는 직렬화되지 않아 그 baked-in
            // 자식들의 존재를 전혀 모른 채 또 새로 만들어 중복이 생긴다. Init 시점에 Content의 기존
            // 자식을 전부 정리하고 항상 빈 상태에서 다시 짓는다.
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _clueRowPool = new UiRowPool(_clueRowTemplate, BuildClueRowTemplate);

            BuildSearchRow();
        }

        public void Refresh()
        {
            ClearMisc();

            // [2026-07-21] 획득한 실제 단서(ClueData)뿐 아니라, 노트에서 유저가 직접 만든 단서
            // (CodexUserEntry — "생성" 버튼, NotePanel.HandleClueCreateRequested)도 서랍에 같이 나열한다 —
            // 그래야 만든 걸 다시 꺼내 그래프에 배치하거나 검색할 수 있다.
            _acquired.Clear();
            if (CodexModule.Instance != null) _acquired.AddRange(CodexModule.Instance.AcquiredClues);
            _lastAllEntries = _acquired.Select(ToEntry).ToList();
            if (CodexModule.Instance != null)
                _lastAllEntries.AddRange(CodexModule.Instance.UserEntries.Select(ToEntry));

            RefreshFilterButtonLabel();
            BuildSeparator();

            var searched = CodexFilterService.Search(_lastAllEntries, _searchQuery);
            var filtered = _activeKeywords.Count == 0
                ? searched
                : searched.Where(HasAnyActiveKeyword).ToList();

            if (filtered.Count == 0)
            {
                BuildEmptyHint();
            }
            else
            {
                foreach (var e in filtered)
                    PopulateClueRow(_clueRowPool.Get(_content), e);
            }
            _clueRowPool.EndPass();
        }

        private bool HasAnyActiveKeyword(CodexEntry entry) =>
            entry.keywords != null && entry.keywords.Any(k => _activeKeywords.Contains(k.Trim()));

        // NotePanel이 ClueKeywordFilterWindow에 넘길 전체 키워드 목록(가나다순)을 뽑을 때 호출.
        public List<string> ComputeAllKeywords()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _lastAllEntries)
            {
                if (e.keywords == null) continue;
                foreach (var kw in e.keywords)
                    if (!string.IsNullOrWhiteSpace(kw)) seen.Add(kw.Trim());
            }
            return seen.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ClueKeywordFilterWindow.OnKeywordToggled를 받은 NotePanel이 호출 — 체크 상태를 뒤집고
        // 검색 결과를 다시 그린다.
        public void ToggleKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return;
            if (!_activeKeywords.Remove(keyword)) _activeKeywords.Add(keyword);
            Refresh();
        }

        // ClueKeywordFilterWindow.OnClearAllRequested를 받은 NotePanel이 호출.
        public void ClearActiveKeywords()
        {
            if (_activeKeywords.Count == 0) return;
            _activeKeywords.Clear();
            Refresh();
        }

        private void RefreshFilterButtonLabel()
        {
            if (_filterBtnLabel == null) return;
            _filterBtnLabel.text = _activeKeywords.Count == 0 ? "필터" : $"필터({_activeKeywords.Count})";
        }

        private static CodexEntry ToEntry(ClueData clue) => new()
        {
            title   = clue.name,
            content = string.IsNullOrEmpty(clue.content) ? clue.description : clue.content,
            keywords = clue.keywords,
            clueId  = clue.id,
        };

        // [신설, 2026-07-21] 유저가 노트에서 직접 만든 단서 — clueId 자리에 CodexUserEntry.guid를 그대로
        // 써서, 드래그로 그래프에 놓을 때(NoteRouteGraphView.PlaceClueAt) 실제 ClueData와 동일하게
        // "clueId 문자열 하나"로 다뤄진다(NoteClueResolver가 조회 시점에 어느 소스인지 알아서 구분).
        private static CodexEntry ToEntry(CodexUserEntry entry) => new()
        {
            title   = entry.title,
            content = entry.content,
            keywords = entry.keywords,
            clueId  = entry.guid,
        };

        private void ClearMisc()
        {
            // 다른 뷰들과 동일한 이유(CodexDrawerTreeView.Clear 참고) — 활성 상태로 그냥 Destroy하면
            // 같은 프레임 리빌드가 파괴 중인 TMP 서브메시에 접근해 예외를 던질 수 있다.
            foreach (var go in _miscSpawned)
            {
                go.SetActive(false);
                Destroy(go);
            }
            _miscSpawned.Clear();
        }

        // ─── 검색창 + 필터 버튼 (고정, Init에서 1회만 생성) ─────────

        private void BuildSearchRow()
        {
            var row = NewRect(_content, "SearchRow");
            var rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 12f;
            rowLe.flexibleWidth = 1f;
            var rowHlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowHlg.spacing = 3f;
            rowHlg.childControlWidth = true;
            rowHlg.childControlHeight = true;
            rowHlg.childForceExpandWidth = false;
            rowHlg.childForceExpandHeight = true;

            var rt = NewRect(row, "SearchInput");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            AddImg(rt, new Color(0.03f, 0.04f, 0.06f));

            var textAreaRT = NewRect(rt, "TextArea");
            StretchFull(textAreaRT);
            textAreaRT.offsetMin = new Vector2(4f, 1f);
            textAreaRT.offsetMax = new Vector2(-4f, -1f);
            textAreaRT.gameObject.AddComponent<RectMask2D>();

            var textRT = NewRect(textAreaRT, "Text");
            StretchFull(textRT);
            var textTmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) textTmp.font = _font;
            textTmp.fontSize = 7f;
            textTmp.color = Color.white;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholderRT = NewRect(textAreaRT, "Placeholder");
            StretchFull(placeholderRT);
            var placeholderTmp = placeholderRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) placeholderTmp.font = _font;
            placeholderTmp.text = "단서 검색...";
            placeholderTmp.fontSize = 7f;
            placeholderTmp.fontStyle = FontStyles.Italic;
            placeholderTmp.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;

            _searchField = rt.gameObject.AddComponent<TMP_InputField>();
            _searchField.textViewport = textAreaRT;
            _searchField.textComponent = textTmp;
            _searchField.placeholder = placeholderTmp;
            _searchField.onValueChanged.AddListener(v => { _searchQuery = v; Refresh(); });

            var filterBtnRT = NewRect(row, "BtnFilter");
            var filterLe = filterBtnRT.gameObject.AddComponent<LayoutElement>();
            filterLe.preferredWidth = 34f;
            filterLe.flexibleWidth = 0f;
            var filterImg = AddImg(filterBtnRT, new Color(0.20f, 0.28f, 0.42f));
            var filterBtn = filterBtnRT.gameObject.AddComponent<Button>();
            filterBtn.targetGraphic = filterImg;
            filterBtn.transition = Selectable.Transition.None;
            filterBtn.onClick.AddListener(() => OnFilterButtonClicked?.Invoke());

            var filterTxtRT = NewRect(filterBtnRT, "Text");
            StretchFull(filterTxtRT);
            _filterBtnLabel = filterTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) _filterBtnLabel.font = _font;
            _filterBtnLabel.text = "필터";
            _filterBtnLabel.fontSize = 6.5f;
            _filterBtnLabel.alignment = TextAlignmentOptions.Center;
            _filterBtnLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            _filterBtnLabel.color = Color.white;
        }

        private void BuildSeparator()
        {
            var rt = NewRect(_content, "Sep");
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            AddImg(rt, new Color(1f, 1f, 1f, 0.10f));
            _miscSpawned.Add(rt.gameObject);
        }

        // ─── 단서 행 (풀링, 드래그 가능) ──────────────────────────

        private void PopulateClueRow(GameObject rowGO, CodexEntry entry)
        {
            var tmp = rowGO.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = entry.title;

            var drag = rowGO.GetComponent<NoteClueDragHandle>();
            if (drag == null) drag = rowGO.AddComponent<NoteClueDragHandle>();
            drag.ClueId = entry.clueId;
            drag.Label = entry.title;
            drag.GhostParent = _canvas != null ? (RectTransform)_canvas.transform : _content;
            drag.Font = _font;
            drag.OnDropped = (clueId, screenPos) => OnClueDropped?.Invoke(clueId, screenPos);
        }

        private GameObject BuildClueRowTemplate()
        {
            var rt = NewRect(null, "ClueRow");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            AddImg(rt, _colClueRow);

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin = new Vector4(4f, 0f, 2f, 0f);
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            rt.gameObject.AddComponent<NoteClueDragHandle>();

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        private void BuildEmptyHint()
        {
            var hintRT = NewRect(_content, "EmptyHint");
            var le = hintRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.flexibleWidth = 1f;
            var tmp = MakeLabel(hintRT, "Text",
                string.IsNullOrEmpty(_searchQuery) && _activeKeywords.Count == 0
                    ? "아직 획득한 단서가 없습니다."
                    : "조건에 맞는 단서가 없습니다.",
                7f, new Color(0.7f, 0.7f, 0.7f));
            tmp.alignment = TextAlignmentOptions.Center;
            _miscSpawned.Add(hintRT.gameObject);
        }

        // ─── UI 헬퍼 ─────────────────────────────────────────────

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

    // 서랍 행을 드래그해 좌측 그래프에 놓는 전용 핸들러 — NoteNodeDragHandle(그래프 노드 재배치용)과는
    // 별개 클래스다: 이쪽은 원본 자리에서 옮기는 게 아니라 "드래그 중엔 별도 고스트가 커서를 따라가고,
    // 놓은 지점의 스크린 좌표만 알려주는" 역할이라 목적이 다르다.
    // 최상위 public 클래스인 이유: NoteNodeDragHandle/NoteClickToToggle(NoteRouteGraphView.cs)과 동일 —
    // private 중첩 MonoBehaviour는 유니티가 프리팹에 제대로 직렬화하지 못해 "missing script"로 프리팹
    // 저장이 막히는 문제가 있다(Codex의 KeywordsValue 사고, CLAUDE.md 참고) — 처음부터 이 문제를 피했다.
    public class NoteClueDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public string ClueId;
        public string Label;
        public RectTransform GhostParent; // 최상위 캔버스 — 드래그 중 다른 UI 위로 지나가도 잘리지 않게
        public TMP_FontAsset Font;
        public Action<string, Vector2> OnDropped; // (clueId, 놓은 지점의 스크린 좌표)

        private RectTransform _ghost;

        public void OnBeginDrag(PointerEventData e)
        {
            if (GhostParent == null) return;

            var go = new GameObject("ClueDragGhost");
            go.transform.SetParent(GhostParent, false);
            _ghost = go.AddComponent<RectTransform>();
            _ghost.sizeDelta = new Vector2(90f, 14f);
            _ghost.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.43f, 0.78f, 0.85f);
            img.raycastTarget = false; // 드롭 대상 판정(그래프 영역 히트 테스트)을 가리면 안 된다

            var txtGO = new GameObject("Text");
            txtGO.transform.SetParent(go.transform, false);
            var txtRT = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;
            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            if (Font != null) tmp.font = Font;
            tmp.text = Label;
            tmp.fontSize = 7f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            _ghost.SetAsLastSibling();
            UpdateGhostPosition(e);
        }

        public void OnDrag(PointerEventData e) => UpdateGhostPosition(e);

        private void UpdateGhostPosition(PointerEventData e)
        {
            if (_ghost == null || GhostParent == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(GhostParent, e.position, e.pressEventCamera, out var local))
                _ghost.anchoredPosition = local;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_ghost != null)
            {
                Destroy(_ghost.gameObject);
                _ghost = null;
            }
            OnDropped?.Invoke(ClueId, e.position);
        }
    }
}
