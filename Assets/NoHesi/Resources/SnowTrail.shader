// Texture de tassement de la neige (World/SnowTrails.cs).
// Passe 0 : recopie décalée (la texture suit le joueur) avec comblement lent par la neige qui tombe.
// Passe 1 : empreinte d'une roue entre deux positions (capsule à bord doux), on garde le tassement le plus fort.
Shader "Hidden/NoHesi/SnowTrail"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Scroll;      // xy = décalage en uv, z = facteur de comblement

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv + _Scroll.xy;
                half inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                half d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r * inside;
                return half4(d * _Scroll.z, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            BlendOp Max
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // uv = (le long de l'empreinte, en travers) en mètres ; data = (longueur, rayon, tassement)
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float3 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 data : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                // position donnée directement en coordonnées de la texture (0-1), sans matrice de caméra
                float2 c = v.positionOS.xy * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    c.y = -c.y;
                #endif
                o.positionCS = float4(c, 0.5, 1.0);
                o.uv = v.uv;
                o.data = v.data;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float along = clamp(i.uv.x, 0.0, i.data.x);
                float dist = length(float2(i.uv.x - along, i.uv.y));
                half d = i.data.z * smoothstep(i.data.y, i.data.y * 0.45, dist);
                return half4(d, 0, 0, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
