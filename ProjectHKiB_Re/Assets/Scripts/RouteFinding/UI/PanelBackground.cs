using UnityEngine;
using UnityEngine.UI;

// 패널/영역 배경 Image를 색상 또는 이미지(Sprite)로 채우는 공용 헬퍼 — RouteFinding의 각 패널
// (MapPanel/CodexPanel/NotePanel)과 그 하위 주요 영역(사이드패널/드로어/카드), 서브 팝업(보드·생성창·
// 필터창 등)이 전부 이 함수로 배경을 그린다. 기존 "[SerializeField] Color ..BgColor" 옆에 짝을 이루는
// "[SerializeField] Sprite ..BgSprite"를 추가하고 이 함수로 넘기기만 하면, 스프라이트가 지정된 경우
// 그 이미지로 채워지고 비어있으면 기존처럼 단색으로 표시된다.
//
// 프리팹/씬에 저장된 기존 인스턴스를 재사용하는 경로(FinalizePanel/BindRefsFromHierarchy)에서도
// 반드시 호출해야 한다 — 그렇지 않으면 이 세션에서 반복적으로 겪은 "코드/인스펙터 값을 바꿔도 저장된
// 프리팹/씬 인스턴스가 재사용되면서 반영 안 되는" 문제가 배경 이미지 설정에도 그대로 재현된다.
//
// Image.Type을 항상 Sliced로 쓰는 이유 — 기존 도트풍 UI(UIManager의 MenuWindow 등, Assets/Images/UI/new/UI.png
// 스프라이트 시트)가 9슬라이스 테두리가 있는 프레임 스프라이트(UI_Frame_normal/UI_Frame_small)와
// 테두리 없는 단순 채우기 스프라이트(UI_Back)를 섞어 쓰는데, 스프라이트 자체의 border 메타데이터가
// 0이면 Sliced도 그냥 단순 스트레치와 동일하게 동작하므로 두 경우 모두 이 한 코드로 커버된다 —
// 어떤 스프라이트를 넣든(테두리 있든 없든) 별도 분기 없이 항상 의도대로 나온다.
public static class PanelBackground
{
    public static Image Apply(RectTransform rt, Color fallbackColor, Sprite sprite)
    {
        if (rt == null) return null;
        var img = rt.GetComponent<Image>();
        if (img == null) img = rt.gameObject.AddComponent<Image>();
        return Apply(img, fallbackColor, sprite);
    }

    public static Image Apply(Image img, Color fallbackColor, Sprite sprite)
    {
        if (img == null) return null;
        if (sprite != null)
        {
            img.sprite = sprite;
            img.color = Color.white;
            img.type = Image.Type.Sliced;
        }
        else
        {
            img.sprite = null;
            img.color = fallbackColor;
            img.type = Image.Type.Simple;
        }
        return img;
    }
}
