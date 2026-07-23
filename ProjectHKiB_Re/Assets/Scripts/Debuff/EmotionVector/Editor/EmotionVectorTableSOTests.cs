using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class EmotionVectorTableSOTests
{
    private const string DefaultTablePath = "Assets/Scripts/Debuff/EmotionVector/EmotionVectorTable_Default.asset";

    private static EmotionVectorTableSO LoadDefaultTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<EmotionVectorTableSO>(DefaultTablePath);
        Assert.IsNotNull(table, $"에셋을 찾을 수 없음: {DefaultTablePath}");
        return table;
    }

    [Test]
    public void GetPosition_SadnessBlue_MatchesSpecCoordinate()
    {
        EmotionVectorTableSO table = LoadDefaultTable();
        Vector2 position = table.GetPosition(EmotionColor.SadnessBlue);

        Assert.AreEqual(-0.62f, position.x, 0.001f);
        Assert.AreEqual(-0.45f, position.y, 0.001f);
    }

    [Test]
    public void GetPosition_UnregisteredColor_ReturnsZeroAndWarnsOnce()
    {
        // 별도 인스턴스를 만들어 경고 억제 캐시가 다른 테스트에 영향받지 않게 함
        var table = ScriptableObject.CreateInstance<EmotionVectorTableSO>();

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Longing.*"));
        Vector2 first = table.GetPosition(EmotionColor.Longing);
        Assert.AreEqual(Vector2.zero, first);

        // 두 번째 조회는 경고가 없어야 함 (LogAssert.NoUnexpectedReceived로 검증)
        Vector2 second = table.GetPosition(EmotionColor.Longing);
        Assert.AreEqual(Vector2.zero, second);
        LogAssert.NoUnexpectedReceived();

        Object.DestroyImmediate(table);
    }

    [Test]
    public void VoidBlack_NotYetRegistered_ByDesign()
    {
        // Step 0.2 시점: 공허(VoidBlack)는 의도적으로 아직 테이블에 없음 (Step 2.3에서 추가 예정)
        EmotionVectorTableSO table = LoadDefaultTable();
        LogAssert.ignoreFailingMessages = true;
        Vector2 position = table.GetPosition(EmotionColor.VoidBlack);
        LogAssert.ignoreFailingMessages = false;

        Assert.AreEqual(Vector2.zero, position);
    }
}
