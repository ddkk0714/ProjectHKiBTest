using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public struct ScreenTearSettings
{
    public float duration;
    public float flashRatio;
    public Color flashColor;
    public Color endColor;
    public Vector2 origin;
    public float angle;
    public float length;
    public float thickness;
    public Color tearColor;
    public Color innerColor;
    public Color shadowEdgeColor;
    public int segmentCount;
    public float jaggedness;
    public float opening;
    public float edgeThickness;
    public int shardCount;
    public float shardSize;
    public float shardSpread;
    public int randomSeed;
}

/// <summary>
/// 이벤트 연출용 전체 화면 효과 — 암전/페이드, 노이즈, 섬광, 화면 찢김.
///
/// [배선이 필요 없다] CodexModule과 같은 자동 생성 싱글턴이고, 오버레이 캔버스와 이미지까지
/// 런타임에 직접 만들어 쓴다. 씬이나 프리팹에 아무것도 얹지 않아도 첫 호출에 알아서 뜬다.
///
/// [아트 리소스가 없다] 여기 있는 효과는 전부 단색 이미지와 절차 생성 텍스처만 쓴다.
/// 기획서가 요구하는 것 중 실제 그림이 있어야 하는 것(눈가 2D 일러스트 클로즈업, 종이가 찢기는
/// 연출)은 <see cref="ScreenTear"/>처럼 "가장 가까운 무アート 대체 연출 + 경고 로그"로 처리해 두었다.
/// 리소스가 들어오면 그 자리만 갈아끼우면 된다.
///
/// [시간] 컷신 도중 TimeManager가 게임을 멈춰도 연출은 흘러야 하므로 전부 unscaled time을 쓴다.
/// </summary>
public class ScreenEffectManager : MonoBehaviour
{
    private static ScreenEffectManager _instance;
    private static bool _isQuitting;

    public static ScreenEffectManager Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying && !_isQuitting)
            {
                _instance = FindObjectOfType<ScreenEffectManager>();
                if (_instance == null)
                    _instance = new GameObject(nameof(ScreenEffectManager)).AddComponent<ScreenEffectManager>();
            }
            return _instance;
        }
    }

    // 돌고 있는 효과 수. 0이면 화면 연출이 전부 끝난 상태 — ScreenEffectEndedDecision이 이걸 본다.
    private int _running;
    public bool IsPlaying => _running > 0;

    private Canvas _canvas;
    private Image _illustrationImage;
    private Image _fadeImage;
    private Image _tearImage;
    private readonly List<Image> _tearGapSegments = new();
    private readonly List<Image> _tearLightEdges = new();
    private readonly List<Image> _tearShadowEdges = new();
    private readonly List<Image> _tearShards = new();
    private Vector2[] _tearPoints = new Vector2[0];
    private Vector2[] _tearShardOrigins = new Vector2[0];
    private Vector2[] _tearShardVelocities = new Vector2[0];
    private float[] _tearShardRotations = new float[0];
    private float[] _tearShardScales = new float[0];
    private int _activeTearSegments;
    private int _activeTearShards;
    private int _tearSeed;
    private RawImage _noiseImage;
    private Texture2D _noiseTexture;
    private int _illustrationRequestVersion;

    private const int NoiseTextureSize = 64;
    // 오버레이가 다른 UI(대화창·메뉴) 위에 확실히 올라오도록. 대화 위에 암전이 덮여야 하는 연출이 있다.
    private const int OverlaySortingOrder = 32000;
    private const float DefaultNoiseTiling = 16f;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        BuildOverlay();
    }

    private void OnApplicationQuit() => _isQuitting = true;

    private void OnDestroy()
    {
        if (_noiseTexture != null) Destroy(_noiseTexture);
    }

    // ─── 공개 API ────────────────────────────────────────────────

    /// <summary>현재 색에서 target 색으로 duration 초에 걸쳐 덮는다. 암전은 target을 불투명 검정으로.</summary>
    public void Fade(Color target, float duration)
    {
        StartEffect(FadeRoutine(target, duration));
    }

    /// <summary>완전 암전. duration 0이면 즉시.</summary>
    public void FadeToBlack(float duration) => Fade(new Color(0f, 0f, 0f, 1f), duration);

    /// <summary>암전 해제(화면 복귀).</summary>
    public void FadeFromBlack(float duration) => Fade(new Color(0f, 0f, 0f, 0f), duration);

    /// <summary>색을 확 덮었다가 되돌린다(피격·충격 순간용).</summary>
    public void Flash(Color color, float duration)
    {
        StartEffect(FlashRoutine(color, duration));
    }

    /// <summary>
    /// TV 노이즈. intensity는 0~1(오버레이 알파), duration 초 동안 유지된 뒤 사라진다.
    /// duration이 0 이하면 <see cref="StopNoise"/>를 부를 때까지 계속된다.
    /// </summary>
    /// <param name="tiling">
    /// 화면을 몇 칸으로 잘라 노이즈를 반복할지. 클수록 알갱이가 잘아진다. 0 이하면 기본값(16).
    /// 예전엔 이 값이 코드에 박혀 있어 연출마다 거칠기를 못 바꿨다.
    /// </param>
    public void SetNoise(float intensity, float duration, float tiling = 0f)
    {
        if (_noiseImage != null && tiling > 0f)
            _noiseImage.uvRect = new Rect(0f, 0f, tiling, tiling);

        StartEffect(NoiseRoutine(Mathf.Clamp01(intensity), duration));
    }

    public void StopNoise()
    {
        if (_noiseImage != null) _noiseImage.enabled = false;
    }

    /// <summary>
    /// ?붾㈃??醫낆씠泥섎읆 諛섏쑝濡?李?뼱吏???곗텧(EVT-006 理쒖쥌 ?덉텧).
    ///
    /// 李?뼱吏?醫낆씠 ?띿뒪泥섍? ?놁뼱 吏湲덉? ???ш킅 + ?붿쟾?쇰줈 ?泥댄븳?? ?꾪듃媛 ?ㅼ뼱?ㅻ㈃
    /// ??硫붿꽌??蹂몃Ц留?援먯껜?섎㈃ ?섍퀬, ?몄텧遺(ScreenTearAction)??洹몃?濡??щ룄 ?쒕떎.
    /// </summary>
    public void ScreenTear(float duration)
    {
        ScreenTear(
            duration, 0.25f, Color.white, new Color(0f, 0f, 0f, 1f),
            new Vector2(0.5f, 0.5f), 0f, 1800f, 18f, Color.white);
    }

    // ??? ?대? ????????????????????????????????????????????????????

    public void ScreenTear(
        float duration,
        float flashRatio,
        Color flashColor,
        Color endColor,
        Vector2 origin,
        float angle,
        float length,
        float thickness,
        Color tearColor)
    {
        ScreenTear(new ScreenTearSettings
        {
            duration = duration,
            flashRatio = flashRatio,
            flashColor = flashColor,
            endColor = endColor,
            origin = origin,
            angle = angle,
            length = length,
            thickness = thickness,
            tearColor = tearColor,
            innerColor = new Color(0.02f, 0.01f, 0.04f, 0.95f),
            shadowEdgeColor = new Color(0.18f, 0.03f, 0.08f, 0.9f),
            segmentCount = 10,
            jaggedness = 0.06f,
            opening = 56f,
            edgeThickness = 5f,
            shardCount = 14,
            shardSize = 34f,
            shardSpread = 260f,
            randomSeed = 0,
        });
    }

    public void ScreenTear(ScreenTearSettings settings)
    {
        StartEffect(ScreenTearRoutine(settings));
    }

    public void ShowIllustration(
        Sprite illustration,
        Color color,
        Vector2 anchor,
        Vector2 size,
        bool preserveAspect,
        float fadeTime)
    {
        if (_illustrationImage == null || illustration == null) return;

        anchor.x = Mathf.Clamp01(anchor.x);
        anchor.y = Mathf.Clamp01(anchor.y);

        RectTransform rect = (RectTransform)_illustrationImage.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));

        Color from = _illustrationImage.enabled ? _illustrationImage.color : new Color(color.r, color.g, color.b, 0f);
        _illustrationImage.sprite = illustration;
        _illustrationImage.preserveAspect = preserveAspect;
        _illustrationImage.enabled = true;

        int requestVersion = ++_illustrationRequestVersion;
        StartEffect(FadeIllustrationRoutine(from, color, fadeTime, requestVersion, false));
    }

    public void HideIllustration(float fadeTime)
    {
        if (_illustrationImage == null || !_illustrationImage.enabled) return;

        int requestVersion = ++_illustrationRequestVersion;
        Color from = _illustrationImage.color;
        Color target = new Color(from.r, from.g, from.b, 0f);
        StartEffect(FadeIllustrationRoutine(from, target, fadeTime, requestVersion, true));
    }

    private void StartEffect(IEnumerator routine)
    {
        if (!isActiveAndEnabled) return;
        StartCoroutine(TrackEffect(routine));
    }

    private IEnumerator TrackEffect(IEnumerator routine)
    {
        _running++;
        yield return StartCoroutine(routine);
        _running--;
    }

    private IEnumerator FadeRoutine(Color target, float duration)
    {
        Color from = _fadeImage.color;
        if (duration <= 0f)
        {
            _fadeImage.color = target;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _fadeImage.color = Color.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        _fadeImage.color = target;
    }

    private IEnumerator FlashRoutine(Color color, float duration)
    {
        Color original = _fadeImage.color;
        _fadeImage.color = color;

        float half = Mathf.Max(duration, 0f) * 0.5f;
        yield return new WaitForSecondsRealtime(half);
        yield return FadeRoutine(original, half);
    }

    private IEnumerator NoiseRoutine(float intensity, float duration)
    {
        _noiseImage.enabled = true;
        _noiseImage.color = new Color(1f, 1f, 1f, intensity);

        float elapsed = 0f;
        while (duration <= 0f || elapsed < duration)
        {
            RegenerateNoise();
            elapsed += Time.unscaledDeltaTime;
            yield return null;

            // duration이 0 이하인 "수동 정지" 모드에서 StopNoise가 불리면 여기서 빠져나온다.
            if (duration <= 0f && !_noiseImage.enabled) yield break;
        }

        _noiseImage.enabled = false;
    }

    private IEnumerator ScreenTearRoutine(float duration)
    {
        float slice = Mathf.Max(duration, 0.01f) * 0.5f;
        yield return FlashRoutine(Color.white, slice);
        yield return FadeRoutine(new Color(0f, 0f, 0f, 1f), slice);
    }

    private IEnumerator FadeIllustrationRoutine(Color from, Color target, float duration, int requestVersion, bool disableWhenDone)
    {
        if (_illustrationImage == null) yield break;

        if (duration <= 0f)
        {
            if (requestVersion != _illustrationRequestVersion) yield break;
            _illustrationImage.color = target;
            if (disableWhenDone) _illustrationImage.enabled = false;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && requestVersion == _illustrationRequestVersion)
        {
            elapsed += Time.unscaledDeltaTime;
            _illustrationImage.color = Color.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (requestVersion != _illustrationRequestVersion) yield break;
        _illustrationImage.color = target;
        if (disableWhenDone) _illustrationImage.enabled = false;
    }

    private IEnumerator ScreenTearRoutine(ScreenTearSettings settings)
    {
        if (_tearImage == null || _fadeImage == null) yield break;

        NormalizeTearSettings(ref settings);
        PrepareTearVisual(settings);

        Color initialFade = _fadeImage.color;
        if (settings.duration <= 0f)
        {
            UpdateTearVisual(settings, 1f);
            _fadeImage.color = settings.endColor;
            HideTearVisual();
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < settings.duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / settings.duration);
            UpdateTearVisual(settings, progress);

            if (settings.flashRatio > 0f && progress < settings.flashRatio)
            {
                float flashProgress = progress / settings.flashRatio;
                _fadeImage.color = Color.Lerp(settings.flashColor, initialFade, flashProgress);
            }
            else
            {
                float fadeProgress = settings.flashRatio >= 1f
                    ? 1f
                    : (progress - settings.flashRatio) / (1f - settings.flashRatio);
                _fadeImage.color = Color.Lerp(initialFade, settings.endColor, Mathf.Clamp01(fadeProgress));
            }

            yield return null;
        }

        _fadeImage.color = settings.endColor;
        HideTearVisual();
    }

    private static void NormalizeTearSettings(ref ScreenTearSettings settings)
    {
        bool isLegacyDefault = settings.segmentCount == 0 && settings.opening <= 0f &&
                               settings.edgeThickness <= 0f && settings.shardCount == 0 &&
                               settings.innerColor.a <= 0f && settings.shadowEdgeColor.a <= 0f;
        if (isLegacyDefault)
        {
            if (settings.flashColor.a <= 0f) settings.flashColor = Color.white;
            if (settings.endColor.a <= 0f) settings.endColor = new Color(0f, 0f, 0f, 1f);
            if (settings.tearColor.a <= 0f) settings.tearColor = Color.white;
            settings.innerColor = new Color(0.02f, 0.01f, 0.04f, 0.95f);
            settings.shadowEdgeColor = new Color(0.18f, 0.03f, 0.08f, 0.9f);
            settings.segmentCount = 10;
            settings.jaggedness = 0.06f;
            settings.opening = 56f;
            settings.edgeThickness = 5f;
            settings.shardCount = 14;
            settings.shardSize = 34f;
            settings.shardSpread = 260f;
        }

        settings.duration = Mathf.Max(0f, settings.duration);
        settings.flashRatio = Mathf.Clamp01(settings.flashRatio);
        settings.origin.x = Mathf.Clamp01(settings.origin.x);
        settings.origin.y = Mathf.Clamp01(settings.origin.y);
        settings.length = Mathf.Max(1f, settings.length);
        settings.thickness = Mathf.Max(1f, settings.thickness);
        settings.edgeThickness = Mathf.Max(1f, settings.edgeThickness);
        settings.segmentCount = Mathf.Clamp(settings.segmentCount, 2, 24);
        settings.jaggedness = Mathf.Clamp(settings.jaggedness, 0f, 0.25f);
        settings.opening = Mathf.Max(0f, settings.opening);
        settings.shardCount = Mathf.Clamp(settings.shardCount, 0, 40);
        settings.shardSize = Mathf.Max(1f, settings.shardSize);
        settings.shardSpread = Mathf.Max(0f, settings.shardSpread);
    }

    private void PrepareTearVisual(ScreenTearSettings settings)
    {
        _activeTearSegments = settings.segmentCount;
        _activeTearShards = settings.shardCount;
        EnsureTearImagePool(_tearGapSegments, _activeTearSegments, "TearGap");
        EnsureTearImagePool(_tearLightEdges, _activeTearSegments, "TearLightEdge");
        EnsureTearImagePool(_tearShadowEdges, _activeTearSegments, "TearShadowEdge");
        EnsureTearImagePool(_tearShards, _activeTearShards, "TearShard");

        _tearPoints = new Vector2[_activeTearSegments + 1];
        _tearShardOrigins = new Vector2[_activeTearShards];
        _tearShardVelocities = new Vector2[_activeTearShards];
        _tearShardRotations = new float[_activeTearShards];
        _tearShardScales = new float[_activeTearShards];

        RectTransform rect = (RectTransform)_tearImage.transform;
        Vector2 canvasSize = rect.rect.size;
        if (canvasSize.x <= 0f || canvasSize.y <= 0f)
            canvasSize = new Vector2(Screen.width, Screen.height);

        float radians = settings.angle * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 normal = new Vector2(-direction.y, direction.x);
        Vector2 center = new Vector2(
            (settings.origin.x - 0.5f) * canvasSize.x,
            (settings.origin.y - 0.5f) * canvasSize.y);
        int seed = settings.randomSeed != 0 ? settings.randomSeed : Time.frameCount * 7919;
        _tearSeed = seed;

        for (int i = 0; i <= _activeTearSegments; i++)
        {
            float t = i / (float)_activeTearSegments;
            float offset = i == 0 || i == _activeTearSegments
                ? 0f
                : (TearNoise01(seed, i) * 2f - 1f) * settings.length * settings.jaggedness;
            _tearPoints[i] = center + direction * ((t - 0.5f) * settings.length) + normal * offset;
        }

        for (int i = 0; i < _activeTearShards; i++)
        {
            float along = TearNoise01(seed, 100 + i);
            float side = TearNoise01(seed, 200 + i) * 2f - 1f;
            int segment = Mathf.Min(_activeTearSegments - 1, Mathf.FloorToInt(along * _activeTearSegments));
            float segmentT = along * _activeTearSegments - segment;
            Vector2 origin = Vector2.Lerp(_tearPoints[segment], _tearPoints[segment + 1], segmentT);
            Vector2 spread = normal * side * settings.shardSpread + direction * (TearNoise01(seed, 300 + i) - 0.25f) * settings.shardSpread * 0.35f;

            _tearShardOrigins[i] = origin;
            _tearShardVelocities[i] = spread;
            _tearShardRotations[i] = settings.angle + (TearNoise01(seed, 400 + i) * 2f - 1f) * 85f;
            _tearShardScales[i] = Mathf.Lerp(0.55f, 1.25f, TearNoise01(seed, 500 + i));
        }

        HideTearVisual();
    }

    private void UpdateTearVisual(ScreenTearSettings settings, float progress)
    {
        float fadeOut = progress < 0.88f ? 1f : 1f - (progress - 0.88f) / 0.12f;
        float opening = settings.opening * Mathf.SmoothStep(0f, 1f, progress);

        for (int i = 0; i < _activeTearSegments; i++)
        {
            float segmentDelay = i / (float)_activeTearSegments * 0.58f;
            float localProgress = Mathf.Clamp01((progress - segmentDelay) / 0.42f);
            if (localProgress <= 0f)
            {
                _tearGapSegments[i].enabled = false;
                _tearLightEdges[i].enabled = false;
                _tearShadowEdges[i].enabled = false;
                continue;
            }

            Vector2 start = _tearPoints[i];
            Vector2 end = Vector2.Lerp(start, _tearPoints[i + 1], Mathf.SmoothStep(0f, 1f, localProgress));
            Vector2 line = end - start;
            if (line.sqrMagnitude < 0.01f) continue;

            Vector2 normal = new Vector2(-line.y, line.x).normalized;
            float localOpening = opening * localProgress;
            float gapWidth = settings.thickness + localOpening;
            Color inner = settings.innerColor;
            inner.a *= fadeOut;
            Color light = settings.tearColor;
            light.a *= fadeOut;
            Color shadow = settings.shadowEdgeColor;
            shadow.a *= fadeOut;

            SetTearLine(_tearGapSegments[i], start, end, gapWidth, inner);
            SetTearLine(_tearLightEdges[i], start + normal * (gapWidth * 0.5f), end + normal * (gapWidth * 0.5f), settings.edgeThickness, light);
            SetTearLine(_tearShadowEdges[i], start - normal * (gapWidth * 0.5f), end - normal * (gapWidth * 0.5f), settings.edgeThickness, shadow);
        }

        for (int i = 0; i < _activeTearShards; i++)
        {
            float start = 0.16f + TearNoise01(_tearSeed, 600 + i) * 0.32f;
            float localProgress = Mathf.Clamp01((progress - start) / Mathf.Max(0.01f, 1f - start));
            if (localProgress <= 0f)
            {
                _tearShards[i].enabled = false;
                continue;
            }

            float ease = 1f - Mathf.Pow(1f - localProgress, 2f);
            Vector2 position = _tearShardOrigins[i] + _tearShardVelocities[i] * ease;
            float size = settings.shardSize * _tearShardScales[i] * Mathf.Lerp(1f, 0.3f, localProgress);
            Color color = Color.Lerp(settings.tearColor, settings.shadowEdgeColor, 0.45f);
            color.a *= (1f - localProgress) * fadeOut;
            SetTearShard(_tearShards[i], position, size, _tearShardRotations[i] + localProgress * 120f, color);
        }
    }

    private void EnsureTearImagePool(List<Image> pool, int count, string prefix)
    {
        while (pool.Count < count)
        {
            GameObject go = new GameObject($"{prefix}_{pool.Count}", typeof(RectTransform));
            go.transform.SetParent(_tearImage.transform, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.enabled = false;
            pool.Add(image);
        }
    }

    private static void SetTearLine(Image image, Vector2 start, Vector2 end, float width, Color color)
    {
        RectTransform rect = (RectTransform)image.transform;
        Vector2 line = end - start;
        rect.anchoredPosition = (start + end) * 0.5f;
        rect.sizeDelta = new Vector2(line.magnitude, Mathf.Max(1f, width));
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg);
        image.color = color;
        image.enabled = color.a > 0f;
    }

    private static void SetTearShard(Image image, Vector2 position, float size, float rotation, Color color)
    {
        RectTransform rect = (RectTransform)image.transform;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(size, Mathf.Max(1f, size * 0.28f));
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        image.color = color;
        image.enabled = color.a > 0f;
    }

    private void HideTearVisual()
    {
        for (int i = 0; i < _tearGapSegments.Count; i++) _tearGapSegments[i].enabled = false;
        for (int i = 0; i < _tearLightEdges.Count; i++) _tearLightEdges[i].enabled = false;
        for (int i = 0; i < _tearShadowEdges.Count; i++) _tearShadowEdges[i].enabled = false;
        for (int i = 0; i < _tearShards.Count; i++) _tearShards[i].enabled = false;
    }

    private static float TearNoise01(int seed, int index)
    {
        float value = Mathf.Sin((seed * 0.017f + index * 12.9898f) * 78.233f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }

    private void RegenerateNoise()
    {
        Color32[] pixels = new Color32[NoiseTextureSize * NoiseTextureSize];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)Random.Range(0, 256);
            pixels[i] = new Color32(v, v, v, 255);
        }
        _noiseTexture.SetPixels32(pixels);
        _noiseTexture.Apply(false);
    }

    private void BuildOverlay()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = OverlaySortingOrder;
        gameObject.AddComponent<CanvasScaler>();

        _illustrationImage = CreateFullScreenChild<Image>("Illustration");
        _illustrationImage.preserveAspect = true;
        _illustrationImage.enabled = false;
        // 노이즈가 페이드보다 아래에 있어야 "노이즈 낀 화면이 서서히 어두워지는" 순서가 된다.
        _noiseImage = CreateFullScreenChild<RawImage>("Noise");
        _noiseTexture = new Texture2D(NoiseTextureSize, NoiseTextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };
        _noiseImage.texture = _noiseTexture;
        // 화면을 잘게 채워야 TV 노이즈처럼 보인다 — 텍스처를 그대로 늘리면 뭉개진 얼룩이 된다.
        // 연출별로 바꾸고 싶으면 SetNoise의 tiling 인자를 준다(ScreenNoiseAction에 노출돼 있다).
        _noiseImage.uvRect = new Rect(0f, 0f, DefaultNoiseTiling, DefaultNoiseTiling);
        _noiseImage.enabled = false;

        _fadeImage = CreateFullScreenChild<Image>("Fade");
        _fadeImage.color = new Color(0f, 0f, 0f, 0f);

        _tearImage = CreateFullScreenChild<Image>("TearLine");
        _tearImage.enabled = false;    }

    private T CreateFullScreenChild<T>(string name) where T : Graphic
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        T graphic = go.AddComponent<T>();
        graphic.raycastTarget = false; // 오버레이가 UI 클릭을 먹지 않도록
        return graphic;
    }
}
