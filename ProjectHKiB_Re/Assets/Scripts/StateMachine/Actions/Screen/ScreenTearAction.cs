using UnityEngine;
namespace StateMachine
{
    // 화면이 종이처럼 찢어지는 연출(EVT-006 최종 탈출).
    // 아트가 없어 지금은 섬광+암전 더미로 동작하며 실행 시 경고 로그를 남긴다.
    // 리소스가 들어오면 ScreenEffectManager.ScreenTear 본문만 갈아끼우면 이 액션은 그대로 쓴다.
    [System.Serializable]
    public class ScreenTearAction : StateAction
    {
        [Header("Timing and base cut")]
        [Tooltip("전체 찢김 연출 시간(초). 컷신 timeScale과 무관하게 진행됩니다.")]
        [Min(0f)] public float duration = 1f;
        [Tooltip("초반 플래시가 전체 시간에서 차지하는 비율입니다.")]
        [Range(0f, 1f)] public float flashRatio = 0.25f;
        public Color flashColor = Color.white;
        public Color endColor = new(0f, 0f, 0f, 1f);
        [Tooltip("화면 기준 시작점입니다. (0,0)은 좌하단, (1,1)은 우상단입니다.")]
        public Vector2 origin = new(0.5f, 0.5f);
        [Tooltip("기준 찢김 방향입니다. 90도는 세로, 0도는 가로입니다.")]
        [Range(-180f, 180f)] public float angle;
        [Min(1f)] public float length = 1800f;
        [Min(1f)] public float thickness = 18f;
        public Color tearColor = Color.white;
        [Header("Multi-line cuts")]
        public Color innerColor = new(0.02f, 0.01f, 0.04f, 0.95f);
        public Color shadowEdgeColor = new(0.18f, 0.03f, 0.08f, 0.9f);
        [Tooltip("동시에 만드는 평행 찢김 선 수입니다.")]
        [Range(1, 4)] public int lineCount = 1;
        [Tooltip("선 사이의 수직 간격(캔버스 픽셀)입니다.")]
        [Min(0f)] public float lineSpacing;
        [Tooltip("각 선에 적용할 기준 각도의 ± 랜덤 편차입니다. 0이면 모두 평행합니다.")]
        [Range(0f, 90f)] public float lineAngleRandomness;
        [Tooltip("선 하나를 나눌 조각 수입니다. 1이면 한 번에 베는 선이 됩니다.")]
        [Range(1, 64)] public int segmentCount = 10;
        [Header("Edge and fragments")]
        [Range(0f, 0.25f)] public float jaggedness = 0.06f;
        [Min(0f)] public float opening = 56f;
        [Min(1f)] public float edgeThickness = 5f;
        [Range(0, 40)] public int shardCount = 14;
        [Min(1f)] public float shardSize = 34f;
        [Min(0f)] public float shardSpread = 260f;
        [Tooltip("0이면 매번 새 모양을 만듭니다. 고정값을 넣으면 같은 모양을 재현합니다.")]
        public int randomSeed;

        public override void Act(StateController stateController)
        {
            Play();
        }

        /// <summary>
        /// Sends the serialized inspector values to the screen effect manager.
        /// Both event chains and the testbed use this same path.
        /// </summary>
        public void Play()
        {
            // 테스트베드와 실제 이벤트 체인이 같은 실행 경로를 사용하도록 유지한다.
            ScreenEffectManager.Instance.ScreenTear(CreateSettings());
        }

        public ScreenTearSettings CreateSettings()
        {
            return new ScreenTearSettings
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
                innerColor = innerColor,
                shadowEdgeColor = shadowEdgeColor,
                lineCount = lineCount,
                lineSpacing = lineSpacing,
                lineAngleRandomness = lineAngleRandomness,
                segmentCount = segmentCount,
                jaggedness = jaggedness,
                opening = opening,
                edgeThickness = edgeThickness,
                shardCount = shardCount,
                shardSize = shardSize,
                shardSpread = shardSpread,
                randomSeed = randomSeed,
            };
        }
    }
}
