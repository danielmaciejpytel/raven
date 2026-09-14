using ModestTree;
using NaughtyAttributes;
using Raven.Core;
using Raven.Player;
using Raven.UI;
using UnityEngine;
using Zenject;

namespace Raven.Collectible.PowerUp
{
    public enum PowerUpType { Energy, Hp }
    public class PowerUp : MonoBehaviour
    {
        [SerializeField] private PowerUpType _powerUpType;
        [SerializeField] private int _addValue;
        [SerializeField] private float _speed;
        [SerializeField] private float _activeRadius;

        private PlayerHudManager _playerHudManager;
        private Transform _playerTransform;
        private Collider[] _overlapResults = new Collider[8];
        private bool _collected;

        [Inject]
        public void Construct(PlayerHudManager p_playerHudManager)
        {
            _playerHudManager = p_playerHudManager;
        }

        private void Update()
        {
            if (_collected || Time.deltaTime <= 0f) return;
            if (_playerTransform == null)
            {
                int hitCount = PhysicsQueries.OverlapSphere(transform.position, _activeRadius, ref _overlapResults);

                for (int i = 0; i < hitCount; i++)
                {
                    Collider hit = _overlapResults[i];
                    if (hit == null)
                    {
                        continue;
                    }

                    if (hit.CompareTag("Player"))
                    {
                        _playerTransform = hit.transform;
                        break;
                    }
                }
            }

            if (_playerTransform != null)
            {
                Vector3 destination = new Vector3(_playerTransform.position.x, _playerTransform.position.y + 1, _playerTransform.position.z);
                float distance = Vector3.Distance(destination, transform.position);

                if (distance > 0.5)
                {
                    float step = _speed * Time.deltaTime;
                    transform.position = Vector3.MoveTowards(transform.position, destination, step);
                    return;
                }

                _collected = true;
                switch (_powerUpType)
                {
                    case PowerUpType.Energy:
                        _playerHudManager.AddEnergy(_addValue);
                        break;

                    case PowerUpType.Hp:
                        _playerHudManager.AddHealth(_addValue);
                        break;
                }

                Destroy(this.gameObject);
            }
        }

#if UNITY_EDITOR

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, _activeRadius);
        }

#endif
    }
}

