using System;
using Raven.Input;
using Raven.Manager;
using UnityEngine;
using Zenject;

namespace Raven.Player.Core
{
    /// <summary>
    /// Manages animator parameter updates and animation state.
    /// Syncs with movement, combat, and camera systems.
    /// </summary>
    public class AnimationSubsystem : IAnimationSubsystem, IDisposable
    {
        private readonly Animator _animator;
        private readonly InputManager _inputManager;
        private readonly CameraManager _cameraManager;

        public Animator Animator => _animator;

        [Inject]
        public AnimationSubsystem(
            Animator animator,
            InputManager inputManager,
            CameraManager cameraManager)
        {
            _animator = animator;
            _inputManager = inputManager;
            _cameraManager = cameraManager;
        }

        public void Initialize()
        {
            _cameraManager.OnAimChange += HandleAimChange;
        }

        public void Tick()
        {
            UpdateMovementDirection();
        }

        public void FixedTick()
        {
            // Physics-based animation updates would go here
        }

        public void SetMovementSpeed(float speed)
        {
            _animator.SetFloat("Speed", speed);
        }

        public void SetAim(bool isAiming)
        {
            _animator.SetBool("Aim", isAiming);
        }

        public void SetDash(bool isDashing)
        {
            _animator.SetBool("Dash", isDashing);
        }

        public void SetMovementDirection(Vector2 direction)
        {
            _animator.SetFloat("DirectionX", direction.x, 0.1f, Time.deltaTime);
            _animator.SetFloat("DirectionY", direction.y, 0.1f, Time.deltaTime);
        }

        public void TriggerAnimation(string triggerName)
        {
            _animator.SetTrigger(triggerName);
        }

        private void UpdateMovementDirection()
        {
            Vector2 inputAxis = _inputManager.GetMovementAxis();
            SetMovementDirection(inputAxis);
        }

        private void HandleAimChange(bool isAiming)
        {
            SetAim(isAiming);
        }

        public void Cleanup()
        {
            _cameraManager.OnAimChange -= HandleAimChange;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
