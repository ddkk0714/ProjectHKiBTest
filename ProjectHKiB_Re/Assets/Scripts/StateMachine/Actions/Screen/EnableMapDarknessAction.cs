using UnityEngine;
namespace StateMachine
{
    // 맵 씬에 컴포넌트를 꺼 둔 MapDarkness를 켠다 — 들어올 땐 밝다가 이벤트 뒤로 계속 어두운 맵용(puzzle1 스위치 2).
    // SetDarknessAction 대신 쓰는 이유: 이미 켜져 있으면 아무것도 안 해서 스위치를 다시 밟아도 손전등이
    // 꺼지지 않고, 맵을 나가면 MapDarkness.OnDisable이 암전을 걷는다.
    [System.Serializable]
    public class EnableMapDarknessAction : StateAction
    {
        [Tooltip("실제로 꺼져 있던 걸 켰을 때만 같이 실행할 액션(글리치·노이즈 등). 이미 어두우면 건너뛴다.")]
        [SerializeReference, SubclassSelector] public StateAction[] onFirstEnable = System.Array.Empty<StateAction>();

        public override void Act(StateController stateController)
        {
            bool turnedOn = false;
            foreach (MapDarkness darkness in Object.FindObjectsOfType<MapDarkness>(true))
            {
                if (darkness.enabled) continue;
                darkness.enabled = true;
                turnedOn = true;
            }
            if (!turnedOn) return;
            foreach (StateAction action in onFirstEnable) action?.Act(stateController);
        }
    }
}
