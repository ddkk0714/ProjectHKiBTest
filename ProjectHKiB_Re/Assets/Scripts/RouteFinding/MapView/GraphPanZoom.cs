using System;
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
        // 0.08f(칸당 ~8% 변화)로 완화. 2026-07-21: 노트 그래프에도 이 컴포넌트를 재사용하게 되면서 다시
        // 0.02f(칸당 ~2% 변화)로 더 완화 — MapView·노트 둘 다 이 기본값을 공유한다. 인스펙터에서 추가 조정 가능.
        [SerializeField] private float _zoomSensitivity = 0.02f;

        private float   _scale = 1f;
        private Vector2 _dragStartLocal;
        private Vector2 _dragStartAnchor;

        // [2026-07-21, 노트의 단서 그래프에 팬·줌을 추가하며 신설] 팬 범위 제한 — 기본은 지도(MapViewer)와
        // 동일하게 무제한이다. NotePanel처럼 콘텐츠 크기가 고정폭인 경우에만 ConfigureBounds()로 켠다.
        [Header("팬 범위 제한 (선택 — 기본은 지도처럼 무제한)")]
        [SerializeField] private bool _clampToContent = false;
        [SerializeField] private float _clampMargin = 80f; // 클램프 시 콘텐츠 바깥으로 허용할 여유(px)

        // [2026-07-21 신설] 노드처럼 배경 위에서 스스로를 드래그하는 자식 UI가, 자신의 드래그가 진행되는
        // 동안 배경 패닝이 동시에 움직이지 않도록 막을 수 있는 훅 — 기본 false. 이 문제 자체가 없는
        // MapViewer(노드가 고정 위치)는 이 프로퍼티를 건드리지 않으므로 기존 동작에 영향이 없다.
        public bool SuppressDrag { get; set; }

        // [2026-07-21, 추가 보강] SuppressDrag만으로는 배경이 같이 움직이는 게 실제로 계속 재현돼 —
        // 정확한 원인(레이캐스트 우선순위 등)을 특정하지 못한 채로도 확실히 막기 위해, 위치/스케일을
        // 아예 물리적으로 고정해버리는 이중 안전장치를 추가한다. LockPosition() 호출 시점의 값을
        // 저장해두고, 잠긴 동안은 매 프레임 LateUpdate에서 그 값으로 강제로 되돌린다 — 그 사이에
        // 어떤 경로로든 anchoredPosition/localScale이 바뀌어도(원인 불문) 화면상 배경은 고정된다.
        private bool _positionLocked;
        private Vector2 _lockedAnchoredPosition;
        private float _lockedScale;

        public void LockPosition()
        {
            _positionLocked = true;
            if (_target == null) return;
            _lockedAnchoredPosition = _target.anchoredPosition;
            _lockedScale = _scale;
        }

        public void UnlockPosition() => _positionLocked = false;

        private void LateUpdate()
        {
            if (!_positionLocked) return;

            // [버그 수정, 2026-07-21] 잠금을 건 오브젝트(NoteNodeDragHandle이 붙은 노드)가 OnEndDrag보다
            // 먼저 실행되는 클릭 핸들러에 의해 드래그 도중 SetActive(false)로 꺼져버리는 경우(펼쳐진
            // 단서 노드 — NoteClickToToggle 클래스 주석 (2) 참고) OnEndDrag 자체가 호출되지 못해
            // UnlockPosition()이 영원히 안 불리고 배경이 완전히 굳어버렸다 — 근본 원인(드래그 동반 클릭)은
            // NoteClickToToggle 쪽에서 막았지만, 비슷한 경로로 또 잠금이 안 풀리는 경우에 대비해 왼쪽
            // 마우스 버튼이 이미 떼어져 있으면 무조건 강제로 풀어버리는 방어 로직을 추가한다.
            if (!Input.GetMouseButton(0))
            {
                _positionLocked = false;
                return;
            }

            if (_target == null) return;
            _target.anchoredPosition = _lockedAnchoredPosition;
            _target.localScale = Vector3.one * _lockedScale;
        }

        public float Scale => _scale;

        // [신설, 2026-08-11] 배율이 바뀔 때마다 알린다 — 지도(MapViewer)가 노드의 호버/클릭 판정 영역을
        // 역보정하는 데 쓴다(줌 아웃하면 노드가 작아져 마우스를 올리기 어려워지는 문제).
        public event Action<float> OnScaleChanged;

        public void Init(RectTransform target, RectTransform viewport, Canvas canvas)
        {
            _target   = target;
            _viewport = viewport;
            _canvas   = canvas;
        }

        // 외부(NotePanel 등)에서 이 인스턴스에만 팬 범위 제한을 켜고 싶을 때 호출한다.
        public void ConfigureBounds(bool clamp, float margin)
        {
            _clampToContent = clamp;
            _clampMargin = margin;
        }

        private Camera WorldCam =>
            _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera : null;

        // EventSystem이 "지금 커서 아래 가장 위에 있는 대상"에게만 호출한다 — 드롭다운 옵션 목록처럼
        // 위에 뜬 다른 스크롤 UI가 있으면 그쪽이 이벤트를 가로채 이 메서드 자체가 호출되지 않는다.
        public void OnScroll(PointerEventData eventData)
        {
            if (_target == null || SuppressDrag) return;

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
            ClampToContentBounds();
            OnScaleChanged?.Invoke(_scale);
        }

        // PointerDown을 수락해야 배경 드래그 시 BeginDrag가 이 핸들러로 전달된다.
        public void OnPointerDown(PointerEventData e)
        {
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_target == null || SuppressDrag) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _viewport, e.position, WorldCam, out _dragStartLocal);
            _dragStartAnchor = _target.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_target == null || SuppressDrag) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _viewport, e.position, WorldCam, out Vector2 cur);
            _target.anchoredPosition = _dragStartAnchor + (cur - _dragStartLocal);
            ClampToContentBounds();
        }

        // [2026-07-21 신설] _clampToContent가 꺼져 있으면(기본, MapViewer) 완전히 무시된다 — 콘텐츠가
        // 뷰포트보다 작으면 그 안에서만, 크면 여백(_clampMargin)만큼만 벗어나도록 anchoredPosition을 되접는다.
        private void ClampToContentBounds()
        {
            if (!_clampToContent || _target == null || _viewport == null) return;

            var pos = _target.anchoredPosition;
            pos.x = ClampAxis(pos.x, _target.rect.width * _scale, _viewport.rect.width);
            pos.y = ClampAxis(pos.y, _target.rect.height * _scale, _viewport.rect.height);
            _target.anchoredPosition = pos;
        }

        private float ClampAxis(float pos, float contentSize, float viewportSize)
        {
            float min, max;
            if (contentSize <= viewportSize)
            {
                // 콘텐츠가 뷰포트보다 작으면 그 안에서만 움직이게(0 ~ 남는 여유분).
                min = 0f;
                max = viewportSize - contentSize;
            }
            else
            {
                min = viewportSize - contentSize - _clampMargin;
                max = _clampMargin;
            }
            return Mathf.Clamp(pos, min, max);
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
            OnScaleChanged?.Invoke(_scale);
        }
    }
}
