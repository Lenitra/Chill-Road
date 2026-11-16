// Végétation au sol (herbes, fleurs, fougères, buissons bas, plantes de lac) : rendu doux, détaillé et vivant.
// - couleur : palette AlpFlora (UV0 = colonne, dégradé de la base à la pointe de chaque brin/pétale) ;
// - normales douces exportées par Blender (dômes, sphères des buissons) + éclairage enveloppant : aucun facettage ;
// - translucidité : les brins s'illuminent à contre-jour, surtout vers la pointe ;
// - occlusion à la base, grandes plaques de teinte très discrètes, légère variation par plante ;
// - vent : UV1.x = souplesse (m), UV1.y = phase ; rafales qui traversent le pré (texture de bruit), frémissement ;
// - fondu à distance : la plante rétrécit vers sa base avant de disparaître (_FadeEnd, 0 = jamais).
// Les deux faces sont dessinées (feuilles simples) ; les normales douces valent pour les deux côtés.
Shader "NoHesi/Foliage"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Palette (colonnes × dégradé)", 2D) = "white" {}
        [NoScaleOffset] _Noise ("Bruits (R rafales, G plaques)", 2D) = "gray" {}
        _Wrap ("Éclairage enveloppant", Range(0, 1)) = 0.55
        _Translucency ("Translucidité à contre-jour", Range(0, 2)) = 0.8
        _TransColor ("Teinte de la lumière traversante", Color) = (1, 0.95, 0.62, 1)
        _AOHeight ("Hauteur de l'ombre du pied (m)", Float) = 0.28
        _AOStrength ("Ombre du pied", Range(0, 1)) = 0.45
        _TintScale ("Taille des plaques de teinte (m)", Float) = 48
        _TintAmount ("Plaques de teinte", Range(0, 1)) = 0.35
        _TintColor ("Teinte des plaques", Color) = (1.08, 1.02, 0.72, 1)
        _Variation ("Variation par plante", Range(0, 0.3)) = 0.09
        _WindStrength ("Vent : amplitude", Float) = 0.22
        _WindSpeed ("Vent : vitesse", Float) = 1.0
        _WindScale ("Vent : taille des rafales (m, diviseur de 960)", Float) = 24
        _WindFlutter ("Vent : frémissement", Range(0, 1)) = 0.3
        _FadeEnd ("Fondu : distance de disparition (0 = jamais)", Float) = 0
        _Smoothness ("Lissé", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

        CBUFFER_START(UnityPerMaterial)
            float _Wrap, _Translucency, _AOHeight, _AOStrength, _TintScale, _TintAmount, _Variation;
            float _WindStrength, _WindSpeed, _WindScale, _WindFlutter, _FadeEnd, _Smoothness;
            half4 _TransColor, _TintColor;
        CBUFFER_END

        // Instanciation procédurale (VegetationRenderer, RenderMeshIndirect) : matrices dans un tampon GPU rempli
        // seulement quand la sélection change (2026-11-16 ; le processeur renvoyait des dizaines de milliers de matrices
        // par image)
        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<float4x4> _NoHesiInstances;
        #endif
        void NoHesiInstanceSetup()
        {
        #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            float4x4 m = _NoHesiInstances[unity_InstanceID];
            unity_ObjectToWorld = m;
            float3x3 a = (float3x3)m;
            float3 c0 = cross(a[1], a[2]), c1 = cross(a[2], a[0]), c2 = cross(a[0], a[1]);
            float det = dot(a[0], c0);
            float3x3 inv = transpose(float3x3(c0, c1, c2)) / det;
            float3 t = -mul(inv, float3(m._m03, m._m13, m._m23));
            unity_WorldToObject = float4x4(float4(inv[0], t.x), float4(inv[1], t.y), float4(inv[2], t.z), float4(0, 0, 0, 1));
        #endif
        }

        // Origine des coordonnées de bruit (multiple de 960 m proche de la caméra), fournie par RoadNetwork
        float4 _NoHesiTexOrigin;
        // Sol mouillé (0-1), fourni par la météo
        float _NoHesiWet;
        // Vent (GroundFlora) : xy = direction au sol (x, z), z = force (1 = temps calme), w = inutilisé
        float4 _NoHesiWind;
        // Défilement des rafales intégré par GroundFlora (unités de la texture de bruit, ramené modulo 64)
        float4 _NoHesiWindScroll;
        // Largeur du fondu de distance (m), fournie par GroundFlora
        float _NoHesiFloraFade;

        float3 ObjectOrigin() { return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23); }
        float ObjectScale() { return length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)); }

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        // Rétrécit la plante vers son pied à l'approche de la distance de disparition
        float FadeScale(float3 originWS)
        {
            if (_FadeEnd <= 0.0) return 1.0;
            float d = distance(originWS, _WorldSpaceCameraPos);
            float w = max(_NoHesiFloraFade, 1.0);
            return smoothstep(0.0, 1.0, saturate((_FadeEnd - d) / w));
        }

        // Déplacement dû au vent ; flex = souplesse (UV1.x, déjà mise à l'échelle), phase = UV1.y
        float3 WindOffset(float3 positionWS, float3 originWS, float flex, float phase)
        {
            if (flex <= 0.0) return 0;
            float2 dir = _NoHesiWind.xy;
            dir = dot(dir, dir) > 1e-4 ? normalize(dir) : float2(0.8, 0.6);
            float force = _NoHesiWind.z > 0.0 ? _NoHesiWind.z : 1.0;
            float t = _Time.y * _WindSpeed;
            float2 uv = (originWS.xz - _NoHesiTexOrigin.xz) / _WindScale - _NoHesiWindScroll.xy;
            float gust = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uv, 0).r;
            gust = smoothstep(0.25, 0.85, gust);
            float wave = sin(t * 1.6 + dot(originWS.xz, dir) * 0.45 + phase * 1.3);
            float flutter = sin(t * 7.1 + phase * 6.2832) * _WindFlutter * (0.4 + gust);
            float bend = _WindStrength * force * (0.25 + gust * 1.15 + wave * 0.22);
            float2 perp = float2(-dir.y, dir.x);
            float2 d = (dir * bend + perp * flutter * _WindStrength * force) * flex;
            float h = max(positionWS.y - originWS.y, 0.03);
            float drop = min(dot(d, d) / (2.0 * h), h * 0.6);       // la tige garde à peu près sa longueur
            return float3(d.x, -drop, d.y);
        }

        // Position monde finale d'un sommet (fondu puis vent)
        float3 FoliagePosition(float3 positionOS, float2 uv1, out float3 originWS)
        {
            originWS = ObjectOrigin();
            float s = FadeScale(originWS);
            float3 positionWS = TransformObjectToWorld(positionOS * s);
            float sc = ObjectScale() * s;
            positionWS += WindOffset(positionWS, originWS, uv1.x * sc * sc, uv1.y);
            return positionWS;
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

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:NoHesiInstanceSetup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 extra : TEXCOORD3;     // x = occlusion du pied, y = plaque de teinte, z = variation, w = brouillard
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 originWS;
                float3 positionWS = FoliagePosition(input.positionOS.xyz, input.uv1, originWS);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv0;
                float ao = saturate(input.positionOS.y / max(_AOHeight, 1e-3));
                float patch = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, (originWS.xz - _NoHesiTexOrigin.xz) / _TintScale + 0.37, 0).g;
                o.extra = half4(ao, smoothstep(0.45, 0.75, patch), Hash12(floor(originWS.xz * 10.0)) * 2.0 - 1.0,
                                ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                albedo = lerp(albedo, albedo * _TintColor.rgb, input.extra.y * _TintAmount);
                albedo *= 1.0 + input.extra.z * _Variation;
                albedo *= lerp(1.0, 0.82, _NoHesiWet);

                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half ao = lerp(1.0 - _AOStrength, 1.0, input.extra.x);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 0);
                #endif
                Light main = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half shadow = main.shadowAttenuation * main.distanceAttenuation;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(screenUV);
                    ao *= ssao.indirectAmbientOcclusion;
                #endif

                half nl = dot(n, main.direction);
                half wrap = saturate((nl + _Wrap) / (1.0 + _Wrap));
                half3 color = albedo * main.color * (wrap * shadow * lerp(0.75, 1.0, ao));

                // Contre-jour : la lumière traverse les brins (davantage vers la pointe)
                half back = pow(saturate(dot(v, -main.direction)), 3.0) * 0.75 + saturate(-nl) * 0.25;
                half tip = lerp(0.35, 1.0, input.uv.y);
                color += albedo * _TransColor.rgb * main.color * (back * _Translucency * tip * shadow);

                // Petit reflet doux (herbe mouillée surtout)
                half3 h = normalize(main.direction + v);
                half smooth = lerp(_Smoothness, 0.55, _NoHesiWet);
                half spec = pow(saturate(dot(n, h)), exp2(10.0 * smooth + 1.0)) * smooth * 0.35;
                color += main.color * spec * shadow;

                // Ambiance du ciel
                color += SampleSH(n) * albedo * ao;

                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = screenUV;
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                        half l = saturate((dot(n, light.direction) + _Wrap) / (1.0 + _Wrap));
                        color += albedo * light.color * (light.distanceAttenuation * light.shadowAttenuation * l);
                    LIGHT_LOOP_END
                #endif

                color = MixFog(color, InitializeInputDataFog(float4(input.positionWS, 1.0), input.extra.w));
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
            #pragma instancing_options procedural:NoHesiInstanceSetup
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 originWS;
                float3 positionWS = FoliagePosition(input.positionOS.xyz, input.uv1, originWS);
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
            #pragma instancing_options procedural:NoHesiInstanceSetup

            struct Attributes { float4 positionOS : POSITION; float2 uv1 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 originWS;
                return TransformWorldToHClip(FoliagePosition(input.positionOS.xyz, input.uv1, originWS));
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
            #pragma instancing_options procedural:NoHesiInstanceSetup

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv1 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings NormalsVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                float3 originWS;
                o.positionCS = TransformWorldToHClip(FoliagePosition(input.positionOS.xyz, input.uv1, originWS));
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
