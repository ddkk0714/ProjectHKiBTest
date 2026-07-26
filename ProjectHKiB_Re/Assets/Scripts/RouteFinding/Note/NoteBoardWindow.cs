using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Note
{
    // 노트 상단 툴바 "저장한 루트" 버튼이 여는 모달 창 — CodexMemoFormView와 같은 패턴(반투명 배경 위
    // 중앙 정렬 박스)으로, 지금 화면에 그려진 경로 + 수동 핀 단서 배치를 이름 붙여 저장하거나, 이미
    // 저장해둔 보드 목록에서 불러오기/삭제한다.
    //
    // 여기서 말하는 "보드"는 자동 게임 세이브(SaveSlotData, 단일 슬롯)와는 별개의 개념 — 한 세이브
    // 슬롯 안에 이름 붙은 보드가 여러 개 같이 저장된다(NoteModule.SavedBoards). 실제 저장/불러오기
    // 로직(RouteModule.ImportSelectedRoute·NoteModule.ApplyManualPins·NoteRouteGraphView.ApplySavedPositions
    // 오케스트레이션)은 NotePanel이 담당하고, 이 클래스는 입력을 받아 이벤트로 알리기만 한다.
    public class NoteBoardWindow : MonoBehaviour
    {
        public event Action<string> OnSaveRequested;   // 입력한 보드 이름
        public event Action<string> OnLoadRequested;    // boardId
        public event Action<string> OnDeleteRequested;  // boardId

        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(창 본체, 화면을 덮는 반투명 배경막은 별개)")]
        [SerializeField] private Color _boxBgColor = new(0.08f, 0.09f, 0.13f, 0.98f);
        [Tooltip("창 본체 배경 이미지 — 지정하면 도트풍 이미지로 대체(9슬라이스 테두리 있는 스프라이트도 지원), 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _boxBgSprite;

        private GameObject _overlayGO;
        private TMP_InputField _nameField;
        private RectTransform _listContent;
        private UiRowPool _rowPool;
        private TMP_FontAsset _font;

        private static readonly Color RowBg = new(0.10f, 0.12f, 0.17f);
        private static readonly Color Gray = new(0.55f, 0.60f, 0.65f);

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            _font = font;

            _overlayGO = new GameObject("BoardWindowOverlay");
            _overlayGO.transform.SetParent(parent, false);
            var backdropRT = _overlayGO.AddComponent<RectTransform>();
            StretchFull(backdropRT);
            AddImg(backdropRT, new Color(0f, 0f, 0f, 0.55f));

            var boxRT = NewRect(backdropRT, "Box");
            boxRT.anchorMin = boxRT.anchorMax = new Vector2(0.5f, 0.5f);
            boxRT.pivot = new Vector2(0.5f, 0.5f);
            boxRT.sizeDelta = new Vector2(220f, 240f);
            PanelBackground.Apply(boxRT, _boxBgColor, _boxBgSprite);

            var vlg = boxRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            MakeTitleLabel(boxRT, "저장한 루트");

            BuildSaveRow(boxRT);
            BuildSeparator(boxRT);
            MakeSmallLabel(boxRT, "목록");
            BuildListArea(boxRT);

            var closeBtnRT = MakeBtn(boxRT, "닫기", Hide);
            closeBtnRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

            _overlayGO.SetActive(false);
        }

        // 프리팹 재사용 시 호출 — Init()으로 새로 만들지 않고 existingRoot 아래의 "BoardWindowOverlay"를
        // 재탐색해 참조를 복원하고 정적 버튼(저장/닫기) onClick만 다시 연결한다. 목록 행은 어차피
        // Refresh()가 매번 새로 채우므로 여기서 다시 연결할 필요가 없다.
        public void Bind(RectTransform existingRoot, TMP_FontAsset font)
        {
            // _font를 여기서도 받아야 한다 — 이 창의 보드 목록 행(BuildRowTemplate)은 CodexMemoFormView의
            // 정적 필드들과 달리 프리팹에 저장되지 않고 매번 새로 만들어지는데(아래 UiRowPool), Init()을
            // 거치지 않는 재사용 경로(Bind)에서 _font를 안 받으면 null인 채로 템플릿을 만들어 한글이
            // 없는 기본 LiberationSans SDF로 폴백해버린다.
            _font = font;

            var overlayTF = FindDeepTransform(existingRoot, "BoardWindowOverlay");
            _overlayGO = overlayTF?.gameObject;
            if (overlayTF == null) return;

            PanelBackground.Apply(FindDeepTransform(overlayTF, "Box") as RectTransform, _boxBgColor, _boxBgSprite);

            _nameField = FindDeepTransform(overlayTF, "NameField")?.GetComponent<TMP_InputField>();
            var listTF = FindDeepTransform(overlayTF, "ListContent");
            _listContent = listTF as RectTransform;
            if (_listContent != null)
            {
                // ClueDrawerView.Init과 동일한 이유 — 프리팹 저장 시점에 이미 채워져 있던 행 클론이
                // 그대로 남아있을 수 있는데, 새로 만드는 UiRowPool의 내부 풀 목록은 그 존재를 전혀
                // 모르므로 먼저 비운 뒤 항상 빈 상태에서 다시 짓는다.
                for (int i = _listContent.childCount - 1; i >= 0; i--)
                {
                    var child = _listContent.GetChild(i).gameObject;
                    child.SetActive(false);
                    Destroy(child);
                }
                _rowPool = new UiRowPool(null, BuildRowTemplate);
            }

            FindDeepTransform(overlayTF, "Btn_저장")?.GetComponent<Button>()?.onClick.AddListener(HandleSaveClicked);
            FindDeepTransform(overlayTF, "Btn_닫기")?.GetComponent<Button>()?.onClick.AddListener(Hide);

            Hide();
        }

        public void Show(IReadOnlyList<NoteSavedBoard> boards)
        {
            if (_nameField != null) _nameField.text = "";
            Refresh(boards);
            Reveal();
        }

        public void Refresh(IReadOnlyList<NoteSavedBoard> boards)
        {
            if (_listContent == null || _rowPool == null) return;

            if (boards == null || boards.Count == 0)
            {
                var emptyGO = _rowPool.Get(_listContent);
                PopulateEmptyRow(emptyGO);
            }
            else
            {
                foreach (var board in boards)
                    PopulateBoardRow(_rowPool.Get(_listContent), board);
            }
            _rowPool.EndPass();
        }

        public void Hide() => _overlayGO?.SetActive(false);

        private void Reveal()
        {
            _overlayGO.SetActive(true);
            _overlayGO.transform.SetAsLastSibling();
        }

        private void HandleSaveClicked()
        {
            var name = _nameField != null ? _nameField.text.Trim() : "";
            OnSaveRequested?.Invoke(name);
        }

        // ─── 목록 행 (풀링) ──────────────────────────────────────

        private void PopulateBoardRow(GameObject rowGO, NoteSavedBoard board)
        {
            var label = rowGO.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = board.boardName;

            var loadBtnTF = rowGO.transform.Find("Btn_불러오기");
            var loadBtn = loadBtnTF?.GetComponent<Button>();
            if (loadBtn != null)
            {
                loadBtnTF.gameObject.SetActive(true); // 직전에 이 행이 빈 목록 안내(PopulateEmptyRow)로 쓰여 꺼져 있었을 수 있다
                loadBtn.onClick.RemoveAllListeners();
                loadBtn.onClick.AddListener(() => OnLoadRequested?.Invoke(board.boardId));
            }

            var deleteBtnTF = rowGO.transform.Find("Btn_삭제");
            var deleteBtn = deleteBtnTF?.GetComponent<Button>();
            if (deleteBtn != null)
            {
                deleteBtnTF.gameObject.SetActive(true);
                deleteBtn.onClick.RemoveAllListeners();
                deleteBtn.onClick.AddListener(() => OnDeleteRequested?.Invoke(board.boardId));
            }
        }

        private void PopulateEmptyRow(GameObject rowGO)
        {
            var label = rowGO.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = "저장한 루트가 없습니다.";

            foreach (var name in new[] { "Btn_불러오기", "Btn_삭제" })
            {
                var btnTF = rowGO.transform.Find(name);
                if (btnTF != null) btnTF.gameObject.SetActive(false);
            }
        }

        private GameObject BuildRowTemplate()
        {
            var rt = NewRect(null, "BoardRow");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 14f;
            le.flexibleWidth = 1f;
            AddImg(rt, RowBg);

            var hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(4, 4, 1, 1);
            hlg.spacing = 3f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var labelRT = NewRect(rt, "Label");
            var labelLe = labelRT.gameObject.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f;
            var labelTmp = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) labelTmp.font = _font;
            labelTmp.fontSize = 7f;
            labelTmp.color = Color.white;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
            labelTmp.overflowMode = TextOverflowModes.Ellipsis;

            MakeRowButton(rt, "Btn_불러오기", "불러오기", new Color(0.20f, 0.45f, 0.30f));
            MakeRowButton(rt, "Btn_삭제", "삭제", new Color(0.45f, 0.18f, 0.18f));

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        private void MakeRowButton(RectTransform parent, string id, string label, Color color)
        {
            var rt = NewRect(parent, id);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 40f;
            le.flexibleWidth = 0f;
            var img = AddImg(rt, color);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = label;
            tmp.fontSize = 6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;
        }

        // ─── 고정 영역 (저장 행 / 목록 스크롤) ─────────────────────

        private void BuildSaveRow(RectTransform parent)
        {
            var row = NewRect(parent, "SaveRow");
            var rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 14f;
            rowLe.flexibleWidth = 1f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            var fieldRT = NewRect(row, "NameField");
            var fieldLe = fieldRT.gameObject.AddComponent<LayoutElement>();
            fieldLe.flexibleWidth = 1f;
            AddImg(fieldRT, new Color(0.03f, 0.04f, 0.06f));

            var textAreaRT = NewRect(fieldRT, "TextArea");
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
            placeholderTmp.text = "보드 이름";
            placeholderTmp.fontSize = 7f;
            placeholderTmp.fontStyle = FontStyles.Italic;
            placeholderTmp.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;

            _nameField = fieldRT.gameObject.AddComponent<TMP_InputField>();
            _nameField.textViewport = textAreaRT;
            _nameField.textComponent = textTmp;
            _nameField.placeholder = placeholderTmp;

            var saveBtnRT = MakeBtn(row, "저장", HandleSaveClicked);
            saveBtnRT.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
        }

        private void BuildListArea(RectTransform parent)
        {
            var scrollArea = NewRect(parent, "ListScroll");
            var scrollLe = scrollArea.gameObject.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.preferredHeight = 110f;
            AddImg(scrollArea, new Color(0.05f, 0.06f, 0.09f));

            var scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;

            var vp = NewRect(scrollArea, "Viewport");
            StretchFull(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = vp;

            _listContent = NewRect(vp, "ListContent");
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = Vector2.one;
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.sizeDelta = Vector2.zero;
            scroll.content = _listContent;

            var csf = _listContent.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(3, 3, 3, 3);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _rowPool = new UiRowPool(null, BuildRowTemplate);
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

        private void MakeSmallLabel(RectTransform parent, string text)
        {
            var rt = NewRect(parent, "Lbl_" + text);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 10f;
            le.flexibleWidth = 1f;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = 7f;
            tmp.color = Gray;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private void BuildSeparator(RectTransform parent)
        {
            var rt = NewRect(parent, "Sep");
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            AddImg(rt, new Color(1f, 1f, 1f, 0.10f));
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
