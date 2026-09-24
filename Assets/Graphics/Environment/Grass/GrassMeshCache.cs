using UnityEngine;

// Authored seed meshes survive scene and managed-domain restoration. Painters
// reference these assets directly and keep edited previews in separate meshes.
[PreferBinarySerialization]
public sealed class GrassMeshCache : ScriptableObject
{
    public string signature;
    public Mesh[] meshes;
}
