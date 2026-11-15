// Eau des lacs dans la direction artistique des voitures : aplat profond, reflet du ciel en incidence rasante,
// fines crêtes blanches nettes qui dérivent lentement, reflet du soleil par le lissé. Opaque.
Shader "NoHesi/Water"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Bruits (celle de la route)", 2D) = "gray" {}
        _NoiseScale ("Taille de la tuile de bruit (m)", Float) = 32
        _Deep ("Eau profonde", Color) = (0.1, 0.36, 0.46, 1)
        _Sky ("Reflet du ciel", Color) = (0.58, 0.78, 0.88, 1)
        _Crest ("Crêtes", Color) = (0.9, 0.96, 1, 1)
        _Flow ("Dérive (m/s, x z)", Vector) = (0.35, 0, 0.2, 0)
        _Smoothness ("Lissé", Range(0, 1)) = 0.86
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
        CBUFFER_START(UnityPerMaterial)
            float _NoiseScale, _Smoothness;
            half4 _Deep, _Sky, _Crest;
            float4 _Flow;
        CBUFFER_END
        float4 _NoHesiTexOrigin;
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
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
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 xz = input.positionWS.xz - _NoHesiTexOrigin.xz;
                float t = _Time.y;
                half a = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, (xz + _Flow.xz * t) / _NoiseScale).b;
                half b = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, (xz - _Flow.zx * t * 1.3) / (_NoiseScale * 0.625) + 0.41).b;
                float w = a * 0.55 + b * 0.45;
                // Crêtes : fine bande nette autour d'un niveau du bruit
                float px = max(fwidth(w), 1e-4);
                half crest = saturate((0.03 - abs(w - 0.66)) / px + 0.5) * saturate(0.06 / px);

                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                half3 albedo = lerp(_Deep.rgb, _Sky.rgb, fres * 0.85);
                albedo = lerp(albedo, _Crest.rgb, crest * 0.85);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
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
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.smoothness = lerp(_Smoothness, 0.3, crest);
                surface.occlusion = 1;
                surface.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
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
            half4 NormalsFrag(Varyings input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0.0); }
            ENDHLSL
        }
    }
    Fallback Off
}
