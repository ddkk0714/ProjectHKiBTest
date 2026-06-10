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

            if (_label != null)
                _label.text = data.nodeName;

            var btn = GetComponent<Button>();
            if (btn == null) return;

            btn.onClick.AddListener(() => OnClicked?.Invoke(this));
        }

        // visited / hasClue / isStart / isSelected / isOnPath / isOnBlockedPath 중 우선순위가 높은 상태를 시각화.
        public void SetState(bool visited, bool hasClue, bool isStart, bool isSelected, bool isOnPath, bool isOnBlockedPath)
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
        }
    }
}
