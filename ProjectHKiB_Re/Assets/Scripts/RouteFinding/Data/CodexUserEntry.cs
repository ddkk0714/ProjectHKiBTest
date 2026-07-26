using System;

// 플레이어가 도감 안에서 직접 작성하는 자유 메모("빈 단서"). clues.json 같은 정적 리소스가 아니라
// 런타임에 CodexModule이 들고 있는 데이터 — 현재는 세이브 미연동(6단계 예정, 재시작하면 사라짐).
[Serializable]
public class CodexUserEntry
{
    public string guid;
    public string title;
    public string content;

    // ClueData.codexMapGuid(실제 맵 GUID)와 달리, 이건 플레이어가 직접 입력한 분류 라벨(자유 텍스트)이다.
    // 유저 메모는 지도 데이터와 연결될 필요가 없으므로 굳이 GUID로 강제하지 않는다 — 3단계 결정.
    // 비어있으면 도감 트리에서 "기타"로 분류된다.
    public string mapCategory;

    public string[] keywords; // 플레이어가 직접 입력 (자동 타입 키워드는 없음 — ClueType 분류 대상이 아니라서)

    // 4단계(2026-07-14) — ClueData와 같은 타입을 공유하지만, 유저 메모에 NPC 코멘트를 붙일 수단이
    // 아직 없어(콘텐츠 작업자가 편집하는 화면은 clues.json 전용) 항상 빈 배열로 유지된다.
    // 필드 자체는 카드 표시 코드(CodexCardView)가 ClueData/CodexUserEntry를 동일하게 다룰 수 있도록 둔다.
    public CodexComment[] comments = Array.Empty<CodexComment>();
}
