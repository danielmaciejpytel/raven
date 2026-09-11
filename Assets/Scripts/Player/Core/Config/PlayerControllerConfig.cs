using UnityEngine;

namespace Raven.Player.Core
{
    /// <summary>
    /// Centralized configuration for all PlayerController subsystems.
    /// Contains references to all config ScriptableObjects and setup parameters.
    /// </summary>
    [CreateAssetMenu(fileName = "Player Controller Config", menuName = "Configs/Player/Controller Config", order = 0)]
    public class PlayerControllerConfig : ScriptableObject
    {
        [Header("Subsystem Configurations")]
        [SerializeField] private Raven.Config.MovementConfig _movementConfig;
        [SerializeField] private Raven.Config.PlayerDataConfig _playerDataConfig;
        [SerializeField] private Raven.Config.PlayerStateConfig[] _combatStateConfigs;

        [Header("References")]
        [SerializeField] private GameObject _playerGameObject;
        [SerializeField] private Animator _playerAnimator;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private Transform _groundCheckTransform;

        [Header("Interaction Settings")]
        [SerializeField, Range(0.5f, 10f)] private float _interactionRange = 5f;
        [SerializeField] private LayerMask _interactionLayer;

        public Raven.Config.MovementConfig MovementConfig => _movementConfig;
        public Raven.Config.PlayerDataConfig PlayerDataConfig => _playerDataConfig;
        public Raven.Config.PlayerStateConfig[] CombatStateConfigs => _combatStateConfigs;
        public GameObject PlayerGameObject => _playerGameObject;
        public Animator PlayerAnimator => _playerAnimator;
        public Transform CameraTransform => _cameraTransform;
        public Transform GroundCheckTransform => _groundCheckTransform;
        public float InteractionRange => _interactionRange;
        public LayerMask InteractionLayer => _interactionLayer;
    }
}
