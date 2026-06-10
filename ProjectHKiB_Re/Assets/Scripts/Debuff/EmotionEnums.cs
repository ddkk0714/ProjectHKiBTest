public enum EmotionColor
{
    // 기본 감정색 - 실제 색상 단위만 사용
    SadnessBlue         = 0,
    SadnessSky          = 1,
    ExcitementDeepPink  = 2,
    HappinessYellow     = 3,
    AngerOrange         = 4,
    AngerScarlet        = 5,
    VoidBlack           = 6,
    FearDarkRed         = 7,

    // 반응 감정색
    Collapse            = 8,
    Madness             = 9,
    Longing             = 10,
    Resentment          = 11,
    VoidReaction        = 12,
    Sorrow              = 13,
    Fury                = 14,
    Panic               = 15,
    Bluff               = 16,
    Cancel              = 17,

    // 새 값은 반드시 여기 아래에 추가할 것 (기존 번호 절대 변경 금지)
}

// 상성 계산에서 "같은 감정"을 판단하는 그룹 단위.
// SadnessBlue/SadnessSky처럼 색상 변형이 달라도 같은 감정이면 동일 그룹.
public enum EmotionGroup
{
    Sadness,
    Excitement,
    Happiness,
    Anger,
    Void,
    Fear,
    Reaction,
}
