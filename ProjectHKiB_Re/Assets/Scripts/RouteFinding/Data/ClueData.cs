using System;

// 단서 하나의 데이터. clues.json에서 별도 관리된다.
// 출발 맵(이 단서가 MapNodeData.clueIds에 등록된 맵)을 방문한 뒤,
// requiredEventKey가 비어있으면 즉시, 아니면 해당 이벤트가 발생해야 획득된다.
[Serializable]
public class ClueData
{
    public string id;
    public string name;
    public string description;
    public string targetMapGuid;         // 이 단서가 공개하는 맵 GUID (없으면 빈 문자열)
    public string targetConnectionGuid;  // 이 단서가 공개하는 연결 GUID (없으면 빈 문자열)
    public string requiredEventKey;      // 획득에 필요한 이벤트 키 (비어있으면 방문만으로 획득)
}

[Serializable]
public class ClueDatabase
{
    public ClueData[] clues;
}
