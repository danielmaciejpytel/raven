using System;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages player statistics: health, energy, buffs, and status effects.
    /// </summary>
    public interface IStatsSubsystem : IPlayerSubsystem
    {
        /// <summary>Gets the current health value.</summary>
        float CurrentHealth { get; }

        /// <summary>Gets the current energy value.</summary>
        float CurrentEnergy { get; }

        /// <summary>Gets the maximum health value.</summary>
        float MaxHealth { get; }

        /// <summary>Gets the maximum energy value.</summary>
        float MaxEnergy { get; }

        /// <summary>Gets whether the player is alive.</summary>
        bool IsAlive { get; }

        /// <summary>Deals damage to the player.</summary>
        void TakeDamage(float amount);

        /// <summary>Heals the player.</summary>
        void Heal(float amount);

        /// <summary>Consumes energy for abilities.</summary>
        bool TryConsumeEnergy(float amount);

        /// <summary>Regenerates energy over time.</summary>
        void RegenerateEnergy(float amount);

        /// <summary>Event fired when health changes.</summary>
        event Action<float> OnHealthChanged;

        /// <summary>Event fired when energy changes.</summary>
        event Action<float> OnEnergyChanged;

        /// <summary>Event fired when the player dies.</summary>
        event Action OnDeath;
    }
}
