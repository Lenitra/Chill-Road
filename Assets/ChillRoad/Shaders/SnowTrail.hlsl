// Neige déformable : traces creusées par les roues, lues par les shaders du sol et de la route.
// World/SnowTrails.cs tient une texture de tassement (0 = neige intacte, 1 = tassée à fond) centrée sur le joueur.
#ifndef CHILLROAD_SNOW_TRAIL_INCLUDED
#define CHILLROAD_SNOW_TRAIL_INCLUDED

TEXTURE2D(_ChillRoadSnowTrail);
float4 _ChillRoadSnowTrailRect;     // xy = coin sud-ouest (m), z = 1 / côté, w = taille d'un texel (m)
float _ChillRoadSnowTrailOn;

struct SnowTrail
{
    half depth;      // tassement au point vu (0-1)
    half2 slope;     // pente du sillon (dérivées en x et z), pour l'éclairage de ses bords
    half rim;        // bourrelet de neige repoussée au bord du sillon (0-1)
};

half SnowTrailTap(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_ChillRoadSnowTrail, sampler_LinearClamp, uv, 0).r;
}

// pos : point du sol ; viewWS : direction vers la caméra ; deep : profondeur du sillon en mètres (décalage de parallaxe)
SnowTrail SampleSnowTrail(float3 pos, float3 viewWS, float deep)
{
    SnowTrail t = (SnowTrail)0;
    if (_ChillRoadSnowTrailOn < 0.5) return t;
    float2 uv = (pos.xz - _ChillRoadSnowTrailRect.xy) * _ChillRoadSnowTrailRect.z;
    float2 e = abs(uv - 0.5) * 2.0;
    half inside = saturate((1.0 - max(e.x, e.y)) * 12.0);
    if (inside <= 0.0) return t;
    // parallaxe en un pas : le fond du sillon est vu décalé vers l'arrière
    half d0 = SnowTrailTap(uv);
    float2 shift = viewWS.xz / max(viewWS.y, 0.25) * (deep * d0) * _ChillRoadSnowTrailRect.z;
    uv -= shift;
    float px = _ChillRoadSnowTrailRect.w * _ChillRoadSnowTrailRect.z * 1.5;
    half d = SnowTrailTap(uv);
    half dx = SnowTrailTap(uv + float2(px, 0.0)) - SnowTrailTap(uv - float2(px, 0.0));
    half dz = SnowTrailTap(uv + float2(0.0, px)) - SnowTrailTap(uv - float2(0.0, px));
    t.depth = d * inside;
    t.slope = half2(dx, dz) * inside;
    // bourrelet : là où le sillon commence (tassement faible mais pente marquée)
    t.rim = saturate(length(half2(dx, dz)) * 2.2) * saturate(1.0 - d * 2.2) * inside;
    return t;
}
#endif
