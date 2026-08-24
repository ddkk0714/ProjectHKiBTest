using UnityEngine;
using UnityEngine.U2D.Animation;

namespace Combat
{
    /// <summary>
    /// 독립 공격체 프리팹에서 기존 Damager의 이펙트 애니메이션 계약을 재현한다.
    /// IAttackable의 EffectAnimationData/EffectSpriteLibrary와 DamageData의 clip/index를 사용한다.
    /// </summary>
    public sealed class CombatAttackEffectPlayer : MonoBehaviour
    {
        [SerializeField] private SimpleAnimationPlayer[] effectAnimationPlayers;
        [SerializeField] private SpriteLibrary[] effectSpriteLibraries;

        private SimpleAnimationPlayer _currentPlayer;

        public void Play(IAttackable attacker, DamageDataSO damageData, EnumManager.AnimDir direction)
        {
            if (attacker == null || damageData == null || effectAnimationPlayers == null) return;

            int playerIndex = damageData.animPlayerNumber;
            if (playerIndex < 0 || playerIndex >= effectAnimationPlayers.Length)
            {
                Debug.LogError($"{name}: Effect animation player index {playerIndex} is out of range.", this);
                return;
            }

            for (int i = 0; i < effectAnimationPlayers.Length; i++)
            {
                SimpleAnimationPlayer player = effectAnimationPlayers[i];
                if (player == null) continue;

                player.gameObject.SetActive(false);
                player.playOnAwake = false;
                player.animationData = attacker.EffectAnimationData;
                if (effectSpriteLibraries != null && i < effectSpriteLibraries.Length &&
                    effectSpriteLibraries[i] != null && attacker.EffectSpriteLibrary != null)
                    effectSpriteLibraries[i].spriteLibraryAsset = attacker.EffectSpriteLibrary;
            }

            _currentPlayer = effectAnimationPlayers[playerIndex];
            if (_currentPlayer == null || attacker.EffectAnimationData == null ||
                attacker.EffectSpriteLibrary == null)
                return;

            _currentPlayer.gameObject.SetActive(true);
            _currentPlayer.Initialize();
            _currentPlayer.SetDirection(direction);
            if (!string.IsNullOrEmpty(damageData.effectAnimationClipName))
                _currentPlayer.Play(damageData.effectAnimationClipName);
        }

        public void SetDirection(EnumManager.AnimDir direction)
        {
            if (_currentPlayer != null) _currentPlayer.SetDirection(direction);
        }

        public void AlignToAttackRoot(Transform attackRoot)
        {
            if (attackRoot == null || TryGetComponent<CombatAreaVisual>(out _)) return;
            transform.SetPositionAndRotation(attackRoot.position, Quaternion.identity);
        }

        public void StopEffect()
        {
            if (_currentPlayer != null) _currentPlayer.Stop();
        }
    }
}
