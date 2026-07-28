using System;
using UnityEngine;

[CreateAssetMenu(fileName = "StatBuff", menuName = "Scriptable Objects/StatBuff")]
public class StatBuffSO : ScriptableObject
{
    public enum TimeStackTypeEnum { Ignore, Stack, Overwrite }
    // Independant: 동일 SO라도 항상 별도 BuffInfo 생성 (중복 적용 허용)
    public enum BuffStackTypeEnum { Ignore, Stack, Overwrite, Independant }
    // Permanent: UnBuff() 무시. ignorePermanent=true 로만 강제 제거 가능
    public enum BuffRemoveTypeEnum { Remove, Unstack, Permanent }

    [Serializable]
    public class BuffEffect
    {
        [field: SerializeField] public StatBuffTypeSO BuffType { get; private set; }
        [field: SerializeField] public bool IsDebuff { get; set; }
        [field: Min(0)][field: SerializeField] public float Value { get; set; }
        // true: 비율 버프 (BuffedStat += Value × baseStat) / false: 고정 수치 (BuffedStat += Value)
        [field: SerializeField] public bool IsValuePropositional { get; set; }
    }

    public int ID => this.GetInstanceID();

    // 세이브 파일에 기록되는 안정적 식별자 — 위 ID(InstanceID)는 실행할 때마다 달라져 저장에 못 쓴다.
    // 에셋 GUID를 그대로 쓰며 에디터에서 자동으로 채워진다(이름 변경·폴더 이동에도 안 깨짐).
    // 로드할 때 이 ID로 에셋을 되찾는 건 StatBuffRegistrySO가 담당한다 — EventFlagSO와 달리
    // 버프는 "복원할 때 SO 참조를 손에 들고 오는 호출자"가 없어서 역참조가 반드시 필요하다.
    [SerializeField] private string saveId;

    public string SaveId => string.IsNullOrEmpty(saveId) ? name : saveId;

#if UNITY_EDITOR
    private void OnValidate() => EnsureSaveId();

    // StatBuffRegistrySO가 수집할 때도 호출한다 — 인스펙터에서 한 번도 열어본 적 없는 에셋은
    // OnValidate가 안 돌아 saveId가 빈 채로 남고, 그러면 SaveId가 name으로 폴백하는데
    // EmotionBuffs/SelfBuff와 OtherBuff에 같은 이름의 에셋이 쌍으로 있어 ID가 충돌한다.
    public void EnsureSaveId()
    {
        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(assetPath)) return;

        string assetGuid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(assetGuid) || saveId == assetGuid) return;

        saveId = assetGuid;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    [field: NaughtyAttributes.ResizableTextArea]
    [field: SerializeField] public string Description { get; set; }

    [field: SerializeField] public bool IsBuffTimeInfinite { get; set; }
    [field: SerializeField] public float BuffTime { get; set; }

    [Header("Emotion Buff")]
    [SerializeField] private bool isEmotionBuff;
    [SerializeField] private EmotionColor emotionColor;
    [SerializeField] private int maxStack = 200;

    public bool IsEmotionBuff => isEmotionBuff;
    public EmotionColor EmotionColor => emotionColor;
    public int MaxStack => maxStack;


    [field: SerializeField] public TimeStackTypeEnum TimeStackType { get; set; }
    [field: SerializeField] public BuffStackTypeEnum BuffStackType { get; set; }
    [field: SerializeField] public BuffRemoveTypeEnum BuffRemoveType { get; set; }

    [field: SerializeField] public BuffEffect[] Effects { get; private set; }

    public int GetEffectID(int effectIndex)
    {
        return HashCode.Combine(ID, effectIndex);
    }

    public int GetEffectID(int effectIndex, Gear sourceGear)
    {
        return HashCode.Combine(ID, effectIndex, sourceGear);
    }

    public BuffEffect GetEffect(int effectIndex)
    {
        if (Effects == null || effectIndex < 0 || effectIndex >= Effects.Length) return null;
        return Effects[effectIndex];
    }

    public void AddBuff(InterfaceRegister interfaceReg, int multiplyer = 1, bool stack = true)
        => AddBuff(interfaceReg, null, multiplyer, stack);

    public void AddBuff(InterfaceRegister interfaceReg, Gear sourceGear, int multiplyer = 1, bool stack = true)
    {
        if (Effects == null) return;

        for (int i = 0; i < Effects.Length; i++)
        {
            BuffEffect effect = Effects[i];
            if (effect == null || effect.BuffType == null) continue;

            effect.BuffType.AddBuff(interfaceReg, this, i, sourceGear, multiplyer, stack);
        }
    }

    public void RemoveBuff(InterfaceRegister interfaceReg, int multiplyer = 1, bool remove = true)
        => RemoveBuff(interfaceReg, null, multiplyer, remove);

    public void RemoveBuff(InterfaceRegister interfaceReg, Gear sourceGear, int multiplyer = 1, bool remove = true)
    {
        if (Effects == null) return;

        for (int i = 0; i < Effects.Length; i++)
        {
            BuffEffect effect = Effects[i];
            if (effect == null || effect.BuffType == null) continue;

            effect.BuffType.RemoveBuff(interfaceReg, this, i, sourceGear, multiplyer, remove);
        }
    }
}