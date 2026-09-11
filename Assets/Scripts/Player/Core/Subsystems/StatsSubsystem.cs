using System;
using Raven.Config;
using UnityEngine;
using Zenject;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages player statistics: health, energy, buffs, and status effects.
    /// </summary>
    public class StatsSubsystem : IStatsSubsystem, IDisposable
    {
        private readonly PlayerDataConfig _config;
        private float _currentHealth;
        private float _currentEnergy;
        private float _energyRegenerationTimer = 0f;
        private float _startEnergyRegenerationTimer = 0f;
        private bool _regeneratingEnergy = false;

        public float CurrentHealth => _currentHealth;
        public float CurrentEnergy => _currentEnergy;
        public float MaxHealth => _config.MaxHealthValue;
        public float MaxEnergy => _config.MaxEnergyValue;
        public bool IsAlive => _currentHealth > 0;

        public event Action<float> OnHealthChanged;
        public event Action<float> OnEnergyChanged;
        public event Action OnDeath;

        [Inject]
        public StatsSubsystem(PlayerDataConfig config)
        {
            _config = config;
            _currentHealth = _config.MaxHealthValue;
            _currentEnergy = _config.MaxEnergyValue;
        }

        public void Initialize()
        {
            // Any initialization needed
        }

        public void Tick()
        {
            UpdateEnergyRegeneration();
        }

        public void FixedTick()
        {
            // Physics-based stat updates would go here
        }

        public void TakeDamage(float amount)
        {
            if (!IsAlive)
                return;

            _currentHealth = Mathf.Max(0, _currentHealth - amount);
            OnHealthChanged?.Invoke(_currentHealth);

            if (!IsAlive)
            {
                OnDeath?.Invoke();
            }
        }

        public void Heal(float amount)
        {
            if (!IsAlive)
                return;

            _currentHealth = Mathf.Min(MaxHealth, _currentHealth + amount);
            OnHealthChanged?.Invoke(_currentHealth);
        }

        public bool TryConsumeEnergy(float amount)
        {
            if (_currentEnergy < amount)
                return false;

            _currentEnergy -= amount;
            _startEnergyRegenerationTimer = 0;
            _regeneratingEnergy = false;
            OnEnergyChanged?.Invoke(_currentEnergy);
            return true;
        }

        public void RegenerateEnergy(float amount)
        {
            _currentEnergy = Mathf.Min(MaxEnergy, _currentEnergy + amount);
            OnEnergyChanged?.Invoke(_currentEnergy);
        }

        private void UpdateEnergyRegeneration()
        {
            if (!_regeneratingEnergy)
            {
                _startEnergyRegenerationTimer += Time.deltaTime;
                if (_startEnergyRegenerationTimer >= _config.TimeToStartRegeneration)
                {
                    _regeneratingEnergy = true;
                }
            }
            else
            {
                _energyRegenerationTimer += Time.deltaTime;
                if (_energyRegenerationTimer >= _config.RegenerationTime)
                {
                    RegenerateEnergy(_config.RegenerationValue);
                    _energyRegenerationTimer = 0;
                }
            }
        }

        public void Cleanup()
        {
            // Cleanup if needed
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
