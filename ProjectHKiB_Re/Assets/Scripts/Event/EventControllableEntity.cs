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

    // 이 진행도에서는 이 엔티티가 "없는" 상태인가(예: 이벤트에서 퇴장한 NPC).
    // 맵을 다시 로드하면 씬이 새로 깔려 퇴장이 없던 일이 되므로, 그걸 복원 정보로 남기기 위한 값이다.
    //
    // active가 아니라 startsInactive(기본 false = 보인다)인 이유: 기존 맵 데이터에는 이 필드가 없어
    // 역직렬화하면 bool 기본값 false가 들어온다. active였다면 기존 엔티티가 전부 사라졌을 것이다.
    public bool startsInactive;
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
                // 꺼진 상태로 복원할 대상은 여기서 끝낸다. 아래 Initialize/ChangeState는 상태 진입에서
                // 코루틴을 돌리는데, 비활성 GameObject에서 StartCoroutine을 부르면 예외가 난다.
                if (initinfos[i].startsInactive)
                {
                    Target.gameObject.SetActive(false);
                    continue;
                }

                Target.gameObject.SetActive(true);
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