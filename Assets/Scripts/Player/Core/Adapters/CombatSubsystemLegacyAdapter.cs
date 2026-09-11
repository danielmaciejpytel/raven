using System;
using Raven.Player.Core;
using UnityEngine;
using Zenject;

namespace Raven.Player.Core
{
    /// <summary>
    /// Integration layer between the new PlayerController subsystem architecture
    /// and the existing PlayerStatesManager.
    /// 
    /// This allows both systems to coexist during migration:
    /// - Old code continues to work
    /// - New code uses PlayerController
    /// - Gradually transition UI and systems
    /// 
    /// TODO: Remove this once PlayerStatesManager is fully migrated to CombatSubsystem
    /// </summary>
    public class CombatSubsystemLegacyAdapter : ICombatSubsystem, IDisposable
    {
        private readonly Player.PlayerStatesManager _legacyStatesManager;
        private string _currentState = "Normal";
        private bool _disposed = false;

        public string CurrentState => _currentState;

        public event Action OnShoot;
        public event Action<string> OnStateChanged;
        public event Action<string> OnAbilityUnlocked;

        [Inject]
        public CombatSubsystemLegacyAdapter(Player.PlayerStatesManager legacyStatesManager = null)
        {
            _legacyStatesManager = legacyStatesManager;

            if (_legacyStatesManager != null)
            {
                _legacyStatesManager.OnChangeState += HandleStateChanged;
                _legacyStatesManager.OnShoot += HandleShoot;
            }
        }

        public void Initialize()
        {
            Debug.Log("CombatSubsystemLegacyAdapter: Using legacy PlayerStatesManager");
        }

        public void Tick()
        {
            // Legacy manager handles its own ticking through Zenject
        }

        public void FixedTick()
        {
            // Legacy manager handles its own physics updates
        }

        public bool TryShoot()
        {
            OnShoot?.Invoke();
            return true;
        }

        public void ChangeState(string newState)
        {
            if (_legacyStatesManager == null)
                return;

            // Map new state names to legacy enum
            if (Enum.TryParse(newState, out Raven.Config.PlayerStateName legacyState))
            {
                _legacyStatesManager.ChangeState(legacyState);
            }
        }

        public bool IsAbilityUnlocked(string abilityName)
        {
            if (_legacyStatesManager == null)
                return false;

            return _legacyStatesManager.UnlockedStates.TryGetValue(
                (Player.CollectibleName)Enum.Parse(typeof(Player.CollectibleName), abilityName),
                out var unlocked) && unlocked;
        }

        public void UnlockAbility(string abilityName)
        {
            if (_legacyStatesManager == null)
                return;

            if (Enum.TryParse(abilityName, out Player.CollectibleName collectibleName))
            {
                if (!_legacyStatesManager.UnlockedStates.ContainsKey(collectibleName))
                {
                    _legacyStatesManager.UnlockedStates[collectibleName] = true;
                    OnAbilityUnlocked?.Invoke(abilityName);
                }
            }
        }

        private void HandleStateChanged(Raven.Config.PlayerStateName newState)
        {
            _currentState = newState.ToString();
            OnStateChanged?.Invoke(_currentState);
        }

        private void HandleShoot()
        {
            OnShoot?.Invoke();
        }

        public void Cleanup()
        {
            if (_legacyStatesManager != null && !_disposed)
            {
                _legacyStatesManager.OnChangeState -= HandleStateChanged;
                _legacyStatesManager.OnShoot -= HandleShoot;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Cleanup();
                _disposed = true;
            }
        }
    }
}
