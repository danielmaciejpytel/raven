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
        private CinemachineCamera _aimCamera;
        private CinemachineInputAxisController[] _orbitInputs;
        private bool[] _orbitInputsEnabled;
        private Transform _playerTransform;
        private Transform _mainCamera;
        private MovementConfig _movementConfig;

        private bool _setPlayerRotation = true;
        private bool _isAiming;
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

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
                _orbitInputs = _tppCamera.GetComponents<CinemachineInputAxisController>();
            }
            else _orbitInputs = Array.Empty<CinemachineInputAxisController>();
            _orbitInputsEnabled = new bool[_orbitInputs.Length];
            for (int i = 0; i < _orbitInputs.Length; i++)
                _orbitInputsEnabled[i] = _orbitInputs[i].enabled;
            _aimCamera = _shootCamera.GetComponent<CinemachineCamera>();
            _playerTransform = p_player.GetComponent<Transform>();
            _mainCamera = p_mainCamera;
            ShootCameraLock = p_ShootCameraLock;

        }

        public void Dispose()
        {
            for (int i = 0; i < _orbitInputs.Length; i++)
                if (_orbitInputs[i] != null) _orbitInputs[i].enabled = _orbitInputsEnabled[i];
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
            _cinemachineTargetYaw = _playerTransform.eulerAngles.y;
            _setPlayerRotation = true;
        }

        private void SetCameras(bool p_aim)
        {
            if (_shootCamera.activeSelf != p_aim)
            {
                _shootCamera.SetActive(p_aim);
            }

            if (p_aim)
            {
                if (_setPlayerRotation)
                {
                    SetPlayerRotation();
                }
                else
                {
                    ShootCameraRotation();
                    if (_tppOrbital != null)
                    {
                        _tppOrbital.HorizontalAxis.Value = _playerTransform.eulerAngles.y;
                    }
                }
            }
            else
            {
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

            ShootCameraLock.transform.localRotation = Quaternion.Euler(-_cinemachineTargetPitch, 0f, 0.0f);
            _playerTransform.rotation = Quaternion.Euler(0f, _cinemachineTargetYaw, 0.0f);
        }

        private float ClampAimPitch(float pitch)
        {
            return Mathf.Clamp(pitch, -_movementConfig.AimMaxDownAngle, _movementConfig.AimMaxUpAngle);
        }

        private void SetPlayerRotation()
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
                _playerTransform.rotation = Quaternion.Euler(0f, _cinemachineTargetYaw, 0f);
                ShootCameraLock.transform.rotation = Quaternion.Euler(-_cinemachineTargetPitch, _cinemachineTargetYaw, 0f);
                if (_aimCamera == null) break;
                _aimCamera.PreviousStateIsValid = false;
                _aimCamera.InternalUpdateCameraState(Vector3.up, -1f);
                Vector3 toTarget = aimPoint - _aimCamera.State.GetFinalPosition();
                if (toTarget.sqrMagnitude < 0.01f || Vector3.Dot(toTarget, _mainCamera.forward) <= 0f) break;
                direction = toTarget.normalized;
            }

            _setPlayerRotation = false;
        }
    }
}

