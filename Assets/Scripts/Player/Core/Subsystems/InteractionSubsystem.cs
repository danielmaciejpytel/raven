using System;
using UnityEngine;
using Zenject;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages player interactions with world objects (doors, puzzles, NPCs, etc.).
    /// Handles raycast detection and interaction prompt display.
    /// </summary>
    public class InteractionSubsystem : IInteractionSubsystem, IDisposable
    {
        private IInteractable _currentInteractable;
        private bool _canInteract = true;
        private const float INTERACTION_RANGE = 5f;

        public IInteractable CurrentInteractable => _currentInteractable;
        public bool CanInteract { get => _canInteract; set => _canInteract = value; }

        public event Action<IInteractable> OnInteractionSuccess;
        public event Action<IInteractable> OnInteractableHighlighted;

        public void Initialize()
        {
            // Initialization for interaction detection
        }

        public void Tick()
        {
            // Could implement continuous raycast detection here
        }

        public void FixedTick()
        {
            // Physics-based detection if needed
        }

        public void TryInteract()
        {
            if (!_canInteract || _currentInteractable == null || !_currentInteractable.CanInteract)
                return;

            // Note: PlayerController will be passed when actually implementing
            // For now, we'll trigger the event
            OnInteractionSuccess?.Invoke(_currentInteractable);
        }

        public void SetCurrentInteractable(IInteractable interactable)
        {
            if (_currentInteractable == interactable)
                return;

            _currentInteractable = interactable;
            if (interactable != null)
            {
                OnInteractableHighlighted?.Invoke(interactable);
            }
        }

        public void Cleanup()
        {
            _currentInteractable = null;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
