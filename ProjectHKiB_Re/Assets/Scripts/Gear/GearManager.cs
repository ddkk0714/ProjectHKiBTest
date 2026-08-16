using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public class GearManager : MonoBehaviour
{
    [HideInInspector] public List<Gear> activeGear;
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

    public GearDataSO setGearData;
    [Button] public void ActivateGear() => ActivateGear(setGearData);

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

    public void ActivateGear(int slotIndex) => ActivateGear(playerCardEquipData[currentActiveCardNum].GearList[slotIndex].data);


    public void ActivateGear(GearDataSO gearData)
    {
        GearDataSO mergedGear = GetMergedGear(gearData);

        transformTimer.ExtendTimer(mergedGear.transformTime, DeactivateAllGears);
        GearDataSO recentGear = activeGear.Count > 0 ? activeGear[^1].data : mergedGear;

        if (mergedGear.gearType != GearDataSO.GearType.Damage) // damageType doesn't go in activeGear list
        {
            if (activeGear.Exists(a => a.data == mergedGear)) DeactivateGear(mergedGear);
            activeGear.Add(new(mergedGear));
        }
        mergedGear.Activate(GameManager.instance.player, recentGear);
    }

    public void DeactivateGear(GearDataSO gearData)
    {
        Gear gear = activeGear.Find(a => a.data == gearData);
        if (gear != null)
        {
            gearData.Deactivate(GameManager.instance.player);
            activeGear.Remove(gear);
        }
    }

    public void DeactivateGear(int activeIndex)
    {
        Gear g = activeGear[activeIndex];
        g.Deactivate(GameManager.instance.player);
        activeGear.Remove(g);
    }

    [Button]
    public void DeactivateAllGears()
    {
        for (int i = 0; i < activeGear.Count; i++)
        {
            activeGear[i].Deactivate(GameManager.instance.player);
        }
        activeGear.Clear();

        RestoreDefaultGear();
    }

    /// <summary>
    /// 변신이 끝나면 기본 상태(DefaultGearData)로 되돌린다.
    ///
    /// 예전엔 여기서 스킨만 기본값으로 바꿨다. 그런데 변신을 켤 때 GearDataSO.Activate가 갈아끼우는
    /// 것은 스킨만이 아니다 — 상태 기계, 메인 애니메이션 데이터, 스프라이트 라이브러리, 공격 이펙트까지
    /// 전부 그 기어 것으로 바꾼다. 되돌리는 쪽이 스킨 하나만 되돌리고 있었으니, 변신 시간이 끝나도
    ///   - 무기를 든 스프라이트/애니메이션이 그대로 남고,
    ///   - 기어의 상태 기계가 그대로 남아 그 무기의 공격을 계속 쓸 수 있었다.
    /// 켜는 경로와 끄는 경로가 손대는 항목이 서로 달랐던 것이 원인이므로, 끄는 쪽도 같은 경로
    /// (Activate)를 그대로 태워 기본 기어를 다시 입힌다 — 앞으로 Activate가 바꾸는 항목이 늘어나도
    /// 여기만 따로 빠뜨려 어긋나는 일이 없다.
    ///
    /// AttackDatas와 회피 데이터는 DefaultGearData에 playerBaseData가 없어 기어 것이 그대로 남는데,
    /// 이건 의도된 결과다 — 기본 캐릭터의 PlayerData는 회피 파티클이 비어 있어(DodgeableModule.
    /// StartKeepDodge가 NullReference를 낸다) 되돌리는 쪽이 오히려 위험하고, 기본 상태 기계에는
    /// 공격 상태 자체가 없어 남아 있는 AttackDatas를 꺼낼 길도 없다.
    /// </summary>
    private void RestoreDefaultGear()
    {
        if (DefaultGearData == null)
        {
            Debug.LogWarning("[GearManager] DefaultGearData가 비어 있어 변신 해제 후 기본 상태로 되돌리지 못했습니다.");
            return;
        }

        Player player = GameManager.instance.player;

        // recentGear로 자기 자신을 넘긴다 — SkinMixList가 비어 있어 어차피 skinData로 떨어지지만,
        // null을 넘기면 Activate 안의 SkinMixList.ContainsKey(null)에서 예외가 난다.
        DefaultGearData.Activate(player, DefaultGearData);

        EnsureBaseStateMachine(player);
    }

    /// <summary>
    /// 돌아갈 기본 상태 기계의 진짜 기준은 그 캐릭터의 PlayerBaseData다 — 게임을 시작할 때
    /// Player.Initialize가 쓰는 것이 그것이고, DefaultGearData.stateMachine은 같은 것을 가리키라고
    /// 둔 사본일 뿐이다. 두 값이 어긋나면 여기서 바로잡고 알린다.
    ///
    /// 실제로 어긋나 있었다(2026-08-16): DefaultGearData는 상태 기계를 새 형식으로 변환하기 전의
    /// Delta_Base_StateMachine을 계속 가리키고 있었다. 구형 WalkState의 EnterActions에는
    /// ApplyWalkAction만 있고 변환 때 추가된 PlayAnimationAction이 없어서, 변신이 풀린 뒤에는
    /// "걷기는 하는데 걷는 애니메이션이 안 나오는" 상태가 됐다. 에셋 쪽은 고쳤지만, 캐릭터가
    /// 바뀌거나 기계를 다시 변환할 때 같은 방식으로 또 낡을 수 있는 자리라 코드에서도 막는다.
    /// </summary>
    private void EnsureBaseStateMachine(Player player)
    {
        StateMachineSO baseStateMachine = player.BaseData != null ? player.BaseData.StateMachine : null;
        if (baseStateMachine == null || player.StateMachine == baseStateMachine) return;

        Debug.LogWarning($"[GearManager] DefaultGearData의 상태 기계('{DefaultGearData.stateMachine?.name}')가 " +
                         $"플레이어 기본 기계('{baseStateMachine.name}')와 달라 후자로 되돌립니다. " +
                         "DefaultGearData.stateMachine을 맞춰 주세요.");
        player.Initialize(baseStateMachine);
    }

    // this also deactivates another merge component gear
    public GearDataSO GetMergedGear(GearDataSO newGear)
    {
        GearDataSO[] mergeOptions;
        mergeOptions = Array.FindAll(allMergedGearDatas, a => Array.Exists(a.mergeSet, b => b == newGear));
        Array.Sort(mergeOptions, (a, b) => a.mergePriority.CompareTo(b.mergePriority));

        for (int i = activeGear.Count - 1; i > -1; i--)
        {
            for (int j = 0; j < mergeOptions.Length; j++)
            {
                GearDataSO mergedGear = Array.Find(mergeOptions[j].mergeSet, a => a == activeGear[i].data);
                if (mergedGear)
                {
                    DeactivateGear(i);
                    return mergedGear;
                }
            }
        }
        return newGear;
    }
}