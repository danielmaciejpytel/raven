using System;
using System.Collections;
using Raven.Input;
using Raven.Config;
using Raven.Manager;
using Raven.UI;
using UnityEngine;

namespace Raven.Player
{
    public class PlayerDataManager: IDisposable
    {
        private PlayerDataConfig _config;
        private PlayerHudManager _playerHudManager;
        private PlayerMovementManager _playerMovementManager;
        private Animator _deadPanelAnimator;

        private float _currentHealth;
        private bool _isDead;
        private bool _respawning;
        private readonly InputManager _inputManager;
        private readonly CoroutinesManager _coroutinesManager;
        private Vector3 _checkpointPosition;
        private Quaternion _checkpointRotation;
        private float _previousTimeScale = 1f;
        public int RemainingLives { get; private set; }

        public event Action OnTakeDamage;
        public event Action OnDead;

        public PlayerDataManager(PlayerDataConfig p_config, PlayerHudManager p_playerHudManager, PlayerMovementManager p_playerMovementManager, Animator p_deadPanelAnimator,
            InputManager p_inputManager, CoroutinesManager p_coroutinesManager)
        {
            _playerMovementManager = p_playerMovementManager;
            _config = p_config;
            _playerHudManager = p_playerHudManager;
            _currentHealth = _config.MaxHealthValue;
            _deadPanelAnimator = p_deadPanelAnimator;
            _inputManager = p_inputManager;
            _coroutinesManager = p_coroutinesManager;
            SetCheckpoint(_playerMovementManager.PlayerTransform.position, _playerMovementManager.PlayerTransform.rotation);
            RemainingLives = _config.StartingLives;
            _playerHudManager.SetLives(RemainingLives, _config.StartingLives);

            _playerHudManager.OnAddHealth += SetCurrentHealth;
        }

        public void Dispose()
        {
            _playerHudManager.OnAddHealth -= SetCurrentHealth;
            _coroutinesManager.StopAllCoroutines(this);
            if (_respawning) Time.timeScale = _previousTimeScale;
        }

        private void SetCurrentHealth(float p_value)
        {
            if (!_isDead)
            {
                _currentHealth = p_value;
            }
        }

        public void TakeDamage(float p_value)
        {
            if (_isDead || _respawning || _playerMovementManager.Dash || p_value <= 0)
            {
                return;
            }

            float damage = Mathf.Min(_currentHealth, p_value);
            _currentHealth -= damage;
            _playerHudManager.TrySubtractHealth(damage);
            OnTakeDamage?.Invoke();

            if (_currentHealth <= 0)
            {
                Dead();
            }
        }

        private void Dead()
        {
            LoseLife();
        }

        public void SetCheckpoint(Vector3 position, Quaternion rotation)
        {
            _checkpointPosition = position;
            _checkpointRotation = rotation;
        }

        public void LoseLife()
        {
            if (_isDead || _respawning) return;
            RemainingLives = Mathf.Max(0, RemainingLives - 1);
            _playerHudManager.SetLives(RemainingLives, _config.StartingLives);
            _isDead = RemainingLives == 0;
            _respawning = true;
            _inputManager.CanInput = false;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            if (_isDead) OnDead?.Invoke();
            _coroutinesManager.StartCoroutine(DeathSequence(), this);
        }

        private IEnumerator DeathSequence()
        {
            yield return _playerHudManager.ShowDeath(_isDead);
            if (_isDead)
            {
                Time.timeScale = 1f;
                _respawning = false;
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
                yield break;
            }
            _playerMovementManager.Teleport(_checkpointPosition, _checkpointRotation);
            _playerHudManager.RestoreHealth();
            _currentHealth = _playerHudManager.MaxHealth;
            yield return _playerHudManager.HideDeath();
            _respawning = false;
            Time.timeScale = _previousTimeScale;
            _inputManager.CanInput = true;
        }
    }
}

