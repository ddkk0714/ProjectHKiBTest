// EmotionColor(기존 감정 버프/복합 감정)와는 별개 개념.
// 예: 복합 감정 Collapse(붕괴)와 역치 상태 Doom(파멸)은 이름이 비슷해도 다르다 (spec §4.3).
public enum EmotionState
{
    Madness, // 광기 — +y(각성) 역치
    Doom,    // 파멸 — -x(부정) 역치
    Ecstasy, // 황홀 — +x(긍정) 역치
    Sleep    // 잠   — -y(비각성) 역치
}
