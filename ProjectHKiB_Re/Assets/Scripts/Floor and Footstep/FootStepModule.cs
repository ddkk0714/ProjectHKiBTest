using System.Collections.Generic;
using UnityEngine;
public interface IFootstepBase
{
    public AudioDataSO DefaultFootstepAudio { get; set; }
}

public interface IFootstep : IFootstepBase, IInitializable
{
    public void PlayFootstepAudio(EnumManager.AnimDir animDir);
}

public class FootStepModule : InterfaceModule, IFootstep
{
    public float footstepAudioVolume;
    public AudioDataSO DefaultFootstepAudio { get; set; }
    private List<AudioDataSO> footstepAudioList;

    [SerializeField] private DirAnimatableModule dirAnimatableModule;

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IFootstep>(this);
    }
    public void Awake()
    {
        if (dirAnimatableModule != null)
            dirAnimatableModule.OnDirChanged += PlayFootstepAudio;
    }
    public void OnDestroy()
    {
        if (dirAnimatableModule != null)
            dirAnimatableModule.OnDirChanged -= PlayFootstepAudio;
    }

    public void Initialize()
    {
        footstepAudioList = new();
    }

    private PhysicsModule _physics;

    public void PlayFootstepAudio(EnumManager.AnimDir animDir)
    {
        // 지금 밟고 있는 바닥(물리 Ground)에 FloorInfoProvider가 있으면 그 바닥 소리로, 없으면 기본 발소리로 낸다.
        AudioDataSO stepAudio = DefaultFootstepAudio;
        float stepVolume = footstepAudioVolume;
        if (_physics == null) TryGetComponent(out _physics);
        if (_physics != null && _physics.Ground != null
            && _physics.Ground.TryGetComponent(out FloorInfoProvider floor) && floor.floorInfo != null)
        {
            stepAudio = floor.floorInfo.floorAudioData;
            stepVolume *= floor.floorInfo.volume;
        }

        if (stepAudio != null)
            GameManager.instance.audioManager.PlayAudioOneShot(stepAudio, stepVolume, transform.position);
        if (footstepAudioList != null && footstepAudioList.Count > 0)
        {
            for (int i = 0; i < footstepAudioList.Count; i++)
            {
                GameManager.instance.audioManager.PlayAudioOneShot(footstepAudioList[i], footstepAudioVolume, transform.position);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.TryGetComponent(out FloorInfoProvider component))
        {
            footstepAudioList.Add(component.floorInfo.floorAudioData);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.TryGetComponent(out FloorInfoProvider component))
        {
            footstepAudioList.Remove(component.floorInfo.floorAudioData);
        }
    }
}