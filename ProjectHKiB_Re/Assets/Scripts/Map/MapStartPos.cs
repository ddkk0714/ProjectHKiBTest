using UnityEngine;

/// <summary>
/// 맵에 들어왔을 때 플레이어가 놓일 지점.
///
/// [기본 진입 지점 vs 전용 진입 지점]
/// 한 맵이 여러 맵과 연결돼 있으면 "어디서 왔는지"별로 지점을 다 만들어야 해서 금방 복잡해진다.
/// 그래서 대부분의 맵은 <see cref="isDefaultEntry"/>를 켠 지점 **하나만** 두면 된다 —
/// 어느 맵에서 넘어오든 이 지점으로 배치된다.
///
/// 특정 방향에서 들어올 때만 다른 자리에 세우고 싶을 때(예: 서쪽 문으로 들어오면 서쪽 끝에)
/// 그 경우에만 <see cref="fromScene"/>을 지정한 전용 지점을 추가한다.
/// MapStartPosPlacer가 **전용 지점 → 기본 지점** 순으로 찾는다.
/// </summary>
public class MapStartPos : MonoBehaviour
{
    [Tooltip("ID selectable by ChangeMapAction. Leave empty to use only the normal entry rules.")]
    [SerializeField] private string entryPointId;
    [Tooltip("어느 맵에서 넘어오든 전용 지점이 없으면 여기로 배치된다. 맵마다 하나만 두면 된다.")]
    [SerializeField] private bool isDefaultEntry = false;

    [Tooltip("이 맵에서 넘어올 때만 사용하는 전용 진입 지점.")]
    [NaughtyAttributes.HideIf(nameof(isDefaultEntry))]
    [NaughtyAttributes.Scene] public string fromScene;

    [SerializeField] private EnumManager.AnimDir _endDir;
    [SerializeField] private GameEvent endEvent;
    [SerializeField] private PhysicsManager physicsManager;

    public bool IsDefaultEntry => isDefaultEntry;
    public string EntryPointId => entryPointId;

    /// <summary>지정한 맵에서 넘어올 때 쓰는 전용 지점인지.</summary>
    public bool MatchesSource(string previousMapID)
        => !isDefaultEntry && !string.IsNullOrEmpty(fromScene) && fromScene == previousMapID;

    public void SetPlayerToStartPos(EnumManager.AnimDir? directionOverride = null)
    {
        // 맵 씬은 additive로 로드되는데 PhysicsManager는 System 씬에 있다 — 씬 간 참조는 저장할 수
        // 없으므로 인스펙터 필드로는 채울 방법이 없다(실제로 프로젝트의 모든 기존 배치가 비어 있다).
        // PhysicsModule이 쓰는 것과 같은 방식으로 씬에서 직접 찾는다. (2026-08-04)
        if (physicsManager == null) physicsManager = FindObjectOfType<PhysicsManager>();

        if (physicsManager == null)
        {
            Debug.LogWarning($"[MapStartPos] PhysicsManager를 찾지 못해 '{name}'으로 배치하지 못했습니다.");
            return;
        }

        physicsManager.RealTeleport(GameManager.instance.player.GetInterface<IPhysics>(), transform.position);

        IDirAnimatable dirAnimatable = GameManager.instance.player.GetInterface<IDirAnimatable>();
        dirAnimatable.SetAnimationDirection(directionOverride ?? _endDir);

        // SetAnimationDirection은 CurrentAnimDir 값만 즉시 바꾼다 — 화면에 보이는 스프라이트는
        // SimpleAnimationPlayer.ApplyFrame이 이 값을 읽어 갱신하는데, 그 호출은 지금 재생 중인
        // 클립이 스스로 다음 프레임으로 넘어갈 때(클립 프레임 지속시간만큼)나 되어야 일어난다.
        // resetWhenDirectionChange가 꺼진 클립(대개 Idle)이 재생 중이면 그 지연이 그대로 체감된다.
        // 맵 전환 직후엔 어색하므로 재생 중인 클립을 강제로 즉시 재시작해 첫 프레임을 새 방향으로
        // 바로 반영한다. (SaveModule.TeleportPlayer가 같은 이유로 같은 처리를 한다)
        SimpleAnimationPlayer animationPlayer = dirAnimatable?.AnimationPlayer;
        if (animationPlayer != null) animationPlayer.Play(animationPlayer.CurrentAnimationName);

        if (endEvent != null) endEvent.TriggerEvent();
    }
}
