using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Codex
{
    // 분류 기준 — 3개 중 하나만 선택되는 라디오 형태.
    public enum CodexFilterMode { ByMap, BySource, ByKeyword }

    // 도감 좌측 서랍 최상단 — 검색창 + 분류 기준(맵/출처/키워드) 전환 버튼.
    // 실제 필터링/그룹핑 로직은 CodexFilterService(무상태)가 담당하고, 이 클래스는 입력만 받아 이벤트로 알린다.
    public class CodexSearchBar : MonoBehaviour
    {
        public event Action<string> OnSearchChanged;
        public event Action<CodexFilterMode> OnFilterModeChanged;

        private Image _btnMapImg;
        private Image _btnSourceImg;
        private Image _btnKeywordImg;
        private CodexFilterMode _mode = CodexFilterMode.ByMap;

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

            BuildSearchInput(parent, font);
            BuildFilterButtons(parent, font);
        }

        // 프리팹 재사용 시 호출 — Init()으로 새로 만들지 않고, 이미 존재하는 자식을 이름으로 재탐색해
        // 참조를 복원하고 onValueChanged/onClick 콜백을 다시 연결한다(Instantiate로는 보존되지 않음).
        public void Bind(RectTransform existingRoot)
        {
            var searchField = FindDeepTransform(existingRoot, "SearchInput")?.GetComponent<TMP_InputField>();
            searchField?.onValueChanged.AddListener(v => OnSearchChanged?.Invoke(v));

            var mapBtnTF = FindDeepTransform(existingRoot, "Btn_맵");
            _btnMapImg = mapBtnTF?.GetComponent<Image>();
            mapBtnTF?.GetComponent<Button>()?.onClick.AddListener(() => SetMode(CodexFilterMode.ByMap));

            var sourceBtnTF = FindDeepTransform(existingRoot, "Btn_출처");
            _btnSourceImg = sourceBtnTF?.GetComponent<Image>();
            sourceBtnTF?.GetComponent<Button>()?.onClick.AddListener(() => SetMode(CodexFilterMode.BySource));

            var keywordBtnTF = FindDeepTransform(existingRoot, "Btn_키워드");
            _btnKeywordImg = keywordBtnTF?.GetComponent<Image>();
            keywordBtnTF?.GetComponent<Button>()?.onClick.AddListener(() => SetMode(CodexFilterMode.ByKeyword));

            RefreshButtons();
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

            RefreshButtons();
        }

        private void SetMode(CodexFilterMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            RefreshButtons();
            OnFilterModeChanged?.Invoke(_mode);
        }

        private void RefreshButtons()
        {
            _btnMapImg.color     = _mode == CodexFilterMode.ByMap     ? _btnActive : _btnInactive;
            _btnSourceImg.color  = _mode == CodexFilterMode.BySource  ? _btnActive : _btnInactive;
            _btnKeywordImg.color = _mode == CodexFilterMode.ByKeyword ? _btnActive : _btnInactive;
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
