public interface IEmotion : IInitializable
{
    void ApplyColor(EmotionColor color, int stack, float overrideDuration = -1f);

    void ApplyColor(
        EmotionColor color,
        int stack,
        EmotionModule.EmotionApplyTarget applyTarget,
        float overrideDuration = -1f
    );


    int GetStacks(EmotionColor color);
    int GetStacks(EmotionColor color, EmotionModule.EmotionApplyTarget applyTarget);

    bool HasColor(EmotionColor color);
    bool HasColor(EmotionColor color, EmotionModule.EmotionApplyTarget applyTarget);

    void RemoveColor(EmotionColor color, int stack = 1);
    void RemoveColor(EmotionColor color, EmotionModule.EmotionApplyTarget applyTarget, int stack = 1);

    string GetApproxRomanStack(EmotionColor color);
    string GetApproxRomanStack(EmotionColor color, EmotionModule.EmotionApplyTarget applyTarget);
}
