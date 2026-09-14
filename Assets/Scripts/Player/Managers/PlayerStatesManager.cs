using System;
using System.Collections.Generic;
using Raven.Config;
using Raven.Container;
using Raven.Core.Interface;
using Raven.Input;
using Raven.Manager;
using Raven.Player;
using Raven.UI;
using UnityEngine;
using Zenject;

namespace Raven.Player
{
    public class PlayerStatesManager : ITickable, IDisposable
    {
        private readonly PlayerStatesContainer _playerStatesContainer;
        private readonly InputManager _inputManager;
        private readonly NormalState _normalState;
        private readonly FireState _fireState;
        private readonly PlayerRigManager _playerRigManager;
        private readonly PlayerHudManager _playerHudManager;
        private readonly PlayerReferences _playerReferences;

        private PlayerStateConfig _currentConfig;
        private IPlayerState _currentBehaviour;
        private bool _canShoot;
        private bool _leftHandShoot;
        private float _shootDelayRemaining;
        private bool _alternateHandsAfterShot;
        private float _energySubtractTimer;
        private bool _disposed;

        private Dictionary<CollectibleName, bool> _unlockedStates = new Dictionary<CollectibleName, bool>();

        public PlayerStateConfig CurrentConfig => _currentConfig;
        public IPlayerState CurrentBehaviour => _currentBehaviour;
        public Dictionary<CollectibleName, bool> UnlockedStates => _unlockedStates;

        public event Action OnShoot;
        public event Action<PlayerStateName> OnChangeState;

        public PlayerStatesManager(PlayerStatesContainer p_playerStatesContainer, InputManager pInputManager,
            NormalState p_normalState, FireState p_fireState, PlayerHudManager p_hudManager,
            PlayerRigManager p_playerRigManager, PlayerReferences p_playerReferences)
        {
            _playerReferences = p_playerReferences;
            _playerStatesContainer = p_playerStatesContainer;
            _inputManager = pInputManager;
            _normalState = p_normalState;
            _fireState = p_fireState;
            _playerRigManager = p_playerRigManager;
            _playerHudManager = p_hudManager;

            _normalState.Initialize(pInputManager, p_hudManager, this);
            _fireState.Initialize(pInputManager, p_hudManager, this);

            _unlockedStates.Add(CollectibleName.Dash, false);
            _unlockedStates.Add(CollectibleName.FireState, false);
            _unlockedStates.Add(CollectibleName.SecondWeapon, false);

            _currentConfig = _playerStatesContainer.FindStateConfig(PlayerStateName.Normal);
            _currentBehaviour = _normalState;
            _canShoot = true;
            _playerReferences.SecondWeapon.SetActive(false);
            SetStateVfx(PlayerStateName.Normal);
        }

        public void Dispose()
        {
            _disposed = true;
            _canShoot = false;
            _shootDelayRemaining = 0f;
            _energySubtractTimer = 0f;
        }

        public void Tick()
        {
            if (_disposed || !IsPlayerActive()) return;

            UpdateShootDelay();
            UpdateEnergyDrain();

            if (_inputManager.ActiveStateButtonPressed() && _unlockedStates[CollectibleName.FireState])
            {
                ChangeState();
            }

            if (_currentBehaviour == _fireState && !_unlockedStates[CollectibleName.FireState])
            {
                return;
            }

            if (_inputManager.ShootButtonPressed() && _inputManager.AimButtonHold() && _canShoot)
            {
                _canShoot = false;
                _alternateHandsAfterShot = _unlockedStates[CollectibleName.SecondWeapon];
                _shootDelayRemaining = _alternateHandsAfterShot ? _currentConfig.TwoHandsDelay : _currentConfig.OneHandDelay;

                if (_unlockedStates[CollectibleName.SecondWeapon])
                {
                    if (_leftHandShoot)
                    {
                        _currentBehaviour.Shoot(_playerReferences.TwoHandsShootPoint, _playerRigManager.RigTarget.transform);
                    }
                    else
                    {
                        _currentBehaviour.Shoot(_playerReferences.OneHandShootPoint, _playerRigManager.RigTarget.transform);
                    }
                }
                else
                {
                    _currentBehaviour.Shoot(_playerReferences.OneHandShootPoint, _playerRigManager.RigTarget.transform);
                }

                OnShoot?.Invoke();
            }
        }

        private void UpdateShootDelay()
        {
            if (_canShoot) return;

            _shootDelayRemaining -= Time.deltaTime;
            if (_shootDelayRemaining > 0f) return;

            if (_alternateHandsAfterShot)
            {
                _leftHandShoot = !_leftHandShoot;
            }

            _shootDelayRemaining = 0f;
            _canShoot = true;
        }

        public void ChangeState()
        {
            if (_disposed || !IsPlayerActive()) return;

            if (_currentConfig.PlayerStateName == PlayerStateName.Normal)
            {
                if (!_unlockedStates[CollectibleName.FireState]) return;
                if (!_playerHudManager.TrySubtractEnergy(2f)) return;

                _currentConfig = _playerStatesContainer.FindStateConfig(PlayerStateName.Fire);
                _currentBehaviour = _fireState;
            }
            else
            {
                _currentConfig = _playerStatesContainer.FindStateConfig(PlayerStateName.Normal);
                _currentBehaviour = _normalState;
            }

            _energySubtractTimer = 0f;
            SetStateVfx(_currentConfig.PlayerStateName);

            OnChangeState?.Invoke(_currentConfig.PlayerStateName);

            _playerHudManager.ChangeStateImage(_currentConfig.PlayerStateName);
        }

        public void UnlockState(CollectibleName p_collectibleName)
        {
            if (_disposed) return;

            _unlockedStates[p_collectibleName] = true;

            if (p_collectibleName == CollectibleName.SecondWeapon)
            {
                _playerRigManager.SecondWeapon = true;
                _playerReferences.SecondWeapon.SetActive(true);
            }
        }

        private void UpdateEnergyDrain()
        {
            if (_currentBehaviour != _fireState) return;

            _energySubtractTimer += Time.deltaTime;
            while (_energySubtractTimer >= 1f)
            {
                _energySubtractTimer -= 1f;

                if (!_playerHudManager.TrySubtractEnergy(2f))
                {
                    ChangeState();
                    return;
                }
            }
        }

        private bool IsPlayerActive()
        {
            return _playerReferences != null && _playerReferences.Player != null && _playerReferences.Player.activeInHierarchy;
        }

        private void SetStateVfx(PlayerStateName p_playerStateName)
        {
            switch (p_playerStateName)
            {
                case PlayerStateName.Normal:
                    for (int i = 0; i < _playerReferences.NorrmalStateVfx.Length; i++)
                    {
                        _playerReferences.NorrmalStateVfx[i].SetActive(true);
                    }

                    for (int i = 0; i < _playerReferences.FireStateVfx.Length; i++)
                    {
                        _playerReferences.FireStateVfx[i].SetActive(false);
                    }
                    break;

                case PlayerStateName.Fire:
                    for (int i = 0; i < _playerReferences.NorrmalStateVfx.Length; i++)
                    {
                        _playerReferences.NorrmalStateVfx[i].SetActive(false);
                    }

                    for (int i = 0; i < _playerReferences.FireStateVfx.Length; i++)
                    {
                        _playerReferences.FireStateVfx[i].SetActive(true);
                    }
                    break;

                default:
                    break;
            }
        }
    }
}
