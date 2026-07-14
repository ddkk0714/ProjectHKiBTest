using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.Note
{
    // 노트의 핵심 산출물 UI — 다중 목적지 이동 계획(RouteWaypointPlan) 목록 + 선택된 계획의
    // 목적지 순서 편집·경로 방식 선택·미리보기 요약·실행/재개. 상태 자체는 NoteModule이 소유하고,
    // 이 뷰는 그 CRUD/실행 API를 그대로 호출해 다시 그리기만 한다(다른 View들과 같은 패턴).
    //
    // 계획 행/목적지 행은 UiRowPool로 재사용한다 — _planRowTemplate/_waypointRowTemplate에 프리팹을
    // 지정하면 Prefab Mode에서 실제 행 하나를 자유롭게 디자인할 수 있고, 비워두면 아래 스타일 값으로
    // 만든 기본 템플릿을 그대로 쓴다. 나머지(새 계획 버튼/구분선/경로방식/요약/실행버튼)는 선택된
    // 계획당 많아야 1개뿐이라 그냥 매번 새로 그린다.
    public class RoutePlanEditorView : MonoBehaviour
    {
        private RectTransform _content;
        private TMP_FontAsset _font;
        private readonly List<GameObject> _miscSpawned = new();

        // 현재 상세를 펼쳐 보고 있는 계획 — "계획에 추가" 액션(NoteRouteGraphView 카드)이 향하는 대상이기도 하다.
        private string _selectedPlanId = "";

        [Header("행 템플릿 (선택 — 비워두면 아래 스타일 값으로 기본 템플릿 생성)")]
        [SerializeField] private GameObject _planRowTemplate;
        [SerializeField] private GameObject _waypointRowTemplate;

        // 계획/목적지 행은 Refresh()마다 UiRowPool에서 재사용된다 — 커스텀 템플릿을 안 쓸 때의
        // 기본 템플릿 스타일로 쓰인다.
        [Header("기본 템플릿 스타일 (프리팹 미지정 시)")]
        [SerializeField] private Color _colPlanRow         = new(0.14f, 0.17f, 0.24f);
        [SerializeField] private Color _colPlanRowSelected = new(0.25f, 0.43f, 0.78f, 0.55f);
        [SerializeField] private Color _colWaypointRow     = new(0.10f, 0.12f, 0.17f);
        [SerializeField] private Color _colBtnActive       = new(0.25f, 0.42f, 0.72f);
        [SerializeField] private Color _colBtnInactive     = new(0.17f, 0.21f, 0.30f);
        [SerializeField] private Color _colBtnGreen        = new(0.15f, 0.32f, 0.19f);
        [SerializeField] private Color _colBtnRed          = new(0.42f, 0.10f, 0.10f);
        [SerializeField] private float _rowFontSize        = 7f;

        private UiRowPool _planRowPool;
        private UiRowPool _waypointRowPool;

        public void Init(RectTransform scrollContent, TMP_FontAsset font)
        {
            _content = scrollContent;
            _font = font;

            // NoteRouteGraphView.Init()과 동일한 이유(GraphArea/OtherArea/NewPlanBtn 중복 생성 버그와
            // 같은 원인) — 프리팹을 재사용/재인스턴스화하면 저장 시점에 이미 있던 PlanRow/WaypointRow
            // 클론, NewPlanBtn/Sep/PathTypeRow/Summary/ActionRow가 Content에 그대로 남아있을 수 있는데,
            // 이 컴포넌트의 런타임 추적 상태(UiRowPool 내부 풀 목록, _miscSpawned)는 직렬화되지 않아 그
            // baked-in 자식들의 존재를 전혀 모른 채 또 새로 만들어 중복이 생긴다. Init 시점에 Content의
            // 기존 자식을 전부 정리하고 항상 빈 상태에서 다시 짓는다 — 어차피 Refresh()가 매번 현재
            // 상태로 다시 채우므로 이전 내용을 보존할 이유가 없다.
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _planRowPool = new UiRowPool(_planRowTemplate, BuildPlanRowTemplate);
            _waypointRowPool = new UiRowPool(_waypointRowTemplate, BuildWaypointRowTemplate);
        }

        // 도감/노트 카드의 "계획에 추가" 액션이 호출한다(NotePanel 경유) — 현재 상세로 펼쳐진 계획이
        // 없으면 새 계획을 만들어 그쪽에 담는다(빈 손으로 액션을 누르는 경우를 위한 최소 UX).
        public void AddMapToSelectedPlan(string mapGuid)
        {
            if (string.IsNullOrEmpty(mapGuid)) return;
            if (string.IsNullOrEmpty(_selectedPlanId) || NoteModule.Instance.GetPlan(_selectedPlanId) == null)
                _selectedPlanId = NoteModule.Instance.CreatePlan().planId;

            NoteModule.Instance.AddWaypoint(_selectedPlanId, mapGuid);
            Refresh();
        }

        public void Refresh()
        {
            ClearMisc();

            foreach (var plan in NoteModule.Instance.Plans)
                PopulatePlanRow(_planRowPool.Get(_content), plan);
            _planRowPool.EndPass();

            BuildNewPlanButton();

            var selected = NoteModule.Instance.GetPlan(_selectedPlanId);
            if (selected != null)
                BuildPlanDetail(selected);
            _waypointRowPool.EndPass(); // 선택된 계획이 없으면 0개 사용 → 이전 행 전부 숨김
        }

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

        // ─── 계획 목록 (풀링) ─────────────────────────────────────

        private void PopulatePlanRow(GameObject rowGO, RouteWaypointPlan plan)
        {
            bool selected = plan.planId == _selectedPlanId;
            var img = rowGO.GetComponent<Image>();
            if (img != null) img.color = selected ? _colPlanRowSelected : _colPlanRow;

            var btn = rowGO.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => { _selectedPlanId = plan.planId; Refresh(); });
            }

            var tmp = rowGO.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = $"{plan.planName} ({plan.orderedMapGuids.Count})";
        }

        private GameObject BuildPlanRowTemplate()
        {
            var rowRT = NewRect(null, "PlanRow");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var img = AddImg(rowRT, _colPlanRow);
            var btn = rowRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var txtRT = NewRect(rowRT, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin = new Vector4(4f, 0f, 2f, 0f);

            rowRT.gameObject.SetActive(false);
            return rowRT.gameObject;
        }

        private void BuildNewPlanButton()
        {
            var rowRT = NewRect(_content, "NewPlanBtn");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var img = AddImg(rowRT, _colBtnGreen);
            var btn = rowRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                _selectedPlanId = NoteModule.Instance.CreatePlan().planId;
                Refresh();
            });

            MakeLabel(rowRT, "Text", "+ 새 계획", _rowFontSize, Color.white);
            _miscSpawned.Add(rowRT.gameObject);
        }

        // ─── 선택된 계획 상세 ─────────────────────────────────────

        private void BuildPlanDetail(RouteWaypointPlan plan)
        {
            BuildSeparator();
            BuildPathTypeRow(plan);

            for (int i = 0; i < plan.orderedMapGuids.Count; i++)
                PopulateWaypointRow(_waypointRowPool.Get(_content), plan, i);

            BuildSummaryRow(plan);
            BuildActionRow(plan);
        }

        private void BuildSeparator()
        {
            var rt = NewRect(_content, "Sep");
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            AddImg(rt, new Color(1f, 1f, 1f, 0.10f));
            _miscSpawned.Add(rt.gameObject);
        }

        private void BuildPathTypeRow(RouteWaypointPlan plan)
        {
            var rowRT = NewRect(_content, "PathTypeRow");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var hlg = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            MakePathTypeBtn(rowRT, plan, PathType.Shortest, "최단");
            MakePathTypeBtn(rowRT, plan, PathType.Balanced, "균형");
            MakePathTypeBtn(rowRT, plan, PathType.MinDifficulty, "최소난이도");

            _miscSpawned.Add(rowRT.gameObject);
        }

        private void MakePathTypeBtn(RectTransform parent, RouteWaypointPlan plan, PathType type, string label)
        {
            bool active = plan.pathType == type;
            var rt = NewRect(parent, "PT_" + type);
            var img = AddImg(rt, active ? _colBtnActive : _colBtnInactive);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                NoteModule.Instance.SetPlanPathType(plan.planId, type);
                Refresh();
            });

            MakeLabel(rt, "Text", label, _rowFontSize, Color.white);
        }

        // ─── 목적지 순서 (풀링) ───────────────────────────────────

        private void PopulateWaypointRow(GameObject rowGO, RouteWaypointPlan plan, int index)
        {
            var node = MapGraph.Instance?.GetNode(plan.orderedMapGuids[index]);
            var label = node != null ? node.nodeName : plan.orderedMapGuids[index];

            var tmp = rowGO.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = $"{index + 1}. {label}";

            BindIconBtn(rowGO, "BtnUp", () => { NoteModule.Instance.MoveWaypoint(plan.planId, index, -1); Refresh(); });
            BindIconBtn(rowGO, "BtnDown", () => { NoteModule.Instance.MoveWaypoint(plan.planId, index, 1); Refresh(); });
            BindIconBtn(rowGO, "BtnDelete", () => { NoteModule.Instance.RemoveWaypoint(plan.planId, index); Refresh(); });
        }

        private static void BindIconBtn(GameObject row, string childName, Action onClick)
        {
            var btn = row.transform.Find(childName)?.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        private GameObject BuildWaypointRowTemplate()
        {
            var rowRT = NewRect(null, "WaypointRow");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            AddImg(rowRT, _colWaypointRow);

            var hlg = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(3, 3, 1, 1);
            hlg.spacing = 2f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var textRT = NewRect(rowRT, "Text");
            var textLe = textRT.gameObject.AddComponent<LayoutElement>();
            textLe.flexibleWidth = 1f;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _rowFontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            BuildIconBtnTemplateChild(rowRT, "BtnUp", "▲", 14f, _colBtnInactive);
            BuildIconBtnTemplateChild(rowRT, "BtnDown", "▼", 14f, _colBtnInactive);
            BuildIconBtnTemplateChild(rowRT, "BtnDelete", "삭제", 22f, _colBtnRed);

            rowRT.gameObject.SetActive(false);
            return rowRT.gameObject;
        }

        private void BuildIconBtnTemplateChild(RectTransform parent, string name, string label, float width, Color bg)
        {
            var rt = NewRect(parent, name);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
            var img = AddImg(rt, bg);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var txtRT = NewRect(rt, "Text");
            StretchFull(txtRT);
            var tmp = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = label;
            tmp.fontSize = _rowFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.color = Color.white;
        }

        // ─── 요약 / 실행 (많아야 1개 — 그냥 매번 새로 그림) ─────────

        private void BuildSummaryRow(RouteWaypointPlan plan)
        {
            var preview = NoteModule.Instance.ComputePreview(plan.planId);
            string status;
            Color color;
            if (plan.orderedMapGuids.Count == 0)
            {
                status = "목적지를 추가하세요";
                color = new Color(0.7f, 0.7f, 0.7f);
            }
            else if (!preview.IsValid)
            {
                status = "도달 불가 구간 포함";
                color = new Color(0.9f, 0.35f, 0.35f);
            }
            else if (preview.IsBlocked)
            {
                status = "통과 불가 구간 포함";
                color = new Color(0.9f, 0.65f, 0.25f);
            }
            else
            {
                status = $"구간 {preview.Legs.Count}개 · 총 난이도 {preview.TotalDifficulty:F1}";
                color = new Color(0.75f, 0.75f, 0.75f);
            }

            var rowRT = NewRect(_content, "Summary");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var tmp = MakeLabel(rowRT, "Text", status, _rowFontSize, color);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin = new Vector4(4f, 0f, 0f, 0f);

            _miscSpawned.Add(rowRT.gameObject);
        }

        private void BuildActionRow(RouteWaypointPlan plan)
        {
            var rowRT = NewRect(_content, "ActionRow");
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 12f;
            le.flexibleWidth = 1f;
            var hlg = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            var execution = NoteModule.Instance.CurrentExecution;
            bool isHaltedThis = execution != null && execution.planId == plan.planId && execution.isHalted;

            if (isHaltedThis)
                MakeActionBtn(rowRT, "재개", _colBtnGreen, () => { NoteModule.Instance.ResumePlan(plan.planId); Refresh(); });
            else
                MakeActionBtn(rowRT, "실행", _colBtnGreen, () => { NoteModule.Instance.ExecutePlan(plan.planId); Refresh(); });

            MakeActionBtn(rowRT, "삭제", _colBtnRed, () =>
            {
                NoteModule.Instance.RemovePlan(plan.planId);
                _selectedPlanId = "";
                Refresh();
            });

            _miscSpawned.Add(rowRT.gameObject);
        }

        // ─── UI 헬퍼 ─────────────────────────────────────────────

        private void MakeActionBtn(RectTransform parent, string label, Color bg, Action onClick)
        {
            var rt = NewRect(parent, "Act_" + label);
            var img = AddImg(rt, bg);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            MakeLabel(rt, "Text", label, _rowFontSize, Color.white).alignment = TextAlignmentOptions.Center;
        }

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
}
