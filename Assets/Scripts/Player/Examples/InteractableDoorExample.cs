using Raven.Player.Core;
using UnityEngine;

namespace Raven.Player.Examples
{
    /// <summary>
    /// Example of creating an interactable world object.
    /// Implement IInteractable to make any object interactive.
    /// </summary>
    public class InteractableDoorExample : MonoBehaviour, IInteractable
    {
        [SerializeField] private float _energyCost = 10f;
        [SerializeField] private Animator _doorAnimator;
        private bool _isOpen = false;

        public string InteractionPrompt => _isOpen ? "Press E to close door" : "Press E to open door";
        public bool CanInteract => true;

        public void OnInteract(PlayerController player)
        {
            if (player.Stats.TryConsumeEnergy(_energyCost))
            {
                ToggleDoor();
                Debug.Log($"Door toggled! Energy remaining: {player.Stats.CurrentEnergy}");
            }
            else
            {
                Debug.LogWarning("Not enough energy to interact with door!");
            }
        }

        private void ToggleDoor()
        {
            _isOpen = !_isOpen;
            if (_doorAnimator != null)
            {
                _doorAnimator.SetBool("IsOpen", _isOpen);
            }
            Debug.Log($"Door is now {(_isOpen ? "open" : "closed")}");
        }
    }
}
