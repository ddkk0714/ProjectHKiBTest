using System;

// 단서 하나에 붙일 수 있는 첨부물의 종류.
// JSON에는 정수(enum index)로 저장되므로 기존 값의 순서를 바꾸지 말 것 — 뒤에만 추가한다.
public enum ClueAttachmentKind
{
    Image,   // 사진 — Addressable 주소의 Sprite(또는 Texture2D)를 카드에 미리보기로 표시
    Audio,   // 소리 — Addressable 주소의 AudioClip을 카드에서 재생
    MapRef   // 맵 참조 — 맵의 아이콘(MapNodeData.iconAddress)과 이름을 표시, 누르면 지도에서 그 맵으로 이동
}

// 단서에 붙는 첨부물 하나. ClueData.attachments에 배열로 담기고 clues.json에 같이 저장된다
// (콘텐츠 작업자가 Editor/MapDatabaseEditorWindow.cs 단서 탭에서 채워 넣는다).
//
// 실제 에셋은 JSON에 담을 수 없으므로 **Addressable 주소** 문자열로 참조한다 — MapDataSO.mapAddressableID가
// 맵 씬을 가리키는 방식과 같다(문자열 하나만 데이터에 남기고, 에셋은 Addressables 그룹이 소유한다).
// 로딩·캐싱은 ClueAttachmentService가 담당하고, 주소가 잘못돼도 카드가 깨지지 않고 "(파일 없음)"으로
// 대체된다.
//
// [왜 Resources가 아닌가] Resources 폴더는 폴더 전체가 항상 빌드에 통째로 포함된다 — 단서 첨부물은
// 콘텐츠가 늘어나는 대로 계속 불어나는 자산이라 이 비용이 그대로 누적된다. Addressable은 그룹 단위로
// 빌드/배포를 떼어낼 수 있고, 등록 누락도 편집기의 "첨부물 Addressable 검증"으로 잡아낼 수 있다.
[Serializable]
public class ClueAttachment
{
    public ClueAttachmentKind kind;
    public string label;    // 카드에 표시할 이름. 비우면 종류 기본 이름(사진/소리) 또는 맵 이름이 쓰인다
    public string address;  // Image/Audio 전용 — Addressable 주소 (예: clue_photo_01)
    public string mapGuid;  // MapRef 전용 — MapNodeData.guid
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
