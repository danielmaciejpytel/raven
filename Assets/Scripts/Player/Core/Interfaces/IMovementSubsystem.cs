using UnityEngine;

namespace Raven.Player.Core
{
    /// <summary>
    /// Handles all player movement, gravity, and locomotion.
    /// Abstracts FPP/TPP switching and ground detection.
    /// </summary>
    public interface IMovementSubsystem : IPlayerSubsystem
    {
        /// <summary>Gets the current movement velocity.</summary>
        Vector3 MoveVector { get; }

        /// <summary>Gets whether the player is currently dashing.</summary>
        bool IsDashing { get; }

        /// <summary>Gets whether the player is in first-person perspective.</summary>
        bool IsFirstPerson { get; }

        /// <summary>Gets whether the player is grounded.</summary>
        bool IsGrounded { get; }

        /// <summary>Gets the character controller component.</summary>
        CharacterController CharacterController { get; }

        /// <summary>Sets the dash state and performs the dash action.</summary>
        void SetDash(bool shouldDash);

        /// <summary>Enables or disables gravity for the player.</summary>
        void SetGravity(bool enabled);

        /// <summary>Event fired when movement speed changes.</summary>
        event System.Action<float> OnMoveSpeedChanged;

        /// <summary>Event fired when dash state changes.</summary>
        event System.Action<bool> OnDashChanged;

        /// <summary>Event fired when perspective changes (FPP/TPP).</summary>
        event System.Action<bool> OnPerspectiveChanged;
    }
}
