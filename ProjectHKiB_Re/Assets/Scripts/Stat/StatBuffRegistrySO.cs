using System.Collections.Generic;
using UnityEngine;

// 세이브에 기록된 StatBuffSO.SaveId(에셋 GUID)로 실제 에셋을 되찾기 위한 목록.
//
// 왜 필요한가: 버프를 복원할 때는 EventFlagSO와 달리 "SO 참조를 들고 오는 호출자"가 없다.
// 세이브 파일에는 문자열 ID만 있고, 그걸로 BuffInfo를 새로 만들어야 하므로 ID → 에셋 역참조가
// 반드시 있어야 한다.
//
// 왜 Resources.LoadAll이 아닌가: StatBuffSO 에셋 54개가 Scripts/Buff/EmotionBuffs, ScriptableObjects/Buffs
// 등 여러 폴더에 흩어져 있고 Resources 폴더 밖이다. 에셋들을 옮기는 대신, 목록을 들고 있는
// 이 레지스트리 하나만 Resources에 두는 쪽을 택했다(Assets/Resources/StatBuffRegistry.asset).
//
// 목록 갱신: 에디터에서 이 에셋 우클릭 → "버프 에셋 전체 다시 수집". 버프 SO를 추가/삭제한 뒤에는
// 반드시 한 번 눌러줘야 한다(비어 있으면 OnValidate가 자동으로 한 번 채운다).
[CreateAssetMenu(fileName = "StatBuffRegistry", menuName = "Scriptable Objects/StatBuffRegistry")]
public class StatBuffRegistrySO : ScriptableObject
{
    [SerializeField] private List<StatBuffSO> buffs = new();

    private Dictionary<string, StatBuffSO> _lookup;

    public bool TryGet(string saveId, out StatBuffSO buff)
    {
        buff = null;
        if (string.IsNullOrEmpty(saveId)) return false;

        if (_lookup == null)
        {
            _lookup = new Dictionary<string, StatBuffSO>();
            foreach (var so in buffs)
            {
                if (so == null) continue;
                _lookup[so.SaveId] = so;
            }
        }

        return _lookup.TryGetValue(saveId, out buff);
    }

#if UNITY_EDITOR
    [ContextMenu("버프 에셋 전체 다시 수집")]
    private void CollectAll()
    {
        buffs.Clear();
        _lookup = null;

        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:StatBuffSO"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var so = UnityEditor.AssetDatabase.LoadAssetAtPath<StatBuffSO>(path);
            if (so == null) continue;

            // 인스펙터에서 열어본 적 없어 saveId가 비어 있는 에셋을 여기서 확정한다 —
            // 안 그러면 SaveId가 name으로 폴백해 SelfBuff/OtherBuff 동명 에셋끼리 충돌한다.
            so.EnsureSaveId();
            buffs.Add(so);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets(); // 위에서 SetDirty된 버프 에셋들의 saveId까지 디스크에 반영

        // 그래도 남는 충돌(예: 같은 에셋이 두 번 잡히는 비정상 상태)은 조용히 넘어가면
        // 로드 때 엉뚱한 버프가 복원되므로 여기서 걸러 알린다.
        var seen = new Dictionary<string, StatBuffSO>();
        foreach (var so in buffs)
        {
            if (so == null) continue;
            if (seen.TryGetValue(so.SaveId, out var other))
                Debug.LogError($"[StatBuffRegistrySO] SaveId 충돌: '{other.name}' 와 '{so.name}' 가 같은 ID({so.SaveId})를 씁니다.");
            else
                seen[so.SaveId] = so;
        }

        Debug.Log($"[StatBuffRegistrySO] 버프 에셋 {buffs.Count}개 수집 완료 (고유 ID {seen.Count}개)");
    }

    private void OnValidate()
    {
        // 갓 만들어진 빈 레지스트리를 한 번 채워준다 — 이후 갱신은 위 컨텍스트 메뉴로 수동.
        // (매번 자동 수집하면 에셋을 인스펙터에서 볼 때마다 AssetDatabase를 훑게 된다)
        if (buffs.Count == 0) CollectAll();
    }
#endif
}
