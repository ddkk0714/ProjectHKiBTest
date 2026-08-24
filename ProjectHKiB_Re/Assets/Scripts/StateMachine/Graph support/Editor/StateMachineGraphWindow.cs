
using GraphProcessor;
using UnityEngine;

public class StateMachineGraphWindow : BaseGraphWindow
{
    protected override void InitializeWindow(BaseGraph graph)
    {
        string name = "name";
        if (graph is StateMachineGraph a)
        {
            name = a.targetStateMachine.name;
        }

        titleContent = new GUIContent($"{name}");

        graphView ??= new BaseGraphView(this);

        rootView.Add(graphView);
    }
}