using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyCompany("Unity Technologies")]
[assembly: InternalsVisibleTo("Unity.ResourceManager.Tests")]
[assembly: InternalsVisibleTo("Unity.Addressables.Editor.Tests")]
[assembly: InternalsVisibleTo("Unity.Addressables.Runtime.Tests")]
[assembly: InternalsVisibleTo("Unity.Addressables")]
// Project compatibility: Localization 1.5 uses ResourceManager group operations.
[assembly: InternalsVisibleTo("Unity.Localization")]
[assembly: InternalsVisibleTo("Unity.Addressables.Android")]
#if UNITY_EDITOR
[assembly: InternalsVisibleTo("Unity.Addressables.Editor")]
#endif
