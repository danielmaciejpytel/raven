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
        private Vector3 _planarVelocity;
        private float _currentGravity;
        private float _airborneTime = float.PositiveInfinity;
        private float _turnSmoothVelocity;
        private float _turnAngle;
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
        public Vector3 PlanarVelocity => _planarVelocity;
        public float PlanarSpeed => _planarVelocity.magnitude;
        public float VerticalSpeed => _playerController != null ? _playerController.velocity.y : 0f;
        public bool Grounded => _playerController != null && _playerController.isGrounded;
        public bool AnimationGrounded => Grounded || _airborneTime < _movementConfig.FallingDelay;
        public float TurnAngle => _turnAngle;
        public bool Fpp => _fpp;
        public float MoveSpeed => _movementConfig.MoveSpeed;
        public float FppExitDelayTime => _movementConfig.FppToTppDelayTime;
        public float AnimationSpeedDampTime => _movementConfig.AnimationSpeedDampTime;
        public float AnimationDirectionDampTime => _movementConfig.AnimationDirectionDampTime;
        public float AnimationTurnDampTime => _movementConfig.AnimationTurnDampTime;
        public float LocomotionMinPlaybackRate => _movementConfig.LocomotionMinPlaybackRate;
        public float RecoilLocalKickDistance => _movementConfig.RecoilLocalKickDistance;
        public float RecoilLocalKickAngle => _movementConfig.RecoilLocalKickAngle;
        public float RecoilRecoverTime => _movementConfig.RecoilRecoverTime;
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

            OnMove?.Invoke(PlanarSpeed);
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
            FollowGround(ref displacement);
            // Keep one Move call: velocity must describe both horizontal and vertical motion.
            _playerController.Move(displacement);
            UpdatePlanarVelocity();
            UpdateGroundContact(deltaTime);
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

        private void FollowGround(ref Vector3 displacement)
        {
            if (!Grounded || _currentGravity > 0f || _movementConfig.GroundSnapDistance <= 0f) return;

            Bounds bounds = _playerController.bounds;
            float lift = Mathf.Max(0.05f, _playerController.skinWidth);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            float minimumNormalY = Mathf.Max(0.01f, Mathf.Cos(_playerController.slopeLimit * Mathf.Deg2Rad));
            // On a slope the capsule touches off-centre. Its bottom stays above the
            // centre ray hit by r * (1 / normal.y - 1); this is not an airborne gap.
            float maxSupportHeight = radius * (1f / minimumNormalY - 1f);
            Vector3 origin = new Vector3(bounds.center.x + displacement.x, bounds.min.y + lift,
                bounds.center.z + displacement.z);
            int count = PhysicsQueries.Raycast(origin, Vector3.down, ref _groundHits,
                lift + _movementConfig.GroundSnapDistance + maxSupportHeight, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            RaycastHit support = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.collider == null || hit.transform == _playerTransform || hit.transform.IsChildOf(_playerTransform) ||
                    Physics.GetIgnoreLayerCollision(_playerController.gameObject.layer, hit.collider.gameObject.layer) ||
                    Physics.GetIgnoreCollision(_playerController, hit.collider) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                support = hit;
            }
            if (float.IsPositiveInfinity(nearest) ||
                support.normal.y < minimumNormalY) return;

            float supportHeight = radius * (1f / support.normal.y - 1f);
            float drop = bounds.min.y - (support.point.y + supportHeight);
            if (drop < 0f || drop > _movementConfig.GroundSnapDistance) return;
            displacement.y = Mathf.Min(displacement.y, -Mathf.Min(_movementConfig.GroundSnapDistance,
                drop + 0.01f));
        }

        private void UpdateGroundContact(float deltaTime)
        {
            // Ground grace only filters ordinary locomotion. An airborne dash keeps
            // its original immediate airborne signal, including the dash exit.
            _airborneTime = Grounded ? 0f : _dash ? float.PositiveInfinity : _airborneTime + deltaTime;
        }

        private void SetMoveVector()
        {
            Vector2 movementAxis = _inputManager.GetMovementAxis();
            _moveVector = Vector3.ClampMagnitude(new Vector3(movementAxis.x, 0f, movementAxis.y), 1f);
        }

        private Vector3 GetMoveDirection(Vector3 p_moveVector, float p_deltaTime)
        {
            if (_fpp)
            {
                _turnAngle = 0f;
                return _playerTransform.right * p_moveVector.x + _playerTransform.forward * p_moveVector.z;
            }

            if (p_moveVector.sqrMagnitude <= 0f)
            {
                _turnAngle = 0f;
                return Vector3.zero;
            }

            float targetAngle = Mathf.Atan2(p_moveVector.x, p_moveVector.z) * Mathf.Rad2Deg + _camTransform.eulerAngles.y;
            _turnAngle = Mathf.DeltaAngle(_playerTransform.eulerAngles.y, targetAngle);
            float angle = Mathf.SmoothDampAngle(_playerTransform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity,
                _movementConfig.TurnSmoothTime, Mathf.Infinity, p_deltaTime);
            _playerTransform.rotation = Quaternion.Euler(0f, angle, 0f);

            return Quaternion.Euler(0f, angle, 0f) * Vector3.forward * p_moveVector.magnitude;
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
                    ? GetMoveDirection(_moveVector, deltaTime).normalized
                    : _playerTransform.forward;
                _playerController.Move(direction * _dashConfig.DashSpeed * deltaTime);
                UpdatePlanarVelocity();
                UpdateGroundContact(deltaTime);
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
            _planarVelocity = Vector3.zero;
            _airborneTime = float.PositiveInfinity;
            _currentGravity = 0f;
            _turnSmoothVelocity = 0f;
            _turnAngle = 0f;
            _dash = false;
            _gravity = true;
            _dashTimer = 0f;
            _dashBehaviour = null;
            _dashConfig = null;
        }

        private void UpdatePlanarVelocity()
        {
            Vector3 velocity = _playerController.velocity;
            velocity.y = 0f;
            _planarVelocity = velocity;
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

