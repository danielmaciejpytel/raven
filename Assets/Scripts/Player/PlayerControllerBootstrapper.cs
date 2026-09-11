using Raven.Manager;
using Raven.Player.Core;
using UnityEngine;
using Zenject;

namespace Raven.Player
{
    /// <summary>
    /// Initializes and manages the lifecycle of the unified Player Controller.
    /// This MonoBehaviour serves as the entry point for the entire player system.
    /// 
    /// How it works:
    /// 1. Gets injected with PlayerController
    /// 2. Initializes all subsystems on Start
    /// 3. Hooks into Zenject tick callbacks for Update/FixedUpdate
    /// 4. Handles cleanup on destroy
    /// </summary>
    public class PlayerControllerBootstrapper : MonoBehaviour
    {
        private PlayerController _playerController;
        private bool _isInitialized = false;

        [Inject]
        public void Construct(PlayerController playerController)
        {
            _playerController = playerController;
        }

        private void Start()
        {
            if (_playerController == null)
            {
                Debug.LogError("PlayerControllerBootstrapper: PlayerController was not injected!", gameObject);
                return;
            }

            _playerController.Initialize();
            _playerController.OnPlayerDeath += HandlePlayerDeath;
            _isInitialized = true;

            Debug.Log("PlayerControllerBootstrapper: Player system initialized successfully!");
        }

        private void HandlePlayerDeath()
        {
            Debug.Log("PlayerControllerBootstrapper: Player died!");
            // You can add death screen handling here
        }

        private void OnDestroy()
        {
            if (_playerController != null)
            {
                _playerController.Dispose();
            }
        }
    }
}
