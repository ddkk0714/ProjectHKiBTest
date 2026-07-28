using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public class GearManager : MonoBehaviour
{
    public List<Gear> activeGear;
    [HideInInspector] public Timer transformTimer = new();
    //[SerializeField] private GearMergeManagerSO gearMergeManager;
    public GearDataSO DefaultGearData;
    [SerializeField] private GearDataSO[] allMergedGearDatas;

    /*[HideInInspector]*/
    public List<Card> playerCardEquipData;
    [field: SerializeField] public int PhysicalMaxGearSlotCount { get; private set; }

    [SerializeField] private int _maxGearSlotCount;
    public int MaxGearSlotCount
    {
        get => _maxGearSlotCount;
        set
        {
            _maxGearSlotCount = value;
            OnMaxSlotChanged?.Invoke();
        }
    }

    [field: SerializeField] public int PhysicalMaxCardCount { get; private set; }

    [SerializeField] private int _maxCardCount;
    public int MaxCardCount
    {
        get => _maxCardCount;
        set
        {
            _maxCardCount = value;
            OnMaxCardChanged?.Invoke();
        }
    }

    public bool canChangeCard = true;

    public Action OnMaxCardChanged;
    public Action OnMaxSlotChanged;
    public Action OnSetCardData;

    public int currentActiveCardNum;
    public int currentEdittingCardNum;
    public int currentEdittingSlotNum;

    public void Start()
    {
        //gearMergeManager.OnRealGearMade += FindObjectOfType<Player>(true).SetGear;
        playerCardEquipData = new(PhysicalMaxCardCount);

        for (int i = 0; i < PhysicalMaxCardCount; i++)
        {
            Card data = new();
            playerCardEquipData.Add(data);
            data.Initialize();
        }

        //SetMaxCard(1); /////////////////////////////////////// temp!!!!!!!
        //SetMaxSlot(4); /////////////////////////////////////// temp!!!!!!!

        //OnMaxCardChanged += () => EquipCard(currentEquippedCardIndex);
        //OnMaxSlotChanged += () => EquipCard(currentEquippedCardIndex);
        //OnSetCardData += () => EquipCard(currentEquippedCardIndex);
    }

    [Button]
    public void AddMaxCard()
    {
        SetMaxCard(MaxCardCount + 1);
    }

    public void SetMaxCard(int max)
    {
        MaxCardCount = max;
        if (MaxCardCount > PhysicalMaxCardCount) MaxCardCount = PhysicalMaxCardCount;
        if (MaxCardCount <= 0) MaxCardCount = 1;
    }

    [Button] public void AddMaxSlot() => SetMaxSlot(MaxGearSlotCount + 1);
    [Button] public void SubMaxSlot() => SetMaxSlot(MaxGearSlotCount - 1);

    public void SetMaxSlot(int max)
    {
        if (max < 0 || max > PhysicalMaxGearSlotCount) return;

        for (int i = 0; i < playerCardEquipData.Count; i++)
        {
            for (int j = 0; j < playerCardEquipData[i].GearList.Length; j++)
            {
                if (j >= max)
                {
                    playerCardEquipData[i].ResetGear(i, j);
                }
            }
        }

        MaxGearSlotCount = max;
    }

    public Card GetCardData(int index)
    {
        if (index >= playerCardEquipData.Count || index < 0)
            return null;

        return playerCardEquipData[index];
    }

    public void SetCardData(int cardIndex, Card data)
    {
        if (cardIndex >= playerCardEquipData.Count || cardIndex < 0)
            return;

        playerCardEquipData[cardIndex] = data;
        OnSetCardData?.Invoke();
    }

    public void ResetGearData(int cardIndex, int gearSlotIndex)
    {
        if (cardIndex >= MaxCardCount || cardIndex < 0)
            return;
        if (gearSlotIndex >= MaxGearSlotCount || gearSlotIndex < 0)
            return;

        playerCardEquipData[cardIndex].ResetGear(cardIndex, gearSlotIndex);
        OnSetCardData?.Invoke();
    }

    public void SetGearData(Gear gear) => SetGearData(currentEdittingCardNum, currentEdittingSlotNum, gear);

    public void SetGearData(int cardIndex, int gearSlotIndex, Gear gear)
    {
        //Debug.Log($"setGear! : {gear.data.name} to card {cardIndex}, slot {gearSlotIndex}");

        if (cardIndex >= MaxCardCount || cardIndex < 0)
            return;
        if (gearSlotIndex >= MaxGearSlotCount || gearSlotIndex < 0)
            return;
        if (gear == null)
            return;

        if (gearSlotIndex != gear.IsEquippedInCard(cardIndex) && gear.IsEquippedInCard(cardIndex) >= 0)
        {
            ResetGearData(cardIndex, gear.IsEquippedInCard(cardIndex));
        }

        gear.EquipTo(cardIndex, gearSlotIndex);
        playerCardEquipData[cardIndex].SetGear(cardIndex, gearSlotIndex, gear);
        OnSetCardData?.Invoke();
    }

    public GearDataSO GetGearData(int slotIndex)
    {
        if (slotIndex > playerCardEquipData[currentActiveCardNum].GearList.Length) return null;

        return playerCardEquipData[currentActiveCardNum].GearList[slotIndex].data;
    }

    public void ActivateGear(int slotIndex)
    {
        Card card = playerCardEquipData[currentActiveCardNum];
        Gear mergedGear = GetMergedGear(card.GearList[slotIndex]);

        transformTimer.ExtendTimer(mergedGear.data.transformTime, DeactivateAllGears);
        GearDataSO recentGear = activeGear.Count > 0 ? activeGear[^1].data : mergedGear.data;

        if (mergedGear.data.gearType != GearDataSO.GearType.Damage) // damageType doesn't go in activeGear list
        {
            if (activeGear.Exists(a => a.data == mergedGear.data)) DeactivateGear(mergedGear);
            activeGear.Add(mergedGear);
        }
        mergedGear.Activate(GameManager.instance.player, recentGear);
    }

    public void DeactivateGear(Gear gear)
    {
        if (activeGear.Exists(a => a == gear))
        {
            gear.Deactivate(GameManager.instance.player);
            activeGear.Remove(gear);
        }
    }

    public void DeactivateGear(int activeIndex)
    {
        Gear g = activeGear[activeIndex];
        g.Deactivate(GameManager.instance.player);
        activeGear.Remove(g);
    }

    public void DeactivateAllGears()
    {
        for (int i = 0; i < activeGear.Count; i++)
        {
            activeGear[i].Deactivate(GameManager.instance.player);
        }
        activeGear.Clear();
        StateController player = GameManager.instance.player;
        if (player.TryGetInterface(out ISkinable skinable)) skinable.SetSkinData(DefaultGearData.skinData);
    }

    // this also deactivates another merge component gear
    public Gear GetMergedGear(Gear newGear)
    {
        GearDataSO[] mergeOptions;
        mergeOptions = Array.FindAll(allMergedGearDatas, a => Array.Exists(a.mergeSet, b => b == newGear.data));
        Array.Sort(mergeOptions, (a, b) => a.mergePriority.CompareTo(b.mergePriority));

        for (int i = activeGear.Count - 1; i > -1; i--)
        {
            for (int j = 0; j < mergeOptions.Length; j++)
            {
                GearDataSO mergedGear = Array.Find(mergeOptions[j].mergeSet, a => a == activeGear[i].data);
                if (mergedGear)
                {
                    Gear mergedNewGear = new(mergedGear);
                    DeactivateGear(i);
                    return mergedNewGear;
                }
            }
        }
        return newGear;
    }
}