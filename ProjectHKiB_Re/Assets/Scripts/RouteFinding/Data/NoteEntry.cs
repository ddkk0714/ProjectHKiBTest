using System;

// 노트에 무엇을 통해 담겼는지 — 경로 연동 자동 편입 / 도감 수동 핀.
// (2026-07-14: 미획득 단서 후보 자동 노출 기능은 제거됨 — 요청으로 삭제, 노트는 이제 항상 획득한
// 단서만 다룬다. 필요해지면 git 히스토리에서 복원)
public enum NotePinReason
{
    RouteLinked, // 선택된 경로가 지나는 맵과 연관돼 자동 편입됨 (0단계)
    ManualPin,   // 도감에서 수동으로 핀함 (2단계)
}

// 노트 안의 단서 한 줄. ClueData 자체를 복제하지 않고 clueId로 원본을 참조만 한다
// (MapGraph.GetClue(clueId)로 조회).
[Serializable]
public class NoteEntry
{
    public string clueId;
    public NotePinReason reason;
    public string linkedRoutePlanId; // RouteLinked인 경우 어느 이동 계획에서 딸려왔는지 (4단계부터 사용, 0단계는 항상 빈 문자열)
}
