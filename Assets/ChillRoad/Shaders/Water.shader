// Eau des lacs et des rivières, style cartoon. Réglage retenu : eau claire émeraude (fond sableux deviné près de la
// rive, réseau de lumière sur les hauts-fonds). Couleur selon la profondeur (dégradé ou paliers nets), écume de rive
// animée et ronds qui viennent mourir sur la berge, motif de surface au choix (crêtes, cellules, éclats, facettes,
// risées), reflet du ciel en incidence rasante et scintillement dessiné du soleil. Opaque.
// Maillage (MapTerrain) : UV0.x = profondeur (m), UV0.y = 1 pour une rivière ; UV1 = (travers, long du cours) en mètres.
Shader "ChillRoad/Water"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Bruits (celle de la route)", 2D) = "gray" {}
        _NoiseScale ("Taille de la tuile de bruit (m)", Float) = 32

        [Header(Couleur selon la profondeur)]
        _Shallow ("Eau de rive", Color) = (0.74, 0.7, 0.5, 1)
        _Mid ("Eau moyenne", Color) = (0.14, 0.52, 0.42, 1)
        _Deep ("Eau profonde", Color) = (0.03, 0.22, 0.27, 1)
        _DepthRange ("Profondeur de l'eau profonde (m)", Float) = 9
        _Bands ("Paliers nets (0 = dégradé)", Float) = 0

        [Header(Rive)]
        _Foam ("Écume", Color) = (0.95, 0.98, 0.95, 1)
        _FoamWidth ("Écume : profondeur couverte (m)", Float) = 0.05
        _Rings ("Ronds vers la berge (0-1)", Range(0, 1)) = 0.25
        _RingReach ("Ronds : profondeur de départ (m)", Float) = 3

        [Header(Surface)]
        _Pattern ("Motif (0 aucun, 1 crêtes, 2 cellules, 3 éclats, 4 facettes, 5 risées)", Float) = 2
        _PatternColor ("Motif : couleur", Color) = (0.96, 0.97, 0.78, 1)
        _PatternStrength ("Motif : force", Range(0, 1)) = 0.6
        _PatternScale ("Motif : taille (m)", Float) = 3
        _PatternShore ("Motif : renfort près de la rive", Range(0, 1)) = 0
        _PatternDepth ("Motif : seulement jusqu à cette profondeur (m, 0 = partout)", Float) = 4
        _Flow ("Dérive (m/s, x z)", Vector) = (0.35, 0, 0.2, 0)
        _RiverSpeed ("Courant des rivières (m/s)", Float) = 2.2

        [Header(Reflets)]
        _Sky ("Reflet du ciel", Color) = (0.62, 0.82, 0.92, 1)
        _Fresnel ("Reflet du ciel : force", Range(0, 1)) = 0.3
        _Smoothness ("Lissé", Range(0, 1)) = 0.55
        _Glint ("Scintillement du soleil", Range(0, 2)) = 0.6
        _GlintSize ("Scintillement : étendue", Range(0.001, 0.2)) = 0.008
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
        CBUFFER_START(UnityPerMaterial)
            float _NoiseScale, _DepthRange, _Bands, _FoamWidth, _Rings, _RingReach;
            float _Pattern, _PatternStrength, _PatternScale, _PatternShore, _PatternDepth, _RiverSpeed;
            float _Fresnel, _Smoothness, _Glint, _GlintSize;
            half4 _Shallow, _Mid, _Deep, _Foam, _PatternColor, _Sky;
            float4 _Flow;
        CBUFFER_END
        float4 _ChillRoadTexOrigin;
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float4 data : TEXCOORD3; // profondeur, rivière, travers, long du cours
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
                o.data = float4(input.uv, input.uv1);
                return o;
            }

            float2 Hash22(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.xx + q.yz) * q.zy);
            }

            // Réseau de cellules : écart entre les deux germes les plus proches (0 sur les arêtes)
            float CellEdge(float2 p, float t)
            {
                float2 ip = floor(p), fp = frac(p);
                float f1 = 8.0, f2 = 8.0;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j);
                    float2 o = 0.5 + 0.38 * sin(t + 6.2831 * Hash22(ip + g));
                    float d = length(g + o - fp);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
                return f2 - f1;
            }

            // Facettes low poly : la surface découpée en triangles qui basculent, ton de la facette de -1 à 1
            float FacetHeight(float2 c, float t)
            {
                float2 h = Hash22(c);
                return sin(t * (0.7 + h.y * 0.9) + h.x * 6.2831);
            }
            float FacetTone(float2 p, float t)
            {
                float2 sk = p + (p.x + p.y) * 0.36602540;
                float2 i = floor(sk), f = frac(sk);
                float2 c1 = f.x > f.y ? float2(1, 0) : float2(0, 1);
                float2 ca = i, cb = i + c1, cc = i + 1.0;
                float2 a = ca - (ca.x + ca.y) * 0.21132487, b = cb - (cb.x + cb.y) * 0.21132487, c = cc - (cc.x + cc.y) * 0.21132487;
                float3 A = float3(a.x, FacetHeight(ca, t) * 0.16, a.y);
                float3 B = float3(b.x, FacetHeight(cb, t) * 0.16, b.y);
                float3 C = float3(c.x, FacetHeight(cc, t) * 0.16, c.y);
                float3 nf = normalize(cross(B - A, C - A));
                nf *= sign(nf.y);
                float3 ld = float3(0.58, 0.58, 0.58);
                float tone = clamp((dot(nf, ld) - ld.y) * 5.0, -1.0, 1.0);
                return floor(tone * 1.5 + 0.5) / 1.5;
            }

            // Bande nette et anticrénelée : 1 quand x < edge
            float Below(float x, float edge)
            {
                return saturate((edge - x) / max(fwidth(x), 1e-4) + 0.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float t = _Time.y;
                float depth = input.data.x;
                float river = step(0.5, input.data.y);

                // Repère du motif : monde qui dérive pour les lacs, cours de la rivière qui défile
                float2 xz = input.positionWS.xz - _ChillRoadTexOrigin.xz;
                float2 pa = lerp(xz + _Flow.xz * t, input.data.zw + float2(0, _RiverSpeed * t), river);
                float2 pb = lerp(xz - _Flow.zx * t * 1.3, input.data.zw * 1.6 + float2(3.7, _RiverSpeed * 1.5 * t), river);
                half a = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, pa / _NoiseScale).b;
                half b = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, pb / (_NoiseScale * 0.625) + 0.41).b;
                float w = a * 0.55 + b * 0.45;

                // Couleur selon la profondeur
                float d01 = saturate(depth / max(_DepthRange, 0.01));
                d01 = sqrt(d01);
                if (_Bands > 0.5)
                {
                    float x = d01 * _Bands + (w - 0.5) * 0.5;
                    float fx = frac(x);
                    d01 = saturate((floor(x) + saturate((fx - 0.5) / max(fwidth(x), 1e-4) + 0.5)) / _Bands);
                }
                half3 albedo = d01 < 0.5 ? lerp(_Shallow.rgb, _Mid.rgb, d01 * 2.0) : lerp(_Mid.rgb, _Deep.rgb, d01 * 2.0 - 1.0);

                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                albedo = lerp(albedo, _Sky.rgb, fres * _Fresnel);

                // Motif de surface
                half shoreBoost = 1.0 + _PatternShore * 3.0 * (1.0 - saturate(depth / max(_DepthRange * 0.4, 0.01)));
                half mark = 0;
                half shadeMark = 0;
                if (_Pattern > 4.5)
                {
                    // Risées : grandes plages allongées, un ton plus clair et un ton plus sombre, sans trait
                    float s = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(pa.x / (_PatternScale * 5.0), pa.y / _PatternScale)).b * 0.6
                        + SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(pb.x / (_PatternScale * 3.0), pb.y / (_PatternScale * 0.7)) + 0.37).b * 0.4;
                    float px = max(fwidth(s), 1e-4);
                    mark = saturate((s - 0.58) / px + 0.5);
                    shadeMark = saturate((0.42 - s) / px + 0.5);
                }
                else if (_Pattern > 3.5)
                {
                    float2 q = pa / _PatternScale;
                    float tone = FacetTone(q, t) * saturate(0.3 / max(length(fwidth(q)), 1e-4));
                    mark = saturate(tone);
                    shadeMark = saturate(-tone);
                }
                else if (_Pattern > 2.5)
                {
                    // Éclats : courts traits qui s'allument et s'éteignent
                    float2 q = pa / _PatternScale;
                    half s1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, q * float2(0.35, 1.4)).b;
                    half s2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, pb / _PatternScale * float2(0.3, 1.1) + 0.27).b;
                    float s = s1 * s2;
                    mark = saturate((s - 0.5) / max(fwidth(s), 1e-4) + 0.5) * saturate(0.08 / max(fwidth(s), 1e-4));
                }
                else if (_Pattern > 1.5)
                {
                    // Cellules : réseau de filets clairs, déformé par le bruit
                    float2 q = (pa + (float2(a, b) - 0.5) * _PatternScale * 0.5) / _PatternScale;
                    float e = CellEdge(q, t * 0.6);
                    float px = max(fwidth(e), 1e-4);
                    mark = saturate((0.07 - e) / px + 0.5) * saturate(0.12 / px);
                }
                else if (_Pattern > 0.5)
                {
                    // Crêtes : fine bande nette autour d'un niveau du bruit
                    float px = max(fwidth(w), 1e-4);
                    mark = saturate((0.03 - abs(w - 0.66)) / px + 0.5) * saturate(0.06 / px);
                }
                half patternFade = _PatternDepth > 0.001 ? 1.0 - saturate(depth / _PatternDepth) : 1.0;
                mark = saturate(mark * _PatternStrength * shoreBoost * patternFade * lerp(1.0, 0.45, river));
                albedo *= 1.0 - shadeMark * _PatternStrength * patternFade * 0.45;
                albedo = lerp(albedo, _PatternColor.rgb, mark);

                // Rive : liseré d'écume qui respire, ronds qui viennent mourir sur la berge
                float lap = 0.5 + 0.5 * sin(t * 0.9 + w * 9.0);
                float fw = _FoamWidth * lerp(1.0, 0.6, river);
                float edge = fw * (0.55 + 0.45 * lap) + (w - 0.5) * fw * 0.8;
                // Pas d'écume là où la nappe passe sous le sol : elle ne se voit que sous un terrain lointain simplifié
                half foam = Below(depth, edge) * step(-0.3, depth);
                float reach = max(_RingReach, 0.01);
                float ph = frac(t * 0.12 + w * 0.35);
                float ringAt = edge + (1.0 - ph) * reach;
                float rw = 0.1 + 0.12 * ph;
                half ring = saturate((rw - abs(depth - ringAt)) / max(fwidth(depth), 1e-4) + 0.5);
                ring *= (1.0 - river) * _Rings * smoothstep(0.0, 0.25, ph) * (0.35 + 0.65 * ph) * step(0.35, a + b * 0.5);
                foam = saturate(foam + ring);
                albedo = lerp(albedo, _Foam.rgb, foam);

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

                half flat = saturate(foam + mark);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.smoothness = lerp(_Smoothness, 0.2, flat);
                surface.occlusion = 1;
                surface.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Scintillement : le reflet du soleil brisé en éclats nets par le bruit
                if (_Glint > 0.001)
                {
                    Light sun = GetMainLight(inputData.shadowCoord);
                    half3 gn = normalize(n + half3(a - 0.5, 0, b - 0.5) * 0.35);
                    half3 h = normalize(sun.direction + v);
                    float s = dot(gn, h);
                    half glint = saturate((s - (1.0 - _GlintSize)) / max(fwidth(s), 1e-5) + 0.5);
                    color.rgb += sun.color * (glint * _Glint * sun.shadowAttenuation * (1.0 - flat) * saturate(sun.direction.y * 6.0));
                }

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
