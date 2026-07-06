using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.MapView
{
    // 지도 그래프 위의 맵 노드 하나를 표시하는 UI 컴포넌트.
    // MapViewer가 런타임에 생성하며, 직접 씬에 추가하지 않는다.
    public class MapNodeView : MonoBehaviour
    {
        public MapNodeData Data { get; private set; }
        public event Action<MapNodeView> OnClicked;

        private Image             _bg;
        private TextMeshProUGUI   _label;
        private Button            _btn;

        // 노드 상태별 색상
        private static readonly Color ColNoClue   = new(0.10f, 0.10f, 0.12f);
        private static readonly Color ColHasClue  = new(0.25f, 0.38f, 0.65f);
        private static readonly Color ColVisited  = new(0.50f, 0.72f, 1.00f);
        private static readonly Color ColStart    = new(0.28f, 0.78f, 0.38f);
        private static readonly Color ColSelected = new(1.00f, 0.82f, 0.08f);
        private static readonly Color ColOnPath   = new(1.00f, 0.52f, 0.04f);
        private static readonly Color ColBlockedPath = new(0.75f, 0.20f, 0.20f); // 추천되었으나 통과 불가한 경로에 포함됨

        public void Init(MapNodeData data)
        {
            Data   = data;
            _bg    = GetComponent<Image>();
            _label = GetComponentInChildren<TextMeshProUGUI>();
            _btn   = GetComponent<Button>();

            if (_btn != null)
                _btn.onClick.AddListener(() => OnClicked?.Invoke(this));
        }

        // 지도에 그려질지 여부(= 밝혀진 노드이거나, 밝혀진 노드와 맞닿은 간선의 반대편인 경우).
        // 어느 쪽에도 해당하지 않으면 완전히 숨긴다.
        public void SetShown(bool shown) => gameObject.SetActive(shown);

        // visited / hasClue / isStart / isSelected / isOnPath / isOnBlockedPath 중 우선순위가 높은 상태를 시각화.
        // known: 단서 보유 노드이거나 그런 노드와 한 칸 맞닿은 이웃이면 true (MapViewer가 계산해 넘김).
        // known이 아니면 애초에 화면에 그려지지 않으므로(SetShown) 실질적으로는 항상 true로 들어오지만,
        // "이름 표시·클릭 가능" 여부의 기준을 이 값 하나로 통일해 둔다 — 그래야 다음 목적지로 선택해
        // 실제로 가볼 수 있다 (단서 없이도 한 칸 이웃까지는 목적지로 삼을 수 있어야 탐사가 진행됨).
        public void SetState(bool visited, bool hasClue, bool isStart, bool isSelected, bool isOnPath, bool isOnBlockedPath, bool known)
        {
            if (_bg == null)
            {
                Debug.LogWarning($"[MapNodeView.SetState] {name}: _bg가 null! Image를 찾지 못했습니다.");
                return;
            }

            Color col = (isSelected, isOnPath, isOnBlockedPath, isStart, visited, hasClue) switch
            {
                (true, _, _, _, _, _) => ColSelected,
                (_, true, _, _, _, _) => ColOnPath,
                (_, _, true, _, _, _) => ColBlockedPath,
                (_, _, _, true, _, _) => ColStart,
                (_, _, _, _, true, _) => ColVisited,
                (_, _, _, _, _, true) => ColHasClue,
                _                     => ColNoClue,
            };
            _bg.color = col;

            if (_label != null) _label.text = known ? Data.nodeName : string.Empty;
            if (_btn != null) _btn.interactable = known;
        }
    }
}
