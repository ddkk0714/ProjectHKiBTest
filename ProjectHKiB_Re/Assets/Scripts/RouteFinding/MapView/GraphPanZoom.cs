using UnityEngine;
using UnityEngine.EventSystems;

namespace RouteFinding.MapView
{
    // GraphArea에 붙는 줌·패닝 핸들러.
    // Init()으로 target(GraphContainer)과 viewport(GraphViewport)를 주입받는다.
    // 줌: 마우스 휠 — 커서 위치 기준으로 스케일 변경
    // 패닝: 좌클릭 드래그 — GraphContainer 위치 이동
    //
    // 2026-07-14 — 줌을 Input.GetAxis 폴링이 아니라 IScrollHandler.OnScroll(EventSystem 경유)로 변경.
    // 폴링 방식은 "지금 마우스가 이 사각형 범위 안에 있는가"만 boolean으로 확인하기 때문에, 그 위에
    // 다른 UI(드롭다운 옵션 목록의 ScrollRect 등)가 덮여 있어도 구분하지 못하고 배경 줌이 같이 동작해버렸다
    // (드롭다운을 열고 목록을 휠로 스크롤하면 뒤 지도까지 같이 줌되던 문제). EventSystem 기반 IScrollHandler는
    // 레이캐스트로 "현재 커서 아래 가장 위에 있는 대상"에게만 이벤트를 전달하므로, 드롭다운 목록처럼 위에 뜬
    // 다른 스크롤 가능 UI가 있으면 그쪽이 이벤트를 가로가고 이 핸들러는 아예 호출되지 않아 자연히 해결된다.
    // 드래그·클릭(IPointerDownHandler/IBeginDragHandler/IDragHandler)은 원래부터 EventSystem 기반이라
    // 이 문제가 없었다 — 줌만 폴링 방식이라 예외였던 것.
    [RequireComponent(typeof(RectTransform))]
    public class GraphPanZoom : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IScrollHandler
    {
        private RectTransform _target;    // GraphContainer (스케일/이동 대상)
        private RectTransform _viewport;  // GraphViewport (클리핑·좌표 기준)
        private Canvas        _canvas;

        private const float ZoomMin = 0.25f;
        private const float ZoomMax = 4.0f;

        // PointerEventData.scrollDelta는 휠 한 칸에 대략 ±1(환경에 따라 다름). 값이 클수록 휠 한 칸당
        // 스케일 변화가 커진다 — 2026-07-14: 기존 0.2f(휠 한 칸당 ~20% 변화)가 너무 민감하다는 피드백으로
        // 0.08f(칸당 ~8% 변화)로 완화. 인스펙터에서 추가 조정 가능.
        [SerializeField] private float _zoomSensitivity = 0.08f;

        private float   _scale = 1f;
        private Vector2 _dragStartLocal;
        private Vector2 _dragStartAnchor;

        public float Scale => _scale;

        public void Init(RectTransform target, RectTransform viewport, Canvas canvas)
        {
            _target   = target;
            _viewport = viewport;
            _canvas   = canvas;
        }

        private Camera WorldCam =>
            _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera : null;

        // EventSystem이 "지금 커서 아래 가장 위에 있는 대상"에게만 호출한다 — 드롭다운 옵션 목록처럼
        // 위에 뜬 다른 스크롤 UI가 있으면 그쪽이 이벤트를 가로채 이 메서드 자체가 호출되지 않는다.
        public void OnScroll(PointerEventData eventData)
        {
            if (_target == null) return;

            float scroll = eventData.scrollDelta.y;
            if (Mathf.Abs(scroll) < 0.001f) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _viewport, eventData.position, WorldCam, out Vector2 mouseLocal))
                return;

            float oldScale = _scale;
            _scale = Mathf.Clamp(_scale * (1f + scroll * _zoomSensitivity), ZoomMin, ZoomMax);
            if (Mathf.Approximately(oldScale, _scale)) return;

            // 커서 위치 기준 줌.
            // _viewport 피벗은 (0.5, 0.5)이므로 mouseLocal 원점이 뷰포트 중심.
            // _target.anchoredPosition은 뷰포트 좌하단(anchor 0,0) 기준이므로
            // 같은 공간으로 변환: mouseInAnchorSpace = mouseLocal + 뷰포트 반크기.
            Vector2 mouseInAnchorSpace = mouseLocal + _viewport.rect.size * 0.5f;
            float   ratio = _scale / oldScale;
            _target.anchoredPosition = mouseInAnchorSpace + (_target.anchoredPosition - mouseInAnchorSpace) * ratio;
            _target.localScale = Vector3.one * _scale;
        }

        // PointerDown을 수락해야 배경 드래그 시 BeginDrag가 이 핸들러로 전달된다.
        public void OnPointerDown(PointerEventData e)
        {
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_target == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _viewport, e.position, WorldCam, out _dragStartLocal);
            _dragStartAnchor = _target.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_target == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _viewport, e.position, WorldCam, out Vector2 cur);
            _target.anchoredPosition = _dragStartAnchor + (cur - _dragStartLocal);
        }

        // localPos(= GraphContainer 기준, 스케일 적용 전 좌표)가 뷰포트 중앙에 오도록 이동.
        public void FocusOn(Vector2 localPos)
        {
            if (_target == null || _viewport == null) return;
            _target.anchoredPosition = _viewport.rect.size * 0.5f - localPos * _scale;
        }

        // 지도 원점 복귀 — 팬/줌을 BuildScrollGraph 직후의 초기 상태(스케일 1, 앵커 위치 0,0)로 되돌린다.
        public void ResetView()
        {
            if (_target == null) return;
            _scale = 1f;
            _target.localScale = Vector3.one;
            _target.anchoredPosition = Vector2.zero;
        }
    }
}
