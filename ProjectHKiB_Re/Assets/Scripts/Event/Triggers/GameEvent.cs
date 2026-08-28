using UnityEngine;

public abstract class GameEvent : MonoBehaviour
{
    // start event by enabling controller update
    public abstract void TriggerEvent();
}