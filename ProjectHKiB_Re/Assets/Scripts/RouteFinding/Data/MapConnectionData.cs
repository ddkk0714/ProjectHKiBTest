using System;

// JSON 로드용 연결 데이터. ScriptableObject 대신 map_database.json에서 관리된다.
//
// 2026-07-14 — 전투 데이터(wavePaths/enemyGroups/requiredGears)는 MapNodeData로 이동했다.
// 연결은 이제 "두 맵이 이어져 있다"는 순수 그래프 구조 + 단서 공개 트리거(startsWithClue)만 담당한다.
[Serializable]
public class MapConnectionData
{
    public string guid;
    public string fromGuid;
    public string toGuid;
    public bool startsWithClue;
}
