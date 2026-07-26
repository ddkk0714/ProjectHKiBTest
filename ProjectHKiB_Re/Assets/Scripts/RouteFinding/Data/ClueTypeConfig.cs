using System.Collections.Generic;

// ClueType enum 값에 대한 표시 이름 정적 조회 테이블. EmotionColorConfig와 동일한 패턴.
public static class ClueTypeConfig
{
    private static readonly Dictionary<ClueType, string> Table = new()
    {
        { ClueType.Creature,    "생명체" },
        { ClueType.Location,    "장소" },
        { ClueType.PuzzleHint,  "퍼즐 힌트" },
        { ClueType.EventHint,   "이벤트 힌트" },
        { ClueType.TravelHint,  "이동 힌트" },
    };

    public static string GetDisplayName(ClueType type) =>
        Table.TryGetValue(type, out var name) ? name : type.ToString();
}
