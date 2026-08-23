using System.Collections.Generic;
using UnityEngine;

namespace Combat
{
    public interface ICombatAttackModule : IInitializable
    {
        int StartAttack(
            CombatAttackDefinitionSO definition,
            CombatPositionReference origin,
            CombatPositionReference destination,
            string slot);
        bool RetargetLatest(string slot, CombatPositionReference destination);
        void CancelLatest(string slot);
        void CancelSlot(string slot);
        void CancelAll();
        bool IsRunning(string slot);
        bool Contains(string slot, Transform target, bool includeTelegraph = false);
        bool Contains(string slot, Vector3 worldPosition, bool includeTelegraph = false);
        bool HasHit(string slot, Transform target);
    }

    /// <summary>
    /// 공격자는 실행 요청과 조회만 담당한다. 실제 공격은 Scene root의 독립 인스턴스가 소유하므로
    /// 공격자 이동/비활성화와 공격 위치·수명·이동을 분리하고 여러 인스턴스를 동시에 유지한다.
    /// </summary>
    public sealed class CombatAttackModule : InterfaceModule, ICombatAttackModule
    {
        [SerializeField] private bool cancelAttacksOnDisable;

        private readonly Dictionary<int, CombatAttackInstance> _instances =
            new Dictionary<int, CombatAttackInstance>();
        private readonly Dictionary<string, List<int>> _slots =
            new Dictionary<string, List<int>>(System.StringComparer.Ordinal);

        private StateController _owner;
        private int _nextHandle = 1;
        private bool _initialized;

        public override void Register(IInterfaceRegistable interfaceRegistable)
        {
            interfaceRegistable.RegisterInterface<ICombatAttackModule>(this);
            _owner = interfaceRegistable as StateController;
        }

        public void Initialize()
        {
            if (_initialized) return;
            if (_owner == null) _owner = GetComponent<StateController>();
            _initialized = _owner != null;
            if (!_initialized)
                Debug.LogError($"{name}: CombatAttackModule requires a StateController on the same object.", this);
        }

        public int StartAttack(
            CombatAttackDefinitionSO definition,
            CombatPositionReference origin,
            CombatPositionReference destination,
            string slot)
        {
            EnsureInitialized();
            if (!_initialized || definition == null)
            {
                Debug.LogError($"{name}: Cannot start a composable attack without owner and definition.", this);
                return 0;
            }

            int handle = NextHandle();
            string normalizedSlot = NormalizeSlot(slot);
            GameObject runtimeObject = new GameObject($"Attack_{normalizedSlot}_{handle}");
            runtimeObject.transform.SetParent(null, true);
            CombatAttackInstance instance = runtimeObject.AddComponent<CombatAttackInstance>();

            _instances.Add(handle, instance);
            if (!_slots.TryGetValue(normalizedSlot, out List<int> handles))
            {
                handles = new List<int>();
                _slots.Add(normalizedSlot, handles);
            }
            handles.Add(handle);

            instance.Initialize(this, _owner, handle, normalizedSlot, definition, origin, destination);
            return handle;
        }

        public bool RetargetLatest(string slot, CombatPositionReference destination)
        {
            CombatAttackInstance instance = GetLatest(slot);
            if (instance == null) return false;
            instance.Retarget(destination);
            return true;
        }

        public void CancelLatest(string slot)
        {
            CombatAttackInstance instance = GetLatest(slot);
            if (instance != null) instance.Cancel();
        }

        public void CancelSlot(string slot)
        {
            string normalizedSlot = NormalizeSlot(slot);
            if (!_slots.TryGetValue(normalizedSlot, out List<int> handles)) return;

            int[] snapshot = handles.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                if (_instances.TryGetValue(snapshot[i], out CombatAttackInstance instance) && instance != null)
                    instance.Cancel();
        }

        public void CancelAll()
        {
            CombatAttackInstance[] snapshot = new CombatAttackInstance[_instances.Count];
            _instances.Values.CopyTo(snapshot, 0);
            for (int i = 0; i < snapshot.Length; i++)
                if (snapshot[i] != null) snapshot[i].Cancel();
        }

        public bool IsRunning(string slot)
        {
            return GetLatest(slot) != null;
        }

        public bool Contains(string slot, Transform target, bool includeTelegraph = false)
        {
            if (target == null) return false;
            return ForAnyInSlot(slot, attack => attack.Contains(target.position, includeTelegraph));
        }

        public bool Contains(string slot, Vector3 worldPosition, bool includeTelegraph = false)
        {
            return ForAnyInSlot(slot, attack => attack.Contains(worldPosition, includeTelegraph));
        }

        public bool HasHit(string slot, Transform target)
        {
            if (target == null) return false;
            return ForAnyInSlot(slot, attack => attack.HasHit(target));
        }

        internal void NotifyEnded(int handle, string slot)
        {
            _instances.Remove(handle);
            if (!_slots.TryGetValue(slot, out List<int> handles)) return;
            handles.Remove(handle);
            if (handles.Count == 0) _slots.Remove(slot);
        }

        private bool ForAnyInSlot(string slot, System.Predicate<CombatAttackInstance> predicate)
        {
            string normalizedSlot = NormalizeSlot(slot);
            if (!_slots.TryGetValue(normalizedSlot, out List<int> handles)) return false;
            for (int i = handles.Count - 1; i >= 0; i--)
            {
                if (!_instances.TryGetValue(handles[i], out CombatAttackInstance instance) || instance == null)
                    continue;
                if (predicate(instance)) return true;
            }
            return false;
        }

        private CombatAttackInstance GetLatest(string slot)
        {
            string normalizedSlot = NormalizeSlot(slot);
            if (!_slots.TryGetValue(normalizedSlot, out List<int> handles)) return null;
            for (int i = handles.Count - 1; i >= 0; i--)
                if (_instances.TryGetValue(handles[i], out CombatAttackInstance instance) && instance != null)
                    return instance;
            return null;
        }

        private void EnsureInitialized()
        {
            if (!_initialized) Initialize();
        }

        private int NextHandle()
        {
            if (_nextHandle == int.MaxValue) _nextHandle = 1;
            while (_instances.ContainsKey(_nextHandle)) _nextHandle++;
            return _nextHandle++;
        }

        private static string NormalizeSlot(string slot)
        {
            return string.IsNullOrWhiteSpace(slot) ? "Default" : slot.Trim();
        }

        private void OnDisable()
        {
            if (cancelAttacksOnDisable) CancelAll();
        }

        private void OnDestroy()
        {
            CancelAll();
        }
    }
}
