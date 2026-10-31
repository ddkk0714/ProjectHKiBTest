using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
[CreateAssetMenu(fileName = "Audio Type", menuName = "Type/Audio Type")]
public class AudioTypeSO : ScriptableObject, IPoolable
{
    [field: SerializeField] public int PoolSize { get; set; }
    public UnityEvent<int, int> OnGameObjectDisabled { get; set; }
    public int ID { get; set; }

    public int initOutTime;
    public bool playOneShot;
    public bool loop;
    // 비워 두면 믹서를 거치지 않는다(예전 동작).
    public AudioMixerGroup mixerGroup;

    public void OnDisable() { }
}
