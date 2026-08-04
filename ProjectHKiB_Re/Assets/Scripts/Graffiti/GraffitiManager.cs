using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

public class GraffitiManager : MonoBehaviour
{
    public Player player;
    [SerializeField] private List<Vector2Int> graffitiProgress = new();
    private Vector2 _graffitiWorldStartPos;
    private Vector2 GraffitiWorldPos => _graffitiWorldStartPos + graffitiCurrentIntPos;
    private Vector2Int graffitiCurrentIntPos;
    private int graffitiMoveCount;
    private bool canGraffitiTinker;
    private bool biggerTinkerPresent;
    public GearManager gearManager;
    public InputManager inputManager;
    private Timer _graffitiTimer = new();
    public float graffitiMaxTime = 6;
    public SimpleAnimationPlayer[] tinkers;

    private Timer _GPRecoverTimer = new();
    public float GPRecovertime = 10;

    public int MaxGP = 5;
    [SerializeField] private int _GP;
    private int _currentTargetSlot;
    public int GP
    {
        get => _GP;
        set
        {
            _GP = value;
            if (_GP >= MaxGP && !_GPRecoverTimer.IsCooltimeEnded) _GPRecoverTimer.CancelTimer();
            if (_GP > MaxGP) _GP = MaxGP;
            else
            {
                if (_GP < 0) _GP = 0;
                if (_GPRecoverTimer.IsCooltimeEnded) StartGPRecoverTimer();
            }
        }
    }

    public bool CanGraffiti => GP > 0;

    public void Start()
    {
        Initialize();
    }
    public void OnDestroy()
    {
        UnBindInputs();
    }

    public void Initialize()
    {
        _GP = MaxGP;
        StartGPRecoverTimer();
        BindInputs();
    }
    public void RecoverGP() => GP++;
    public void StartGPRecoverTimer()
    {
        if (_GP == MaxGP || !_GPRecoverTimer.IsCooltimeEnded) return;
        _GPRecoverTimer.CancelTimer();
        _GPRecoverTimer.StartTimer(GPRecovertime, RecoverGP);
    }

    public void StartGraffiti(int targetSlot, Vector2 startPos)
    {
        if (!CanGraffiti) return;
        if (GameManager.instance.gearManager.GetCardData(targetSlot) == null) return;
        _currentTargetSlot = targetSlot;

        inputManager.GRAFFITIMode();
        _graffitiWorldStartPos = startPos;
        graffitiProgress.Clear();
        _graffitiTimer.StartTimer(graffitiMaxTime, TimeOutGraffiti);
        GP--;
        graffitiMoveCount = 0;
        canGraffitiTinker = false;
        biggerTinkerPresent = false;

        ProcessGraffiti(Vector2Int.zero);
    }
    public void ResetGraffiti()
    {
        CancelAllTinkerAnimation();
        graffitiProgress.Clear();
        graffitiMoveCount = 0;

        ProcessGraffiti(Vector2Int.zero);
    }
    public void TimeOutGraffiti() => ExitGraffiti(_currentTargetSlot);

    /// <summary>
    /// called everytime when moved in graffiti
    /// </summary>
    /// <param name="pos"> current position</param>
    public void ProcessGraffiti(Vector2Int pos)
    {
        graffitiCurrentIntPos = pos;

        if (!graffitiProgress.Contains(pos))
        {
            graffitiProgress.Add(pos);
            PlayNormalTinkerAnimation(graffitiMoveCount, GraffitiWorldPos);
            graffitiMoveCount++;
        }
        else PlayNormalTinkerAnimation(graffitiProgress.IndexOf(pos), GraffitiWorldPos);

        if (CheckCompleted(_currentTargetSlot) >= 0)
        {
            PlayBiggerTinkerAnimation();//success feedback
            return;
        }

        if (!ValidateProgress(_currentTargetSlot))
        {
            CancelBiggerTinkerAnimation();//fail feedback

            //graffitiProgress.Clear();
        }
    }

    public void ExitGraffiti(int targetSlot)
    {
        StartTinker();
        _graffitiTimer.CancelTimer();

        if (sequence != null && sequence.active) sequence.Complete();

        bool comp = CheckCompleted(targetSlot) >= 0;
        for (int i = 0; i < graffitiMoveCount; i++)
        {
            tinkers[i].ClearReservation();
            if (comp)
            {
                tinkers[i].Reserve("BiggerStart");
                tinkers[i].Reserve("BiggerIdle");
                tinkers[i].Reserve("BiggerExit");
            }
            else tinkers[i].Reserve("NormalExit");
            tinkers[i].Reserve("Stop");
        }

        if (comp) gearManager.ActivateGear(targetSlot);
        else player.ChangeState(player.BaseData.StateMachine.initialState);

        graffitiProgress.Clear();
        inputManager.PLAYMode();

        if (CheckCompleted(targetSlot) == 1) StartCoroutine(GraffitiEndSkillCoroutine());
        else if (CheckCompleted(targetSlot) == 0) StartCoroutine(GraffitiEndAttackCoroutine());
    }

    private IEnumerator GraffitiEndAttackCoroutine()
    {
        yield return null;
        if (player.BaseData.GraffitiAttackState != null)
            player.ChangeState(player.BaseData.GraffitiAttackState);
    }
    private IEnumerator GraffitiEndSkillCoroutine()
    {
        GP = 0;
        yield return null;
        if (player.BaseData.GraffitiSkillState != null)
            player.ChangeState(player.BaseData.GraffitiSkillState);
    }

    private bool ValidateProgress(int targetSlot)
    {
        GearDataSO gear = gearManager.GetGearData(targetSlot);
        if (!gear || gear == gearManager.DefaultGearData) return false;
        for (int i = 0; i < gear.graffitiAllCases.Count; i++)
        {
            List<Vector2Int> graffitiCode = gear.graffitiAllCases[i].code;
            if (graffitiCode.Intersect(graffitiProgress).ToList().Count == graffitiProgress.Count)
                return true;
        }

        return false;
    }

    private int CheckCompleted(int targetSlot) // -1 = error, 0 = normal/failed, 1 = skill/completed
    {
        GearDataSO gear = gearManager.GetGearData(targetSlot);
        if (!gear || gear == gearManager.DefaultGearData) return -1;
        for (int i = 0; i < gear.graffitiAllCases.Count; i++)
        {
            List<Vector2Int> graffitiCode = gear.graffitiAllCases[i].code;
            if (graffitiCode.Count == graffitiProgress.Count
                && graffitiCode.OrderBy(x => x.x).ThenBy(y => y.y).SequenceEqual(graffitiProgress.OrderBy(x => x.x).ThenBy(y => y.y)))
                return 1;
        }
        return 0;
    }

    #region Animation

    public void StartTinker()
    {
        if (canGraffitiTinker) return;
        canGraffitiTinker = true;
        for (int i = 0; i < graffitiMoveCount; i++)
            PlayNormalTinkerAnimation(i, _graffitiWorldStartPos + graffitiProgress[i]);
        if (CheckCompleted(_currentTargetSlot) >= 0)
            PlayBiggerTinkerAnimation();
    }
    public void PlayNormalTinkerAnimation(int animatorIndex, Vector2 pos)
    {
        if (canGraffitiTinker && animatorIndex < tinkers.Length && tinkers[animatorIndex])
        {
            tinkers[animatorIndex].transform.position = pos;
            tinkers[animatorIndex].Play("NormalStart");
            tinkers[animatorIndex].Reserve("NormalIdle");
        }
    }

    private Sequence sequence;
    private int tempTinkerIndex;
    public void PlayBiggerTinkerAnimation()
    {
        if (!canGraffitiTinker) return;
        // SetUpdate(true): 그래피티 입력 UI 연출이므로 TimeManager 일시정지의 영향을 받지 않는다.
        sequence = DOTween.Sequence().SetUpdate(true);
        for (int i = 0; i < graffitiMoveCount; i++)
        {
            sequence.AppendCallback(BiggerTinkerCallback);
            sequence.AppendInterval(0.05f);
        }
        tempTinkerIndex = 0;
        sequence.Play();
        biggerTinkerPresent = true;
    }
    private void BiggerTinkerCallback()
    {
        tinkers[tempTinkerIndex].Play("NormalExit");
        tinkers[tempTinkerIndex].Reserve("BiggerStart");
        tinkers[tempTinkerIndex].Reserve("BiggerIdle");
        tempTinkerIndex++;
    }

    public void CancelBiggerTinkerAnimation()
    {
        if (!canGraffitiTinker || !biggerTinkerPresent) return;
        for (int i = 0; i < graffitiMoveCount; i++)
        {
            tinkers[i].Play("BiggerExit");
            tinkers[i].Reserve("NormalStart");
            tinkers[i].Reserve("NormalIdle");
        }
        biggerTinkerPresent = false;
    }

    public void CancelAllTinkerAnimation()
    {
        if (!canGraffitiTinker) return;
        for (int i = 0; i < graffitiMoveCount; i++)
        {
            tinkers[i].Play("NormalExit");
            tinkers[i].Reserve("Stop");
        }
    }

    #endregion

    #region Binding
    private void BindInputs()
    {
        inputManager.inputs.GRAFFITI.MovePressedD.performed += ProcessGraffitiDown;
        inputManager.inputs.GRAFFITI.MovePressedD.performed += ProcessGraffitiDown;
        inputManager.inputs.GRAFFITI.MovePressedL.performed += ProcessGraffitiLeft;
        inputManager.inputs.GRAFFITI.MovePressedR.performed += ProcessGraffitiRight;
        inputManager.inputs.GRAFFITI.MovePressedU.performed += ProcessGraffitiUp;
        inputManager.inputs.GRAFFITI.Graffiti1.canceled += EndGraffiti1;
        inputManager.inputs.GRAFFITI.Graffiti2.canceled += EndGraffiti2;
        inputManager.inputs.GRAFFITI.Graffiti3.canceled += EndGraffiti3;
        inputManager.inputs.GRAFFITI.Graffiti4.canceled += EndGraffiti4;
        inputManager.inputs.GRAFFITI.Graffiti5.canceled += EndGraffiti5;
    }

    private void UnBindInputs()
    {
        inputManager.inputs.GRAFFITI.MovePressedD.performed -= ProcessGraffitiDown;
        inputManager.inputs.GRAFFITI.MovePressedL.performed -= ProcessGraffitiLeft;
        inputManager.inputs.GRAFFITI.MovePressedR.performed -= ProcessGraffitiRight;
        inputManager.inputs.GRAFFITI.MovePressedU.performed -= ProcessGraffitiUp;
        inputManager.inputs.GRAFFITI.Graffiti1.canceled -= EndGraffiti1;
        inputManager.inputs.GRAFFITI.Graffiti2.canceled -= EndGraffiti2;
        inputManager.inputs.GRAFFITI.Graffiti3.canceled -= EndGraffiti3;
        inputManager.inputs.GRAFFITI.Graffiti4.canceled -= EndGraffiti4;
        inputManager.inputs.GRAFFITI.Graffiti5.canceled -= EndGraffiti5;
    }

    public void ProcessGraffitiDown(InputAction.CallbackContext context) { if (context.performed) ProcessGraffiti(graffitiCurrentIntPos + Vector2Int.down); }
    public void ProcessGraffitiLeft(InputAction.CallbackContext context) { if (context.performed) ProcessGraffiti(graffitiCurrentIntPos + Vector2Int.left); }
    public void ProcessGraffitiRight(InputAction.CallbackContext context) { if (context.performed) ProcessGraffiti(graffitiCurrentIntPos + Vector2Int.right); }
    public void ProcessGraffitiUp(InputAction.CallbackContext context) { if (context.performed) ProcessGraffiti(graffitiCurrentIntPos + Vector2Int.up); }
    public void EndGraffiti1(InputAction.CallbackContext context) { if (context.canceled) ExitGraffiti(0); }
    public void EndGraffiti2(InputAction.CallbackContext context) { if (context.canceled) ExitGraffiti(1); }
    public void EndGraffiti3(InputAction.CallbackContext context) { if (context.canceled) ExitGraffiti(2); }
    public void EndGraffiti4(InputAction.CallbackContext context) { if (context.canceled) ExitGraffiti(3); }
    public void EndGraffiti5(InputAction.CallbackContext context) { if (context.canceled) ExitGraffiti(4); }

    #endregion
}
