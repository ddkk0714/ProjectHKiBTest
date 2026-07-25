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
    Collapse            = 8, // EmotionVector Step 4.2에서 복합 감정으로 유지(좌표 등록됨)

    // EmotionVector Phase 4(감정 벡터 모듈, useVectorCombination) 도입으로 "반응 결과색"으로서는
    // 미사용 — 값/이름은 직렬화된 에셋이 참조하므로 절대 삭제/변경 금지. 필요해지면 이 [Obsolete]
    // 줄만 지우면 즉시 원복됨(경고만 내는 표시일 뿐, 컴파일에러 아님).
    // 주의: Madness_Other.asset/Sorrow_Other.asset은 EmotionVector Phase 3에서 여전히 SO 참조로
    // 재사용 중 — 아래 [Obsolete]는 "반응색으로서의 역할"에만 해당, 그 에셋들 자체는 살아있음.
    [System.Obsolete("EmotionVector Phase 4(useVectorCombination)에서 각성 역치 상태로 흡수됨 — 반응색으로는 미사용. Madness_Other.asset은 여전히 재사용 중이므로 에셋 자체는 건드리지 말 것")]
    Madness             = 9,
    Longing             = 10, // EmotionVector Step 4.2에서 복합 감정으로 유지(좌표 등록됨)
    [System.Obsolete("EmotionVector Phase 4(useVectorCombination)에서 대체(Replace) 판정으로 흡수됨 — 반응색으로 미사용")]
    Resentment          = 11,
    [System.Obsolete("EmotionVector Phase 4(useVectorCombination)에서 공허 촉매(§2.4)로 대체됨 — 반응색으로 미사용")]
    VoidReaction        = 12,
    [System.Obsolete("EmotionVector Phase 4(useVectorCombination)에서 동일 사분면 중첩으로 흡수됨 — 반응색으로는 미사용. Sorrow_Other.asset은 여전히 재사용 중이므로 에셋 자체는 건드리지 말 것")]
    Sorrow              = 13,
    [System.Obsolete("EmotionVector Phase 4(useVectorCombination)에서 동일 사분면 중첩으로 흡수됨 — 반응색으로 미사용")]
    Fury                = 14,
    [System.Obsolete("EmotionVector Phase 4(useVectorCombination)에서 대응하는 대각선 조합이 없어 미사용 분기가 됨 — 반응색으로 미사용")]
    Panic               = 15,
    Bluff               = 16, // EmotionVector Step 4.2에서 3사분면 복합으로 재활용(좌표 등록됨, 유지)
    [System.Obsolete("EmotionVector Phase 4(useVectorCombination)에서 상쇄(Cancel) 판정이 색을 남기지 않음 — 반응색으로 미사용")]
    Cancel              = 17,

    // EmotionVector Phase 5 — 신규 감정 5종. 좌표만 SO로 추가하는 게 원칙이었으나 EmotionColor가
    // C# enum이라 새 감정은 결국 코드 한 줄(enum 값 추가)이 불가피함 — Phase 5 게이트 자체가 예상한
    // "설계 결함" 지점(사용자 확인 후 진행, 2026-07-25). 기존 번호는 그대로 두고 아래에만 추가.
    Disgust             = 18, // 혐오
    Stress              = 19, // 스트레스
    Satisfaction        = 20, // 만족
    Peace               = 21, // 평안
    Fatigue             = 22, // 피로

    // Step 5.2 — 사랑. spec §2.5: 1사분면 고각성·고긍정이라 황홀(Ecstasy) 역치 방향과 거의 겹쳐서
    // 밸런싱 부담이 크다고 보류돼 있던 것 — "일단 임시값" 지시로 스펙 좌표 그대로 등록만 해둔다.
    // 좌표/스탯 전부 잠정치, 실제 밸런싱은 재논의 후 진행할 것.
    Love                = 23, // 사랑 (임시)

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
