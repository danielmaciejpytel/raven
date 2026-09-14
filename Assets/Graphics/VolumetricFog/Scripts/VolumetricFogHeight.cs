using UnityEngine;

namespace VolumetricFogAndMist2 {
    public partial class VolumetricFog {
        public bool enableHeightMap;
        [Tooltip("Weighted local height in metres (R) and painted influence (G). Uses Fog Of War world coverage.")]
        public Texture2D fogHeightMap;
        [Min(0.05f)] public float maskBrushHeight = 10f;
        public bool maskBrushPaintHeight;

        static readonly int HeightMapId = Shader.PropertyToID("_FogHeightMap");
        static readonly int HeightMapBoundsId = Shader.PropertyToID("_FogHeightMapBounds");
        const string HeightMapKeyword = "VF2_HEIGHT_MAP";

        public float DefaultPaintHeight => Mathf.Max(0.05f, profile != null && profile.terrainFit
            ? profile.terrainFogHeight : transform.lossyScale.y * 0.5f);

        void UpdateHeightMapMaterial() {
            if (fogMat == null) return;
            bool enabled = enableHeightMap && fogHeightMap != null;
            if (enabled) fogMat.EnableKeyword(HeightMapKeyword);
            else fogMat.DisableKeyword(HeightMapKeyword);
            fogMat.SetTexture(HeightMapId, fogHeightMap);
            fogMat.SetVector(HeightMapBoundsId, new Vector4(fogOfWarCenter.x, fogOfWarCenter.z,
                Mathf.Max(0.001f, fogOfWarSize.x), Mathf.Max(0.001f, fogOfWarSize.z)));
        }

        // Returns false outside the map. Distance is measured in world space, including rectangular coverage.
        public bool PaintFogHeight(Vector3 position, float height, float radius, float opacity, float softness, bool reset = false) {
            if (fogHeightMap == null || !fogHeightMap.isReadable || radius <= 0 || opacity <= 0 ||
                fogOfWarSize.x <= 0 || fogOfWarSize.z <= 0 || float.IsNaN(height) || float.IsInfinity(height)) return false;
            float u = (position.x - fogOfWarCenter.x) / fogOfWarSize.x + 0.5f;
            float v = (position.z - fogOfWarCenter.z) / fogOfWarSize.z + 0.5f;
            if (u < 0 || u > 1 || v < 0 || v > 1) return false;
            int width = fogHeightMap.width, depth = fogHeightMap.height;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((u - radius / fogOfWarSize.x) * width), 0, width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((u + radius / fogOfWarSize.x) * width), 0, width - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((v - radius / fogOfWarSize.z) * depth), 0, depth - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((v + radius / fogOfWarSize.z) * depth), 0, depth - 1);
            int patchWidth = x1 - x0 + 1, patchHeight = z1 - z0 + 1;
            Color[] pixels = fogHeightMap.GetPixels(x0, z0, patchWidth, patchHeight);
            float baseHeight = DefaultPaintHeight;
            bool changed = false;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) {
                float dx = ((x + 0.5f) / width - u) * fogOfWarSize.x;
                float dz = ((z + 0.5f) / depth - v) * fogOfWarSize.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                if (distance >= 1) continue;
                float falloff = softness <= 0 ? 1 : 1 - Mathf.SmoothStep(0, 1,
                    Mathf.InverseLerp(1 - Mathf.Clamp01(softness), 1, distance));
                float blend = Mathf.Clamp01(opacity) * falloff;
                if (blend <= 0) continue;
                int index = (z - z0) * patchWidth + x - x0;
                Color pixel = pixels[index];
                if (reset) {
                    pixel.r *= 1 - blend;
                    pixel.g *= 1 - blend;
                }
                else {
                    float current = baseHeight * (1 - Mathf.Clamp01(pixel.g)) + pixel.r;
                    pixel.r = Mathf.Lerp(current, Mathf.Max(0.05f, height), blend);
                    pixel.g = 1;
                }
                pixels[index] = pixel;
                changed = true;
            }
            if (!changed) return false;
            fogHeightMap.SetPixels(x0, z0, patchWidth, patchHeight, pixels);
            fogHeightMap.Apply(false, false);
            UpdateHeightMapMaterial();
            return true;
        }
    }
}
