using GraphProcessor;
using UnityEngine;
using System.Collections.Generic;

[System.Serializable, NodeMenuItem("State Machine/State Node")]
public class StateNode : BaseNode
{
    [NaughtyAttributes.Label("")] public StateSO stateSO;

    [Input(name = "In", allowMultiple = true)] public StateSO inputState;
    [Vertical][Input(name = "In (Vertical)", allowMultiple = true)] public StateSO inputStateV;

    [Output(name = "Transitions")] public StateSO outputTransitions;
    [Vertical][Output(name = "Transitions (Vertical)")] public StateSO outputTransitionsV;

    public override string name => stateSO != null ? stateSO.name : "Empty State";
    public override bool isRenamable => true;
    public override bool needsInspector => true;

    public override void SetCustomName(string customName)
    {
        base.SetCustomName(customName);

        if (stateSO != null && stateSO.name != customName)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(stateSO, "Rename State Asset");

            stateSO.name = customName;

            UnityEditor.EditorUtility.SetDirty(stateSO);

            if (graph != null)
            {
                UnityEditor.EditorUtility.SetDirty(graph);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(stateSO);
            }
#endif
        }
    }

    [CustomPortBehavior(nameof(outputTransitions))]
    IEnumerable<PortData> GetPortsForTransitions(List<SerializableEdge> edges)
    {
        if (stateSO == null || stateSO.transitions == null) yield break;

        for (int i = 0; i < stateSO.transitions.Length; i++)
        {
            if (stateSO.transitions[i].showTrueStatePort) // True Port
                yield return new PortData
                {
                    displayName = $"{stateSO.transitions[i].name} (True)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_True"
                };

            if (stateSO.transitions[i].showFalseStatePort) // False Port
                yield return new PortData
                {
                    displayName = $"{stateSO.transitions[i].name} (False)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_False"
                };
        }
        for (int i = 0; i < stateSO.additionalTransitions.Length; i++)
        {
            if (stateSO.additionalTransitions[i].showTrueStatePort) // True Port
                yield return new PortData
                {
                    displayName = $"{stateSO.additionalTransitions[i].name} (True)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_True"
                };

            if (stateSO.additionalTransitions[i].showFalseStatePort) // False Port
                yield return new PortData
                {
                    displayName = $"{stateSO.additionalTransitions[i].name} (False)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_False"
                };
        }
    }

    [CustomPortBehavior(nameof(outputTransitionsV))]
    IEnumerable<PortData> GetPortsForTransitionsV(List<SerializableEdge> edges)
    {
        if (stateSO == null || stateSO.transitions == null) yield break;

        for (int i = 0; i < stateSO.transitions.Length; i++)
        {
            if (stateSO.transitions[i].showTrueStatePort) // True Port
                yield return new PortData
                {
                    displayName = $"{stateSO.transitions[i].name} (True)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_True",
                    vertical = true,
                    tooltip = $"{stateSO.transitions[i].name} (True)"
                };

            if (stateSO.transitions[i].showFalseStatePort) // False Port
                yield return new PortData
                {
                    displayName = $"{stateSO.transitions[i].name} (False)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_False",
                    vertical = true,
                    tooltip = $"{stateSO.transitions[i].name} (False)"
                };
        }
        for (int i = 0; i < stateSO.additionalTransitions.Length; i++)
        {
            if (stateSO.additionalTransitions[i].showTrueStatePort) // True Port
                yield return new PortData
                {
                    displayName = $"{stateSO.additionalTransitions[i].name} (True)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_True",
                    vertical = true,
                    tooltip = $"{stateSO.additionalTransitions[i].name} (True)"
                };

            if (stateSO.additionalTransitions[i].showFalseStatePort) // False Port
                yield return new PortData
                {
                    displayName = $"{stateSO.additionalTransitions[i].name} (False)",
                    displayType = typeof(StateSO),
                    identifier = $"T_{i}_False",
                    vertical = true,
                    tooltip = $"{stateSO.additionalTransitions[i].name} (False)"
                };
        }
    }

    protected override void Process() { }

    public override void OnEdgeConnected(SerializableEdge edge)
    {
        if (edge.outputNode == this && stateSO != null)
        {
            var targetNode = edge.inputNode as StateNode;
            if (targetNode == null || targetNode.stateSO == null) return;

            ParseIdentifierAndAssign(edge.outputPort.portData.identifier, targetNode.stateSO);
        }
        base.OnEdgeConnected(edge);
    }

    public override void OnEdgeDisconnected(SerializableEdge edge)
    {
        if (edge.outputNode == this && stateSO != null)
        {
            ParseIdentifierAndAssign(edge.outputPort.portData.identifier, null);
        }
        base.OnEdgeDisconnected(edge);
    }

    private void ParseIdentifierAndAssign(string identifier, StateSO targetStateSO)
    {
        if (identifier.StartsWith("T_"))
        {
            string[] parts = identifier.Split('_');

            // parts[0] = "T", parts[1] = index, parts[2] = "True" or "False"
            if (parts.Length == 3 && int.TryParse(parts[1], out int index))
            {
                bool isTrueState = parts[2] == "True";

                if (index >= 0 && index < stateSO.transitions.Length)
                {
                    if (isTrueState)
                        stateSO.transitions[index].trueState = targetStateSO;
                    else
                        stateSO.transitions[index].falseState = targetStateSO;
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(stateSO);
#endif
                }
            }
        }
    }
}