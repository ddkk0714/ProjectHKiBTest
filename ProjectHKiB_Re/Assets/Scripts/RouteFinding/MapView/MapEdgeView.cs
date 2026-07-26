using UnityEngine;
using UnityEngine.UI;

namespace RouteFinding.MapView
{
    // 지도 그래프 위의 연결(간선) 하나를 표시하는 UI 컴포넌트.
    // MapViewer가 런타임에 생성하며, 직접 씬에 추가하지 않는다.
    //
    // 2026-07-14 — 전투(난이도/통과 조건/클리어)가 연결에서 맵으로 이동하면서, 간선은 이제
    // "두 맵이 이어져 있다는 사실 + 단서 공개 여부 + 추천 경로 하이라이트"만 표시한다.
    // 난이도 숫자·통과 불가(X) 표시는 MapNodeView 쪽으로 옮겨갔다.
    public class MapEdgeView : MonoBehaviour
    {
        public MapConnectionData Data { get; private set; }

        private RectTransform _rt;
        private Image         _line;

        private float _thicknessNormal    = 3f;
        private float _thicknessHighlight = 7f;

        private static readonly Color ColNoClue      = new(0.22f, 0.22f, 0.22f, 0.55f);
        private static readonly Color ColNormal      = new(0.40f, 0.50f, 0.75f, 0.80f);
        private static readonly Color ColBlockedPath = new(0.75f, 0.20f, 0.20f, 0.95f); // 추천 경로지만 통과 불가 맵을 포함
        private Color _colOnPath = new(1.00f, 0.82f, 0.08f, 1.00f);

        public void Init(MapConnectionData data)
        {
            Data  = data;
            _rt   = (RectTransform)transform;
            _line = GetComponent<Image>();

            _rt.sizeDelta = new Vector2(0f, _thicknessNormal);
        }

        // 경로 강조(선택 시 색상·두께) 스타일 설정. MapViewer가 PopulateGraph에서 주입한다.
        public void SetHighlightStyle(Color onPathColor, float thicknessNormal, float thicknessHighlight)
        {
            _colOnPath          = onPathColor;
            _thicknessNormal    = thicknessNormal;
            _thicknessHighlight = thicknessHighlight;

            if (_rt != null)
                _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, _thicknessNormal);
        }

        // 두 캔버스 좌표 사이의 선 위치·회전·길이를 설정한다.
        public void SetLayout(Vector2 fromPos, Vector2 toPos)
        {
            Vector2 dir = toPos - fromPos;
            Vector2 mid = fromPos + dir * 0.5f;

            _rt.anchoredPosition = mid;
            _rt.sizeDelta        = new Vector2(dir.magnitude, _rt.sizeDelta.y);
            _rt.localRotation    = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        // 지도에 그려질지 여부(= 두 노드 중 하나라도 밝혀진 경우).
        public void SetShown(bool shown) => gameObject.SetActive(shown);

        // isOnBlockedPath: 추천되었으나 통과 불가한 맵을 포함하는 경로에 포함됨 (선택 불가, 빨강 강조)
        public void SetState(bool hasClue, bool isOnPath, bool isOnBlockedPath)
        {
            Color col = (isOnPath, isOnBlockedPath, hasClue) switch
            {
                (true, _, _) => _colOnPath,
                (_, true, _) => ColBlockedPath,
                (_, _, true) => ColNormal,
                _            => ColNoClue,
            };

            if (_line != null)
                _line.color = col;

            bool highlight = isOnPath || isOnBlockedPath;
            if (_rt != null)
                _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, highlight ? _thicknessHighlight : _thicknessNormal);
        }
    }
}
