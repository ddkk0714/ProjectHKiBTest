using System;
using System.Collections.Generic;
using UnityEngine;

namespace StateMachine
{
    public interface ITargetControl : IInitializable
    {
        StateController InstantiateAndRegister(
            string slot,
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            bool replaceExisting,
            bool destroyReplacedOwnedTarget);

        bool RegisterTarget(
            string slot,
            StateController target,
            bool replaceExisting = true,
            bool destroyReplacedOwnedTarget = true);

        bool TryGetTarget(string slot, out StateController target);
        bool UnregisterTarget(string slot);
        bool DestroyTarget(string slot, bool destroyRegisteredSceneObject = false);
        void DestroyAllOwnedTargets();
    }

    [Serializable]
    public sealed class SceneTargetRegistration
    {
        [SerializeField] private string slot = "Default";
        [SerializeField] private StateController target;

        public string Slot => slot;
        public StateController Target => target;
    }

    /// <summary>
    /// 한 StateController가 생성하거나 Scene에서 참조한 다른 StateController를 슬롯별로 관리한다.
    /// 생성한 대상과 Scene 대상을 구분하여 기본 파괴 동작이 Scene 오브젝트를 제거하지 않게 한다.
    /// </summary>
    public sealed class TargetControlModule : InterfaceModule, ITargetControl
    {
        private sealed class TargetEntry
        {
            public StateController Controller;
            public GameObject DestructionRoot;
            public bool Owned;
        }

        [Tooltip("Scene에 미리 배치된 StateController를 게임 시작 시 슬롯에 등록한다.")]
        [SerializeField]
        private SceneTargetRegistration[] sceneTargets =
            Array.Empty<SceneTargetRegistration>();

        [Tooltip("소유자가 비활성화될 때 이 모듈이 생성한 대상들을 파괴한다.")]
        [SerializeField] private bool destroyOwnedTargetsOnDisable = true;

        private readonly Dictionary<string, TargetEntry> _targets = new();
        private bool _initialized;

        public override void Register(IInterfaceRegistable interfaceRegistable)
        {
            interfaceRegistable.RegisterInterface<ITargetControl>(this);
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterConfiguredSceneTargets();
        }

        public StateController InstantiateAndRegister(
            string slot,
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            bool replaceExisting,
            bool destroyReplacedOwnedTarget)
        {
            EnsureInitialized();
            if (prefab == null)
            {
                Debug.LogError("ERROR: ControlledTargetModule - 생성할 Prefab이 비어 있습니다.", this);
                return null;
            }

            string normalizedSlot = NormalizeSlot(slot);
            if (_targets.ContainsKey(normalizedSlot) && !replaceExisting)
            {
                Debug.LogWarning($"[ControlledTargetModule] '{normalizedSlot}' 슬롯이 이미 사용 중이므로 생성하지 않았습니다.", this);
                return null;
            }

            GameObject instance = Instantiate(prefab, position, rotation, parent);
            StateController controller = instance.GetComponent<StateController>();
            if (controller == null)
                controller = instance.GetComponentInChildren<StateController>(true);

            if (controller == null)
            {
                Debug.LogError($"ERROR: ControlledTargetModule - Prefab '{prefab.name}'에서 StateController를 찾을 수 없습니다.", instance);
                Destroy(instance);
                return null;
            }

            if (!RegisterCore(
                    normalizedSlot,
                    controller,
                    instance,
                    true,
                    replaceExisting,
                    destroyReplacedOwnedTarget))
            {
                Destroy(instance);
                return null;
            }

            return controller;
        }

        public bool RegisterTarget(
            string slot,
            StateController target,
            bool replaceExisting = true,
            bool destroyReplacedOwnedTarget = true)
        {
            EnsureInitialized();
            return RegisterCore(
                NormalizeSlot(slot),
                target,
                target != null ? target.gameObject : null,
                false,
                replaceExisting,
                destroyReplacedOwnedTarget);
        }

        public bool TryGetTarget(string slot, out StateController target)
        {
            EnsureInitialized();
            string normalizedSlot = NormalizeSlot(slot);
            if (_targets.TryGetValue(normalizedSlot, out TargetEntry entry) &&
                entry.Controller != null)
            {
                target = entry.Controller;
                return true;
            }

            _targets.Remove(normalizedSlot);
            target = null;
            return false;
        }

        public bool UnregisterTarget(string slot)
        {
            EnsureInitialized();
            return _targets.Remove(NormalizeSlot(slot));
        }

        public bool DestroyTarget(string slot, bool destroyRegisteredSceneObject = false)
        {
            EnsureInitialized();
            string normalizedSlot = NormalizeSlot(slot);
            if (!_targets.TryGetValue(normalizedSlot, out TargetEntry entry)) return false;

            _targets.Remove(normalizedSlot);
            if ((entry.Owned || destroyRegisteredSceneObject) && entry.DestructionRoot != null)
                Destroy(entry.DestructionRoot);
            return true;
        }

        public void DestroyAllOwnedTargets()
        {
            EnsureInitialized();
            List<string> ownedSlots = new();
            foreach (KeyValuePair<string, TargetEntry> pair in _targets)
            {
                if (!pair.Value.Owned) continue;
                if (pair.Value.DestructionRoot != null) Destroy(pair.Value.DestructionRoot);
                ownedSlots.Add(pair.Key);
            }

            for (int i = 0; i < ownedSlots.Count; i++)
                _targets.Remove(ownedSlots[i]);
        }

        private bool RegisterCore(
            string slot,
            StateController target,
            GameObject destructionRoot,
            bool owned,
            bool replaceExisting,
            bool destroyReplacedOwnedTarget)
        {
            if (target == null)
            {
                Debug.LogError($"ERROR: ControlledTargetModule - '{slot}'에 등록할 StateController가 비어 있습니다.", this);
                return false;
            }

            if (_targets.TryGetValue(slot, out TargetEntry previous))
            {
                if (!replaceExisting) return false;
                _targets.Remove(slot);
                if (destroyReplacedOwnedTarget && previous.Owned && previous.DestructionRoot != null)
                    Destroy(previous.DestructionRoot);
            }

            _targets[slot] = new TargetEntry
            {
                Controller = target,
                DestructionRoot = destructionRoot,
                Owned = owned
            };
            return true;
        }

        private void RegisterConfiguredSceneTargets()
        {
            if (sceneTargets == null) return;
            for (int i = 0; i < sceneTargets.Length; i++)
            {
                SceneTargetRegistration registration = sceneTargets[i];
                if (registration == null || registration.Target == null) continue;

                string slot = NormalizeSlot(registration.Slot);
                if (_targets.ContainsKey(slot))
                    Debug.LogWarning($"[ControlledTargetModule] Scene Target의 '{slot}' 슬롯이 중복되어 마지막 참조로 교체됩니다.", this);

                RegisterCore(
                    slot,
                    registration.Target,
                    registration.Target.gameObject,
                    false,
                    true,
                    false);
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized) Initialize();
        }

        private static string NormalizeSlot(string slot)
        {
            return string.IsNullOrWhiteSpace(slot) ? "Default" : slot.Trim();
        }

        private void OnDisable()
        {
            if (!destroyOwnedTargetsOnDisable) return;
            DestroyAllOwnedTargets();
            _targets.Clear();
            _initialized = false;
        }

        private void OnDestroy()
        {
            if (!_initialized) return;
            DestroyAllOwnedTargets();
            _targets.Clear();
        }
    }
}
