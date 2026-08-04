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

    // ─── 세이브 연동 provider 합성 (2026-07-28) ─────────────────────
    // SaveModule.eventProvider는 예전엔 단일 슬롯이라 RouteModule 하나만 담을 수 있었다.
    // EventManager도 이벤트 플래그를 저장해야 하게 되면서 GameManager.instance.eventManager를
    // 직접 붙잡는 특별 취급 코드가 SaveEvents/LoadEvents에 따로 생겼었는데(부채로 예고된 상황),
    // 이제 "명시적으로 주입된 provider(보통 RouteModule) + 항상 존재하는 EventManager"를
    // 리스트로 합쳐 전부 같은 IEventSaveProvider 경로로 다룬다. 나중에 대사/퀘스트 등 provider가
    // 더 늘어나도 여기 한 곳에만 추가하면 된다.
    private List<IEventSaveProvider> CollectEventProviders()
    {
        var providers = new List<IEventSaveProvider>();
        if (eventProvider != null) providers.Add(eventProvider);

        var eventManager = GameManager.instance != null ? GameManager.instance.eventManager : null;
        if (eventManager != null) providers.Add(eventManager);

        return providers;
    }

    public void SaveEvents()
    {
        if (_currentSaveData == null) return;

        _currentSaveData.providerFlags.Clear();

        var providers = CollectEventProviders();
        if (providers.Count == 0)
        {
            // provider 없이 저장하면 이벤트/통로 진행이 통째로 빈 채로 저장되는데도 예외가 나지 않아
            // 눈치채기 어렵다 — 조용히 실패하는 대신 경고를 남긴다.
            Debug.LogWarning("[SaveModule] 등록된 IEventSaveProvider가 없어 이벤트 플래그/통로를 저장하지 않습니다.");
        }

        foreach (var provider in providers)
        {
            var snapshot = new ProviderFlagsSaveInfo { providerId = provider.ProviderId };

            if (provider.EventFlags != null)
            {
                foreach (var kv in provider.EventFlags)
                    snapshot.eventFlags.Add(new EventFlagSaveInfo { id = kv.Key, value = kv.Value });
            }

            if (provider.Passages != null)
            {
                foreach (var kv in provider.Passages)
                    snapshot.passages.Add(new PassageSaveInfo { id = kv.Key, opened = kv.Value });
            }

            _currentSaveData.providerFlags.Add(snapshot);
        }

        // 플레이어 버프(= 감정 스택) — 전용 세이브 State를 새로 만들지 않고 여기에 얹었다.
        // 이 메서드는 이미 노트/도감/경로/장비/위치까지 받아내는 "나머지 전부" 자리가 된 지 오래다.
        SaveBuffs();

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

        // 플레이어의 실제 씬 좌표 + 현재 맵 — currentLocationGuid(RouteFinding 추상 노드)와 별개.
        SavePlayerPosition();

        // 누적 게임 내 시간 — 일시정지 구간은 제외된 값이다(TimeManager 참고).
        SaveGameTime();
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

        var providers = CollectEventProviders();
        if (providers.Count == 0)
            Debug.LogWarning("[SaveModule] 등록된 IEventSaveProvider가 없어 이벤트 플래그/통로를 복원하지 않습니다.");

        foreach (var provider in providers)
        {
            var snapshot = _loadedData.providerFlags?.Find(p => p.providerId == provider.ProviderId);

            if (snapshot == null)
            {
                // 이 provider의 데이터가 세이브에 없다 — 이 구조로 개편되기 전(2026-07-28 이전)에
                // 만들어진 세이브이거나, 그 사이에 새로 추가된 provider다. ResetForLoad를 부르면
                // 인스펙터/게임 시작 시점에 저작된 초기 상태까지 지워버리므로 아예 건드리지 않는다.
                // 대가로 "정말 플래그가 하나도 없는 상태"를 저장한 세이브는 로드해도 현재 상태가
                // 안 지워진다 — 게임 시작 상태가 곧 그 상태라 실질적인 차이가 없다고 보고 감수했다.
                continue;
            }

            // 항목을 하나씩 SetEventFlag/SetPassage로 넘기기 전에 구현체가 이전 상태를 지울 기회를 준다.
            provider.ResetForLoad();

            if (snapshot.eventFlags != null)
            {
                foreach (var f in snapshot.eventFlags)
                    provider.SetEventFlag(f.id, f.value);
            }

            if (snapshot.passages != null)
            {
                foreach (var p in snapshot.passages)
                    provider.SetPassage(p.id, p.opened);
            }
        }

        // ※ EventManager 쪽 이벤트 플래그: 이미 Initialize()가 끝난 월드 오브젝트
        //   (EventControllableEntity/Animation)는 여기서 값을 되돌려도 스스로 다시 배치되지
        //   않는다 — 로드 후 맵을 다시 들어갈 때 반영된다.

        // 플레이어 버프(= 감정 스택) 복원 — SaveBuffs()와 대칭.
        // 로드 순서상 이 시점은 LoadGears/LoadCards 이후라 SourceGear를 인벤토리에서 되찾을 수 있고,
        // ApplyHP 이후이기도 하다. 버프가 MaxHP를 건드려도 ApplyHP가 프레임 끝에 한 번 더
        // 재적용(ReapplyHpEndOfFrame)하면서 복원된 버프 기준으로 다시 클램프되므로 순서 문제는 없다.
        LoadBuffs();

        // 지도/노트에서 마지막으로 커밋했던 단일 경로 복원 — Progress(위에서 이미 복원됨) 기준으로
        // 노드 GUID를 다시 MapNodeData로 해석한다.
        RouteModule.Instance?.ImportSelectedRoute(_loadedData.selectedRouteNodeGuids);

        // 장착 장비 + 현재 위치 복원 — SaveEvents()에서 같이 저장한 것과 대칭.
        RouteModule.Instance?.ImportEquippedGears(_loadedData.equippedGears);
        RouteModule.Instance?.ImportCurrentLocation(_loadedData.currentLocationGuid);

        // 플레이어의 실제 씬 좌표 + 현재 맵 복원 — SavePlayerPosition()과 대칭.
        LoadPlayerPosition();

        // 누적 게임 내 시간 복원 — SaveGameTime()과 대칭.
        LoadGameTime();

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

    // ================= BUFF (감정 스택 포함) =================
    // 감정 스택은 EmotionModule이 따로 들고 있지 않고 BuffInfo.BuffStack을 그대로 읽으므로
    // (EmotionModule.GetStacks), 버프를 저장/복원하면 감정 상태도 같이 따라온다.
    private StatBuffRegistrySO _buffRegistry;

    private StatBuffRegistrySO BuffRegistry
        => _buffRegistry != null ? _buffRegistry : (_buffRegistry = Resources.Load<StatBuffRegistrySO>("StatBuffRegistry"));

    public void SaveBuffs()
    {
        if (_currentSaveData == null) return;

        _currentSaveData.buffs.Clear();

        var buffable = ResolveFromPlayer<IBuffable>(playerRoot);
        if (buffable == null || buffable.CurrentBuffs == null)
        {
            Debug.LogWarning($"[SaveModule] SaveBuffs: playerRoot='{(playerRoot != null ? playerRoot.name : "(null)")}'에서 IBuffable을 찾을 수 없어 버프/감정 스택을 저장하지 않습니다.");
            return;
        }

        var vectorModules = ThresholdBuffOwners();

        foreach (var info in buffable.CurrentBuffs)
        {
            if (info == null || info.Buff == null) continue;

            // 무한 지속 버프는 남은 시간 개념이 없다 — 복원할 때도 타이머를 걸지 않도록 -1로 저장.
            float remain = -1f;
            if (!info.Buff.IsBuffTimeInfinite && info.Cooltime != null && GameManager.instance != null)
                remain = info.Cooltime.RemainTime;

            // 역치 버프(예: Madness_Other)는 EmotionVectorModule이 걸 때 24시간으로 오버라이드해서
            // 저장한다 — 이 SO가 "색상 버프"와 별개 존재가 아니라 **같은 BuffInfo를 공유**하는 경우가
            // 있기 때문에(BuffStackType=Stack이라 축 활성화 시 +1이 기존 색상 스택 위에 그냥 누적됨),
            // 이 버프를 통째로 저장에서 빼면 그 안에 섞여 있던 진짜 색상 스택까지 같이 날아간다
            // (실제로 겪은 버그 — 세이브/로드 후 Madness/Sorrow 계열 스택이 어긋남).
            // 그래서 스택은 항상 그대로 저장하고, 지속시간만 SO 기본값(-1)으로 되돌린다 — 그러면
            // 로드 후 정상 BuffTime(5~6초)으로 자연 만료되어 "안 사라지는 24시간 버프" 문제도
            // 그대로 해결된다. 축 장부 자체는 ResetAxisStateForLoad()가 별도로 정리한다.
            if (remain > 0f && IsThresholdOwned(vectorModules, info.Buff))
                remain = -1f;

            _currentSaveData.buffs.Add(new BuffSaveInfo
            {
                buffId = info.Buff.SaveId,
                buffStack = info.BuffStack,
                remainTime = remain
            });
        }

    }

    public void LoadBuffs()
    {
        if (_loadedData == null) return;

        var buffable = ResolveFromPlayer<IBuffable>(playerRoot);
        if (buffable == null || buffable.CurrentBuffs == null)
        {
            Debug.LogWarning($"[SaveModule] LoadBuffs: playerRoot='{(playerRoot != null ? playerRoot.name : "(null)")}'에서 IBuffable을 찾을 수 없어 버프/감정 스택을 복원하지 않습니다.");
            return;
        }

        // 기존 버프 제거 — 리스트만 비우면 버프가 걸어둔 스탯 보정이 그대로 남으므로 반드시 UnBuff를 탄다.
        // ignorePermanent=true라야 Permanent 타입도 걷힌다. 순회 중 CurrentBuffs가 변형되므로 복사본으로 돈다.
        foreach (var existing in new List<BuffInfo>(buffable.CurrentBuffs))
        {
            if (existing == null || existing.Buff == null) continue;
            buffable.UnBuff(existing.Buff, existing.BuffStack, 0, true);
        }

        if (buffable.CurrentBuffs.Count > 0)
            Debug.LogWarning($"[SaveModule] UnBuff 후에도 버프 {buffable.CurrentBuffs.Count}개가 남아 있습니다 — 스탯이 중복 적용될 수 있습니다.");

        // 역치 버프 장부 백지화 — 위에서 버프를 전부 걷어냈으므로 EmotionVectorModule의 _activeAxes도
        // 같이 비워야 한다. 안 그러면 옛 축이 활성으로 남아 재부여가 건너뛰어지고, 복원된 스택에
        // 맞는 역치 버프가 다시 걸리지 않는다. 저장된 버프가 하나도 없어도 반드시 거쳐야 하므로
        // 아래 early return보다 앞에 둔다.
        foreach (var module in ThresholdBuffOwners())
        {
            if (module != null) module.ResetAxisStateForLoad();
        }

        if (_loadedData.buffs == null || _loadedData.buffs.Count == 0)
            return;

        var registry = BuffRegistry;
        if (registry == null)
        {
            Debug.LogWarning("[SaveModule] Resources/StatBuffRegistry를 찾을 수 없어 버프를 복원하지 않습니다.");
            return;
        }

        var loadVectorModules = ThresholdBuffOwners();

        foreach (var saved in _loadedData.buffs)
        {
            if (!registry.TryGet(saved.buffId, out var buffSO) || buffSO == null)
            {
                Debug.LogWarning($"[SaveModule] 버프 에셋을 찾을 수 없습니다(id={saved.buffId}). StatBuffRegistry에서 '버프 에셋 전체 다시 수집'을 실행해보세요.");
                continue;
            }

            // remainTime이 -1(무한 지속)이거나 0 이하면 overrideTime을 -1로 넘겨 SO 기본값/무한 규칙을 그대로 따르게 한다.
            float duration = saved.remainTime > 0f ? saved.remainTime : -1f;

            // 이 필드가 -1로 저장되기 전(구버전 세이브)에 만들어진 파일 대비 안전장치 — 역치 버프인데
            // 저장된 남은 시간이 SO의 BuffTime보다 길면 24시간 오버라이드가 그대로 저장된 것이다.
            // 역치 소유 버프로 한정해서만 잘라낸다 — 다른 시스템이 의도적으로 BuffTime보다 긴
            // overrideTime을 주는 경우(예: 아이템으로 지속시간 연장)까지 건드리면 안 되기 때문이다.
            if (duration > 0f && !buffSO.IsBuffTimeInfinite && buffSO.BuffTime > 0f && duration > buffSO.BuffTime
                && IsThresholdOwned(loadVectorModules, buffSO))
            {
                Debug.LogWarning($"[SaveModule] 역치 버프 '{buffSO.name}'의 저장된 남은 시간({duration:F1}s)이 BuffTime({buffSO.BuffTime:F1}s)보다 깁니다(구버전 세이브 추정) — BuffTime으로 잘라 복원합니다.");
                duration = buffSO.BuffTime;
            }

            // gearGuid가 비어 있으면 "장비 없음" 상태였다는 뜻이다 — 이때 sourceGear로 C# null을
            // 넘기면 안 된다. Card.cs는 "빈 슬롯"을 null이 아니라 new Gear(null) 플레이스홀더로
            // 표현하고, 이 플레이스홀더는 LoadCards()가 재초기화할 때마다 새 인스턴스로 교체된다.
            // FindBuff/UnBuff는 SourceGear를 참조(==) 비교하므로, 여기서 null을 쓰면 이 버프는
            // 영원히 "현재 장비 없음" 상태(GetCurrentSourceGear()가 반환하는, 로드 후 새로 만들어진
            // 플레이스홀더)와 매치되지 않는 고아가 된다 — EmotionModule.GetStacks가 항상 0을 보고하고,
            // 이 버프를 대상으로 한 이후의 모든 UnBuff(자연 만료 포함)도 못 찾아서 조용히 무시된다.
            // 그래서 빈 GUID는 FindOwnedGear가 아니라 지금 시점의 GetCurrentSourceGear()로 푼다.

            buffable.Buff(buffSO, saved.buffStack, 1, duration);
        }
    }

    private EmotionVectorModule[] ThresholdBuffOwners()
        => playerRoot != null
            ? playerRoot.GetComponentsInChildren<EmotionVectorModule>(true)
            : System.Array.Empty<EmotionVectorModule>();

    private static bool IsThresholdOwned(EmotionVectorModule[] owners, StatBuffSO buff)
    {
        foreach (var module in owners)
        {
            if (module != null && module.OwnsBuff(buff)) return true;
        }

        return false;
    }

    // ================= PLAYER POSITION (실제 씬 좌표 + 현재 맵) =================
    // RouteModule.CurrentLocation(currentLocationGuid)은 RouteFinding 추상 그래프 노드 단위라
    // 지도 화면·난이도 계산에는 쓰이지만, 실제 게임플레이 씬 안에서 플레이어가 정확히 어디
    // 서 있었는지는 담지 못한다. 이 두 메서드가 그 간극을 메운다.
    //
    // [2026-08-04] 맵 전환 담당을 MapChangeManager(SceneManager 기반) → MapManager(Addressables
    // 기반)로 이관했다. 저장하는 문자열도 "씬 이름"에서 "맵 Addressable ID"로 의미가 바뀌었지만,
    // Addressable 주소를 씬 이름으로 통일해 쓰기로 해서 값 자체는 같다 — 그래서 SaveData의
    // currentMapSceneName 필드명을 그대로 두었고 기존 세이브도 마이그레이션 없이 읽힌다.
    [SerializeField] private MapDataRegistrySO mapDataRegistry;

    private MapManager ResolveMapManager()
        => GameManager.instance == null ? null : GameManager.instance.mapManager;

    public void SavePlayerPosition()
    {
        if (_currentSaveData == null || playerRoot == null) return;

        MapManager mapManager = ResolveMapManager();
        MapDataSO currentMap = mapManager != null ? mapManager.CurrentMapData : null;

        _currentSaveData.hasPlayerPosition = true;
        _currentSaveData.currentMapSceneName = currentMap != null ? currentMap.mapAddressableID : "";
        _currentSaveData.playerPosition = playerRoot.transform.position;

        var dirAnimatable = ResolveFromPlayer<IDirAnimatable>(playerRoot);
        if (dirAnimatable != null)
            _currentSaveData.playerDirection = dirAnimatable.AnimationDirection;
    }

    public void LoadPlayerPosition()
    {
        // hasPlayerPosition이 false면 이 필드가 생기기 전(구버전) 세이브다 — playerPosition이
        // JsonUtility 기본값(0,0,0)으로 채워져 있을 뿐 실제로 저장된 좌표가 아니므로, 원점
        // 텔레포트 같은 오동작을 막기 위해 아예 건드리지 않는다.
        if (_loadedData == null || !_loadedData.hasPlayerPosition || playerRoot == null) return;

        MapManager mapManager = ResolveMapManager();
        Vector3 targetPosition = _loadedData.playerPosition;
        EnumManager.AnimDir targetDirection = _loadedData.playerDirection;

        string savedMapID = _loadedData.currentMapSceneName;
        string currentMapID = mapManager != null && mapManager.CurrentMapData != null
            ? mapManager.CurrentMapData.mapAddressableID
            : "";

        bool needsMapChange = mapManager != null
            && !string.IsNullOrEmpty(savedMapID)
            && savedMapID != currentMapID;

        if (!needsMapChange)
        {
            TeleportPlayer(targetPosition, targetDirection);
            return;
        }

        // 저장된 건 문자열 ID뿐이라, 실제 MapDataSO를 레지스트리에서 되찾아야 LoadMap()에 넘길 수 있다.
        MapDataSO targetMap = mapDataRegistry != null ? mapDataRegistry.Find(savedMapID) : null;
        if (targetMap == null)
        {
            // 맵 전환은 포기하되 좌표 복원까지 버리지는 않는다 — 현재 맵이 이미 맞을 수도 있고,
            // 아니더라도 원점에 방치하는 것보다는 낫다.
            Debug.LogWarning($"[SaveModule] 맵 ID '{savedMapID}'에 해당하는 MapDataSO를 찾지 못했습니다. " +
                             (mapDataRegistry == null
                                 ? "MapDataRegistry가 연결되지 않았습니다."
                                 : "레지스트리에서 Collect All을 실행했는지 확인하세요.") +
                             " 맵 전환 없이 좌표만 복원합니다.");
            TeleportPlayer(targetPosition, targetDirection);
            return;
        }

        // LoadMap()은 Addressables 비동기 콜백이라 완료를 동기적으로 기다릴 수 없다 — 완료 이벤트가
        // 올 때까지 텔레포트를 미룬다. 로드 스테이트머신 자체는 이 완료를 기다리지 않고 그대로
        // 진행한다(맵 전환이 끝나는 동안 플레이어가 이전 위치에 남아 있다가, 전환이 끝나는 순간
        // 저장된 좌표/방향으로 텔레포트된다).
        void OnMapLoaded(MapDataSO _)
        {
            mapManager.OnMapLoaded -= OnMapLoaded;
            TeleportPlayer(targetPosition, targetDirection);
        }

        // 시작 지점 배치를 건너뛰게 한다 — 복원은 저장된 좌표로 직접 텔레포트하므로 배치가
        // 필요 없고, MapStartPos.SetPlayerToStartPos()가 endEvent까지 발동시켜서 그대로 두면
        // 세이브를 불러올 때마다 맵 진입 이벤트가 잘못 재생된다.
        ResolveStartPosPlacer()?.SkipNextPlacement();

        mapManager.OnMapLoaded += OnMapLoaded;
        mapManager.LoadMap(targetMap);
    }

    private MapStartPosPlacer _startPosPlacer;

    private MapStartPosPlacer ResolveStartPosPlacer()
        => _startPosPlacer != null ? _startPosPlacer : (_startPosPlacer = FindObjectOfType<MapStartPosPlacer>(true));

    // ================= GAME TIME =================
    // TimeManager.GameTime — 일시정지를 뺀 누적 게임 내 시간. 실제 플레이 시간과 다르고
    // (메뉴를 열어둔 시간은 빠진다) Time.time과도 다르다(세이브를 거쳐 이어진다).
    private static TimeManager ResolveTimeManager()
        => GameManager.instance == null ? null : GameManager.instance.timeManager;

    public void SaveGameTime()
    {
        if (_currentSaveData == null) return;

        TimeManager timeManager = ResolveTimeManager();
        if (timeManager == null) return;

        _currentSaveData.gameTime = timeManager.GameTime;
    }

    public void LoadGameTime()
    {
        if (_loadedData == null) return;

        TimeManager timeManager = ResolveTimeManager();
        if (timeManager == null) return;

        timeManager.SetGameTime(_loadedData.gameTime);
    }

    private void TeleportPlayer(Vector3 position, EnumManager.AnimDir direction)
    {
        var physics = ResolveFromPlayer<IPhysics>(playerRoot);
        if (physics != null) physics.RealTeleport(position);
        else playerRoot.transform.position = position; // IPhysics 없는 대상(테스트 리그 등) 대비 폴백

        var dirAnimatable = ResolveFromPlayer<IDirAnimatable>(playerRoot);
        if (dirAnimatable == null) return;

        dirAnimatable.SetAnimationDirection(direction);

        // SetAnimationDirection은 CurrentAnimDir 값만 즉시 바꾼다 — 화면에 보이는 스프라이트는
        // SimpleAnimationPlayer.ApplyFrame이 이 값을 읽어 갱신하는데, 그 호출은 지금 재생 중인
        // 클립이 스스로 다음 프레임으로 넘어갈 때(클립의 프레임 지속시간만큼, 보통 0.1~0.2초 뒤)
        // 되어야 일어난다. resetWhenDirectionChange가 꺼진 클립(대개 Idle)이 재생 중이면 이 지연이
        // 그대로 체감된다. 로드 직후에는 이 지연이 어색하므로, 지금 재생 중인 클립을 강제로
        // 즉시 재시작해 첫 프레임을 새 방향으로 바로 반영한다.
        var animationPlayer = dirAnimatable.AnimationPlayer;
        if (animationPlayer != null)
            animationPlayer.Play(animationPlayer.CurrentAnimationName);
    }

    // ================= PLAYER RESOLVE =================
    // ResolvePlayerFromRegister(IDamagable 전용)와 같은 탐색 순서를 임의 인터페이스로 일반화한 것.
    private static T ResolveFromPlayer<T>(Component playerRoot) where T : class
    {
        if (playerRoot == null) return null;

        if (playerRoot is IInterfaceRegistable selfReg)
        {
            if (selfReg.TryGetInterface<T>(out var t0) && t0 != null) return t0;

            var t1 = selfReg.GetInterface<T>();
            if (t1 != null) return t1;
        }

        var regs = playerRoot.GetComponentsInChildren<MonoBehaviour>(true)
                             .OfType<IInterfaceRegistable>();

        foreach (var reg in regs)
        {
            if (reg.TryGetInterface<T>(out var t2) && t2 != null) return t2;

            var t3 = reg.GetInterface<T>();
            if (t3 != null) return t3;
        }

        foreach (var mb in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb is T t4) return t4;
        }

        return null;
    }

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