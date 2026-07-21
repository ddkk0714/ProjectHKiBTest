using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Codex
{
    // 도감 좌측 "서랍" 트리 — 그룹핑(맵/출처/키워드 분류, 검색 필터링)은 CodexFilterService가 미리 계산해서
    // CodexGroup 목록으로 넘겨준다. 이 클래스는 그 목록을 그대로 그리기만 한다(Logic/View 분리).
    // 카테고리 헤더 클릭 시 접고 펼치고, 하위 항목 클릭 시 OnEntrySelected를 발행한다.
    //
    // 행/헤더는 매번 Destroy+Instantiate하지 않고 UiRowPool로 재사용한다 — _categoryTemplate/_rowTemplate에
    // 프리팹을 지정하면 Prefab Mode에서 실제 행 하나를 자유롭게 디자인할 수 있고, 비워두면 아래
    // [SerializeField] 스타일 값으로 만든 기본 템플릿을 그대로 쓴다("Text" 자식 이름은 유지해야 함).
    public class CodexDrawerTreeView : MonoBehaviour
    {
        public event Action<CodexEntry> OnEntrySelected;

        private RectTransform _content;
        private TMP_FontAsset _font;

        private readonly Dictionary<string, bool> _expanded = new();
        private readonly List<CodexGroup> _groups = new();
        private CodexEntry _selected;

        // 6-4단계 — FocusCategory가 스크롤 위치를 계산하는 데 쓴다. Rebuild() 패스마다 다시 채워지므로
        // (풀에서 재사용되는 GameObject라도 이번 패스에 실제로 그려진 헤더만 유효해야 함) 매번 Clear한다.
        private readonly Dictionary<string, RectTransform> _categoryHeaderRTs = new();
        private ScrollRect _scrollRect;

        [Header("행 템플릿 (선택 — 비워두면 아래 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _categoryTemplate;
        [SerializeField] private GameObject _rowTemplate;

        // 행/헤더는 SetGroups()마다 다시 그려지므로(풀에서 재사용) 색상을 코드 상수로 두면 프리팹 편집이
        // 다음 갱신에 곧바로 덮어써진다 — 커스텀 템플릿을 안 쓸 때의 기본 템플릿 스타일로 쓰인다.
        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colHeader   = new(0.14f, 0.17f, 0.24f);
        [SerializeField] private Color _colRow      = new(0.10f, 0.12f, 0.17f);
        [SerializeField] private Color _colSelected = new(0.25f, 0.43f, 0.78f, 0.55f);
        [SerializeField] private float _rowFontSize = 7f;

        private UiRowPool _categoryPool;
        private UiRowPool _rowPool;

        public void Init(RectTransform scrollContent, TMP_FontAsset font)
        {
            _content = scrollContent;
            _font = font;
            _categoryPool = new UiRowPool(_categoryTemplate, BuildCategoryTemplate);
            _rowPool = new UiRowPool(_rowTemplate, BuildRowTemplate);
            _scrollRect = GetComponent<ScrollRect>(); // CodexPanel이 이 컴포넌트를 ScrollRect와 같은 GameObject에 붙인다
        }

        // 도감을 열 때마다 CodexPanel.Open()이 호출 — 이전에 내려서 보고 있던 스크롤 위치가 다음에
        // 열었을 때도 그대로 남아있지 않도록 맨 위로 되돌린다. 1이 맨 위(ScrollRect 정규화 좌표 규약).
        public void ResetScroll()
        {
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
        }

        public void SetGroups(IReadOnlyList<CodexGroup> groups)
        {
            _groups.Clear();
            _groups.AddRange(groups);
            Rebuild();
        }

        private void Rebuild()
        {
            _categoryHeaderRTs.Clear();
            foreach (var group in _groups)
            {
                if (!_expanded.ContainsKey(group.category)) _expanded[group.category] = true;
                BuildCategory(group.category, group.entries);
            }
            _categoryPool.EndPass();
            _rowPool.EndPass();
        }

        // 6-4단계 — 지정한 카테고리를 펼치고 스크롤해서 화면 상단 근처로 가져온다. 도감이 접힌 상태로
        // 카테고리를 감춰뒀을 수도 있으므로 펼침 상태부터 강제한 뒤 다시 그린다.
        public void FocusCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            _expanded[category] = true;
            Rebuild();

            if (_scrollRect == null || !_categoryHeaderRTs.TryGetValue(category, out var headerRT)) return;

            // VerticalLayoutGroup+ContentSizeFitter가 이번 프레임에 아직 크기를 확정하지 않았을 수 있어,
            // 위치를 읽기 전에 강제로 레이아웃을 확정한다(NoteRouteGraphView.ClueNodeAnchor와 같은 이유).
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

            float contentHeight = _content.rect.height;
            float viewportHeight = _scrollRect.viewport.rect.height;
            float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
            if (maxScroll <= 0f) return; // 스크롤할 필요 자체가 없음

            // Content의 pivot이 (0.5,1)이라 anchoredPosition.y가 곧 "맨 위에서부터의 거리(음수)"다.
            float headerDistanceFromTop = -headerRT.anchoredPosition.y;
            _scrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(headerDistanceFromTop / maxScroll);
        }

        private void BuildCategory(string category, List<CodexEntry> entries)
        {
            bool expanded = _expanded[category];

            var headerGO = _categoryPool.Get(_content);
            _categoryHeaderRTs[category] = (RectTransform)headerGO.transform;
            var headerImg = headerGO.GetComponent<Image>();
            if (headerImg != null) headerImg.color = _colHeader;

            var headerBtn = headerGO.GetComponent<Button>();
            if (headerBtn != null)
            {
                headerBtn.onClick.RemoveAllListeners();
                headerBtn.onClick.AddListener(() =>
                {
                    _expanded[category] = !_expanded[category];
                    Rebuild();
                });
            }

            var headerTmp = FindDeepChild<TextMeshProUGUI>(headerGO.transform, "Text");
            if (headerTmp != null)
                headerTmp.text = $"{(expanded ? "▾" : "▸")} {category}  ({entries.Count})";

            if (!expanded) return;

            foreach (var entry in entries)
                BuildRow(entry);
        }

        private void BuildRow(CodexEntry entry)
        {
            var rowGO = _rowPool.Get(_content);

            bool isSelected = _selected == entry;
            var img = rowGO.GetComponent<Image>();
            if (img != null) img.color = isSelected ? _colSelected : _colRow;

            var btn = rowGO.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    _selected = entry;
                    OnEntrySelected?.Invoke(entry);
                    Rebuild();
                });
            }

            var tmp = FindDeepChild<TextMeshProUGUI>(rowGO.transform, "Text");
            if (tmp != null)
            {
                var baseText = string.IsNullOrEmpty(entry.typeLabel) ? entry.title : $"{entry.title}: {entry.typeLabel}";
                // 6-3단계 — 새로 획득했지만 아직 카드로 안 열어본 단서에 NEW 배지(리치 텍스트 태그, TMP 기본 지원).
                tmp.text = entry.isNew ? $"<color=#FFD24C>[NEW]</color> {baseText}" : baseText;
                // 6-2단계 "???" 슬롯은 실제 항목과 구분되도록 흐리게 표시 — 클릭하면 카드에 고정 문구만 뜬다.
                tmp.color = entry.isPlaceholder ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            }
        }

        // ─── 기본 템플릿(프리팹 미지정 시 1회만 생성) ────────────────

        private GameObject BuildCategoryTemplate()
        {
            var rt = NewRect(null, "Category");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var img = AddImg(rt, _colHeader);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin = new Vector4(4f, 0f, 2f, 0f);

            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        private GameObject BuildRowTemplate()
        {
            var rt = NewRect(null, "Row");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var img = AddImg(rt, _colRow);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin = new Vector4(14f, 0f, 2f, 0f);
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            rt.gameObject.SetActive(false);
            return rt.gameObject;
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

        private static T FindDeepChild<T>(Transform parent, string childName) where T : Component
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                {
                    var comp = child.GetComponent<T>();
                    if (comp != null) return comp;
                }
                var found = FindDeepChild<T>(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
