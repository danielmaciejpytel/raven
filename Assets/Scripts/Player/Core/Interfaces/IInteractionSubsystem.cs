using System;
using UnityEngine;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages player interactions with world objects (doors, puzzles, NPCs, etc.).
    /// NEW subsystem to support puzzle and world interaction systems.
    /// </summary>
    public interface IInteractionSubsystem : IPlayerSubsystem
    {
        /// <summary>Gets the currently highlighted interactable object.</summary>
        IInteractable CurrentInteractable { get; }

        /// <summary>Gets whether the player can interact with objects.</summary>
        bool CanInteract { get; set; }

        /// <summary>Attempts to interact with the currently highlighted object.</summary>
        void TryInteract();

        /// <summary>Sets the current interactable object based on raycast or proximity.</summary>
        void SetCurrentInteractable(IInteractable interactable);

        /// <summary>Event fired when an interaction succeeds.</summary>
        event Action<IInteractable> OnInteractionSuccess;

        /// <summary>Event fired when an interactable is highlighted.</summary>
        event Action<IInteractable> OnInteractableHighlighted;
    }

    /// <summary>
    /// Interface for world objects that the player can interact with.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Gets the interaction prompt text.</summary>
        string InteractionPrompt { get; }

        /// <summary>Gets whether this object can currently be interacted with.</summary>
        bool CanInteract { get; }

        /// <summary>Called when the player interacts with this object.</summary>
        void OnInteract(PlayerController player);
    }
}
