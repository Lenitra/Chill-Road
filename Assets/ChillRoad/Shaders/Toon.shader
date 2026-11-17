// Rendu cartoon des modèles du jeu (voitures, décor du pack alpin, pack environnement).
// - couleur : palette de dégradés (UV0 = colonne × hauteur) × _BaseColor (peinture teintée par bloc de propriétés) ;
// - lumière principale en aplats : rampe douce à deux tons, ombres teintées (froides) plutôt que noires ;
// - reflet spéculaire « dessiné » (tache nette) dont la taille suit _Smoothness, liseré de contre-jour ;
// - ambiance du ciel adoucie, lumières additionnelles (phares, lampadaires) en aplats doux ;
// - émission (_EMISSION : optiques, fenêtres éclairées la nuit), brouillard, SSAO, instanciation, SRP Batcher ;
// - vent dans les arbres : seuls les sommets marqués (UV2.y = 2, feuillus du pack alpin) bougent.
// Les noms des propriétés reprennent ceux d'URP/Lit (_BaseMap, _BaseColor, _Smoothness, _EmissionColor...) :
// le code du jeu (teinte du trafic, cycle jour/nuit, feux arrière) fonctionne sans changement.
Shader "ChillRoad/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Palette", 2D) = "white" {}
        [MainColor] _BaseColor ("Teinte", Color) = (1, 1, 1, 1)
        _Smoothness ("Brillance (taille du reflet)", Range(0, 1)) = 0.3
        _Metallic ("Métal (inutilisé, compatibilité Lit)", Range(0, 1)) = 0
        [HDR] _EmissionColor ("Émission", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap ("Carte d'émission", 2D) = "white" {}
        [Toggle(_EMISSION)] _EmissionToggle ("Émission active", Float) = 0

        _RampMid ("Rampe : seuil lumière/ombre", Range(-1, 1)) = 0.05
        _RampSoft ("Rampe : douceur du bord", Range(0.001, 0.5)) = 0.12
        _ShadeColor ("Teinte de l'ombre", Color) = (0.58, 0.62, 0.8, 1)
        _ShadeLevel ("Clarté de l'ombre", Range(0, 1)) = 0.62
        _AmbientFlat ("Ambiance aplatie (0 = suit la normale)", Range(0, 1)) = 0.55
        _AmbientStrength ("Force de l'ambiance", Range(0, 2)) = 0.85
        _SpecStrength ("Reflet : intensité", Range(0, 2)) = 0.7
        _RimStrength ("Liseré : intensité", Range(0, 1)) = 0.18
        _RimWidth ("Liseré : largeur", Range(0, 1)) = 0.32

        [Toggle(_DETAIL_LINES)] _DetailLines ("Traits de détail (planches, pierres, tuiles)", Float) = 0
        [NoScaleOffset] _DetailMap ("Motif par colonne de palette (R = 1 planches, 2 pierres, 3 tuiles, 4 écorce)", 2D) = "black" {}
        _DetailStrength ("Traits : intensité", Range(0, 1)) = 0.32
        [Toggle] _SnowCap ("Neige sur les faces tournées vers le ciel (zones enneigées de la carte)", Float) = 0
        _TreeWind ("Vent dans les arbres : amplitude", Range(0, 3)) = 1
        _TreeFlutter ("Vent : frémissement des feuilles", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        TEXTURE2D(_DetailMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _EmissionColor, _ShadeColor;
            half _Smoothness, _Metallic, _EmissionToggle;
            half _RampMid, _RampSoft, _ShadeLevel, _AmbientFlat, _AmbientStrength, _SpecStrength, _RimStrength, _RimWidth;
            half _DetailLines, _DetailStrength, _SnowCap;
            float _TreeWind, _TreeFlutter;
        CBUFFER_END

        // Neige (carte de la station de ski) : enneigement du sol lu dans la carte (MapTerrain), 0-1
        TEXTURE2D(_ChillRoadSnowMap);
        float4 _ChillRoadSnowRect;      // xy = coin sud-ouest de la carte, z = 1 / côté
        float _ChillRoadSnowOn;
        half SnowAt(float3 pos)
        {
            return (half)_ChillRoadSnowOn * SAMPLE_TEXTURE2D(_ChillRoadSnowMap, sampler_LinearClamp, (pos.xz - _ChillRoadSnowRect.xy) * _ChillRoadSnowRect.z).r;
        }

        // Instanciation procédurale (VegetationRenderer, RenderMeshIndirect) : matrices dans un tampon GPU rempli
        // seulement quand la sélection change (2026-11-16 ; le processeur renvoyait des dizaines de milliers de matrices
        // par image)
        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<float4x4> _ChillRoadInstances;
        #endif
        void ChillRoadInstanceSetup()
        {
        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            float4x4 m = _ChillRoadInstances[unity_InstanceID];
            unity_ObjectToWorld = m;
            float3x3 a = (float3x3)m;
            float3 c0 = cross(a[1], a[2]), c1 = cross(a[2], a[0]), c2 = cross(a[0], a[1]);
            float det = dot(a[0], c0);
            float3x3 inv = transpose(float3x3(c0, c1, c2)) / det;
            float3 t = -mul(inv, float3(m._m03, m._m13, m._m23));
            unity_WorldToObject = float4x4(float4(inv[0], t.x), float4(inv[1], t.y), float4(inv[2], t.z), float4(0, 0, 0, 1));
        #endif
        }

        // Réglage global de qualité (GameSettings) : 0 = éclairage simplifié (pas de reflet ni liseré)
        half _ChillRoadToonDetail;

        // Vent (GroundFlora, partagé avec l'herbe) : xy = direction au sol (x, z), z = force (1 calme, jusqu'à 2,7 en tempête)
        float4 _ChillRoadWind;

        // Vent dans les arbres (2026-11-16). Sommets marqués par l'export Blender (alp_lib.finish, fiches `wind`) :
        // UV1.x = balancement de l'arbre (m, croît comme le carré de la hauteur), UV1.y = frémissement (m, nul au pied
        // des feuilles), UV2 = (phase de la feuille ou du rameau, marque 2). Un champ de déplacement continu : le
        // maillage reste d'un seul tenant. Les autres modèles ne bougent pas : un canal
        // d'UV absent est remplacé par l'UV0 (toujours dans 0-1), d'où la marque à 2.
        float3 TreeWind(float3 positionWS, float2 w, float2 ph)
        {
            float3 d = float3(0, 0, 0);
            UNITY_BRANCH
            if (ph.y > 1.5)
            {
                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float scale = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                float2 dir = _ChillRoadWind.xy;
                dir = dot(dir, dir) > 1e-4 ? normalize(dir) : float2(0.8, 0.6);
                float force = _ChillRoadWind.z > 0.0 ? _ChillRoadWind.z : 1.0;
                float2 side = float2(-dir.y, dir.x);
                float t = _Time.y;
                float seed = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
                // Rafales qui traversent la vallée dans le sens du vent
                float gust = 0.5 + 0.5 * sin(dot(origin.xz, dir) * 0.045 - t * (0.8 + 0.25 * force))
                                       * sin(dot(origin.xz, side) * 0.021 + t * 0.37 + 1.3);
                // Balancement de tout l'arbre : poussé dans le sens du vent, oscille lentement, un peu de travers
                float bend = w.x * scale * force * _TreeWind;
                float push = (0.25 + 0.75 * gust) + 0.45 * sin(t * 1.15 + seed) * (0.5 + 0.5 * gust);
                d = float3(dir.x, 0.0, dir.y) * (bend * push)
                  + float3(side.x, 0.0, side.y) * (bend * 0.3 * sin(t * 0.83 + seed * 1.7));
                d.y -= bend * push * push * 0.08;
                // Frémissement des feuilles (et balancement des rameaux du saule) : chaque feuille à son rythme
                float f = w.y * scale * _TreeFlutter * (0.45 + 0.55 * gust) * (0.7 + 0.3 * force);
                float a = t * (4.6 + 1.2 * force) + ph.x * 6.2831853;
                d += float3(sin(a) * dir.x + cos(a * 0.87) * side.x, sin(a * 1.31 + 1.7) * 0.5,
                            sin(a) * dir.y + cos(a * 0.87) * side.y) * f;
            }
            return positionWS + d;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local _DETAIL_LINES
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ChillRoadInstanceSetup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 wind : TEXCOORD1;
                float2 windPhase : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                #if defined(_DETAIL_LINES)
                    float3 positionOS : TEXCOORD4;
                    half3 normalOS : TEXCOORD5;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TreeWind(TransformObjectToWorld(input.positionOS.xyz), input.wind, input.windPhase);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                #if defined(_DETAIL_LINES)
                    o.positionOS = input.positionOS.xyz;
                    o.normalOS = input.normalOS;
                #endif
                return o;
            }

            // Trait fin et net sur les valeurs entières de x ; s'efface quand les traits deviennent trop serrés
            // à l'écran (au loin), pour éviter le moiré
            half Line(float x, half width)
            {
                float w = fwidth(x);
                float d = abs(frac(x + 0.5) - 0.5);
                half aa = 1.0 - smoothstep(width, width + w * 1.5, d);
                return aa * (1.0 - saturate((w - 0.12) / 0.2));
            }

            float Hash11(float n) { return frac(sin(n * 127.1) * 43758.5453); }

            // Motifs dessinés (repère de l'objet, en mètres) : planches, moellons, rangs de tuiles, écorce
            half DetailLines(half id, float3 p, half3 n)
            {
                half3 an = abs(n);
                float2 tdir = normalize(float2(-n.z, n.x) + float2(1e-4, 0.0));
                float t = dot(p.xz, tdir);
                if (id < 0.5) return 0.0;
                if (id < 1.5)                                   // planches horizontales des murs en bois
                    return an.y < 0.6 ? Line(p.y / 0.21, 0.05) : 0.0;
                if (id < 2.5)                                   // pierres : assises et joints décalés
                {
                    if (an.y > 0.6) return 0.0;
                    float r = p.y / 0.3;
                    float row = floor(r);
                    float tt = t / (0.5 + 0.25 * Hash11(row)) + row * 0.5;
                    return max(Line(r, 0.06), Line(tt, 0.05) * 0.85);
                }
                if (id < 3.5)                                   // toits : rangs de tuiles et joints décalés
                {
                    if (n.y < 0.2) return 0.0;
                    float r = p.y / 0.15;
                    float row = floor(r);
                    return max(Line(r, 0.07), Line(t / 0.32 + row * 0.5, 0.05) * 0.6);
                }
                // écorce : stries verticales irrégulières
                if (an.y > 0.7) return 0.0;
                float s = t / 0.07;
                return Line(s + 0.35 * sin(p.y * 1.7 + floor(s) * 2.3), 0.08) * 0.8;
            }

            // Passage lumière → ombre en aplat, bord légèrement adouci
            half Ramp(half ndl)
            {
                return smoothstep(_RampMid - _RampSoft, _RampMid + _RampSoft, ndl);
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                #if defined(_DETAIL_LINES)
                    half id = SAMPLE_TEXTURE2D(_DetailMap, sampler_PointClamp, float2(input.uv.x, 0.5)).r * 255.0;
                    albedo *= 1.0 - DetailLines(id, input.positionOS, normalize(input.normalOS)) * _DetailStrength;
                #endif

                half3 n = normalize(input.normalWS);
                n = frontFace ? n : -n;
                // Décors des zones enneigées : la neige tient sur ce qui regarde le ciel (toits, branches, murets)
                UNITY_BRANCH
                if (_SnowCap > 0.5 && _ChillRoadSnowOn > 0.5)
                {
                    half snow = SnowAt(input.positionWS);
                    half cap = saturate((n.y - 0.38) * 6.0 + (snow - 0.75) * 2.0) * saturate(snow * 4.0 - 1.2);
                    albedo = lerp(albedo, half3(0.93, 0.95, 0.99), cap);
                }
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 0);
                #endif
                Light main = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));

                half indirectAO = 1.0, directAO = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(screenUV);
                    indirectAO = ssao.indirectAmbientOcclusion;
                    directAO = lerp(1.0, ssao.directAmbientOcclusion, 0.5);
                #endif

                // Lumière principale : rampe × ombre portée (adoucie, jamais un noir franc)
                half ndl = dot(n, main.direction);
                half shadow = main.shadowAttenuation * main.distanceAttenuation;
                half lit = Ramp(ndl) * smoothstep(0.15, 0.85, shadow) * directAO;
                half3 shade = _ShadeColor.rgb * _ShadeLevel;
                half3 color = albedo * main.color * lerp(shade, 1.0, lit);

                // Reflet dessiné (tache nette) et liseré de contre-jour
                half detail = _ChillRoadToonDetail > 0.5 ? 1.0 : 0.0;
                half3 h = SafeNormalize(main.direction + v);
                half ndh = saturate(dot(n, h));
                half size = exp2(10.0 * _Smoothness + 1.0);
                half spec = smoothstep(0.42, 0.5, pow(ndh, size)) * _Smoothness * _SpecStrength;
                color += main.color * spec * lit * detail;
                half rim = smoothstep(1.0 - _RimWidth, 1.0 - _RimWidth + 0.08, 1.0 - saturate(dot(n, v)));
                color += albedo * main.color * rim * _RimStrength * lerp(0.35, 1.0, lit) * detail;

                // Ambiance du ciel, en partie aplatie (moins de dégradés « réalistes » sur les volumes)
                half3 sky = lerp(SampleSH(n), SampleSH(half3(0, 1, 0)), _AmbientFlat);
                color += albedo * sky * (_AmbientStrength * indirectAO);

                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = screenUV;
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                        half l = smoothstep(-0.05, 0.25, dot(n, light.direction));
                        color += albedo * light.color * (light.distanceAttenuation * light.shadowAttenuation * l);
                    LIGHT_LOOP_END
                #endif

                #if defined(_EMISSION)
                    color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                #endif

                color = MixFog(color, input.fog);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ChillRoadInstanceSetup
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 wind : TEXCOORD1;
                float2 windPhase : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TreeWind(TransformObjectToWorld(input.positionOS.xyz), input.wind, input.windPhase);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ChillRoadInstanceSetup

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 wind : TEXCOORD1;
                float2 windPhase : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformWorldToHClip(TreeWind(TransformObjectToWorld(input.positionOS.xyz), input.wind, input.windPhase));
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ChillRoadInstanceSetup

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 wind : TEXCOORD1;
                float2 windPhase : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings NormalsVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformWorldToHClip(TreeWind(TransformObjectToWorld(input.positionOS.xyz), input.wind, input.windPhase));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 NormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
