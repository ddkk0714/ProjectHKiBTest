using UnityEngine;
using UnityEngine.U2D.Animation;

public class Damager : MonoBehaviour
{
    [SerializeField] private DamageDataSO _damageData;
    private IAttackable _attackable;
    [SerializeField] private SimpleAnimationPlayer[] _effectAnimationPlayer;
    [SerializeField] private SpriteLibrary[] _effectSpritelibrary;

    public void Start()
    {
        for (int i = 0; i < _effectAnimationPlayer.Length; i++)
            _effectAnimationPlayer[i].gameObject.SetActive(false);
    }

    public void SetAnimationData(SimpleAnimationDataSO effectAnimationData, SpriteLibraryAsset effectSpriteLibrary)
    {
        for (int i = 0; i < _effectAnimationPlayer.Length; i++)
        {
            _effectAnimationPlayer[i].gameObject.SetActive(false);

            if (effectAnimationData && effectSpriteLibrary)
            {
                _effectAnimationPlayer[i].animationData = effectAnimationData;
                _effectSpritelibrary[i].spriteLibraryAsset = effectSpriteLibrary;
            }
        }
    }

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
        gizmoRefPlayer = _damageData.animPlayerNumber;

        if (_damageData.initialSound)
            GameManager.instance.audioManager.PlayAudioOneShot(_damageData.initialSound, 1, transform.position);

        if (!_effectAnimationPlayer[_damageData.animPlayerNumber]) return;

        _effectAnimationPlayer[_damageData.animPlayerNumber].gameObject.SetActive(true);
        if (_damageData.effectAnimationClipName != "") _effectAnimationPlayer[_damageData.animPlayerNumber].Play(_damageData.effectAnimationClipName);

        EnumManager.AnimDir animDir = _effectAnimationPlayer[_damageData.animPlayerNumber].CurrentAnimDir;

        if (_damageData.DLRUDamageEffects.ContainsKey(animDir) && _damageData.DLRUDamageEffects[animDir])
            GameManager.instance.particleManager.PlayParticle(_damageData.DLRUDamageEffects[animDir].GetHashCode(), transform, _damageData.attatchParticleToBody);

        int colLength = Physics2D.OverlapBoxNonAlloc
        (
            transform.position + animDir.DirToQuaternion4() * _damageData.downwardDamageArea.offset,
            _damageData.downwardDamageArea.size,
            animDir.DirToAngle4(),
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

    public void StopEffect(int animPlayerNum)
    {
        // StopAttackEffectAction.EffectPlayerNumber는 State 에셋에 손으로 적는 값이라 배열보다
        // 클 수 있다. 원소 null만 보던 검사로는 범위 초과를 못 막는다.
        if (animPlayerNum < 0 || animPlayerNum >= _effectAnimationPlayer.Length) return;
        if (!_effectAnimationPlayer[animPlayerNum]) return;

        _effectAnimationPlayer[animPlayerNum].Stop();
    }

    private int gizmoRefPlayer;
    private int gizmoTrig;
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        if (gizmoTrig > 0)
        {
            Vector2 offset = _damageData.downwardDamageArea.offset;
            Vector2 size = _damageData.downwardDamageArea.size;

            offset = _effectAnimationPlayer[gizmoRefPlayer].CurrentAnimDir.DirToQuaternion4() * offset;
            size = _effectAnimationPlayer[gizmoRefPlayer].CurrentAnimDir.DirToQuaternion4() * size;

            Gizmos.DrawWireCube((Vector2)transform.position + offset, size);
            gizmoTrig--;
        }
    }
}
