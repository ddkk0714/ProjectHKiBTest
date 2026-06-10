using UnityEngine;
using UnityEngine.EventSystems;

namespace RouteFinding.MapView
{
    // 획득한 단서가 가리키는 맵 옆에 표시되는 작은 마커.
    // 마우스 오버 시 MapViewer에 알려 추천 경로 툴팁을 띄운다.
    public class ClueMarkerView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public ClueData Clue { get; private set; }
        public System.Action<ClueMarkerView> OnHoverEnter;
        public System.Action<ClueMarkerView> OnHoverExit;

        public void Init(ClueData clue) => Clue = clue;

        public void OnPointerEnter(PointerEventData e) => OnHoverEnter?.Invoke(this);
        public void OnPointerExit(PointerEventData e)  => OnHoverExit?.Invoke(this);
    }
}
