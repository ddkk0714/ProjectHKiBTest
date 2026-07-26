using System;
using System.Collections.Generic;

[Serializable]
public class ItemSaveInfo
{
    public string itemGuid;
    public int count;
}

[Serializable]
public class GearSaveInfo
{
    public string gearGuid;
    public int slot;
    public List<int> equippedCards;
}

[Serializable]
public class CardSaveInfo
{
    public string cardName;
    public List<string> gearGuids; // 슬롯별
}

[Serializable]
public class EventFlagSaveInfo
{
    public string id;
    public int value;
}

[Serializable]
public class PassageSaveInfo
{
    public string id;
    public bool opened;
}

[Serializable]
public class SaveSlotData
{
    public string savedAt;

    public float hp;

    public List<ItemSaveInfo> items = new();
    public List<GearSaveInfo> ownedGears = new();
    public List<CardSaveInfo> cards = new();

    public List<EventFlagSaveInfo> eventFlags = new();
    public List<PassageSaveInfo> passages = new();

    // 노트(핀 단서)/도감(유저 메모) — IEventSaveProvider(Dictionary<string,bool> 전용)로는
    // 표현 안 되는 구조화 데이터라 별도 필드로 둔다. NoteEntry/CodexUserEntry는
    // RouteFinding/Data/*.cs에 정의된 [Serializable] 클래스(네임스페이스 없음, 이 파일과 동일)라
    // using 없이 바로 참조 가능하다 — RouteFinding 폴더 밖인 이 파일이 RouteFinding 타입을 직접
    // 아는 것은 SaveModule.cs와 마찬가지로 이번 세이브 연동 작업에서 의도적으로 감수한 결합이다.
    public List<NoteEntry> noteEntries = new();
    public List<CodexUserEntry> codexUserEntries = new();

    // 노트 상단 툴바 "저장한 루트" 창에서 이름 붙여 저장해둔 스냅샷들 — 위 noteEntries(현재 화면
    // 상태)와 별개로, 세이브 슬롯 하나 안에 이름 붙은 보드가 여러 개 같이 저장된다.
    public List<NoteSavedBoard> noteSavedBoards = new();

    // 노트 "단서 연동 모드"로 사용자가 마우스로 직접 이어둔 단서 관계(NoteModule._clueLinks) —
    // ValueTuple은 JsonUtility가 직렬화 못 해서 NoteClueLink(평범한 클래스)로 변환해 저장한다.
    public List<NoteClueLink> noteClueLinks = new();

    // [신설, 2026-07-21] 노트 그래프에서 사용자가 옮겨둔 단서 노드의 위치와, 카드가 아니라 노드로
    // "펼쳐져" 있던 경로연동 단서 목록 — "저장한 루트" 보드의 cluePositions/expandedClueIds와 같은
    // 개념을 F5/F9 일반 세이브에도 그대로 적용한 것(단서 연동 간선이 화면에 그려지려면 두 끝이 전부
    // 노드로 떠 있어야 하는데, 이게 없으면 로드 후 경로연동 단서가 카드로 되돌아가 있어 noteClueLinks가
    // 정상 복원돼도 간선이 안 그려져 "연동이 저장 안 된 것처럼" 보였다). CluePositionEntry는
    // Data/NoteSavedBoard.cs에 정의된 것을 그대로 재사용한다. 저장/복원은 NoteRouteGraphView.Instance를
    // 통해 이뤄진다(NoteModule과 달리 씬 오브젝트라 자동 생성 싱글턴이 아님).
    public List<CluePositionEntry> notePositions = new();
    public List<string> noteExpandedClueIds = new();

    // 지도/노트에서 마지막으로 커밋(RouteModule.SelectRoute)한 단일 경로 — 노트 좌측 그래프
    // (NoteRouteGraphView)가 표시하는 데 쓴다. PathResult 자체(MapNodeData 객체 참조를 들고 있어
    // JsonUtility로 그대로 직렬화하기 부적절)를 저장하지 않고, 노드 GUID 순서만 저장한다 —
    // 로드 시 RouteModule.ImportSelectedRoute가 복원.
    public List<string> selectedRouteNodeGuids = new();

    // 장착 장비(RouteModule._equippedGears) — 난이도 계산·통과 가능 판정 기준이라 SelectedRoute와
    // 마찬가지로 세이브 대상에서 빠져 있으면 로드 후 재계산되는 난이도가 저장 시점과 달라지는
    // 불일치가 생긴다.
    public List<EmotionColor> equippedGears = new();

    // 이동 중이 아닐 때도 유지되는 "현재 위치"(RouteModule.CurrentLocation) — 비어있으면 로드 시
    // 자동으로 집(시작 노드)으로 취급된다(RouteModule.CurrentLocation의 기존 기본값과 동일).
    // 이동 중(_isTraveling)이었던 상태 자체는 세이브 대상이 아니다 — 웨이브 전투 재개 자체가
    // 아직 구현되지 않았고(CLAUDE.md MVP 잔여 1번), 죽음/미세이브 진도 손실 설계상 이동 중 저장은
    // 애초에 지원 대상이 아니다.
    public string currentLocationGuid = "";
}
