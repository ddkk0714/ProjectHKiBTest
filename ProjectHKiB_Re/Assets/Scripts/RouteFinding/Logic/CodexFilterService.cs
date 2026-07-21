using System;
using System.Collections.Generic;
using System.Linq;
using RouteFinding.Codex;

// 도감 항목(CodexEntry) 분류/검색 — 무상태 정적 클래스. MapPathFinder/DifficultyCalculator와 같은 패턴.
// 맵별/출처별/키워드별 세 기준으로 그룹핑하고, 자유 텍스트 검색을 지원한다.
public static class CodexFilterService
{
    public static List<CodexGroup> GroupByMap(IReadOnlyList<CodexEntry> entries) =>
        GroupBy(entries, e => string.IsNullOrEmpty(e.mapCategory) ? "기타" : e.mapCategory);

    public static List<CodexGroup> GroupBySource(IReadOnlyList<CodexEntry> entries) =>
        GroupBy(entries, e => string.IsNullOrWhiteSpace(e.source) ? "기타" : e.source.Trim());

    // 키워드 기준 — 한 항목이 키워드를 여러 개 가지면 모든 해당 그룹에 중복 노출된다(상호 배타적이지 않음).
    // ClueType 표시 이름(entry.typeLabel)도 자동으로 키워드 그룹에 합쳐 넣는다.
    public static List<CodexGroup> GroupByKeyword(IReadOnlyList<CodexEntry> entries)
    {
        var groups = new Dictionary<string, List<CodexEntry>>();
        foreach (var e in entries)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (e.keywords != null)
                foreach (var kw in e.keywords)
                    if (!string.IsNullOrWhiteSpace(kw)) keys.Add(kw.Trim());
            if (!string.IsNullOrEmpty(e.typeLabel)) keys.Add(e.typeLabel);

            foreach (var key in keys)
            {
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<CodexEntry>();
                list.Add(e);
            }
        }
        return ToSortedGroups(groups);
    }

    // 이름/본문/출처/키워드를 대상으로 부분 문자열 매칭(대소문자 무시). 오타 허용 등 고급 검색은 범위 밖.
    public static List<CodexEntry> Search(IReadOnlyList<CodexEntry> entries, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return entries.ToList();
        var q = query.Trim();

        var result = new List<CodexEntry>();
        foreach (var e in entries)
        {
            bool hit = Contains(e.title, q) || Contains(e.content, q) || Contains(e.source, q) ||
                       (e.keywords != null && e.keywords.Any(kw => Contains(kw, q)));
            if (hit) result.Add(e);
        }
        return result;
    }

    // 6-5단계 — 그룹 내부(entries) 정렬. ToSortedGroups는 그룹 자체(카테고리)만 가나다순으로 정렬하고
    // 그룹 안 항목 순서는 원래 삽입 순서 그대로였다 — 그 삽입 순서 자체가 세이브 로드 후에는 사실상
    // 무작위(HashSet 기반)라 "가나다순" 조차 보장되지 않던 상태였다. acquisitionRank는 clueId → 획득
    // 순번(작을수록 먼저 획득, RouteProgressState.AcquisitionOrder 기준) — 없는 항목(유저 메모·미발견
    // 슬롯)은 정렬 우선순위상 맨 뒤로 보낸다.
    public static void SortEntries(List<CodexGroup> groups, CodexSortOrder order, IReadOnlyDictionary<string, int> acquisitionRank)
    {
        foreach (var group in groups)
        {
            switch (order)
            {
                case CodexSortOrder.ByType:
                    group.entries.Sort((a, b) =>
                    {
                        int c = string.Compare(a.typeLabel, b.typeLabel, StringComparison.CurrentCultureIgnoreCase);
                        return c != 0 ? c : string.Compare(a.title, b.title, StringComparison.CurrentCultureIgnoreCase);
                    });
                    break;
                case CodexSortOrder.RecentlyAcquired:
                    group.entries.Sort((a, b) =>
                    {
                        int ra = RankOf(a.clueId, acquisitionRank);
                        int rb = RankOf(b.clueId, acquisitionRank);
                        return rb.CompareTo(ra); // 순번이 큰(최근) 것이 먼저
                    });
                    break;
                default: // Alphabetical
                    group.entries.Sort((a, b) => string.Compare(a.title, b.title, StringComparison.CurrentCultureIgnoreCase));
                    break;
            }
        }
    }

    private static int RankOf(string clueId, IReadOnlyDictionary<string, int> acquisitionRank)
    {
        if (string.IsNullOrEmpty(clueId) || acquisitionRank == null) return -1;
        return acquisitionRank.TryGetValue(clueId, out var rank) ? rank : -1;
    }

    private static bool Contains(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static List<CodexGroup> GroupBy(IReadOnlyList<CodexEntry> entries, Func<CodexEntry, string> keySelector)
    {
        var groups = new Dictionary<string, List<CodexEntry>>();
        foreach (var e in entries)
        {
            var key = keySelector(e);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<CodexEntry>();
            list.Add(e);
        }
        return ToSortedGroups(groups);
    }

    // "기타" 그룹은 항상 마지막, 나머지는 가나다순 — 분류 기준을 바꿔도 트리 순서가 흔들리지 않게.
    private static List<CodexGroup> ToSortedGroups(Dictionary<string, List<CodexEntry>> groups)
    {
        var list = groups.Select(kv => new CodexGroup { category = kv.Key, entries = kv.Value }).ToList();
        list.Sort((a, b) =>
        {
            bool aEtc = a.category == "기타", bEtc = b.category == "기타";
            if (aEtc != bEtc) return aEtc ? 1 : -1;
            return string.Compare(a.category, b.category, StringComparison.CurrentCultureIgnoreCase);
        });
        return list;
    }
}
