using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Note
{
    // [신설, 2026-07-21] 노트 상단 툴바 "단서 생성" 버튼이 여는 입력 창 — 유저가 직접 단서를 만들면
    // 도감(CodexModule.AddUserEntry)에도 자동 등록된다(NotePanel.HandleClueCreateRequested 참고).
    // NoteBoardWindow/CodexMemoFormView와 같은 패턴(반투명 배경 위 중앙 정렬 박스) — 이 창 자체는
    // 순수 입력 UI이고, 실제 생성·등록·핀은 NotePanel이 이벤트를 받아 처리한다.
    public class NoteClueCreateWindow : MonoBehaviour
    {
        public event Action<string, string, string[]> OnCreateRequested; // title, content, keywords

        private GameObject _overlayGO;
        private TMP_InputField _titleField;
        private TMP_InputField _contentField;
        private TMP_InputField _keywordsField;
        private TMP_FontAsset _font;

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            _font = font;

            _overlayGO = new GameObject("ClueCreateOverlay");
            _overlayGO.transform.SetParent(parent, false);
            var backdropRT = _overlayGO.AddComponent<RectTransform>();
            StretchFull(backdropRT);
            AddImg(backdropRT, new Color(0f, 0f, 0f, 0.55f));

            var boxRT = NewRect(backdropRT, "Box");
            boxRT.anchorMin = boxRT.anchorMax = new Vector2(0.5f, 0.5f);
            boxRT.pivot = new Vector2(0.5f, 0.5f);
            boxRT.sizeDelta = new Vector2(220f, 180f);
            AddImg(boxRT, new Color(0.08f, 0.09f, 0.13f, 0.98f));

            var vlg = boxRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            MakeTitleLabel(boxRT, "단서 생성");

            MakeFieldLabel(boxRT, "제목");
            _titleField = MakeInputField(boxRT, "제목", 12f, multiline: false, id: "TitleField");

            MakeFieldLabel(boxRT, "내용");
            _contentField = MakeInputField(boxRT, "내용", 45f, multiline: true, id: "ContentField");

            MakeFieldLabel(boxRT, "키워드 (쉼표로 구분)");
            _keywordsField = MakeInputField(boxRT, "", 12f, multiline: false, id: "KeywordsField");

            var btnRow = NewRect(boxRT, "Buttons");
            var btnRowLe = btnRow.gameObject.AddComponent<LayoutElement>();
            btnRowLe.preferredHeight = 14f;
            btnRowLe.flexibleWidth = 1f;
            var hlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            MakeBtn(btnRow, "생성", HandleCreateClicked);
            MakeBtn(btnRow, "취소", Hide);

            _overlayGO.SetActive(false);
        }

        // 프리팹 재사용 시 호출 — NoteBoardWindow.Bind와 동일한 이유·구조.
        public void Bind(RectTransform existingRoot, TMP_FontAsset font)
        {
            _font = font;

            var overlayTF = FindDeepTransform(existingRoot, "ClueCreateOverlay");
            _overlayGO = overlayTF?.gameObject;
            if (overlayTF == null) return;

            _titleField    = FindDeepTransform(overlayTF, "TitleField")?.GetComponent<TMP_InputField>();
            _contentField  = FindDeepTransform(overlayTF, "ContentField")?.GetComponent<TMP_InputField>();
            _keywordsField = FindDeepTransform(overlayTF, "KeywordsField")?.GetComponent<TMP_InputField>();

            FindDeepTransform(overlayTF, "Btn_생성")?.GetComponent<Button>()?.onClick.AddListener(HandleCreateClicked);
            FindDeepTransform(overlayTF, "Btn_취소")?.GetComponent<Button>()?.onClick.AddListener(Hide);

            Hide();
        }

        public void Show()
        {
            if (_titleField != null) _titleField.text = "";
            if (_contentField != null) _contentField.text = "";
            if (_keywordsField != null) _keywordsField.text = "";
            Reveal();
        }

        public void Hide() => _overlayGO?.SetActive(false);

        private void Reveal()
        {
            _overlayGO.SetActive(true);
            _overlayGO.transform.SetAsLastSibling();
        }

        private void HandleCreateClicked()
        {
            var title = _titleField != null ? _titleField.text.Trim() : "";
            if (string.IsNullOrEmpty(title)) return; // 최소 검증 — 제목 없는 단서는 만들지 않는다.

            var content = _contentField != null ? _contentField.text : "";
            var keywords = _keywordsField != null
                ? _keywordsField.text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray()
                : Array.Empty<string>();

            OnCreateRequested?.Invoke(title, content, keywords);
            Hide();
        }

        // ─── UI 헬퍼 ─────────────────────────────────────────────

        private void MakeTitleLabel(RectTransform parent, string text)
        {
            var rt = NewRect(parent, "Title");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = 8f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private void MakeFieldLabel(RectTransform parent, string text)
        {
            var rt = NewRect(parent, "Lbl_" + text);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 10f;
            le.flexibleWidth = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = 7f;
            tmp.color = new Color(0.55f, 0.60f, 0.65f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private TMP_InputField MakeInputField(RectTransform parent, string placeholder, float height, bool multiline, string id)
        {
            var rt = NewRect(parent, id);
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
            if (_font != null) textTmp.font = _font;
            textTmp.fontSize = 7f;
            textTmp.color = Color.white;
            textTmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            textTmp.enableWordWrapping = true;

            var placeholderRT = NewRect(textAreaRT, "Placeholder");
            StretchFull(placeholderRT);
            var placeholderTmp = placeholderRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) placeholderTmp.font = _font;
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

        private RectTransform MakeBtn(RectTransform parent, string label, Action onClick)
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
            if (_font != null) tmp.font = _font;
            tmp.text = label;
            tmp.fontSize = 7f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;

            return rt;
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
