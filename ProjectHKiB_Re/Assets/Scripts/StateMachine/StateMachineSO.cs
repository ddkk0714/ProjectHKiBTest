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
        if (graph == null)
        {
            graph = (StateMachineGraph)CreateInstance(typeof(StateMachineGraph));
            graph.name = this.name + "Editor";
            graph.targetStateMachine = this;

            UnityEditor.AssetDatabase.AddObjectToAsset(graph, UnityEditor.AssetDatabase.GetAssetPath(this));
            UnityEditor.Undo.RegisterCreatedObjectUndo(graph, "Added Graph Editor");
            UnityEditor.EditorUtility.SetDirty(this);
        }
        //UnityEditor.EditorWindow.GetWindow<StateMachineGraphWindow>().InitializeGraph(graph);
        Debug.LogError("임시로 비활성화됨!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
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
