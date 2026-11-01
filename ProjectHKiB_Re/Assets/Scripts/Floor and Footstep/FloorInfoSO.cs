using UnityEngine;

[CreateAssetMenu(fileName = "Floor Info", menuName = "Scriptable Objects/Floor Info", order = 0)]
public class FloorInfoSO : ScriptableObject
{
    public AudioDataSO floorAudioData;
    [Tooltip("이 바닥 발소리에 곱할 음량(FootStepModule.footstepAudioVolume과 곱해진다).")]
    [Range(0f, 1f)] public float volume = 1f;
}