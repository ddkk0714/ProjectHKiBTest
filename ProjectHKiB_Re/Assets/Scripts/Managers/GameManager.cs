using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    public AudioManager audioManager;
    public ParticleManager particleManager;
    public DamageParticleManager damageParticleManager;
    public AttackAreaIndicatorManager attackAreaIndicatorManager;
    public InputManager inputManager;
    public PathFindingManager pathFindingManager;
    public ChunkManager chunkManager;
    public CameraManager cameraManager;
    public TimerManager timerManager;
    public ObjectSpawnManager objectSpawnManager;
    public ObjectDeathCountManager objectDeathCountManager;
    public GearManager gearManager;
    public InventoryManager inventoryManager;
    public UIManager UIManager;
    public GraffitiManager graffitiManager;
    public EventManager eventManager;
    public MapManager mapManager;

    public Player player;
    private void Awake()
    {
        instance = this;
        Application.targetFrameRate = 500;
    }
}
