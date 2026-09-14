using Raven.Config;
using Raven.Core;
using Raven.Core.Interface;
using Raven.Input;
using System;
using Raven.Player;
using UnityEngine;
using Zenject;

namespace Raven.Manager
{
    public class PlayerMovementManager : IFixedTickable, ITickable, IDisposable
    {
        private readonly Transform _playerTransform;
        private readonly CharacterController _playerController;
        private readonly InputManager _inputManager;
        private readonly MovementConfig _movementConfig;
        private readonly Transform _camTransform;
        private readonly CameraManager _cameraManager;
        private readonly PlayerStatesManager _playerStatesManager;
        private RaycastHit[] _groundHits = new RaycastHit[8];

        private readonly Transform _groundCheck;

        private Vector3 _moveVector;
        private float _currentGravity;
        private float _turnSmoothVelocity;
        private bool _dash;
        private float _dashTimer;
        private IPlayerState _dashBehaviour;
        private PlayerStateConfig _dashConfig;
        private bool _fpp;
        private bool _fppToTppDelay;
        private float _fppToTppTimer;
        private bool _gravity;
        private bool _disposed;

        public CharacterController PlayerController => _playerController;
        public Transform PlayerTransform => _playerTransform;
        public bool Dash => _dash;
        public bool GravityBool => _gravity;
        public Vector3 MoveVector => _moveVector;
        public bool Fpp => _fpp;
        internal PlayerStateConfig DashConfig => _dashConfig;

        public event Action<float> OnMove;
        public Action<bool> OnDash;
        public event Action<PlayerStateName> OnDashStart;

        [Inject]
        public PlayerMovementManager(GameObject p_player, MovementConfig p_movementConfig, Transform p_camTransform, CameraManager p_cameraManager,
            InputManager pInputManager, PlayerStatesManager p_playerStatesManager, Transform p_groundCheck)
        {
            _groundCheck = p_groundCheck;
            _playerTransform = p_player.GetComponent<Transform>();
            _playerController = p_player.GetComponent<CharacterController>();
            _inputManager = pInputManager;
            _movementConfig = p_movementConfig;
            _camTransform = p_camTransform;
            _cameraManager = p_cameraManager;
            _playerStatesManager = p_playerStatesManager;

            _cameraManager.OnAimChange += SetFpp;

            _gravity = true;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cameraManager.OnAimChange -= SetFpp;
            ResetMovement();
            OnMove = null;
            OnDash = null;
            OnDashStart = null;
        }

        public void Teleport(Vector3 p_position, Quaternion p_rotation)
        {
            if (_disposed || _playerController == null) return;

            bool controllerWasEnabled = _playerController.enabled;
            _playerController.enabled = false;
            try
            {
                _playerTransform.SetPositionAndRotation(p_position, p_rotation);
            }
            finally
            {
                _playerController.enabled = controllerWasEnabled;
            }
            CancelDash();
            ResetMovement();
            Physics.SyncTransforms();
            _cameraManager.OnPlayerTeleported();
            OnMove?.Invoke(0f);
        }

        public void Tick()
        {
            if (_disposed) return;

            if (!CanMove())
            {
                CancelDash(_playerController != null);
                ResetMovement();
                if (_playerController != null) OnMove?.Invoke(0f);
                return;
            }

            UpdateFppExitDelay();
            SetMoveVector();

            if (!_dash && IsGrounded())
            {
                _playerStatesManager.CurrentBehaviour.ActiveDash(this);
            }

            if (_moveVector.sqrMagnitude > 0f)
            {
                OnMove?.Invoke(_movementConfig.MoveSpeed);
            }
            else
            {
                OnMove?.Invoke(0);
            }
        }

        public void FixedTick()
        {
            if (_disposed) return;

            if (!CanMove())
            {
                CancelDash(_playerController != null);
                ResetMovement();
                return;
            }

            if (_dash)
            {
                _dashBehaviour.Dash(this);
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            Vector3 displacement = GetMoveDirection(_moveVector, deltaTime) * _movementConfig.MoveSpeed * deltaTime;
            displacement += Gravity(deltaTime);
            _playerController.Move(displacement);
        }

        #region Movement Scripts

        private Vector3 Gravity(float p_deltaTime)
        {
            if (!_playerController.isGrounded)
            {
                _currentGravity += _movementConfig.GravityValue * p_deltaTime;
            }
            else
            {
                _currentGravity = -1f;
            }

            return Vector3.up * _currentGravity * p_deltaTime;
        }

        private void SetMoveVector()
        {
            Vector2 movementAxis = _inputManager.GetMovementAxis();
            _moveVector = new Vector3(movementAxis.x, 0f, movementAxis.y).normalized;
        }

        private Vector3 GetMoveDirection(Vector3 p_moveVector, float p_deltaTime)
        {
            if (_fpp)
            {
                return _playerTransform.right * p_moveVector.x + _playerTransform.forward * p_moveVector.z;
            }

            if (p_moveVector.sqrMagnitude <= 0f)
            {
                return Vector3.zero;
            }

            float targetAngle = Mathf.Atan2(p_moveVector.x, p_moveVector.z) * Mathf.Rad2Deg + _camTransform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(_playerTransform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity,
                _movementConfig.TurnSmoothTime, Mathf.Infinity, p_deltaTime);
            _playerTransform.rotation = Quaternion.Euler(0f, angle, 0f);

            return Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        }

        internal void BeginDash(IPlayerState p_behaviour, PlayerStateConfig p_config)
        {
            if (_disposed || _dash || !CanMove()) return;

            // A state change must not replace the dash that has already been paid for.
            _dashBehaviour = p_behaviour;
            _dashConfig = p_config;
            _dashTimer = 0f;
            _dash = true;
            _gravity = false;
            _currentGravity = 0f;
            OnDash?.Invoke(true);
            OnDashStart?.Invoke(p_config.PlayerStateName);
        }

        internal bool MoveDash()
        {
            if (!_dash) return false;

            float remainingTime = Mathf.Max(0f, _dashConfig.DashTime - _dashTimer);
            float deltaTime = Mathf.Min(Time.fixedDeltaTime, remainingTime);
            if (deltaTime > 0f)
            {
                Vector3 direction = _moveVector.sqrMagnitude > 0f
                    ? GetMoveDirection(_moveVector, deltaTime)
                    : _playerTransform.forward;
                _playerController.Move(direction * _dashConfig.DashSpeed * deltaTime);
                _dashTimer += deltaTime;
            }

            if (_dashTimer < _dashConfig.DashTime && !Mathf.Approximately(_dashTimer, _dashConfig.DashTime)) return false;

            CancelDash();
            return true;
        }

        private void CancelDash(bool p_notify = true)
        {
            if (!_dash) return;

            _dash = false;
            _gravity = true;
            _dashTimer = 0f;
            _dashBehaviour = null;
            _dashConfig = null;
            if (p_notify) OnDash?.Invoke(false);
        }

        private bool CanMove()
        {
            return _playerController != null && _playerController.enabled && _playerController.gameObject.activeInHierarchy;
        }

        private void ResetMovement()
        {
            _moveVector = Vector3.zero;
            _currentGravity = 0f;
            _turnSmoothVelocity = 0f;
            _dash = false;
            _gravity = true;
            _dashTimer = 0f;
            _dashBehaviour = null;
            _dashConfig = null;
        }

        #endregion

        private void SetFpp(bool p_aim)
        {
            if (p_aim)
            {
                _fpp = true;
                _fppToTppDelay = false;
                _fppToTppTimer = 0f;
            }
            else if (_fpp)
            {
                _fppToTppDelay = true;
                _fppToTppTimer = 0f;
            }
        }

        private void UpdateFppExitDelay()
        {
            if (!_fppToTppDelay)
            {
                return;
            }

            _fppToTppTimer += Time.deltaTime;
            if (_fppToTppTimer < _movementConfig.FppToTppDelayTime)
            {
                return;
            }

            _fpp = false;
            _fppToTppDelay = false;
            _fppToTppTimer = 0f;
        }

        private bool IsGrounded()
        {
            int hitCount = PhysicsQueries.Raycast(_groundCheck.position, Vector3.down, ref _groundHits, 1f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = _groundHits[i].collider;
                if (hitCollider != null && (hitCollider.CompareTag("Ground") || hitCollider.CompareTag("Laver")))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

