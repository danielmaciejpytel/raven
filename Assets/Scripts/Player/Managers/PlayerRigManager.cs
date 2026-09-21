using System;
using Raven.Manager;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Zenject;

namespace Raven.Player
{
    public class PlayerRigManager : ITickable, IDisposable
    {
        private Rig[] _rigs;
        private GameObject _rigTarget;
        private LayerMask _rayLayerMask;
        private Transform _mainCamera;

        private bool _activeWeight;
        private bool _secondWeapon;
        private bool _aimDeactivatePending;
        private float _aimDeactivateTimer;
        private Transform _visualHandTarget;
        private Vector3 _visualHandTargetBasePosition;
        private Quaternion _visualHandTargetBaseRotation;
        private float _recoilAmount;
        private float _recoilRecoverSpeed;
        private float _recoilKickDistance;
        private float _recoilKickAngle;
        private const float AimWeightSmoothTime = 0.12f;

        public GameObject RigTarget => _rigTarget;

        public bool SecondWeapon
        {
            get => _secondWeapon;
            set
            {
                if (_secondWeapon == value) return;
                _secondWeapon = value;
            }
        }

        public PlayerRigManager(CameraManager p_cameraManager, Rig[] p_rigs, GameObject p_rigTarget, LayerMask p_rayMask, Transform p_mainCamera)
        {
            _rayLayerMask = p_rayMask;
            _rigs = p_rigs;
            _rigTarget = p_rigTarget;
            _mainCamera = p_mainCamera;

            if (_rigs.Length > 0 && _rigs[0] != null)
            {
                _visualHandTarget = _rigs[0].transform.Find("Target");
                if (_visualHandTarget != null)
                {
                    _visualHandTargetBasePosition = _visualHandTarget.localPosition;
                    _visualHandTargetBaseRotation = _visualHandTarget.localRotation;
                }
            }

            ApplyRigWeights(0f);

        }

        public void Dispose()
        {
            if (_visualHandTarget != null)
            {
                _visualHandTarget.localPosition = _visualHandTargetBasePosition;
                _visualHandTarget.localRotation = _visualHandTargetBaseRotation;
            }
        }

        public void Tick()
        {
            UpdateAimDeactivateDelay();
            UpdateVisualRecoil();
            UpdateRigWeights();

            Vector3 rayOrigin = _mainCamera.position;
            Vector3 rayDirection = _mainCamera.forward;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, 9999f, _rayLayerMask, QueryTriggerInteraction.Ignore))
            {
                _rigTarget.transform.position = hit.point;
                return;
            }

            _rigTarget.transform.position = rayOrigin + rayDirection * 9999f;
        }

        public RaycastHit GetRaycastHit()
        {
            Physics.Raycast(_mainCamera.position, _mainCamera.forward, out RaycastHit hit, 9999f, _rayLayerMask, QueryTriggerInteraction.Ignore);
            return hit;
        }

        public void KickRecoil(float kickDistance, float kickAngle, float recoverTime)
        {
            if (!_activeWeight || _visualHandTarget == null) return;

            _recoilKickDistance = Mathf.Max(0f, kickDistance);
            _recoilKickAngle = Mathf.Max(0f, kickAngle);
            _recoilAmount = 1f;
            _recoilRecoverSpeed = 1f / Mathf.Max(0.01f, recoverTime);
        }

        public void SetAimState(bool aim, float exitDelay)
        {
            if (aim)
            {
                _activeWeight = true;
                _aimDeactivatePending = false;
                _aimDeactivateTimer = 0f;
                return;
            }

            _aimDeactivateTimer = Mathf.Max(0f, exitDelay);
            _aimDeactivatePending = _aimDeactivateTimer > 0f;
            if (!_aimDeactivatePending)
            {
                _activeWeight = false;
            }
        }

        private void UpdateAimDeactivateDelay()
        {
            if (!_aimDeactivatePending) return;

            _aimDeactivateTimer -= Time.deltaTime;
            if (_aimDeactivateTimer > 0f) return;

            _aimDeactivatePending = false;
            _aimDeactivateTimer = 0f;
            _activeWeight = false;
        }

        private void UpdateRigWeights()
        {
            if (_rigs.Length == 0) return;

            float blendStep = AimWeightSmoothTime <= 0f ? 1f : Time.deltaTime / AimWeightSmoothTime;
            for (int i = 0; i < _rigs.Length; i++)
            {
                float targetWeight = _activeWeight && (_secondWeapon || i == 0) ? 1f : 0f;
                _rigs[i].weight = Mathf.MoveTowards(_rigs[i].weight, targetWeight, blendStep);
            }
        }

        private void UpdateVisualRecoil()
        {
            if (_visualHandTarget == null) return;

            _recoilAmount = Mathf.MoveTowards(_recoilAmount, 0f, _recoilRecoverSpeed * Time.deltaTime);
            float easedAmount = _recoilAmount * _recoilAmount;
            _visualHandTarget.localPosition = _visualHandTargetBasePosition + Vector3.back * (_recoilKickDistance * easedAmount);
            _visualHandTarget.localRotation = _visualHandTargetBaseRotation *
                                              Quaternion.Euler(-_recoilKickAngle * easedAmount, 0f, 0f);
        }

        private void ApplyRigWeights(float weight)
        {
            for (int i = 0; i < _rigs.Length; i++)
            {
                _rigs[i].weight = weight;
            }
        }
    }
}

