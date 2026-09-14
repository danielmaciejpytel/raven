using Raven.Config;
using Raven.Manager;
using Raven.Player;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    private PlayerStatesManager _playerStatesManager;
    private PlayerDataManager _playerDataManager;
    private EnemyConfig _enemyConfig;
    private float _speed;
    private float _power;
    private float _lifeTime = 4f;
    private GameObject _thisEnemy;

    private float _lifeTimer;
    private bool _spent;

    public void Initialization(PlayerStatesManager p_playerStatesManager)
    {
        _playerStatesManager = p_playerStatesManager;
        PlayerStateConfig config = _playerStatesManager.CurrentConfig;
        _speed = config.BulletSpeed;
        _power = config.bulletPower;
        _lifeTime = config.BulletLifeTime;
    }

    public void Initialization(float p_bulletSpeed, PlayerDataManager p_playerDataManager, EnemyConfig p_enemyConfig, GameObject p_thisEnemy)
    {
        _thisEnemy = p_thisEnemy;
        _enemyConfig = p_enemyConfig;
        _playerDataManager = p_playerDataManager;
        _speed = p_bulletSpeed;
        _power = p_enemyConfig.Power;
    }

    void Update()
    {
        if (_spent) return;

        transform.position += transform.forward * _speed * Time.deltaTime;
        _lifeTimer += Time.deltaTime;

        if (_lifeTimer >= _lifeTime)
        {
            _spent = true;
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_spent) return;
        if (other.CompareTag("Collectible") || other.CompareTag("Laver")) return;
        if (other.isTrigger && (other.GetComponent<Checpoint>() != null || other.GetComponent<ResetPoint>() != null)) return;
        if (_playerStatesManager == null && _thisEnemy != null &&
            (other.gameObject == _thisEnemy || other.transform.IsChildOf(_thisEnemy.transform))) return;

        CharacterController player = other.GetComponentInParent<CharacterController>();
        bool hitPlayer = other.CompareTag("Player") || (player != null && player.CompareTag("Player"));
        if (_playerStatesManager != null && hitPlayer) return;

        _spent = true;
        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy != null && (_playerStatesManager != null || _enemyConfig != null))
        {
            enemy.TakeDamage(_power);
        }
        else if (_playerDataManager != null && hitPlayer)
        {
            _playerDataManager.TakeDamage(_power);
        }

        Destroy(this.gameObject);
    }
}
