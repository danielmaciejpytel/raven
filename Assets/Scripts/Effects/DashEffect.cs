using Raven.Config;
using UnityEngine;

namespace Raven.Player
{
    public class DashEffect : MonoBehaviour
    {
        [SerializeField, Tooltip("Set same value in config")] private float _debugRadius = 4f;

        private PlayerStateConfig _config;

        private float _timer;

        public void Initialize(PlayerStateConfig p_config)
        {
            _config = p_config;
        }

        public void Update()
        {
            _timer += Time.deltaTime;

            if (_timer > _config.EffectTime)
            {
                Destroy(this.gameObject);
            }

            // TODO: Apply fire damage within the effect radius.
        }

#if UNITY_EDITOR
        public void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _debugRadius);
        }
#endif
    }
}

