using System;
using System.Collections.Generic;
using Raven.Input;
using Raven.Manager;
using UnityEngine;
using Zenject;

namespace Raven.Player.Core
{
    /// <summary>
    /// Centralized Player Controller that orchestrates all player subsystems.
    /// This is the single entry point for all player-related logic.
    /// 
    /// Architecture:
    /// - PlayerController (Orchestrator)
    ///   - IMovementSubsystem (handles movement, gravity, perspective)
    ///   - IAnimationSubsystem (syncs animator parameters)
    ///   - IStatsSubsystem (manages health, energy)
    ///   - ICombatSubsystem (handles abilities, weapons, states)
    ///   - IInteractionSubsystem (handles world interactions)
    /// </summary>
    public class PlayerController : IInitializable, ITickable, IFixedTickable, IDisposable
    {
        private readonly InputManager _inputManager;
        private readonly CameraManager _cameraManager;
        private readonly Dictionary<Type, IPlayerSubsystem> _subsystems = new();

        private IMovementSubsystem _movement;
        private IAnimationSubsystem _animation;
        private IStatsSubsystem _stats;
        private ICombatSubsystem _combat;
        private IInteractionSubsystem _interaction;

        private bool _isInitialized = false;
        private bool _isActive = true;

        public IMovementSubsystem Movement => _movement;
        public IAnimationSubsystem Animation => _animation;
        public IStatsSubsystem Stats => _stats;
        public ICombatSubsystem Combat => _combat;
        public IInteractionSubsystem Interaction => _interaction;

        public bool IsActive => _isActive;
        public bool IsAlive => _stats?.IsAlive ?? false;

        /// <summary>Event fired when the player dies.</summary>
        public event Action OnPlayerDeath;

        /// <summary>Event fired when player input is disabled/enabled.</summary>
        public event Action<bool> OnInputStateChanged;

        [Inject]
        public PlayerController(
            InputManager inputManager,
            CameraManager cameraManager,
            IMovementSubsystem movementSubsystem,
            IAnimationSubsystem animationSubsystem,
            IStatsSubsystem statsSubsystem,
            ICombatSubsystem combatSubsystem,
            IInteractionSubsystem interactionSubsystem)
        {
            _inputManager = inputManager;
            _cameraManager = cameraManager;
            _movement = movementSubsystem;
            _animation = animationSubsystem;
            _stats = statsSubsystem;
            _combat = combatSubsystem;
            _interaction = interactionSubsystem;

            RegisterSubsystems();
            SubscribeToEvents();
        }

        /// <summary>Initializes all subsystems.</summary>
        public void Initialize()
        {
            if (_isInitialized)
                return;

            foreach (var subsystem in _subsystems.Values)
            {
                subsystem.Initialize();
            }

            _isInitialized = true;
        }

        /// <summary>Updates all subsystems (non-physics).</summary>
        public void Tick()
        {
            if (!_isActive || !_isInitialized)
                return;

            foreach (var subsystem in _subsystems.Values)
            {
                subsystem.Tick();
            }
        }

        /// <summary>Updates all subsystems (physics).</summary>
        public void FixedTick()
        {
            if (!_isActive || !_isInitialized)
                return;

            foreach (var subsystem in _subsystems.Values)
            {
                subsystem.FixedTick();
            }
        }

        /// <summary>Disables player input and gameplay.</summary>
        public void SetActive(bool active)
        {
            if (_isActive == active)
                return;

            _isActive = active;
            OnInputStateChanged?.Invoke(active);
        }

        /// <summary>Registers all subsystems for orchestration.</summary>
        private void RegisterSubsystems()
        {
            _subsystems[typeof(IMovementSubsystem)] = _movement;
            _subsystems[typeof(IAnimationSubsystem)] = _animation;
            _subsystems[typeof(IStatsSubsystem)] = _stats;
            _subsystems[typeof(ICombatSubsystem)] = _combat;
            _subsystems[typeof(IInteractionSubsystem)] = _interaction;
        }

        /// <summary>Subscribes to subsystem events.</summary>
        private void SubscribeToEvents()
        {
            if (_stats != null)
            {
                _stats.OnDeath += HandlePlayerDeath;
            }

            if (_movement != null)
            {
                _movement.OnMoveSpeedChanged += _animation?.SetMovementSpeed;
                _movement.OnDashChanged += _animation?.SetDash;
            }

            if (_combat != null)
            {
                _combat.OnShoot += _animation?.TriggerAnimation;
            }
        }

        /// <summary>Handles player death.</summary>
        private void HandlePlayerDeath()
        {
            SetActive(false);
            OnPlayerDeath?.Invoke();
        }

        /// <summary>Cleanup and disposal.</summary>
        public void Dispose()
        {
            foreach (var subsystem in _subsystems.Values)
            {
                subsystem.Cleanup();
                (subsystem as IDisposable)?.Dispose();
            }

            if (_stats != null)
            {
                _stats.OnDeath -= HandlePlayerDeath;
            }
        }
    }
}
