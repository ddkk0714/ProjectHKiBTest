using System;

// 인터넷 시스템의 정적 데이터 스키마 — Resources/internet.json 하나에 담긴다
// (Internet_System_Plan.md 5장). MapDatabase/ClueDatabase와 같은 JsonUtility 방식이고,
// 로드·조회는 InternetModule이 맡는다.
//
// ★ 단서 정의는 여기 없다 — 게시글은 "어떤 단서를 주는가"(grantClueIds)만 가리키고,
//   단서의 이름·본문·첨부(사진/소리/맵)는 전부 clues.json(ClueData)이 소유한다.
//   같은 단서를 도감·노트·인터넷 어디서 봐도 내용이 어긋나지 않게 하기 위한 설계 결정이다
//   (기획서 3.3장). 게시글 자신의 attachments는 단서와 무관한 "분위기용 장식"에만 쓴다.

// 사이트/게시글 공통 잠금 조건. 필드를 전부 비우면(또는 unlock 자체를 생략하면) 처음부터 열려 있다.
// 세 조건은 AND — 전부 만족해야 보인다.
[Serializable]
public class InternetUnlockCondition
{
    public string[] requiredClueIds;    // 이 단서들을 전부 획득해야 보임 (RouteProgressState.IsClueAcquired)
    public string[] requiredEventKeys;  // "mapGuid:eventKey" 형식 — RouteProgressState.HasEventFlag
    public float minGameTime;           // TimeManager.GameTime(초) 이상이어야 보임. 0이면 조건 없음

    // JsonUtility는 JSON에 없는 필드를 건드리지 않으므로 배열이 null로 남을 수 있다 —
    // 조건 평가(InternetModule.IsUnlocked)는 null을 "조건 없음"으로 취급한다.
    public bool IsEmpty =>
        (requiredClueIds == null || requiredClueIds.Length == 0) &&
        (requiredEventKeys == null || requiredEventKeys.Length == 0) &&
        minGameTime <= 0f;
}

// 게시글 하나. 열람하면 grantClueIds의 단서가 즉시 획득된다(1차 규칙 — 기획서 3.5장).
[Serializable]
public class InternetPost
{
    public string id;
    public string title;
    public string author;
    public string postedAt;   // 표시용 텍스트("00:14") — 실제 게임 시간과 연동되지 않는다
    public string body;

    public string[] grantClueIds = Array.Empty<string>();
    public InternetUnlockCondition unlock;

    // 단서와 무관한 장식용 첨부(스키마만 열어둔 자리 — 기획서 3.3장). 단서가 가진 첨부는
    // 이 배열이 아니라 ClueData.attachments에서 오고, 본문 화면에서 함께 표시된다.
    public ClueAttachment[] attachments = Array.Empty<ClueAttachment>();

    // 도감 코멘트(CodexComment)와 같은 타입을 그대로 재사용한다 — 작성자/본문/시각 구조가 동일해서
    // 표시 코드도 공유할 수 있다(기획서 5장).
    public CodexComment[] comments = Array.Empty<CodexComment>();
}

[Serializable]
public class InternetSite
{
    public string id;
    public string name;
    public string iconPath;   // Resources 상대 경로(선택) — 없으면 아이콘 없이 이름만 표시
    public InternetUnlockCondition unlock;
    public InternetPost[] posts = Array.Empty<InternetPost>();
}

// internet.json의 최상위 구조. ClueDatabase와 같은 역할.
[Serializable]
public class InternetDatabase
{
    public InternetSite[] sites;
}
