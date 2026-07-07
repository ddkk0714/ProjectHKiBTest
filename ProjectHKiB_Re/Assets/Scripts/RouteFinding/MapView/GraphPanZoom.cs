using UnityEngine;
using UnityEngine.EventSystems;

namespace RouteFinding.MapView
{
    // GraphArea에 붙는 줌·패닝 핸들러.
    // Init()으로 target(GraphContainer)과 viewport(GraphViewport)를 주입받는다.
    // 줌: 마우스 휠 — 커서 위치 기준으로 스케일 변경
    // 패닝: 좌클릭 드래그 — GraphContainer 위치 이동
    [RequireComponent(typeof(RectTransform))]
    public class GraphPanZoom : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        private RectTransform _target;    // GraphContainer (스케일/이동 대상)
        private RectTransform _viewport;  // GraphViewport (클리핑·좌표 기준)
        private Canvas        _canvas;

        private const float ZoomMin = 0.25f;
        private const float ZoomMax = 4.0f;

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

        private void Update()
        {
            if (_target == null) return;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.001f) return;

            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)transform, Input.mousePosition, WorldCam))
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _viewport, Input.mousePosition, WorldCam, out Vector2 mouseLocal))
                return;

            float oldScale = _scale;
            _scale = Mathf.Clamp(_scale * (1f + scroll * 2f), ZoomMin, ZoomMax);
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
