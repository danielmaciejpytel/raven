using System;
using System.Collections.Generic;
using NaughtyAttributes;
using Raven.Input;
using Raven.UI;
using UnityEngine;
using Zenject;

namespace Raven.Player
{
    public enum CollectibleName { Dash, FireState, SecondWeapon, AddHpEnergy }

    [RequireComponent(typeof(Collider))]
    public class Collectible : MonoBehaviour
    {
        [SerializeField] private CollectibleName _collectibleName;
        [Space]
        [SerializeField] private GameObject _canvas;
        [SerializeField] private Transform _infoUiTransform;
        [SerializeField] private Transform _infoUiLockTransform;
        [SerializeField, ShowIf("_collectibleName", CollectibleName.AddHpEnergy)] private int _hpValue;
        [SerializeField, ShowIf("_collectibleName", CollectibleName.AddHpEnergy)] private int _energyValue;

        private PlayerStatesManager _playerStatesManager;
        private PlayerHudManager _playerHudManager; 
        private InputManager _inputManager;
        private bool _canTake;
        private bool _collected;
        private Camera _camera;
        private readonly HashSet<Collider> _playerColliders = new HashSet<Collider>();

        public event Action<CollectibleName> OnUnlock;

        [Inject]
        public void Construt(PlayerStatesManager p_playerStatesManager, InputManager pInputManager, PlayerHudManager p_playerHudManager)
        {
            _inputManager = pInputManager;
            _playerStatesManager = p_playerStatesManager;
            _playerHudManager = p_playerHudManager;

            _camera = Camera.main;
        }

        public void Update()
        {
            if (_collected || !_canTake || !_inputManager.TakeButtonPressed()) return;

            _collected = true;
            if (_collectibleName == CollectibleName.AddHpEnergy)
            {
                _playerHudManager.AddMaxHelthEnergy(_hpValue, _energyValue);
            }
            else
            {
                _playerStatesManager.UnlockState(_collectibleName);
                OnUnlock?.Invoke(_collectibleName);
            }
            Destroy(this.gameObject);
        }

        private void LateUpdate()
        {
            if (!_canTake || _infoUiTransform == null || _infoUiLockTransform == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera != null) _infoUiTransform.position = _camera.WorldToScreenPoint(_infoUiLockTransform.position);
        }

        private void OnTriggerEnter(Collider p_collider)
        {
            CharacterController player = p_collider.GetComponentInParent<CharacterController>();
            if (p_collider.CompareTag("Player") || (player != null && player.CompareTag("Player")))
            {
                _playerColliders.Add(p_collider);
                if (_canvas != null) _canvas.SetActive(true);
                _canTake = true;
            }
        }

        private void OnTriggerExit(Collider p_collider)
        {
            if (_playerColliders.Remove(p_collider) && _playerColliders.Count == 0)
            {
                if (_canvas != null) _canvas.SetActive(false);
                _canTake = false;
            }
        }

        private void OnDisable()
        {
            _playerColliders.Clear();
            _canTake = false;
            if (_canvas != null) _canvas.SetActive(false);
        }
    }
}

