using Raven.Core;
using UnityEngine;
using Zenject;

public class ManagersInstaller : MonoInstaller
{
    [Header("-----References-----")]
    [SerializeField] private AudioReferences _audioReferences;

    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<AudioManager>().AsSingle().WithArguments(_audioReferences).NonLazy();
    }
}
