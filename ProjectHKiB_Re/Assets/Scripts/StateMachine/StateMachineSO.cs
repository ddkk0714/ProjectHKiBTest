using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "State Machine", menuName = "State Machine/State Machine")]
public class StateMachineSO : ScriptableObject
{
    public CustomVariableSets customVariables;
    public StateSO initialState;
    private StateMachineGraph graph;

    [NaughtyAttributes.Expandable] public List<StateSO> allStates;

    public List<CommandPair> _commandPairs;

#if UNITY_EDITOR
    [NaughtyAttributes.Button]
    public void UpdateStateMachine()
    {
        _commandPairs = new();
        foreach (StateSO state in allStates)
        {
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
        UnityEditor.EditorWindow.GetWindow<StateMachineGraphWindow>().InitializeGraph(graph);
    }
#endif

    public void BindCommands(StateController stateController)
    {
        for (int i = 0; i < _commandPairs.Count; i++)
            _commandPairs[i].Bind(stateController);
    }

    public void UnbindCommands()
    {
        for (int i = 0; i < _commandPairs.Count; i++)
            _commandPairs[i].Unbind();
    }

}
