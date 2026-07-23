#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

// 감정 평면 2D 플롯을 화면에 그리는 런타임 디버그 오버레이 (spec §8).
// 개발 빌드/에디터에서만 컴파일됨 — 배포 빌드에서는 파일 전체가 제외된다.
[RequireComponent(typeof(EmotionVectorModule))]
public class EmotionVectorDebugView : MonoBehaviour
{
    private const int TrailLength = 60;

    [SerializeField] private bool showView = true;
    [SerializeField] private Vector2 panelPosition = new Vector2(20, 20);
    [SerializeField] private float panelSize = 320f;

    [Header("Inject Test Stack (실제 게임 상태에 영향 없음, 미리보기 전용)")]
    [SerializeField] private EmotionColor injectColor = EmotionColor.SadnessBlue;
    [SerializeField] private int injectStack = 10;

    [Header("Smoothing (표시 전용 — 실제 계산값에는 영향 없음)")]
    [SerializeField] private float smoothTime = 0.15f;

    private EmotionVectorModule _vectorModule;
    private readonly Dictionary<EmotionColor, int> _injectedStacks = new();
    private readonly Queue<Vector2> _trail = new();

    // 화면에 그리는 값(부드럽게 따라감). 실제 계산값(ComputeVector 결과)은 매 프레임 그대로 다시 구하고,
    // 여기 있는 _display* 값만 SmoothDamp로 뒤쫓아가게 해서 "틱마다 순간이동"하는 느낌을 없앤다.
    private Vector2 _displayVector;
    private Vector2 _displayVelocity;
    private float _displayEntropy;
    private float _entropyVelocity;
    private readonly Dictionary<EmotionColor, Vector2> _displayContribution = new();
    private readonly Dictionary<EmotionColor, Vector2> _contributionVelocity = new();

    private static Texture2D _pixelTex;
    private static Texture2D PixelTex
    {
        get
        {
            if (_pixelTex == null)
            {
                _pixelTex = new Texture2D(1, 1);
                _pixelTex.SetPixel(0, 0, Color.white);
                _pixelTex.Apply();
            }
            return _pixelTex;
        }
    }

    private void Awake()
    {
        _vectorModule = GetComponent<EmotionVectorModule>();
    }

    [Button("Inject Test Stack (Preview Only)", EButtonEnableMode.Always)]
    private void InjectTestStack()
    {
        _injectedStacks.TryGetValue(injectColor, out int current);
        _injectedStacks[injectColor] = current + injectStack;
    }

    [Button("Clear Injected Stacks", EButtonEnableMode.Always)]
    private void ClearInjectedStacks()
    {
        _injectedStacks.Clear();
    }

    private int GetPreviewStack(EmotionColor color)
    {
        _injectedStacks.TryGetValue(color, out int injected);
        return _vectorModule.GetRawStack(color) + injected;
    }

    private void Update()
    {
        if (!showView || _vectorModule.Table == null) return;

        EmotionVector preview = EmotionVectorModule.ComputeVector(
            _vectorModule.Table, EmotionVectorModule.PolledColors,
            GetPreviewStack, out float entropy, out _);

        Vector2 target = new Vector2(preview.X, preview.Y);
        _displayVector = Vector2.SmoothDamp(_displayVector, target, ref _displayVelocity, smoothTime);
        _displayEntropy = Mathf.SmoothDamp(_displayEntropy, entropy, ref _entropyVelocity, smoothTime);

        for (int i = 0; i < EmotionVectorModule.PolledColors.Length; i++)
        {
            EmotionColor color = EmotionVectorModule.PolledColors[i];
            if (color == EmotionColor.VoidBlack) continue;

            Vector2 rawContribution = _vectorModule.Table.GetPosition(color) * GetPreviewStack(color);
            _displayContribution.TryGetValue(color, out Vector2 currentDisplay);
            _contributionVelocity.TryGetValue(color, out Vector2 velocity);

            Vector2 smoothed = Vector2.SmoothDamp(currentDisplay, rawContribution, ref velocity, smoothTime);
            _displayContribution[color] = smoothed;
            _contributionVelocity[color] = velocity;
        }

        _trail.Enqueue(_displayVector);
        while (_trail.Count > TrailLength)
            _trail.Dequeue();
    }

    private void OnGUI()
    {
        if (!showView || _vectorModule == null || _vectorModule.Table == null) return;

        EmotionVectorTableSO table = _vectorModule.Table;

        // 화면에 그리는 값은 전부 Update()에서 스무딩해둔 _display* — 카테고리 값(Dominant)만 순간값 사용
        EmotionVectorModule.ComputeVector(table, EmotionVectorModule.PolledColors,
            GetPreviewStack, out _, out EmotionColor dominant);

        Rect panelRect = new Rect(panelPosition.x, panelPosition.y, panelSize, panelSize + 24f);
        Rect plotRect = new Rect(panelRect.x, panelRect.y, panelSize, panelSize);
        Vector2 center = new Vector2(plotRect.x + plotRect.width * 0.5f, plotRect.y + plotRect.height * 0.5f);
        float halfSize = plotRect.width * 0.5f;

        // 자동 스케일 — 스무딩 도중 화살표가 잘리지 않도록 raw/표시값 중 큰 쪽 기준
        float maxMagnitude = Mathf.Max(1f, _displayVector.magnitude);
        for (int i = 0; i < EmotionVectorModule.PolledColors.Length; i++)
        {
            EmotionColor color = EmotionVectorModule.PolledColors[i];
            if (color == EmotionColor.VoidBlack) continue;
            int stack = GetPreviewStack(color);
            if (stack <= 0) continue;
            maxMagnitude = Mathf.Max(maxMagnitude, (table.GetPosition(color) * stack).magnitude);
            if (_displayContribution.TryGetValue(color, out Vector2 displayed))
                maxMagnitude = Mathf.Max(maxMagnitude, displayed.magnitude);
        }
        float scale = (halfSize * 0.9f) / maxMagnitude;

        Vector2 ToScreen(Vector2 planeValue) => center + new Vector2(planeValue.x, -planeValue.y) * scale;

        DrawBackground(plotRect);
        DrawQuadrants(center, halfSize);
        DrawAxes(center, halfSize);
        DrawEmotionLabels(table, ToScreen);
        DrawContributions(ToScreen, center);
        DrawTrail(ToScreen);
        DrawVectorArrow(center, ToScreen(_displayVector));
        DrawEntropyBar(plotRect, _displayEntropy);

        GUI.Label(new Rect(plotRect.x, plotRect.yMax + 2f, panelSize, 20f),
            $"V=({_displayVector.x:F1}, {_displayVector.y:F1})  |V|={_displayVector.magnitude:F1}  Entropy={_displayEntropy:F2}  Dominant={dominant}");
    }

    private static void DrawBackground(Rect rect)
    {
        DrawRect(rect, new Color(0f, 0f, 0f, 0.6f));
    }

    private static void DrawQuadrants(Vector2 center, float halfSize)
    {
        // 1사분면(우상, 긍정+각성), 2사분면(좌상, 부정+각성), 3사분면(좌하, 부정+비각성), 4사분면(우하, 긍정+비각성)
        DrawRect(new Rect(center.x, center.y - halfSize, halfSize, halfSize), new Color(0.2f, 0.6f, 0.5f, 0.25f)); // 1
        DrawRect(new Rect(center.x - halfSize, center.y - halfSize, halfSize, halfSize), new Color(0.55f, 0.2f, 0.6f, 0.25f)); // 2
        DrawRect(new Rect(center.x - halfSize, center.y, halfSize, halfSize), new Color(0.75f, 0.25f, 0.25f, 0.25f)); // 3
        DrawRect(new Rect(center.x, center.y, halfSize, halfSize), new Color(0.3f, 0.65f, 0.3f, 0.25f)); // 4
    }

    private static void DrawAxes(Vector2 center, float halfSize)
    {
        DrawLine(new Vector2(center.x - halfSize, center.y), new Vector2(center.x + halfSize, center.y), Color.white, 1f);
        DrawLine(new Vector2(center.x, center.y - halfSize), new Vector2(center.x, center.y + halfSize), Color.white, 1f);
    }

    private static void DrawEmotionLabels(EmotionVectorTableSO table, System.Func<Vector2, Vector2> toScreen)
    {
        foreach (EmotionVectorTableSO.Entry entry in table.Entries)
        {
            if (entry.isCatalyst) continue; // 공허처럼 평면에 없는 항목은 라벨 생략

            Vector2 screenPos = toScreen(entry.position);
            GUI.color = Color.white;
            GUI.Label(new Rect(screenPos.x - 20f, screenPos.y - 8f, 60f, 16f), entry.displayName);
            DrawRect(new Rect(screenPos.x - 2f, screenPos.y - 2f, 4f, 4f), Color.white);
        }
        GUI.color = Color.white;
    }

    private void DrawContributions(System.Func<Vector2, Vector2> toScreen, Vector2 center)
    {
        foreach (KeyValuePair<EmotionColor, Vector2> pair in _displayContribution)
        {
            if (pair.Value.sqrMagnitude < 0.0001f) continue; // 0으로 수렴한 건 안 그림
            DrawLine(center, toScreen(pair.Value), new Color(1f, 1f, 0f, 0.4f), 2f);
        }
    }

    private void DrawTrail(System.Func<Vector2, Vector2> toScreen)
    {
        Vector2[] points = _trail.ToArray();
        for (int i = 0; i < points.Length; i++)
        {
            float alpha = (i + 1f) / points.Length * 0.5f;
            Vector2 screenPos = toScreen(points[i]);
            DrawRect(new Rect(screenPos.x - 1.5f, screenPos.y - 1.5f, 3f, 3f), new Color(1f, 1f, 1f, alpha));
        }
    }

    private static void DrawVectorArrow(Vector2 from, Vector2 to)
    {
        DrawLine(from, to, Color.cyan, 3f);
        DrawRect(new Rect(to.x - 3f, to.y - 3f, 6f, 6f), Color.cyan);
    }

    private static void DrawEntropyBar(Rect plotRect, float entropy)
    {
        Rect barBg = new Rect(plotRect.x, plotRect.yMax + 22f, plotRect.width, 8f);
        DrawRect(barBg, new Color(1f, 1f, 1f, 0.15f));
        DrawRect(new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(entropy), barBg.height), new Color(1f, 0.8f, 0.2f, 0.8f));
    }

    private static void DrawRect(Rect rect, Color color)
    {
        Color savedColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, PixelTex);
        GUI.color = savedColor;
    }

    private static void DrawLine(Vector2 p1, Vector2 p2, Color color, float width)
    {
        Color savedColor = GUI.color;
        GUI.color = color;

        Vector2 delta = p2 - p1;
        float length = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Matrix4x4 matrixBackup = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, p1);
        GUI.DrawTexture(new Rect(p1.x, p1.y - width * 0.5f, length, width), PixelTex);
        GUI.matrix = matrixBackup;

        GUI.color = savedColor;
    }
}
#endif
