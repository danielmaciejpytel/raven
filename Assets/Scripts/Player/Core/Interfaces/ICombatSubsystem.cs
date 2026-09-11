using System;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages player combat states, weapons, and abilities.
    /// Coordinates with stats and movement subsystems.
    /// </summary>
    public interface ICombatSubsystem : IPlayerSubsystem
    {
        /// <summary>Gets the current combat state (e.g., Normal, Fire).</summary>
        string CurrentState { get; }

        /// <summary>Attempts to fire the current weapon/ability.</summary>
        bool TryShoot();

        /// <summary>Changes the player's combat state.</summary>
        void ChangeState(string newState);

        /// <summary>Checks if a specific ability is unlocked.</summary>
        bool IsAbilityUnlocked(string abilityName);

        /// <summary>Unlocks an ability or weapon.</summary>
        void UnlockAbility(string abilityName);

        /// <summary>Event fired when the player shoots.</summary>
        event Action OnShoot;

        /// <summary>Event fired when combat state changes.</summary>
        event Action<string> OnStateChanged;

        /// <summary>Event fired when an ability is unlocked.</summary>
        event Action<string> OnAbilityUnlocked;
    }
}
