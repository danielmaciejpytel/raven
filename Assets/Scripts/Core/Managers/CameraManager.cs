using Raven.Input;
using System;
using Unity.Cinemachine;
using Raven.Config;
using UnityEngine;
using Zenject;

namespace Raven.Manager
{
    public class CameraManager : ITickable, IDisposable
    {
        private InputManager _inputManager;
        private GameObject _shootCamera;
        private CinemachineCamera _tppCamera;
        private CinemachineOrbitalFollow _tppOrbital;
        private Vector3 _originalTppPositionDamping;
        private CinemachineCamera _aimCamera;
        private CinemachineBrain _brain;
        private CinemachineBlenderSettings _originalBlends;
        private CinemachineBlenderSettings _aimBlends;
        private CinemachineCore.BlendHints _originalTppBlendHint;
        private CinemachineCore.BlendHints _originalAimBlendHint;
        private CinemachineInputAxisController[] _orbitInputs;
        private bool[] _orbitInputsEnabled;
        private Transform _playerTransform;
        private Transform _mainCamera;
        private MovementConfig _movementConfig;

        private bool _setPlayerRotation = true;
        private bool _isAiming;
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;
        private CharacterController _playerController;
        private bool _aimEntryActive;
        private bool _aimEntryMoving;
        private float _aimEntryTimer;
        private float _aimEntryDuration;
        private float _aimEntryStartYaw;
        private float _aimEntryTargetYaw;
        private float _aimEntryAngle;

        public bool AimEntryActive => _aimEntryActive;
        public bool AimEntryMoving => _aimEntryMoving;
        public bool AimEntryRunningPivot => _aimEntryMoving && Mathf.Abs(_aimEntryAngle) >= _movementConfig.RunPivotAngle;
        public float AimEntryAngle => _aimEntryAngle;
        public float AimEntryProgress => _aimEntryActive ? Mathf.Clamp01(_aimEntryTimer / _aimEntryDuration) : 1f;
        public float AimYawError => Mathf.DeltaAngle(_playerTransform.eulerAngles.y, _cinemachineTargetYaw);
        public float AimPoseWeight => _aimEntryActive
            ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 80f, Mathf.Abs(AimYawError))) : 1f;
        public float AimEntryBlendStart => _movementConfig.AimEntryBlendStart;
        public float AimEntryStepAngle => _movementConfig.AimEntryStepAngle;

        public GameObject ShootCameraLock;

        public event Action<bool> OnAimChange;
        public bool IsAiming => _isAiming;
        public bool IsCameraBlending => _mainCamera != null
            && _mainCamera.TryGetComponent<CinemachineBrain>(out var brain) && brain.IsBlending;

        public void PrepareGameplayCamera()
        {
            SetOrbitInputEnabled(false);
            SetCameras(false);
            if (_tppOrbital != null)
            {
                // World-space yaw also matches the existing aim-to-orbit handoff.
                _tppOrbital.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
                _tppOrbital.HorizontalAxis.Value = _tppOrbital.HorizontalAxis.ClampValue(
                    _playerTransform.eulerAngles.y + _movementConfig.StartCameraYawOffset);
                _tppOrbital.VerticalAxis.Value = Mathf.Lerp(_tppOrbital.VerticalAxis.Range.x,
                    _tppOrbital.VerticalAxis.Range.y, _movementConfig.StartCameraHeight);
            }
            OnPlayerTeleported();
        }

        public CameraManager(InputManager pInputManager, GameObject p_shootCamera, CinemachineCamera p_tppCamera,
            GameObject p_player, Transform p_mainCamera, GameObject p_ShootCameraLock, MovementConfig p_movementConfig)
        {
            _movementConfig = p_movementConfig;
            _inputManager = pInputManager;
            _shootCamera = p_shootCamera;
            _tppCamera = p_tppCamera;
            if (_tppCamera != null)
            {
                _tppOrbital = _tppCamera.GetComponent<CinemachineOrbitalFollow>();
                if (_tppOrbital != null)
                {
                    _originalTppPositionDamping = _tppOrbital.TrackerSettings.PositionDamping;
                    // Composer looks at the live player. A horizontally lagging follow position
                    // makes strafing rotate the view, which then steers camera-relative movement.
                    // Keep vertical terrain smoothing; camera-mode travel has its own blend.
                    _tppOrbital.TrackerSettings.PositionDamping = new Vector3(0f, _originalTppPositionDamping.y, 0f);
                }
                _orbitInputs = _tppCamera.GetComponents<CinemachineInputAxisController>();
            }
            else _orbitInputs = Array.Empty<CinemachineInputAxisController>();
            _orbitInputsEnabled = new bool[_orbitInputs.Length];
            for (int i = 0; i < _orbitInputs.Length; i++)
                _orbitInputsEnabled[i] = _orbitInputs[i].enabled;
            _aimCamera = _shootCamera.GetComponent<CinemachineCamera>();
            _playerTransform = p_player.GetComponent<Transform>();
            _playerController = p_player.GetComponent<CharacterController>();
            _mainCamera = p_mainCamera;
            ShootCameraLock = p_ShootCameraLock;
            ConfigureAimCameraBlend();

        }

        public void Dispose()
        {
            if (_tppOrbital != null) _tppOrbital.TrackerSettings.PositionDamping = _originalTppPositionDamping;
            if (_aimBlends != null)
            {
                if (_brain != null && _brain.CustomBlends == _aimBlends) _brain.CustomBlends = _originalBlends;
                if (_tppCamera != null) _tppCamera.BlendHint = _originalTppBlendHint;
                if (_aimCamera != null) _aimCamera.BlendHint = _originalAimBlendHint;
                UnityEngine.Object.Destroy(_aimBlends);
            }
            for (int i = 0; i < _orbitInputs.Length; i++)
                if (_orbitInputs[i] != null) _orbitInputs[i].enabled = _orbitInputsEnabled[i];
        }

        private void ConfigureAimCameraBlend()
        {
            if (_mainCamera == null || _tppCamera == null || _aimCamera == null ||
                !_mainCamera.TryGetComponent(out _brain)) return;

            // Keep menu/other camera rules and never mutate a shared settings asset in Play Mode.
            _originalBlends = _brain.CustomBlends;
            _aimBlends = _originalBlends != null ? UnityEngine.Object.Instantiate(_originalBlends)
                : ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
            _aimBlends.name = "Raven aim camera blends (runtime)";
            _aimBlends.hideFlags = HideFlags.DontSave;
            var rules = new System.Collections.Generic.List<CinemachineBlenderSettings.CustomBlend>(
                _aimBlends.CustomBlends ?? Array.Empty<CinemachineBlenderSettings.CustomBlend>());
            rules.RemoveAll(r => (r.From == _tppCamera.Name && r.To == _aimCamera.Name) ||
                (r.From == _aimCamera.Name && r.To == _tppCamera.Name));
            rules.Add(new CinemachineBlenderSettings.CustomBlend { From = _tppCamera.Name, To = _aimCamera.Name,
                Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, _movementConfig.AimCameraBlendIn) });
            rules.Add(new CinemachineBlenderSettings.CustomBlend { From = _aimCamera.Name, To = _tppCamera.Name,
                Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, _movementConfig.AimCameraBlendOut) });
            _aimBlends.CustomBlends = rules.ToArray();
            _brain.CustomBlends = _aimBlends;
            _originalTppBlendHint = _tppCamera.BlendHint;
            _originalAimBlendHint = _aimCamera.BlendHint;
            // Cinemachine snapshots the rendered blend when it is interrupted, including rapid re-aim.
            _tppCamera.BlendHint |= CinemachineCore.BlendHints.FreezeWhenBlendingOut;
            _aimCamera.BlendHint |= CinemachineCore.BlendHints.FreezeWhenBlendingOut;
        }

        public void Tick()
        {
            SetOrbitInputEnabled(_inputManager.GameplayInputEnabled && !_inputManager.AimButtonHold());
            // Preserve the active camera and aim pose while the pause menu is open.
            if (Time.timeScale <= 0f && _inputManager.CanInput) return;

            bool isAiming = _inputManager.AimButtonHold();

            if (_isAiming != isAiming)
            {
                _isAiming = isAiming;
                OnAimChange?.Invoke(isAiming);
            }

            SetCameras(isAiming);
        }

        private void SetOrbitInputEnabled(bool enabled)
        {
            for (int i = 0; i < _orbitInputs.Length; i++)
            {
                bool active = enabled && _orbitInputsEnabled[i];
                if (_orbitInputs[i] != null && _orbitInputs[i].enabled != active)
                    _orbitInputs[i].enabled = active;
            }
        }

        public void OnPlayerTeleported()
        {
            // Discard damping/collision history from the old checkpoint position.
            if (_tppCamera != null) _tppCamera.PreviousStateIsValid = false;
            if (_aimCamera != null) _aimCamera.PreviousStateIsValid = false;
            _aimEntryActive = false;
            _cinemachineTargetYaw = _playerTransform.eulerAngles.y;
            _setPlayerRotation = true;
        }

        private void SetCameras(bool p_aim)
        {
            if (_shootCamera.activeSelf != p_aim)
            {
                if (!p_aim && _tppOrbital != null)
                {
                    // Prepare the destination only when returning. Changing the outgoing TPP
                    // orbit during aim entry used to move both ends of the camera blend.
                    // A quick release can interrupt aim entry. Match the rendered view,
                    // not the aim camera's destination yaw, and seed its new tracking history.
                    float exitYaw = _mainCamera.eulerAngles.y;
                    _tppOrbital.HorizontalAxis.Value = _tppOrbital.HorizontalAxis.ClampValue(exitYaw);
                    _tppCamera.PreviousStateIsValid = false;
                    _tppCamera.InternalUpdateCameraState(Vector3.up, -1f);
                }
                _shootCamera.SetActive(p_aim);
            }

            if (p_aim)
            {
                if (_setPlayerRotation)
                {
                    BeginAimEntry();
                }
                ShootCameraRotation();
            }
            else
            {
                _aimEntryActive = false;
                _setPlayerRotation = true;
            }

        }

        private void ShootCameraRotation()
        {
            Vector2 look = _inputManager.GetMouseDelta();
            // Pointer delta already contains this frame's displacement. Preserve the
            // existing sensitivity at 60 FPS; sticks still express a rate per second.
            float inputScale = _inputManager.IsPointerLook ? 1f / 60f : Time.deltaTime;
            look *= inputScale * _movementConfig.FppMouseSensitivity;
            _cinemachineTargetYaw = Mathf.Repeat(_cinemachineTargetYaw + look.x + 180f, 360f) - 180f;
            _cinemachineTargetPitch = ClampAimPitch(_cinemachineTargetPitch + look.y);

            if (_aimEntryActive)
            {
                _aimEntryTargetYaw += look.x;
                _aimEntryTimer += Time.deltaTime;
                float progress = AimEntryProgress;
                float yaw = Mathf.Lerp(_aimEntryStartYaw, _aimEntryTargetYaw, Mathf.SmoothStep(0f, 1f, progress));
                _playerTransform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _aimEntryMoving |= PlanarSpeed() > _movementConfig.MoveSpeed * 0.2f;
                if (progress >= 1f) _aimEntryActive = false;
            }
            else _playerTransform.rotation = Quaternion.Euler(0f, _cinemachineTargetYaw, 0f);
            // The camera aims immediately; body yaw catches up independently.
            ShootCameraLock.transform.rotation = Quaternion.Euler(-_cinemachineTargetPitch, _cinemachineTargetYaw, 0f);
        }

        private float ClampAimPitch(float pitch)
        {
            return Mathf.Clamp(pitch, -_movementConfig.AimMaxDownAngle, _movementConfig.AimMaxUpAngle);
        }

        private void BeginAimEntry()
        {
            // Capture the rendered view, including a partially completed camera blend.
            Vector3 direction = _mainCamera.forward;
            Vector3 aimPoint = _mainCamera.position + direction * 1000f;
            float nearest = 1000f;
            foreach (var hit in Physics.RaycastAll(_mainCamera.position, direction, nearest,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == _playerTransform || hit.transform.IsChildOf(_playerTransform)) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    aimPoint = hit.point;
                }
            }

            // Recompute from the shoulder camera position to compensate for parallax.
            // Reset old damping so returning to aim cannot reuse the previous aim pose.
            for (int i = 0; i < 6; i++)
            {
                _cinemachineTargetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                _cinemachineTargetPitch = ClampAimPitch(Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg);
                ShootCameraLock.transform.rotation = Quaternion.Euler(-_cinemachineTargetPitch, _cinemachineTargetYaw, 0f);
                if (_aimCamera == null) break;
                _aimCamera.PreviousStateIsValid = false;
                _aimCamera.InternalUpdateCameraState(Vector3.up, -1f);
                Vector3 toTarget = aimPoint - _aimCamera.State.GetFinalPosition();
                if (toTarget.sqrMagnitude < 0.01f || Vector3.Dot(toTarget, _mainCamera.forward) <= 0f) break;
                direction = toTarget.normalized;
            }

            _aimEntryStartYaw = _playerTransform.eulerAngles.y;
            _aimEntryAngle = Mathf.DeltaAngle(_aimEntryStartYaw, _cinemachineTargetYaw);
            _aimEntryTargetYaw = _aimEntryStartYaw + _aimEntryAngle;
            _aimEntryDuration = Mathf.Lerp(_movementConfig.AimEntryMinTime, _movementConfig.AimEntryMaxTime,
                Mathf.Abs(_aimEntryAngle) / 180f);
            _aimEntryTimer = 0f;
            _aimEntryMoving = PlanarSpeed() > _movementConfig.MoveSpeed * 0.2f;
            _aimEntryActive = Mathf.Abs(_aimEntryAngle) > 1f;
            _setPlayerRotation = false;
        }

        private float PlanarSpeed()
        {
            if (_playerController == null) return 0f;
            Vector3 velocity = _playerController.velocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }
    }
}

