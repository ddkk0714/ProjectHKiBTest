using System;
using UnityEngine;

[Serializable]
public class MapEventFlag
{
    public string key;
    public bool value;
}

public enum EnemyScale { Small, Medium, Large }

// JSON 로드용 적 구성 데이터.
[Serializable]
public class EnemyGroupEntry
{
    public EmotionColor emotionType; // JSON에서 정수(enum index)로 저장됨
    public EnemyScale scale;
    public int count;
}

// JSON 로드용 맵 노드 데이터. ScriptableObject 대신 map_database.json에서 관리된다.
[Serializable]
public class MapNodeData
{
    public string guid;
    public string nodeName;
    public string description;
    public Vector2 graphPosition;   // JSON 포맷: { "x": 0.0, "y": 0.0 }
    public bool isStartNode;        // 집(지도 열람/세이브 기준점) 여부
    public bool startsWithClue;     // 초기 단서 보유 여부
    public string sceneName;        // 씬별 로드 시 사용할 씬 이름
    public MapEventFlag[] events;   // 이벤트 보유 여부 (key-bool 쌍)
    public string[] clueIds;        // 이 맵에서 획득 가능한 단서 ID (clues.json 참조)

    // 2026-07-14 — 전투가 연결(Connection)에서 맵(Node)으로 이동하며 여기로 옮겨온 필드.
    public string[] wavePaths;             // Resources.Load 경로, WaveDataSO 참조
    public EnemyGroupEntry[] enemyGroups;  // 난이도 계산용 적 구성
    public EmotionColor[] requiredGears;   // 이 맵에 진입하기 위한 필수 장비 (비어있으면 제한 없음)

    public bool GetEvent(string key)
    {
        if (events == null) return false;
        foreach (var e in events)
            if (e.key == key) return e.value;
        return false;
    }

    // 장착 장비가 이 맵의 필수 장비 조건을 모두 충족하는지 여부.
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
