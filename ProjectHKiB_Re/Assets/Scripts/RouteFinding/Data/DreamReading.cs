using System;
using UnityEngine;

// 해몽 레시피 하나 — "이 단서들을 노트에서 서로 이으면 이런 규칙을 깨닫는다".
//
// [성립 조건]
//   1. requiredClueIds가 전부 획득된 상태여야 한다.
//   2. 그 단서들이 노트에서 하나의 연결 덩어리를 이뤄야 한다(NoteModule.ClueLinks 기준).
//      전부가 서로 직접 이어질 필요는 없다 — A-B, B-C면 A·B·C가 한 덩어리로 인정된다.
//      재료가 하나뿐이면 간선 없이 노트에 올려두기만 해도 성립한다(1:1 해석용 퇴화 형태).
//
// 기획서의 EVT-003 예시는 재료 1개짜리로도, 2개 이상을 엮는 조합 퍼즐로도 표현된다 —
// 재료 개수만 데이터에서 바꾸면 되므로 기획이 뒤집혀도 코드는 그대로다.
[Serializable]
public class DreamReading
{
    // 세이브에 남는 식별자. 콘텐츠 작업자가 직접 정한다(예: "reading_wing", "reading_eye").
    public string id;

    // 해석 카드에 뜰 제목과 본문. 본문에 "깨달은 규칙"을 그대로 쓴다
    // (예: "보스가 날개로 내리칠 때 패링한 후 [가위] 인터랙션을 할 것.").
    public string title;
    [TextArea(2, 6)] public string interpretation;

    public string[] requiredClueIds;

    // 성립 시 이 플래그들을 unlockValue로 세팅한다(FLAG_USE_SCISSORS 등).
    public EventFlagSO[] unlockFlags;
    public int unlockValue = 1;
}
