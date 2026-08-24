using UnityEngine;
namespace StateMachine
{
    // 디지털 글리치 — 화면이 가로 띠 단위로 어긋나고 RGB 채널이 갈라진다.
    //
    // [노이즈와 무엇이 다른가] ScreenNoiseAction은 화면을 지지직거리는 알갱이로 **덮는다**
    // (원본 화면이 안 보인다). 이건 반대로 원본 화면을 **일그러뜨린다**(원본이 보인다).
    // 둘은 서로 다른 레이어라 같이 켜도 되고, 그러면 "일그러진 화면 위에 지지직"이 된다.
    //
    // [보이는 범위] 월드만 일그러지고 그 위의 UI(대화창·메뉴)는 멀쩡하다 — 연출 중에도 대사가
    // 읽혀야 하므로 의도한 것이다.
    //
    // [끄는 법] duration을 0으로 두면 stop을 켠 이 액션을 따로 넣어 줄 때까지 계속된다.
    // 컷신 끝에 끄는 배선을 빼먹으면 글리치가 평상시 화면에 그대로 남는다.
    [System.Serializable]
    public class ScreenGlitchAction : StateAction
    {
        [Header("세기")]
        [Tooltip("전체 세기. 아래 값들에 한 번 더 곱해지는 마스터 볼륨이다.")]
        [Range(0f, 1f)] public float intensity = 1f;

        [Tooltip("0이면 stop을 켠 액션을 만날 때까지 계속된다.")]
        [Min(0f)] public float duration = 1f;

        [Header("찢김 (가로 띠 어긋남)")]
        [Tooltip("어긋나는 폭(화면 폭 대비). 0.02면 잔글리치, 0.15면 형체가 무너진다.")]
        [Range(0f, 0.5f)] public float blockShift = 0.06f;

        [Tooltip("화면을 가로로 몇 겹으로 나눌지. 클수록 띠가 얇아진다.")]
        [Range(1f, 128f)] public float blockDensity = 24f;

        [Tooltip("그중 실제로 어긋나는 비율. 1이면 전부 흔들려 죽처럼 보인다.")]
        [Range(0f, 1f)] public float blockCoverage = 0.35f;

        [Header("RGB 스플릿 (색수차)")]
        [Tooltip("채널이 갈라지는 거리(화면 폭 대비). 0.003 정도가 자연스럽다.")]
        [Range(0f, 0.1f)] public float rgbSplit = 0.006f;

        [Tooltip("갈라지는 방향(라디안). 0이면 좌우, 1.57이면 위아래.")]
        [Range(0f, 6.2832f)] public float splitAngle;

        [Header("부가")]
        [Range(0f, 1f)] public float scanline = 0.25f;

        [Tooltip("세로 흔들림(수직 동기 어긋남) 폭.")]
        [Range(0f, 0.2f)] public float jitter = 0.01f;

        // 켜는 대신 끄는 용도로 쓸 때 체크. 위 값들은 무시된다.
        public bool stop;

        public override void Act(StateController stateController)
        {
            Play();
        }

        // 테스트베드가 인스펙터 버튼에서 직접 부르는 경로(다른 화면 연출 액션과 같은 형태).
        public void Play()
        {
            if (stop)
            {
                ScreenEffectManager.Instance.StopGlitch();
                return;
            }

            ScreenEffectManager.Instance.SetGlitch(
                intensity,
                duration,
                blockShift,
                rgbSplit,
                blockDensity,
                blockCoverage,
                splitAngle,
                scanline,
                jitter);
        }
    }
}
