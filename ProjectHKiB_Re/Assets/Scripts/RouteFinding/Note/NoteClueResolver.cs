using System.Linq;

namespace RouteFinding.Note
{
    // [신설, 2026-07-21] 노트에서 유저가 직접 단서를 생성할 수 있게 되면서, 노트 그래프/서랍이 다루는
    // "단서"가 두 소스로 갈렸다 — clues.json 기반 ClueData(MapGraph)와, 유저가 노트/도감에서 직접 만든
    // CodexUserEntry(CodexModule). 둘은 필드 이름이 달라(name/description vs title/content 등) 호출부마다
    // 분기하면 중복이 심해지므로, 이 헬퍼가 둘을 하나의 읽기 전용 뷰로 통일해서 돌려준다.
    public readonly struct ResolvedClue
    {
        public readonly string Name;
        public readonly string Description;
        public readonly string[] Keywords;
        public readonly CodexComment[] Comments;

        // 첨부물(사진/소리/맵). 유저가 노트·도감에서 직접 만든 단서(CodexUserEntry)는 파일을 고를
        // 수단이 없어 항상 비어 있다 — 호출부는 둘을 구분하지 않고 그냥 개수만 보면 된다.
        public readonly ClueAttachment[] Attachments;

        public ResolvedClue(string name, string description, string[] keywords, CodexComment[] comments,
            ClueAttachment[] attachments = null)
        {
            Name = name;
            Description = description;
            Keywords = keywords;
            Comments = comments;
            Attachments = attachments ?? System.Array.Empty<ClueAttachment>();
        }
    }

    public static class NoteClueResolver
    {
        // clues.json(MapGraph) 먼저 찾고, 없으면 유저가 만든 CodexUserEntry(CodexModule)에서 찾는다.
        // 둘 다 없으면 null(단서 삭제·핀 해제 등으로 참조가 끊긴 경우) — 호출부가 각자 폴백을 정한다.
        public static ResolvedClue? Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            var clue = MapGraph.Instance?.GetClue(id);
            if (clue != null)
                return new ResolvedClue(clue.name, clue.description, clue.keywords, clue.comments, clue.attachments);

            var userEntry = CodexModule.Instance?.UserEntries.FirstOrDefault(u => u.guid == id);
            if (userEntry != null)
                return new ResolvedClue(userEntry.title, userEntry.content, userEntry.keywords, userEntry.comments);

            return null;
        }
    }
}
