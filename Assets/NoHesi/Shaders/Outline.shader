// Contours façon dessin animé, en plein écran (NoHesi.Rendering.OutlineFeature) : bords détectés sur la profondeur
// et les normales de la scène. Le trait assombrit la couleur de l'image (il garde sa teinte) au lieu d'être noir,
// s'affine puis s'efface avec la distance, et ignore le ciel. Les petites différences de profondeur (herbe sur le
// sol, pente rasante du terrain) ne font pas de trait.
Shader "NoHesi/Outline"
{
    Properties
    {
        _Thickness ("Épaisseur (px à 1080p)", Range(0.5, 3)) = 1.2
        _Darkness ("Couleur du trait (× image)", Color) = (0.22, 0.2, 0.28, 1)
        _Strength ("Force", Range(0, 1)) = 0.85
        _DepthThreshold ("Seuil profondeur (relatif)", Range(0.005, 0.5)) = 0.07
        _NormalThreshold ("Seuil normales", Range(0.05, 1)) = 0.45
        _FadeStart ("Fondu : début (m)", Float) = 50
        _FadeEnd ("Fondu : fin (m)", Float) = 220
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "Outline"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _Thickness, _Strength, _DepthThreshold, _NormalThreshold, _FadeStart, _FadeEnd;
            half4 _Darkness;

            float EyeDepth(float2 uv, out bool sky)
            {
                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    sky = raw <= 1e-6;
                #else
                    sky = raw >= 1.0 - 1e-6;
                #endif
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                bool sky0;
                float d0 = EyeDepth(uv, sky0);
                float3 n0 = SampleSceneNormals(uv);

                float px = _Thickness * max(_ScreenParams.y / 1080.0, 0.5);
                // L'épaisseur diminue un peu au loin
                px *= lerp(1.0, 0.6, saturate((d0 - _FadeStart) / max(_FadeEnd - _FadeStart, 1.0)));
                float2 o = px / _ScreenParams.xy;
                float2 offs[4] = { float2(o.x, o.y), float2(-o.x, -o.y), float2(o.x, -o.y), float2(-o.x, o.y) };

                // Surfaces vues en biais : seuil de profondeur relevé (sinon le terrain se strie de traits)
                float3 viewDir = normalize(mul((float3x3)UNITY_MATRIX_V, n0));
                float facing = sky0 ? 1.0 : saturate(abs(viewDir.z));
                float depthThr = _DepthThreshold * (1.0 + 6.0 * (1.0 - facing) * (1.0 - facing));

                float edge = 0.0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    bool skyI;
                    float di = EyeDepth(uv + offs[i], skyI);
                    if (sky0 && skyI) continue;
                    // Bord de silhouette : le voisin est nettement plus loin (ou le ciel) que ce pixel, ou l'inverse
                    float nearD = min(d0, di);
                    float dd = (sky0 != skyI) ? 1.0 : abs(di - d0) / max(nearD, 0.1);
                    edge = max(edge, smoothstep(depthThr, depthThr * 1.6, dd));
                    if (!sky0 && !skyI)
                    {
                        float3 ni = SampleSceneNormals(uv + offs[i]);
                        float nd = 1.0 - saturate(dot(n0, ni));
                        edge = max(edge, smoothstep(_NormalThreshold, _NormalThreshold * 1.5, nd) * 0.85);
                    }
                }

                float dist = sky0 ? 0.0 : d0;
                // Un pixel de ciel au bord d'un objet prend la distance de l'objet
                if (sky0) { bool s; dist = min(min(EyeDepth(uv + offs[0], s), EyeDepth(uv + offs[1], s)), min(EyeDepth(uv + offs[2], s), EyeDepth(uv + offs[3], s))); }
                edge *= 1.0 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1.0));

                color.rgb = lerp(color.rgb, color.rgb * _Darkness.rgb, edge * _Strength);
                return color;
            }
            ENDHLSL
        }
    }
}
