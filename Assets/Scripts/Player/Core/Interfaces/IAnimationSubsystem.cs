using UnityEngine;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages animator parameter updates and animation state.
    /// Syncs with movement and combat subsystems.
    /// </summary>
    public interface IAnimationSubsystem : IPlayerSubsystem
    {
        /// <summary>Gets the Animator component.</summary>
        Animator Animator { get; }

        /// <summary>Sets the movement speed parameter for animation blending.</summary>
        void SetMovementSpeed(float speed);

        /// <summary>Sets the aim state in the animator.</summary>
        void SetAim(bool isAiming);

        /// <summary>Sets the dash state in the animator.</summary>
        void SetDash(bool isDashing);

        /// <summary>Sets the movement direction for blending locomotion animations.</summary>
        void SetMovementDirection(Vector2 direction);

        /// <summary>Triggers a generic animation trigger by name.</summary>
        void TriggerAnimation(string triggerName);
    }
}
