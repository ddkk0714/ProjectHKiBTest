using System;
using UnityEngine;

namespace StateMachine
{
    /// <summary>BranchAction이 분기에 사용할 정수 값을 제공한다.</summary>
    [Serializable]
    public abstract class BranchIntSource
    {
        public abstract bool TryGetValue(StateController stateController, out int value);

        protected static bool TryGetStateMachineValue(
            StateController variableOwner,
            string variableName,
            string sourceName,
            StateController logContext,
            out int value)
        {
            value = 0;
            if (variableOwner == null) return false;

            if (string.IsNullOrWhiteSpace(variableName))
            {
                Debug.LogError($"ERROR: BranchAction - {sourceName}의 변수 이름이 비어 있습니다.", logContext);
                return false;
            }

            if (variableOwner.customVariables == null || variableOwner.customVariables.intVariables == null)
            {
                Debug.LogError(
                    $"ERROR: BranchAction - {sourceName}에 int 변수 저장소가 준비되지 않았습니다.",
                    logContext);
                return false;
            }

            // 기존 StateMachine 정수 접근 규칙을 따른다. 선언되지 않은 이름은
            // StateController.GetIntParameter가 경고와 함께 0으로 생성한다.
            value = variableOwner.GetIntParameter(variableName);
            return true;
        }
    }

    /// <summary>Action을 실행한 StateController의 StateMachine 정수를 읽는다.</summary>
    [AddTypeMenu("StateMachine Variable")]
    [Serializable]
    public sealed class StateMachineBranchIntSource : BranchIntSource
    {
        [Tooltip("실행 주체의 StateMachine에 정의된 int 변수 이름.")]
        [SerializeField] private string variableName;

        public override bool TryGetValue(StateController stateController, out int value)
        {
            return TryGetStateMachineValue(
                stateController,
                variableName,
                "StateMachine",
                stateController,
                out value);
        }
    }

    /// <summary>현재 EventManager에서 실행 중인 Event StateMachine의 정수를 읽는다.</summary>
    [AddTypeMenu("Event Variable")]
    [Serializable]
    public sealed class EventBranchIntSource : BranchIntSource
    {
        [Tooltip("현재 Event의 StateMachine에 정의된 int 변수 이름.")]
        [SerializeField] private string variableName;

        public override bool TryGetValue(StateController stateController, out int value)
        {
            EventManager eventManager = GameManager.instance != null
                ? GameManager.instance.eventManager
                : null;

            if (eventManager == null)
            {
                Debug.LogError("ERROR: BranchAction - Event 변수를 읽을 EventManager가 없습니다.", stateController);
                value = 0;
                return false;
            }

            return TryGetStateMachineValue(
                eventManager,
                variableName,
                "Event",
                stateController,
                out value);
        }
    }

    /// <summary>BranchAction이 실행될 때마다 지정한 범위에서 새 정수를 뽑는다.</summary>
    [AddTypeMenu("Random")]
    [Serializable]
    public sealed class RandomBranchIntSource : BranchIntSource
    {
        [Tooltip("랜덤 범위에 포함되는 최솟값.")]
        [SerializeField] private int minimumInclusive;

        [Tooltip("랜덤 범위에 포함되지 않는 최댓값. 예: 0~3을 뽑으려면 4로 설정.")]
        [SerializeField] private int maximumExclusive = 2;

        public override bool TryGetValue(StateController stateController, out int value)
        {
            if (maximumExclusive <= minimumInclusive)
            {
                Debug.LogError(
                    $"ERROR: BranchAction - Random 범위가 올바르지 않습니다. " +
                    $"Minimum({minimumInclusive})은 Maximum Exclusive({maximumExclusive})보다 작아야 합니다.",
                    stateController);
                value = 0;
                return false;
            }

            value = UnityEngine.Random.Range(minimumInclusive, maximumExclusive);
            return true;
        }
    }

    /// <summary>특정 정수 값과 그 값일 때 실행할 Action의 대응 관계.</summary>
    [Serializable]
    public sealed class IntBranch
    {
        [SerializeField] private int value;
        [SerializeReference, SubclassSelector] private StateAction action;

        public int Value => value;
        public StateAction Action => action;
    }

    /// <summary>
    /// 선택한 정수 공급원의 값을 읽고, 처음으로 일치한 분기의 Action 하나만 실행한다.
    /// 일치하는 값이 없으면 선택적인 Default Action을 실행한다.
    /// </summary>
    [AddTypeMenu("General/Branch By Int")]
    [Serializable]
    public sealed class BranchAction : StateAction
    {
        [Tooltip("분기에 사용할 정수의 출처를 선택한다.")]
        [SerializeReference, SubclassSelector] private BranchIntSource valueSource;

        [Tooltip("정수 값별로 실행할 Action. 중복 값이 있으면 위쪽의 첫 분기만 실행된다.")]
        [SerializeField] private IntBranch[] branches = Array.Empty<IntBranch>();

        [Tooltip("어떤 값에도 대응하지 않을 때 실행한다. 비워두면 아무것도 실행하지 않는다.")]
        [SerializeReference, SubclassSelector] private StateAction defaultAction;

        public override void Act(StateController stateController)
        {
            if (valueSource == null)
            {
                Debug.LogError("ERROR: BranchAction - Value Source가 설정되지 않았습니다.", stateController);
                return;
            }

            if (!valueSource.TryGetValue(stateController, out int value)) return;

            if (branches != null)
            {
                for (int i = 0; i < branches.Length; i++)
                {
                    IntBranch branch = branches[i];
                    if (branch == null || branch.Value != value) continue;

                    branch.Action?.Act(stateController);
                    return;
                }
            }

            defaultAction?.Act(stateController);
        }
    }
}
