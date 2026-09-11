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
    /// Zenject installer for the unified Player Controller system.
    /// Registers all subsystems and their dependencies.
    /// 
    /// Usage:
    /// 1. Create an empty GameObject in your scene
    /// 2. Add this script as a MonoBehaviour
    /// 3. It will automatically register all player subsystems with Zenject
    /// </summary>
    public class PlayerControllerInstaller : MonoInstaller
    {
        [SerializeField] private PlayerControllerConfig _playerControllerConfig;
        [SerializeField] private bool _autoWireReferences = true;

        public override void InstallBindings()
        {
            if (_playerControllerConfig == null)
            {
                Debug.LogError("PlayerControllerInstaller: PlayerControllerConfig is not assigned!", gameObject);
                return;
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
            Container.Bind<PlayerController>().AsSingle().WithArguments(
                Container.Resolve<InputManager>(),
                Container.Resolve<CameraManager>(),
                Container.Resolve<IMovementSubsystem>(),
                Container.Resolve<IAnimationSubsystem>(),
                Container.Resolve<IStatsSubsystem>(),
                Container.Resolve<ICombatSubsystem>(),
                Container.Resolve<IInteractionSubsystem>()
            );

            Debug.Log("PlayerControllerInstaller: All player subsystems registered successfully!");
        }
    }
}
