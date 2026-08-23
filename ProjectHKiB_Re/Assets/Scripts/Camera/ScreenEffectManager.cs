using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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
    private Image _fadeImage;
    private RawImage _noiseImage;
    private Texture2D _noiseTexture;

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
    /// 화면이 종이처럼 반으로 찢어지는 연출(EVT-006 최종 탈출).
    ///
    /// 찢어진 종이 텍스처가 없어 지금은 흰 섬광 + 암전으로 대체한다. 아트가 들어오면
    /// 이 메서드 본문만 교체하면 되고, 호출부(ScreenTearAction)는 그대로 둬도 된다.
    /// </summary>
    public void ScreenTear(float duration)
    {
        Debug.LogWarning("[ScreenEffectManager] ScreenTear: 화면 찢김 아트가 없어 섬광+암전 더미로 대체합니다.");
        StartEffect(ScreenTearRoutine(duration));
    }

    // ─── 내부 ────────────────────────────────────────────────────

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
