using UnityEngine;
public interface ISkinableBase
{
    public SkinDataSO SkinData { get; set; }
}

public interface ISkinable : ISkinableBase, IInitializable
{
    public void ApplySkin();
    public void SetSkinData(SkinDataSO skinData);
}

public class SkinableModule : InterfaceModule, ISkinable
{
    [field: SerializeField] public SkinDataSO SkinData { get; set; }
    [SerializeField] protected SpriteRenderer mainSpriteRenderer;
    [SerializeField] protected SpriteRenderer effectSpriteRenderer;

    public override void Register(IInterfaceRegistable interfaceRegistable) => interfaceRegistable.RegisterInterface<ISkinable>(this);

    public virtual void Initialize() => ApplySkin();

    public virtual void SetSkinData(SkinDataSO skinData)
    {
        SkinData = skinData;
        ApplySkin();
    }

    public virtual void ApplySkin()
    {
        if (SkinData == null) return;
        MaterialPropertyBlock materialPropertyBlock = new();
        materialPropertyBlock.SetTexture("_SkinTex", SkinData.skinTexture);
        materialPropertyBlock.SetTexture("_EmissionSkinTex", SkinData.emissionSkinTexture);
        materialPropertyBlock.SetTexture("_MainTex", mainSpriteRenderer.sprite.texture);
        mainSpriteRenderer.SetPropertyBlock(materialPropertyBlock);

        materialPropertyBlock.SetTexture("_SkinTex", SkinData.effectSkinTexture);
        effectSpriteRenderer.SetPropertyBlock(materialPropertyBlock);
    }
}
