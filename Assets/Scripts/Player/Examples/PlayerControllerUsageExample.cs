using Raven.Player.Core;
using UnityEngine;

namespace Raven.Player.Examples
{
    /// <summary>
    /// Example usage of the PlayerController subsystem architecture.
    /// Shows how to interact with subsystems from external systems.
    /// </summary>
    public class PlayerControllerUsageExample : MonoBehaviour
    {
        private PlayerController _playerController;

        private void Start()
        {
            // Get reference to player controller (would be injected in real usage)
            _playerController = GetComponent<PlayerControllerBootstrapper>()
                .GetType()
                .GetField("_playerController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(GetComponent<PlayerControllerBootstrapper>()) as PlayerController;

            if (_playerController == null)
            {
                Debug.LogWarning("PlayerController not found. Make sure PlayerControllerBootstrapper is in scene.");
                return;
            }

            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            // Subscribe to player death
            _playerController.OnPlayerDeath += OnPlayerDeath;

            // Subscribe to health changes
            _playerController.Stats.OnHealthChanged += OnHealthChanged;

            // Subscribe to energy changes
            _playerController.Stats.OnEnergyChanged += OnEnergyChanged;

            // Subscribe to movement speed changes
            _playerController.Movement.OnMoveSpeedChanged += OnMovementSpeedChanged;

            // Subscribe to interactions
            _playerController.Interaction.OnInteractionSuccess += OnInteractionSuccess;
        }

        /// <summary>Example: Deal damage to player from enemy.</summary>
        public void DamagePlayer(float amount)
        {
            if (_playerController?.IsAlive == true)
            {
                _playerController.Stats.TakeDamage(amount);
                Debug.Log($"Player took {amount} damage. Health: {_playerController.Stats.CurrentHealth}");
            }
        }

        /// <summary>Example: Heal the player.</summary>
        public void HealPlayer(float amount)
        {
            if (_playerController?.IsAlive == true)
            {
                _playerController.Stats.Heal(amount);
                Debug.Log($"Player healed for {amount}. Health: {_playerController.Stats.CurrentHealth}");
            }
        }

        /// <summary>Example: Check if player has enough energy for ability.</summary>
        public bool TryExecuteAbility(float energyCost)
        {
            if (_playerController?.Stats.TryConsumeEnergy(energyCost) == true)
            {
                Debug.Log($"Ability executed! Energy remaining: {_playerController.Stats.CurrentEnergy}");
                return true;
            }

            Debug.LogWarning("Not enough energy!");
            return false;
        }

        /// <summary>Example: Unlock an ability.</summary>
        public void UnlockAbility(string abilityName)
        {
            _playerController?.Combat.UnlockAbility(abilityName);
            Debug.Log($"Unlocked: {abilityName}");
        }

        /// <summary>Example: Set an interactable object.</summary>
        public void HighlightInteractable(IInteractable interactable)
        {
            _playerController?.Interaction.SetCurrentInteractable(interactable);
        }

        // Event handlers
        private void OnPlayerDeath()
        {
            Debug.Log("Player died! Show death screen.");
        }

        private void OnHealthChanged(float newHealth)
        {
            Debug.Log($"Health changed: {newHealth}/{_playerController.Stats.MaxHealth}");
        }

        private void OnEnergyChanged(float newEnergy)
        {
            Debug.Log($"Energy changed: {newEnergy}/{_playerController.Stats.MaxEnergy}");
        }

        private void OnMovementSpeedChanged(float speed)
        {
            Debug.Log($"Movement speed: {speed}");
        }

        private void OnInteractionSuccess(IInteractable interactable)
        {
            Debug.Log($"Interacted with: {interactable.InteractionPrompt}");
        }

        private void OnDestroy()
        {
            if (_playerController == null) return;

            _playerController.OnPlayerDeath -= OnPlayerDeath;
            _playerController.Stats.OnHealthChanged -= OnHealthChanged;
            _playerController.Stats.OnEnergyChanged -= OnEnergyChanged;
            _playerController.Movement.OnMoveSpeedChanged -= OnMovementSpeedChanged;
            _playerController.Interaction.OnInteractionSuccess -= OnInteractionSuccess;
        }
    }
}
