using UnityEngine;

[CreateAssetMenu(fileName = "Event", menuName = "Event/EventFlag")]
public class EventFlagSO : ScriptableObject
{
    // 세이브 파일에 기록되는 안정적 식별자 — 에셋 GUID를 그대로 쓰며 에디터에서 자동으로 채워진다.
    // 에셋 이름을 바꾸거나 폴더를 옮겨도 GUID는 유지되므로 기존 세이브가 깨지지 않는다.
    //
    // 이 ID로 에셋을 되찾는(ID → EventFlagSO) 경로는 어디에도 없다 — 그게 있으려면 에셋이
    // Resources 폴더 아래 있거나 별도 레지스트리 에셋이 필요한데, 플래그를 읽는 쪽
    // (EventControllableEntity/Animation, SetEventFlagAction)은 전부 이미 EventFlagSO 참조를
    // 들고 있어서 SO → ID 한 방향이면 충분하기 때문이다. 자세한 건 EventManager 주석 참고.
    [SerializeField] private string id;

    // id가 비어 있으면(아직 OnValidate가 한 번도 안 돈 에셋) 에셋 이름으로 대체한다 — 비어 있는
    // 문자열이 세이브에 들어가 서로 다른 플래그가 한 칸을 공유하는 것보다는 낫다.
    public string Id => string.IsNullOrEmpty(id) ? name : id;

#if UNITY_EDITOR
    private void OnValidate()
    {
        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(assetPath)) return; // 아직 에셋으로 저장되지 않은 인스턴스

        string assetGuid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(assetGuid) || id == assetGuid) return;

        id = assetGuid;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
