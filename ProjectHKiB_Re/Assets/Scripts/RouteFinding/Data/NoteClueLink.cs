using System;

// 노트 "단서 연동 모드"로 사용자가 마우스로 직접 이어둔 두 단서 사이의 관계 — NoteModule._clueLinks의
// 세이브용 직렬화 형태. 런타임에는 정규화된(사전순) ValueTuple 쌍(HashSet<(string,string)>)으로 들고
// 있지만, ValueTuple은 JsonUtility가 그대로 직렬화하지 못해 이 평범한 클래스로 변환해 저장한다.
[Serializable]
public class NoteClueLink
{
    public string clueIdA;
    public string clueIdB;
}
