using System;
using System.Collections;
using Raven.Config;
using Raven.Input;
using Raven.Manager;
using UnityEngine;
using Zenject;

namespace Raven.Player.Core
{
    /// <summary>
    /// Handles all player movement, gravity, and locomotion.
    /// Manages FPP/TPP perspective switching and ground detection.
    /// </summary>
    public class MovementSubsystem : IMovementSubsystem, IDisposable
    {
        private readonly Transform _playerTransform;
        private readonly CharacterController _charController;
        private readonly InputManager _inputManager;
        private readonly MovementConfig _movementConfig;
        private readonly Transform _cameraTransform;
        private readonly CameraManager _cameraManager;
        private readonly CoroutinesManager _coroutinesManager;
        private readonly Transform _groundCheck;

        private Vector3 _moveVector = Vector3.zero;
        private Vector3 _gravityVelocity = Vector3.zero;
        private float _currentGravity = 0f;
        private float _turnSmoothVelocity = 0f;
        private bool _isDashing = false;
        private bool _isFirstPerson = false;
        private bool _isGravityEnabled = true;
        private bool _fppToTppDelay = false;

        public Vector3 MoveVector => _moveVector;
        public bool IsDashing => _isDashing;
        public bool IsFirstPerson => _isFirstPerson;
        public bool IsGrounded => CheckGrounded();
        public CharacterController CharacterController => _charController;

        public event Action<float> OnMoveSpeedChanged;
        public event Action<bool> OnDashChanged;
        public event Action<bool> OnPerspectiveChanged;

        [Inject]
        public MovementSubsystem(
            GameObject playerGameObject,
            MovementConfig movementConfig,
            Transform cameraTransform,
            CameraManager cameraManager,
            CoroutinesManager coroutinesManager,
            InputManager inputManager,
            Transform groundCheck)
        {
            _playerTransform = playerGameObject.transform;
            _charController = playerGameObject.GetComponent<CharacterController>();
            _movementConfig = movementConfig;
            _cameraTransform = cameraTransform;
            _cameraManager = cameraManager;
            _coroutinesManager = coroutinesManager;
            _inputManager = inputManager;
            _groundCheck = groundCheck;
        }

        public void Initialize()
        {
            _cameraManager.OnAimChange += HandleAimChange;
        }

        public void Tick()
        {
            if (_isGravityEnabled)
            {
                UpdateGravity();
            }

            UpdateMovementInput();
            NotifyMovementSpeed();
        }

        public void FixedTick()
        {
            if (!_isDashing)
            {
                if (_isFirstPerson)
                {
                    ApplyFirstPersonMovement(_moveVector, _movementConfig.MoveSpeed);
                }
                else
                {
                    ApplyThirdPersonMovement(_moveVector, _movementConfig.MoveSpeed);
                }
            }
        }

        public void SetDash(bool shouldDash)
        {
            if (_isDashing == shouldDash)
                return;

            _isDashing = shouldDash;
            OnDashChanged?.Invoke(_isDashing);
        }

        public void SetGravity(bool enabled)
        {
            _isGravityEnabled = enabled;
        }

        private void UpdateGravity()
        {
            if (!_charController.isGrounded)
            {
                _currentGravity += _movementConfig.GravityValue * Time.deltaTime;
            }
            else
            {
                _currentGravity = -1f;
            }

            _gravityVelocity = new Vector3(0, _currentGravity, 0);
            _charController.Move(_gravityVelocity * Time.deltaTime);
        }

        private void UpdateMovementInput()
        {
            Vector2 inputAxis = _inputManager.GetMovementAxis();
            _moveVector = new Vector3(inputAxis.x, 0, inputAxis.y).normalized;
        }

        private void ApplyThirdPersonMovement(Vector3 moveVector, float speed)
        {
            if (moveVector.magnitude <= 0)
                return;

            float targetAngle = Mathf.Atan2(moveVector.x, moveVector.z) * Mathf.Rad2Deg + _cameraTransform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(
                _playerTransform.eulerAngles.y,
                targetAngle,
                ref _turnSmoothVelocity,
                _movementConfig.TurnSmoothTime);

            _playerTransform.rotation = Quaternion.Euler(0f, angle, 0f);
            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            _charController.Move(moveDir.normalized * speed * Time.deltaTime);
        }

        private void ApplyFirstPersonMovement(Vector3 moveVector, float speed)
        {
            Vector3 move = _playerTransform.right * moveVector.x + _playerTransform.forward * moveVector.z;
            _charController.Move(move * speed * Time.deltaTime);
        }

        private void HandleAimChange(bool isAiming)
        {
            if (isAiming)
            {
                _isFirstPerson = true;
                OnPerspectiveChanged?.Invoke(true);
                _fppToTppDelay = true;
            }
            else if (_fppToTppDelay)
            {
                _fppToTppDelay = false;
                _coroutinesManager.StartCoroutine(FppToTppDelayCoroutine());
            }
        }

        private IEnumerator FppToTppDelayCoroutine()
        {
            yield return new WaitForSeconds(_movementConfig.FppToTppDelayTime);
            _isFirstPerson = false;
            OnPerspectiveChanged?.Invoke(false);
        }

        private void NotifyMovementSpeed()
        {
            float speed = _moveVector.magnitude > 0 ? _movementConfig.MoveSpeed : 0;
            OnMoveSpeedChanged?.Invoke(speed);
        }

        private bool CheckGrounded()
        {
            RaycastHit[] hits = Physics.RaycastAll(_groundCheck.position, Vector3.down, 1);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.CompareTag("Ground") || hit.collider.CompareTag("Laver"))
                    return true;
            }
            return false;
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
