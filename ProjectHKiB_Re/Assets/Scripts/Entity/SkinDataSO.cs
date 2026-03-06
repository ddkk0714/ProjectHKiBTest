using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.U2D.Animation;

[CreateAssetMenu(fileName = "Skin Data", menuName = "Scriptable Objects/Data/Skin Data")]
public class SkinDataSO : ScriptableObject
{
    public BodytypeDataSO bodyType;
    public Texture2D skinTexture;
    public Texture2D emissionSkinTexture;

    public void SetSKin(SpriteLibrary spriteLibrary, SimpleAnimationDataSO animationData, SpriteRenderer spriteRenderer)
    {
        spriteLibrary.spriteLibraryAsset = bodyType.Bodytypes[animationData];
        MaterialPropertyBlock materialPropertyBlock = new();
        materialPropertyBlock.SetTexture("_MainTex", bodyType.MainTex[animationData]);
        materialPropertyBlock.SetTexture("_SkinTex", skinTexture);
        materialPropertyBlock.SetTexture("_EmissionSkinTex", emissionSkinTexture);
        spriteRenderer.SetPropertyBlock(materialPropertyBlock);
    }
}
