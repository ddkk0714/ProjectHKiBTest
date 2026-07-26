using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 게이트 1.1 중 "조합" 케이스는 실제 씬에서 재현 불가 (기존 EvaluateReaction이 살아있어
// 서로 다른 두 그룹의 감정을 동시에 유지할 수 없음). 그래서 순수 공식(ComputeVector)만 따로 검증한다.
public class EmotionVectorModuleTests
{
    private const string DefaultTablePath = "Assets/Scripts/Debuff/EmotionVector/EmotionVectorTable_Default.asset";

    private static readonly EmotionColor[] Colors =
    {
        EmotionColor.SadnessBlue,
        EmotionColor.SadnessSky,
        EmotionColor.ExcitementDeepPink,
        EmotionColor.HappinessYellow,
        EmotionColor.AngerOrange,
        EmotionColor.AngerScarlet,
        EmotionColor.VoidBlack,
        EmotionColor.FearDarkRed,
    };

    private static EmotionVectorTableSO LoadDefaultTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<EmotionVectorTableSO>(DefaultTablePath);
        Assert.IsNotNull(table, $"에셋을 찾을 수 없음: {DefaultTablePath}");
        return table;
    }

    private static Vector2 Compute(EmotionVectorTableSO table, Dictionary<EmotionColor, int> stacks, out float entropy)
    {
        EmotionVector v = EmotionVectorModule.ComputeVector(
            table, Colors,
            color => stacks.TryGetValue(color, out int s) ? s : 0,
            out entropy, out _);
        return new Vector2(v.X, v.Y);
    }

    [Test]
    public void SadnessBlue10Alone_MatchesExpectedVector()
    {
        var table = LoadDefaultTable();
        Vector2 v = Compute(table, new() { { EmotionColor.SadnessBlue, 10 } }, out _);

        Assert.AreEqual(-6.2f, v.x, 0.05f);
        Assert.AreEqual(-4.5f, v.y, 0.05f);
    }

    [Test]
    public void SadnessBlue10PlusHappiness10_MatchesExpectedVectorAndHighEntropy()
    {
        var table = LoadDefaultTable();
        Vector2 v = Compute(table, new()
        {
            { EmotionColor.SadnessBlue, 10 },
            { EmotionColor.HappinessYellow, 10 },
        }, out float entropy);

        Assert.AreEqual(1.6f, v.x, 0.05f);
        Assert.AreEqual(-3.3f, v.y, 0.05f);
        Assert.Greater(entropy, 0.5f, "서로 상쇄되는 조합이라 Entropy가 높아야 함");
    }

    [Test]
    public void SadnessBlue10PlusSadnessSky10_MatchesExpectedVectorAndLowEntropy()
    {
        var table = LoadDefaultTable();
        Vector2 v = Compute(table, new()
        {
            { EmotionColor.SadnessBlue, 10 },
            { EmotionColor.SadnessSky, 10 },
        }, out float entropy);

        Assert.AreEqual(-11.2f, v.x, 0.05f);
        Assert.AreEqual(-10.7f, v.y, 0.05f);
        Assert.Less(entropy, 0.05f, "같은 방향이라 Entropy가 0에 가까워야 함");
    }

    [Test]
    public void VoidBlack50Alone_ExcludedFromSum_ReturnsZero()
    {
        var table = LoadDefaultTable();
        Vector2 v = Compute(table, new() { { EmotionColor.VoidBlack, 50 } }, out _);

        Assert.AreEqual(Vector2.zero, v);
    }
}
