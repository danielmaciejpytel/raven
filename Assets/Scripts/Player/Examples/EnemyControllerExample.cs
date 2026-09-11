using Raven.Player.Core;
using UnityEngine;

namespace Raven.Player.Examples
{
    /// <summary>
    /// Example of how enemies interact with the player controller.
    /// Shows damage dealing, ability unlocks, and healing.
    /// </summary>
    public class EnemyControllerExample : MonoBehaviour
    {
        [SerializeField] private float _damagePerAttack = 10f;
        [SerializeField] private float _attackCooldown = 2f;
        [SerializeField] private float _detectionRange = 10f;

        private PlayerControllerBootstrapper _playerBootstrapper;
        private PlayerController _playerController;
        private float _lastAttackTime = 0f;
        private Transform _playerTransform;

        private void Start()
        {
            _playerBootstrapper = FindObjectOfType<PlayerControllerBootstrapper>();
            if (_playerBootstrapper != null)
            {
                _playerController = _playerBootstrapper.GetType()
                    .GetField("_playerController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(_playerBootstrapper) as PlayerController;
            }

            _playerTransform = _playerController?.Movement.CharacterController.transform;
        }

        private void Update()
        {
            if (_playerController == null || !_playerController.IsAlive)
                return;

            float distanceToPlayer = Vector3.Distance(transform.position, _playerTransform.position);

            if (distanceToPlayer < _detectionRange)
            {
                if (Time.time - _lastAttackTime > _attackCooldown)
                {
                    AttackPlayer();
                    _lastAttackTime = Time.time;
                }
            }
        }

        private void AttackPlayer()
        {
            _playerController.Stats.TakeDamage(_damagePerAttack);
            Debug.Log($"Enemy attacked player! Damage: {_damagePerAttack}, Remaining health: {_playerController.Stats.CurrentHealth}");
        }

        public void DropLoot()
        {
            if (_playerController?.IsAlive == true)
            {
                _playerController.Stats.Heal(5);
                _playerController.Stats.RegenerateEnergy(10);
                Debug.Log("Enemy dropped loot!");
            }
        }
    }
}
