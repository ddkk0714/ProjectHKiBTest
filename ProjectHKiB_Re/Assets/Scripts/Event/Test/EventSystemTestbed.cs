using System.Collections;
using System.Text;
using NaughtyAttributes;
using UnityEngine;

/// <summary>
/// 아트 리소스도 씬 배선도 없이 이벤트 시스템을 검증하는 테스트 컴포넌트
/// (RouteSystemTest와 같은 패턴 — 인스펙터 [Button]으로 하나씩 눌러 본다).
///
/// 빈 GameObject에 이 컴포넌트만 붙이고 플레이하면 된다. ScreenEffectManager와
/// DreamReadingModule은 자동 생성 싱글턴이라 별도 배치가 필요 없다.
///
/// [무엇을 확인할 수 있나]
///   화면 연출 · 입력 모드 잠금 · 진행도 플래그 · 맵 전환 · 단서 지급/도감 · 해몽 판정 · 사망,
///   그리고 EVT-002 전체 흐름을 아트 없이 로그와 단색 연출만으로 처음부터 끝까지 재생한다.
/// </summary>
public class EventSystemTestbed : MonoBehaviour
{
    [Header("진행도 플래그")]
    [SerializeField] private EventFlagSO _doodFlag;
    [SerializeField] private int _doodValue;

    [Header("맵 전환")]
    [SerializeField] private MapDataSO _targetMap;

    [Header("단서")]
    [SerializeField] private string _clueId;

    [Header("EVT-002 실제 경로 (편집기로 만든 에셋)")]
    [Tooltip("Tools > Event > 이벤트 체인 편집기 로 '빌드'해서 만든 Dummy_EVT002.asset")]
    [SerializeField] private EventSO _dummyEvent;
    [Tooltip("Dummy_EVT001~004_Trigger.prefab을 씬에 놓고 순서대로 연결한다. 사전 조건 게이팅까지 실제 경로로 확인된다.")]
    [SerializeField] private GameStateEvent[] _chainTriggers;

    [Header("EVT-002 더미 시퀀스")]
    [Tooltip("비워두면 맵 전환 단계를 건너뛴다 — 현실 침실 맵이 아직 없어도 나머지를 확인할 수 있다.")]
    [SerializeField] private MapDataSO _dreamExitMap;
    [SerializeField] private float _stepInterval = 1.2f;

    // ─── 화면 연출 ───────────────────────────────────────────────

    [Button("연출: 암전")]
    private void FadeOut() => ScreenEffectManager.Instance.FadeToBlack(1f);

    [Button("연출: 암전 해제")]
    private void FadeIn() => ScreenEffectManager.Instance.FadeFromBlack(1f);

    [Button("연출: 노이즈 3초")]
    private void Noise() => ScreenEffectManager.Instance.SetNoise(0.6f, 3f);

    [Button("연출: 노이즈 정지")]
    private void NoiseStop() => ScreenEffectManager.Instance.StopNoise();

    [Button("연출: 흰 섬광")]
    private void Flash() => ScreenEffectManager.Instance.Flash(Color.white, 0.3f);

    [Button("연출: 화면 찢김(더미)")]
    private void Tear() => ScreenEffectManager.Instance.ScreenTear(1f);

    [Button("연출: 클로즈업 줌")]
    private void ZoomIn()
    {
        if (!CameraManager.instance) { Debug.LogWarning("[Testbed] CameraManager가 없습니다."); return; }
        CameraManager.instance.ZoomViaOrig(0.4f, 0.5f);
    }

    [Button("연출: 줌 복귀")]
    private void ZoomOut()
    {
        if (!CameraManager.instance) { Debug.LogWarning("[Testbed] CameraManager가 없습니다."); return; }
        CameraManager.instance.ReturntoOrigRes(0.5f);
    }

    [Button("연출: 카메라 흔들기")]
    private void Shake()
    {
        if (!CameraManager.instance) { Debug.LogWarning("[Testbed] CameraManager가 없습니다."); return; }
        CameraManager.instance.Shake();
    }

    // ─── 입력 모드 ───────────────────────────────────────────────

    [Button("입력: 컷신 모드(조작 잠금)")]
    private void InputCutscene()
    {
        GameManager.instance.inputManager.SetInputMode(EnumManager.InputMode.Cutscene);
        Debug.Log("[Testbed] 컷신 모드 — 이동·공격·회피·낙서·메뉴가 전부 막혀야 정상입니다.");
    }

    [Button("입력: 플레이 모드(잠금 해제)")]
    private void InputPlay()
    {
        GameManager.instance.inputManager.SetInputMode(EnumManager.InputMode.Play);
        Debug.Log("[Testbed] 플레이 모드 — 조작과 UI 토글이 모두 복구되어야 정상입니다.");
    }

    // ─── 진행도 플래그 ───────────────────────────────────────────

    [Button("플래그: 지정 값으로 세팅")]
    private void SetFlag()
    {
        if (!_doodFlag) { Debug.LogWarning("[Testbed] _doodFlag가 비어 있습니다."); return; }
        GameManager.instance.eventManager.SetEventFlag(_doodFlag, _doodValue);
        Debug.Log($"[Testbed] {_doodFlag.name} = {_doodValue}");
    }

    [Button("플래그: 전체 덤프")]
    private void DumpFlags()
    {
        var flags = GameManager.instance.eventManager.EventFlags;
        var sb = new StringBuilder($"[Testbed] 이벤트 플래그 {flags.Count}개\n");
        foreach (var kv in flags) sb.AppendLine($"  {kv.Key} = {kv.Value}");
        Debug.Log(sb.ToString());
    }

    // ─── 맵 전환 ─────────────────────────────────────────────────

    [Button("맵: 현재 맵 로그")]
    private void LogCurrentMap()
    {
        MapDataSO current = GameManager.instance.mapManager.CurrentMapData;
        Debug.Log($"[Testbed] 현재 맵: {(current ? current.name + " / " + current.mapAddressableID : "(없음)")}");
    }

    [Button("맵: 지정 맵으로 전환")]
    private void ChangeMap()
    {
        if (!_targetMap) { Debug.LogWarning("[Testbed] _targetMap이 비어 있습니다."); return; }
        GameManager.instance.mapManager.LoadMap(_targetMap);
    }

    // ─── 단서 ────────────────────────────────────────────────────

    [Button("단서: 지급 + 도감 열기")]
    private void GrantClue()
    {
        if (RouteModule.Instance == null) { Debug.LogWarning("[Testbed] RouteModule이 없습니다."); return; }

        bool granted = RouteModule.Instance.Progress.AcquireClueById(_clueId);
        Debug.Log($"[Testbed] 단서 지급 결과 — {_clueId} : {(granted ? "신규 획득" : "이미 보유이거나 없는 ID")}");

        var panel = FindObjectOfType<RouteFinding.Codex.CodexPanel>(true);
        if (panel == null) { Debug.LogWarning("[Testbed] 도감 패널이 씬에 없습니다."); return; }
        panel.OpenWithClue(_clueId);
    }

    [Button("단서: 획득 목록 덤프")]
    private void DumpClues()
    {
        if (RouteModule.Instance == null) { Debug.LogWarning("[Testbed] RouteModule이 없습니다."); return; }

        var acquired = RouteModule.Instance.Progress.AcquiredClueIds;
        var sb = new StringBuilder($"[Testbed] 획득 단서 {acquired.Count}개\n");
        foreach (string id in acquired) sb.AppendLine("  " + id);
        Debug.Log(sb.ToString());
    }

    // ─── 해몽 ────────────────────────────────────────────────────

    [Button("해몽: 노트 연결 상태 덤프")]
    private void DumpLinks()
    {
        if (NoteModule.Instance == null) { Debug.LogWarning("[Testbed] NoteModule이 없습니다."); return; }

        var sb = new StringBuilder("[Testbed] 노트 단서 연결\n");
        foreach (var pair in NoteModule.Instance.ClueLinks) sb.AppendLine($"  {pair.a} - {pair.b}");
        Debug.Log(sb.ToString());
    }

    [Button("해몽: 판정 강제 실행")]
    private void EvaluateReadings()
    {
        DreamReadingModule.Instance.Evaluate();

        var resolved = DreamReadingModule.Instance.ResolvedIds;
        var sb = new StringBuilder($"[Testbed] 해몽 완료 {resolved.Count}건\n");
        foreach (string id in resolved) sb.AppendLine("  " + id);
        Debug.Log(sb.ToString());
    }

    // ─── 사망 ────────────────────────────────────────────────────

    [Button("사망: 플레이어 즉사")]
    private void KillPlayer()
    {
        Player player = GameManager.instance.player;
        if (!player) { Debug.LogWarning("[Testbed] 플레이어가 없습니다."); return; }

        if (!player.TryGetInterface(out IDamagable damagable))
        {
            Debug.LogWarning("[Testbed] 플레이어에서 IDamagable을 찾을 수 없습니다.");
            return;
        }

        Debug.Log("[Testbed] 플레이어 사망 처리 — EntityDeathEventTrigger가 붙어 있으면 리스폰 이벤트가 발동해야 합니다.");
        damagable.Die();
    }

    // ─── EVT-002 실제 경로 (EventManager 경유) ───────────────────

    [Button("더미 NPC 2명 생성 + 이벤트 대상 등록")]
    private void SpawnDummyNpcs()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Testbed] 플레이 모드에서만 가능합니다."); return; }

        MapLocalManager local = GameManager.instance.mapManager.localManager;
        if (local == null)
        {
            Debug.LogWarning("[Testbed] MapLocalManager가 없습니다 — 맵이 아직 로드되지 않았습니다.");
            return;
        }

        SpawnDummyNpc(local, "NPC_A", new Vector3(-2f, 0f, 0f));
        SpawnDummyNpc(local, "NPC_B", new Vector3(2f, 0f, 0f));
        Debug.Log("[Testbed] 더미 NPC 2명을 만들어 이벤트 대상(FromMap)으로 등록했습니다.");
    }

    // 이벤트가 대상을 찾는 경로는 MapLocalManager.allEventTargets다(TargetSearchType.FromMap).
    // 더미 NPC는 StateController + EventControllableEntity만 있으면 충분하다 — 보스화 액션이
    // 상태 기계를 통째로 갈아끼우므로 처음부터 상태 기계를 들고 있을 필요가 없다.
    private void SpawnDummyNpc(MapLocalManager local, string id, Vector3 position)
    {
        var go = new GameObject("DummyNPC_" + id);
        go.transform.position = position;

        var controller = go.AddComponent<StateController>();
        var controllable = go.AddComponent<EventControllableEntity>();
        controllable.ID = id;
        controllable.Target = controller;

        local.allEventTargets.targetEntities[id] = controllable;
    }

    [Button("EVT-002 실제 경로 실행 (EventManager 경유)")]
    private void RunEvt002RealPath()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Testbed] 플레이 모드에서만 가능합니다."); return; }
        if (!_dummyEvent) { Debug.LogWarning("[Testbed] _dummyEvent가 비어 있습니다 — 생성기를 먼저 실행하세요."); return; }

        Debug.Log("[Testbed] EventManager.StartEvent 호출 — 여기서부터는 실제 이벤트 상태 기계가 돕니다.");
        GameManager.instance.eventManager.StartEvent(_dummyEvent);
    }

    // 지금 진행도로 시작할 수 있는 이벤트를 찾아 하나 실행한다.
    // 사전 조건 판정은 GameStateEvent.CanTrigger가 하므로, 이 버튼을 반복해서 누르면
    // EVT-001 → 002 → 003 → 004 순서로 저절로 진행된다(각 이벤트가 끝나며 dood를 올리므로).
    [Button("체인 진행 시도 (조건 통과하는 이벤트 1개 실행)")]
    private void AdvanceChain()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Testbed] 플레이 모드에서만 가능합니다."); return; }
        if (_chainTriggers == null || _chainTriggers.Length == 0)
        {
            Debug.LogWarning("[Testbed] _chainTriggers가 비어 있습니다 — 트리거 프리팹들을 씬에 놓고 연결하세요.");
            return;
        }

        for (int i = 0; i < _chainTriggers.Length; i++)
        {
            GameStateEvent trigger = _chainTriggers[i];
            if (!trigger || !trigger.CanTrigger()) continue;

            Debug.Log($"[Testbed] 사전 조건 통과 — '{trigger.name}' 실행");
            trigger.TriggerEvent();
            return;
        }

        Debug.LogWarning("[Testbed] 지금 진행도로 시작할 수 있는 이벤트가 없습니다. '진행 상태 요약'으로 확인하세요.");
    }

    [Button("진행 상태 요약")]
    private void DumpProgress()
    {
        var sb = new StringBuilder("[Testbed] 진행 상태" + System.Environment.NewLine);

        if (Application.isPlaying)
        {
            var flags = GameManager.instance.eventManager.EventFlags;
            sb.AppendLine($"  이벤트 플래그 {flags.Count}개");
            foreach (var kv in flags) sb.AppendLine($"    {kv.Key} = {kv.Value}");

            var resolved = DreamReadingModule.Instance.ResolvedIds;
            sb.AppendLine($"  해몽 완료 {resolved.Count}건");
            foreach (string id in resolved) sb.AppendLine("    " + id);
        }

        if (_chainTriggers != null)
        {
            sb.AppendLine("  트리거별 사전 조건");
            for (int i = 0; i < _chainTriggers.Length; i++)
            {
                GameStateEvent trigger = _chainTriggers[i];
                if (!trigger) { sb.AppendLine($"    [{i}] (비어 있음)"); continue; }
                sb.AppendLine($"    {trigger.name} : {(trigger.CanTrigger() ? "통과" : "차단")}");
            }
        }

        Debug.Log(sb.ToString());
    }

    // ─── EVT-002 전체 흐름 (아트 없이) ───────────────────────────

    [Button("EVT-002 더미 시퀀스 재생")]
    private void PlayEvt002()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Testbed] 플레이 모드에서만 실행할 수 있습니다.");
            return;
        }

        StopAllCoroutines();
        StartCoroutine(Evt002Routine());
    }

    // 기획서 EVT-002의 연출 순서를 그대로 따라가되, 실제 NPC·일러스트·현실 침실 맵이 없어도
    // 끝까지 돌도록 각 단계를 로그와 단색 연출로 대신한다. 여기서 부르는 API는 실제 배선에서
    // 각 StateAction이 부르는 것과 같은 것들이라, 이 시퀀스가 돌면 배선해도 돈다.
    private IEnumerator Evt002Routine()
    {
        ScreenEffectManager screen = ScreenEffectManager.Instance;

        Debug.Log("[EVT-002] S0 대화 — 조작 잠금");
        GameManager.instance.inputManager.SetInputMode(EnumManager.InputMode.Cutscene);
        yield return new WaitForSecondsRealtime(_stepInterval);

        Debug.Log("[EVT-002] S1 클로즈업 + 눈동자 노이즈 — 2D 일러스트가 없어 카메라 줌으로 대체");
        if (CameraManager.instance) CameraManager.instance.ZoomViaOrig(0.4f, 0.5f);
        screen.SetNoise(0.6f, _stepInterval * 2f);
        yield return new WaitForSecondsRealtime(_stepInterval * 2f);

        Debug.Log("[EVT-002] S2 보스화 — NPC가 없어 상태 기계 교체 대신 로그만 남깁니다");
        if (CameraManager.instance) CameraManager.instance.Shake();
        screen.Flash(Color.white, 0.3f);
        yield return new WaitForSecondsRealtime(_stepInterval);

        Debug.Log("[EVT-002] S3 플레이어 강제 넉백");
        KnockbackPlayer();
        yield return new WaitForSecondsRealtime(_stepInterval);

        Debug.Log("[EVT-002] S4 암전");
        if (CameraManager.instance) CameraManager.instance.ReturntoOrigRes(0.3f);
        screen.FadeToBlack(1f);
        while (screen.IsPlaying) yield return null;

        yield return Evt002ChangeMap();

        Debug.Log("[EVT-002] S6 기상 — 암전 해제 · 진행도 갱신 · 조작 복구");
        screen.FadeFromBlack(1f);
        if (_doodFlag) GameManager.instance.eventManager.SetEventFlag(_doodFlag, 2);
        while (screen.IsPlaying) yield return null;

        GameManager.instance.inputManager.SetInputMode(EnumManager.InputMode.Play);
        Debug.Log("[EVT-002] 시퀀스 종료 — 조작이 돌아왔는지 직접 움직여 확인하세요");
    }

    private IEnumerator Evt002ChangeMap()
    {
        if (!_dreamExitMap)
        {
            Debug.Log("[EVT-002] S5 맵 전환 건너뜀 — 현실 침실 맵(_dreamExitMap)이 지정되지 않았습니다");
            yield break;
        }

        Debug.Log($"[EVT-002] S5 맵 전환 시작 — {_dreamExitMap.name}");
        MapManager mapManager = GameManager.instance.mapManager;
        mapManager.LoadMap(_dreamExitMap);

        // 로드는 Addressables 비동기라 완료를 기다려야 한다 — 실제 배선에서 MapLoadedDecision이
        // 하는 일과 같다. 맵이 영영 안 뜨는 경우에 코루틴이 멈춰 있지 않도록 시간 제한을 둔다.
        float timeout = Time.unscaledTime + 10f;
        while (mapManager.CurrentMapData != _dreamExitMap && Time.unscaledTime < timeout)
            yield return null;

        Debug.Log(mapManager.CurrentMapData == _dreamExitMap
            ? "[EVT-002] S5 맵 전환 완료"
            : "[EVT-002] S5 맵 전환 시간 초과 — 그대로 진행합니다");
    }

    private void KnockbackPlayer()
    {
        Player player = GameManager.instance.player;
        if (!player) { Debug.LogWarning("[EVT-002] 플레이어가 없어 넉백을 건너뜁니다."); return; }

        if (player.TryGetInterface(out IPhysics physics))
        {
            // KnockBackAction과 같은 규칙 — 바라보는 반대쪽으로, 짧은 가속 + 완만한 감쇠.
            Vector3 facing = player.TryGetInterface(out IDirAnimatable dirAnimatable)
                             && dirAnimatable.LastSetAnimationDir8 != Vector2.zero
                ? (Vector3)dirAnimatable.LastSetAnimationDir8.normalized
                : Vector3.down;
            physics.KnockBack(-facing, 110f, 0.12f, 0.95f);
        }
        else Debug.LogWarning("[EVT-002] 플레이어에서 IPhysics를 찾을 수 없습니다.");
    }
}
