using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RouteFinding.MapView
{
    // 지도 그래프 위의 연결(간선) 하나를 표시하는 UI 컴포넌트.
    // 선(Image)은 이 GO에 붙고, 난이도 레이블은 회전되지 않는 별도 GO(_diffLabel)에 있다.
    // MapViewer가 런타임에 생성하며, 직접 씬에 추가하지 않는다.
    public class MapEdgeView : MonoBehaviour
    {
        public MapConnectionData Data { get; private set; }

        private RectTransform   _rt;
        private Image           _line;
        private TextMeshProUGUI _diffLabel; // 회전 없이 고정된 별도 컨테이너에 위치

        private float _thicknessNormal    = 3f;
        private float _thicknessHighlight = 7f;

        private static readonly Color ColNoClue     = new(0.22f, 0.22f, 0.22f, 0.55f);
        private static readonly Color ColNormal     = new(0.40f, 0.50f, 0.75f, 0.80f);
        private static readonly Color ColCleared    = new(0.32f, 0.80f, 0.45f, 0.88f);
        private static readonly Color ColLocked     = new(0.55f, 0.18f, 0.55f, 0.90f); // 필수 장비 미충족 — 통과 불가
        private static readonly Color ColBlockedPath = new(0.75f, 0.20f, 0.20f, 0.95f); // 추천 경로지만 통과 불가 구간
        private static readonly Color ColLabelDefault = new(1f, 1f, 1f, 0.88f);
        private Color _colOnPath = new(1.00f, 0.82f, 0.08f, 1.00f);

        // diffLabel: _labelContainer 아래의 별도 GO. MapViewer가 외부에서 주입한다.
        public void Init(MapConnectionData data, TextMeshProUGUI diffLabel)
        {
            Data       = data;
            _rt        = (RectTransform)transform;
            _line      = GetComponent<Image>();
            _diffLabel = diffLabel;

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

        // 두 캔버스 좌표 사이의 선 위치·회전·길이를 설정하고, 레이블을 중점 위에 배치한다.
        public void SetLayout(Vector2 fromPos, Vector2 toPos)
        {
            Vector2 dir = toPos - fromPos;
            Vector2 mid = fromPos + dir * 0.5f;

            _rt.anchoredPosition = mid;
            _rt.sizeDelta        = new Vector2(dir.magnitude, _rt.sizeDelta.y);
            _rt.localRotation    = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

            if (_diffLabel != null)
                ((RectTransform)_diffLabel.transform).anchoredPosition = mid + Vector2.up * 3f;
        }

        // isOnBlockedPath: 추천되었으나 통과 불가한 경로에 포함됨 (선택 불가, 빨강 강조)
        // isPassable/requiredGears: 필수 장비 충족 여부 — 미충족이면 보라색 + "X" 표시
        public void SetState(bool cleared, bool hasClue, bool isOnPath, bool isOnBlockedPath, bool isPassable, EmotionColor[] requiredGears, float difficulty)
        {
            bool hasRequirement = requiredGears != null && requiredGears.Length > 0;
            bool locked = hasRequirement && !isPassable;

            Color col = (isOnPath, isOnBlockedPath, locked, cleared, hasClue) switch
            {
                (true, _, _, _, _) => _colOnPath,
                (_, true, _, _, _) => ColBlockedPath,
                (_, _, true, _, _) => ColLocked,
                (_, _, _, true, _) => ColCleared,
                (_, _, _, _, true) => ColNormal,
                _                  => ColNoClue,
            };

            if (_line != null)
                _line.color = col;

            bool highlight = isOnPath || isOnBlockedPath;
            if (_rt != null)
                _rt.sizeDelta = new Vector2(_rt.sizeDelta.x, highlight ? _thicknessHighlight : _thicknessNormal);

            if (_diffLabel != null)
            {
                if (locked)
                {
                    _diffLabel.text  = "X";
                    _diffLabel.color = ColLocked;
                }
                else
                {
                    _diffLabel.text  = hasClue || cleared ? difficulty.ToString("F0") : "?";
                    _diffLabel.color = hasRequirement ? EmotionColorConfig.GetColor(requiredGears[0]) : ColLabelDefault;
                }
            }
        }
    }
}
