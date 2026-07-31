using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;
using UnityEngine.U2D.Animation;
[CreateAssetMenu(fileName = "Gear Data", menuName = "Scriptable Objects/Data/Gear Data", order = 2)]
public class GearDataSO : ItemDataSO
{
    public enum GearType { Damage, Transform, Util }
    [Header("Base Data")]
    //public GearTypeSO gearType;
    //public PlayerBaseDataSO playerBaseData;
    //public GameObject tutorialPrefab;
    public GearType gearType;
    public StateMachineSO stateMachine;
    public string startStateName;
    public float transformTime;

    [Header("Base Stat Buff")]
    public StatBuffSO baseBuff;
    public StatBuffSO stackableBuff;

    [Header("Base Animation")]
    public SimpleAnimationDataSO mainAnimationData;
    public SpriteLibraryAsset mainSpriteLibrary;
    public SimpleAnimationDataSO effectAnimationData;
    public SpriteLibraryAsset effectSpriteLibrary;

    [Header("Skin Data")]
    public SkinDataSO skinData;
    public SpriteLibraryAsset standingCGData;

    [Header("Merge Setting")]
    public GearDataSO[] mergeSet;
    [Tooltip("smaller the value is, priority is higher")]
    public int mergePriority;
    public SerializedDictionary<GearDataSO, SkinDataSO> SkinMixList;

    [Header("Gear Effect")]
    public string mainGearEffectDiscription;
    public GearEffectSO[] mainGearEffects;

    [Header("Graffiti")]
    public List<GraffitiCode> graffitiCodes;
    public List<GraffitiCode> graffitiAllCases;

    public void CalculateAllCases()
    {
        graffitiAllCases.Clear();
        foreach (GraffitiCode graffitiCode in graffitiCodes)
        {
            foreach (Vector2Int center in graffitiCode.code)
            {
                GraffitiCode skillCase = new() { code = new(graffitiCode.code.Count) };
                foreach (Vector2Int point in graffitiCode.code)
                {
                    skillCase.code.Add(point - center);
                }
                graffitiAllCases.Add(skillCase);
            }
        }
    }

    public void ApplyMainGearEffect(MergedPlayerBaseData realGear)
    {
        for (int i = 0; i < mainGearEffects.Length; i++)
            mainGearEffects[i].ApplyEffect(realGear);
    }

    public void Activate(StateController player, GearDataSO recentGear)
    {
        if (gearType != GearType.Damage)
        {
            if (stateMachine) player.Initialize(stateMachine);
            if (player.TryGetInterface(out IAnimatable animatable))
            {
                animatable.MainAnimationData = mainAnimationData;
                animatable.MainSpriteLibrary = mainSpriteLibrary;
            }
            if (player.TryGetInterface(out IAttackable attackable))
            {
                attackable.EffectAnimationData = effectAnimationData;
                attackable.EffectSpriteLibrary = effectSpriteLibrary;
            }

            SkinDataSO skin = SkinMixList.ContainsKey(recentGear) ? SkinMixList[recentGear] : skinData;
            if (player.TryGetInterface(out ISkinable skinable)) skinable.SetSkinData(skin);
        }

        player.ChangeState(startStateName);

        if (player.TryGetInterface(out IBuffable buffable))
        {
            if (baseBuff) buffable.Buff(baseBuff);
            if (stackableBuff) buffable.Buff(stackableBuff);
        }
    }

    public void Deactivate(StateController player)
    {
        if (player.TryGetInterface(out IBuffable buffable))
        {
            if (baseBuff) buffable.UnBuff(baseBuff);
        }
    }
}
