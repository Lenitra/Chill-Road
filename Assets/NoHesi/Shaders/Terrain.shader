// Sol du terrain dans la direction artistique des voitures : facettes low poly et aplats de couleur, sans texture peinte.
// - éclairage à facettes : normale de chaque triangle (dérivées de la position), sans toucher au maillage ;
// - aplats nets : prairie, herbe grasse, herbe sèche, sous-bois, terre battue, roche (sur les facettes en pente) ;
// - couleurs de sommet issues de la génération : r = sous-bois (humus), g = herbe sèche, b = roche, a = terre battue ;
// - une texture de bruits doux (celle de la route) découpe les contours ; grandes plaques de teinte très discrètes.
Shader "NoHesi/Terrain"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Bruits (R plaques, G formes, B détours, A éclats)", 2D) = "gray" {}
        _NoiseScale ("Taille de la tuile de bruit (m)", Float) = 32
        _Facets ("Facettes (0 lisse, 1 low poly)", Range(0, 1)) = 1
        _Grass ("Prairie", Color) = (0.33, 0.56, 0.22, 1)
        _GrassLush ("Herbe grasse", Color) = (0.25, 0.48, 0.19, 1)
        _GrassDry ("Herbe sèche", Color) = (0.62, 0.62, 0.3, 1)
        _Forest ("Sous-bois", Color) = (0.36, 0.32, 0.2, 1)
        _Dirt ("Terre battue", Color) = (0.57, 0.49, 0.36, 1)
        _Rock ("Roche", Color) = (0.5, 0.5, 0.49, 1)
        _RockDark ("Roche sombre", Color) = (0.4, 0.4, 0.4, 1)
        _RockSlope ("Pente de la roche (1 - n.y)", Range(0, 1)) = 0.3
        _Paved ("Pavé (places, cours de la ville)", Color) = (0.55, 0.54, 0.52, 1)
        _Snow ("Neige des sommets", Color) = (0.93, 0.95, 0.98, 1)
        _Smoothness ("Lissé", Range(0, 1)) = 0.04
        _Toon ("Éclairage cartoon (aplats)", Range(0, 1)) = 1
        _ShadeColor ("Cartoon : teinte de l'ombre", Color) = (0.6, 0.64, 0.8, 1)
        _ShadeLevel ("Cartoon : clarté de l'ombre", Range(0, 1)) = 0.68
        _RampSoft ("Cartoon : douceur du bord", Range(0.01, 0.6)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

        CBUFFER_START(UnityPerMaterial)
            float _NoiseScale, _Facets, _RockSlope, _Smoothness, _Toon, _ShadeLevel, _RampSoft;
            half4 _ShadeColor;
            half4 _Grass, _GrassLush, _GrassDry, _Forest, _Dirt, _Rock, _RockDark, _Paved, _Snow;
        CBUFFER_END

        // Origine des coordonnées de bruit (multiple de 960 m proche de la caméra), fournie par RoadNetwork
        float4 _NoHesiTexOrigin;
        // Sol mouillé (0-1), fourni par la météo (WeatherSystem)
        float _NoHesiWet;

        // Seuil net : 0 → 1 quand x dépasse e, sur environ un pixel
        half Crisp(float x, float e)
        {
            return saturate((x - e) / max(fwidth(x), 1e-5) + 0.5);
        }

        // Couleur du sol ; nFlat = normale de la facette (roche sur les pentes)
        // extra (UV2 du maillage) : x = pavé, y = neige
        half3 GroundAlbedo(float3 p, half3 nFlat, half4 mask, half2 extra)
        {
            float2 xz = p.xz - _NoHesiTexOrigin.xz;
            half4 n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, xz / _NoiseScale);
            half huge = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, xz / (_NoiseScale * 5.0) + 0.37).r;
            half giant = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, xz / (_NoiseScale * 15.0) + 0.71).g;

            // Prairie : grandes plaques d'herbe grasse et d'herbe sèche aux contours nets
            half3 col = _Grass.rgb;
            col = lerp(col, _GrassLush.rgb, Crisp(huge * 0.6 + giant * 0.6 + (n.g - 0.5) * 0.25, 0.66));
            col = lerp(col, _GrassDry.rgb, Crisp(mask.g + (huge - 0.5) * 0.5 + (n.g - 0.5) * 0.3, 0.58));

            // Sous-bois sous les arbres, terre battue au bord des routes
            col = lerp(col, _Forest.rgb * (1.0 + (n.r - 0.5) * 0.2), Crisp(mask.r + (n.b - 0.5) * 0.45, 0.5));
            col = lerp(col, _Dirt.rgb * (1.0 + (n.r - 0.5) * 0.12), Crisp(mask.a + (n.b - 0.5) * 0.5, 0.55));

            // Pavé des places et des cours : dalles en deux tons
            col = lerp(col, _Paved.rgb * (Crisp(n.g, 0.5) * 0.08 + 0.96), Crisp(extra.x + (n.b - 0.5) * 0.3, 0.5));

            // Roche sur les facettes en pente et dans les zones rocheuses (deux tons)
            half slope = 1.0 - nFlat.y;
            half rock = Crisp(slope + (n.a - 0.5) * 0.1 + mask.b * 0.35, _RockSlope);
            // (deux tons en grandes plaques : un seuil sur le bruit fin crénelait en « pointillés » au loin)
            col = lerp(col, lerp(_Rock.rgb, _RockDark.rgb, Crisp(huge * 0.75 + n.r * 0.25, 0.52)), rock);

            // Neige des sommets : tient sur les facettes peu inclinées, laisse la roche à nu dans les parois
            col = lerp(col, _Snow.rgb, Crisp(extra.y + (n.g - 0.5) * 0.35 - slope * 0.55, 0.42));

            // Très grandes variations de teinte, à peine visibles
            return col * (1.0 + (giant - 0.5) * 0.1);
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
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half2 extra : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.extra = input.uv1;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Normale de la facette (triangle) : dérivées écran de la position, orientée vers le haut
                float3 nFlat = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                nFlat *= nFlat.y < 0.0 ? -1.0 : 1.0;
                half3 n = normalize(lerp(normalize(input.normalWS), nFlat, _Facets));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = GroundAlbedo(input.positionWS, nFlat, input.color, input.extra) * lerp(1.0, 0.8, _NoHesiWet);
                surface.alpha = 1;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = lerp(_Smoothness, 0.22, _NoHesiWet);
                surface.occlusion = lerp(1.0, 0.8, saturate(input.color.r));
                surface.normalTS = half3(0, 0, 1);

                half4 color;
                if (_Toon > 0.5)
                {
                    // Aplats cartoon (comme NoHesi/Toon) : rampe douce, ombre teintée, ambiance du ciel aplatie
                    Light main = GetMainLight(inputData.shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                    half shadow = smoothstep(0.15, 0.85, main.shadowAttenuation * main.distanceAttenuation);
                    half ao = surface.occlusion;
                    #if defined(_SCREEN_SPACE_OCCLUSION)
                        AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                        ao *= ssao.indirectAmbientOcclusion;
                    #endif
                    half lit = smoothstep(0.05 - _RampSoft, 0.05 + _RampSoft, dot(n, main.direction)) * shadow;
                    half3 c = surface.albedo * main.color * lerp(_ShadeColor.rgb * _ShadeLevel, 1.0, lit);
                    c += surface.albedo * lerp(SampleSH(n), SampleSH(half3(0, 1, 0)), 0.5) * (0.85 * ao);
                    #if defined(_ADDITIONAL_LIGHTS)
                        uint count = GetAdditionalLightsCount();
                        LIGHT_LOOP_BEGIN(count)
                            Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                            half l = smoothstep(-0.05, 0.25, dot(n, light.direction));
                            c += surface.albedo * light.color * (light.distanceAttenuation * light.shadowAttenuation * l);
                        LIGHT_LOOP_END
                    #endif
                    // Sol mouillé : un peu de reflet du ciel
                    c += GlossyEnvironmentReflection(reflect(-inputData.viewDirectionWS, n), 0.6, 1.0) * (0.12 * _NoHesiWet);
                    color = half4(c, 1.0);
                }
                else
                    color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings NormalsVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
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
    Fallback Off
}
