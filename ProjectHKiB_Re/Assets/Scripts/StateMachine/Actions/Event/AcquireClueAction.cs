using UnityEngine;
namespace StateMachine
{
    // 단서를 지급하고, 원하면 그 자리에서 도감을 열어 방금 얻은 카드를 펼쳐 보여준다.
    //
    // 맵에 등록된 단서를 "그 맵에서 사건이 나면" 주는 경로는 SetRouteEventFlagAction이 담당한다.
    // 이 액션은 그 규칙 밖에서 이벤트가 직접 단서를 쥐여줄 때 쓴다
    // (RouteProgressState.AcquireClueById — 인터넷 게시글 열람이 쓰는 것과 같은 훅).
    //
    // openCodexImmediately를 켜면 획득 직후 도감이 열리며 해당 단서가 선택된다. 얻자마자 내용을
    // 확인시키고 싶은 연출용이고, 조용히 주기만 할 거면 꺼두면 된다.
    [System.Serializable]
    public class AcquireClueAction : StateAction
    {
        public string clueId;
        public bool openCodexImmediately = true;

        public override void Act(StateController stateController)
        {
            RouteModule route = RouteModule.Instance;
            if (route == null || route.Progress == null)
            {
                Debug.LogError("ERROR: AcquireClueAction - RouteModule을 찾을 수 없습니다.");
                return;
            }

            // 이미 갖고 있으면 false가 온다 — 재실행되는 이벤트에서는 정상이므로 오류로 다루지 않는다.
            bool newlyAcquired = route.Progress.AcquireClueById(clueId);

            if (!openCodexImmediately) return;

            var panel = Object.FindObjectOfType<RouteFinding.Codex.CodexPanel>(true);
            if (panel == null)
            {
                Debug.LogWarning($"[AcquireClueAction] 도감 패널을 찾을 수 없어 '{clueId}'를 바로 열지 못했습니다.");
                return;
            }

            if (!newlyAcquired)
                Debug.Log($"[AcquireClueAction] '{clueId}'는 이미 획득한 단서입니다. 도감만 엽니다.");

            panel.OpenWithClue(clueId);
        }
    }
}
