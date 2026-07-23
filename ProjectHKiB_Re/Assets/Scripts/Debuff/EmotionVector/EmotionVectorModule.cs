using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// EmotionModule을 읽기 전용으로 폴링만 한다 — 기존 코드 무수정 (spec §7.1).
// 역치 판정/버프 부여/조합 판정은 아직 하지 않는다. 순수 벡터 계산만 (Phase 1).
[RequireComponent(typeof(EmotionModule))]
public class EmotionVectorModule : MonoBehaviour
{
    public static readonly EmotionColor[] PolledColors =
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

    [SerializeField] private EmotionVectorTableSO table;
    [SerializeField] private bool showDebugLog = false;

    private EmotionModule _emotionModule;
    private readonly Dictionary<EmotionColor, int> _lastStacks = new();
    private bool _isDirty;
    private EmotionVector _current;

    [ShowNativeProperty] public Vector2 Vector => new Vector2(_current.X, _current.Y);
    [ShowNativeProperty] public float Magnitude => _current.Magnitude;
    [ShowNativeProperty] public float Entropy { get; private set; }
    [ShowNativeProperty] public EmotionColor DominantColor { get; private set; }
    [ShowNativeProperty] public int RecalculateCallCount { get; private set; } // 게이트 1.1 검증용 (Step 4 이후 제거 검토)

    public EmotionVectorTableSO Table => table;

    // 디버그 뷰의 "가짜 주입" 미리보기용 — 실제 게임 상태(EmotionModule)는 건드리지 않는다 (Step 1.2, spec §8).
    public int GetRawStack(EmotionColor color) => _emotionModule.GetStacks(color, EmotionModule.EmotionApplyTarget.Other);

    private void Awake()
    {
        _emotionModule = GetComponent<EmotionModule>();
    }

    private void Update()
    {
        for (int i = 0; i < PolledColors.Length; i++)
        {
            EmotionColor color = PolledColors[i];
            int current = _emotionModule.GetStacks(color, EmotionModule.EmotionApplyTarget.Other);

            _lastStacks.TryGetValue(color, out int last);
            if (last == current) continue;

            _lastStacks[color] = current;
            _isDirty = true;
        }
    }

    private void LateUpdate()
    {
        if (!_isDirty) return;
        Recalculate();
        _isDirty = false;
    }

    private void Recalculate()
    {
        RecalculateCallCount++;

        _current = ComputeVector(table, PolledColors,
            color => _emotionModule.GetStacks(color, EmotionModule.EmotionApplyTarget.Other),
            out float entropy, out EmotionColor dominant);

        Entropy = entropy;
        DominantColor = dominant;

        if (showDebugLog)
            Debug.Log($"[EmotionVectorModule] Recalculate #{RecalculateCallCount}: V={_current}, Magnitude={_current.Magnitude:F2}, Entropy={Entropy:F2}, Dominant={DominantColor}");
    }

    // 순수 함수로 분리 — EmotionModule/씬 없이도 공식 자체를 단위 테스트할 수 있게 함 (spec §3.1, §3.2).
    // 기존 반응 시스템이 살아있는 한 실제 씬에서는 서로 다른 두 그룹의 감정이 동시에 존재할 수 없어서
    // (즉시 반응으로 소각됨) "슬픔+행복" 같은 조합 케이스는 이 함수를 통한 코드 테스트로만 검증 가능하다.
    public static EmotionVector ComputeVector(
        EmotionVectorTableSO table,
        IReadOnlyList<EmotionColor> colors,
        Func<EmotionColor, int> stackLookup,
        out float entropy,
        out EmotionColor dominantColor)
    {
        Vector2 sum = Vector2.zero;
        float weightedMagnitudeSum = 0f;
        float bestContribution = 0f;
        EmotionColor dominant = default;
        bool hasDominant = false;

        if (table != null)
        {
            for (int i = 0; i < colors.Count; i++)
            {
                EmotionColor color = colors[i];
                if (color == EmotionColor.VoidBlack) continue; // 공허는 촉매, 합에서 제외 (spec §2.4, §3.1)

                int stack = stackLookup(color);
                if (stack <= 0) continue;

                Vector2 contribution = table.GetPosition(color) * stack;
                sum += contribution;

                float contributionMagnitude = contribution.magnitude;
                weightedMagnitudeSum += contributionMagnitude;

                if (!hasDominant || contributionMagnitude > bestContribution)
                {
                    bestContribution = contributionMagnitude;
                    dominant = color;
                    hasDominant = true;
                }
            }
        }

        EmotionVector result = new EmotionVector(sum.x, sum.y);
        entropy = weightedMagnitudeSum > 0f ? 1f - (result.Magnitude / weightedMagnitudeSum) : 0f;
        dominantColor = dominant;
        return result;
    }

    // 게이트 1.1 검증용 — 한 프레임에 여러 색이 동시에 바뀌어도 Recalculate()가 1회만 도는지 확인
    [Button("Test: Apply 3 Colors At Once", EButtonEnableMode.Playmode)]
    private void TestApplyThreeColorsAtOnce()
    {
        _emotionModule.ApplyColor(EmotionColor.SadnessBlue, 10, EmotionModule.EmotionApplyTarget.Other);
        _emotionModule.ApplyColor(EmotionColor.HappinessYellow, 10, EmotionModule.EmotionApplyTarget.Other);
        _emotionModule.ApplyColor(EmotionColor.AngerOrange, 10, EmotionModule.EmotionApplyTarget.Other);
    }
}
