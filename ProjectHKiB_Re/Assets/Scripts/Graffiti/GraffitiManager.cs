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
    private bool _isGraffitiActive;
    private int _graffitiSession;
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

    // graffitiMoveCount는 방문한 칸 수만큼 상한 없이 늘어나지만 tinkers는 고정 길이다.
    // 경계 검사가 있는 곳은 PlayNormalTinkerAnimation 하나뿐이었다.
    private int TinkerCount => Mathf.Min(graffitiMoveCount, tinkers.Length);

    public void StartGraffiti(int targetSlot, Vector2 startPos)
    {
        if (!CanGraffiti || GameManager.instance.gearManager.GetCardData(targetSlot) == null)
        {
            AbortGraffiti();
            return;
        }

        int session = ++_graffitiSession;
        _isGraffitiActive = true;
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
        StartCoroutine(ExitWhenGraffitiSlotIsReleased(targetSlot, session));
        LogControlState("변신 시작", targetSlot);
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
    /// 낙서가 시작되지 못했을 때 변신 상태에서 빠져나온다.
    ///
    /// StartGraffiti는 State의 EnterActions에서 불린다(StartGraffitiAction). 여기서 조기 반환하면
    /// GRAFFITIMode()가 실행되지 않아 GRAFFITI 입력 맵이 꺼진 채로 남고, 낙서 타이머도 돌지 않는다.
    /// 그런데 TransformStart/Transforming을 빠져나가는 길은 (a) GraffitiManager가 ChangeState로
    /// 꺼내주거나 (b) OnGraffitiCancel 전이뿐인데, 그 전이는 trigger가 비어 있어 죽어 있고 애초에
    /// 꺼진 입력 맵에 속한다. 되돌리지 않으면 플레이어는 변신 도중에 영구히 멈춘다.
    /// </summary>
    private void AbortGraffiti()
    {
        _isGraffitiActive = false;
        _graffitiSession++;
        StartCoroutine(AbortGraffitiCoroutine());
    }

    private IEnumerator AbortGraffitiCoroutine()
    {
        // EnterState가 끝난 다음에 상태를 바꾼다. 도중에 바꾸면 떠나는 State의 ReserveTransitions가
        // 새 State 위에 덮여서 엉뚱한 전이 조건이 켜진다.
        yield return null;

        inputManager.PLAYMode();
        ChangeToInitialStateOfCurrentMachine();
    }

    private void ChangeToInitialStateOfCurrentMachine()
    {
        if (player.StateMachine != null && player.StateMachine.initialState)
            player.ChangeState(player.StateMachine.initialState);
    }

    // 변신 후 조작 불능을 재현할 때, 실제로 입력 맵이 남았는지 또는 상태 머신이
    // 예상과 다른 상태에 있는지를 Player.log/Console 한 줄로 확인하기 위한 상태 기록이다.
    private void LogControlState(string phase, int targetSlot)
    {
        bool playEnabled = inputManager != null && inputManager.inputs != null && inputManager.inputs.PLAY.enabled;
        bool graffitiEnabled = inputManager != null && inputManager.inputs != null && inputManager.inputs.GRAFFITI.enabled;
        string stateMachineName = player != null && player.StateMachine != null ? player.StateMachine.name : "(없음)";
        string stateName = player != null && player.CurrentState != null ? player.CurrentState.name : "(없음)";

        Debug.Log($"[GraffitiManager] {phase} (slot {targetSlot}) — PLAY={playEnabled}, GRAFFITI={graffitiEnabled}, " +
                  $"StateMachine={stateMachineName}, CurrentState={stateName}, TimeScale={Time.timeScale}");
    }

    /// <summary>
    /// 변신을 시작한 숫자 키는 PLAY 맵에서 눌린 뒤 곧바로 GRAFFITI 맵으로 넘어간다.
    /// 액션 맵 전환 시점에 이미 눌려 있던 버튼은 canceled 콜백을 놓칠 수 있으므로,
    /// 실제 버튼이 떼어진 것도 확인해 낙서 모드가 영구히 남지 않게 한다.
    /// </summary>
    private IEnumerator ExitWhenGraffitiSlotIsReleased(int targetSlot, int session)
    {
        // 입력 맵이 전환되고 현재 입력 상태가 동기화될 때까지 한 프레임 기다린다.
        yield return null;

        InputAction slotAction = targetSlot switch
        {
            0 => inputManager.inputs.GRAFFITI.Graffiti1,
            1 => inputManager.inputs.GRAFFITI.Graffiti2,
            2 => inputManager.inputs.GRAFFITI.Graffiti3,
            3 => inputManager.inputs.GRAFFITI.Graffiti4,
            4 => inputManager.inputs.GRAFFITI.Graffiti5,
            _ => null,
        };

        if (slotAction == null) yield break;

        while (_isGraffitiActive && session == _graffitiSession && slotAction.IsPressed())
            yield return null;

        if (_isGraffitiActive && session == _graffitiSession)
            ExitGraffiti(targetSlot);
    }

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
        // canceled 이벤트, 타임아웃, 위의 보조 코루틴이 같은 프레임에 겹쳐도
        // 종료 처리는 한 번만 실행해야 한다.
        if (!_isGraffitiActive) return;

        _isGraffitiActive = false;
        _graffitiSession++;
        _graffitiTimer.CancelTimer();

        int result = CheckCompleted(targetSlot);
        bool comp = result >= 0;

        // 팅커 연출은 실패해도 되지만 상태 복구는 반드시 해야 한다. 변신 상태에는 동작하는 탈출
        // 전이가 없어서(AbortGraffiti 주석 참고) 아래가 한 번 건너뛰어지면 되돌아올 길이 없다.
        try
        {
            StartTinker();

            if (sequence != null && sequence.active) sequence.Complete();

            for (int i = 0; i < TinkerCount; i++)
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
        }
        finally
        {
            // 기어 활성화 도중 예외가 나더라도, 낙서 모드에 남아 이동/공격 입력이 영구히
            // 막히면 안 된다. 먼저 PLAY 입력을 복구한 뒤 상태 머신을 바꾼다.
            graffitiProgress.Clear();
            inputManager.PLAYMode();

            try
            {
                if (comp) gearManager.ActivateGear(targetSlot);
            }
            catch (System.Exception exception)
            {
                // 활성화 실패 시에도 아래의 초기 상태 복구로 Transforming 상태를 빠져나간다.
                Debug.LogError($"[GraffitiManager] 기어 활성화에 실패했습니다. 변신을 취소하고 기본 상태로 복구합니다.\n{exception}");
            }

            // ActivateGear는 새 기어 상태 머신의 초기 상태를 진입시킨다. 혹시 기어 데이터의
            // startStateName이 실제 상태 이름과 달라 전환이 실패하더라도, 현재 머신의 초기
            // 상태(Idle)로 한 번 더 확정해 Transforming에 남지 않게 한다.
            ChangeToInitialStateOfCurrentMachine();
            LogControlState("변신 종료 복구", targetSlot);

            if (result == 1) StartCoroutine(GraffitiEndSkillCoroutine());
            else if (result == 0) StartCoroutine(GraffitiEndAttackCoroutine());
        }
    }

    /// <summary>
    /// PlayerData가 물고 있는 State는 원본 .asset이고, 새 형식 기계는 그것의 복제본을 서브에셋으로
    /// 갖고 있다. 원본 객체를 그대로 ChangeState에 넘기면 이 기계에 없는 State가 CurrentState가 되어
    /// StateMachineSO._commandPairs의 conditionState와 아무것도 맞지 않는다 - 입력 전이가 전부 죽어
    /// 캐릭터가 멈춘 것처럼 보인다. 이름은 변환해도 그대로라, 지금 기계 안에서 같은 이름으로 찾는다.
    /// (서브에셋 fileID는 재변환마다 바뀌므로 PlayerData가 직접 가리키게 두면 안 된다.)
    /// </summary>
    private void ChangeToStateInCurrentMachine(StateSO stateFromBaseData)
    {
        if (stateFromBaseData == null) return;

        if (player.StateMachine != null && player.StateMachine.allStates.Exists(a => a.name == stateFromBaseData.name))
            player.ChangeState(stateFromBaseData.name);
        else
            Debug.LogWarning($"[GraffitiManager] 지금 기계에 '{stateFromBaseData.name}'이 없어 상태를 바꾸지 못했습니다.");
    }

    private IEnumerator GraffitiEndAttackCoroutine()
    {
        yield return null;
        ChangeToStateInCurrentMachine(player.BaseData.GraffitiAttackState);
    }
    private IEnumerator GraffitiEndSkillCoroutine()
    {
        GP = 0;
        yield return null;
        ChangeToStateInCurrentMachine(player.BaseData.GraffitiSkillState);
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
        for (int i = 0; i < TinkerCount; i++)
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
        for (int i = 0; i < TinkerCount; i++)
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
        if (tempTinkerIndex >= tinkers.Length) return;

        tinkers[tempTinkerIndex].Play("NormalExit");
        tinkers[tempTinkerIndex].Reserve("BiggerStart");
        tinkers[tempTinkerIndex].Reserve("BiggerIdle");
        tempTinkerIndex++;
    }

    public void CancelBiggerTinkerAnimation()
    {
        if (!canGraffitiTinker || !biggerTinkerPresent) return;
        for (int i = 0; i < TinkerCount; i++)
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
        for (int i = 0; i < TinkerCount; i++)
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
