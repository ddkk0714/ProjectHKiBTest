using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "State Machine", menuName = "State Machine/State Machine")]
public class StateMachineSO : ScriptableObject
{
    public CustomVariableSets customVariables;
    public StateSO initialState;
    public StateMachineGraph graph;

    [NaughtyAttributes.Expandable] public List<StateSO> allStates;

    public List<CommandPair> _commandPairs;

#if UNITY_EDITOR
    [NaughtyAttributes.Button]
    public void UpdateStateMachine()
    {
        _commandPairs = new();
        foreach (StateSO state in allStates)
        {
            if (state == null) continue; // 목록에 빈 칸이 섞여 있어도 나머지는 정상 처리한다
            state.temporaryID = Random.value;
            if (state.transitions == null) continue;
            foreach (StateTransition transition in state.transitions)
            {
                if (transition.activationInput != EnumManager.InputType.None || transition.trigger)
                    _commandPairs.Add(new(state, transition.activationInput, transition.trigger, transition.type));
            }
        }
    }

    [NaughtyAttributes.Button]
    public void OpenGraphView()
    {
        StateMachineGraph synchronizedGraph = StateMachineGraphSynchronizer.Synchronize(this);
        if (synchronizedGraph == null) return;

        // StateMachineGraphWindow는 Editor 어셈블리에 있어 이 런타임 스크립트에서 이름으로 못 쓴다.
        // 추상 클래스 BaseGraphWindow를 직접 열면 "Can not create instance of abstract class"가 나므로 타입을 찾아 연다.
        foreach (System.Type windowType in UnityEditor.TypeCache.GetTypesDerivedFrom<GraphProcessor.BaseGraphWindow>())
        {
            if (windowType.Name != "StateMachineGraphWindow") continue;
            ((GraphProcessor.BaseGraphWindow)UnityEditor.EditorWindow.GetWindow(windowType)).InitializeGraph(synchronizedGraph);
            return;
        }
        Debug.LogError("[StateMachineSO] StateMachineGraphWindow를 찾지 못해 그래프 창을 열 수 없습니다.");
    }
#endif

    // _commandPairs는 UpdateStateMachine()이 채운다. 한 번도 안 돌린 기계(코드로 만들고 잊었거나
    // 그래프 편집기에서 갓 만든 것)는 null이라, 상태 기계를 붙이는 순간 여기서 터졌다.
    public void BindCommands(StateController stateController)
    {
        if (_commandPairs == null) return;
        for (int i = 0; i < _commandPairs.Count; i++)
            _commandPairs[i]?.Bind(stateController);
    }

    public void UnbindCommands()
    {
        if (_commandPairs == null) return;
        for (int i = 0; i < _commandPairs.Count; i++)
            _commandPairs[i]?.Unbind();
    }

}
