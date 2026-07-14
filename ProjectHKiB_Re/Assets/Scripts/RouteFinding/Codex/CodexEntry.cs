using System;
using System.Collections.Generic;

namespace RouteFinding.Codex
{
    // 도감 카드 한 장에 표시할 데이터. 1단계부터 ClueData(정식 단서)를 이 형태로 변환해
    // 트리·카드에 공급한다(CodexPanel.ToEntry). 3단계에서 CodexUserEntry(유저 메모)도
    // 같은 타입으로 변환한다(CodexPanel.ToEntryFromUser).
    public class CodexEntry
    {
        public string title;
        public string typeLabel;   // 타입 배지 표시용 (생명체/장소/퍼즐 힌트/이벤트 힌트/이동 힌트). 유저 메모는 빈 문자열
        public string timestamp;   // 빈 문자열이면 카드에 표시 안 함
        public string content;
        public string source;
        public string mapCategory; // 맵별 분류 기준. 없으면 "기타"
        public string[] keywords;

        // 이 항목이 CodexUserEntry(유저 메모)에서 왔으면 그 guid, ClueData에서 왔으면 빈 문자열.
        // CodexCardView가 편집/삭제 버튼을 보여줄지 판단하는 기준으로 쓴다.
        public string userEntryGuid = "";

        // ClueData에서 왔으면 그 id, 유저 메모에서 왔으면 빈 문자열 (2단계, 노트 "핀" 액션용 —
        // 유저 메모는 clueId가 없어 NoteEntry로 표현할 수 없으므로 핀 대상에서 자연히 제외된다).
        public string clueId = "";

        // 4단계 — NPC/시스템 코멘트(ClueData.comments/CodexUserEntry.comments를 그대로 옮겨온 것).
        public CodexComment[] comments = Array.Empty<CodexComment>();

        // 6-2단계(Clue_System.md) — 아직 획득하지 않은 단서의 "???" 빈칸 슬롯. true면 title/content가
        // 이미 "??? (미발견)"/고정 문구로 채워져 있고, clueId/typeLabel/timestamp/source/keywords는
        // 전부 비어있다 — CodexCardView/CodexDrawerTreeView가 별도 분기 없이 그대로 표시해도 스포일러가
        // 새지 않는다(핀·편집 버튼도 clueId/userEntryGuid가 비어 자연히 숨겨짐). 맵별 그룹핑에서만
        // 채워진다(슬롯 개수 자체가 MapNodeData.clueIds 기준이라 맵과 무관한 출처/키워드 분류에는 안 맞음).
        public bool isPlaceholder = false;
    }

    // CodexFilterService의 그룹핑 결과 한 덩어리 — 카테고리 이름 + 그 안에 속한 항목들.
    // 키워드 분류에서는 같은 CodexEntry가 여러 CodexGroup에 동시에 나타날 수 있다(상호 배타적이지 않음).
    public class CodexGroup
    {
        public string category;
        public List<CodexEntry> entries;
    }
}
