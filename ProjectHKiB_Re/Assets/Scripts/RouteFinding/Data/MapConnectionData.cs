using System;

public enum EnemyScale { Small, Medium, Large }

// JSON 로드용 적 구성 데이터.
[Serializable]
public class EnemyGroupEntry
{
    public EmotionColor emotionType; // JSON에서 정수(enum index)로 저장됨
    public EnemyScale scale;
    public int count;
}

// JSON 로드용 연결 데이터. ScriptableObject 대신 map_database.json에서 관리된다.
[Serializable]
public class MapConnectionData
{
    public string guid;
    public string fromGuid;
    public string toGuid;
    public bool startsWithClue;
    public string[] wavePaths;             // Resources.Load 경로, WaveDataSO 참조
    public EnemyGroupEntry[] enemyGroups;  // 난이도 계산용 적 구성
    public EmotionColor[] requiredGears;   // 통과에 필요한 필수 장비 (비어있으면 제한 없음)

    // 장착 장비가 이 연결의 필수 장비 조건을 모두 충족하는지 여부.
    // 감정 그룹 기준으로 비교 (SadnessBlue·SadnessSky 등 색상 변형은 동일 취급).
    public bool IsPassableWith(EmotionColor[] equippedGears)
    {
        if (requiredGears == null || requiredGears.Length == 0) return true;
        if (equippedGears == null || equippedGears.Length == 0) return false;

        foreach (var req in requiredGears)
        {
            var reqGroup = EmotionColorConfig.ToGroup(req);
            bool satisfied = false;
            foreach (var g in equippedGears)
            {
                if (EmotionColorConfig.ToGroup(g) == reqGroup) { satisfied = true; break; }
            }
            if (!satisfied) return false;
        }
        return true;
    }
}
