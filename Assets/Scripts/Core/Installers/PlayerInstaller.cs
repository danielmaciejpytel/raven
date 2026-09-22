using Unity.Cinemachine;
using Raven.Config;
using Raven.Container;
using Raven.Manager;
using Raven.Player;
using Raven.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.UI;
using Zenject;

namespace Raven.Core.Installer
{
    public class PlayerInstaller : MonoInstaller
    {
        [SerializeField] private LayerMask _shootRaycastHits;
        [Header("-----References-----")]
        [SerializeField] private PlayerReferences _playerReferences;
        [SerializeField] private Transform _mainCameraTransform;   
        [SerializeField] private GameObject _shootCamera;
        [SerializeField] private CinemachineCamera _tppCamera;    
        [SerializeField] private GameObject _rigTarget;
        [SerializeField] private GameObject _shootCameraLock;      
        [SerializeField] private PlayerHudReferences _hudReferences;
        [SerializeField] private Player.Collectible[] _collectibles;
        [SerializeField] private Animator _deadPanelAnimator;

        [Header("-----Configs-----")]
        [SerializeField] private MovementConfig _movementConfig;
        [SerializeField] private PlayerDataConfig _playerDataConfig;

        [Header("-----containers-----")]
        [SerializeField] private PlayerStatesContainer _playerStatesContainer;

        public override void InstallBindings()
        {
            var headLook = _playerReferences.PlayerAnimator.GetComponent<RavenHeadLook>();
            if (headLook != null)
                headLook.Initialize(_mainCameraTransform, _playerReferences.Player.transform,
                    Container.Resolve<Raven.Input.InputManager>(), _rigTarget.transform);
            var face = _playerReferences.PlayerAnimator.GetComponent<RavenFacialAnimation>();
            var holster = _playerReferences.PlayerAnimator.GetComponent<RavenWeaponHolster>();
            if (holster != null) holster.Initialize(Container.Resolve<Raven.Input.InputManager>(), _playerReferences.Player.transform);
            if (face != null) face.Initialize(Container.Resolve<Raven.Input.InputManager>());
            var torso = _playerReferences.PlayerAnimator.GetComponent<RavenAimTorso>();
            if (torso != null)
                torso.Initialize(_mainCameraTransform, _playerReferences.Player.transform,
                    Container.Resolve<Raven.Input.InputManager>(), _movementConfig);
            Container.BindInterfacesAndSelfTo<PlayerDataManager>().AsSingle().WithArguments(_playerDataConfig, _deadPanelAnimator);
            Container.BindInterfacesAndSelfTo<PlayerStatesManager>().AsSingle().WithArguments(_playerStatesContainer, _playerReferences).NonLazy();
            Container.BindInterfacesAndSelfTo<PlayerHudManager>().AsSingle().WithArguments(_hudReferences, _playerDataConfig, _collectibles).NonLazy();
            Container.BindInterfacesAndSelfTo<PlayerMovementManager>().AsSingle().WithArguments(_playerReferences.Player, _movementConfig, _mainCameraTransform, _playerReferences.PlayerGroundCheck).NonLazy();
            Container.BindInterfacesAndSelfTo<CameraManager>().AsSingle().WithArguments(_shootCamera, _tppCamera, _playerReferences.Player, _mainCameraTransform, _shootCameraLock, _movementConfig).NonLazy();
            Container.BindInterfacesAndSelfTo<PlayerAnimatorManager>().AsSingle().WithArguments(_playerReferences.PlayerAnimator).NonLazy();
            Container.BindInterfacesAndSelfTo<PlayerRigManager>().AsSingle().WithArguments(_playerReferences.PlayerRigs, _rigTarget, _shootRaycastHits, _mainCameraTransform, _movementConfig).NonLazy();
        }
    }
}
