using UnityEngine;
namespace StateMachine
{
    // 화면이 종이처럼 찢어지는 연출(EVT-006 최종 탈출).
    // 아트가 없어 지금은 섬광+암전 더미로 동작하며 실행 시 경고 로그를 남긴다.
    // 리소스가 들어오면 ScreenEffectManager.ScreenTear 본문만 갈아끼우면 이 액션은 그대로 쓴다.
    [System.Serializable]
    public class ScreenTearAction : StateAction
    {
        [Min(0f)] public float duration = 1f;
        [Range(0f, 1f)] public float flashRatio = 0.25f;
        public Color flashColor = Color.white;
        public Color endColor = new(0f, 0f, 0f, 1f);
        public Vector2 origin = new(0.5f, 0.5f);
        [Range(-180f, 180f)] public float angle;
        [Min(1f)] public float length = 1800f;
        [Min(1f)] public float thickness = 18f;
        public Color tearColor = Color.white;
        [Header("Tear detail")]
        public Color innerColor = new(0.02f, 0.01f, 0.04f, 0.95f);
        public Color shadowEdgeColor = new(0.18f, 0.03f, 0.08f, 0.9f);
        [Range(2, 24)] public int segmentCount = 10;
        [Range(0f, 0.25f)] public float jaggedness = 0.06f;
        [Min(0f)] public float opening = 56f;
        [Min(1f)] public float edgeThickness = 5f;
        [Range(0, 40)] public int shardCount = 14;
        [Min(1f)] public float shardSize = 34f;
        [Min(0f)] public float shardSpread = 260f;
        public int randomSeed;

        public override void Act(StateController stateController)
        {
            ScreenEffectManager.Instance.ScreenTear(new ScreenTearSettings
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
                segmentCount = segmentCount,
                jaggedness = jaggedness,
                opening = opening,
                edgeThickness = edgeThickness,
                shardCount = shardCount,
                shardSize = shardSize,
                shardSpread = shardSpread,
                randomSeed = randomSeed,
            });
        }
    }
}
