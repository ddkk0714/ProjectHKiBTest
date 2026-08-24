using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public struct ScreenTearSettings
{
    // ScreenEffectManager.ScreenTear(settings)의 데이터 계약.
    // 이벤트 체인에서는 ScreenTearAction이 이 값을 자동으로 만든다.
    // 모든 시간값은 unscaled time 기준이므로 컷신으로 timeScale이 0이어도 재생된다.
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
    public int lineCount;              // 평행한 찢김 선 수. 1~4.
    public float lineSpacing;          // 기준 선의 수직 방향 간격(캔버스 픽셀).
    public float lineAngleRandomness;  // 각 선에 적용할 기준 angle의 ± 랜덤 편차.
    public int segmentCount;           // 선 하나를 나눌 조각 수. 1이면 한 번에 베는 선.
    public float jaggedness;
    public float opening;
    public float edgeThickness;
    public int shardCount;
    public float shardSize;
    public float shardSpread;
    public int randomSeed;             // 0이면 매 재생 새 패턴, 그 외 값이면 같은 패턴을 재현.
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
    private Vector2[] _tearLineDirections = new Vector2[0];
    private Vector2[] _tearLineNormals = new Vector2[0];
    private int _tearLineCount;
    private int _segmentsPerTearLine;
    private int _activeTearSegments;
    private int _activeTearShards;
    private int _tearSeed;
    private RawImage _noiseImage;
    private Material _noiseMaterial;
    private int _noiseRequestVersion;
    private int _illustrationRequestVersion;

    // 글리치는 다른 효과들과 캔버스를 공유하지 못한다. 원본 화면(_CameraOpaqueTexture)을 읽어야 하는데
    // 그 텍스처는 카메라 렌더 루프 안에서만 묶여 있고, 메인 오버레이 캔버스는 ScreenSpaceOverlay라
    // 그 밖에서 그려지기 때문이다. 그래서 ScreenSpaceCamera 캔버스를 따로 만든다.
    // 글리치 캔버스는 이 매니저의 자식이 아니라 **별도 루트 오브젝트**다(아래 BuildGlitchOverlay 주석).
    // 그래서 직접 들고 있다가 같이 지워 줘야 한다.
    private GameObject _glitchCanvasObject;
    private Canvas _glitchCanvas;
    private RawImage _glitchImage;
    private Material _glitchMaterial;
    private int _glitchRequestVersion;

    private const string ProceduralNoiseShaderPath = "ScreenEffects/ProceduralScreenNoise";
    private const string GlitchShaderPath = "ScreenEffects/ScreenGlitch";
    // 오버레이가 다른 UI(대화창·메뉴) 위에 확실히 올라오도록. 대화 위에 암전이 덮여야 하는 연출이 있다.
    private const int OverlaySortingOrder = 32000;
    private const float DefaultNoiseTiling = 16f;
    // Addressables 씬 로딩 직후에는 한 프레임의 unscaled delta가 페이드 시간보다
    // 커질 수 있다. 그 값을 그대로 쓰면 빌드에서 페이드 인이 한 프레임에 끝나
    // 맵이 즉시 전환된 것처럼 보인다. 화면 연출은 실제 경과 시간보다 보이는
    // 프레임을 우선하므로, 한 프레임이 소비할 페이드 시간을 제한한다.
    private const float MaxFadeStepSeconds = 1f / 30f;
    // 글리치는 월드를 일그러뜨리는 층이라 UI(대화창/메뉴)보다 **아래**여야 한다. 위로 올리면
    // 대사창까지 찢겨 읽을 수 없게 된다. 메인 오버레이(암전/노이즈)는 이보다 훨씬 위에 있다.
    // 글리치 캔버스가 설 정렬 레이어. 셰이더가 읽는 _CameraSortingLayerTexture는 Renderer2D가
    // **Top 레이어까지** 그린 결과라, 그보다 뒤에 서야 완성된 화면을 읽을 수 있다.
    // Blur는 Top 바로 뒤쪽이면서 대화창·StandingCG·UI보다는 앞이라 딱 맞는 자리다 —
    // 월드는 일그러지고 그 위의 UI는 멀쩡히 읽힌다.
    //
    // 이 이름을 바꾸려면 Renderer2D.asset의 Camera Sorting Layer Texture 경계도 같이 봐야 한다.
    private const string GlitchSortingLayer = "Blur";
    private const int GlitchSortingOrder = 100;

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
        if (_noiseMaterial != null) Destroy(_noiseMaterial);
        if (_glitchMaterial != null) Destroy(_glitchMaterial);
        if (_glitchCanvasObject != null) Destroy(_glitchCanvasObject);
    }

    // ─── 사용 API 안내 ───────────────────────────────────────────
    // 이벤트 체인: ScreenFadeAction, ScreenFlashAction, ScreenNoiseAction, ScreenGlitchAction,
    //             ScreenTearAction, CameraZoomAction을 추가하고 인스펙터에서 값을 조절한다.
    // 런타임 코드: Instance.Fade / Flash / SetNoise / StopNoise / SetGlitch / StopGlitch /
    //             ScreenTear를 직접 호출한다.
    //
    // [노이즈와 글리치의 차이] 노이즈는 화면을 지지직거리는 알갱이로 **덮는다**(원본이 안 보인다).
    // 글리치는 원본 화면을 가로로 어긋내고 RGB를 갈라 **일그러뜨린다**(원본이 보인다).
    // 둘은 서로 독립이라 같이 켜도 되고, 그러면 "일그러진 화면 위에 지지직"이 된다.
    // 완료 대기: ScreenEffectEndedDecision 또는 IsPlaying을 사용한다. 모든 화면 효과는 unscaled time 기준이다.

    // ─── 공개 API ────────────────────────────────────────────────

    /// <summary>현재 색에서 target 색으로 duration 초에 걸쳐 덮는다. 암전은 target을 불투명 검정으로.</summary>
    /// <remarks>
    /// 직접 호출 API. 이벤트 체인에서는 ScreenFadeAction을 사용한다.
    /// 같은 종류의 효과를 다시 호출하면 새 요청이 기존 색상에서 이어진다.
    /// </remarks>
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
    /// GPU 절차형 TV 노이즈. 화면 해상도 기반의 미세 입자·아날로그 뭉침·수평 간섭을 합성하며,
    /// intensity는 노이즈 강도, alpha는 투명도이며 각각 0~1이다. duration 초 동안 유지된 뒤 사라진다.
    /// duration이 0 이하면 <see cref="StopNoise"/>를 부를 때까지 계속된다.
    /// </summary>
    /// <param name="tiling">
    /// 노이즈 밀도. 클수록 보조 입자가 잘아진다. 0 이하면 기본값(16).
    /// </param>
    /// <param name="alpha">최종 노이즈 투명도. 1이면 원본 화면을 완전히 가리고, 0이면 완전히 투명하다.</param>
    /// <param name="shakeStrength">노이즈와 함께 재생할 카메라 흔들림의 임펄스 강도. 0이면 흔들지 않는다.</param>
    /// <param name="shakeDuration">반복 흔들림 전체 시간(초).</param>
    /// <param name="shakeCount">shakeDuration 동안 발생할 흔들림 횟수.</param>
    // 기존 3인자 호출은 이전 구현처럼 intensity를 화면 투명도로도 사용한다.
    // 간단 호출 API. alpha는 intensity와 같은 값으로 처리하며, 카메라 흔들림은 넣지 않는다.
    // 이벤트 체인에서는 ScreenNoiseAction의 필드를 편집하는 편이 안전하다.
    public void SetNoise(float intensity, float duration, float tiling = 0f)
        => SetNoise(intensity, duration, tiling, Mathf.Clamp01(intensity), 0f, 0f, 0);

    public void SetNoise(float intensity, float duration, float tiling, float alpha)
        => SetNoise(intensity, duration, tiling, alpha, 0f, 0f, 0);

    public void SetNoise(
        float intensity,
        float duration,
        float tiling,
        float alpha,
        float shakeStrength,
        float shakeDuration,
        int shakeCount)
    {
        if (_noiseImage == null || _noiseMaterial == null)
        {
            Debug.LogError("[ScreenEffectManager] 노이즈 셰이더를 준비하지 못해 효과를 재생할 수 없습니다.");
            return;
        }

        float noiseTiling = tiling > 0f ? tiling : DefaultNoiseTiling;
        int requestVersion = ++_noiseRequestVersion;
        StartEffect(NoiseRoutine(
            Mathf.Clamp01(intensity),
            Mathf.Clamp01(alpha),
            duration,
            noiseTiling,
            requestVersion));

        if (shakeStrength > 0f && shakeDuration > 0f && shakeCount > 0)
            StartEffect(NoiseShakeRoutine(shakeStrength, shakeDuration, shakeCount, requestVersion));
    }

    public void StopNoise()
    {
        _noiseRequestVersion++;
        if (_noiseImage != null) _noiseImage.enabled = false;
    }

    /// <summary>
    /// 디지털 글리치 - 화면이 가로 띠 단위로 어긋나고 RGB 채널이 갈라진다.
    /// </summary>
    /// <param name="intensity">전체 세기(0~1). 아래 값들에 한 번 더 곱해지는 마스터 볼륨이다.</param>
    /// <param name="duration">지속 시간(초). 0 이하면 StopGlitch를 부를 때까지 계속된다.</param>
    /// <remarks>
    /// 직접 호출 API. 이벤트 체인에서는 ScreenGlitchAction을 사용한다.
    /// 노이즈(SetNoise)와는 별개 레이어라 동시에 켜도 된다.
    /// </remarks>
    public void SetGlitch(float intensity, float duration) => SetGlitch(intensity, duration, 0.06f, 0.006f);

    /// <param name="blockShift">가로로 어긋나는 폭(화면 폭 대비). 0.02면 잔글리치, 0.15면 형체가 무너진다.</param>
    /// <param name="rgbSplit">RGB 채널이 갈라지는 거리(화면 폭 대비). 0.003 정도가 자연스럽다.</param>
    public void SetGlitch(float intensity, float duration, float blockShift, float rgbSplit)
        => SetGlitch(intensity, duration, blockShift, rgbSplit, 24f, 0.35f, 0f, 0.25f, 0.01f);

    /// <param name="blockDensity">화면을 가로로 몇 겹으로 나눌지. 클수록 띠가 얇아진다.</param>
    /// <param name="blockCoverage">그중 실제로 어긋나는 비율(0~1). 1이면 전부 흔들려 죽처럼 보인다.</param>
    /// <param name="splitAngle">RGB가 갈라지는 방향(라디안). 0이면 좌우, 1.57이면 위아래.</param>
    /// <param name="scanline">주사선 세기(0~1).</param>
    /// <param name="jitter">세로 흔들림(수직 동기 어긋남) 폭.</param>
    public void SetGlitch(
        float intensity,
        float duration,
        float blockShift,
        float rgbSplit,
        float blockDensity,
        float blockCoverage,
        float splitAngle,
        float scanline,
        float jitter)
    {
        if (_glitchImage == null || _glitchMaterial == null)
        {
            Debug.LogError("[ScreenEffectManager] 글리치 셰이더를 준비하지 못해 효과를 재생할 수 없습니다.");
            return;
        }

        StartEffect(GlitchRoutine(
            Mathf.Clamp01(intensity),
            duration,
            Mathf.Max(0f, blockShift),
            Mathf.Max(0f, rgbSplit),
            Mathf.Max(1f, blockDensity),
            Mathf.Clamp01(blockCoverage),
            splitAngle,
            Mathf.Clamp01(scanline),
            Mathf.Max(0f, jitter),
            ++_glitchRequestVersion));
    }

    /// <summary>글리치를 끈다. duration을 0으로 걸어 둔 글리치는 이걸 불러야 멈춘다.</summary>
    public void StopGlitch()
    {
        // 버전을 올리면 돌고 있던 루틴이 다음 프레임에 스스로 빠져나간다(노이즈와 같은 방식).
        _glitchRequestVersion++;
        if (_glitchImage != null) _glitchImage.enabled = false;
    }

    /// <summary>
    /// 화면이 종이처럼 반으로 찢어지는 연출(EVT-006 최종 탈출).
    ///
    /// 찢어진 종이 텍스처가 없어 지금은 흰 섬광 + 암전으로 대체한다. 아트가 들어오면
    /// 이 메서드 본문만 교체하면 되고, 호출부(ScreenTearAction)는 그대로 둬도 된다.
    /// </summary>
    public void ScreenTear(float duration)
    {
        ScreenTear(
            duration, 0.25f, Color.white, new Color(0f, 0f, 0f, 1f),
            new Vector2(0.5f, 0.5f), 0f, 1800f, 18f, Color.white);
    }

    // ─── 내부 ────────────────────────────────────────────────────

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
            lineCount = 1,
            lineSpacing = 0f,
            lineAngleRandomness = 0f,
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

    /// <summary>
    /// 설정형 화면 찢김 API. 여러 선·각도 편차·파편은 ScreenTearSettings로 지정한다.
    /// 이벤트 체인에서는 ScreenTearAction이 동일한 경로를 사용한다.
    /// </summary>
    public void ScreenTear(ScreenTearSettings settings)
    {
        StartEffect(ScreenTearRoutine(settings));
    }

    /// <summary>
    /// 화면 중앙 기준의 일러스트 오버레이를 표시한다. CameraZoomAction의 선택 일러스트 설정이 이 API를 사용한다.
    /// anchor는 0~1 범위로 보정되며, hide 전까지 마지막 일러스트가 유지된다.
    /// </summary>
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

    /// <summary>현재 일러스트 오버레이를 서서히 숨긴다.</summary>
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
            // 맵 로드/GC 등으로 프레임이 잠시 멈춘 뒤에도 남은 페이드가 즉시
            // 건너뛰어지지 않게 한다. 일반적인 프레임에서는 delta가 이 값보다
            // 작으므로 기존 속도와 동일하다.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFadeStepSeconds);
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

    private IEnumerator NoiseRoutine(float intensity, float alpha, float duration, float tiling, int requestVersion)
    {
        _noiseImage.enabled = true;
        _noiseMaterial.SetFloat("_Intensity", intensity);
        _noiseMaterial.SetFloat("_Opacity", alpha);
        _noiseMaterial.SetFloat("_GrainTiling", tiling);
        _noiseMaterial.SetFloat("_Seed", Random.value * 4096f);

        float elapsed = 0f;
        while (requestVersion == _noiseRequestVersion && (duration <= 0f || elapsed < duration))
        {
            // 셰이더 시간도 unscaled time으로 넣는다. 컷신에서 게임 시간이 멈춰도 노이즈는 계속 흐른다.
            _noiseMaterial.SetFloat("_NoiseTime", Time.unscaledTime);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // 새 요청이 이미 시작됐다면 그 요청의 화면을 끄면 안 된다.
        if (requestVersion == _noiseRequestVersion)
            _noiseImage.enabled = false;
    }

    private IEnumerator GlitchRoutine(
        float intensity,
        float duration,
        float blockShift,
        float rgbSplit,
        float blockDensity,
        float blockCoverage,
        float splitAngle,
        float scanline,
        float jitter,
        int requestVersion)
    {
        // 캔버스가 어느 카메라에 붙을지는 플레이 중에야 정해진다(CameraManager가 씬마다 다시 뜬다).
        // 매 재생 시점에 다시 확인해 둔다 - 맵을 옮겨 카메라가 바뀌어도 계속 동작해야 한다.
        BindGlitchCanvasCamera();

        _glitchImage.enabled = true;
        _glitchMaterial.SetFloat("_Intensity", intensity);
        _glitchMaterial.SetFloat("_Opacity", 1f);
        _glitchMaterial.SetFloat("_BlockShift", blockShift);
        _glitchMaterial.SetFloat("_RgbSplit", rgbSplit);
        _glitchMaterial.SetFloat("_BlockDensity", blockDensity);
        _glitchMaterial.SetFloat("_BlockCoverage", blockCoverage);
        _glitchMaterial.SetFloat("_SplitAngle", splitAngle);
        _glitchMaterial.SetFloat("_Scanline", scanline);
        _glitchMaterial.SetFloat("_Jitter", jitter);
        _glitchMaterial.SetFloat("_Seed", Random.value * 4096f);

        float elapsed = 0f;
        while (requestVersion == _glitchRequestVersion && (duration <= 0f || elapsed < duration))
        {
            // 노이즈와 같은 이유로 unscaled time을 넣는다 - 컷신에서 게임이 멈춰도 글리치는 흘러야 한다.
            _glitchMaterial.SetFloat("_GlitchTime", Time.unscaledTime);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // 새 요청이 이미 시작됐다면 그 요청의 화면을 끄면 안 된다.
        if (requestVersion == _glitchRequestVersion)
            _glitchImage.enabled = false;
    }

    // 글리치 캔버스를 메인 카메라에 묶는다. 카메라를 못 찾으면 Overlay로 떨어뜨린다 -
    // 그 경우 _CameraOpaqueTexture가 묶여 있다는 보장이 없어 화면이 검게 나올 수 있으므로 알린다.
    private void BindGlitchCanvasCamera()
    {
        if (_glitchCanvas == null) return;

        Camera target = CameraManager.instance != null ? CameraManager.instance.theCamera : null;
        if (target == null) target = Camera.main;

        if (target == null)
        {
            // Overlay로 떨어뜨리면 카메라 렌더 루프 밖이라 _CameraSortingLayerTexture를 못 읽는다.
            // 화면이 새까맣게 덮이느니 아예 켜지 않는 편이 낫다.
            _glitchImage.enabled = false;
            Debug.LogWarning("[ScreenEffectManager] 글리치를 걸 카메라를 찾지 못해 연출을 건너뜁니다 " +
                             "(CameraManager.theCamera / Camera.main 둘 다 없음).");
            return;
        }

        _glitchCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        _glitchCanvas.worldCamera = target;
        // 직교 카메라라 거리가 변해도 화면을 덮는 크기는 그대로다. 다만 클립 범위 안에 있어야
        // 그려지는데, 이 프로젝트의 2D 카메라는 near clip이 **음수**(-5000)라 "near + 0.01"로 잡으면
        // 카메라 5000유닛 뒤에 놓인다. 그래서 범위 안쪽의 적당한 값으로 잡는다.
        // 앞뒤 관계는 어차피 정렬 레이어(Blur)가 정하므로 거리 자체는 연출에 영향이 없다.
        _glitchCanvas.planeDistance = Mathf.Clamp(1f, target.nearClipPlane + 0.01f, target.farClipPlane - 0.01f);
    }

    private IEnumerator NoiseShakeRoutine(float strength, float duration, int count, int requestVersion)
    {
        CameraManager camera = CameraManager.instance;
        if (!camera)
        {
            Debug.LogWarning("[ScreenEffectManager] CameraManager가 없어 노이즈 흔들림을 재생할 수 없습니다.");
            yield break;
        }

        float interval = duration / count;
        for (int i = 0; i < count && requestVersion == _noiseRequestVersion; i++)
        {
            // 매 임펄스마다 방향을 바꿔 기계적으로 한쪽만 튀는 느낌을 피한다.
            Vector2 randomDirection = Random.insideUnitCircle;
            if (randomDirection.sqrMagnitude < 0.01f)
                randomDirection = Vector2.right;

            camera.Shake(new Vector3(randomDirection.x, randomDirection.y, 0f), strength);

            float elapsed = 0f;
            while (requestVersion == _noiseRequestVersion && elapsed < interval)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
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
        // 신규 필드가 없던 기존 SerializeReference 자산도 1줄 찢김으로 안전하게 재생한다.
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
            settings.lineCount = 1;
            settings.lineSpacing = 0f;
            settings.lineAngleRandomness = 0f;
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
        settings.lineCount = Mathf.Clamp(settings.lineCount <= 0 ? 1 : settings.lineCount, 1, 4);
        settings.lineSpacing = Mathf.Max(0f, settings.lineSpacing);
        settings.lineAngleRandomness = Mathf.Clamp(settings.lineAngleRandomness, 0f, 90f);
        settings.segmentCount = Mathf.Clamp(settings.segmentCount, 1, 64);
        settings.jaggedness = Mathf.Clamp(settings.jaggedness, 0f, 0.25f);
        settings.opening = Mathf.Max(0f, settings.opening);
        settings.shardCount = Mathf.Clamp(settings.shardCount, 0, 40);
        settings.shardSize = Mathf.Max(1f, settings.shardSize);
        settings.shardSpread = Mathf.Max(0f, settings.shardSpread);
    }

    private void PrepareTearVisual(ScreenTearSettings settings)
    {
        // 풀 크기는 '선 수 × 선당 세그먼트 수'다. 기존 Image는 재사용해 연출마다 생성하지 않는다.
        _tearLineCount = settings.lineCount;
        _segmentsPerTearLine = settings.segmentCount;
        _activeTearSegments = _tearLineCount * _segmentsPerTearLine;
        _activeTearShards = settings.shardCount;
        EnsureTearImagePool(_tearGapSegments, _activeTearSegments, "TearGap");
        EnsureTearImagePool(_tearLightEdges, _activeTearSegments, "TearLightEdge");
        EnsureTearImagePool(_tearShadowEdges, _activeTearSegments, "TearShadowEdge");
        EnsureTearImagePool(_tearShards, _activeTearShards, "TearShard");

        _tearPoints = new Vector2[_tearLineCount * (_segmentsPerTearLine + 1)];
        _tearShardOrigins = new Vector2[_activeTearShards];
        _tearShardVelocities = new Vector2[_activeTearShards];
        _tearShardRotations = new float[_activeTearShards];
        _tearShardScales = new float[_activeTearShards];
        _tearLineDirections = new Vector2[_tearLineCount];
        _tearLineNormals = new Vector2[_tearLineCount];

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
        // randomSeed가 0이면 매 재생 다른 frameCount를 사용하고, 0이 아니면 재현 가능한 모양을 만든다.
        int seed = settings.randomSeed != 0 ? settings.randomSeed : Time.frameCount * 7919;
        _tearSeed = seed;

        for (int lineIndex = 0; lineIndex < _tearLineCount; lineIndex++)
        {
            float centeredLineIndex = lineIndex - (_tearLineCount - 1) * 0.5f;
            Vector2 lineCenter = center + normal * centeredLineIndex * settings.lineSpacing;
            // lineAngleRandomness는 각 선마다 독립적으로 적용된다. 파편도 아래에서 이 방향을 따른다.
            float lineAngle = settings.angle + (TearNoise01(seed, 700 + lineIndex) * 2f - 1f) * settings.lineAngleRandomness;
            float lineRadians = lineAngle * Mathf.Deg2Rad;
            Vector2 lineDirection = new Vector2(Mathf.Cos(lineRadians), Mathf.Sin(lineRadians));
            Vector2 lineNormal = new Vector2(-lineDirection.y, lineDirection.x);
            int pointOffset = lineIndex * (_segmentsPerTearLine + 1);
            _tearLineDirections[lineIndex] = lineDirection;
            _tearLineNormals[lineIndex] = lineNormal;
            for (int pointIndex = 0; pointIndex <= _segmentsPerTearLine; pointIndex++)
            {
                float t = pointIndex / (float)_segmentsPerTearLine;
                float offset = pointIndex == 0 || pointIndex == _segmentsPerTearLine
                    ? 0f
                    : (TearNoise01(seed, lineIndex * 1000 + pointIndex) * 2f - 1f) * settings.length * settings.jaggedness;
                _tearPoints[pointOffset + pointIndex] = lineCenter + lineDirection * ((t - 0.5f) * settings.length) + lineNormal * offset;
            }
        }

        for (int i = 0; i < _activeTearShards; i++)
        {
            float along = TearNoise01(seed, 100 + i);
            float side = TearNoise01(seed, 200 + i) * 2f - 1f;
            int lineIndex = i % _tearLineCount;
            int segment = Mathf.Min(_segmentsPerTearLine - 1, Mathf.FloorToInt(along * _segmentsPerTearLine));
            float segmentT = along * _segmentsPerTearLine - segment;
            int pointOffset = lineIndex * (_segmentsPerTearLine + 1);
            Vector2 origin = Vector2.Lerp(_tearPoints[pointOffset + segment], _tearPoints[pointOffset + segment + 1], segmentT);
            Vector2 spread = _tearLineNormals[lineIndex] * side * settings.shardSpread + _tearLineDirections[lineIndex] * (TearNoise01(seed, 300 + i) - 0.25f) * settings.shardSpread * 0.35f;

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

        for (int lineIndex = 0; lineIndex < _tearLineCount; lineIndex++)
        {
            // 선은 짧은 지연을 두고 순차로 베여, 다중 선이 동시에 나타나는 것보다 '촤자작' 느낌을 낸다.
            float lineDelay = _tearLineCount <= 1 ? 0f : lineIndex / (float)(_tearLineCount - 1) * 0.16f;
            int pointOffset = lineIndex * (_segmentsPerTearLine + 1);
            int imageOffset = lineIndex * _segmentsPerTearLine;
            for (int segmentIndex = 0; segmentIndex < _segmentsPerTearLine; segmentIndex++)
            {
                int imageIndex = imageOffset + segmentIndex;
                float segmentDelay = lineDelay + segmentIndex / (float)_segmentsPerTearLine * 0.46f;
                float localProgress = Mathf.Clamp01((progress - segmentDelay) / 0.36f);
                if (localProgress <= 0f)
                {
                    _tearGapSegments[imageIndex].enabled = false;
                    _tearLightEdges[imageIndex].enabled = false;
                    _tearShadowEdges[imageIndex].enabled = false;
                    continue;
                }

                Vector2 start = _tearPoints[pointOffset + segmentIndex];
                Vector2 end = Vector2.Lerp(start, _tearPoints[pointOffset + segmentIndex + 1], Mathf.SmoothStep(0f, 1f, localProgress));
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

                SetTearLine(_tearGapSegments[imageIndex], start, end, gapWidth, inner);
                SetTearLine(_tearLightEdges[imageIndex], start + normal * (gapWidth * 0.5f), end + normal * (gapWidth * 0.5f), settings.edgeThickness, light);
                SetTearLine(_tearShadowEdges[imageIndex], start - normal * (gapWidth * 0.5f), end - normal * (gapWidth * 0.5f), settings.edgeThickness, shadow);
            }
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

    private void BuildOverlay()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = OverlaySortingOrder;
        gameObject.AddComponent<CanvasScaler>();

        // 노이즈가 페이드보다 아래에 있어야 "노이즈 낀 화면이 서서히 어두워지는" 순서가 된다.
        _illustrationImage = CreateFullScreenChild<Image>("Illustration");
        _illustrationImage.preserveAspect = true;
        _illustrationImage.enabled = false;

        _noiseImage = CreateFullScreenChild<RawImage>("Noise");
        Shader noiseShader = Resources.Load<Shader>(ProceduralNoiseShaderPath);
        if (noiseShader == null)
        {
            Debug.LogError($"[ScreenEffectManager] Resources/{ProceduralNoiseShaderPath}.shader를 찾을 수 없습니다.");
        }
        else
        {
            _noiseMaterial = new Material(noiseShader) { hideFlags = HideFlags.DontSave };
            _noiseImage.material = _noiseMaterial;
            _noiseImage.texture = Texture2D.whiteTexture;
        }
        _noiseImage.enabled = false;

        _fadeImage = CreateFullScreenChild<Image>("Fade");
        _fadeImage.color = new Color(0f, 0f, 0f, 0f);

        _tearImage = CreateFullScreenChild<Image>("TearLine");
        _tearImage.enabled = false;

        BuildGlitchOverlay();
    }

    // 글리치만 별도 캔버스를 쓴다(이유는 _glitchCanvas 선언부 주석 참고).
    // 이 캔버스는 메인 오버레이보다 아래, 월드보다 위에 그려진다 - 월드는 일그러지고 UI는 멀쩡하다.
    private void BuildGlitchOverlay()
    {
        // [반드시 루트여야 한다] 이 매니저 자신이 ScreenSpaceOverlay 캔버스라, 여기에 자식으로
        // 붙이면 두 가지가 한꺼번에 망가진다.
        //   1. 자식 Canvas는 renderMode를 스스로 정하지 못하고 루트 캔버스를 따른다. 아래에서
        //      ScreenSpaceCamera로 지정해도 무시되고 Overlay로 그려져 _CameraSortingLayerTexture를
        //      못 읽는다.
        //   2. RectTransform이 화면 크기에 맞춰지지 않고 기본값(100x100)으로 남는다 — 화면 한가운데
        //      조그만 사각형 안에서만 글리치가 보이는 증상이 정확히 이것이었다.
        GameObject canvasObject = new("GlitchCanvas", typeof(RectTransform));
        DontDestroyOnLoad(canvasObject);
        _glitchCanvasObject = canvasObject;

        _glitchCanvas = canvasObject.AddComponent<Canvas>();
        _glitchCanvas.renderMode = RenderMode.ScreenSpaceOverlay; // 카메라는 재생 시점에 묶는다
        _glitchCanvas.sortingLayerName = GlitchSortingLayer;
        _glitchCanvas.sortingOrder = GlitchSortingOrder;
        canvasObject.AddComponent<CanvasScaler>();

        GameObject imageObject = new("Glitch", typeof(RectTransform));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _glitchImage = imageObject.AddComponent<RawImage>();
        _glitchImage.raycastTarget = false;

        Shader glitchShader = Resources.Load<Shader>(GlitchShaderPath);
        if (glitchShader == null)
        {
            Debug.LogError($"[ScreenEffectManager] Resources/{GlitchShaderPath}.shader를 찾을 수 없습니다.");
        }
        else
        {
            _glitchMaterial = new Material(glitchShader) { hideFlags = HideFlags.DontSave };
            _glitchImage.material = _glitchMaterial;
            _glitchImage.texture = Texture2D.whiteTexture;
        }
        _glitchImage.enabled = false;
    }

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
