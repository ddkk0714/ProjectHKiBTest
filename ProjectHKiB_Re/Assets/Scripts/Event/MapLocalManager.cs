using System.Linq;
using UnityEngine;

public class MapLocalManager : MonoBehaviour
{
    public MapDataSO mapData;
    public EventTargets allEventTargets;

    [NaughtyAttributes.Button]
    public void AutoFindEventTargets()
    {
        allEventTargets = new();
        EventControllableEntity[] entityEvControllers = FindObjectsOfType<EventControllableEntity>();
        for (int i = 0; i < entityEvControllers.Length; i++)
        {
            if (entityEvControllers[i].gameObject.scene == gameObject.scene)
                allEventTargets.targetEntities[entityEvControllers[i].ID] = entityEvControllers[i];
        }

        EventControllableAnimation[] animEvControllers = FindObjectsOfType<EventControllableAnimation>();
        for (int i = 0; i < animEvControllers.Length; i++)
        {
            if (animEvControllers[i].gameObject.scene == gameObject.scene)
                allEventTargets.targetAnimations[animEvControllers[i].ID] = animEvControllers[i];
        }
    }

    // 엔티티/애니메이션 초기화보다 반드시 먼저 돌아야 한다 — 그쪽이 바로 이 플래그를 읽어
    // "어떤 상태 기계로 복원할지"를 고르기 때문이다(EventControllableEntity.Initialize).
    private void ApplyInitialEventFlags()
    {
        if (!mapData || mapData.initialEventFlags == null) return;

        EventManager eventManager = GameManager.instance.eventManager;
        if (!eventManager) return;

        foreach (var pair in mapData.initialEventFlags)
            eventManager.EnsureEventFlag(pair.Key, pair.Value);
    }

    public void Initialize()
    {
        ApplyInitialEventFlags();

        string[] entities = allEventTargets.targetEntities.Keys.ToArray();
        for (int i = 0; i < entities.Length; i++)
        {
            if (mapData.allEntityInitInfos.ContainsKey(entities[i]))
                allEventTargets.targetEntities[entities[i]].Initialize(mapData.allEntityInitInfos[entities[i]]);
        }

        string[] animations = allEventTargets.targetAnimations.Keys.ToArray();
        for (int i = 0; i < animations.Length; i++)
        {
            if (mapData.allAnimInitInfos.ContainsKey(animations[i]))
                allEventTargets.targetAnimations[animations[i]].Initialize(mapData.allAnimInitInfos[animations[i]]);
        }
    }


#if UNITY_EDITOR
    public void Awake()
    {
        string[] entities = allEventTargets.targetEntities.Keys.ToArray();
        for (int i = 0; i < entities.Length; i++)
        {
            allEventTargets.targetEntities[entities[i]].saveToMapDataSO += SaveCurrentEntityStateToInitInfo;
        }

        string[] animations = allEventTargets.targetAnimations.Keys.ToArray();
        for (int i = 0; i < animations.Length; i++)
        {
            allEventTargets.targetAnimations[animations[i]].saveToMapDataSO += SaveCurrentAnimationStateToInitInfo;
        }
    }

    public void SaveCurrentEntityStateToInitInfo(string targetID, EntityInitializeInfo info)
    {
        if (!mapData.allEntityInitInfos.ContainsKey(targetID)) mapData.allEntityInitInfos[targetID] = new();

        int existing = mapData.allEntityInitInfos[targetID].FindIndex(a => a.eventFlag == info.eventFlag && a.eventFlagCondition == info.eventFlagCondition);
        if (existing > -1)
        {
            mapData.allEntityInitInfos[targetID][existing] = info;
            Debug.Log("Already Existing! " + targetID + ": " + info.eventFlag + " = " + info.eventFlagCondition);
        }
        else mapData.allEntityInitInfos[targetID].Add(info);
    }

    public void SaveCurrentAnimationStateToInitInfo(string targetID, AnimationInitializeInfo info)
    {
        if (!mapData.allAnimInitInfos.ContainsKey(targetID)) mapData.allAnimInitInfos[targetID] = new();

        int existing = mapData.allAnimInitInfos[targetID].FindIndex(a => a.eventFlag == info.eventFlag && a.eventFlagCondition == info.eventFlagCondition);
        if (existing > -1)
        {
            mapData.allAnimInitInfos[targetID][existing] = info;
            Debug.Log("Already Existing! " + targetID + ": " + info.eventFlag + " = " + info.eventFlagCondition);
        }
        else mapData.allAnimInitInfos[targetID].Add(info);
    }
#endif
}