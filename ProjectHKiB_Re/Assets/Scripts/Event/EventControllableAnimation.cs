using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AnimationInitializeInfo
{
    public EventFlagSO eventFlag;
    public int eventFlagCondition;
    public Vector3 position;
    public EnumManager.AnimDir dir;
    public string animationName;
}

public class EventControllableAnimation : EventControllableBase<SimpleAnimationPlayer>
{

    public List<AnimationInitializeInfo> InitInfos = new();

    public void Initialize(List<AnimationInitializeInfo> initinfos)
    {
        EventManager eventManager = GameManager.instance.eventManager;
        for (int i = 0; i < initinfos.Count; i++)
        {
            if (eventManager.HasEventFlag(initinfos[i].eventFlag, initinfos[i].eventFlagCondition))
            {
                Target.Initialize();
                Target.transform.position = initinfos[i].position;
                Target.SetDirection(initinfos[i].dir);
                Target.Play(initinfos[i].animationName);
            }
        }
    }


#if UNITY_EDITOR
    public Action<string, AnimationInitializeInfo> saveToMapDataSO;
    public void SaveCurrentStateToInitInfo(EventFlagSO flag, int flagValue)
    {
        AnimationInitializeInfo info = new()
        {
            eventFlag = flag,
            eventFlagCondition = flagValue,
            position = Target.transform.position,
            dir = Target.CurrentAnimDir,
            animationName = Target.CurrentAnimationName
        };
        saveToMapDataSO.Invoke(ID, info);
    }
#endif
}