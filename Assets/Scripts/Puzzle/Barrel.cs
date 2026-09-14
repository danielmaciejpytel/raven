using Raven.Enemy;
using UnityEngine;

namespace Raven.Puzzle
{
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Explode))]
    public class Barrel : MonoBehaviour
    {
        [SerializeField] private GameObject[] _destroyable;

        private Collider _collider;
        private Explode _explode;
        private bool _exploded;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
            _explode = GetComponent<Explode>();
        }

        private void OnTriggerEnter(Collider p_other)
        {
            if (!_exploded && (p_other.CompareTag("Bullet") || p_other.CompareTag("FireBullet")))
            {
                _exploded = true;
                _explode.ExplodeBehaviour();

                for (int i = 0; i < _destroyable.Length; i++)
                {
                    Destroy(_destroyable[i]);
                }

                Destroy(gameObject);
            }
        }
    }
}
