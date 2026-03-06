using System.Collections;
using UnityEngine;

public class Damager : MonoBehaviour
{
    [SerializeField] private DamageDataSO _damageData;
    private IAttackable _attackable;
    [SerializeField] private DirAnimatableModule _animationController;

    public void SetAttackable(IAttackable attackable)
    => _attackable = attackable;

    public void SetDamageData(IAttackable attackable, int attackNum, int damageNum)
    {
        SetAttackable(attackable);
        SetDamageData(attackNum, damageNum);
    }

    public void SetDamageData(int attackNum, int damageNum)
    {
        if (_attackable.AttackDatas.Equals(null))
        {
            Debug.LogError("ERROR: AttackDatas is missing!!!"); return;
        }
        if (_attackable.AttackDatas.Length - 1 < attackNum)
        {
            Debug.LogError("ERROR: AttackData[" + attackNum + "] is missing!!!"); return;
        }
        if (_attackable.AttackDatas[attackNum].damageDatas.Length - 1 < damageNum)
        {
            Debug.LogError("ERROR: DamageData[" + damageNum + "] is missing!!!"); return;
        }

        SetDamageData(_attackable.AttackDatas[attackNum].damageDatas[damageNum]);
    }

    public void SetDamageData(DamageDataSO damageData)
    {
        _damageData = damageData;
    }

    private readonly Collider2D[] col = new Collider2D[72];
    public void Damage()
    {
        gizmoTrig = 5;
        if (_damageData.initialSound)
            GameManager.instance.audioManager.PlayAudioOneShot(_damageData.initialSound, 1, transform.position);

        if (!_animationController) return;
        if (_damageData.DLRUDamageEffects.ContainsKey(_animationController.AnimationDirection) && _damageData.DLRUDamageEffects[_animationController.AnimationDirection])
            GameManager.instance.particleManager.PlayParticle(_damageData.DLRUDamageEffects[_animationController.AnimationDirection].GetHashCode(), transform, _damageData.attatchParticleToBody);

        int colLength = Physics2D.OverlapBoxNonAlloc
        (
            transform.position + _animationController.LastSetAnimationQuaternion4 * _damageData.downwardDamageArea.offset,
            _damageData.downwardDamageArea.size,
            _animationController.LastSetAnimationAngle4,
            col,
            _damageData.damageLayer
        );

        for (int i = 0; i < colLength; i++)
        {
            if (col[i].TryGetComponent(out IDamagable component))
            {
                component.Damage(_damageData, _attackable, _damageData.downwardDamageArea.pivot + this.transform.position);
            }
        }
    }

    private int gizmoTrig;
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        if (gizmoTrig > 0)
        {
            Vector2 offset = _damageData.downwardDamageArea.offset;
            Vector2 size = _damageData.downwardDamageArea.size;

            offset = _animationController.LastSetAnimationQuaternion4 * offset;
            size = _animationController.LastSetAnimationQuaternion4 * size;

            Gizmos.DrawWireCube((Vector2)transform.position + offset, size);
            gizmoTrig--;
        }
    }
}
