// Ciel jour/nuit dans la direction artistique des voitures : dégradé du jour, nuages en aplats (deux tons, contours nets)
// sur un plafond qui dérive avec le vent, lueur du lever/coucher autour du soleil, nuit avec dégradé, étoiles
// scintillantes (cachées par les nuages) et lune. Piloté par DayNightCycle (_SunDir, _Night, _Glow, _DayTint...).
Shader "NoHesi/SkyDayNight"
{
    Properties
    {
        [NoScaleOffset] _CloudNoise ("Bruits des nuages (R grandes formes, G détails)", 2D) = "gray" {}
        _DayZenith ("Jour : zénith", Color) = (0.12, 0.34, 0.78, 1)
        _DayHorizon ("Jour : horizon", Color) = (0.62, 0.82, 0.95, 1)
        _Haze ("Sous l'horizon", Color) = (0.56, 0.68, 0.74, 1)
        _CloudLight ("Nuages : lumière", Color) = (1, 1, 1, 1)
        _CloudShade ("Nuages : ombre", Color) = (0.74, 0.8, 0.9, 1)
        _CloudCover ("Couverture nuageuse", Range(0, 1)) = 0.5
        _CloudScale ("Taille des nuages (m par tuile)", Float) = 4200
        _CloudHeight ("Altitude du plafond (m)", Float) = 1000
        _Wind ("Vent (m/s, x z)", Vector) = (6, 0, 2.5, 0)
        _DayTint ("Teinte du jour", Color) = (1, 1, 1, 1)
        _DayExposure ("Exposition du jour", Float) = 1
        _Night ("Nuit", Range(0, 1)) = 0
        _NightZenith ("Nuit : zénith", Color) = (0.006, 0.01, 0.03, 1)
        _NightHorizon ("Nuit : horizon", Color) = (0.03, 0.045, 0.09, 1)
        _GlowColor ("Lueur du couchant", Color) = (1, 0.45, 0.2, 1)
        _Glow ("Intensité de la lueur", Float) = 0
        _SunDir ("Direction du soleil", Vector) = (0, 1, 0, 0)
        _MoonDir ("Direction de la lune", Vector) = (0, -1, 0, 0)
        _SunDisk ("Disque solaire", Float) = 1
        _Stars ("Étoiles", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudNoise);
            SAMPLER(sampler_CloudNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _DayZenith, _DayHorizon, _Haze, _CloudLight, _CloudShade;
                float _CloudCover, _CloudScale, _CloudHeight;
                float4 _Wind;
                float4 _DayTint;
                float _DayExposure;
                float _Night;
                float4 _NightZenith;
                float4 _NightHorizon;
                float4 _GlowColor;
                float _Glow;
                float4 _SunDir;
                float4 _MoonDir;
                float _SunDisk;
                float _Stars;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // Étoiles : une chance sur ~50 par cellule d'une grille sur la sphère, taille et éclat variables
            float Stars(float3 d)
            {
                float3 p = d * 160.0;
                float3 cell = floor(p);
                float h = Hash(cell);
                if (h < 0.978) return 0;
                float3 jitter = float3(Hash(cell + 1.3), Hash(cell + 2.7), Hash(cell + 4.1)) - 0.5;
                float dist = length(p - (cell + 0.5 + jitter * 0.6));
                float twinkle = 0.7 + 0.3 * sin(_Time.y * (1.5 + h * 5.0) + h * 60.0);
                float core = saturate(1.0 - dist * 2.6);
                return core * core * (0.35 + Hash(cell + 7.7) * 1.4) * twinkle;
            }

            // Densité des nuages en un point du plafond (coordonnées en tuiles de bruit) : grandes formes arrondies
            float CloudDensity(float2 uv)
            {
                float2 n = SAMPLE_TEXTURE2D_LOD(_CloudNoise, sampler_CloudNoise, uv, 0).rg;
                return n.r * 0.75 + n.g * 0.25;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_SunDir.xyz);
                float3 moon = normalize(_MoonDir.xyz);
                float up = saturate(d.y);

                // Jour : dégradé zénith → horizon, brume sous l'horizon
                float3 day = lerp(_DayHorizon.rgb, _DayZenith.rgb, pow(up, 0.55));
                day = lerp(day, _Haze.rgb, saturate(-d.y * 8.0));

                // Nuages en aplats : plafond plan à _CloudHeight ; ombre propre = même nuage décalé vers le soleil.
                // Pas de branche : les dérivées (contours nets) restent définies jusqu'à l'horizon.
                float2 plane = d.xz / max(d.y, 0.015) * _CloudHeight + _Wind.xz * _Time.y;
                float2 uv = plane / _CloudScale;
                float dens = CloudDensity(uv);
                float edge = lerp(0.78, 0.5, _CloudCover);
                float px = max(fwidth(dens), 1e-4);
                float cloud = saturate((dens - edge) / px + 0.5) * smoothstep(0.015, 0.14, d.y);
                float2 toSun = normalize(sun.xz + 1e-4) * 0.035;
                float shade = saturate((CloudDensity(uv + toSun) - edge - 0.04) / px + 0.5);
                day = lerp(day, lerp(_CloudLight.rgb, _CloudShade.rgb, shade), cloud);
                day *= _DayTint.rgb * _DayExposure;

                float3 night = lerp(_NightHorizon.rgb, _NightZenith.rgb, sqrt(up));
                night += Stars(d) * _Stars * (1.0 - cloud) * saturate(d.y * 6.0);
                float mu = dot(d, moon);
                night += float3(0.85, 0.9, 1.0) * (smoothstep(0.99955, 0.9998, mu) * 1.6 + pow(saturate(mu), 48.0) * 0.06);
                night = lerp(night, _NightHorizon.rgb * 1.8 + pow(saturate(mu), 6.0) * 0.04, cloud * 0.75);

                float3 col = lerp(day, night, _Night);

                // Lueur autour du soleil, surtout près de l'horizon (aube, crépuscule)
                float sd = dot(d, sun);
                float horizon = pow(1.0 - abs(d.y), 3.0);
                col += _GlowColor.rgb * _Glow * (pow(saturate(sd * 0.5 + 0.5), 5.0) * horizon + pow(saturate(sd), 24.0) * 0.6);
                // Disque solaire (renforcé par le bloom), caché par les nuages
                col += float3(1.0, 0.9, 0.75) * smoothstep(0.99955, 0.9999, sd) * 16.0 * _SunDisk * saturate(sun.y * 12.0 + 0.4) * (1.0 - cloud);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
