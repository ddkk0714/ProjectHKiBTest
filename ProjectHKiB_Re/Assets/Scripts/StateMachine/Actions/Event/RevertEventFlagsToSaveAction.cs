using UnityEngine;
namespace StateMachine
{
    // 이벤트 플래그를 **마지막 세이브 시점**으로 되돌린다 — 사망 복귀 이벤트의 핵심 단계.
    //
    // [무엇을 무르나] 죽기 전까지 올려둔 진행도(dood 같은 것)를 세이브 시점 값으로 되돌린다.
    // 그래서 "진행 중이던 이벤트"는 사전 조건이 다시 맞아떨어져 처음부터 다시 뜰 수 있게 된다.
    // 되돌리는 것은 EventManager의 플래그뿐이다 — 루트파인딩 진도는 RouteModule이 따로 관리한다
    // (DeathHandler.HandleDeath 참고).
    //
    // [순서가 중요하다] 이 액션만으로는 이미 배치가 끝난 월드 오브젝트가 되돌아가지 않는다.
    // EventControllableEntity/Animation은 맵을 열 때 플래그를 읽어 자기 상태를 정하는데, 이미
    // Initialize를 마친 뒤라 값만 바꿔서는 스스로 다시 배치되지 않기 때문이다.
    // **반드시 뒤에 ChangeMapAction을 두어 맵을 다시 열 것** — 사망 복귀는 대개 현실의 방으로
    // 이동하므로 자연히 충족된다.
    //
    // [세이브가 없으면] 아무것도 하지 않고 경고만 남긴다. 한 번도 저장하지 않고 죽었을 때
    // 전부 비워버리면 맵이 저작해 둔 초기 플래그까지 날아가 이벤트가 영영 안 뜨기 때문이다
    // (SaveModule.LoadEvents도 같은 판단을 한다).
    [System.Serializable]
    public class RevertEventFlagsToSaveAction : StateAction
    {
        [Tooltip("켜면 저장 직후 상태(CurrentSaveData) 대신 이번 판을 시작할 때 불러온 데이터(LoadedData)를 기준으로 되돌린다.")]
        public bool useLoadedDataInstead;

        public override void Act(StateController stateController)
        {
            EventManager eventManager = GameManager.instance != null ? GameManager.instance.eventManager : null;
            if (eventManager == null)
            {
                Debug.LogError("ERROR: RevertEventFlagsToSaveAction - EventManager를 찾을 수 없습니다.");
                return;
            }

            if (!TryGetSaveModule(out SaveModule save))
            {
                Debug.LogWarning("[RevertEventFlagsToSaveAction] 플레이어에서 SaveModule을 찾지 못해 " +
                                 "이벤트 플래그를 되돌리지 못했습니다.");
                return;
            }

            // 저장 직후 스냅샷(CurrentSaveData)을 기본으로 본다. 세이브를 한 번도 안 했다면 비어 있으므로
            // 이번 판 시작에 불러온 데이터로 떨어진다.
            SaveSlotData snapshot = useLoadedDataInstead
                ? save.LoadedData
                : save.CurrentSaveData ?? save.LoadedData;

            eventManager.RevertFlagsToSave(snapshot);
        }

        // SaveModule은 싱글턴이 아니라 플레이어에 DI로 등록되는 컴포넌트다(DeathHandler 주석 참고).
        private static bool TryGetSaveModule(out SaveModule save)
        {
            save = null;
            Player player = GameManager.instance != null ? GameManager.instance.player : null;
            return player != null && player.TryGetInterface(out save);
        }
    }
}
