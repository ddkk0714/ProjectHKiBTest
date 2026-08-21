using UnityEngine;

[CreateAssetMenu(fileName = "DamageManager", menuName = "Scriptable Objects/Manager/DamageManager", order = 3)]
public class DamageManagerSO : ScriptableObject
{
    public void Damage(DamageDataSO damageData, IAttackable hitter, IDamagable getHit, Vector3 hitPos, bool IsKnockback)
    {
        int value = 0;
        bool isCritical = Random.value < hitter.CriticalChanceRate;

        if (!getHit.Invincible)
        {
            float resistance = Mathf.Clamp(getHit.Resistance, 0f, 100f);

            // 제곱형 피해율
            float resistanceCoeff = Mathf.Pow((100f - resistance) / 100f, 2f);

            value = (int)
            (
                (
                    damageData.damageCoefficient
                    * hitter.ATK
                    * (1f + (isCritical ? hitter.CriticalDamageRate : 0f))
                    * resistanceCoeff
                )
                - getHit.DEF
            );

            if (value <= 0) value = 1;

            getHit.HP -= value;
        }

        if (!getHit.Invincible || IsKnockback)
        {
            // 타격이 성립한 순간의 소리는 두 겹이다. hitSound는 무기(공격)마다 다른 "닿는" 소리고,
            // getHit.HitSound는 맞는 쪽 종류마다 다른 소리다. 둘 다 겹쳐 울리는 게 정상.
            // (휘두르는 소리는 명중 여부와 무관해야 하므로 여기가 아니라 Damager.Damage()에 있다.)
            if (damageData.hitSound)
                GameManager.instance.audioManager.PlayAudioOneShot(damageData.hitSound, 1, hitPos);
            if (getHit.HitSound)
                GameManager.instance.audioManager.PlayAudioOneShot(getHit.HitSound, 1, hitPos);

            GameManager.instance.damageParticleManager.PlayHitParticle
            (
                hitter.DamageParticle,
                value,
                value > getHit.MaxHP * 0.5f || IsKnockback,
                isCritical,
                hitPos,
                hitter.DamageIndicatorRandomPosInfo
            );
        }
    }
}