using UnityEngine;
using GraphProcessor;

[CreateAssetMenu(fileName = "New StateMachine Graph", menuName = "State Machine/Graph")]
public class StateMachineGraph : BaseGraph
{
    public StateMachineSO targetStateMachine;

    protected override void OnEnable()
    {
        base.OnEnable();
    }

    public override BaseNode AddNode(BaseNode node)
    {
        node = base.AddNode(node);

        if (node is StateNode sn)
        {
            if (targetStateMachine == null)
            {
                Debug.LogError("targetStateMachine이 지정되지 않아 StateSO를 생성할 수 없습니다!");
                return node;
            }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(targetStateMachine, "Create State Node");

            StateSO newfile = CreateInstance<StateSO>();
            newfile.name = "New State";
            sn.stateSO = newfile;
            targetStateMachine.allStates.Add(newfile);
            targetStateMachine.UpdateStateMachine();

            UnityEditor.AssetDatabase.AddObjectToAsset(sn.stateSO, targetStateMachine);

            UnityEditor.Undo.RegisterCreatedObjectUndo(sn.stateSO, "Create State Node");

            UnityEditor.EditorUtility.SetDirty(targetStateMachine);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(targetStateMachine);
#endif
        }

        return node;
    }

    public override void RemoveNode(BaseNode node)
    {
        if (node is StateNode sn && sn.stateSO != null)
        {
#if UNITY_EDITOR
            if (targetStateMachine != null)
            {
                UnityEditor.Undo.RecordObject(targetStateMachine, "Delete State Node");
                targetStateMachine.allStates.Remove(sn.stateSO);
                targetStateMachine.UpdateStateMachine();
            }

            UnityEditor.Undo.DestroyObjectImmediate(sn.stateSO);

            if (targetStateMachine != null)
            {
                UnityEditor.EditorUtility.SetDirty(targetStateMachine);
            }
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(targetStateMachine);
#endif
        }

        base.RemoveNode(node);
    }
}