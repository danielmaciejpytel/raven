using Raven.Config;
using Raven.Input;
using Raven.Manager;
using Raven.Player.Core;
using Raven.UI;
using UnityEngine;
using Zenject;

namespace Raven.Player.Installers
{
    /// <summary>
    /// Simplified installer specifically for Level1 scene.
    /// Auto-discovers and configures all components.
    /// </summary>
    public class Level1PlayerInstaller : MonoInstaller
    {
        [SerializeField] private PlayerControllerConfig _playerControllerConfig;
        [SerializeField] private bool _autoDiscoverComponents = true;
        [SerializeField] private bool _debugLogging = true;

        public override void InstallBindings()
        {
            if (_playerControllerConfig == null)
            {
                Debug.LogError("Level1PlayerInstaller: PlayerControllerConfig is not assigned!");
                return;
            }

            if (_debugLogging)
            {
                Debug.Log("<b><color=cyan>[Level1PlayerInstaller]</color></b> Starting player system setup...");
            }

            // Bind configuration
            Container.Bind<PlayerControllerConfig>().FromInstance(_playerControllerConfig).AsSingle();
            Container.Bind<MovementConfig>().FromInstance(_playerControllerConfig.MovementConfig).AsSingle();
            Container.Bind<PlayerDataConfig>().FromInstance(_playerControllerConfig.PlayerDataConfig).AsSingle();

            // Bind player references
            Container.Bind<GameObject>()
                .WithId("PlayerGameObject")
                .FromInstance(_playerControllerConfig.PlayerGameObject)
                .AsSingle();

            Container.Bind<Animator>()
                .WithId("PlayerAnimator")
                .FromInstance(_playerControllerConfig.PlayerAnimator)
                .AsSingle();

            Container.Bind<Transform>()
                .WithId("CameraTransform")
                .FromInstance(_playerControllerConfig.CameraTransform)
                .AsSingle();

            Container.Bind<Transform>()
                .WithId("GroundCheck")
                .FromInstance(_playerControllerConfig.GroundCheckTransform)
                .AsSingle();

            // Bind subsystems
            Container.Bind<IMovementSubsystem>().To<MovementSubsystem>().AsSingle();
            Container.Bind<IAnimationSubsystem>().To<AnimationSubsystem>().AsSingle();
            Container.Bind<IStatsSubsystem>().To<StatsSubsystem>().AsSingle();
            Container.Bind<ICombatSubsystem>().To<CombatSubsystem>().AsSingle();
            Container.Bind<IInteractionSubsystem>().To<InteractionSubsystem>().AsSingle();

            // Bind main controller
            Container.Bind<PlayerController>().AsSingle();

            if (_debugLogging)
            {
                Debug.Log("<b><color=lime>[Level1PlayerInstaller]</color></b> ✅ All subsystems registered successfully!");
            }
        }
    }
}
