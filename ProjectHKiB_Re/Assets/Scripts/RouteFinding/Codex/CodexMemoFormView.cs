using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Codex
{
    // 저장 버튼 클릭 시 넘어오는 결과 — guid가 비어있으면 신규 생성, 있으면 해당 guid를 수정.
    public struct CodexMemoFormResult
    {
        public string guid;
        public string title;
        public string content;
        public string mapCategory;
        public string[] keywords;
    }

    // "새 메모 추가" / 유저 메모 편집 폼 — 패널 전체를 덮는 반투명 배경 위에 중앙 정렬된 입력 박스.
    // 실제 CodexUserEntry CRUD는 CodexModule이 담당하고, 이 클래스는 입력만 받아 이벤트로 알린다.
    public class CodexMemoFormView : MonoBehaviour
    {
        public event Action<CodexMemoFormResult> OnSaved;
        public event Action OnCancelled;
        public event Action<string> OnDeleteRequested; // 삭제 대상 guid — 편집 중이던 항목 기준

        private GameObject _overlayGO;
        private TMP_InputField _titleField;
        private TMP_InputField _contentField;
        private TMP_InputField _mapField;
        private TMP_InputField _keywordsField;
        private GameObject _deleteBtnGO;
        private string _editingGuid = "";

        private static readonly Color Gray = new(0.55f, 0.60f, 0.65f);

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            _overlayGO = new GameObject("MemoFormOverlay");
            _overlayGO.transform.SetParent(parent, false);
            var backdropRT = _overlayGO.AddComponent<RectTransform>();
            StretchFull(backdropRT);
            AddImg(backdropRT, new Color(0f, 0f, 0f, 0.55f));

            var boxRT = NewRect(backdropRT, "Box");
            boxRT.anchorMin = boxRT.anchorMax = new Vector2(0.5f, 0.5f);
            boxRT.pivot = new Vector2(0.5f, 0.5f);
            boxRT.sizeDelta = new Vector2(220f, 220f);
            AddImg(boxRT, new Color(0.08f, 0.09f, 0.13f, 0.98f));

            var vlg = boxRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            MakeTitleLabel(boxRT, font, "메모");

            MakeFieldLabel(boxRT, font, "제목");
            _titleField = MakeInputField(boxRT, font, "제목", 12f, multiline: false, id: "TitleField");

            MakeFieldLabel(boxRT, font, "내용");
            _contentField = MakeInputField(boxRT, font, "내용", 45f, multiline: true, id: "ContentField");

            MakeFieldLabel(boxRT, font, "소속 맵 (선택, 없으면 기타)");
            _mapField = MakeInputField(boxRT, font, "", 12f, multiline: false, id: "MapField");

            MakeFieldLabel(boxRT, font, "키워드 (쉼표로 구분)");
            _keywordsField = MakeInputField(boxRT, font, "", 12f, multiline: false, id: "KeywordsField");

            var btnRow = NewRect(boxRT, "Buttons");
            var btnRowLe = btnRow.gameObject.AddComponent<LayoutElement>();
            btnRowLe.preferredHeight = 12f;
            btnRowLe.flexibleWidth = 1f;
            var hlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            MakeBtn(btnRow, font, "저장", HandleSaveClicked);
            MakeBtn(btnRow, font, "취소", HandleCancelClicked);
            _deleteBtnGO = MakeBtn(btnRow, font, "삭제", HandleDeleteClicked);

            _overlayGO.SetActive(false);
        }

        public void ShowForCreate()
        {
            _editingGuid = "";
            _titleField.text = "";
            _contentField.text = "";
            _mapField.text = "";
            _keywordsField.text = "";
            _deleteBtnGO.SetActive(false);
            Reveal();
        }

        public void ShowForEdit(CodexUserEntry entry)
        {
            _editingGuid = entry.guid;
            _titleField.text = entry.title;
            _contentField.text = entry.content;
            _mapField.text = entry.mapCategory;
            _keywordsField.text = entry.keywords != null ? string.Join(", ", entry.keywords) : "";
            _deleteBtnGO.SetActive(true);
            Reveal();
        }

        // 프리팹 재사용 시 호출 — Init()으로 새로 만들지 않고, existingRoot(패널 루트) 아래의
        // "MemoFormOverlay"를 재탐색해 참조를 복원하고 버튼 onClick을 다시 연결한다.
        public void Bind(RectTransform existingRoot)
        {
            var overlayTF = FindDeepTransform(existingRoot, "MemoFormOverlay");
            _overlayGO = overlayTF?.gameObject;
            if (overlayTF == null) return;

            _titleField    = FindDeepTransform(overlayTF, "TitleField")?.GetComponent<TMP_InputField>();
            _contentField  = FindDeepTransform(overlayTF, "ContentField")?.GetComponent<TMP_InputField>();
            _mapField      = FindDeepTransform(overlayTF, "MapField")?.GetComponent<TMP_InputField>();
            _keywordsField = FindDeepTransform(overlayTF, "KeywordsField")?.GetComponent<TMP_InputField>();

            FindDeepTransform(overlayTF, "Btn_저장")?.GetComponent<Button>()?.onClick.AddListener(HandleSaveClicked);
            FindDeepTransform(overlayTF, "Btn_취소")?.GetComponent<Button>()?.onClick.AddListener(HandleCancelClicked);
            var deleteBtnTF = FindDeepTransform(overlayTF, "Btn_삭제");
            _deleteBtnGO = deleteBtnTF?.gameObject;
            deleteBtnTF?.GetComponent<Button>()?.onClick.AddListener(HandleDeleteClicked);

            Hide();
        }

        public void Hide() => _overlayGO.SetActive(false);

        private void Reveal()
        {
            _overlayGO.SetActive(true);
            _overlayGO.transform.SetAsLastSibling();
        }

        private void HandleSaveClicked()
        {
            var title = _titleField.text.Trim();
            if (string.IsNullOrEmpty(title)) return; // 최소 검증 — 제목 없는 메모는 저장하지 않는다.

            var keywords = _keywordsField.text
                .Split(',')
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToArray();

            OnSaved?.Invoke(new CodexMemoFormResult
            {
                guid        = _editingGuid,
                title       = title,
                content     = _contentField.text,
                mapCategory = _mapField.text.Trim(),
                keywords    = keywords,
            });
            Hide();
        }

        private void HandleCancelClicked()
        {
            OnCancelled?.Invoke();
            Hide();
        }

        private void HandleDeleteClicked()
        {
            OnDeleteRequested?.Invoke(_editingGuid);
            Hide();
        }

        // ─── UI 헬퍼 ─────────────────────────────────────────────

        private static void MakeTitleLabel(RectTransform parent, TMP_FontAsset font, string text)
        {
            var rt = NewRect(parent, "Title");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = 8f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static void MakeFieldLabel(RectTransform parent, TMP_FontAsset font, string text)
        {
            var rt = NewRect(parent, "Lbl_" + text);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = 7f;
            tmp.color = Gray;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static TMP_InputField MakeInputField(RectTransform parent, TMP_FontAsset font,
            string placeholder, float height, bool multiline, string id = null)
        {
            var rt = NewRect(parent, id ?? "Field");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
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
            textTmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            textTmp.enableWordWrapping = true;

            var placeholderRT = NewRect(textAreaRT, "Placeholder");
            StretchFull(placeholderRT);
            var placeholderTmp = placeholderRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) placeholderTmp.font = font;
            placeholderTmp.text = placeholder;
            placeholderTmp.fontSize = 7f;
            placeholderTmp.fontStyle = FontStyles.Italic;
            placeholderTmp.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderTmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;

            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = textAreaRT;
            field.textComponent = textTmp;
            field.placeholder = placeholderTmp;
            field.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            return field;
        }

        private static GameObject MakeBtn(RectTransform parent, TMP_FontAsset font, string label, Action onClick)
        {
            var rt = NewRect(parent, "Btn_" + label);
            var img = AddImg(rt, new Color(0.25f, 0.42f, 0.72f));
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

            return rt.gameObject;
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
