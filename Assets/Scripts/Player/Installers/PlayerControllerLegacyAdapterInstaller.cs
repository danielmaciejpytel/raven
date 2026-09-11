using Raven.Manager;
using Raven.Player.Core;
using UnityEngine;
using Zenject;

namespace Raven.Player.Installers
{
    /// <summary>
    /// Legacy adapter installer for gradual migration.
    /// Use this if you want to keep using PlayerStatesManager while integrating PlayerController.
    /// Once fully migrated, switch back to PlayerControllerInstaller.
    /// </summary>
    public class PlayerControllerLegacyAdapterInstaller : MonoInstaller
    {
        [SerializeField] private PlayerControllerConfig _playerControllerConfig;

        public override void InstallBindings()
        {
            if (_playerControllerConfig == null)
            {
                Debug.LogError("PlayerControllerLegacyAdapterInstaller: PlayerControllerConfig is not assigned!");
                return;
            }

            // Bind configuration
            Container.Bind<PlayerControllerConfig>().FromInstance(_playerControllerConfig).AsSingle();
            Container.Bind<Raven.Config.MovementConfig>().FromInstance(_playerControllerConfig.MovementConfig).AsSingle();
            Container.Bind<Raven.Config.PlayerDataConfig>().FromInstance(_playerControllerConfig.PlayerDataConfig).AsSingle();

            // Bind player references
            Container.Bind<GameObject>().WithId("PlayerGameObject").FromInstance(_playerControllerConfig.PlayerGameObject).AsSingle();
            Container.Bind<Animator>().WithId("PlayerAnimator").FromInstance(_playerControllerConfig.PlayerAnimator).AsSingle();
            Container.Bind<Transform>().WithId("CameraTransform").FromInstance(_playerControllerConfig.CameraTransform).AsSingle();
            Container.Bind<Transform>().WithId("GroundCheck").FromInstance(_playerControllerConfig.GroundCheckTransform).AsSingle();

            // Bind subsystems (except combat, which uses legacy adapter)
            Container.Bind<IMovementSubsystem>().To<MovementSubsystem>().AsSingle();
            Container.Bind<IAnimationSubsystem>().To<AnimationSubsystem>().AsSingle();
            Container.Bind<IStatsSubsystem>().To<StatsSubsystem>().AsSingle();
            Container.Bind<IInteractionSubsystem>().To<InteractionSubsystem>().AsSingle();

            // Use legacy adapter for combat
            Container.Bind<ICombatSubsystem>().To<CombatSubsystemLegacyAdapter>().AsSingle();

            // Bind main controller
            Container.Bind<PlayerController>().AsSingle();

            Debug.Log("PlayerControllerLegacyAdapterInstaller: Player system with legacy combat support registered!");
        }
    }
}
