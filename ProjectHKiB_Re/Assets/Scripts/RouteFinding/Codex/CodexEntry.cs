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
    }

    // CodexFilterService의 그룹핑 결과 한 덩어리 — 카테고리 이름 + 그 안에 속한 항목들.
    // 키워드 분류에서는 같은 CodexEntry가 여러 CodexGroup에 동시에 나타날 수 있다(상호 배타적이지 않음).
    public class CodexGroup
    {
        public string category;
        public List<CodexEntry> entries;
    }
}
