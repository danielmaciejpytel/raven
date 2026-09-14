#ifndef VOLUMETRIC_FOG_LOCAL_HEIGHT
#define VOLUMETRIC_FOG_LOCAL_HEIGHT
#if VF2_HEIGHT_MAP
sampler2D _FogHeightMap;
float4 _FogHeightMapBounds; // centre XZ, coverage XZ

float ApplyLocalFogHeight(float3 wpos, inout float3 extents) {
    float2 uv = (wpos.xz - _FogHeightMapBounds.xy) / _FogHeightMapBounds.zw + 0.5;
    if (any(uv < 0) || any(uv > 1)) return 0;
    float2 height = tex2Dlod(_FogHeightMap, float4(uv, 0, 0)).rg;
    float weight = saturate(height.g);
    // R is premultiplied by G so bilinear filtering against unpainted texels stays smooth.
    extents.y = max(0.05, extents.y * (1.0 - weight) + height.r);
    return weight;
}
#endif
#endif
