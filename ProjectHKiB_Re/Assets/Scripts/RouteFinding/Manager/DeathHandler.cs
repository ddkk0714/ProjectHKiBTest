using System;
using UnityEngine;

// 사망 처리 매니저 — RouteSpawnManager.ConsumeRespawnNode()(복귀 지점 결정)와
// RouteModule.RevertToLastSave()(미세이브 맵 진도 손실)를 하나로 묶는다.
//
// 기획서의 사망 처리 흐름(CLAUDE.md):
//   사망
//   ├── 침대에서 잠든 적 있음 → 해당 침대로 복귀 (1회 소모) + 미세이브 맵 진도 손실
//   └── 침대 없음            → 집으로 복귀 + 미세이브 맵 진도 손실
//
// [범위, 2026-07-21] 이 클래스는 RouteFinding 쪽 처리(맵 진행 상태 되돌리기 + 다음 위치 결정)만
// 담당한다. 실제 플레이어 리스폰(오브젝트 재활성화, 씬 내 위치 이동, HP 회복)과 사망 감지
// (IDamagable.OnDie 구독, 플레이어/적 구분)는 범위 밖 — 조사 결과 프로젝트 전체에 플레이어 전용
// 사망/리스폰 처리 자체가 아직 없고(DamagableModule.Die()는 플레이어/적 공용이며 게임오브젝트
// 비활성화 외 후속 처리가 없음), SaveModule도 싱글턴이 아니라 플레이어 컴포넌트로 DI 등록되는
// 구조라 "마지막 세이브 데이터"를 이 클래스가 직접 찾아올 방법이 없다. 그래서 HandleDeath가
// lastSaved를 파라미터로 받는 형태로 열어두고, 실제 호출부(전투/플레이어 시스템에서 사망을
// 감지하는 지점)는 그쪽에서 resolve한 SaveModule.CurrentSaveData(또는 LoadedData)를 넘겨
// 호출하면 된다 — Test/RouteSystemTest.cs의 "사망 시뮬레이션" 버튼 참고.
// ════════════════════════════════════════════════════════════════
// [외부 모듈 연동 API] — 플레이어 사망을 감지하는 시스템(전투/체력 시스템)이 사용한다.
// ★ 아직 실제 호출부가 없음(TODO) — 이 클래스 자체는 완성돼 있지만, "사망을 감지해서 이걸
// 호출해주는" 코드가 프로젝트 어디에도 없다. 사망 감지 로직을 만들 때 아래대로 연결하면 된다.
//
// ▸ 접근: DeathHandler.Instance
//   ※ RouteSpawnManager와 같은 패턴 — 자동 생성 안 됨, 씬에 GameObject로 미리 배치해야 한다.
//
// ▸ 호출 시점: 플레이어 사망이 확정된 그 순간(IDamagable.OnDie 등에서 플레이어인지 구분한 뒤) 1회.
//     var respawnNode = DeathHandler.Instance.HandleDeath(currentSaveSlotData);
//   currentSaveSlotData는 호출자가 직접 구해서 넘겨야 한다(이 클래스는 SaveModule 접근 방법을
//   모른다 — SaveModule이 싱글턴이 아니라 플레이어 컴포넌트에 DI로 등록되는 구조라서다). 보통
//   플레이어의 SaveModule 컴포넌트에서 CurrentSaveData(또는 LoadedData)를 꺼내 넘기면 된다.
//   한 번도 세이브 안 하고 죽었다면 null을 넘겨도 된다(맵 진도가 게임 시작 상태로 리셋됨).
//
// ▸ 호출 한 번으로 자동 처리되는 것: 이동 중이었다면 중단, 침대/집 중 복귀 지점 결정 + 소모,
//   미세이브 맵 진도 손실(마지막 세이브로 되돌림), RouteModule.CurrentLocation 갱신.
//
// ▸ 반환값(= 복귀 지점, MapNodeData) 또는 OnRespawned(MapNodeData) 이벤트 구독으로 결과를 받는다.
//   ★ 이 클래스는 "어디로 돌아가야 하는지"만 알려줄 뿐, 실제로 플레이어 게임오브젝트를 그 위치로
//   옮기고 재활성화하고 HP를 회복시키는 건 호출자(또는 OnRespawned 구독자) 책임이다.
// ════════════════════════════════════════════════════════════════
public class DeathHandler : MonoBehaviour
{
    public static DeathHandler Instance { get; private set; }

    // 복귀 처리가 끝난 뒤 발행 — 실제 리스폰(플레이어 위치 이동 등)을 맡는 시스템이 구독해서
    // 이 노드 위치로 플레이어를 옮기면 된다.
    public event Action<MapNodeData> OnRespawned;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DeathHandler] 중복 인스턴스 감지 — 새 인스턴스를 제거합니다.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 사망 처리 진입점. lastSaved가 null이면 맵 진도는 게임 시작 상태로 리셋된다
    // (RouteModule.RevertToLastSave가 이미 null을 그렇게 처리함 — 한 번도 세이브 안 한 채
    // 사망한 경우에 해당).
    // 반환값: 복귀할 노드(침대 또는 집) — 호출부가 이 위치로 플레이어를 실제로 옮겨야 한다.
    public MapNodeData HandleDeath(SaveSlotData lastSaved)
    {
        if (RouteModule.Instance != null && RouteModule.Instance.IsTraveling)
            RouteModule.Instance.AbortTravel(); // 이동 중 사망 방어 — 보통 전투 실패 경로에서 이미 처리되지만 방어적으로 한 번 더

        var respawnNode = RouteSpawnManager.Instance != null
            ? RouteSpawnManager.Instance.ConsumeRespawnNode()
            : null;

        RouteModule.Instance?.RevertToLastSave(lastSaved);

        if (respawnNode != null)
            RouteModule.Instance?.ImportCurrentLocation(respawnNode.guid);

        Debug.Log($"[DeathHandler] 사망 처리 완료 — 복귀: {respawnNode?.nodeName ?? "알 수 없음"}");
        OnRespawned?.Invoke(respawnNode);
        return respawnNode;
    }
}
