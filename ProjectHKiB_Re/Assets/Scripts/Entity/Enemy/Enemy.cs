using UnityEngine;
public class Enemy : Entity
{
    public int LastAttackIndicatorID { get; set; }

    public EnemyDataSO BaseData;
    [SerializeField] private DatabaseManagerSO databaseManager;
    public override void Start()
    {
        base.Start();
        GetInterface<IDamagable>().OnDie += OnDie;
    }
    protected void OnDestroy()
    {
        if (TryGetInterface(out IDamagable damagable))
        {
            damagable.OnDie -= OnDie;
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        if (BaseData == null)
        {
            Debug.Log("BaseData is Null");
            return;
        }
        databaseManager.SetIPhysics(this, BaseData);
        databaseManager.SetIAttackable(this, BaseData);
        databaseManager.SetIDamagable(this, BaseData);
        databaseManager.SetIFootstep(this, BaseData);
        databaseManager.SetIDirAnimatable(this, BaseData);
        databaseManager.SetITargetable(this, BaseData);
        //databaseManager.SetISkinable(this, BaseData);
        Initialize(BaseData.StateMachine);
        InitializeModules();
    }

    public void OnDie()
    {
        if (LastAttackIndicatorID != 0)
            GameManager.instance.attackAreaIndicatorManager.StopIndicating(LastAttackIndicatorID);
    }
}