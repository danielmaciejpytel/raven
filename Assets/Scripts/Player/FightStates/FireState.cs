using Raven.Config;
using Raven.Core.Interface;
using Raven.Input;
using Raven.Manager;
using Raven.UI;
using UnityEngine;

namespace Raven.Player
{
    public class FireState : IPlayerState
    {
        private InputManager _inputManager;
        private PlayerHudManager _hudManager;
        private PlayerStatesManager _playerStatesManager;

        public void Initialize(InputManager pInputManager, PlayerHudManager p_hudManager,
            PlayerStatesManager p_playerStatesManager)
        {
            _playerStatesManager = p_playerStatesManager;
            _inputManager = pInputManager;
            _hudManager = p_hudManager;
        }

        public void Shoot(Transform p_shootPoint, Transform p_lookAt)
        {
            var obj = GameObject.Instantiate(_playerStatesManager.CurrentConfig.BulletPrefab);
            obj.GetComponent<Bullet>().Initialization(_playerStatesManager);
            obj.transform.position = p_shootPoint.position;
            obj.transform.LookAt(p_lookAt);
        }

        public void ActiveDash(PlayerMovementManager p_movementManager)
        {
            if (_playerStatesManager.UnlockedStates[CollectibleName.FireState])
            {
                if (!_inputManager.DashButtonPressed()) return;
                if (!_hudManager.TrySubtractEnergy(_playerStatesManager.CurrentConfig.DashCost)) return;

                p_movementManager.BeginDash(this, _playerStatesManager.CurrentConfig);
            }
        }

        public void Dash(PlayerMovementManager p_movementManager)
        {
            PlayerStateConfig config = p_movementManager.DashConfig;
            if (p_movementManager.MoveDash())
            {
                GenerateEffect(p_movementManager, config);
            }
        }

        private void GenerateEffect(PlayerMovementManager p_movementManager, PlayerStateConfig p_config)
        {
            if (p_config.EffectPrefab == null) return;

            var obj = GameObject.Instantiate(p_config.EffectPrefab, p_movementManager.PlayerTransform.position,
                p_config.EffectPrefab.transform.rotation);
            obj.GetComponent<DashEffect>().Initialize(p_config);
        }
    }
}

