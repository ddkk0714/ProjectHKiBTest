#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

// 게이트 1.3 ★설계 검증 지점 — 좌표에서 산출한 ATK/Speed 방향(부호)이 기존 StatBuffSO 실제 수치와
// 어긋나지 않는지 확인한다. 통과 조건은 "수치 일치"가 아니라 "부호 일치"다 (spec §3.3).
// 크기(k_atk, k_spd)는 나중에 튜닝하면 되지만, 부호가 틀리면 §2.2 좌표 자체가 잘못된 것이다.
public static class EmotionVectorStatComparisonReport
{
    private const string TablePath = "Assets/Scripts/Debuff/EmotionVector/EmotionVectorTable_Default.asset";
    private const string OtherBuffFolder = "Assets/Scripts/Debuff/EmotionBuffs/OtherBuff";
    private const int TestStack = 10;

    private static readonly (EmotionColor color, string assetName)[] Targets =
    {
        (EmotionColor.FearDarkRed, "FearDarkRed_Other"),
        (EmotionColor.AngerScarlet, "AngerScarlet_Other"),
        (EmotionColor.AngerOrange, "AngerOrange_Other"),
        (EmotionColor.ExcitementDeepPink, "Excitement_Other"),
        (EmotionColor.HappinessYellow, "HappinessYellow_Other"),
        (EmotionColor.SadnessSky, "SadnessSky_Other"),
        (EmotionColor.SadnessBlue, "SadnessBlue_Other"),
    };

    [MenuItem("Emotion/Vector Stat Comparison Report")]
    public static void Run()
    {
        var table = AssetDatabase.LoadAssetAtPath<EmotionVectorTableSO>(TablePath);
        if (table == null)
        {
            Debug.LogError($"[EmotionVectorStatComparisonReport] 테이블 에셋을 찾을 수 없음: {TablePath}");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("색상 | 좌표(x,y) | 예측ATK | 실제ATK | ATK | 예측Speed | 실제Speed | Speed | 2사분면(방어력 감소 후보)");
        bool anyMismatch = false;

        foreach ((EmotionColor color, string assetName) in Targets)
        {
            Vector2 pos = table.GetPosition(color);
            Vector2 contribution = pos * TestStack;

            // ATK 배율 = 1 - k_atk*V.x, Speed 배율 = 1 + k_spd*V.y (spec §3.3). k값은 부호에 영향 없어 1로 고정.
            string predictedAtk = SignLabel(-contribution.x);
            string predictedSpeed = SignLabel(contribution.y);
            bool inTopLeftRegion = contribution.x < 0f && contribution.y > 0f;

            var buff = AssetDatabase.LoadAssetAtPath<StatBuffSO>($"{OtherBuffFolder}/{assetName}.asset");
            if (buff == null)
            {
                sb.AppendLine($"{color} | ({pos.x:F2},{pos.y:F2}) | {predictedAtk} | (에셋 없음: {assetName}) | - | {predictedSpeed} | (에셋 없음) | - | {(inTopLeftRegion ? "예" : "-")}");
                continue;
            }

            string actualAtk = FindEffectDirection(buff, typeof(ATKBuffType));
            string actualSpeed = FindEffectDirection(buff, typeof(SpeedBuffType));

            bool atkMatch = actualAtk == null || actualAtk == predictedAtk;
            bool speedMatch = actualSpeed == null || actualSpeed == predictedSpeed;
            if (!atkMatch || !speedMatch) anyMismatch = true;

            sb.AppendLine($"{color} | ({pos.x:F2},{pos.y:F2}) | {predictedAtk} | {actualAtk ?? "없음"} | {(atkMatch ? "OK" : "**불일치**")} | {predictedSpeed} | {actualSpeed ?? "없음"} | {(speedMatch ? "OK" : "**불일치**")} | {(inTopLeftRegion ? "예 (아직 미구현 신규 효과)" : "-")}");
        }

        Debug.Log("[EmotionVectorStatComparisonReport]\n" + sb);

        if (anyMismatch)
            Debug.LogError("[EmotionVectorStatComparisonReport] 부호 불일치 발견 — 게이트 1.3 실패. spec §2.2 좌표 재검토 필요.");
        else
            Debug.Log("[EmotionVectorStatComparisonReport] 전부 부호 일치 — 게이트 1.3 통과.");
    }

    private static string SignLabel(float value)
    {
        if (Mathf.Approximately(value, 0f)) return "중립";
        return value > 0f ? "증가" : "감소";
    }

    private static string FindEffectDirection(StatBuffSO buff, System.Type buffTypeClass)
    {
        if (buff.Effects == null) return null;

        foreach (StatBuffSO.BuffEffect effect in buff.Effects)
        {
            if (effect?.BuffType == null) continue;
            if (effect.BuffType.GetType() == buffTypeClass)
                return effect.IsDebuff ? "감소" : "증가";
        }

        return null;
    }
}
#endif
