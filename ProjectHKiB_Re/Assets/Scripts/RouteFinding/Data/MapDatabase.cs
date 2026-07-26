using System;

// map_database.json 최상위 래퍼. JsonUtility.FromJson<MapDatabase>(json)으로 역직렬화된다.
[Serializable]
public class MapDatabase
{
    public MapNodeData[] maps;
    public MapConnectionData[] connections;
}
