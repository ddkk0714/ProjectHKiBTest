using System;
using UnityEngine;

public class DirAnimatableModule : AnimatableModule, IDirAnimatable
{
    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IDirAnimatable>(this);
    }

    public MathManagerSO mathManager;
    public EnumManager.AnimDir AnimationDirectionInternal { get; set; }
    public EnumManager.AnimDir AnimationDirection
    {
        get => AnimationDirectionInternal;
        private set
        {
            if (AnimationDirectionInternal != value)
            {
                AnimationDirectionInternal = value;
                OnDirChanged?.Invoke(value);
            }
        }
    }

    public Vector2 LastSetAnimationDir8 { get; set; }

    public Vector2 LastSetAnimationDir4 { get; set; }

    public Quaternion LastSetAnimationQuaternion4
    {
        get
        {
            if (LastSetAnimationDir8 == Vector2.zero) return Quaternion.identity;
            return AnimationDirection switch
            {
                EnumManager.AnimDir.D => Quaternion.identity,
                EnumManager.AnimDir.L => Quaternion.Euler(0, 0, -90),
                EnumManager.AnimDir.R => Quaternion.Euler(0, 0, 90),
                EnumManager.AnimDir.U => Quaternion.Euler(0, 0, 180),
                _ => Quaternion.identity,
            };
        }
    }

    public float LastSetAnimationAngle4
    {
        get
        {
            if (LastSetAnimationDir8 == Vector2.zero) return 0;
            return AnimationDirection switch
            {
                EnumManager.AnimDir.D => 0,
                EnumManager.AnimDir.L => -90,
                EnumManager.AnimDir.R => 90,
                EnumManager.AnimDir.U => 180,
                _ => 0,
            };
        }
    }

    public Action<EnumManager.AnimDir> OnDirChanged { get; set; }

    public override void Initialize()
    {
        base.Initialize();
        if (LastSetAnimationDir4 == Vector2.zero) LastSetAnimationDir4 = Vector2.down;
        if (LastSetAnimationDir8 == Vector2.zero) LastSetAnimationDir8 = Vector2.down;
    }

    public Vector2 GetAnimationRestrictedDirection(Vector3 input)
    {
        if (input.Equals(Vector3.zero)) return Vector2.zero;
        switch (AnimationDirection)
        {
            case EnumManager.AnimDir.D:
                if (input.y > 0) return Vector2.zero;
                if (input.x < -MathManagerSO.tan225) return Vector2.down + Vector2.left;
                if (input.x > MathManagerSO.tan225) return Vector2.down + Vector2.right;
                return input;

            case EnumManager.AnimDir.L:
                if (input.x > 0) return Vector2.zero;
                if (input.y < -MathManagerSO.tan225) return Vector2.left + Vector2.down;
                if (input.y > MathManagerSO.tan225) return Vector2.left + Vector2.up;
                return input;

            case EnumManager.AnimDir.R:
                if (input.x < 0) return Vector2.zero;
                if (input.y < -MathManagerSO.tan225) return Vector2.right + Vector2.down;
                if (input.y > MathManagerSO.tan225) return Vector2.right + Vector2.up;
                return input;

            case EnumManager.AnimDir.U:
                if (input.y < 0) return Vector2.zero;
                if (input.x < -MathManagerSO.tan225) return Vector2.up + Vector2.left;
                if (input.x > MathManagerSO.tan225) return Vector2.up + Vector2.right;
                return input;

            default: return Vector2.zero;
        }
    }
    /*
        public bool CheckIfLastSetDirectionSame(Vector2 input)
        => mathManager.SetDirection8One(input).Equals(_lastSetAnimationDir);

        public void SetAnimationDirection(Vector2 vectorDir, bool maintainProgress = false)
        {
            if (vectorDir.Equals(Vector2.zero)) return;
            vectorDir = vectorDir.normalized;
            if (mathManager.Absolute(vectorDir.y) > mathManager.Absolute(vectorDir.x))
                SetAnimationDirectionInternal(vectorDir.y > 0 ? EnumManager.AnimDir.U : EnumManager.AnimDir.D, maintainProgress);
            else
                SetAnimationDirectionInternal(vectorDir.x > 0 ? EnumManager.AnimDir.R : EnumManager.AnimDir.L, maintainProgress);
            _lastSetAnimationDir = mathManager.SetDirection8One(vectorDir);
        }

        public void SetAnimationDirection(EnumManager.AnimDir animDir, bool maintainProgress = false)
        {
            SetAnimationDirectionInternal(animDir, maintainProgress);
            _lastSetAnimationDir = AnimationDirection switch
            {
                EnumManager.AnimDir.D => Vector2.down,
                EnumManager.AnimDir.L => Vector2.left,
                EnumManager.AnimDir.R => Vector2.right,
                EnumManager.AnimDir.U => Vector2.up,
                _ => Vector2.zero,
            };
        }

        private void SetAnimationDirectionInternal(EnumManager.AnimDir animDir, bool maintainProgress)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(CurrentAnimation + animDir))
                return;
            AnimationDirection = animDir;
            if (maintainProgress)
            {
                float time = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                animator.Play(CurrentAnimation + animDir, 0, time + 0.01f);
                return;
            }
            animator.Play(CurrentAnimation + animDir);
        }
    */


    public bool CheckIfLastSetDirectionSame(Vector2 input)
   => mathManager.SetDirection8One(input).Equals(LastSetAnimationDir8);

    public void SetAnimationDirection(Vector2 vectorDir)
    {
        if (vectorDir == Vector2.zero) return;
        vectorDir = mathManager.SetDirection8One(vectorDir);
        if (vectorDir == LastSetAnimationDir8) return;

        LastSetAnimationDir8 = vectorDir;
        vectorDir.y = (vectorDir.x != 0 && vectorDir.y != 0) ? 0 : vectorDir.y;
        LastSetAnimationDir4 = vectorDir;

        AnimationDirection = LastSetAnimationDir4.y < 0 ? EnumManager.AnimDir.D :
                             LastSetAnimationDir4.x < 0 ? EnumManager.AnimDir.L :
                             LastSetAnimationDir4.x > 0 ? EnumManager.AnimDir.R :
                             LastSetAnimationDir4.y > 0 ? EnumManager.AnimDir.U :
                             AnimationDirection;
        AnimationPlayer.SetDirection(AnimationDirection);
    }

    public void SetAnimationDirection(EnumManager.AnimDir animDir)
    {
        Vector2 dir = animDir switch
        {
            EnumManager.AnimDir.D => Vector2.down,
            EnumManager.AnimDir.L => Vector2.left,
            EnumManager.AnimDir.R => Vector2.right,
            EnumManager.AnimDir.U => Vector2.up,
            _ => Vector2.zero,
        };

        LastSetAnimationDir4 = dir;
        LastSetAnimationDir8 = dir;
        if (dir == Vector2.zero) return;
        AnimationPlayer.SetDirection(animDir);
        AnimationDirection = animDir;
    }
}