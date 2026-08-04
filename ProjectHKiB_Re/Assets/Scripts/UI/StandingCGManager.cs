using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class StandingCGManager : MonoBehaviour
{
    [Serializable]
    public class GravityElement
    {
        public Transform transform;
        public float minLocalAngle;
        public float maxLocalAngle;
        public bool isLeft;
        public bool isRight;
        public bool isHairPart;
        public Tween tween;
        [HideInInspector] public float currentTargetAngle;
    }
    private Camera _camera;
    public Animator bodyAnimator;
    public FaceController faceController;
    public Color originalColor;
    public Color disabledColor;
    public Color damagedColor;
    public Vector3 hideLocalPosition;
    public Vector3 originalLocallPosition;
    [HideInInspector] public bool talkEnabled;
    [HideInInspector] public bool isHidden;

    public bool applyGravity = true;
    public float defaultImpactStrength = 5;
    public float wind = 0;
    public bool trackMouseMode = false;
    public float followHeadDistance;
    public float dontChangeHeadAngle;
    public float stopFollowingTicks;
    public float stopFollowingRadius;
    [NaughtyAttributes.Button()] public void ImpactRight() => ImpactRight(defaultImpactStrength, false);
    [NaughtyAttributes.Button()] public void ImpactLeft() => ImpactLeft(defaultImpactStrength, false);
    [NaughtyAttributes.Button()] public void ImpactUp() => ImpactUp(defaultImpactStrength, false);
    [NaughtyAttributes.Button()] public void ImpactDown() => ImpactDown(defaultImpactStrength, false);

    private GravityPart[] gravityParts;
    private CursorFollower[] cursorFollowers;

    public GameObject head_F;
    public GameObject head_D;
    public GameObject head_U;
    public GameObject head_R;
    public GameObject head_L;

    private GameObject currentHead;
    private Tween headTween;
    private int stopFollowingCounter;
    private Vector2 prevMousePos;
    private bool trackMouseRest;

    public void Awake()
    {
        gravityParts = GetComponentsInChildren<GravityPart>(true);
        cursorFollowers = GetComponentsInChildren<CursorFollower>(true);
        _camera = FindObjectOfType<Camera>();
        currentHead = head_F;
    }

    public void PlayBodyAnimation(string name)
    {
        if (bodyAnimator) bodyAnimator.Play(name);
    }

    public void PlayFaceAnimation(string name)
    {
        if (faceController) faceController.PlayAnimation(name);
    }

    private const string UPLAYER = "UpStandingCG";
    private const string DOWNLAYER = "DownStandingCG";
    public void SetSortingLayerUp()
    {
        var canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].sortingLayerName = UPLAYER;
        }
    }

    public void SetSortingLayerDown()
    {
        var canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].sortingLayerName = DOWNLAYER;
        }
    }

    [NaughtyAttributes.Button()]
    public void Hide() => Move(hideLocalPosition);

    [NaughtyAttributes.Button()]
    public void Move() => Move(Vector3.zero);
    // SetUpdate(true): 스탠딩 CG는 대화 연출용 UI이므로 TimeManager가 게임을 멈춘
    // 동안에도 계속 움직여야 한다. (이 파일의 다른 트윈들도 같은 이유)
    public void Move(Vector3 localPosition) => transform.DOLocalMove(localPosition, 0.36f).SetUpdate(true);
    public void MoveToOriginalPosition() => Move(originalLocallPosition);

    [NaughtyAttributes.Button()]
    public void SetTalkDisabled()
    {
        SetSortingLayerDown();
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            images[i].DOColor(disabledColor, 0.36f).SetUpdate(true);
        }
        talkEnabled = false;
    }
    [NaughtyAttributes.Button()]
    public void SetTalkEnabled()
    {
        SetSortingLayerUp();
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            images[i].DOColor(originalColor, 0.36f).SetUpdate(true);
        }
        talkEnabled = true;
    }
    [NaughtyAttributes.Button()]
    public void Damage()
    {
        ImpactUp();
        Image[] images = GetComponentsInChildren<Image>();
        for (int i = 0; i < images.Length; i++)
        {
            images[i].color = damagedColor;
        }
        if (talkEnabled) SetTalkEnabled();
        else SetTalkDisabled();
    }

    private void Update()
    {
        if (trackMouseMode)
        {
            if (Vector2.Distance(prevMousePos, Input.mousePosition) < stopFollowingRadius)
            {
                if (!trackMouseRest)
                {
                    stopFollowingCounter++;
                    if (stopFollowingCounter > stopFollowingTicks)
                    {
                        stopFollowingCounter = 0;
                        trackMouseRest = true;
                        EndFollowMouse();
                    }
                }
            }
            else
            {
                stopFollowingCounter = 0;
                prevMousePos = Input.mousePosition;
                trackMouseRest = false;
            }

            if (!trackMouseRest)
                FollowMouse();
        }
        if (applyGravity)
            ApplyGravity();
    }

    public void ApplyGravity()
    {
        for (int i = 0; i < gravityParts.Length; i++)
        {
            GravityPart part = gravityParts[i];
            Vector3 currentLocalAngle = part.transform.localEulerAngles;
            if (currentLocalAngle.z < -180f) currentLocalAngle.z += 360;
            if (currentLocalAngle.z > 180f) currentLocalAngle.z -= 360;
            if (currentLocalAngle.z < part.minLocalAngle)
                part.transform.localEulerAngles = Vector3.forward * part.minLocalAngle;
            if (currentLocalAngle.z > part.maxLocalAngle)
                part.transform.localEulerAngles = Vector3.forward * part.maxLocalAngle;


            Vector3 targetLocalAngle = -part.transform.parent.eulerAngles + Vector3.forward * wind;
            if (targetLocalAngle.z < -180f) targetLocalAngle.z += 360;
            if (targetLocalAngle.z > 180f) targetLocalAngle.z -= 360;
            float clampedZ = Mathf.Clamp(targetLocalAngle.z, part.minLocalAngle, part.maxLocalAngle);
            //if (Mathf.Abs(element.transform.localEulerAngles.z - clampedZ) < 0.1f) continue;
            if (Mathf.Abs(part.currentTargetAngle - clampedZ) < 0.1f)
                continue;

            part.currentTargetAngle = clampedZ;
            targetLocalAngle = Vector3.forward * clampedZ;

            part.tween?.Kill();
            part.tween = part.transform.DOLocalRotate(targetLocalAngle, 0.36f).SetEase(Ease.OutBack).SetUpdate(true);
        }
    }
    public void ImpactHairRight(float strength) => ImpactRight(strength, true);
    public void ImpactRight(float strength, bool onlyHairPart)
    {
        if (strength < 0) return;
        for (int i = 0; i < gravityParts.Length; i++)
        {
            GravityPart part = gravityParts[i];
            if (onlyHairPart && !part.isHairPart) continue;

            Vector3 targetLocalAngle = part.transform.localEulerAngles + Vector3.forward * strength;
            if (targetLocalAngle.z < -180f) targetLocalAngle.z += 360;
            if (targetLocalAngle.z > 180f) targetLocalAngle.z -= 360;
            float clampedZ = Mathf.Clamp(targetLocalAngle.z, part.minLocalAngle, part.maxLocalAngle);

            part.currentTargetAngle = clampedZ;
            targetLocalAngle = Vector3.forward * clampedZ;
            part.transform.localEulerAngles = targetLocalAngle;
        }
    }
    public void ImpactHairLeft(float strength) => ImpactLeft(strength, true);
    public void ImpactLeft(float strength, bool onlyHairPart)
    {
        if (strength < 0) return;
        for (int i = 0; i < gravityParts.Length; i++)
        {
            GravityPart part = gravityParts[i];
            if (onlyHairPart && !part.isHairPart) continue;

            Vector3 targetLocalAngle = part.transform.localEulerAngles - Vector3.forward * strength;
            if (targetLocalAngle.z < -180f) targetLocalAngle.z += 360;
            if (targetLocalAngle.z > 180f) targetLocalAngle.z -= 360;
            float clampedZ = Mathf.Clamp(targetLocalAngle.z, part.minLocalAngle, part.maxLocalAngle);

            part.currentTargetAngle = clampedZ;
            targetLocalAngle = Vector3.forward * clampedZ;
            part.transform.localEulerAngles = targetLocalAngle;
        }
    }
    public void ImpactHairUp(float strength) => ImpactUp(strength, true);
    public void ImpactUp(float strength, bool onlyHairPart)
    {
        if (strength < 0) return;
        for (int i = 0; i < gravityParts.Length; i++)
        {
            GravityPart part = gravityParts[i];
            if (onlyHairPart && !part.isHairPart) continue;

            Vector3 targetLocalAngle = part.transform.localEulerAngles + (part.isLeft ? -1 : part.isRight ? 1 : 0) * strength * Vector3.forward;
            if (targetLocalAngle.z < -180f) targetLocalAngle.z += 360;
            if (targetLocalAngle.z > 180f) targetLocalAngle.z -= 360;
            float clampedZ = Mathf.Clamp(targetLocalAngle.z, part.minLocalAngle, part.maxLocalAngle);

            part.currentTargetAngle = clampedZ;
            targetLocalAngle = Vector3.forward * clampedZ;
            part.transform.localEulerAngles = targetLocalAngle;
        }
    }
    public void ImpactHairDown(float strength) => ImpactDown(strength, true);
    public void ImpactDown(float strength, bool onlyHairPart)
    {
        if (strength < 0) return;
        for (int i = 0; i < gravityParts.Length; i++)
        {
            GravityPart part = gravityParts[i];
            if (onlyHairPart && !part.isHairPart) continue;

            Vector3 targetLocalAngle = part.transform.localEulerAngles - (part.isLeft ? -1 : part.isRight ? 1 : 0) * strength * Vector3.forward;
            if (targetLocalAngle.z < -180f) targetLocalAngle.z += 360;
            if (targetLocalAngle.z > 180f) targetLocalAngle.z -= 360;
            float clampedZ = Mathf.Clamp(targetLocalAngle.z, part.minLocalAngle, part.maxLocalAngle);

            part.currentTargetAngle = clampedZ;
            targetLocalAngle = Vector3.forward * clampedZ;
            part.transform.localEulerAngles = targetLocalAngle;
        }
    }



    public void FollowMouse()
    {
        Vector2 vector = _camera.ScreenPointToRay(Input.mousePosition).origin;
        for (int i = 0; i < cursorFollowers.Length; i++)
        {
            cursorFollowers[i].Follow(vector);
        }
        if (Vector2.Distance(vector, cursorFollowers[0].transform.position) > followHeadDistance)
        {
            Vector2 dir = vector - (Vector2)cursorFollowers[0].transform.position;

            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y) + dontChangeHeadAngle)
            {
                if (dir.x < 0) HeadLookRight();
                if (dir.x > 0) HeadLookLeft();
            }
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x) + dontChangeHeadAngle)
            {
                if (dir.y < 0) HeadLookDown();
                if (dir.y > 0) HeadLookUp();
            }
        }
        else HeadLookFront();
    }
    public void EndFollowMouse()
    {
        HeadLookFront();
        for (int i = 0; i < cursorFollowers.Length; i++)
        {
            cursorFollowers[i].ResetFollow();
        }
    }
    [NaughtyAttributes.Button()]
    public void HeadLookFront()
    {
        if (currentHead == head_F) return;
        if (currentHead == head_R) { ImpactHairLeft(10); head_F.transform.localPosition = Vector2.left; }
        else if (currentHead == head_L) { ImpactHairRight(10); head_F.transform.localPosition = Vector2.right; }
        else if (currentHead == head_U) { ImpactHairDown(10); head_F.transform.localPosition = Vector2.up; }
        else if (currentHead == head_D) { ImpactHairUp(10); head_F.transform.localPosition = Vector2.down; }

        DisableAllHeads();
        head_F.SetActive(true);
        currentHead = head_F;
        headTween?.Complete();
        headTween = head_F.transform.DOLocalMove(Vector2.zero, 0.1f).SetUpdate(true);
        headTween.Play();
    }
    [NaughtyAttributes.Button()]
    public void HeadLookDown()
    {
        if (currentHead == head_D) return;
        if (currentHead == head_R) ImpactHairLeft(10);
        else if (currentHead == head_L) ImpactHairRight(10);
        else ImpactHairDown(10);
        head_D.transform.localPosition = Vector2.zero;

        DisableAllHeads();
        head_D.SetActive(true);
        currentHead = head_D;
        headTween?.Complete();
        headTween = head_D.transform.DOLocalMove(Vector2.down, 0.1f).SetUpdate(true);
        headTween.Play();
    }
    [NaughtyAttributes.Button()]
    public void HeadLookUp()
    {
        if (currentHead == head_U) return;
        if (currentHead == head_R) ImpactHairLeft(10);
        else if (currentHead == head_L) ImpactHairRight(10);
        else ImpactHairUp(10);
        head_U.transform.localPosition = Vector2.zero;

        DisableAllHeads();
        head_U.SetActive(true);
        currentHead = head_U;
        headTween?.Complete();
        headTween = head_U.transform.DOLocalMove(Vector2.up, 0.1f).SetUpdate(true);
        headTween.Play();
    }
    [NaughtyAttributes.Button()]
    public void HeadLookRight()
    {
        if (currentHead == head_R) return;
        ImpactHairRight(10);
        head_R.transform.localPosition = Vector2.zero;

        DisableAllHeads();
        head_R.SetActive(true);
        currentHead = head_R;
        headTween?.Complete();
        headTween = head_R.transform.DOLocalMove(Vector2.left, 0.1f).SetUpdate(true);
        headTween.Play();
    }
    [NaughtyAttributes.Button()]
    public void HeadLookLeft()
    {
        if (currentHead == head_L) return;
        ImpactHairLeft(10);
        head_L.transform.localPosition = Vector2.zero;

        DisableAllHeads();
        head_L.SetActive(true);
        currentHead = head_L;
        headTween?.Complete();
        headTween = head_L.transform.DOLocalMove(Vector2.right, 0.1f).SetUpdate(true);
        headTween.Play();
    }
    public void DisableAllHeads()
    {
        head_F.SetActive(false);
        head_D.SetActive(false);
        head_U.SetActive(false);
        head_R.SetActive(false);
        head_L.SetActive(false);
    }
}
