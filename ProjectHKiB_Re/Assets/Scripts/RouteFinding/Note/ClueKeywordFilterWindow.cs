using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Note
{
    // 단서 서랍(ClueDrawerView) 검색창 옆 "필터" 버튼이 여는 키워드 다중 선택 창 — 기존에 서랍 안에
    // 항상 펼쳐져 있던 배타적 단일 선택 키워드 행("전체"/키워드 하나만 고를 수 있던 것)을 요청으로
    // 교체: 이제 여러 키워드를 체크박스로 동시에 넣고 뺄 수 있다(OR 조건 — 체크된 것 중 하나라도
    // 겹치면 통과, ClueDrawerView.HasAnyActiveKeyword 참고).
    //
    // NoteBoardWindow와 같은 이유로 NotePanelRoot에 직접 붙는다(ClueDrawerView가 아니라 NotePanel이
    // 소유) — ClueDrawerView가 사는 ClueDrawerScroll은 RectMask2D로 잘리는 스크롤 영역이라 그 안에
    // 모달을 띄우면 잘리고, 무엇보다 NotePanel을 닫을 때(_panelGO.SetActive(false)) 같이 꺼져야
    // 하는데 그러려면 _panelGO(NotePanelRoot)의 자손이어야 한다 — 최상위 캔버스에 붙이면(드래그
    // 고스트처럼) 노트를 닫아도 계속 화면에 남는 사고가 난다.
    //
    // 실제 필터 상태(_activeKeywords)는 ClueDrawerView가 소유하고, 이 창은 순수 입력 UI — 체크/해제·
    // 전체 해제 이벤트만 쏘고 상태 반영과 재계산은 전부 호출자(NotePanel)가 ClueDrawerView를 통해 한다.
    public class ClueKeywordFilterWindow : MonoBehaviour
    {
        public event Action<string> OnKeywordToggled;
        public event Action OnClearAllRequested;

        [Tooltip("스프라이트를 지정 안 했을 때 쓰는 단색 배경(창 본체, 화면을 덮는 반투명 배경막은 별개)")]
        [SerializeField] private Color _boxBgColor = new(0.08f, 0.09f, 0.13f, 0.98f);
        [Tooltip("창 본체 배경 이미지 — 지정하면 도트풍 이미지로 대체(9슬라이스 테두리 있는 스프라이트도 지원), 비워두면 위 단색 사용")]
        [SerializeField] private Sprite _boxBgSprite;

        private GameObject _overlayGO;
        private RectTransform _listContent;
        private UiRowPool _rowPool;
        private TMP_FontAsset _font;

        private static readonly Color RowBg = new(0.10f, 0.12f, 0.17f);
        private static readonly Color RowBgChecked = new(0.20f, 0.34f, 0.52f);

        public void Init(RectTransform parent, TMP_FontAsset font)
        {
            _font = font;

            _overlayGO = new GameObject("ClueKeywordFilterOverlay");
            _overlayGO.transform.SetParent(parent, false);
            var backdropRT = _overlayGO.AddComponent<RectTransform>();
            StretchFull(backdropRT);
            AddImg(backdropRT, new Color(0f, 0f, 0f, 0.55f));

            var boxRT = NewRect(backdropRT, "Box");
            boxRT.anchorMin = boxRT.anchorMax = new Vector2(0.5f, 0.5f);
            boxRT.pivot = new Vector2(0.5f, 0.5f);
            boxRT.sizeDelta = new Vector2(200f, 220f);
            PanelBackground.Apply(boxRT, _boxBgColor, _boxBgSprite);

            var vlg = boxRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            MakeTitleLabel(boxRT, "키워드 필터");
            BuildListArea(boxRT);

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

            MakeBtn(btnRow, "전체 해제", () => OnClearAllRequested?.Invoke());
            MakeBtn(btnRow, "닫기", Hide);

            _overlayGO.SetActive(false);
        }

        // 프리팹 재사용 시 호출 — NoteBoardWindow.Bind와 동일한 이유·구조.
        public void Bind(RectTransform existingRoot, TMP_FontAsset font)
        {
            _font = font;

            var overlayTF = FindDeepTransform(existingRoot, "ClueKeywordFilterOverlay");
            _overlayGO = overlayTF?.gameObject;
            if (overlayTF == null) return;

            PanelBackground.Apply(FindDeepTransform(overlayTF, "Box") as RectTransform, _boxBgColor, _boxBgSprite);

            var listTF = FindDeepTransform(overlayTF, "ListContent");
            _listContent = listTF as RectTransform;
            if (_listContent != null)
            {
                // NoteBoardWindow.Bind와 동일한 이유 — 새로 만드는 UiRowPool은 프리팹 저장 시점에
                // 이미 채워져 있던 행 클론의 존재를 모르므로 먼저 비운다.
                for (int i = _listContent.childCount - 1; i >= 0; i--)
                {
                    var child = _listContent.GetChild(i).gameObject;
                    child.SetActive(false);
                    Destroy(child);
                }
                _rowPool = new UiRowPool(null, BuildRowTemplate);
            }

            FindDeepTransform(overlayTF, "Btn_전체 해제")?.GetComponent<Button>()?.onClick.AddListener(() => OnClearAllRequested?.Invoke());
            FindDeepTransform(overlayTF, "Btn_닫기")?.GetComponent<Button>()?.onClick.AddListener(Hide);

            Hide();
        }

        public void Show(IReadOnlyList<string> allKeywords, IReadOnlyCollection<string> activeKeywords)
        {
            Refresh(allKeywords, activeKeywords);
            Reveal();
        }

        public void Refresh(IReadOnlyList<string> allKeywords, IReadOnlyCollection<string> activeKeywords)
        {
            if (_listContent == null || _rowPool == null) return;

            if (allKeywords == null || allKeywords.Count == 0)
            {
                PopulateEmptyRow(_rowPool.Get(_listContent));
            }
            else
            {
                foreach (var kw in allKeywords)
                    PopulateKeywordRow(_rowPool.Get(_listContent), kw, activeKeywords != null && activeKeywords.Contains(kw));
            }
            _rowPool.EndPass();
        }

        public void Hide() => _overlayGO?.SetActive(false);

        private void Reveal()
        {
            _overlayGO.SetActive(true);
            _overlayGO.transform.SetAsLastSibling();
        }

        // ─── 행 (풀링, 체크박스) ──────────────────────────────────

        private void PopulateKeywordRow(GameObject rowGO, string keyword, bool isChecked)
        {
            var img = rowGO.GetComponent<Image>();
            if (img != null) img.color = isChecked ? RowBgChecked : RowBg;

            var label = rowGO.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = (isChecked ? "[X] " : "[ ] ") + keyword;

            var btn = rowGO.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnKeywordToggled?.Invoke(keyword));
            }
        }

        private void PopulateEmptyRow(GameObject rowGO)
        {
            var img = rowGO.GetComponent<Image>();
            if (img != null) img.color = RowBg;

            var label = rowGO.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = "표시할 키워드가 없습니다.";

            var btn = rowGO.GetComponent<Button>();
            if (btn != null) btn.onClick.RemoveAllListeners();
        }

        private GameObject BuildRowTemplate()
        {
            var rt = NewRect(null, "KeywordRow");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 13f;
            le.flexibleWidth = 1f;
            var img = AddImg(rt, RowBg);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var labelRT = NewRect(rt, "Label");
            StretchFull(labelRT);
            labelRT.offsetMin = new Vector2(4f, 0f);
            var labelTmp = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) labelTmp.font = _font;
            labelTmp.fontSize = 7f;
            labelTmp.color = Color.white;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ─── 고정 영역 ───────────────────────────────────────────

        private void BuildListArea(RectTransform parent)
        {
            var scrollArea = NewRect(parent, "ListScroll");
            var scrollLe = scrollArea.gameObject.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.preferredHeight = 150f;
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
