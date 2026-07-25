using UnityEngine;
using GraphProcessor;
using UnityEditor;

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
            Undo.RecordObject(targetStateMachine, "Create State Node");

            StateSO newfile = CreateInstance<StateSO>();
            newfile.name = "New State";
            sn.stateSO = newfile;
            targetStateMachine.allStates.Add(newfile);
            targetStateMachine.UpdateStateMachine();

            AssetDatabase.AddObjectToAsset(sn.stateSO, targetStateMachine);

            Undo.RegisterCreatedObjectUndo(sn.stateSO, "Create State Node");

            EditorUtility.SetDirty(targetStateMachine);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(targetStateMachine);
        }

        return node;
    }

    public override void RemoveNode(BaseNode node)
    {
        if (node is StateNode sn && sn.stateSO != null)
        {
            if (targetStateMachine != null)
            {
                Undo.RecordObject(targetStateMachine, "Delete State Node");
                targetStateMachine.allStates.Remove(sn.stateSO);
                targetStateMachine.UpdateStateMachine();
            }

            Undo.DestroyObjectImmediate(sn.stateSO);

            if (targetStateMachine != null)
            {
                EditorUtility.SetDirty(targetStateMachine);
            }
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(targetStateMachine);
        }

        base.RemoveNode(node);
    }
}