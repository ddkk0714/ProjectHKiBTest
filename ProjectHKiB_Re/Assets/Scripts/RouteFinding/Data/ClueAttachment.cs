using System;

// 단서 하나에 붙일 수 있는 첨부물의 종류.
// JSON에는 정수(enum index)로 저장되므로 기존 값의 순서를 바꾸지 말 것 — 뒤에만 추가한다.
public enum ClueAttachmentKind
{
    Image,   // 사진 — Resources 경로의 Sprite(또는 Texture2D)를 카드에 미리보기로 표시
    Audio,   // 소리 — Resources 경로의 AudioClip을 카드에서 재생
    MapRef   // 맵 참조 — 맵의 아이콘(MapNodeData.iconPath)과 이름을 표시, 누르면 지도에서 그 맵으로 이동
}

// 단서에 붙는 첨부물 하나. ClueData.attachments에 배열로 담기고 clues.json에 같이 저장된다
// (콘텐츠 작업자가 Editor/MapDatabaseEditorWindow.cs 단서 탭에서 채워 넣는다).
//
// 실제 에셋은 JSON에 담을 수 없으므로 Resources 상대 경로로 참조한다 — MapNodeData.wavePaths와
// 같은 방식이다. 로딩·캐싱은 ClueAttachmentService가 담당하고, 경로가 잘못돼도 카드가 깨지지 않고
// "(파일 없음)" 표시로 대체된다.
[Serializable]
public class ClueAttachment
{
    public ClueAttachmentKind kind;
    public string label;        // 카드에 표시할 이름. 비우면 종류 기본 이름(사진/소리) 또는 맵 이름이 쓰인다
    public string resourcePath; // Image/Audio 전용 — Resources 이후 상대 경로 (예: RouteFinding/Clues/photo_01)
    public string mapGuid;      // MapRef 전용 — MapNodeData.guid
}

// ClueAttachmentKind 표시 이름 정적 조회 테이블. ClueTypeConfig와 동일한 패턴.
public static class ClueAttachmentConfig
{
    public static string GetDisplayName(ClueAttachmentKind kind) => kind switch
    {
        ClueAttachmentKind.Image  => "사진",
        ClueAttachmentKind.Audio  => "소리",
        ClueAttachmentKind.MapRef => "맵",
        _                         => kind.ToString(),
    };
}
