using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CameraManager : MonoBehaviour
{
    static public CameraManager instance;
    private void Awake()
    {
        instance = this;
    }

    [SerializeField] private CinemachineVirtualCamera[] Cameras = new CinemachineVirtualCamera[2];
    private CinemachineConfiner2D[] Confiners = new CinemachineConfiner2D[2];
    [SerializeField] private CinemachineBrain CBrain;
    public Camera theCamera;

    [SerializeField] private BGRenderer bgrenderer;

    private CinemachineImpulseSource impulseSource;
    public float OriginalRes = 5;
    private int CurrentCamera = 0; // 0 or 1
    public bool freeze = false;
    public Transform currentFollowTarget;

    // 컷신에서 카메라를 NPC 쪽으로 돌렸다가 되돌리기 위해, 원래 추적 대상을 기억해 둔다.
    private Transform _defaultFollowTarget;

    private void Start()
    {
        theCamera = GetComponent<Camera>();
        _defaultFollowTarget = currentFollowTarget;
        this.transform.position = currentFollowTarget.position;
        for (int i = 0; i < Cameras.Length; i++)
        {
            Confiners[i] = Cameras[i].GetComponent<CinemachineConfiner2D>();
        }
        Cameras[CurrentCamera].Priority = 11;
        ReturntoOrigRes(0);

        impulseSource = GetComponent<CinemachineImpulseSource>();
        UpdateConfiner(this.transform.position);
    }

    public void TogglePostProcessing(bool _enable) =>
    theCamera.GetUniversalAdditionalCameraData().renderPostProcessing = _enable;

    public void StrictMovement(Vector3 _targetPos, Vector3 _prevPos)
    {
        Vector3 way = _targetPos - _prevPos;
        for (int i = 0; i < Cameras.Length; i++)
        {
            Cameras[i].OnTargetObjectWarped(Cameras[i].Follow, way);
        }
        this.transform.position = _targetPos;

        UpdateConfiner(_targetPos);
    }

    private void UpdateConfiner(Vector3 _pos)
    {
        Collider2D other = Physics2D.OverlapCircle(_pos, 0.5f, LayerMask.GetMask("CameraBound"));
        if (other)
        {
            for (int i = 0; i < Cameras.Length; i++)
            {
                Confiners[i].m_BoundingShape2D = other;
                Confiners[i].m_MaxWindowSize = 0;
            }
        }
        else
        {
            for (int i = 0; i < Cameras.Length; i++)
            {
                Confiners[i].m_BoundingShape2D = null;
                Confiners[i].m_MaxWindowSize = 0;
            }
        }
    }

    // 0 to 1, 1 to 0
    private int FlipNum(int _i) => (_i + 1) % 2;

    // OWO
    public void Zoom(float _res, float _blendTime,
    CinemachineBlendDefinition.Style _style = CinemachineBlendDefinition.Style.EaseOut)
    {
        CurrentCamera = FlipNum(CurrentCamera);
        CBrain.m_DefaultBlend.m_Time = _blendTime;
        CBrain.m_DefaultBlend.m_Style = _style;
        Cameras[CurrentCamera].m_Lens.OrthographicSize = _res;

        Cameras[CurrentCamera].Priority = 11;
        Cameras[FlipNum(CurrentCamera)].Priority = 10;
        Confiners[CurrentCamera].m_MaxWindowSize = _res + 0.1f;
    }

    public void ZoomViaOrig(float _multiplyer, float _blendTime,
    CinemachineBlendDefinition.Style _style = CinemachineBlendDefinition.Style.EaseOut)
    {
        Zoom(OriginalRes * _multiplyer, _blendTime, _style);
    }

    // Set original resolution
    public void SetOrigRes(float _res) => OriginalRes = _res;


    // Set current resolution to original resolution
    public void ReturntoOrigRes(float _blendTime,
    CinemachineBlendDefinition.Style _style = CinemachineBlendDefinition.Style.EaseOut)
    {
        Zoom(OriginalRes, _blendTime, _style);
    }

    public void SetBound(AreaInfo _areaInfo)
    {
        for (int i = 0; i < Cameras.Length; i++)
        {
            Confiners[i].m_BoundingShape2D = _areaInfo.cameraBound;
        }
    }

    public void SetBG(AreaInfo _areaInfo)
    {
        bgrenderer.RenderBackGround(_areaInfo.backGround);
    }

    public void Shake()
    {
        impulseSource.GenerateImpulse();
    }

    /// <summary>세기를 지정해 흔든다. strength가 0 이하면 임펄스 소스에 설정된 기본 세기를 쓴다.</summary>
    public void Shake(Vector3 direction, float strength)
    {
        if (strength <= 0f || direction == Vector3.zero)
        {
            impulseSource.GenerateImpulse();
            return;
        }

        impulseSource.GenerateImpulse(direction.normalized * strength);
    }

    /// <summary>
    /// 카메라가 따라다닐 대상을 바꾼다 — 컷신에서 특정 NPC를 클로즈업할 때 쓴다.
    /// null을 넘기면 시작 시점의 기본 대상(보통 플레이어)으로 되돌린다.
    /// </summary>
    public void SetFollowTarget(Transform target)
    {
        if (target == null) target = _defaultFollowTarget;
        if (target == null) return;

        currentFollowTarget = target;
        for (int i = 0; i < Cameras.Length; i++)
            if (Cameras[i]) Cameras[i].Follow = target;
    }

    // 컷신 진입 전의 브레인 갱신 방식. 컷신이 끝나면 이 값으로 되돌린다.
    private CinemachineBrain.UpdateMethod _updateMethodBeforeCutscene;
    private bool _cutsceneCameraMode;

    /// <summary>
    /// 컷신용 카메라 모드. 게임플레이가 멈춘(Time.timeScale = 0) 동안에도 카메라 연출이 흐르게 한다.
    ///
    /// 두 가지를 같이 손봐야 한다:
    ///  1. m_IgnoreTimeScale — 블렌딩이 스케일 시간을 쓰면 timeScale 0에서 진행이 멎는다.
    ///  2. m_UpdateMethod — 기본값 SmartUpdate는 대상이 물리 오브젝트면 그 가상 카메라를
    ///     FixedUpdate에서 갱신하는데, timeScale이 0이면 FixedUpdate 자체가 호출되지 않는다.
    ///     그래서 OrthographicSize를 바꿔도 화면이 그대로였다(줌이 안 먹는 것처럼 보이던 원인).
    ///     컷신 동안에는 LateUpdate로 고정해 두고 끝나면 원래 값으로 되돌린다.
    ///
    /// (Cinemachine Impulse로 만드는 흔들림은 별도 경로라 이것만으로는 안 풀릴 수 있다.)
    /// </summary>
    public void SetCutsceneCameraMode(bool enabled)
    {
        if (!CBrain || _cutsceneCameraMode == enabled) return;
        _cutsceneCameraMode = enabled;

        if (enabled)
        {
            _updateMethodBeforeCutscene = CBrain.m_UpdateMethod;
            CBrain.m_UpdateMethod = CinemachineBrain.UpdateMethod.LateUpdate;
            CBrain.m_IgnoreTimeScale = true;
            return;
        }

        CBrain.m_UpdateMethod = _updateMethodBeforeCutscene;
        CBrain.m_IgnoreTimeScale = false;
    }

    public Vector3 GetCurrentCameraPos()
    {
        return Cameras[CurrentCamera].transform.position;
    }
}
