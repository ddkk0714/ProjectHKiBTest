using UnityEngine;

public class SaveTester : MonoBehaviour
{
    [Header("Required")]
    [SerializeField] private SaveModule saveModule;
    [SerializeField] private InventoryManager inventory;
    [SerializeField] private GearManager gearManager;

    [Header("Slot")]
    [SerializeField] private int slot = 0;

    [Header("Optional")]
    [SerializeField] private Component playerRoot;
    [SerializeField] private MonoBehaviour eventProviderBehaviour;

    [Header("Hotkeys (Optional)")]
    [SerializeField] private bool useHotkeys = true;
    [SerializeField] private KeyCode saveKey = KeyCode.F5;
    [SerializeField] private KeyCode loadKey = KeyCode.F9;

    // eventProviderBehaviour를 인스펙터에서 명시적으로 지정하지 않으면 RouteModule을 기본값으로 쓴다
    // (RouteModule이 IEventSaveProvider를 구현한 이 프로젝트의 유일한 provider, 2026-07-20).
    // RouteModule.Instance는 씬에 없으면 자동 생성되므로 항상 사용 가능 — null이 되는 경우는 없다.
    // 나중에 다른 시스템(대사/퀘스트 등)도 이벤트 플래그를 저장해야 하게 되면, SaveModule.eventProvider가
    // 단일 슬롯이라 provider 여러 개를 합성하는 구조로 다시 손봐야 한다.
    private IEventSaveProvider EventProvider =>
        (eventProviderBehaviour as IEventSaveProvider) ?? RouteModule.Instance;

    private void Reset()
    {
        // 자동 참조(있으면 잡힘)
        if (saveModule == null) saveModule = FindFirstObjectByType<SaveModule>();
        if (inventory == null) inventory = FindFirstObjectByType<InventoryManager>();
        if (gearManager == null) gearManager = FindFirstObjectByType<GearManager>();
    }

    private void Update()
    {
        if (!useHotkeys) return;

        if (Input.GetKeyDown(saveKey))
            Save();

        if (Input.GetKeyDown(loadKey))
            Load();
    }

    public void Save()
    {
        if (!ValidateRefs()) return;
        saveModule.StartSave(slot, inventory, gearManager, playerRoot, EventProvider);
    }

    public void Load()
    {
        if (!ValidateRefs()) return;
        saveModule.StartLoad(slot, inventory, gearManager, playerRoot, EventProvider);
    }

    public void SetSlot(int newSlot)
    {
        slot = Mathf.Max(0, newSlot);
    }

    private bool ValidateRefs()
    {
        if (saveModule == null)
        {
            Debug.LogError("[SaveTester] SaveModule reference is missing.");
            return false;
        }
        if (inventory == null)
        {
            Debug.LogError("[SaveTester] InventoryManager reference is missing.");
            return false;
        }
        if (gearManager == null)
        {
            Debug.LogError("[SaveTester] GearManager reference is missing.");
            return false;
        }
        return true;
    }
}