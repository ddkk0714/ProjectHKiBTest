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
    public class CodexDrawerTreeView : MonoBehaviour
    {
        public event Action<CodexEntry> OnEntrySelected;

        private RectTransform _content;
        private TMP_FontAsset _font;

        private readonly Dictionary<string, bool> _expanded = new();
        private readonly List<GameObject> _spawned = new();
        private readonly List<CodexGroup> _groups = new();
        private CodexEntry _selected;

        private static readonly Color ColHeader   = new(0.14f, 0.17f, 0.24f);
        private static readonly Color ColRow      = new(0.10f, 0.12f, 0.17f);
        private static readonly Color ColSelected = new(0.25f, 0.43f, 0.78f, 0.55f);

        public void Init(RectTransform scrollContent, TMP_FontAsset font)
        {
            _content = scrollContent;
            _font = font;
        }

        public void SetGroups(IReadOnlyList<CodexGroup> groups)
        {
            _groups.Clear();
            _groups.AddRange(groups);
            Rebuild();
        }

        private void Rebuild()
        {
            Clear();

            foreach (var group in _groups)
            {
                if (!_expanded.ContainsKey(group.category)) _expanded[group.category] = true;
                BuildCategory(group.category, group.entries);
            }
        }

        private void Clear()
        {
            // 먼저 비활성화한 뒤 Destroy — TMP 오브젝트를 활성 상태로 그냥 Destroy하면, 같은 프레임에
            // ScrollRect.LateUpdate가 강제하는 CanvasUpdateRegistry 리빌드가 이미 파괴 중인 TMP의
            // 서브메시(폴백 폰트) 머티리얼에 접근하려다 MissingReferenceException을 던지는 경우가 있다
            // (MapViewer.RefreshClueList와 동일한 문제). SetActive(false)는 OnDisable을 즉시 호출해
            // 리빌드 대상에서 그 프레임에 바로 빠지게 한다.
            foreach (var go in _spawned)
            {
                go.SetActive(false);
                Destroy(go);
            }
            _spawned.Clear();
        }

        private void BuildCategory(string category, List<CodexEntry> entries)
        {
            bool expanded = _expanded[category];

            var headerRT = NewRect(_content, "Category_" + category);
            var headerLe = headerRT.gameObject.AddComponent<LayoutElement>();
            headerLe.preferredHeight = 20f;
            headerLe.flexibleWidth = 1f;
            var headerImg = AddImg(headerRT, ColHeader);
            var headerBtn = headerRT.gameObject.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            headerBtn.transition = Selectable.Transition.None;
            headerBtn.onClick.AddListener(() =>
            {
                _expanded[category] = !_expanded[category];
                Rebuild();
            });

            var headerTxtRT = NewRect(headerRT, "Text");
            StretchFull(headerTxtRT);
            var headerTmp = headerTxtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) headerTmp.font = _font;
            headerTmp.text = $"{(expanded ? "▾" : "▸")} {category}  ({entries.Count})";
            headerTmp.fontSize = 9f;
            headerTmp.fontStyle = FontStyles.Bold;
            headerTmp.color = Color.white;
            headerTmp.alignment = TextAlignmentOptions.MidlineLeft;
            headerTmp.margin = new Vector4(4f, 0f, 2f, 0f);

            _spawned.Add(headerRT.gameObject);

            if (!expanded) return;

            foreach (var entry in entries)
                BuildRow(entry);
        }

        private void BuildRow(CodexEntry entry)
        {
            var rowRT = NewRect(_content, "Entry_" + entry.title);
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 18f;
            le.flexibleWidth = 1f;

            bool isSelected = _selected == entry;
            var img = AddImg(rowRT, isSelected ? ColSelected : ColRow);
            var btn = rowRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                _selected = entry;
                OnEntrySelected?.Invoke(entry);
                Rebuild();
            });

            var txtRT = NewRect(rowRT, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = string.IsNullOrEmpty(entry.typeLabel) ? entry.title : $"{entry.title}: {entry.typeLabel}";
            tmp.fontSize = 8f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin = new Vector4(14f, 0f, 2f, 0f);
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            _spawned.Add(rowRT.gameObject);
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
    }
}
