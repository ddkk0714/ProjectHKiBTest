using UnityEngine;
using Assets.Scripts.Interfaces.Modules;

[RequireComponent(typeof(GetBuffEventModule))]
public class BuffEventTestModule : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform _defaultTarget;
    [SerializeField] private bool _useSelfIfTargetIsNull = true;

    [Header("Normal Buff")]
    [SerializeField] private KeyCode _buffKey = KeyCode.Space;
    [SerializeField] private StatBuffSO _buff;

    [Header("First Emotion")]
    [SerializeField] private KeyCode _firstKey = KeyCode.B;
    [SerializeField] private EmotionColor _firstColor = EmotionColor.SadnessBlue;
    [SerializeField] private int _firstStack = 1;

    [Header("Second Emotion")]
    [SerializeField] private KeyCode _secondKey = KeyCode.N;
    [SerializeField] private EmotionColor _secondColor = EmotionColor.SadnessSky;
    [SerializeField] private int _secondStack = 1;

    [Header("Apply Target")]
    [SerializeField] private EmotionModule.EmotionApplyTarget _applyTarget = EmotionModule.EmotionApplyTarget.Other;

    [Header("Option")]
    [SerializeField] private float _overrideDuration = -1f;
    [SerializeField] private bool _showLog = true;

    private GetBuffEventModule _getBuffEventModule;

    private void Awake()
    {
        _getBuffEventModule = GetComponent<GetBuffEventModule>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(_buffKey))
            ApplyNormalBuffToDefaultTarget();

        if (Input.GetKeyDown(_firstKey))
            ApplyEmotionToDefaultTarget(_firstColor, _firstStack);

        if (Input.GetKeyDown(_secondKey))
            ApplyEmotionToDefaultTarget(_secondColor, _secondStack);
    }

    private Transform GetDefaultTarget()
    {
        if (_defaultTarget != null)
            return _defaultTarget;

        if (_useSelfIfTargetIsNull)
            return transform;

        return null;
    }

    private void ApplyNormalBuffToDefaultTarget()
    {
        Transform target = GetDefaultTarget();

        if (target == null)
        {
            Debug.LogError("[BuffEventTestModule] Target이 null임.");
            return;
        }

        if (_getBuffEventModule == null)
        {
            Debug.LogError("[BuffEventTestModule] GetBuffEventModule을 찾지 못함.");
            return;
        }

        if (_buff == null)
        {
            Debug.LogError("[BuffEventTestModule] Buff가 비어 있음.");
            return;
        }

        _getBuffEventModule.GetBuff(target, _buff);

        if (_showLog)
            Debug.Log($"[BuffEventTestModule] {target.name} 에게 {_buff.name} 버프 적용");
    }

    private void ApplyEmotionToDefaultTarget(EmotionColor color, int stack)
    {
        Transform target = GetDefaultTarget();
        ApplyEmotion(target, color, stack);
    }

    public void ApplyEmotion(Transform target, EmotionColor color, int stack)
    {
        if (target == null)
        {
            Debug.LogError("[BuffEventTestModule] Target이 null임.");
            return;
        }

        EmotionModule emotionModule = FindEmotionModule(target);

        if (emotionModule == null)
        {
            Debug.LogError($"[BuffEventTestModule] {target.name} 에서 EmotionModule을 찾지 못함.");
            return;
        }

        emotionModule.ApplyColor(color, stack, _applyTarget, _overrideDuration);

        if (_showLog)
            Debug.Log($"[BuffEventTestModule] {target.name} 에게 {color} / {_applyTarget} {stack}스택 적용");
    }

    private EmotionModule FindEmotionModule(Transform target)
    {
        EmotionModule directModule = target.GetComponent<EmotionModule>();
        if (directModule != null)
            return directModule;

        EmotionModule parentModule = target.GetComponentInParent<EmotionModule>();
        if (parentModule != null)
            return parentModule;

        EmotionModule childModule = target.GetComponentInChildren<EmotionModule>();
        if (childModule != null)
            return childModule;

        if (target.TryGetComponent(out InterfaceRegister register) &&
            register.TryGetInterface(out IEmotion emotionInterface) &&
            emotionInterface is EmotionModule moduleFromRegister)
            return moduleFromRegister;

        InterfaceRegister parentRegister = target.GetComponentInParent<InterfaceRegister>();
        if (parentRegister != null &&
            parentRegister.TryGetInterface(out emotionInterface) &&
            emotionInterface is EmotionModule moduleFromParentRegister)
            return moduleFromParentRegister;

        InterfaceRegister childRegister = target.GetComponentInChildren<InterfaceRegister>();
        if (childRegister != null &&
            childRegister.TryGetInterface(out emotionInterface) &&
            emotionInterface is EmotionModule moduleFromChildRegister)
            return moduleFromChildRegister;

        return null;
    }
}