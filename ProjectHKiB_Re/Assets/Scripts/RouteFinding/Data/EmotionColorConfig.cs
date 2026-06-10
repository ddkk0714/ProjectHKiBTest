using UnityEngine;
using System.Collections.Generic;

// EmotionColor enum 값에 대한 표시 정보(이름, 색상) 정적 조회 테이블.
// 감정 추가 시 enum과 이 테이블만 함께 수정하면 된다.
public static class EmotionColorConfig
{
    private static readonly Dictionary<EmotionColor, (string name, Color color)> Table = new()
    {
        { EmotionColor.SadnessBlue,        ("슬픔",  new Color(0.20f, 0.40f, 1.00f)) },
        { EmotionColor.SadnessSky,         ("슬픔",  new Color(0.50f, 0.80f, 1.00f)) },
        { EmotionColor.ExcitementDeepPink, ("흥분",  new Color(1.00f, 0.10f, 0.50f)) },
        { EmotionColor.HappinessYellow,    ("행복",  new Color(1.00f, 0.90f, 0.20f)) },
        { EmotionColor.AngerOrange,        ("분노",  new Color(1.00f, 0.50f, 0.10f)) },
        { EmotionColor.AngerScarlet,       ("분노",  new Color(0.90f, 0.10f, 0.10f)) },
        { EmotionColor.VoidBlack,          ("공허",  new Color(0.15f, 0.15f, 0.15f)) },
        { EmotionColor.FearDarkRed,        ("공포",  new Color(0.50f, 0.00f, 0.00f)) },
    };

    public static string GetName(EmotionColor e) =>
        Table.TryGetValue(e, out var v) ? v.name : e.ToString();

    public static Color GetColor(EmotionColor e) =>
        Table.TryGetValue(e, out var v) ? v.color : Color.white;

    // EmotionColor → 감정 그룹 변환.
    // 색상 변형(Blue/Sky, Orange/Scarlet 등)이 달라도 같은 감정이면 같은 그룹 반환.
    public static EmotionGroup ToGroup(EmotionColor e) => e switch
    {
        EmotionColor.SadnessBlue        => EmotionGroup.Sadness,
        EmotionColor.SadnessSky         => EmotionGroup.Sadness,
        EmotionColor.ExcitementDeepPink => EmotionGroup.Excitement,
        EmotionColor.HappinessYellow    => EmotionGroup.Happiness,
        EmotionColor.AngerOrange        => EmotionGroup.Anger,
        EmotionColor.AngerScarlet       => EmotionGroup.Anger,
        EmotionColor.VoidBlack          => EmotionGroup.Void,
        EmotionColor.FearDarkRed        => EmotionGroup.Fear,
        _                               => EmotionGroup.Reaction,
    };
}
