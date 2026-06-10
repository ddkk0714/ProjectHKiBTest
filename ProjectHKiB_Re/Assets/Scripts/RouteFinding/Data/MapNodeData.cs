using System;
using UnityEngine;

[Serializable]
public class MapEventFlag
{
    public string key;
    public bool value;
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

    public bool GetEvent(string key)
    {
        if (events == null) return false;
        foreach (var e in events)
            if (e.key == key) return e.value;
        return false;
    }
}
