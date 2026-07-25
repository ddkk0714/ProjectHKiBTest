using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(StateController))]
public class SaveModule : InterfaceModule, IInitializable
{
    public void Initialize()
    {
        // 필요 시 모듈 초기화
    }

    // ====== (필수) StateMachine 레퍼런스만 인스펙터 연결용으로 유지 ======
    [SerializeField] private StateMachineSO saveStateMachine;
    [SerializeField] private StateMachineSO loadStateMachine;

    // ====== (선택) 튜닝 값 ======
    [SerializeField] private int maxWaitFrames = 120;

    // ====== 런타임 주입(인스펙터 보기용 노출 제거) ======
    [SerializeField] private InventoryManager inventory;
    [SerializeField] private GearManager gearManager;
    [SerializeField] private Component playerRoot;
    [SerializeField] private IEventSaveProvider eventProvider;

    // ====== (필수) 외부에서 읽을 수 있어야 하는 값들 ======
    [SerializeField] public int Slot { get; private set; } = -1;

    public SaveSlotData CurrentSaveData => _currentSaveData;
    public SaveSlotData LoadedData => _loadedData;

    public IDamagable ResolvedPlayer => _resolvedPlayer;
    public bool IsGearManagerReady => _isGearManagerReady;

    private SaveSlotData _currentSaveData;
    private SaveSlotData _loadedData;

    private IDamagable _resolvedPlayer;
    private bool _isGearManagerReady;

    // ====== 캐시 ======
    private Dictionary<string, GearDataSO> gearCache;
    private Dictionary<string, ItemDataSO> itemCache;

    // ====== PATH ======
    private string GetPath(int slot)
        => Path.Combine(Application.persistentDataPath, $"save_slot_{slot}.json");

    // ====== REGISTER ======
    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<SaveModule>(this);
    }

    // ====== PUBLIC: Start Save/Load ======
    public void StartSave(int slot, InventoryManager inv, GearManager gearMgr, Component player = null, IEventSaveProvider provider = null)
    {
        Slot = slot;
        inventory = inv;
        gearManager = gearMgr;
        playerRoot = player;
        eventProvider = provider;

        _resolvedPlayer = ResolvePlayerFromRegister(playerRoot);

        GetComponent<StateController>().ResetStateMachine(saveStateMachine);
    }

    public void StartLoad(int slot, InventoryManager inv, GearManager gearMgr, Component player = null, IEventSaveProvider provider = null)
    {
        Slot = slot;
        inventory = inv;
        gearManager = gearMgr;
        playerRoot = player;
        eventProvider = provider;

        _resolvedPlayer = ResolvePlayerFromRegister(playerRoot);

        GetComponent<StateController>().ResetStateMachine(loadStateMachine);
    }

    // ================= SAVE =================
    public void BeginSaveSession()
    {
        _currentSaveData = new SaveSlotData
        {
            savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            hp = (_resolvedPlayer != null) ? _resolvedPlayer.HP : 0f
        };
    }

    public void SaveItems()
    {
        if (inventory == null || _currentSaveData == null) return;

        _currentSaveData.items.Clear();

        foreach (var kv in inventory.playerInventory)
        {
            _currentSaveData.items.Add(new ItemSaveInfo
            {
                itemGuid = kv.Value.data.GUID,
                count = kv.Value.Count
            });
        }
    }

    public void SaveGears()
    {
        if (inventory == null || _currentSaveData == null) return;

        _currentSaveData.ownedGears.Clear();

        foreach (var gear in inventory.playerGearInventory.Values)
        {
            _currentSaveData.ownedGears.Add(new GearSaveInfo
            {
                gearGuid = gear.data.GUID,
                slot = gear.slot,
                equippedCards = new List<int>(gear.equippedCards)
            });
        }
    }

    public void SaveCards()
    {
        if (gearManager == null || _currentSaveData == null) return;

        _currentSaveData.cards.Clear();

        if (gearManager.playerCardEquipData == null)
            return;

        foreach (var card in gearManager.playerCardEquipData)
        {
            if (card == null) continue;

            var save = new CardSaveInfo
            {
                cardName = card.cardName,
                gearGuids = new List<string>()
            };

            var slots = card.GearList;
            if (slots != null)
            {
                foreach (var gear in slots)
                    save.gearGuids.Add((gear != null && gear.data != null) ? gear.data.GUID : null);
            }

            _currentSaveData.cards.Add(save);
        }
    }

    public void SaveEvents()
    {
        if (_currentSaveData == null) return;

        _currentSaveData.eventFlags.Clear();
        _currentSaveData.passages.Clear();

        if (eventProvider == null) return;

        if (eventProvider.EventFlags != null)
        {
            foreach (var kv in eventProvider.EventFlags)
                _currentSaveData.eventFlags.Add(new EventFlagSaveInfo { id = kv.Key, value = kv.Value });
        }

        if (eventProvider.Passages != null)
        {
            foreach (var kv in eventProvider.Passages)
                _currentSaveData.passages.Add(new PassageSaveInfo { id = kv.Key, opened = kv.Value });
        }
    }

    public void WriteSaveFile()
    {
        if (_currentSaveData == null) return;

        File.WriteAllText(GetPath(Slot), JsonUtility.ToJson(_currentSaveData, true));
        Debug.Log($"[SAVE] Slot {Slot} saved");
    }

    // ================= LOAD =================
    public bool ReadSaveFile()
    {
        string path = GetPath(Slot);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[LOAD] Slot {Slot} not found");
            _loadedData = null;
            return false;
        }

        _loadedData = JsonUtility.FromJson<SaveSlotData>(File.ReadAllText(path));

        if (_loadedData != null)
            Debug.Log($"[LOAD] Slot {Slot} read success");

        return _loadedData != null;
    }

    public void LoadItems()
    {
        if (inventory == null || _loadedData == null) return;

        inventory.playerInventory.Clear();

        foreach (var item in _loadedData.items)
        {
            ItemDataSO itemSO = FindItemSO(item.itemGuid);
            if (itemSO != null)
                inventory.AddItem(itemSO, item.count);
        }
    }

    public void LoadGears()
    {
        if (inventory == null || _loadedData == null) return;

        inventory.playerGearInventory.Clear();

        foreach (var g in _loadedData.ownedGears)
        {
            GearDataSO gearSO = FindGearSO(g.gearGuid);
            if (gearSO == null) continue;

            inventory.AddGear(gearSO);

            foreach (var gear in inventory.playerGearInventory.Values)
            {
                if (gear.data == gearSO)
                {
                    gear.slot = g.slot;
                    gear.equippedCards.Clear();
                    gear.equippedCards.AddRange(g.equippedCards);
                    break;
                }
            }
        }
    }

    public IEnumerator WaitGearManagerReady()
    {
        _isGearManagerReady = false;

        for (int frames = 0; frames < maxWaitFrames; frames++)
        {
            if (gearManager != null && gearManager.playerCardEquipData != null)
            {
                _isGearManagerReady = true;
                yield break;
            }
            yield return null;
        }

        _isGearManagerReady = (gearManager != null && gearManager.playerCardEquipData != null);
    }

    public void LoadCards()
    {
        if (inventory == null || gearManager == null || _loadedData == null) return;
        if (gearManager.playerCardEquipData == null) return;

        int cardCount = Mathf.Min(gearManager.playerCardEquipData.Count, _loadedData.cards.Count);

        for (int cardIndex = 0; cardIndex < cardCount; cardIndex++)
        {
            var runtimeCard = gearManager.playerCardEquipData[cardIndex];
            var savedCard = _loadedData.cards[cardIndex];
            if (runtimeCard == null) continue;

            runtimeCard.cardName = savedCard.cardName;

            if (runtimeCard.GearList == null)
                runtimeCard.Initialize();

            int slotCount = Mathf.Min(runtimeCard.GearList.Length, savedCard.gearGuids.Count);

            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                string guid = savedCard.gearGuids[slotIndex];

                if (string.IsNullOrEmpty(guid))
                {
                    runtimeCard.ResetGear(cardIndex, slotIndex);
                    continue;
                }

                GearDataSO gearSO = FindGearSO(guid);
                if (gearSO == null)
                {
                    runtimeCard.ResetGear(cardIndex, slotIndex);
                    continue;
                }

                Gear owned = null;
                foreach (var g in inventory.playerGearInventory.Values)
                {
                    if (g != null && g.data == gearSO) { owned = g; break; }
                }

                if (owned == null)
                {
                    inventory.AddGear(gearSO);
                    foreach (var g in inventory.playerGearInventory.Values)
                    {
                        if (g != null && g.data == gearSO) { owned = g; break; }
                    }
                }

                if (owned == null)
                {
                    runtimeCard.ResetGear(cardIndex, slotIndex);
                    continue;
                }

                gearManager.SetGearData(cardIndex, slotIndex, owned);
            }

            //gearManager.MergeGear(runtimeCard);
        }
    }

    public void ApplyHP()
    {
        if (_loadedData == null) return;

        ApplyHPNow(_resolvedPlayer, _loadedData.hp);
        StartCoroutine(ReapplyHpEndOfFrame(_resolvedPlayer, _loadedData.hp));
    }

    public void LoadEvents()
    {
        if (_loadedData == null) return;
        if (eventProvider == null) return;

        if (_loadedData.eventFlags != null)
        {
            foreach (var f in _loadedData.eventFlags)
                eventProvider.SetEventFlag(f.id, f.value);
        }

        if (_loadedData.passages != null)
        {
            foreach (var p in _loadedData.passages)
                eventProvider.SetPassage(p.id, p.opened);
        }
    }

    // ================= PLAYER RESOLVE =================
    private static IDamagable ResolvePlayerFromRegister(Component playerRoot)
    {
        if (playerRoot == null) return null;

        if (playerRoot is IInterfaceRegistable selfReg)
        {
            if (selfReg.TryGetInterface<IDamagable>(out var d0) && d0 != null)
                return d0;

            var d1 = selfReg.GetInterface<IDamagable>();
            if (d1 != null) return d1;
        }

        var regs = playerRoot.GetComponentsInChildren<MonoBehaviour>(true)
                             .OfType<IInterfaceRegistable>();

        foreach (var reg in regs)
        {
            if (reg.TryGetInterface<IDamagable>(out var d2) && d2 != null)
                return d2;

            var d3 = reg.GetInterface<IDamagable>();
            if (d3 != null) return d3;
        }

        foreach (var mb in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb is IDamagable d4) return d4;
        }

        return null;
    }

    // ================= HP APPLY =================
    private static void ApplyHPNow(IDamagable player, float savedHp)
    {
        if (player == null) return;

        float max = player.MaxHP;
        player.HP = Mathf.Clamp(savedHp, 0f, max);
        player.OnHPChanged?.Invoke(player.HP);
    }

    private static IEnumerator ReapplyHpEndOfFrame(IDamagable player, float savedHp)
    {
        yield return new WaitForEndOfFrame();
        ApplyHPNow(player, savedHp);
    }

    // ================= SO FIND (캐시) =================
    private void BuildGearCache()
    {
        gearCache = new Dictionary<string, GearDataSO>();
        var arr = Resources.LoadAll<GearDataSO>("Items/Gears");

        foreach (var so in arr)
        {
            if (so == null) continue;
            if (string.IsNullOrEmpty(so.GUID)) continue;
            gearCache[so.GUID] = so;
        }
    }

    private GearDataSO FindGearSO(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        if (gearCache == null) BuildGearCache();
        return gearCache.TryGetValue(guid, out var result) ? result : null;
    }

    private ItemDataSO FindItemSO(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;

        if (itemCache == null)
        {
            itemCache = new Dictionary<string, ItemDataSO>();
            foreach (var itemSO in Resources.LoadAll<ItemDataSO>("Items"))
            {
                if (itemSO == null) continue;
                if (string.IsNullOrEmpty(itemSO.GUID)) continue;
                itemCache[itemSO.GUID] = itemSO;
            }
        }

        return itemCache.TryGetValue(guid, out var result) ? result : null;
    }
}