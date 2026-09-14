using NaughtyAttributes;
using Raven.Config;
using Raven.Core;
using Raven.Manager;
using Raven.Player;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace Raven.Enemy
{
    public class Explode : MonoBehaviour
    {
        [SerializeField] private GameObject _effectPrefab;
        [HideIf("_effectOnEnemy"), SerializeField] private float _power = 2;
        [HideIf("_effectOnEnemy"), SerializeField] private float _explodeRadius = 5;
        [SerializeField] private Transform _effectPosition;
        [SerializeField] private bool _effectOnEnemy;
        [ShowIf("_effectOnEnemy"), SerializeField] private EnemyConfig _config;
        [SerializeField] private AudioClipConditions _explodeClip;

        private PlayerDataManager _playerDataManager;
        private float _currentPower;
        private float _currentExplodeRadius;
        private AudioManager _audioManager;
        private bool _exploded;

        [Inject]
        public void Construct(PlayerDataManager p_playerDataManager, AudioManager p_audioManager)
        {
            _audioManager = p_audioManager;
            _playerDataManager = p_playerDataManager;

            if (_effectOnEnemy)
            {
                _currentPower = _config.Power;
                _currentExplodeRadius = _config.ExplodeRadius;
            }
            else
            {
                _currentPower = _power;
                _currentExplodeRadius = _explodeRadius;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            CharacterController player = other.GetComponentInParent<CharacterController>();
            if (_effectOnEnemy && (other.CompareTag("Player") || (player != null && player.CompareTag("Player"))))
            {
                EnemyController enemy = GetComponentInParent<EnemyController>();
                if (enemy != null) enemy.TakeDamage(_config.MaxHealth);
            }
        }

        public void ExplodeBehaviour()
        {
            if (_exploded) return;
            _exploded = true;

            Collider[] hits = Physics.OverlapSphere(transform.position, _currentExplodeRadius);
            EnemyController owner = GetComponentInParent<EnemyController>();
            var damagedEnemies = new HashSet<EnemyController>();
            bool playerDamaged = false;

            if (_effectPrefab != null)
            {
                Vector3 position = _effectPosition != null ? _effectPosition.position : transform.position;
                var obj = Instantiate(_effectPrefab, position, _effectPrefab.transform.rotation);
                ExplodeEffect effect = obj.GetComponent<ExplodeEffect>();
                if (effect != null && _audioManager != null) effect.Init(_audioManager);
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;

                EnemyController enemy = hit.GetComponentInParent<EnemyController>();
                if (enemy != null)
                {
                    if (enemy != owner && damagedEnemies.Add(enemy))
                    {
                        enemy.TakeDamage(_currentPower);
                    }
                }
                else if (!playerDamaged && _playerDataManager != null)
                {
                    CharacterController player = hit.GetComponentInParent<CharacterController>();
                    if (hit.CompareTag("Player") || (player != null && player.CompareTag("Player")))
                    {
                        playerDamaged = true;
                        _playerDataManager.TakeDamage(_currentPower);
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_effectOnEnemy && (_config == null || !_config.ExplodeAfterDead))
            {
                return;
            }

            Gizmos.color = Color.red;

            Gizmos.DrawWireSphere(transform.position, _effectOnEnemy ? _config.ExplodeRadius : _explodeRadius);
        }
#endif
    }
}

