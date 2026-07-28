using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EntityInitializeInfo
{
    public EventFlagSO eventFlag;
    public int eventFlagCondition;
    public Vector3 position;
    public EnumManager.AnimDir dir;
    public StateMachineSO stateMachine;
    public StateSO state;
}

public class EventControllableEntity : EventControllableBase<StateController>
{
    public void Initialize(List<EntityInitializeInfo> initinfos)
    {
        EventManager eventManager = GameManager.instance.eventManager;
        for (int i = 0; i < initinfos.Count; i++)
        {
            if (eventManager.HasEventFlag(initinfos[i].eventFlag, initinfos[i].eventFlagCondition))
            {
                Target.Initialize();
                Target.Initialize(initinfos[i].stateMachine);
                Target.ChangeState(initinfos[i].state);
                if (Target.TryGetInterface(out IPhysics phys)) phys.RealTeleport(initinfos[i].position);
                else Target.transform.position = initinfos[i].position;
                if (Target.TryGetInterface(out IDirAnimatable dirAnimatable)) dirAnimatable.SetAnimationDirection(initinfos[i].dir);
            }
        }
    }

#if UNITY_EDITOR
    public Action<string, EntityInitializeInfo> saveToMapDataSO;
    public void SaveCurrentStateToInitInfo(EventFlagSO flag, int flagValue)
    {
        EnumManager.AnimDir dir = EnumManager.AnimDir.D;
        if (Target.TryGetInterface(out IDirAnimatable animatable)) dir = animatable.AnimationDirection;

        EntityInitializeInfo info = new()
        {
            eventFlag = flag,
            eventFlagCondition = flagValue,
            position = Target.transform.position,
            dir = dir,
            stateMachine = Target.StateMachine,
            state = Target.CurrentState
        };
        saveToMapDataSO.Invoke(ID, info);
    }
#endif
}