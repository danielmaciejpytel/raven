using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages player combat states, weapons, and abilities.
    /// Coordinates with stats and movement subsystems.
    /// TODO: Integrate with existing PlayerStatesManager
    /// </summary>
    public class CombatSubsystem : ICombatSubsystem, IDisposable
    {
        private string _currentState = "Normal";
        private Dictionary<string, bool> _unlockedAbilities = new();
        private bool _canShoot = true;

        public string CurrentState => _currentState;

        public event Action OnShoot;
        public event Action<string> OnStateChanged;
        public event Action<string> OnAbilityUnlocked;

        [Inject]
        public CombatSubsystem()
        {
            // Initialize default abilities
            _unlockedAbilities["Dash"] = false;
            _unlockedAbilities["Fire"] = false;
            _unlockedAbilities["SecondWeapon"] = false;
        }

        public void Initialize()
        {
            _canShoot = true;
        }

        public void Tick()
        {
            // Combat state updates
        }

        public void FixedTick()
        {
            // Physics-based combat updates
        }

        public bool TryShoot()
        {
            if (!_canShoot)
                return false;

            OnShoot?.Invoke();
            return true;
        }

        public void ChangeState(string newState)
        {
            if (_currentState == newState)
                return;

            _currentState = newState;
            OnStateChanged?.Invoke(_currentState);
        }

        public bool IsAbilityUnlocked(string abilityName)
        {
            return _unlockedAbilities.TryGetValue(abilityName, out var unlocked) && unlocked;
        }

        public void UnlockAbility(string abilityName)
        {
            if (!_unlockedAbilities.ContainsKey(abilityName))
            {
                _unlockedAbilities[abilityName] = true;
            }
            else if (!_unlockedAbilities[abilityName])
            {
                _unlockedAbilities[abilityName] = true;
                OnAbilityUnlocked?.Invoke(abilityName);
            }
        }

        public void Cleanup()
        {
            _canShoot = false;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
