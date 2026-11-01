using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

public enum FlashlightMode
{
    [Tooltip("바라보는 방향으로 부채꼴을 비춘다.")] Facing,
    [Tooltip("방향 없이 플레이어 주위를 원으로 밝힌다(랜턴·촛불).")] Around,
}

// 거리 단위는 전부 월드 유닛(타일 = 1).
[Serializable]
public class DarknessSettings
{
    [Tooltip("어둠의 짙기. 0이면 평소 밝기, 1이면 전역 조명이 완전히 꺼진다.")]
    [Range(0f, 1f)] public float darkness = 0.92f;
    [Tooltip("어둠의 색조. 밝기는 무시하고 색만 쓴다.")]
    public Color color = new(0.02f, 0.02f, 0.06f, 1f);

    [Header("플레이어 주변")]
    [Tooltip("손전등과 별개로 은은하게 보이는 반경(월드 유닛). 0이면 끈다.")]
    [Min(0f)] public float playerLightRadius = 2.5f;
    [Tooltip("주변 빛 세기. 1이면 평소 밝기, 0.1~0.2면 발밑만 겨우 보인다.")]
    [Range(0f, 1f)] public float playerLightStrength = 0.15f;
    [Tooltip("가장자리 부드러움. 1이면 중심부터 서서히 어두워진다.")]
    [Range(0f, 1f)] public float softness = 1f;
    [Tooltip("주변 빛이 나오는 높이(발밑 기준). 낮을수록 발밑이 밝고 머리는 어둡다.")]
    [Min(0f)] public float playerLightHeight = 1f;
    [Tooltip("주변 빛이 플레이어 몸을 덜 비추는 정도. 0보다 크면 벽도 주변 빛을 약하게 가린다.")]
    [Range(0f, 1f)] public float playerSelfShadow;

    [Header("손전등")]
    [Tooltip("손전등을 켠 채로 시작한다. 게임 중에는 F키(손전등 액션)로 켜고 끈다.")]
    public bool flashlight;
    [Tooltip("Facing: 바라보는 방향 부채꼴 / Around: 주위 원형.")]
    public FlashlightMode flashlightMode = FlashlightMode.Around;
    [Tooltip("손전등 밝기. 1이면 빛 안이 평소 밝기.")]
    [Range(0f, 1f)] public float flashlightBrightness = 1f;
    [Tooltip("손전등 빛 색상.")]
    public Color flashlightColor = new(1f, 0.82f, 0.62f, 1f);
    [Tooltip("점프해서 떠 있는 동안 손전등이 어두워지는 비율. 착지하면 돌아온다. 0이면 끈다.")]
    [Range(0f, 1f)] public float flashlightJumpDim = 0.5f;
    [Tooltip("빛이 닿는 거리(월드 유닛). Around일 때는 원의 반지름.")]
    [Min(0f)] public float flashlightRange = 6f;
    [Tooltip("부채꼴 전체 각도(도). Facing일 때만 쓴다.")]
    [Range(1f, 179f)] public float flashlightAngle = 50f;
    [Tooltip("부채꼴 양 끝으로 갈수록 어두워지는 정도. 0이면 각도 끝에서 딱 끊긴다.")]
    [Range(0f, 1f)] public float flashlightAngleFalloff = 0.6f;
    [Tooltip("멀어질수록 어두워지는 정도. 0이면 사거리 끝까지 같은 밝기.")]
    [Range(0f, 1f)] public float flashlightDistanceFalloff = 0.6f;
    [Tooltip("어두워지는 곡선. 높을수록 가운데·가까운 곳에 빛이 몰린다.")]
    [Range(0f, 1f)] public float flashlightFalloffCurve = 0.5f;
    [Tooltip("빛 시작 부분의 둥근 반지름(월드 유닛). 0이면 꼭짓점이 뾰족하다. Facing 전용.")]
    [Min(0f)] public float flashlightStartRoundness = 0.35f;
    [Tooltip("랜턴 불꽃처럼 느리게 일렁이는 세기. 1이면 밝기가 최대 절반까지 내려간다. 0이면 안정.")]
    [Range(0f, 1f)] public float flicker;
    [Tooltip("광원을 바라보는 방향으로 띄우는 거리(월드 유닛). 벽 그림자 판 앞에서 멈춘다. Facing 전용.")]
    [Min(0f)] public float flashlightOffset = 0.6f;

    [Header("손전등 그림자")]
    [Tooltip("벽이 손전등 빛을 가린다. 벽 모양은 WallShadowCaster의 그림자 판(MapDarkness가 맵 벽에 자동으로 단다)을 쓴다.")]
    public bool flashlightShadows = true;
    [Tooltip("그림자 진하기. 1이면 완전히 어둡다.")]
    [Range(0f, 1f)] public float flashlightShadowIntensity = 0.9f;
    [Tooltip("켜면 셰이더로 그림자 경계를 흐린다. 끄면 URP 그림자(경계가 또렷함). 아래 값들은 켰을 때만 쓴다.")]
    public bool flashlightShaderShadows = true;
    [Tooltip("그림자 경계를 흐리는 폭(월드 유닛). 0이면 블러 없음.")]
    [Min(0f)] public float flashlightShadowSoftness = 0.6f;
    [Tooltip("벽에 붙은 선명한 그림자가 벽 뒤로 이 거리(월드 유닛)만큼 가면서 블러로 바뀐다.")]
    [Min(0.01f)] public float flashlightShadowSpreadDistance = 1.5f;
    [Tooltip("그림자를 벽 안쪽으로 밀어 넣는 폭. 벽과 그림자 사이 미세한 틈을 덮는다.")]
    [Min(0f)] public float flashlightShadowInset = 0f;
    [Tooltip("그림자 판정을 월드 픽셀 격자(스프라이트 PPU)에 맞춰 이동 중 경계가 미끄러지지 않게 한다. 0이면 끈다.")]
    [Min(0f)] public float flashlightShadowPixelsPerUnit = 16f;
    [Tooltip("벽 앞면이 어두워지는 거리(월드 유닛). 광원이 벽 밑선 앞(아래)이면 다 밝고, 밑선을 넘어 이만큼 위로 가면 꺼진다.")]
    [Min(0.01f)] public float flashlightWallFaceFade = 1f;

    [Header("부드러움")]
    [Tooltip("손전등이 플레이어를 따라가는 빠르기(1/초). 물리 틱 사이를 메워 그림자가 뚝뚝 끊기지 않게 한다. 0이면 보간 안 함.")]
    [Min(0f)] public float flashlightFollowSharpness = 40f;

    public DarknessSettings Clone() => (DarknessSettings)MemberwiseClone();
}

/// <summary>
/// URP 2D 라이팅으로 암전과 손전등을 만든다. 어둠은 Global Light2D를 낮추고, 주변 빛은 Player의 Point Light2D를 빌리고,
/// 손전등은 그것을 복제한 Sprite Light2D다. 벽 가림은 1D 그림자 맵 + FlashlightOcclusion 셰이더(URP 그림자는 경계를 못 흐려서).
/// </summary>
public class DarknessManager : MonoBehaviour
{
    // 종료 중(GameManager가 사라진 뒤)에는 null.
    public static DarknessManager Instance => GameManager.instance ? GameManager.instance.darknessManager : null;

    private const float FlashlightTurnSpeed = 720f;
    // 이보다 멀리 한 번에 움직이면(순간이동) 보간 없이 바로 옮긴다.
    private const float FlashlightSnapDistance = 1.5f;
    // 광원과 벽(그림자 판) 사이 틈. 0이면 광원이 판 경계에 걸려 빛이 샌다.
    private const float WallGap = 0.1f;
    // 점프 밝기 변화 속도(1/초). 이륙하면 줄고 착지하면 돌아온다.
    private const float JumpDimSpeed = 6f;

    // _current = 지금 값(전환 중엔 중간값), _target = 전환 끝 값.
    private DarknessSettings _current = new() { darkness = 0f };
    private DarknessSettings _target = new() { darkness = 0f };
    private float _flashOn;          // 켜짐 정도 0~1(켜고 끌 때 보간)
    private float _flashAngle = -90f;
    private bool _flashAngleInitialized;
    private Vector2 _flashPivot;
    private bool _flashPivotInitialized;
    private Coroutine _transition;
    private UnityEngine.Object _owner;

    [Header("손전등 켜고 끄는 소리")]
    [SerializeField] private EffectAudioCue _flashlightOnAudio = new();
    [SerializeField] private EffectAudioCue _flashlightOffAudio = new();

    private Light2D _globalLight;
    private float _globalBaseIntensity = 1f;
    private Color _globalBaseColor = Color.white;
    private float _nextGlobalLightSearch;

    private Player _trackedPlayer;
    private IDirAnimatable _facing;
    private IPhysics _physics;
    private float _jumpDim;          // 떠 있는 정도 0~1(보간)
    private Light2D _ambientLight;
    private LightSnapshot _ambientOriginal;
    private Light2D _flashLight;
    private Texture2D _beamBase;     // 가림 없는 원본 빛 모양
    private Texture2D _beamTexture;  // 조명이 쓰는 그림(원본 × 가림, 매 프레임 GPU 복사)
    private Sprite _beamSprite;
    private BeamShape _beamShape;
    private Vector4 _beamBounds;     // 로컬 영역: xMin, yMin, 폭, 높이
    // 빛 그림 텍셀이 화면 픽셀보다 커지면 광원이 움직일 때 그림자 경계가 일렁인다.
    private const int BeamTextureHeight = 512;

    private const string OcclusionShaderPath = "ScreenEffects/FlashlightOcclusion";
    // 각도 칸(0.18°). 굵으면 일직선 벽 경계가 계단지고 움직일 때 미끄러진다.
    private const int OcclusionBins = 2048;
    private Material _occlusionMaterial;
    private RenderTexture _beamTarget;
    private Texture2D _occlusionMap;
    // 가림 맵 줄 구성은 FlashlightOcclusionJob.Rows 참고.
    private const int OcclusionRows = FlashlightOcclusionJob.Rows;
    private bool _beamOccluded;
    private bool _occlusionUnavailable;
    private static readonly int BoundsId = Shader.PropertyToID("_Bounds");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int ShadowStrengthId = Shader.PropertyToID("_ShadowStrength");
    private static readonly int InsetId = Shader.PropertyToID("_Inset");
    private static readonly int SpreadDistanceId = Shader.PropertyToID("_SpreadDistance");
    private static readonly int RotationId = Shader.PropertyToID("_Rotation");
    private static readonly int OcclusionMapId = Shader.PropertyToID("_OcclusionMap");
    private static readonly int OriginId = Shader.PropertyToID("_Origin");
    private static readonly int PixelsPerUnitId = Shader.PropertyToID("_PixelsPerUnit");
    private static readonly int WallMapId = Shader.PropertyToID("_WallMap");
    private static readonly int WallMapRectId = Shader.PropertyToID("_WallMapRect");
    private static readonly int WallFaceFadeId = Shader.PropertyToID("_WallFaceFade");
    private readonly List<SpriteRenderer> _playerSprites = new();
    private readonly List<SpriteRenderer> _shadowProxies = new();
    private GameObject _shadowProxyRoot;

    public bool IsActive => _current.darkness > 0.001f;
    public bool FlashlightOn => _target.flashlight;
    /// <summary>지금 걸린 암전을 마지막으로 건 쪽(MapDarkness 등). 맵이 내려갈 때 자기 것만 걷는 데 쓴다.</summary>
    public UnityEngine.Object Owner => _owner;

    public bool UsesShaderShadows =>
        _current.flashlightShadows && _current.flashlightShaderShadows && !_occlusionUnavailable;

    private void Awake() => UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;

    // 격자는 맵 크기만큼 메모리를 쓴다.
    private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene _)
    {
        _wallGrids.Clear();
        DisposeOcclusionJobGrids();
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
        RestoreGlobalLight();
        ReleasePlayerLights();
        if (_beamSprite) Destroy(_beamSprite);
        if (_beamTexture) Destroy(_beamTexture);
        if (_beamBase) Destroy(_beamBase);
        if (_beamTarget) { _beamTarget.Release(); Destroy(_beamTarget); }
        if (_occlusionMap) Destroy(_occlusionMap);
        if (_wallMap) Destroy(_wallMap);
        if (_occlusionMaterial) Destroy(_occlusionMaterial);
        DisposeOcclusionJobData();
    }

    // ─── 공개 API ────────────────────────────────────────────────

    public void Apply(DarknessSettings settings, float duration, UnityEngine.Object owner = null)
    {
        if (settings == null) return;
        _owner = owner;
        StartTransition(settings.Clone(), duration);
    }

    // 모양 값은 남겨 두어 다시 짙기만 올리면 같은 모양으로 돌아온다.
    public void Clear(float duration)
    {
        _owner = null;
        DarknessSettings next = _target.Clone();
        next.darkness = 0f;
        next.flashlight = false;
        StartTransition(next, duration);
    }

    public void SetFlashlight(bool on)
    {
        // 키와 SetFlashlightAction이 모두 여기를 지난다. 이미 그 상태면 울리지 않는다.
        if (_target.flashlight != on) (on ? _flashlightOnAudio : _flashlightOffAudio).Play();
        _target.flashlight = on;
        if (_transition == null) _current.flashlight = on;
    }

    public void ToggleFlashlight() => SetFlashlight(!_target.flashlight);

    // ─── 전환 ───────────────────────────────────────────────────

    private void StartTransition(DarknessSettings next, float duration)
    {
        if (_transition != null) StopCoroutine(_transition);
        _transition = null;
        _target = next;

        if (duration <= 0f)
        {
            _current = next.Clone();
            return;
        }
        _transition = StartCoroutine(TransitionRoutine(_current.Clone(), next, duration));
    }

    private IEnumerator TransitionRoutine(DarknessSettings from, DarknessSettings to, float duration)
    {
        // 모양(켜짐·방식)은 바로 목표값, 수치만 보간한다.
        _current = to.Clone();
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _current.darkness = Mathf.Lerp(from.darkness, to.darkness, t);
            _current.color = Color.Lerp(from.color, to.color, t);
            _current.playerLightRadius = Mathf.Lerp(from.playerLightRadius, to.playerLightRadius, t);
            _current.playerLightStrength = Mathf.Lerp(from.playerLightStrength, to.playerLightStrength, t);
            _current.softness = Mathf.Lerp(from.softness, to.softness, t);
            _current.playerLightHeight = Mathf.Lerp(from.playerLightHeight, to.playerLightHeight, t);
            _current.playerSelfShadow = Mathf.Lerp(from.playerSelfShadow, to.playerSelfShadow, t);
            _current.flashlightBrightness = Mathf.Lerp(from.flashlightBrightness, to.flashlightBrightness, t);
            _current.flashlightColor = Color.Lerp(from.flashlightColor, to.flashlightColor, t);
            _current.flashlightJumpDim = Mathf.Lerp(from.flashlightJumpDim, to.flashlightJumpDim, t);
            _current.flashlightRange = Mathf.Lerp(from.flashlightRange, to.flashlightRange, t);
            _current.flashlightAngle = Mathf.Lerp(from.flashlightAngle, to.flashlightAngle, t);
            _current.flashlightAngleFalloff = Mathf.Lerp(from.flashlightAngleFalloff, to.flashlightAngleFalloff, t);
            _current.flashlightDistanceFalloff = Mathf.Lerp(from.flashlightDistanceFalloff, to.flashlightDistanceFalloff, t);
            _current.flashlightFalloffCurve = Mathf.Lerp(from.flashlightFalloffCurve, to.flashlightFalloffCurve, t);
            _current.flashlightStartRoundness = Mathf.Lerp(from.flashlightStartRoundness, to.flashlightStartRoundness, t);
            _current.flashlightShadowIntensity = Mathf.Lerp(from.flashlightShadowIntensity, to.flashlightShadowIntensity, t);
            _current.flashlightShadowSoftness = Mathf.Lerp(from.flashlightShadowSoftness, to.flashlightShadowSoftness, t);
            _current.flashlightShadowSpreadDistance = Mathf.Lerp(from.flashlightShadowSpreadDistance, to.flashlightShadowSpreadDistance, t);
            _current.flashlightShadowInset = Mathf.Lerp(from.flashlightShadowInset, to.flashlightShadowInset, t);
            _current.flashlightWallFaceFade = Mathf.Lerp(from.flashlightWallFaceFade, to.flashlightWallFaceFade, t);
            _current.flashlightOffset = Mathf.Lerp(from.flashlightOffset, to.flashlightOffset, t);
            _current.flicker = Mathf.Lerp(from.flicker, to.flicker, t);
            yield return null;
        }
        _current = to.Clone();
        _transition = null;
    }

    // ─── 매 프레임 조명 반영 ─────────────────────────────────────

    private void LateUpdate()
    {
        _current.flashlight = _target.flashlight;
        _flashOn = Mathf.MoveTowards(_flashOn, _current.flashlight ? 1f : 0f, Time.unscaledDeltaTime * 8f);

        float d = IsActive ? _current.darkness : 0f;
        ApplyGlobalLight(d);
        TrackPlayer();
        ApplyAmbientLight(d);
        ApplyFlashLight(d);
        bool ambientSelfShadow = _ambientLight && _ambientLight.isActiveAndEnabled && _current.playerSelfShadow > 0.001f;
        SyncShadowProxies((_flashLight && _flashLight.isActiveAndEnabled) || ambientSelfShadow);
    }

    private void ApplyGlobalLight(float d)
    {
        if (!_globalLight)
        {
            // 전역 조명이 없는 씬에서 매 프레임 찾지 않도록 1초 간격.
            if (d <= 0.001f || Time.unscaledTime < _nextGlobalLightSearch) return;
            _nextGlobalLightSearch = Time.unscaledTime + 1f;
            CaptureGlobalLight();
            if (!_globalLight) return;
        }

        _globalLight.intensity = _globalBaseIntensity * (1f - d);
        _globalLight.color = Color.Lerp(_globalBaseColor, Hue(_current.color), d);
    }

    // 플레이어 조명은 전역 조명이 잃은 만큼만 더해 빛 안이 원래 밝기가 되게 한다.
    private float LostIntensity(float d) => _globalBaseIntensity * d;

    private void ApplyAmbientLight(float d)
    {
        if (!_ambientLight) return;
        bool on = d > 0.001f && _current.playerLightRadius > 0.01f && _current.playerLightStrength > 0.001f;
        if (!on)
        {
            _ambientOriginal.Restore(_ambientLight);
            return;
        }

        if (!_ambientLight.gameObject.activeSelf) _ambientLight.gameObject.SetActive(true);
        _ambientLight.enabled = true;
        // 이 조명에 붙은 위치 확인용 점(SpriteRenderer)은 빌려 쓰는 동안 숨긴다.
        _ambientOriginal.HideRenderers(_ambientLight);
        _ambientLight.pointLightOuterRadius = _current.playerLightRadius;
        _ambientLight.pointLightInnerRadius = _current.playerLightRadius * (1f - _current.softness);
        _ambientLight.pointLightOuterAngle = 360f;
        _ambientLight.pointLightInnerAngle = 360f;
        _ambientLight.intensity = LostIntensity(d) * _current.playerLightStrength;
        _ambientOriginal.SetHeight(_ambientLight, _current.playerLightHeight);
        _ambientLight.shadowsEnabled = _current.playerSelfShadow > 0.001f;
        _ambientLight.shadowIntensity = _current.playerSelfShadow;
    }

    private void ApplyFlashLight(float d)
    {
        bool on = d > 0.001f && _flashOn > 0.001f && _current.flashlightBrightness > 0f && _trackedPlayer;
        if (!on)
        {
            if (_flashLight) _flashLight.gameObject.SetActive(false);
            return;
        }

        if (!_flashLight) _flashLight = CreateFlashLight();
        if (!_flashLight) return;
        if (!_flashLight.gameObject.activeSelf) _flashLight.gameObject.SetActive(true);

        float intensity = _flashOn * _current.flashlightBrightness;
        if (_current.flicker > 0f)
        {
            // 느린 물결에 조금 빠른 떨림을 얹는다(초당 약 1번 + 3번). 꺼지지 않고 부드럽게 오르내린다.
            float t = Time.unscaledTime;
            float n = Mathf.PerlinNoise(t * 1.1f, 0.37f) * 0.7f + Mathf.PerlinNoise(t * 3.2f, 1.73f) * 0.3f;
            intensity *= 1f - _current.flicker * 0.5f * Mathf.Clamp01(n);
        }
        bool airborne = _physics != null && !_physics.Ground;
        _jumpDim = Mathf.MoveTowards(_jumpDim, airborne ? 1f : 0f, Time.unscaledDeltaTime * JumpDimSpeed);
        intensity *= 1f - _current.flashlightJumpDim * _jumpDim;

        UpdateFlashAngle();
        float rad = _flashAngle * Mathf.Deg2Rad;
        Vector2 dir = new(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 pivot = FollowFlashPivot(_trackedPlayer.transform.position);
        float offset = _current.flashlightMode == FlashlightMode.Around ? 0f : _current.flashlightOffset;
        ConfigureFlashLight(_flashLight, ClampBeforeWall(pivot, dir, offset), LostIntensity(d) * Mathf.Clamp01(intensity));
    }

    private void ConfigureFlashLight(Light2D light, Vector2 position, float intensity)
    {
        Sprite beam = EnsureBeamSprite();
        if (light.lightCookieSprite != beam) light.lightCookieSprite = beam;
        light.intensity = intensity;
        light.color = _current.flashlightColor;
        light.shadowsEnabled = _current.flashlightShadows;
        light.shadowIntensity = _current.flashlightShadowIntensity;

        // 광원 위치는 격자에 맞추지 않는다(천천히 걸을 때 그림자가 뚝뚝 끊긴다). 경계 고정은 셰이더의 월드 픽셀 판정이 맡는다.
        Transform tr = light.transform;
        tr.position = new Vector3(position.x, position.y, tr.position.z);
        tr.rotation = Quaternion.Euler(0f, 0f, _flashAngle - 90f);

        UpdateOcclusion(position, _flashAngle - 90f);
    }

    // ─── 손전등 빛 모양 그림 ─────────────────────────────────────
    // 흰색 + 알파(=밝기). 피벗이 빛 시작점이고 빛은 그림의 +y로 뻗는다. 모양 설정이 바뀔 때만 다시 그린다.

    private struct BeamShape
    {
        public bool Around;
        public float Range, Angle, AngleFalloff, DistanceFalloff, Curve, Roundness;

        public static BeamShape From(DarknessSettings s) => new()
        {
            Around = s.flashlightMode == FlashlightMode.Around,
            // 전환 중 매 프레임 다시 그리지 않도록 반올림한다.
            Range = Mathf.Round(Mathf.Max(0.1f, s.flashlightRange) * 20f) / 20f,
            Angle = Mathf.Round(s.flashlightAngle * 2f) / 2f,
            AngleFalloff = Mathf.Round(s.flashlightAngleFalloff * 50f) / 50f,
            DistanceFalloff = Mathf.Round(s.flashlightDistanceFalloff * 50f) / 50f,
            Curve = Mathf.Round(s.flashlightFalloffCurve * 50f) / 50f,
            Roundness = Mathf.Round(s.flashlightStartRoundness * 50f) / 50f,
        };

        public bool Same(BeamShape o) => Around == o.Around && Range == o.Range && Angle == o.Angle &&
            AngleFalloff == o.AngleFalloff && DistanceFalloff == o.DistanceFalloff && Curve == o.Curve && Roundness == o.Roundness;
    }

    private Sprite EnsureBeamSprite()
    {
        BeamShape shape = BeamShape.From(_current);
        if (_beamSprite && shape.Same(_beamShape)) return _beamSprite;
        _beamShape = shape;

        float range = shape.Range;
        float round = shape.Around ? 0f : shape.Roundness;
        float half = shape.Angle * 0.5f * Mathf.Deg2Rad;
        float halfTan = Mathf.Tan(half);

        float xMin, xMax, yMin, yMax;
        if (shape.Around) { xMin = yMin = -range; xMax = yMax = range; }
        else
        {
            float side = Mathf.Min(range + round, round + range * Mathf.Sin(half) / Mathf.Max(0.01f, Mathf.Cos(half)));
            side = Mathf.Min(side, range + round);
            xMin = -side; xMax = side; yMin = -round - 0.05f; yMax = range;
        }
        float worldW = xMax - xMin, worldH = yMax - yMin;
        int height = BeamTextureHeight;
        int width = Mathf.Clamp(Mathf.RoundToInt(height * worldW / worldH), 16, 1024);

        // Sprite.Create는 가로세로 PPU가 같아야 해서 반올림한 폭에 맞춰 범위를 다시 잡는다.
        worldW = width * worldH / height;
        xMin = -worldW * 0.5f;
        xMax = worldW * 0.5f;

        if (!_beamTexture || _beamTexture.width != width || _beamTexture.height != height)
        {
            if (_beamTexture) Destroy(_beamTexture);
            if (_beamBase) Destroy(_beamBase);
            if (_beamTarget) { _beamTarget.Release(); Destroy(_beamTarget); }
            // 세 텍스처 형식이 같아야 CopyTexture가 된다.
            _beamBase = NewBeamTexture(width, height, "Flashlight Beam Base (DarknessManager)");
            _beamTexture = NewBeamTexture(width, height, "Flashlight Beam (DarknessManager)");
            _beamTarget = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_UNorm)
            {
                name = "Flashlight Beam Target (DarknessManager)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        float exponent = Mathf.Lerp(0.5f, 2.5f, shape.Curve);
        float angleSoft = Mathf.Max(0.02f, shape.AngleFalloff);
        float distSoft = Mathf.Max(0.02f, shape.DistanceFalloff);
        var pixels = new Color32[width * height];
        for (int py = 0; py < height; py++)
        {
            float y = yMin + (py + 0.5f) / height * worldH;
            for (int px = 0; px < width; px++)
            {
                float x = xMin + (px + 0.5f) / width * worldW;
                float d = Mathf.Sqrt(x * x + y * y);
                float value = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(range * (1f - distSoft), range, d));
                if (!shape.Around)
                {
                    float u = y >= 0f
                        ? Mathf.Abs(x) / Mathf.Max(0.0001f, round + y * halfTan)
                        : d / Mathf.Max(0.0001f, round);
                    value *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - angleSoft, 1f, u));
                }
                value = Mathf.Pow(Mathf.Clamp01(value), exponent);
                pixels[py * width + px] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(value * 255f));
            }
        }
        _beamBase.SetPixels32(pixels);
        _beamBase.Apply(false);
        _beamTexture.SetPixels32(pixels);
        _beamTexture.Apply(false);
        _beamOccluded = false;
        _beamBounds = new Vector4(xMin, yMin, worldW, worldH);

        if (_beamSprite) Destroy(_beamSprite);
        Vector2 pivot = new(-xMin / worldW, -yMin / worldH);
        _beamSprite = Sprite.Create(_beamTexture, new Rect(0, 0, width, height), pivot, height / worldH, 0, SpriteMeshType.FullRect);
        _beamSprite.name = "Flashlight Beam (DarknessManager)";
        return _beamSprite;
    }

    private static Texture2D NewBeamTexture(int width, int height, string textureName) =>
        new(width, height, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
        {
            name = textureName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };

    // ─── 벽 가림(1D 그림자 맵 + 셰이더 블러) ──────────────────────

    private void UpdateOcclusion(Vector2 origin, float rotationDegrees)
    {
        bool wanted = UsesShaderShadows && _current.flashlightShadowIntensity > 0.001f;
        if (!wanted || !EnsureOcclusionResources())
        {
            if (_beamOccluded && _beamBase && _beamTexture)
            {
                Graphics.CopyTexture(_beamBase, _beamTexture);
                _beamOccluded = false;
            }
            return;
        }

        // 광원·방향·설정·주변 벽이 그대로면 지난 결과를 그대로 쓴다(가만히 서 있을 때 비용 0).
        CollectOccluders(origin);
        var key = new OcclusionKey
        {
            Origin = origin,
            Rotation = rotationDegrees,
            Bounds = _beamBounds,
            ParamsA = new Vector4(_current.flashlightShadowSoftness, _current.flashlightShadowIntensity,
                                  _current.flashlightShadowInset, _current.flashlightShadowSpreadDistance),
            PixelsPerUnit = _current.flashlightShadowPixelsPerUnit,
            WallFaceFade = _current.flashlightWallFaceFade,
            Walls = _occluderStateHash,
        };
        if (_beamOccluded && key.Same(_lastOcclusionKey)) return;
        _lastOcclusionKey = key;

        RunOcclusionJob(origin, rotationDegrees);
        _occlusionMap.Apply(false);
        UpdateWallMap(origin);

        _occlusionMaterial.SetTexture(OcclusionMapId, _occlusionMap);
        _occlusionMaterial.SetVector(BoundsId, _beamBounds);
        _occlusionMaterial.SetFloat(SoftnessId, _current.flashlightShadowSoftness);
        _occlusionMaterial.SetFloat(ShadowStrengthId, _current.flashlightShadowIntensity);
        _occlusionMaterial.SetFloat(InsetId, Mathf.Max(0f, _current.flashlightShadowInset));
        _occlusionMaterial.SetFloat(SpreadDistanceId, _current.flashlightShadowSpreadDistance);
        _occlusionMaterial.SetFloat(RotationId, rotationDegrees * Mathf.Deg2Rad);
        _occlusionMaterial.SetVector(OriginId, origin);
        _occlusionMaterial.SetFloat(PixelsPerUnitId, _current.flashlightShadowPixelsPerUnit);
        _occlusionMaterial.SetTexture(WallMapId, _wallMap);
        _occlusionMaterial.SetVector(WallMapRectId, _wallMapRect);
        _occlusionMaterial.SetFloat(WallFaceFadeId, _current.flashlightWallFaceFade);
        var descriptor = _beamTarget.descriptor;
        descriptor.depthBufferBits = 0;
        var mask = RenderTexture.GetTemporary(descriptor);
        var horizontal = RenderTexture.GetTemporary(descriptor);
        var blurred = RenderTexture.GetTemporary(descriptor);
        mask.filterMode = horizontal.filterMode = blurred.filterMode = FilterMode.Bilinear;
        mask.wrapMode = horizontal.wrapMode = blurred.wrapMode = TextureWrapMode.Clamp;
        try
        {
            Graphics.Blit(_beamBase, mask, _occlusionMaterial, 0);
            _occlusionMaterial.SetVector("_BlurAxis", new Vector4(1f / _beamBounds.z, 0f, 0f, 0f));
            Graphics.Blit(mask, horizontal, _occlusionMaterial, 1);
            _occlusionMaterial.SetVector("_BlurAxis", new Vector4(0f, 1f / _beamBounds.w, 0f, 0f));
            Graphics.Blit(horizontal, blurred, _occlusionMaterial, 1);
            _occlusionMaterial.SetTexture("_VisibilityMask", mask);
            _occlusionMaterial.SetTexture("_BlurredMask", blurred);
            Graphics.Blit(_beamBase, _beamTarget, _occlusionMaterial, 2);
            Graphics.CopyTexture(_beamTarget, _beamTexture);
        }
        finally
        {
            _occlusionMaterial.SetTexture("_VisibilityMask", null);
            _occlusionMaterial.SetTexture("_BlurredMask", null);
            RenderTexture.ReleaseTemporary(mask);
            RenderTexture.ReleaseTemporary(horizontal);
            RenderTexture.ReleaseTemporary(blurred);
        }
        _beamOccluded = true;
    }

    private bool EnsureOcclusionResources()
    {
        if (_occlusionUnavailable) return false;
        if (_occlusionMaterial && _occlusionMap && _beamTarget) return true;

        if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.RTToTexture) == 0 ||
            !SystemInfo.SupportsTextureFormat(TextureFormat.RFloat))
        {
            _occlusionUnavailable = true;
            Debug.LogWarning("[DarknessManager] 이 기기는 GPU 텍스처 복사나 RFloat 텍스처를 지원하지 않아 손전등 벽 가림을 끕니다.");
            return false;
        }
        if (!_occlusionMaterial)
        {
            Shader shader = Resources.Load<Shader>(OcclusionShaderPath);
            if (!shader)
            {
                _occlusionUnavailable = true;
                Debug.LogError($"[DarknessManager] Resources/{OcclusionShaderPath}.shader를 찾을 수 없어 손전등 벽 가림을 끕니다.");
                return false;
            }
            _occlusionMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        }
        if (!_occlusionMap)
        {
            _occlusionMap = new Texture2D(OcclusionBins, OcclusionRows, TextureFormat.RFloat, false, true)
            {
                name = "Flashlight Occlusion (DarknessManager)",
                wrapMode = TextureWrapMode.Repeat, // 각도는 -π와 π가 이어진다
                // 보간하면 벽 모서리에서 거리 중간값이 생겨 빛이 샌다.
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave,
            };
        }
        return _beamTarget;
    }

    private struct OcclusionKey
    {
        public Vector2 Origin;
        public float Rotation;
        public Vector4 Bounds, ParamsA;
        public float PixelsPerUnit, WallFaceFade;
        public int Walls;

        public bool Same(OcclusionKey o) => Origin == o.Origin && Rotation == o.Rotation && Bounds == o.Bounds &&
                                             ParamsA == o.ParamsA && PixelsPerUnit == o.PixelsPerUnit &&
                                             WallFaceFade == o.WallFaceFade && Walls == o.Walls;
    }

    private readonly List<WallVisualGrid> _gridOccluders = new();
    private int _occluderStateHash;
    private OcclusionKey _lastOcclusionKey;

    private void CollectOccluders(Vector2 origin)
    {
        _gridOccluders.Clear();
        float reach = BeamReach() + 1f;

        int hash = 17;
        foreach (WallShadowCaster wall in WallShadowCaster.Active)
        {
            WallVisualGrid grid = GetWallGrid(wall);
            if (grid == null || grid.SqrDistanceTo(origin) > reach * reach) continue;
            _gridOccluders.Add(grid);
            hash = hash * 31 + grid.Version;
        }
        _occluderStateHash = hash;
    }

    // 광원에서 빛 그림의 가장 먼 모서리까지 거리(돌아가도 빛이 닿는 범위).
    private float BeamReach()
    {
        Vector2 min = new(_beamBounds.x, _beamBounds.y), max = min + new Vector2(_beamBounds.z, _beamBounds.w);
        return Mathf.Sqrt(Mathf.Max(min.sqrMagnitude, max.sqrMagnitude,
                                    new Vector2(min.x, max.y).sqrMagnitude, new Vector2(max.x, min.y).sqrMagnitude));
    }

    // ─── 벽 지도 ────────────────────────────────────────────────
    // 광원 주변 벽 칸을 월드 픽셀(격자 칸) 하나당 텍셀 하나로 옮긴다(값은 FlashlightWallMapJob.Pixels 참고).
    // 셰이더는 벽 그림에 그림자 판 그림자를 드리우지 않는다. 벽 앞면만 광원을 향한 정도와 바로 앞 바닥의 그림자로 어두워진다.

    private Texture2D _wallMap;
    private NativeArray<byte> _wallPixels;
    private Vector4 _wallMapRect;

    private void UpdateWallMap(Vector2 origin)
    {
        const float cell = WallVisualGrid.GridCell;
        float radius = BeamReach();
        int side = Mathf.CeilToInt(2f * radius / cell) + 2;
        if (!_wallMap || _wallMap.width != side)
        {
            if (_wallMap) Destroy(_wallMap);
            _wallMap = new Texture2D(side, side, TextureFormat.R8, false, true)
            {
                name = "Flashlight Walls (DarknessManager)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave,
            };
            if (_wallPixels.IsCreated) _wallPixels.Dispose();
            _wallPixels = new NativeArray<byte>(side * side, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        int x0 = Mathf.FloorToInt((origin.x - radius) / cell), y0 = Mathf.FloorToInt((origin.y - radius) / cell);
        // RunOcclusionJob이 _jobGrids/_jobClear/_jobVisual을 이미 채워 두었다.
        new FlashlightWallMapJob
        {
            Grids = _jobGrids, Clear = _jobClear, Visual = _jobVisual, GridCount = _gridOccluders.Count,
            X0 = x0, Y0 = y0, Side = side, Pixels = _wallPixels,
        }.Schedule(side, 16).Complete();
        _wallMap.SetPixelData(_wallPixels, 0);
        _wallMap.Apply(false);
        _wallMapRect = new Vector4(x0 * cell, y0 * cell, side * cell, side * cell);
    }

    // ─── 가림 계산(FlashlightOcclusionJob) ───────────────────────
    // 격자 데이터(수 MB)는 격자가 바뀔 때만 네이티브 배열로 복사한다.

    private NativeArray<byte> _jobClear;
    private NativeArray<byte> _jobVisual;
    private NativeArray<FlashlightOcclusionJob.Grid> _jobGrids;
    private NativeArray<float> _jobDistances;
    private readonly Dictionary<int, int> _jobGridOffsets = new(); // 격자 Version → 네이티브 배열 안 시작 위치

    private void RunOcclusionJob(Vector2 origin, float rotationDegrees)
    {
        EnsureJobGridData();

        if (!_jobGrids.IsCreated || _jobGrids.Length < Mathf.Max(1, _gridOccluders.Count))
        {
            if (_jobGrids.IsCreated) _jobGrids.Dispose();
            _jobGrids = new NativeArray<FlashlightOcclusionJob.Grid>(Mathf.Max(16, _gridOccluders.Count * 2), Allocator.Persistent);
        }
        for (int i = 0; i < _gridOccluders.Count; i++)
        {
            WallVisualGrid g = _gridOccluders[i];
            _jobGrids[i] = new FlashlightOcclusionJob.Grid
            {
                MinX = g.MinX, MinY = g.MinY, Cell = g.Cell,
                Width = g.Width, Height = g.Height, Offset = _jobGridOffsets[g.Version],
            };
        }
        if (!_jobDistances.IsCreated)
            _jobDistances = new NativeArray<float>(OcclusionBins * OcclusionRows, Allocator.Persistent);

        float rot = rotationDegrees * Mathf.Deg2Rad;
        Vector2 boundsMin = new(_beamBounds.x, _beamBounds.y);
        var job = new FlashlightOcclusionJob
        {
            Grids = _jobGrids, Clear = _jobClear,
            GridCount = _gridOccluders.Count,
            Origin = origin, Cos = Mathf.Cos(rot), Sin = Mathf.Sin(rot),
            BoundsMin = boundsMin, BoundsMax = boundsMin + new Vector2(_beamBounds.z, _beamBounds.w),
            Bins = OcclusionBins, Distances = _jobDistances,
        };
        job.Schedule(OcclusionBins, 64).Complete();
        _occlusionMap.SetPixelData(_jobDistances, 0);
    }

    private void EnsureJobGridData()
    {
        bool upToDate = _jobClear.IsCreated && _jobGridOffsets.Count == _gridOccluders.Count;
        for (int i = 0; upToDate && i < _gridOccluders.Count; i++)
            upToDate = _jobGridOffsets.ContainsKey(_gridOccluders[i].Version);
        if (upToDate) return;

        DisposeOcclusionJobGrids();
        int total = 0;
        foreach (WallVisualGrid g in _gridOccluders) total += g.Clear.Length;
        _jobClear = new NativeArray<byte>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _jobVisual = new NativeArray<byte>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        int offset = 0;
        foreach (WallVisualGrid g in _gridOccluders)
        {
            NativeArray<byte>.Copy(g.Clear, 0, _jobClear, offset, g.Clear.Length);
            NativeArray<byte>.Copy(g.Visual, 0, _jobVisual, offset, g.Visual.Length);
            _jobGridOffsets[g.Version] = offset;
            offset += g.Clear.Length;
        }
    }

    private void DisposeOcclusionJobGrids()
    {
        if (_jobClear.IsCreated) _jobClear.Dispose();
        if (_jobVisual.IsCreated) _jobVisual.Dispose();
        _jobGridOffsets.Clear();
    }

    private void DisposeOcclusionJobData()
    {
        DisposeOcclusionJobGrids();
        if (_jobGrids.IsCreated) _jobGrids.Dispose();
        if (_jobDistances.IsCreated) _jobDistances.Dispose();
        if (_wallPixels.IsCreated) _wallPixels.Dispose();
    }

    // ─── 벽 격자 캐시 ───────────────────────────────────────────

    private sealed class WallGridEntry
    {
        public WallVisualGrid Grid;
        public int SourceVersion;
        public int CheckedFrame = -1;
    }

    private readonly Dictionary<WallShadowCaster, WallGridEntry> _wallGrids = new();

    private WallVisualGrid GetWallGrid(WallShadowCaster wall)
    {
        if (!_wallGrids.TryGetValue(wall, out WallGridEntry entry))
            _wallGrids[wall] = entry = new WallGridEntry();
        if (entry.CheckedFrame == Time.frameCount) return entry.Grid;
        entry.CheckedFrame = Time.frameCount;

        int version = wall.ShapeVersion;
        if (entry.Grid != null && entry.SourceVersion == version) return entry.Grid;
        entry.SourceVersion = version;
        entry.Grid = WallVisualGrid.Build(wall);
        return entry.Grid;
    }

    // 광원이 그림자 판 안에 들어가면 빛이 벽 너머로 샌다(맵 벽이 판 하나면 맵 전체 그림자가 꺼진다).
    //   1) 기준점(발밑)이 판 안이면 가장 가까운 판 밖으로 꺼낸다(뒤쪽 → 상하좌우 중 짧은 쪽, PushOutLimit 이내).
    //   2) 바라보는 방향으로 offset만큼 띄우다가 판 WallGap 앞에서 멈춘다.
    private const float PushOutLimit = 1.5f;
    private static readonly Vector2[] PushOutAxes = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

    private Vector2 ClampBeforeWall(Vector2 pivot, Vector2 dir, float offset)
    {
        CollectNearbyGrids(pivot, offset + PushOutLimit + WallGap);
        if (_nearbyGrids.Count == 0) return pivot + dir * offset;

        Vector2 from = pivot;
        if (InAnyFootprint(from))
        {
            float best = float.MaxValue;
            Vector2 bestDir = Vector2.zero;
            TryPushOut(from, -dir, ref best, ref bestDir);
            if (best > PushOutLimit)
                foreach (Vector2 axis in PushOutAxes) TryPushOut(from, axis, ref best, ref bestDir);
            if (best <= PushOutLimit) from += bestDir * (best + WallGap);
        }

        if (offset < 0.0001f) return from;
        float allowed = offset;
        foreach (WallVisualGrid grid in _nearbyGrids)
        {
            float hit = grid.FirstFootprintEntry(from, dir, offset + WallGap);
            if (hit >= 0f) allowed = Mathf.Min(allowed, hit - WallGap);
        }
        return from + dir * Mathf.Max(0f, allowed);
    }

    // p에서 dir로 나아가 모든 판 밖으로 나오는 거리(겹친 판·이웃 판을 이어서 빠져나간다).
    private void TryPushOut(Vector2 p, Vector2 dir, ref float best, ref Vector2 bestDir)
    {
        float t = 0f;
        for (int guard = 0; guard < 8 && t <= PushOutLimit; guard++)
        {
            float next = t;
            foreach (WallVisualGrid grid in _nearbyGrids)
                next = Mathf.Max(next, t + grid.FootprintExitDistance(p + dir * t, dir, PushOutLimit));
            if (next <= t) break;
            t = next;
        }
        if (t < best) { best = t; bestDir = dir; }
    }

    private bool InAnyFootprint(Vector2 p)
    {
        foreach (WallVisualGrid grid in _nearbyGrids)
            if (grid.InFootprint(p)) return true;
        return false;
    }

    private readonly List<WallVisualGrid> _nearbyGrids = new();

    private void CollectNearbyGrids(Vector2 p, float radius)
    {
        _nearbyGrids.Clear();
        foreach (WallShadowCaster wall in WallShadowCaster.Active)
        {
            WallVisualGrid grid = GetWallGrid(wall);
            if (grid != null && grid.SqrDistanceTo(p) <= radius * radius) _nearbyGrids.Add(grid);
        }
    }

    // 8방향 입력을 그대로 쓰면 대각선 전환이 툭툭 끊겨서 빠르게 돌려 맞춘다.
    private void UpdateFlashAngle()
    {
        if (_facing == null) return;
        Vector2 dir = _facing.LastSetAnimationDir8;
        if (dir.sqrMagnitude < 0.0001f) return;

        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (!_flashAngleInitialized)
        {
            _flashAngle = targetAngle;
            _flashAngleInitialized = true;
            return;
        }
        _flashAngle = Mathf.MoveTowardsAngle(_flashAngle, targetAngle, FlashlightTurnSpeed * Time.unscaledDeltaTime);
    }

    // ─── 조명 찾기 ──────────────────────────────────────────────

    private void CaptureGlobalLight()
    {
        foreach (Light2D light in FindObjectsOfType<Light2D>())
        {
            if (light.lightType != Light2D.LightType.Global || !light.isActiveAndEnabled) continue;
            _globalLight = light;
            _globalBaseIntensity = light.intensity;
            _globalBaseColor = light.color;
            return;
        }
    }

    private void RestoreGlobalLight()
    {
        if (!_globalLight) return;
        _globalLight.intensity = _globalBaseIntensity;
        _globalLight.color = _globalBaseColor;
    }

    private void TrackPlayer()
    {
        Player player = GameManager.instance ? GameManager.instance.player : null;
        if (player == _trackedPlayer && (player == null || _ambientLight)) return;

        ReleasePlayerLights();
        _trackedPlayer = player;
        if (player == null) return;

        player.TryGetInterface(out _facing);
        player.TryGetInterface(out _physics);
        foreach (Light2D light in player.GetComponentsInChildren<Light2D>(true))
        {
            if (light.lightType != Light2D.LightType.Point) continue;
            _ambientLight = light;
            _ambientOriginal = LightSnapshot.Take(light);
            break;
        }

        if (!_ambientLight)
            Debug.LogWarning("[DarknessManager] Player에 Point Light2D가 없어 플레이어 주변 빛과 손전등을 켤 수 없습니다. " +
                             "Player 아래에 Point Light2D를 하나 두세요(꺼 둬도 된다 — 어두워지면 이 매니저가 켠다).");

        CollectPlayerSprites(player);
    }

    // 손전등이 플레이어 뒤통수를 비추지 않게 몸 스프라이트마다 투명한 그림자 대역을 둔다. 플레이어와 벽이 같은
    // 정렬 레이어라 레이어로는 못 뺀다. URP 12는 castsShadows=false 캐스터가 혼자면 안 그려서 그룹으로 묶는다.
    private void CollectPlayerSprites(Player player)
    {
        _playerSprites.Clear();
        foreach (SpriteRenderer sprite in player.GetComponentsInChildren<SpriteRenderer>(true))
            if (!sprite.GetComponent<Light2D>()) _playerSprites.Add(sprite); // 조명 위치 표시용 점은 뺀다

        if (!_shadowProxyRoot)
        {
            _shadowProxyRoot = new GameObject("Player Flashlight Shadow");
            _shadowProxyRoot.transform.SetParent(transform, false);
            // 빈 그룹 목록을 미리 만들지 않으면 URP가 NRE를 낸다.
            WallShadowCaster.AddShadowGroup(_shadowProxyRoot);
        }

        for (int i = 0; i < _playerSprites.Count; i++)
        {
            var go = new GameObject($"Proxy {_playerSprites[i].name}");
            go.transform.SetParent(_shadowProxyRoot.transform, false);
            SpriteRenderer proxy = go.AddComponent<SpriteRenderer>();
            proxy.sprite = _playerSprites[i].sprite;
            proxy.color = new Color(1f, 1f, 1f, 0f);
            proxy.sortingLayerID = _playerSprites[i].sortingLayerID;

            ShadowCaster2D silhouette = go.AddComponent<ShadowCaster2D>();
            silhouette.castsShadows = false;
            silhouette.useRendererSilhouette = true;
            silhouette.selfShadows = true;
            _shadowProxies.Add(proxy);
        }
    }

    private void SyncShadowProxies(bool active)
    {
        for (int i = 0; i < _shadowProxies.Count; i++)
        {
            SpriteRenderer proxy = _shadowProxies[i];
            SpriteRenderer source = i < _playerSprites.Count ? _playerSprites[i] : null;
            if (!proxy) continue;

            bool on = active && source && source.enabled && source.gameObject.activeInHierarchy && source.sprite;
            if (proxy.gameObject.activeSelf != on) proxy.gameObject.SetActive(on);
            if (!on) continue;

            Transform from = source.transform, to = proxy.transform;
            to.SetPositionAndRotation(from.position, from.rotation);
            to.localScale = from.lossyScale;
            proxy.sprite = source.sprite;
            proxy.flipX = source.flipX;
            proxy.flipY = source.flipY;
        }
    }

    // Light2D의 적용 정렬 레이어는 코드로 지정할 API가 없어서 올바르게 설정된 주변 빛을 복제한다.
    private Light2D CreateFlashLight()
    {
        if (!_ambientLight) return null;
        bool wasActive = _ambientLight.gameObject.activeSelf;
        _ambientLight.gameObject.SetActive(false); // 복제본이 활성 상태로 한 프레임 나오지 않게
        Light2D light = Instantiate(_ambientLight, _ambientLight.transform.parent);
        _ambientLight.gameObject.SetActive(wasActive);
        light.name = "Flashlight (DarknessManager)";
        light.lightType = Light2D.LightType.Sprite;
        foreach (Renderer marker in light.GetComponentsInChildren<Renderer>(true)) Destroy(marker);
        return light;
    }

    private void ReleasePlayerLights()
    {
        if (_ambientLight) _ambientOriginal.Restore(_ambientLight);
        if (_flashLight) Destroy(_flashLight.gameObject);
        _playerSprites.Clear();
        foreach (SpriteRenderer proxy in _shadowProxies)
            if (proxy) Destroy(proxy.gameObject);
        _shadowProxies.Clear();
        _ambientLight = null;
        _flashLight = null;
        _facing = null;
        _physics = null;
        _trackedPlayer = null;
        _flashAngleInitialized = false;
        _flashPivotInitialized = false;
    }

    private Vector2 FollowFlashPivot(Vector2 target)
    {
        float sharpness = _current.flashlightFollowSharpness;
        if (sharpness <= 0f || !_flashPivotInitialized ||
            (target - _flashPivot).sqrMagnitude > FlashlightSnapDistance * FlashlightSnapDistance)
        {
            _flashPivot = target;
            _flashPivotInitialized = true;
            return _flashPivot;
        }
        _flashPivot = Vector2.Lerp(_flashPivot, target, 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime));
        return _flashPivot;
    }

    private static Color Hue(Color c)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return max > 0.0001f ? new Color(c.r / max, c.g / max, c.b / max, 1f) : Color.white;
    }

    private struct LightSnapshot
    {
        private bool _active;
        private bool _enabled;
        private float _intensity;
        private float _outerRadius;
        private float _innerRadius;
        private float _outerAngle;
        private float _innerAngle;
        private Vector3 _localPosition;
        private bool _shadowsEnabled;
        private float _shadowIntensity;
        private Renderer[] _renderers;
        private bool[] _rendererEnabled;

        public static LightSnapshot Take(Light2D light) => new()
        {
            _active = light.gameObject.activeSelf,
            _enabled = light.enabled,
            _intensity = light.intensity,
            _outerRadius = light.pointLightOuterRadius,
            _innerRadius = light.pointLightInnerRadius,
            _outerAngle = light.pointLightOuterAngle,
            _innerAngle = light.pointLightInnerAngle,
            _localPosition = light.transform.localPosition,
            _shadowsEnabled = light.shadowsEnabled,
            _shadowIntensity = light.shadowIntensity,
            _renderers = light.GetComponentsInChildren<Renderer>(true),
        };

        public void SetHeight(Light2D light, float height)
        {
            Transform tr = light.transform;
            Vector3 feet = tr.parent ? tr.parent.InverseTransformPoint(tr.root.position) : Vector3.zero;
            tr.localPosition = new Vector3(_localPosition.x, feet.y + height, _localPosition.z);
        }

        public void HideRenderers(Light2D light)
        {
            if (_rendererEnabled == null)
            {
                _rendererEnabled = new bool[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++) _rendererEnabled[i] = _renderers[i] && _renderers[i].enabled;
            }
            foreach (Renderer r in _renderers) if (r) r.enabled = false;
        }

        public void Restore(Light2D light)
        {
            light.intensity = _intensity;
            light.pointLightOuterRadius = _outerRadius;
            light.pointLightInnerRadius = _innerRadius;
            light.pointLightOuterAngle = _outerAngle;
            light.pointLightInnerAngle = _innerAngle;
            light.transform.localPosition = _localPosition;
            light.shadowsEnabled = _shadowsEnabled;
            light.shadowIntensity = _shadowIntensity;
            light.enabled = _enabled;
            if (_rendererEnabled != null)
            {
                for (int i = 0; i < _renderers.Length; i++) if (_renderers[i]) _renderers[i].enabled = _rendererEnabled[i];
                _rendererEnabled = null;
            }
            if (light.gameObject.activeSelf != _active) light.gameObject.SetActive(_active);
        }
    }
}
