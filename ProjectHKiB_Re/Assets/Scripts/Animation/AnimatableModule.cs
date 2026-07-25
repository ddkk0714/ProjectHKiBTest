using UnityEngine;
using UnityEngine.U2D.Animation;

public interface IAnimatableBase
{
    public SimpleAnimationDataSO MainAnimationData { get; set; }
    public SpriteLibraryAsset MainSpriteLibrary { get; set; }
}

public interface IAnimatable : IAnimatableBase, IInitializable
{
    public SimpleAnimationPlayer AnimationPlayer { get; set; }
    public string CurrentAnimation => AnimationPlayer.CurrentAnimationName;
    public void Play(string animationName);
}

public class AnimatableModule : InterfaceModule, IAnimatable
{
    [field: SerializeField] public SimpleAnimationDataSO MainAnimationData { get; set; }
    [field: SerializeField] public SpriteLibraryAsset MainSpriteLibrary { get; set; }
    [field: SerializeField] public SimpleAnimationPlayer AnimationPlayer { get; set; }
    public string CurrentAnimation => AnimationPlayer.CurrentAnimationName;

    [SerializeField] private SpriteLibrary mainSpriteLibrary;

    public override void Register(IInterfaceRegistable interfaceRegistable) => interfaceRegistable.RegisterInterface<IAnimatable>(this);

    public virtual void Initialize()
    {
        if (!MainAnimationData || !MainSpriteLibrary) return;
        SetAnimationData(MainAnimationData, MainSpriteLibrary);
        Play("Idle");
    }

    public void SetAnimationData(SimpleAnimationDataSO mainAnimationData, SpriteLibraryAsset mainSpriteLibrary)
    {
        this.mainSpriteLibrary.spriteLibraryAsset = mainSpriteLibrary;
        AnimationPlayer.animationData = mainAnimationData;
        Play("Idle");
    }

    public void Play(string animationName)
    {
        AnimationPlayer.gameObject.SetActive(true);
        AnimationPlayer.Play(animationName);
    }
}