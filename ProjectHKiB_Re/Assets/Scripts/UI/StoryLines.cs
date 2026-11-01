using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 임시 스토리 연출: 화면 중앙에 실험 로그를 한 화면씩 타이핑하고 확인(Enter)마다 넘긴 뒤, 끝나면 다음 맵으로 간다.
/// 대사 안에 적는 표시:
///   (페이드 인)   화면 맨 앞 — 글자가 서서히 나타난다
///   {0-100}       숫자가 0→100으로 빠르게 오른다(로딩)
///   [[글자]]      기호로 깜빡이다가 확정된다(스크램블)
///   {noise}       기호 노이즈 줄이 잠깐 흐르고 사라진다
///   {0.5s}        그 자리에서 0.5초 멈췄다가 이어서 찍는다(숫자는 자유)
///   !로 시작하는 줄  붉은색
///   !!로 시작하는 줄  붉은색 + 소리·노이즈·글리치(화면에는 ! 하나로 보인다)
/// 한 화면이 다 찍히면 끝에 _ 커서가 깜빡인다.
/// </summary>
public class StoryLines : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField, TextArea(3, 12)] private string[] lines;
    [SerializeField] private MapDataSO nextMap;
    [SerializeField] private float fadeSec = 0.5f;
    // 글자가 찍히는 동안만 반복 재생하는 타이핑 소리(루프 AudioSource). 다 찍히면(또는 Enter로 완성하면) 멈춘다.
    [SerializeField] private AudioSource typingSource;
    [Tooltip("타이핑 소리 재생 배속(AudioSource.pitch). 빨라지면 소리 높이도 같이 올라간다.")]
    [SerializeField, Range(0.25f, 3f)] private float typingSoundSpeed = 1f;
    [SerializeField] private float counterSec = 1.5f;
    [SerializeField] private float noiseSec = 0.6f;
    [SerializeField] private Color errorColor = new(1f, 0.33f, 0.33f);
    // 오류 줄 노이즈·글리치와 함께 내는 소리(puzzle1 스위치 2 글리치와 같은 소리).
    [SerializeField] private EffectAudioCue glitchAudio;

    [Header("!! 줄 글리치 — 그 화면이 떠 있는 동안 유지, 플레이 중 바꾸면 바로 반영")]
    [SerializeField, Range(0f, 1f)] private float glitchIntensity = 1f;
    [Tooltip("가로로 어긋나는 폭(화면 폭 대비). 0.02 잔글리치, 0.15 이상이면 형체가 무너진다.")]
    [SerializeField, Min(0f)] private float glitchBlockShift = 0.5f;
    [Tooltip("RGB 채널이 갈라지는 거리(화면 폭 대비).")]
    [SerializeField, Min(0f)] private float glitchRgbSplit = 0.025f;
    [Tooltip("화면을 가로로 몇 겹으로 나눌지. 클수록 띠가 얇다.")]
    [SerializeField, Min(1f)] private float glitchBlockDensity = 12f;
    [Tooltip("그중 실제로 어긋나는 비율.")]
    [SerializeField, Range(0f, 1f)] private float glitchBlockCoverage = 1f;
    [Tooltip("RGB가 갈라지는 방향(라디안). 0 좌우, 1.57 위아래.")]
    [SerializeField] private float glitchSplitAngle = 0f;
    [SerializeField, Range(0f, 1f)] private float glitchScanline = 0.8f;
    [Tooltip("세로 흔들림 폭.")]
    [SerializeField, Min(0f)] private float glitchJitter = 0.08f;
    [Tooltip("글리치 직전 화면 노이즈 세기·시간.")]
    [SerializeField, Range(0f, 1f)] private float glitchNoiseIntensity = 1f;
    [SerializeField, Min(0f)] private float glitchNoiseSec = 0.2f;

    [Tooltip("글자 하나가 찍히는 시간(초). 작을수록 빠르다. 대화창은 0.1.")]
    [SerializeField, Min(0.005f)] private float charSec = 0.1f;
    private const string FadeInMark = "(페이드 인)";
    private const string CounterMark = "{0-100}";
    private const string NoiseMark = "{noise}";
    private const string GlitchMark = "!!";
    // neodgm에 확실히 있는 ASCII 기호만 쓴다(인터넷 접속 노이즈와 같은 이유 — 없는 글자는 네모로 뜬다).
    private const string NoiseChars = "01#/\\|-_*+=<>[]";
    private static readonly Regex ScrambleRegex = new(@"\[\[(.*?)\]\]");
    private static readonly Regex DelayRegex = new(@"\{(\d+(?:\.\d+)?)s\}");

    private int _index;
    private Coroutine _typing;
    private string _full;      // 지금 화면이 다 찍혔을 때의 최종 글
    private bool _done;        // 다 찍혀서 Enter를 기다리는 중(커서 깜빡임)
    private bool _cursorShown;
    private float _doneAt;
    private bool _glitching;   // !! 줄이 나온 화면이라 글리치를 유지하는 중
    private bool _pageHasGlitch;

    private void Start()
    {
        AttachToCamera();
        GameManager.instance.inputManager.MENUMode(); // 확인 입력은 MENU 맵에 있고, 그동안 플레이어는 못 움직인다
        GameManager.instance.inputManager.onSubmit += OnSubmit;
        if (lines.Length > 0) Print(lines[0]);
    }

    // 화면 글리치는 카메라가 그린 화면(Top 레이어까지)만 일그러뜨리고 오버레이 UI는 건드리지 않는다.
    // 글자가 오버레이에 있으면 검은 배경만 일그러져 글리치가 거의 안 보이므로, 글자 캔버스를 같은 카메라에
    // 붙여 검은 배경(Top 100) 바로 위에 그린다. 맵 씬이라 카메라를 씬에 저장해 둘 수 없어 시작할 때 연결한다.
    private void AttachToCamera()
    {
        Camera cam = CameraManager.instance != null ? CameraManager.instance.theCamera : Camera.main;
        if (cam == null) return;
        Canvas canvas = text.canvas.rootCanvas;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = Mathf.Clamp(1f, cam.nearClipPlane + 0.01f, cam.farClipPlane - 0.01f);
        canvas.sortingLayerName = "Top";
        canvas.sortingOrder = 200;
    }

    private void OnDestroy()
    {
        text.DOKill(); // 글자 페이드 인 도중 씬이 내려가면 DOTween이 "대상 없음" 경고를 남긴다
        StopGlitch();
        if (GameManager.instance != null) GameManager.instance.inputManager.onSubmit -= OnSubmit;
    }

    // D: 다 찍힌 뒤 커서 깜빡임(0.5초 간격).
    private void Update()
    {
        if (!_done) return;
        bool show = (Time.unscaledTime - _doneAt) % 1f < 0.5f;
        if (show == _cursorShown) return;
        _cursorShown = show;
        text.text = show ? _full + "_" : _full;
    }

    private void Print(string page)
    {
        if (_typing != null) StopCoroutine(_typing);
        _done = false;
        StopGlitch();
        text.text = "";

        text.DOKill();
        text.alpha = 1f;
        if (page.StartsWith(FadeInMark))
        {
            page = page.Substring(FadeInMark.Length).TrimStart('\n');
            text.alpha = 0f;
            text.DOFade(1f, fadeSec).SetUpdate(true);
        }

        _pageHasGlitch = System.Array.Exists(page.Split('\n'), row => row.StartsWith(GlitchMark));
        _full = FinalText(page);
        _typing = StartCoroutine(Type(page));
        typingSource.pitch = typingSoundSpeed;
        typingSource.Play();
    }

    private IEnumerator Type(string page)
    {
        var wait = new WaitForSecondsRealtime(charSec);
        var sb = new StringBuilder();
        string[] rows = page.Split('\n');
        for (int r = 0; r < rows.Length; r++)
        {
            if (r > 0) sb.Append('\n');
            string row = rows[r];
            bool glitch = row.StartsWith(GlitchMark);
            if (glitch) row = row.Substring(1);
            bool error = row.StartsWith("!");
            if (error) sb.Append(ColorOpen);
            if (glitch && !_glitching)
            {
                // B: !! 줄은 나오기 직전 소리와 화면 노이즈를 짧게 친 뒤 일그러뜨리고, 그 화면 동안 유지한다.
                glitchAudio.Play();
                ScreenEffectManager.Instance.SetNoise(glitchNoiseIntensity, glitchNoiseSec);
                yield return new WaitForSecondsRealtime(glitchNoiseSec * 0.75f);
                StartGlitch();
            }

            int i = 0;
            while (i < row.Length)
            {
                if (string.CompareOrdinal(row, i, CounterMark, 0, CounterMark.Length) == 0)
                {
                    yield return Counter(sb);
                    i += CounterMark.Length;
                    continue;
                }
                Match delay = DelayRegex.Match(row, i);
                if (delay.Success && delay.Index == i)
                {
                    typingSource.Pause(); // 멈춰 있는 동안엔 타이핑 소리도 쉰다
                    yield return new WaitForSecondsRealtime(float.Parse(delay.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                    typingSource.UnPause();
                    i += delay.Length;
                    continue;
                }
                if (string.CompareOrdinal(row, i, NoiseMark, 0, NoiseMark.Length) == 0)
                {
                    yield return Noise(sb);
                    i += NoiseMark.Length;
                    continue;
                }
                if (string.CompareOrdinal(row, i, "[[", 0, 2) == 0)
                {
                    int end = row.IndexOf("]]", i + 2, System.StringComparison.Ordinal);
                    if (end > 0)
                    {
                        yield return Scramble(sb, row.Substring(i + 2, end - i - 2));
                        i = end + 2;
                        continue;
                    }
                }

                sb.Append(row[i]);
                i++;
                text.text = sb.ToString();
                yield return wait;
            }
            if (error) sb.Append("</color>");
        }
        Finish();
    }

    // {0-100}: 처음과 끝은 느리게, 가운데는 빠르게 오른다.
    private IEnumerator Counter(StringBuilder sb)
    {
        string head = sb.ToString();
        for (float t = 0f; t < counterSec; t += Time.unscaledDeltaTime)
        {
            text.text = head + Mathf.RoundToInt(100f * Mathf.SmoothStep(0f, 1f, t / counterSec));
            yield return null;
        }
        sb.Append("100");
        text.text = sb.ToString();
    }

    // C: 인터넷 접속 연출과 같은 기호 노이즈 줄을 잠깐 흘렸다가 지운다.
    private IEnumerator Noise(StringBuilder sb)
    {
        string head = sb.ToString();
        var wait = new WaitForSecondsRealtime(0.06f);
        for (float t = 0f; t < noiseSec; t += 0.06f)
        {
            text.text = head + RandomChars(16);
            yield return wait;
        }
        text.text = head;
    }

    // A: 아직 안 찍힌 자리는 기호로 깜빡이고, 한 글자씩 확정된다.
    private IEnumerator Scramble(StringBuilder sb, string segment)
    {
        string head = sb.ToString();
        var wait = new WaitForSecondsRealtime(charSec);
        for (int k = 0; k <= segment.Length; k++)
        {
            text.text = head + segment.Substring(0, k) + RandomChars(segment.Length - k);
            yield return wait;
        }
        sb.Append(segment);
    }

    private static string RandomChars(int count)
    {
        var sb = new StringBuilder(count);
        for (int i = 0; i < count; i++) sb.Append(NoiseChars[Random.Range(0, NoiseChars.Length)]);
        return sb.ToString();
    }

    private string ColorOpen => $"<color=#{ColorUtility.ToHtmlStringRGB(errorColor)}>";

    // Enter로 바로 완성할 때 보여 줄 최종 글(표시를 실제 결과로 바꾼 것).
    private string FinalText(string page)
    {
        string[] rows = page.Split('\n');
        for (int r = 0; r < rows.Length; r++)
        {
            string src = rows[r].StartsWith(GlitchMark) ? rows[r].Substring(1) : rows[r];
            string row = ScrambleRegex.Replace(DelayRegex.Replace(src.Replace(CounterMark, "100").Replace(NoiseMark, ""), ""), "$1");
            rows[r] = src.StartsWith("!") ? ColorOpen + row + "</color>" : row;
        }
        return string.Join("\n", rows);
    }

    // 시간 0 = StopGlitch를 부를 때까지 계속된다.
    private void StartGlitch()
    {
        _glitching = true;
        ScreenEffectManager.Instance.SetGlitch(glitchIntensity, 0f, glitchBlockShift, glitchRgbSplit,
            glitchBlockDensity, glitchBlockCoverage, glitchSplitAngle, glitchScanline, glitchJitter);
    }

    private void StopGlitch()
    {
        if (!_glitching) return;
        _glitching = false;
        ScreenEffectManager.Instance?.StopGlitch();
    }

#if UNITY_EDITOR
    // 플레이 중 인스펙터에서 글리치 값을 바꾸면 걸려 있는 글리치에 바로 반영한다.
    private void OnValidate()
    {
        if (Application.isPlaying && _glitching) StartGlitch();
        if (Application.isPlaying && typingSource != null) typingSource.pitch = typingSoundSpeed;
    }
#endif

    private void Finish()
    {
        _typing = null;
        text.text = _full;
        typingSource.Stop();
        // Enter로 !! 줄 전에 건너뛰어도 그 화면에선 글리치가 걸려 있게 한다.
        if (_pageHasGlitch && !_glitching)
        {
            glitchAudio.Play();
            StartGlitch();
        }
        _done = true;
        _cursorShown = false;
        _doneAt = Time.unscaledTime;
    }

    private void OnSubmit(InputAction.CallbackContext context)
    {
        if (!context.started) return;

        // 타이핑 중이면 이번 Enter는 화면을 바로 완성만 한다.
        if (_typing != null)
        {
            StopCoroutine(_typing);
            Finish();
            return;
        }

        _index++;
        if (_index < lines.Length)
        {
            Print(lines[_index]);
            return;
        }

        GameManager.instance.inputManager.onSubmit -= OnSubmit;
        _done = false;
        StopGlitch();
        text.text = "";
        // 화면을 검게 덮은 뒤 다음 맵을 부른다. 다시 밝히는 건 맵 로드가 끝난 MapManager가 한다.
        ScreenEffectManager.Instance.FadeToBlack(fadeSec);
        DOVirtual.DelayedCall(fadeSec, () =>
        {
            GameManager.instance.inputManager.PLAYMode();
            // Story 맵엔 바닥이 없어 대사 동안 플레이어가 계속 떨어진다. 시작 지점 배치는 위치만 옮기므로
            // 낙하 속도가 남으면 다음 맵 바닥에 닿자마자 위로 튕긴다 — 넘어가기 전에 지운다.
            FindObjectOfType<PhysicsManager>().RemoveVelocity(GameManager.instance.player.GetInterface<IPhysics>());
            GameManager.instance.mapManager.LoadMap(nextMap);
        }, true);
    }
}
