using Raven.Config;
using Raven.Input;
using Raven.Player;
using UnityEngine;
using Zenject;

namespace Raven.Core.Installer
{
    public sealed class PlayerCharacterVisualsInitializer : IInitializable
    {
        private readonly PlayerReferences _playerReferences;
        private readonly Transform _mainCameraTransform;
        private readonly GameObject _rigTarget;
        private readonly MovementConfig _movementConfig;
        private readonly InputManager _inputManager;

        public PlayerCharacterVisualsInitializer(PlayerReferences playerReferences,
            Transform mainCameraTransform, GameObject rigTarget, MovementConfig movementConfig,
            InputManager inputManager)
        {
            _playerReferences = playerReferences;
            _mainCameraTransform = mainCameraTransform;
            _rigTarget = rigTarget;
            _movementConfig = movementConfig;
            _inputManager = inputManager;
        }

        public void Initialize()
        {
            var animator = _playerReferences.PlayerAnimator;
            var player = _playerReferences.Player.transform;

            var headLook = animator.GetComponent<RavenHeadLook>();
            if (headLook != null)
                headLook.Initialize(_mainCameraTransform, player, _inputManager, _rigTarget.transform);

            var holster = animator.GetComponent<RavenWeaponHolster>();
            if (holster != null) holster.Initialize(_inputManager, player);

            var face = animator.GetComponent<RavenFacialAnimation>();
            if (face != null) face.Initialize(_inputManager);

            var torso = animator.GetComponent<RavenAimTorso>();
            if (torso != null)
                torso.Initialize(_mainCameraTransform, player, _inputManager, _movementConfig);
        }
    }
}
