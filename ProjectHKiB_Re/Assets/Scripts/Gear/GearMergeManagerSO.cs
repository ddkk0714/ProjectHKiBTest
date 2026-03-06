using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "Gear Merge Manager", menuName = "Scriptable Objects/Manager/Gear Merge Manager", order = 2)]
public class GearMergeManagerSO : ScriptableObject
{
    private const int MAINGEAR = 0;
    public GearDataSO defaultGearData;
    [SerializeField] private DatabaseManagerSO databaseManager;

    // Every gears that can be merged from other gears
    [SerializeField] private GearDataSO[] allMergedGearDatas;
    public delegate void OnRealGearMadeEventHandler(MergedPlayerBaseData realGear);
    public event OnRealGearMadeEventHandler OnRealGearMade;


    private void Awake()
    {
        allMergedGearDatas = allMergedGearDatas.OrderBy(a => a.mergePriority).ToArray();
        if (defaultGearData == null)
            Debug.LogError("ERROR : default gear data not attached!!!");
    }

    public void MergeCard(CardData card, int max)
    {
        int i, j, k;
        int physMax = card.GearList.Length;
        // If gear in slot is null or already merged, its value is false
        bool[] isGearAvailableSlot = new bool[physMax];

        // Stores nullcheck in isGearAvailableSlot
        // if null, put defaultGear in
        for (i = 0; i < physMax; i++)
        {
            isGearAvailableSlot[i] = card.GearList[i].data != null;
            card.mergedGearList[i] = card.GearList[i].data == null ? defaultGearData : card.GearList[i].data;
        }

        // For all merged gears check if their mergeset gears are in equipped gears 
        // If they exists, check them as already merged
        List<int> inMergeSetGearSlots = new(max);
        for (i = 0; i < allMergedGearDatas.Length; i++)
        {
            inMergeSetGearSlots.Clear();
            for (j = 0; j < allMergedGearDatas[i].mergeSet.Length; j++)
            {
                for (k = 0; k < max; k++)
                {
                    if (isGearAvailableSlot[k] && allMergedGearDatas[i].mergeSet[j] == card.GearList[k].data)
                        inMergeSetGearSlots.Add(k);
                }
            }

            if (inMergeSetGearSlots.Count == allMergedGearDatas[i].mergeSet.Length)
            {
                for (j = 0; j < inMergeSetGearSlots.Count; j++)
                {
                    isGearAvailableSlot[inMergeSetGearSlots[j]] = false;
                    card.mergedGearList[inMergeSetGearSlots[j]] = allMergedGearDatas[i];
                }
            }
        }
    }

    public void EquipMergedCard(CardData card, int max)
    {
        // final merged gear data
        MergedPlayerBaseData mergedGearData = new();

        // Set skin skin and animation or base stats of main gear as default
        // and apply main effect, set geartype
        SetDatas(mergedGearData, card.mergedGearList[MAINGEAR].playerBaseData);
        card.mergedGearList[MAINGEAR].ApplyMainGearEffect(mergedGearData);
        mergedGearData.gearType = card.mergedGearList[MAINGEAR].gearType;

        // If there is attack gear in subgears, override attack animation and geartype
        // also if maingear cannot attack it doesn't happen
        // and apply sub effects
        for (int i = max - 1; i > MAINGEAR; i--)
        {
            if (card.mergedGearList[i].gearType.isAttackGear
            && !card.mergedGearList[MAINGEAR].gearType.isAttackGear
            && !card.mergedGearList[MAINGEAR].gearType.cannotAttack)
            {
                mergedGearData.StateMachine = card.mergedGearList[i].playerBaseData.StateMachine;
                mergedGearData.gearType = card.mergedGearList[i].gearType;
                mergedGearData.AttackDatas = card.mergedGearList[i].playerBaseData.AttackDatas;
                mergedGearData.AnimationData = card.mergedGearList[i].playerBaseData.AnimationData;
            }
            card.mergedGearList[i].ApplySubGearEffect(mergedGearData);
        }
        // This triggers player to reset data
        // also triggers other effects or something
        OnRealGearMade?.Invoke(mergedGearData);
    }

    public void SetDatas(MergedPlayerBaseData mergedGearData, PlayerBaseDataSO playerBaseData)
    {
        databaseManager.SetIAnimatable(mergedGearData, playerBaseData);
        databaseManager.SetIMovable(mergedGearData, playerBaseData);
        databaseManager.SetIAttackable(mergedGearData, playerBaseData);
        databaseManager.SetIDodgeable(mergedGearData, playerBaseData);
        databaseManager.SetIDamagable(mergedGearData, playerBaseData);
        databaseManager.SetIDodgeable(mergedGearData, playerBaseData);
        mergedGearData.StateMachine = playerBaseData.StateMachine;
        databaseManager.SetISkinable(mergedGearData, playerBaseData);
        databaseManager.SetITargetable(mergedGearData, playerBaseData);
        databaseManager.SetIFootstep(mergedGearData, playerBaseData);
    }
}