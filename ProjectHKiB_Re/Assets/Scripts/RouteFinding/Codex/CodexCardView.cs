using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Codex
{
    // 도감 우측 상세 카드 — 타입 배지/시간/내용/출처/맵/키워드를 표시한다.
    // 내용이 넘칠 수 있어(특히 4단계 코멘트 추가 이후) ScrollRect로 감싸 드래그/휠 스크롤을 지원한다.
    // 3단계: 유저 메모(CodexEntry.userEntryGuid가 있는 항목)에는 편집/삭제 버튼을 추가로 보여준다.
    // 코멘트 목록·타이프라이터 연출은 4단계에서 추가한다.
    //
    // 프리팹 재사용(CodexPanel 참고): Init()은 처음 생성할 때만 호출되고, 프리팹을 재사용할 때는
    // Bind()가 이름으로 기존 자식을 재탐색해 참조를 복원하고 버튼 onClick을 다시 연결한다
    // (Instantiate는 private 필드와 런타임에 AddListener한 콜백을 보존하지 않기 때문).
    public class CodexCardView : MonoBehaviour
    {
        public event Action<CodexEntry> OnEditRequested;
        public event Action<CodexEntry> OnDeleteRequested;

        private TextMeshProUGUI _titleTmp;
        private GameObject _typeBadgeGO;
        private TextMeshProUGUI _typeBadgeTmp;
        private TextMeshProUGUI _timestampTmp;
        private TextMeshProUGUI _contentTmp;
        private TextMeshProUGUI _sourceTmp;
        private TextMeshProUGUI _mapTmp;
        private TextMeshProUGUI _keywordsTmp;
        private GameObject _editRowGO;

        private CodexEntry _currentEntry;

        private static readonly Color Gray = new(0.55f, 0.60f, 0.65f);
        private static readonly Color BadgeColor = new(0.25f, 0.42f, 0.72f);

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            var content = BuildScrollContent(parent);

            var headerRow = NewRect(content, "HeaderRow");
            var headerLe = headerRow.gameObject.AddComponent<LayoutElement>();
            headerLe.preferredHeight = 22f;
            headerLe.flexibleWidth = 1f;
            var hlg = headerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            _titleTmp = MakeTMP(headerRow, font, "", 13f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, id: "TitleLabel");
            _titleTmp.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var badgeRT = NewRect(headerRow, "TypeBadge");
            _typeBadgeGO = badgeRT.gameObject;
            var badgeLe = badgeRT.gameObject.AddComponent<LayoutElement>();
            badgeLe.preferredWidth = 60f;
            badgeLe.flexibleWidth = 0f;
            AddImg(badgeRT, BadgeColor);
            var badgeTxtRT = NewRect(badgeRT, "Text");
            StretchFull(badgeTxtRT);
            _typeBadgeTmp = badgeTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) _typeBadgeTmp.font = font;
            _typeBadgeTmp.fontSize = 8f;
            _typeBadgeTmp.alignment = TextAlignmentOptions.Center;
            _typeBadgeTmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            _typeBadgeTmp.color = Color.white;

            _timestampTmp = MakeTMP(content, font, "", 8f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, id: "TimestampLabel");
            _timestampTmp.color = Gray;

            MakeSep(content);

            _contentTmp = MakeTMP(content, font, "", 9f, FontStyles.Normal, TextAlignmentOptions.TopLeft, height: 80f, id: "ContentLabel");
            _contentTmp.enableWordWrapping = true;

            MakeSep(content);

            MakeLabel(content, font, "출처");
            _sourceTmp = MakeTMP(content, font, "", 9f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, id: "SourceValue");

            MakeLabel(content, font, "맵");
            _mapTmp = MakeTMP(content, font, "", 9f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, id: "MapValue");

            MakeLabel(content, font, "키워드");
            _keywordsTmp = MakeTMP(content, font, "", 9f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, id: "KeywordsValue");
            _keywordsTmp.enableWordWrapping = true;

            BuildEditRow(content, font);

            ShowEmpty();
        }

        // 프리팹 재사용 시 호출 — Init()이 다시 만들지 않고, 이미 존재하는 자식들을 이름으로 재탐색해
        // 참조를 복원하고 버튼 클릭 콜백을 다시 연결한다.
        public void Bind(RectTransform existingRoot)
        {
            _titleTmp = FindDeepChild<TextMeshProUGUI>(existingRoot, "TitleLabel");

            var badgeTF = FindDeepTransform(existingRoot, "TypeBadge");
            _typeBadgeGO = badgeTF?.gameObject;
            _typeBadgeTmp = badgeTF != null ? badgeTF.GetComponentInChildren<TextMeshProUGUI>() : null;

            _timestampTmp = FindDeepChild<TextMeshProUGUI>(existingRoot, "TimestampLabel");
            _contentTmp   = FindDeepChild<TextMeshProUGUI>(existingRoot, "ContentLabel");
            _sourceTmp    = FindDeepChild<TextMeshProUGUI>(existingRoot, "SourceValue");
            _mapTmp       = FindDeepChild<TextMeshProUGUI>(existingRoot, "MapValue");
            _keywordsTmp  = FindDeepChild<TextMeshProUGUI>(existingRoot, "KeywordsValue");

            var editRowTF = FindDeepTransform(existingRoot, "EditRow");
            _editRowGO = editRowTF?.gameObject;
            if (editRowTF != null)
            {
                FindDeepTransform(editRowTF, "Btn_편집")?.GetComponent<Button>()?.onClick.AddListener(() => OnEditRequested?.Invoke(_currentEntry));
                FindDeepTransform(editRowTF, "Btn_삭제")?.GetComponent<Button>()?.onClick.AddListener(() => OnDeleteRequested?.Invoke(_currentEntry));
            }

            ShowEmpty();
        }

        // 카드 내용이 영역보다 길어질 수 있어(코멘트 등) 드래그/휠 스크롤이 되도록 감싼다.
        private static RectTransform BuildScrollContent(RectTransform parent)
        {
            var scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;

            var vp = NewRect(parent, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vp;

            var content = NewRect(vp, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            return content;
        }

        // 유저 메모(userEntryGuid가 있는 항목)에만 보이는 편집/삭제 버튼 — 정식 단서(ClueData)는 읽기 전용.
        private void BuildEditRow(RectTransform parent, TMP_FontAsset font)
        {
            var row = NewRect(parent, "EditRow");
            _editRowGO = row.gameObject;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 18f;
            le.flexibleWidth = 1f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            MakeSmallBtn(row, font, "편집", () => OnEditRequested?.Invoke(_currentEntry));
            MakeSmallBtn(row, font, "삭제", () => OnDeleteRequested?.Invoke(_currentEntry));
        }

        public void ShowEmpty()
        {
            _currentEntry = null;
            _titleTmp.text = "← 좌측에서 단서를 선택하세요";
            _typeBadgeTmp.text = "";
            _typeBadgeGO.SetActive(false);
            _timestampTmp.gameObject.SetActive(false);
            _contentTmp.text = "";
            _sourceTmp.text = "";
            _mapTmp.text = "";
            _keywordsTmp.text = "";
            _editRowGO.SetActive(false);
        }

        public void ShowEntry(CodexEntry e)
        {
            _currentEntry = e;
            _titleTmp.text = e.title;

            bool hasType = !string.IsNullOrEmpty(e.typeLabel);
            _typeBadgeGO.SetActive(hasType);
            if (hasType) _typeBadgeTmp.text = e.typeLabel;

            bool hasTime = !string.IsNullOrEmpty(e.timestamp);
            _timestampTmp.gameObject.SetActive(hasTime);
            if (hasTime) _timestampTmp.text = e.timestamp;

            _contentTmp.text = e.content;
            _sourceTmp.text = string.IsNullOrEmpty(e.source) ? "-" : e.source;
            _mapTmp.text = string.IsNullOrEmpty(e.mapCategory) ? "기타" : e.mapCategory;
            _keywordsTmp.text = (e.keywords == null || e.keywords.Length == 0) ? "-" : string.Join(", ", e.keywords);

            _editRowGO.SetActive(!string.IsNullOrEmpty(e.userEntryGuid));
        }

        private static void MakeLabel(RectTransform parent, TMP_FontAsset font, string text)
        {
            MakeTMP(parent, font, text, 7f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, height: 12f).color = Gray;
        }

        private static TextMeshProUGUI MakeTMP(RectTransform parent, TMP_FontAsset font, string text,
            float fontSize, FontStyles style, TextAlignmentOptions align, float height = 16f, string id = null)
        {
            var rt = NewRect(parent, id ?? "Lbl");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = Color.white;
            return tmp;
        }

        private static void MakeSmallBtn(RectTransform parent, TMP_FontAsset font, string label, Action onClick)
        {
            var rt = NewRect(parent, "Btn_" + label);
            var img = AddImg(rt, new Color(0.17f, 0.21f, 0.30f));
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 8f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;
        }

        private static void MakeSep(RectTransform parent)
        {
            var rt = NewRect(parent, "Sep");
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            AddImg(rt, new Color(1f, 1f, 1f, 0.10f));
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

        private static T FindDeepChild<T>(Transform parent, string childName) where T : Component
        {
            var t = FindDeepTransform(parent, childName);
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
    }
}
