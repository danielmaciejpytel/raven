using System;
using System.Collections.Generic;
using Raven.Manager;
using Raven.Config;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Zenject;

namespace Raven.Player
{
    public class PlayerRigManager : ITickable, ILateTickable, IDisposable
    {
        private Rig[] _rigs;
        private readonly CameraManager _cameraManager;
        private readonly float[] _rigWeights;
        private readonly float[] _rigBlendProgress;
        private readonly float _aimReturnTime;
        private GameObject _rigTarget;
        private LayerMask _rayLayerMask;
        private Transform _mainCamera;

        private bool _activeWeight;
        private bool _secondWeapon;
        private readonly Transform[] _armBones;
        private readonly Quaternion[] _lastArmPose;
        private readonly Quaternion[] _returnFromPose;
        private bool _hasArmPose;
        private bool _returningPose;
        private float _poseReturnTimer;
        private float _poseReturnDuration;
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

        public PlayerRigManager(CameraManager p_cameraManager, Rig[] p_rigs, GameObject p_rigTarget, LayerMask p_rayMask, Transform p_mainCamera, MovementConfig p_movementConfig = null)
        {
            _cameraManager = p_cameraManager;
            _rayLayerMask = p_rayMask;
            _rigs = p_rigs;
            _rigWeights = new float[p_rigs.Length];
            _rigBlendProgress = new float[p_rigs.Length];
            _aimReturnTime = p_movementConfig != null ? p_movementConfig.AimReturnTime : 0.2f;
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

            var bones = new List<Transform>();
            foreach (var rig in _rigs)
                foreach (var constraint in rig.GetComponentsInChildren<TwoBoneIKConstraint>())
                    foreach (var bone in new[] { constraint.data.root, constraint.data.mid, constraint.data.tip })
                        if (bone != null && !bones.Contains(bone)) bones.Add(bone);
            _armBones = bones.ToArray();
            _lastArmPose = new Quaternion[_armBones.Length];
            _returnFromPose = new Quaternion[_armBones.Length];
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

        public void SetAimState(bool aim)
        {
            if (_activeWeight == aim) return;
            // Capture the last evaluated pose, not a camera-space or distant IK target.
            // Local bone rotations move with the body during a locomotion turn.
            if (_hasArmPose && (!aim || _returningPose))
            {
                Array.Copy(_lastArmPose, _returnFromPose, _armBones.Length);
                _poseReturnTimer = 0f;
                _poseReturnDuration = aim ? AimWeightSmoothTime : _aimReturnTime;
                _returningPose = true;
            }
            _activeWeight = aim;
            if (!aim)
            {
                // The late pose blend owns the release. Camera-driven IK must no
                // longer influence it, even while the camera is blending to TPP.
                ApplyRigWeights(0f);
                _recoilAmount = 0f;
            }
        }

        public void LateTick()
        {
            // Animator and RigBuilder have evaluated before LateUpdate. Blend the
            // actual last visible arm pose into this frame's locomotion pose.
            float blend = 1f;
            if (_returningPose)
            {
                _poseReturnTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_poseReturnTimer / Mathf.Max(0.01f, _poseReturnDuration));
                blend = 1f - (1f - progress) * (1f - progress);
                if (progress >= 1f) _returningPose = false;
            }
            for (int i = 0; i < _armBones.Length; i++)
            {
                if (blend < 1f)
                    _armBones[i].localRotation = Quaternion.Slerp(_returnFromPose[i], _armBones[i].localRotation, blend);
                _lastArmPose[i] = _armBones[i].localRotation;
            }
            _hasArmPose = true;
        }

        private void UpdateRigWeights()
        {
            if (_rigs.Length == 0) return;

            float duration = AimWeightSmoothTime;
            float blendStep = Time.deltaTime / Mathf.Max(0.01f, duration);
            for (int i = 0; i < _rigs.Length; i++)
            {
                float targetWeight = _activeWeight && (_secondWeapon || i == 0)
                    ? Mathf.Sqrt(_cameraManager != null ? _cameraManager.AimPoseWeight : 1f) : 0f;
                // Animator/RigBuilder can write back the streamed component weight.
                // Keep smoothing state separate from that evaluated output.
                _rigBlendProgress[i] = Mathf.MoveTowards(_rigBlendProgress[i], targetWeight, blendStep);
                _rigWeights[i] = _rigBlendProgress[i] * _rigBlendProgress[i];
                _rigs[i].weight = _rigWeights[i];
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
                _rigBlendProgress[i] = Mathf.Sqrt(weight);
                _rigWeights[i] = weight;
                _rigs[i].weight = weight;
            }
        }
    }
}

