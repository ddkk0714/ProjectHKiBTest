using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using RouteFinding.Note; // NoteRouteGraphView(그래프 위치/펼침 상태 세이브 연동, 2026-07-21) — NoteModule 등과
                          // 달리 이 타입만 네임스페이스가 있어 using이 필요하다.

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

        if (eventProvider != null)
        {
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
        else
        {
            // provider 없이 저장하면 이벤트/통로 진행이 통째로 빈 채로 저장되는데도 예외가 나지 않아
            // 눈치채기 어렵다 — 조용히 실패하는 대신 경고를 남긴다.
            Debug.LogWarning("[SaveModule] eventProvider가 없어 이벤트 플래그/통로를 저장하지 않습니다.");
        }

        // 노트(핀 단서)/도감(유저 메모) 스냅샷 — eventProvider 유무와 무관하게 항상 저장한다.
        // IEventSaveProvider(Dictionary<string,bool> 전용)로 표현 안 되는 구조화 데이터라 이 메서드에
        // 같이 얹었다. NoteModule/CodexModule은 자동 생성되는 싱글턴이라 별도 주입 없이 Instance로 접근.
        _currentSaveData.noteEntries = NoteModule.Instance != null
            ? new List<NoteEntry>(NoteModule.Instance.Entries)
            : new List<NoteEntry>();
        _currentSaveData.codexUserEntries = CodexModule.Instance != null
            ? new List<CodexUserEntry>(CodexModule.Instance.UserEntries)
            : new List<CodexUserEntry>();

        // 노트 "저장한 루트" 보드 목록 — 위 noteEntries(현재 화면 상태)와 별개인 이름 붙은 다중 스냅샷.
        _currentSaveData.noteSavedBoards = NoteModule.Instance != null
            ? NoteModule.Instance.ExportSavedBoards()
            : new List<NoteSavedBoard>();

        // 노트 "단서 연동 모드"로 이어둔 단서 관계.
        _currentSaveData.noteClueLinks = NoteModule.Instance != null
            ? NoteModule.Instance.ExportClueLinks()
            : new List<NoteClueLink>();

        // [신설, 2026-07-21] 노트 그래프 위치/펼침 상태 — "저장한 루트" 보드와 같은 이유로 F5/F9
        // 일반 세이브에도 필요하다(안 그러면 로드 후 경로연동 단서가 카드로 되돌아가 있어, 위
        // noteClueLinks가 정상 복원돼도 그 간선이 그려질 노드 자체가 없다). NoteRouteGraphView는
        // NoteModule과 달리 씬 오브젝트라 Instance가 자동 생성되지 않는다 — null이면(예: 아직
        // Awake가 안 돈 극초반 타이밍) 그냥 빈 목록으로 저장.
        _currentSaveData.notePositions = NoteRouteGraphView.Instance != null
            ? NoteRouteGraphView.Instance.ExportCluePositions(NoteRouteGraphView.Instance.GetPlacedClueIds())
            : new List<CluePositionEntry>();
        _currentSaveData.noteExpandedClueIds = NoteRouteGraphView.Instance != null
            ? NoteRouteGraphView.Instance.GetExpandedClueIds().ToList()
            : new List<string>();

        // 지도/노트에서 마지막으로 커밋한 단일 경로(노트 좌측 그래프가 표시) — PathResult 자체가 아니라
        // 노드 GUID 순서만 저장한다(SaveData.cs 참고).
        _currentSaveData.selectedRouteNodeGuids = RouteModule.Instance?.SelectedRoute?.Nodes != null
            ? RouteModule.Instance.SelectedRoute.Nodes.Select(n => n.guid).ToList()
            : new List<string>();

        // 장착 장비 + 현재 위치 — 둘 다 위 진행 상태/선택 경로와 같은 이유로 함께 저장해야 로드 후
        // 노트의 계획 미리보기(난이도)·다음 구간 시작점이 저장 시점과 일치한다.
        _currentSaveData.equippedGears = RouteModule.Instance != null
            ? new List<EmotionColor>(RouteModule.Instance.EquippedGears)
            : new List<EmotionColor>();
        _currentSaveData.currentLocationGuid = RouteModule.Instance?.CurrentLocation?.guid ?? "";
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

        if (eventProvider != null)
        {
            // 항목을 하나씩 SetEventFlag/SetPassage로 넘기기 전에 구현체가 이전 상태를 지울 기회를 준다.
            eventProvider.ResetForLoad();

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
        else
        {
            Debug.LogWarning("[SaveModule] eventProvider가 없어 이벤트 플래그/통로를 복원하지 않습니다.");
        }

        // 지도/노트에서 마지막으로 커밋했던 단일 경로 복원 — Progress(위에서 이미 복원됨) 기준으로
        // 노드 GUID를 다시 MapNodeData로 해석한다.
        RouteModule.Instance?.ImportSelectedRoute(_loadedData.selectedRouteNodeGuids);

        // 장착 장비 + 현재 위치 복원 — SaveEvents()에서 같이 저장한 것과 대칭.
        RouteModule.Instance?.ImportEquippedGears(_loadedData.equippedGears);
        RouteModule.Instance?.ImportCurrentLocation(_loadedData.currentLocationGuid);

        // [신설, 2026-07-21] 순서 중요 — 그래프 펼침 상태/위치를 먼저 큐잉해둬야, 바로 아래
        // NoteModule.ImportFrom이 발행하는 OnNoteChanged → NotePanel.Refresh → SetData 재생성 때
        // 경로연동 단서가 카드가 아니라 노드로 만들어지고, 그 자리에 저장된 위치가 적용된다
        // ("저장한 루트" 보드 불러오기(NotePanel.HandleBoardLoadRequested)와 동일한 순서 규칙).
        NoteRouteGraphView.Instance?.ApplyExpandedClueIds(_loadedData.noteExpandedClueIds);
        NoteRouteGraphView.Instance?.ApplySavedPositions(_loadedData.notePositions);

        // 노트/도감 구조화 데이터 복원 — eventProvider 유무와 무관.
        NoteModule.Instance?.ImportFrom(_loadedData.noteEntries);
        CodexModule.Instance?.ImportUserEntries(_loadedData.codexUserEntries);
        NoteModule.Instance?.ImportSavedBoards(_loadedData.noteSavedBoards);
        NoteModule.Instance?.ImportClueLinks(_loadedData.noteClueLinks);
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