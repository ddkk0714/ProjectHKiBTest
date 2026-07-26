using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Codex
{
    // 분류 기준 — 3개 중 하나만 선택되는 라디오 형태.
    public enum CodexFilterMode { ByMap, BySource, ByKeyword }

    // 그룹 내부 항목 정렬 기준 — 3개 중 하나만 선택되는 라디오 형태 (Clue_System.md 6-5).
    public enum CodexSortOrder { Alphabetical, ByType, RecentlyAcquired }

    // 도감 좌측 서랍 최상단 — 검색창 + 분류 기준(맵/출처/키워드) 전환 버튼.
    // 실제 필터링/그룹핑 로직은 CodexFilterService(무상태)가 담당하고, 이 클래스는 입력만 받아 이벤트로 알린다.
    public class CodexSearchBar : MonoBehaviour
    {
        public event Action<string> OnSearchChanged;
        public event Action<CodexFilterMode> OnFilterModeChanged;
        public event Action<CodexSortOrder> OnSortOrderChanged;

        private Image _btnMapImg;
        private Image _btnSourceImg;
        private Image _btnKeywordImg;
        private CodexFilterMode _mode = CodexFilterMode.ByMap;

        private Image _btnSortAlphaImg;
        private Image _btnSortTypeImg;
        private Image _btnSortRecentImg;
        private CodexSortOrder _sortOrder = CodexSortOrder.Alphabetical;

        // 6-1단계 — "획득 N / 전체 M" 진행률 표시. CodexPanel.RefreshTree가 호출해 갱신한다.
        private TextMeshProUGUI _progressTmp;

        // RefreshButtons()가 모드 전환마다(Init/Bind/SetMode) 색을 다시 칠하므로, 프리팹에서 버튼의
        // Image 색을 직접 바꿔도 다음 갱신에 곧바로 덮어써진다 — [SerializeField]로 빼야 편집이 유지된다.
        [Header("스타일 (프리팹에서 조정 가능 — RefreshButtons가 매번 다시 칠하므로 여기서만 바꿀 수 있음)")]
        [SerializeField] private Color _btnActive   = new(0.25f, 0.42f, 0.72f);
        [SerializeField] private Color _btnInactive = new(0.17f, 0.21f, 0.30f);

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            var vlg = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 3, 3);
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BuildProgressLabel(parent, font);
            BuildSearchInput(parent, font);
            BuildFilterButtons(parent, font);
            BuildSortButtons(parent, font);
            RefreshButtons(); // 두 행의 버튼이 전부 만들어진 뒤 한 번만 — 그 전엔 sort 버튼 참조가 비어있다
        }

        // 프리팹 재사용 시 호출 — Init()으로 새로 만들지 않고, 이미 존재하는 자식을 이름으로 재탐색해
        // 참조를 복원하고 onValueChanged/onClick 콜백을 다시 연결한다(Instantiate로는 보존되지 않음).
        public void Bind(RectTransform existingRoot)
        {
            var searchField = FindDeepTransform(existingRoot, "SearchInput")?.GetComponent<TMP_InputField>();
            searchField?.onValueChanged.AddListener(v => OnSearchChanged?.Invoke(v));

            _progressTmp = FindDeepTransform(existingRoot, "ProgressLabel")?.GetComponent<TextMeshProUGUI>();

            var mapBtnTF = FindDeepTransform(existingRoot, "Btn_맵");
            _btnMapImg = mapBtnTF?.GetComponent<Image>();
            mapBtnTF?.GetComponent<Button>()?.onClick.AddListener(() => SetMode(CodexFilterMode.ByMap));

            var sourceBtnTF = FindDeepTransform(existingRoot, "Btn_출처");
            _btnSourceImg = sourceBtnTF?.GetComponent<Image>();
            sourceBtnTF?.GetComponent<Button>()?.onClick.AddListener(() => SetMode(CodexFilterMode.BySource));

            var keywordBtnTF = FindDeepTransform(existingRoot, "Btn_키워드");
            _btnKeywordImg = keywordBtnTF?.GetComponent<Image>();
            keywordBtnTF?.GetComponent<Button>()?.onClick.AddListener(() => SetMode(CodexFilterMode.ByKeyword));

            var sortAlphaTF = FindDeepTransform(existingRoot, "Btn_가나다순");
            _btnSortAlphaImg = sortAlphaTF?.GetComponent<Image>();
            sortAlphaTF?.GetComponent<Button>()?.onClick.AddListener(() => SetSortOrder(CodexSortOrder.Alphabetical));

            var sortTypeTF = FindDeepTransform(existingRoot, "Btn_타입순");
            _btnSortTypeImg = sortTypeTF?.GetComponent<Image>();
            sortTypeTF?.GetComponent<Button>()?.onClick.AddListener(() => SetSortOrder(CodexSortOrder.ByType));

            var sortRecentTF = FindDeepTransform(existingRoot, "Btn_최신순");
            _btnSortRecentImg = sortRecentTF?.GetComponent<Image>();
            sortRecentTF?.GetComponent<Button>()?.onClick.AddListener(() => SetSortOrder(CodexSortOrder.RecentlyAcquired));

            RefreshButtons();
        }

        // 6-1단계 — 드로어 최상단 진행률 표시. CodexPanel.RefreshTree가 갱신 시점마다 호출한다.
        private void BuildProgressLabel(RectTransform parent, TMP_FontAsset font)
        {
            var rt = NewRect(parent, "ProgressLabel");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 10f;
            le.flexibleWidth = 1f;
            _progressTmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) _progressTmp.font = font;
            _progressTmp.fontSize = 7f;
            _progressTmp.alignment = TextAlignmentOptions.MidlineLeft;
            _progressTmp.color = new Color(0.7f, 0.75f, 0.8f);
        }

        // "획득 N / 전체 M" — M은 정식 단서 전체 수(유저 메모 제외)만 센다.
        public void SetProgress(int acquired, int total)
        {
            if (_progressTmp != null) _progressTmp.text = $"획득 {acquired} / 전체 {total}";
        }

        private void BuildSearchInput(RectTransform parent, TMP_FontAsset font)
        {
            var rt = NewRect(parent, "SearchInput");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
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
            if (font != null) textTmp.font = font;
            textTmp.fontSize = 7f;
            textTmp.color = Color.white;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholderRT = NewRect(textAreaRT, "Placeholder");
            StretchFull(placeholderRT);
            var placeholderTmp = placeholderRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) placeholderTmp.font = font;
            placeholderTmp.text = "검색...";
            placeholderTmp.fontSize = 7f;
            placeholderTmp.fontStyle = FontStyles.Italic;
            placeholderTmp.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = textAreaRT;
            field.textComponent = textTmp;
            field.placeholder = placeholderTmp;
            field.onValueChanged.AddListener(v => OnSearchChanged?.Invoke(v));
        }

        private void BuildFilterButtons(RectTransform parent, TMP_FontAsset font)
        {
            var row = NewRect(parent, "FilterRow");
            var rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 12f;
            rowLe.flexibleWidth = 1f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            _btnMapImg     = MakeFilterBtn(row, "맵",     () => SetMode(CodexFilterMode.ByMap), font);
            _btnSourceImg  = MakeFilterBtn(row, "출처",   () => SetMode(CodexFilterMode.BySource), font);
            _btnKeywordImg = MakeFilterBtn(row, "키워드", () => SetMode(CodexFilterMode.ByKeyword), font);
        }

        // 6-5단계 — 그룹 내부 항목 정렬 기준. "드롭다운"으로 적혀 있던 문서 스펙 대신, 이 파일에
        // 이미 있는 분류 버튼(맵/출처/키워드)과 같은 배타적 토글 버튼 UI를 재사용했다 — 새 드롭다운
        // 위젯을 또 만드는 것보다 훨씬 적은 코드로 동일한 기능을 제공하고, 기존 상호작용과 일관된다.
        private void BuildSortButtons(RectTransform parent, TMP_FontAsset font)
        {
            var row = NewRect(parent, "SortRow");
            var rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 12f;
            rowLe.flexibleWidth = 1f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            _btnSortAlphaImg  = MakeFilterBtn(row, "가나다순", () => SetSortOrder(CodexSortOrder.Alphabetical), font);
            _btnSortTypeImg   = MakeFilterBtn(row, "타입순",   () => SetSortOrder(CodexSortOrder.ByType), font);
            _btnSortRecentImg = MakeFilterBtn(row, "최신순",   () => SetSortOrder(CodexSortOrder.RecentlyAcquired), font);
        }

        private void SetMode(CodexFilterMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            RefreshButtons();
            OnFilterModeChanged?.Invoke(_mode);
        }

        // 6-4단계(키워드 태그 클릭) 전용 — 카드에서 키워드 태그를 클릭했을 때 CodexPanel이 호출한다.
        // SetMode는 private으로 유지하고(버튼 클릭 경로와 완전히 같은 코드를 타게 하려는 목적) 이
        // 얇은 공개 래퍼만 추가했다.
        public void SetFilterModeExternally(CodexFilterMode mode) => SetMode(mode);

        private void SetSortOrder(CodexSortOrder order)
        {
            if (_sortOrder == order) return;
            _sortOrder = order;
            RefreshButtons();
            OnSortOrderChanged?.Invoke(_sortOrder);
        }

        private void RefreshButtons()
        {
            _btnMapImg.color     = _mode == CodexFilterMode.ByMap     ? _btnActive : _btnInactive;
            _btnSourceImg.color  = _mode == CodexFilterMode.BySource  ? _btnActive : _btnInactive;
            _btnKeywordImg.color = _mode == CodexFilterMode.ByKeyword ? _btnActive : _btnInactive;

            _btnSortAlphaImg.color  = _sortOrder == CodexSortOrder.Alphabetical     ? _btnActive : _btnInactive;
            _btnSortTypeImg.color   = _sortOrder == CodexSortOrder.ByType           ? _btnActive : _btnInactive;
            _btnSortRecentImg.color = _sortOrder == CodexSortOrder.RecentlyAcquired ? _btnActive : _btnInactive;
        }

        private Image MakeFilterBtn(RectTransform parent, string label, Action onClick, TMP_FontAsset font)
        {
            var rt = NewRect(parent, "Btn_" + label);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            var img = AddImg(rt, _btnInactive);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 7f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;

            return img;
        }

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
