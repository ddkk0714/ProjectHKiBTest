using AYellowpaper.SerializedCollections;
using UnityEngine;
[CreateAssetMenu(fileName = "Damage Data", menuName = "Scriptable Objects/Data/Damage Data", order = 3)]
public class DamageDataSO : ScriptableObject
{
    public float damageCoefficient;
    public float knockBack;
    public LayerMask damageLayer;
    public AudioDataSO initialSound;
    public AudioDataSO hitSound;
    public bool camShake;
    public SerializedDictionary<EnumManager.AnimDir, ParticlePlayer> DLRUDamageEffects;
    public string effectAnimationClipName;
    public int animPlayerNumber;
    public bool attatchParticleToBody;
    public BoxData downwardDamageArea;
}
