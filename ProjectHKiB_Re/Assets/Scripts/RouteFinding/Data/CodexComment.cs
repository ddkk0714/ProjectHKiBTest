using System;

// 도감 카드에 붙는 NPC/시스템 코멘트 — 플레이어가 입력하는 게 아니라, 특정 이벤트(단서 획득 직후,
// 특정 조건 충족 등)가 트리거될 때 미리 정해진 대사가 붙는 방식이다(Clue_System.md 1-4장 확정 사항).
// ClueData/CodexUserEntry 양쪽이 공유하는 데이터 — 콘텐츠 작업자가 Editor/MapDatabaseEditorWindow.cs에서
// 직접 채워 넣는다(대사 시스템이 따로 생기면 그쪽으로 이관 예정, 1-4장 참고).
[Serializable]
public class CodexComment
{
    public string author;    // 코멘트를 다는 NPC/시스템 캐릭터 이름 (예: "델타")
    public string text;
    public string createdAt; // 표시용 시간 텍스트(예: "00:00"). 빈 문자열이면 카드에 표시 안 함
}
