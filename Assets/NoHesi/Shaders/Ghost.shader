// Fantôme du record d'une course : la voiture du meilleur temps, en coque translucide d'une seule teinte.
// Deux passes : la première n'écrit que la profondeur (on ne voit pas l'intérieur du modèle au travers de la caisse),
// la seconde pose la teinte, plus dense sur les bords vus de biais (silhouette lisible, faces presque vides).
// Sans éclairage ni ombre : le fantôme se lit de jour comme de nuit, et ne cache jamais la route.
Shader "NoHesi/Ghost"
{
    Properties
    {
        _Tint ("Teinte", Color) = (0.45, 0.85, 1, 1)
        _Alpha ("Opacité d'ensemble", Range(0, 1)) = 1
        _Body ("Opacité des faces", Range(0, 1)) = 0.34
        _Rim ("Opacité des bords", Range(0, 1)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _Alpha, _Body, _Rim;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            float3 viewWS : TEXCOORD1;
            half fog : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 ws = TransformObjectToWorld(input.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(ws);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.viewWS = GetWorldSpaceViewDir(ws);
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half rim = pow(1.0 - facing, 2.2);
                half3 rgb = lerp(_Tint.rgb, half3(1, 1, 1), rim * 0.5);
                rgb = MixFog(rgb, input.fog);
                return half4(rgb, lerp(_Body, _Rim, rim) * _Alpha);
            }
            ENDHLSL
        }
    }
}
