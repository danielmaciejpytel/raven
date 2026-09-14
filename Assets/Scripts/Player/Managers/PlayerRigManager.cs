using System;
using Raven.Manager;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Zenject;

namespace Raven.Player
{
    public class PlayerRigManager : ITickable, IDisposable
    {
        private CameraManager _cameraManager;

        private Rig[] _rigs;
        private GameObject _rigTarget;
        private LayerMask _rayLayerMask;
        private Transform _mainCamera;

        private bool _activeWeight;
        private bool _secondWeapon;

        public GameObject RigTarget => _rigTarget;

        public bool SecondWeapon
        {
            get => _secondWeapon;
            set
            {
                if (_secondWeapon == value) return;
                _secondWeapon = value;
                UpdateRigWeights();
            }
        }

        public PlayerRigManager(CameraManager p_cameraManager, Rig[] p_rigs, GameObject p_rigTarget, LayerMask p_rayMask, Transform p_mainCamera)
        {
            _rayLayerMask = p_rayMask;
            _rigs = p_rigs;
            _rigTarget = p_rigTarget;
            _cameraManager = p_cameraManager;
            _mainCamera = p_mainCamera;

            UpdateRigWeights();

            _cameraManager.OnAimChange += ActiveWeight;
        }

        public void Dispose()
        {
            _cameraManager.OnAimChange -= ActiveWeight;
        }

        public void Tick()
        {
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

        private void ActiveWeight(bool p_aim)
        {
            _activeWeight = p_aim;
            UpdateRigWeights();
        }

        private void UpdateRigWeights()
        {
            for (int i = 0; i < _rigs.Length; i++)
            {
                _rigs[i].weight = 0f;
            }

            if (!_activeWeight || _rigs.Length == 0)
            {
                return;
            }

            if (_secondWeapon)
            {
                for (int i = 0; i < _rigs.Length; i++)
                {
                    _rigs[i].weight = 1f;
                }
            }
            else
            {
                _rigs[0].weight = 1f;
            }
        }
    }
}

