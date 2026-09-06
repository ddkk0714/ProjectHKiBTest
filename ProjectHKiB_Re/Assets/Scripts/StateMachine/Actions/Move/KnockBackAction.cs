using UnityEngine;
namespace StateMachine
{
    /// <summary>넉백 방향을 무엇으로 정할지.</summary>
    public enum KnockbackDirectionMode
    {
        /// <summary>대상이 바라보는 반대쪽 — "뒤로 밀려난다". 정면에서 맞은 연출의 기본값.</summary>
        Backward,
        /// <summary>대상이 바라보는 쪽 — 뒤에서 밀려 앞으로 고꾸라지는 연출.</summary>
        Forward,
        /// <summary>월드 기준 고정 방향(fixedDirection). 특정 방향으로 날려야 할 때만.</summary>
        Fixed,
    }

    // 대상을 튕겨 밀어낸다(EVT-002의 "날개 폭발로 심연까지 밀려남").
    // 플레이어를 밀려면 TargetEntityManipulateAction으로 플레이어를 지목한 뒤 이 액션을 넣는다.
    //
    // [방향] 기본은 대상이 바라보는 반대쪽이다. 월드 기준 고정 방향으로 밀면 캐릭터가 어디를 보고
    // 있든 늘 같은 쪽으로 날아가 어색하다. 바라보는 방향은 IDirAnimatable.LastSetAnimationDir8에서
    // 얻는다 — 그 값은 0으로는 갱신되지 않아 멈춰 서 있어도 마지막으로 향했던 쪽을 유지한다.
    //
    // [왜 속도가 아니라 가속인가]
    // 속도를 한 번에 꽂으면 그 순간 최고 속도가 나왔다가 곧바로 마찰에 먹혀 "툭" 끊긴다.
    // 짧은 시간 동안 가속을 주면 튕겨 나가는 탄력이 생기고, 넉백 전용 마찰로 천천히 감속시키면
    // 미끄러지듯 밀려난다.
    //
    // [거리 감각]
    //   최고 속도 ≈ acceleration × accelDuration
    //   이후 이동 거리 ≈ 최고속도 × fixedDeltaTime × f / (1 − f)   (f = knockbackFriction)
    // 기본값(80 × 0.12 ≈ 9.6 units/s, f = 0.95, dt = 0.016)이면 대략 3유닛 밀려난다.
    // 평소 마찰(0.85)이면 같은 속도로도 1유닛 남짓밖에 못 간다 — 그래서 전용 마찰이 필요하다.
    //
    // [시간] 밀려나는 건 물리라서 시간이 흘러야 한다 — 정지형 컷신(InputMode.Cutscene) 안에서
    // 부르면 FixedUpdate가 돌지 않아 아무 일도 일어나지 않는다. InputMode.CutsceneLive를 쓸 것.
    //
    // [애니메이션] 밀려나는 동안 IPhysics.IsKnockedBack이 켜지므로, 캐릭터 상태 기계에
    // KnockbackMoveDecision으로 이어진 KnockbackState가 있으면 그 애니메이션이 자동으로 재생된다.
    // 그런 상태가 없는 대상(더미 등)은 PlayAnimationAction을 나란히 배선하면 된다.
    [System.Serializable]
    public class KnockBackAction : StateAction
    {
        public KnockbackDirectionMode directionMode = KnockbackDirectionMode.Backward;

        /// <summary>directionMode가 Fixed일 때만 쓰는 월드 기준 방향.</summary>
        public Vector3 fixedDirection = Vector3.down;

        /// <summary>밀어내는 가속도(units/s²).</summary>
        [Min(0f)] public float acceleration = 80f;

        /// <summary>가속을 주는 시간(초). 짧을수록 "탁 튕기는" 느낌이 된다.</summary>
        [Min(0f)] public float accelDuration = 0.12f;

        /// <summary>넉백 중 매 틱 곱해지는 감쇠. 1에 가까울수록 오래 미끄러진다.</summary>
        [Range(0f, 1f)] public float knockbackFriction = 0.95f;

        [Header("효과음 (선택)")]
        [Tooltip("넉백과 함께 재생할 SO 기반 원샷 효과음입니다. 비워 두면 무음입니다.")]
        public EffectAudioCue audioCue = new();

        public override void Act(StateController stateController)
        {
            if (!stateController.TryGetInterface(out IPhysics physics))
            {
                Debug.LogError("ERROR: KnockBackAction - IPhysics를 찾을 수 없습니다.");
                return;
            }

            physics.KnockBack(ResolveDirection(stateController, physics), acceleration, accelDuration, knockbackFriction);

            // 넉백이 실제로 걸린 뒤에만 울린다. TargetEntityManipulateAction 안에서 실행되므로
            // 여기 stateController는 밀려나는 대상이다(AudioPlayer가 2D라 위치는 청감에 영향 없음).
            audioCue?.Play(stateController);
        }

        private Vector3 ResolveDirection(StateController stateController, IPhysics physics)
        {
            if (directionMode == KnockbackDirectionMode.Fixed) return fixedDirection;

            Vector3 facing = GetFacing(stateController, physics);
            return directionMode == KnockbackDirectionMode.Forward ? facing : -facing;
        }

        // 바라보는 방향. 애니메이션 방향이 가장 믿을 만하다 — 멈춰 서 있어도 마지막 방향을 유지한다.
        // 그게 없는 대상은 물리의 마지막 이동 방향으로 떨어지고, 그것도 없으면 아래쪽으로 둔다
        // (DirAnimatableModule의 초기값과 같은 기본값이라 방향이 튀지 않는다).
        private static Vector3 GetFacing(StateController stateController, IPhysics physics)
        {
            if (stateController.TryGetInterface(out IDirAnimatable dirAnimatable)
                && dirAnimatable.LastSetAnimationDir8 != Vector2.zero)
                return dirAnimatable.LastSetAnimationDir8.normalized;

            if (physics.LastSetDir != Vector3.zero) return physics.LastSetDir.normalized;

            return Vector3.down;
        }
    }
}
