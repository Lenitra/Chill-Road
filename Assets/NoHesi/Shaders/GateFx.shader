// Portes des courses : lumière posée sur la route et fumée, sans rien de solide en travers de la voie.
// Trois usages, un mot-clé chacun :
// - _GATE_GROUND  : ligne lumineuse en travers de la chaussée, halo, barres qui défilent vers la ligne (uv en mètres :
//                   x = écart à l'axe de la porte, y = distance à la ligne, négative avant elle) ;
// - _GATE_CURTAIN : voile de brume éclairé au-dessus de la ligne (uv : x en mètres, y = 0 au sol, 1 en haut) ;
// - _GATE_SMOKE   : bouffée de fumée colorée (particule ; couleur et opacité par sommet).
// Tout s'efface près de la caméra (_Near : début et fin du fondu, en mètres) : rien ne brouille la vue au passage.
Shader "NoHesi/GateFx"
{
    Properties
    {
        [HDR] _Tint ("Teinte", Color) = (1, 0.3, 0.36, 1)
        _Strength ("Intensité", Float) = 1
        _Half ("Demi-largeur de la porte (m)", Float) = 6
        _Back ("Longueur du tapis avant la ligne (m)", Float) = 16
        _Near ("Fondu près de la caméra (début, fin, plancher)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GateFx"
            Tags { "LightMode" = "UniversalForward" }
            // Prémultiplié : la couleur s'ajoute, l'alpha assombrit un peu ce qu'il y a dessous (lisible en plein jour)
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _GATE_GROUND _GATE_CURTAIN _GATE_SMOKE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float4 _Near;
                float _Strength, _Half, _Back;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                // xyz : distance à la caméra, visibilité dans le brouillard, libre
                float3 misc : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = input.uv;
                o.color = input.color;
                o.misc.x = distance(ws, _WorldSpaceCameraPos);
                o.misc.y = ComputeFogIntensity(ComputeFogFactor(o.positionCS.z));
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // Fondu près de la caméra : 0 en deçà de _Near.x, 1 au-delà de _Near.y, jamais sous le plancher _Near.z
            half NearFade(float dist)
            {
                return max(_Near.z, smoothstep(_Near.x, max(_Near.y, _Near.x + 0.01), dist));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 tint = _Tint.rgb;
                half3 rgb = 0;
                half alpha = 0;
                float t = _Time.y;

            #if defined(_GATE_GROUND)
                float x = input.uv.x, z = input.uv.y, ax = abs(x);
                // Bouts de la ligne : elle s'arrête aux projecteurs
                half ends = 1.0 - smoothstep(_Half - 0.9, _Half - 0.1, ax);
                // Ligne nette (0,5 m) de la couleur du tracé, filet blanc en son milieu
                float pz = max(fwidth(z), 0.001);
                half core = (1.0 - smoothstep(0.25 - pz, 0.25 + pz, abs(z))) * ends;
                half hot = (1.0 - smoothstep(0.07 - pz, 0.07 + pz, abs(z))) * ends;
                // Halo autour de la ligne, tapis de lumière qui s'éteint en s'éloignant (surtout du côté d'où l'on arrive)
                half before = saturate(-z / _Back);
                half halo = exp(-abs(z) * 1.1) * 0.6;
                half carpet = z < 0.0 ? pow(1.0 - before, 2.2) * 0.16 : exp(-z * 0.9) * 0.16;
                // Barres fines qui glissent vers la ligne, de plus en plus pâles en amont
                float f = frac(-z / 3.2 + t * 0.55);
                float pf = max(fwidth(z) / 3.2, 0.0005);
                half bar = 1.0 - smoothstep(0.04 - pf, 0.04 + pf, min(f, 1.0 - f));
                bar *= step(z, -0.7) * pow(1.0 - before, 1.5) * 0.5;
                // Flaques de lumière au pied des deux projecteurs
                float2 d = float2(ax - (_Half - 0.35), z);
                half pool = exp(-dot(d, d) * 0.75) * 0.8;
                // Bords du maillage : rien ne se coupe net
                half rim = (1.0 - smoothstep(_Half + 0.2, _Half + 1.4, ax)) * (1.0 - smoothstep(0.82, 1.0, before));
                half glow = ((halo + carpet + bar) * ends + pool) * rim;
                rgb = tint * (glow + core * 1.15) + hot * half3(0.3, 0.3, 0.3);
                alpha = saturate(glow * 0.45 + core * 0.9);
                half near = NearFade(input.misc.x);
                rgb *= near;
                alpha *= near;

            #elif defined(_GATE_CURTAIN)
                float h = input.uv.y;
                half ends = 1.0 - smoothstep(_Half - 1.2, _Half - 0.1, abs(input.uv.x));
                // Volutes qui dérivent et montent lentement
                float n = Noise(float2(input.uv.x * 0.42 + t * 0.11, h * 2.1 - t * 0.2)) * 0.62
                        + Noise(float2(input.uv.x * 1.1 - t * 0.17, h * 4.6 - t * 0.34)) * 0.38;
                half wisp = smoothstep(0.3, 0.78, n);
                half a = pow(1.0 - h, 2.6) * (0.22 + 0.78 * wisp) * ends * NearFade(input.misc.x);
                rgb = tint * a * 0.5;
                alpha = a * 0.05;

            #else
                // Bouffée : disque au bord ondulé et doux, plus dense au cœur
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float ang = atan2(p.y, p.x);
                float wob = 1.0 + 0.07 * sin(ang * 5.0 + input.color.a * 9.0) + 0.045 * sin(ang * 9.0 - input.color.r * 7.0);
                half body = 1.0 - smoothstep(0.3, 1.0, r * wob);
                half a = body * input.color.a * NearFade(input.misc.x);
                rgb = input.color.rgb * a;
                alpha = a * 0.9;
            #endif

                half k = _Strength * input.misc.y;
                return half4(rgb * k, saturate(alpha * k));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
