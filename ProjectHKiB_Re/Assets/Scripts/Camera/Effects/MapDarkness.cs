using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵 씬에 두면 켜질 때 암전을 걸고 꺼질 때 걷는다(이벤트 중 잠깐 어두워지는 건 SetDarknessAction).
/// 켜질 때 이 맵의 벽(Wall 레이어 ZCollider2D, 계단·경사 제외)에 WallShadowCaster를 붙인다.
/// </summary>
public class MapDarkness : MonoBehaviour
{
    [Tooltip("이 맵에 들어올 때 걸 암전 모양(짙기·플레이어 주변 빛·손전등·그림자). 플레이 중에 고치면 바로 화면에 반영된다.")]
    [SerializeField] private DarknessSettings _settings = new();
    [Tooltip("켜질 때(맵에 들어올 때) 어두워지는 시간. 맵 전환 페이드와 겹치므로 보통 0.")]
    [SerializeField, Min(0f)] private float _fadeIn;
    [Tooltip("이 맵의 벽에 손전등 그림자를 자동으로 단다. 끄면 WallShadowCaster를 직접 붙인 벽만 가린다.")]
    [SerializeField] private bool _autoWallShadows = true;
    [Tooltip("벽으로 볼 물리 레이어. 계단(isStair)·경사(useSlope)로 표시된 콜라이더는 이 레이어여도 빼고 본다.")]
    [SerializeField] private LayerMask _wallLayers = 1 << 3; // Wall

#if UNITY_EDITOR
    private bool _lastInspectorFlashlight; // 인스펙터 손전등 체크가 바뀌었는지 보려고 기억한다
#endif

    // 맵 언로드도 OnDisable을 거친다. 다음 맵이 어두운 맵이면 그 맵의 MapDarkness가 OnEnable에서 바로 다시 건다.
    private void OnEnable()
    {
        // 플레이어가 F로 켜 둔 손전등을 암전이 끄지 않게 한다(밝을 때 켜 두면 어두워지는 순간 바로 보인다).
        DarknessSettings settings = _settings.Clone();
        settings.flashlight = _settings.flashlight || DarknessManager.Instance.FlashlightOn;
        DarknessManager.Instance.Apply(settings, _fadeIn, this);
#if UNITY_EDITOR
        _lastInspectorFlashlight = _settings.flashlight;
#endif
        if (_autoWallShadows) AttachWallShadows();
    }

    private void AttachWallShadows()
    {
        foreach (ZCollider2D wall in FindWalls())
        {
            if (wall.TryGetComponent(out WallShadowCaster _)) continue;
            wall.gameObject.AddComponent<WallShadowCaster>();
        }
    }

    // 꺼진 벽(퍼즐로 켜지는 벽 등)도 포함한다 — 켜지는 순간 그림자도 같이 생긴다.
    private List<ZCollider2D> FindWalls()
    {
        var walls = new List<ZCollider2D>();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (ZCollider2D wall in root.GetComponentsInChildren<ZCollider2D>(true))
            {
                if ((_wallLayers.value & (1 << wall.gameObject.layer)) == 0) continue;
                if (wall.isStair || wall.useSlopeDU || wall.useSlopeRL) continue;
                walls.Add(wall);
            }
        }
        return walls;
    }

#if UNITY_EDITOR
    // 이미 만든 판은 새로 덮어쓴다.
    [NaughtyAttributes.Button("맵 벽 그림자 판 생성 (편집 가능)")]
    private void BakeWallShadows()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[MapDarkness] 그림자 판 생성은 플레이 중이 아닐 때만 됩니다(플레이 중 만든 것은 저장되지 않는다).", this);
            return;
        }
        int count = 0;
        foreach (ZCollider2D wall in FindWalls())
        {
            if (!wall.TryGetComponent(out WallShadowCaster caster))
                caster = UnityEditor.Undo.AddComponent<WallShadowCaster>(wall.gameObject);
            caster.BakeFootprints();
            count++;
        }
        Debug.Log($"[MapDarkness] 벽 {count}개에 그림자 판을 만들었습니다. 각 'WallShadows (편집 가능)' 아래 판의 Edit Shape로 고칠 수 있습니다.", this);
    }
#endif

#if UNITY_EDITOR
    // 플레이 중 인스펙터 값을 바로 반영한다. 손전등 체크 자체를 바꿨을 때만 그 값을 쓰고, 다른 값을 만질 땐 F키로 바꾼 현재 상태를 유지한다.
    private void OnValidate()
    {
        DarknessManager manager = DarknessManager.Instance;
        if (!Application.isPlaying || !isActiveAndEnabled || !manager || manager.Owner != this) return;

        DarknessSettings live = _settings.Clone();
        if (_settings.flashlight == _lastInspectorFlashlight) live.flashlight = manager.FlashlightOn;
        _lastInspectorFlashlight = _settings.flashlight;
        manager.Apply(live, 0f, this);
    }
#endif

    private void OnDisable()
    {
        // 다른 쪽(다음 맵·이벤트)이 이미 덮어썼으면 건드리지 않는다.
        DarknessManager manager = DarknessManager.Instance;
        if (manager && manager.Owner == this) manager.Clear(0f);
    }
}
