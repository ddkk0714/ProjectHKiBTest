using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class EmotionThresholdProfileSOTests
{
    private const string RusherProfilePath = "Assets/Scripts/Debuff/EmotionVector/EmotionThresholdProfileSO_Rusher.asset";
    private const string DefaultProfilePath = "Assets/Scripts/Debuff/EmotionVector/EmotionThresholdProfileSO_Default.asset";

    [Test]
    public void GetEffectiveThreshold_Mental50_IsHalfOfMental100()
    {
        var profile = AssetDatabase.LoadAssetAtPath<EmotionThresholdProfileSO>(RusherProfilePath);
        Assert.IsNotNull(profile, $"에셋을 찾을 수 없음: {RusherProfilePath}");

        float atMental100 = profile.GetEffectiveThreshold(EmotionAxis.PositiveY, 100f);
        float atMental50 = profile.GetEffectiveThreshold(EmotionAxis.PositiveY, 50f);

        Assert.AreEqual(50f, atMental100, 0.01f);
        Assert.AreEqual(25f, atMental50, 0.01f);
        Assert.AreEqual(atMental100 * 0.5f, atMental50, 0.01f);
    }

    [Test]
    public void GetEffectiveThreshold_UnconfiguredAxis_ReturnsPositiveInfinity()
    {
        var profile = ScriptableObject.CreateInstance<EmotionThresholdProfileSO>();

        LogAssert.ignoreFailingMessages = true;
        float result = profile.GetEffectiveThreshold(EmotionAxis.PositiveY, 100f);
        LogAssert.ignoreFailingMessages = false;

        Assert.IsTrue(float.IsPositiveInfinity(result), "미설정 축은 절대 발동되면 안 되므로 PositiveInfinity여야 함");
        Object.DestroyImmediate(profile);
    }

    [Test]
    public void EmotionVectorModule_ProfileNull_FallsBackToDefaultProfile_NoError()
    {
        var go = new GameObject("EmotionVectorModule_FallbackTest");
        try
        {
            var vectorModule = go.AddComponent<EmotionVectorModule>();
            var defaultProfile = AssetDatabase.LoadAssetAtPath<EmotionThresholdProfileSO>(DefaultProfilePath);
            Assert.IsNotNull(defaultProfile, $"에셋을 찾을 수 없음: {DefaultProfilePath}");

            SerializedObject so = new SerializedObject(vectorModule);
            so.FindProperty("profile").objectReferenceValue = null;
            so.FindProperty("defaultProfile").objectReferenceValue = defaultProfile;
            so.ApplyModifiedPropertiesWithoutUndo();

            EmotionThresholdProfileSO resolved = vectorModule.GetEffectiveProfile();
            Assert.AreEqual(defaultProfile, resolved);

            float threshold = vectorModule.GetEffectiveThreshold(EmotionAxis.PositiveY);
            Assert.AreEqual(50f, threshold, 0.01f); // Player(Enemy 없음) → Mental 100 고정 → base 그대로
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EmotionVectorModule_BothProfilesNull_ReturnsPositiveInfinity_WarnsOnce()
    {
        var go = new GameObject("EmotionVectorModule_NoProfileTest");
        try
        {
            var vectorModule = go.AddComponent<EmotionVectorModule>();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*profile.*"));
            float first = vectorModule.GetEffectiveThreshold(EmotionAxis.PositiveY);
            Assert.IsTrue(float.IsPositiveInfinity(first));

            float second = vectorModule.GetEffectiveThreshold(EmotionAxis.PositiveY);
            Assert.IsTrue(float.IsPositiveInfinity(second));
            LogAssert.NoUnexpectedReceived(); // 경고 1회만
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
