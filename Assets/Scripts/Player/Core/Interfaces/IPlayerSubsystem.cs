namespace Raven.Player.Core
{
    /// <summary>
    /// Base interface for all player subsystems.
    /// Defines lifecycle and update methods.
    /// </summary>
    public interface IPlayerSubsystem
    {
        /// <summary>Called when the subsystem is initialized.</summary>
        void Initialize();

        /// <summary>Called once per frame for non-physics updates.</summary>
        void Tick();

        /// <summary>Called once per fixed timestep for physics updates.</summary>
        void FixedTick();

        /// <summary>Called when the subsystem is disabled or the player dies.</summary>
        void Cleanup();
    }
}
