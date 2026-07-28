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

    // eventProviderBehaviour를 인스펙터에서 명시적으로 지정하지 않으면 RouteModule을 기본값으로 쓴다.
    // RouteModule.Instance는 씬에 없으면 자동 생성되므로 항상 사용 가능 — null이 되는 경우는 없다.
    //
    // [2026-07-28] SaveModule.eventProvider는 여기서 넘기는 이 값 하나만 명시적 provider로 받지만,
    // EventManager는 SaveModule이 GameManager.instance.eventManager로 직접 찾아 항상 자동으로
    // 같이 합성한다(SaveModule.CollectEventProviders 참고) — 이전엔 단일 슬롯이라 provider가
    // 둘 이상이 되면 손봐야 할 부채로 남아 있었는데, 이제 SaveModule 쪽에서 리스트로 합성하므로
    // 여기서 EventManager를 따로 신경 쓸 필요는 없다. 세 번째 provider가 생기면, 그것도 씬 싱글턴
    // 수준으로 항상 접근 가능하면 CollectEventProviders에 추가하고, 여기 이 필드처럼 인스펙터로
    // 명시 주입해야 하는 대상이면 이 프로퍼티가 반환하는 목록을 넓히면 된다.
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