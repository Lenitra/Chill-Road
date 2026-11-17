// Végétation au sol (fleurs, trèfle, fougères, buissons bas, plantes de lac, touffes d'herbe) dans la direction
// artistique du champ d'herbe (ChillRoad/Grass) : les plantes se fondent dans le pré, seules les fleurs ressortent.
// - couleur : palette AlpFlora (UV0 = colonne, dégradé de la base à la pointe de chaque élément) ; les parties vertes
//   prennent la couleur du champ d'herbe à leur pied (UV2.x) : pied sombre, milieu = sol, pointe claire, même calcul
//   que ChillRoad/Grass et ChillRoad/Terrain ;
// - éclairage cartoon, mêmes aplats et même ombre teintée que le terrain ; la normale d'éclairage va de celle du
//   terrain (la plante s'éclaire comme le pré, d'une seule nappe) à la normale propre du modèle (UV2.y, buissons) ;
// - normale du terrain : portée par l'axe Y de la matrice de la plante quand _SlopeLit = 1 (GroundFlora) ; le
//   shader redresse la plante, qui pousse droit ;
// - vent : UV1.x = souplesse (m), UV1.y = phase ; mêmes rafales que le champ d'herbe (elles couchent les plantes et
//   éclaircissent un peu leurs pointes) ; les voitures couchent les plantes autour d'elles ;
// - fondu à distance : la plante rétrécit vers son pied avant de disparaître (_FadeEnd, 0 = jamais).
// Les deux faces sont dessinées (feuilles simples) ; les normales douces valent pour les deux côtés.
Shader "ChillRoad/Foliage"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Palette (colonnes × dégradé)", 2D) = "white" {}
        [NoScaleOffset] _Noise ("Bruits (ceux du terrain)", 2D) = "gray" {}
        _NoiseScale ("Taille de la tuile de bruit (m)", Float) = 32
        [Header(Couleur du pre)]
        _Grass ("Prairie", Color) = (0.33, 0.56, 0.22, 1)
        _GrassLush ("Herbe grasse", Color) = (0.25, 0.48, 0.19, 1)
        _RootColor ("Pied des brins (multiplie le sol)", Color) = (0.56, 0.62, 0.6, 1)
        _TipColor ("Pointe des brins", Color) = (0.76, 0.8, 0.56, 1)
        _TipAmount ("Pointe : part de la couleur claire", Range(0, 1)) = 0.2
        _TipBright ("Pointe : éclat", Range(0.8, 1.6)) = 1.0
        _Saturation ("Saturation des parties vertes", Range(0, 1.2)) = 0.8
        _Variation ("Variation par plante", Range(0, 0.3)) = 0.08
        [Header(Eclairage cartoon)]
        _ShadeColor ("Teinte de l'ombre", Color) = (0.6, 0.64, 0.8, 1)
        _ShadeLevel ("Clarté de l'ombre", Range(0, 1)) = 0.68
        _RampSoft ("Douceur du bord", Range(0.01, 0.6)) = 0.22
        _Translucency ("Translucidité à contre-jour", Range(0, 2)) = 0.45
        _TransColor ("Teinte de la lumière traversante", Color) = (0.95, 0.95, 0.72, 1)
        _AOHeight ("Hauteur de l'ombre du pied (m)", Float) = 0.3
        _AOStrength ("Ombre du pied", Range(0, 1)) = 0.28
        [Header(Vent)]
        _WindStrength ("Vent : amplitude", Float) = 0.45
        _WindScale ("Vent : taille des rafales (m, diviseur de 960)", Float) = 26
        _WindFlutter ("Vent : frémissement", Range(0, 1)) = 0.2
        _GustBright ("Vent : éclat des rafales", Range(0, 1)) = 0.16
        _Comb ("Peignage : inclinaison commune dans le sens du vent (celle de l'herbe)", Range(0, 1)) = 0.6
        _CombShare ("Part du peignage suivie par les plantes", Range(0, 1)) = 0.6
        _Push ("Voitures : force qui couche les plantes", Range(0, 4)) = 1.3
        [Header(Placement)]
        [Toggle] _SlopeLit ("L'axe Y de la matrice est la normale du terrain", Float) = 0
        _SlopeLean ("Part de la pente gardée par la plante", Range(0, 1)) = 0.25
        _FadeEnd ("Fondu : distance de disparition (0 = jamais)", Float) = 0
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
            float _NoiseScale, _TipAmount, _TipBright, _Saturation, _Variation;
            float _ShadeLevel, _RampSoft, _Translucency, _AOHeight, _AOStrength;
            float _WindStrength, _WindScale, _WindFlutter, _GustBright, _Push, _Comb, _CombShare;
            float _SlopeLit, _SlopeLean, _FadeEnd;
            half4 _Grass, _GrassLush, _RootColor, _TipColor, _ShadeColor, _TransColor;
        CBUFFER_END

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

        // Origine des coordonnées de bruit (multiple de 960 m proche de la caméra), fournie par RoadNetwork
        float4 _ChillRoadTexOrigin;
        // Sol mouillé (0-1), fourni par la météo
        float _ChillRoadWet;
        // Vent (GroundFlora) : xy = direction au sol (x, z), z = force (1 = temps calme), w = inutilisé
        float4 _ChillRoadWind;
        // Défilement des rafales intégré par GroundFlora (unités de la texture de bruit, ramené modulo 64)
        float4 _ChillRoadWindScroll;
        // Largeur du fondu de distance (m), fournie par GroundFlora
        float _ChillRoadFloraFade;
        // Capsules des voitures qui couchent l'herbe (GrassField) : (ax, az, bx, bz) et (y, rayon, force, 0)
        float4 _ChillRoadGrassPush[4];
        float4 _ChillRoadGrassPushInfo[4];

        float3 ObjectOrigin() { return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23); }

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        half Edge(float x, float e) { return smoothstep(e - 0.03, e + 0.03, x); }

        // Couleur du pré au pied de la plante (même recette que ChillRoad/Grass et ChillRoad/Terrain, partie prairie)
        half3 GroundColor(float2 p)
        {
            float2 xz = p - _ChillRoadTexOrigin.xz;
            half4 n = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, xz / _NoiseScale, 0);
            half huge = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, xz / (_NoiseScale * 5.0) + 0.37, 0).r;
            half giant = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, xz / (_NoiseScale * 15.0) + 0.71, 0).g;
            half3 col = lerp(_Grass.rgb, _GrassLush.rgb, Edge(huge * 0.6 + giant * 0.6 + (n.g - 0.5) * 0.25, 0.66));
            return col * (1.0 + (giant - 0.5) * 0.1);
        }

        // Dégradé d'un brin du champ d'herbe (t = 0 au pied, 1 à la pointe) sur la couleur du sol
        half3 FieldColor(half3 ground, half t)
        {
            half lum = dot(ground, half3(0.3, 0.59, 0.11));
            half3 tip = lerp(ground, _TipColor.rgb * (lum / 0.45), _TipAmount) * _TipBright;
            half3 col = lerp(ground * _RootColor.rgb, ground, smoothstep(0.0, 0.5, t));
            col = lerp(col, tip, smoothstep(0.45, 1.0, t));
            return lerp(dot(col, half3(0.3, 0.59, 0.11)).xxx, col, _Saturation);
        }

        // Rétrécit la plante vers son pied à l'approche de la distance de disparition
        float FadeScale(float3 originWS)
        {
            if (_FadeEnd <= 0.0) return 1.0;
            float d = distance(originWS, _WorldSpaceCameraPos);
            float w = max(_ChillRoadFloraFade, 1.0);
            return smoothstep(0.0, 1.0, saturate((_FadeEnd - d) / w));
        }

        // Rotation qui ramène la normale du terrain n sur la verticale, appliquée à v
        float3 Upright(float3 v, float3 n)
        {
            float3 k = cross(n, float3(0, 1, 0));
            return v + cross(k, v) + cross(k, cross(k, v)) / (1.0 + max(n.y, 0.0));
        }

        struct Plant
        {
            float3 positionWS;
            float3 originWS;
            half3 normalWS;     // normale propre du modèle (douce)
            half3 terrainWS;    // normale du terrain sous la plante
            half gust;          // rafale qui passe (0-1)
            half height;        // hauteur au-dessus du pied (m)
        };

        // Position monde finale d'un sommet : fondu, redressement, vent, voitures
        Plant BuildPlant(float3 positionOS, float3 normalOS, float2 uv1)
        {
            Plant o = (Plant)0;
            float3 originWS = ObjectOrigin();
            o.originWS = originWS;
            float3 axisY = float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21);
            float scale = length(axisY);
            float fade = FadeScale(originWS);
            float3 rel = mul((float3x3)UNITY_MATRIX_M, positionOS) * fade;
            float3 n = TransformObjectToWorldNormal(normalOS);
            float3 terrain = float3(0, 1, 0);
            if (_SlopeLit > 0.5)
            {
                terrain = axisY / max(scale, 1e-5);
                rel = lerp(Upright(rel, terrain), rel, _SlopeLean);
                n = normalize(lerp(Upright(n, terrain), n, _SlopeLean));
            }
            o.terrainWS = terrain;
            o.normalWS = n;
            float h = max(rel.y, 0.0);
            o.height = h;

            // Vent : mêmes rafales que le champ d'herbe (même texture, même défilement)
            // Souplesse en mètres (flex × s²) : une plante agrandie plie du même angle, pas davantage
            float flex = uv1.x * scale * fade;
            float2 dir = _ChillRoadWind.xy;
            dir = dot(dir, dir) > 1e-4 ? normalize(dir) : float2(0.8, 0.6);
            float force = _ChillRoadWind.z > 0.0 ? _ChillRoadWind.z : 1.0;
            float2 nuv = (originWS.xz - _ChillRoadTexOrigin.xz) / _WindScale - _ChillRoadWindScroll.xy;
            float gust = smoothstep(0.3, 0.85, SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, nuv, 0).r);
            o.gust = (half)(gust * saturate(force * 0.8));
            if (flex > 0.0)
            {
                float tt = _Time.y;
                float wave = sin(tt * 1.7 + dot(originWS.xz, dir) * 0.6 + uv1.y * 2.0);
                float flutter = sin(tt * 6.3 + uv1.y * 6.2831853) * _WindFlutter * (0.35 + gust);
                float2 bend = dir * (_Comb * _CombShare)
                            + (dir * (0.15 + gust * 0.9 + wave * 0.12) + float2(-dir.y, dir.x) * flutter) * (_WindStrength * force);

                // Les voitures couchent les plantes autour d'elles
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float4 cap = _ChillRoadGrassPush[i];
                    float4 info = _ChillRoadGrassPushInfo[i];
                    if (info.y <= 0.0) continue;
                    float2 ab = cap.zw - cap.xy;
                    float kk = saturate(dot(originWS.xz - cap.xy, ab) / max(dot(ab, ab), 1e-4));
                    float2 away = originWS.xz - (cap.xy + ab * kk);
                    float d = length(away);
                    float r = info.y;
                    if (d < r && abs(originWS.y - info.x) < 2.5)
                        bend += (d > 1e-3 ? away / d : dir) * (smoothstep(r, r * 0.35, d) * info.z * _Push);
                }

                // La tige garde à peu près sa longueur
                float2 d2 = bend * flex;
                float hh = max(h, 0.03);
                float dl = length(d2);
                d2 *= min(1.0, 0.9 * hh / max(dl, 1e-4));
                dl = min(dl, 0.9 * hh);
                rel += float3(d2.x, -min(dl * dl / (2.0 * hh), hh * 0.6), d2.y);
            }
            o.positionWS = originWS + rel;
            return o;
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
            #pragma instancing_options procedural:ChillRoadInstanceSetup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;      // normale d'éclairage (terrain ↔ modèle selon UV2.y)
                float2 uv : TEXCOORD2;
                half4 field : TEXCOORD3;         // rgb = couleur du champ d'herbe à cette hauteur du brin, a = part « sol »
                half4 extra : TEXCOORD4;         // x = occlusion du pied, y = rafale, z = variation, w = brouillard
                half vol : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                Plant p = BuildPlant(input.positionOS.xyz, input.normalOS, input.uv1);
                o.positionWS = p.positionWS;
                o.positionCS = TransformWorldToHClip(p.positionWS);
                half vol = saturate(input.uv2.y);
                o.normalWS = lerp(p.terrainWS, p.normalWS, vol);
                o.vol = vol;
                o.uv = input.uv0;
                o.field = half4(FieldColor(GroundColor(p.originWS.xz), (half)input.uv0.y), saturate(input.uv2.x));
                half ao = lerp(1.0 - _AOStrength, 1.0, saturate(p.height / max(_AOHeight, 1e-3)));
                o.extra = half4(ao, p.gust, Hash12(floor(p.originWS.xz * 10.0)) * 2.0 - 1.0, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half t = input.uv.y;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                albedo = lerp(albedo, input.field.rgb, input.field.a);
                albedo *= 1.0 + input.extra.z * _Variation;
                // Rafale : les pointes couchées accrochent un peu la lumière (comme l'herbe)
                albedo = lerp(albedo, albedo * 1.15 + 0.02, input.extra.y * _GustBright * smoothstep(0.2, 1.0, t) * input.field.a);
                albedo *= lerp(1.0, 0.82, _ChillRoadWet);

                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half ao = input.extra.x;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 0);
                #endif
                Light main = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half shadow = smoothstep(0.15, 0.85, main.shadowAttenuation * main.distanceAttenuation);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(screenUV);
                    ao *= lerp(1.0, ssao.indirectAmbientOcclusion, 0.6);
                #endif

                // Aplats cartoon, comme le terrain et l'herbe (même rampe, même ombre teintée)
                half lit = smoothstep(0.05 - _RampSoft, 0.05 + _RampSoft, dot(n, main.direction)) * shadow;
                half3 color = albedo * main.color * lerp(_ShadeColor.rgb * _ShadeLevel, 1.0, lit);
                color += albedo * lerp(SampleSH(n), SampleSH(half3(0, 1, 0)), 0.5) * (0.85 * ao);

                // Contre-jour : les pointes s'illuminent face au soleil (les masses des buissons beaucoup moins)
                half back = pow(saturate(dot(v, -main.direction)), 4.0);
                color += albedo * _TransColor.rgb * main.color * (back * _Translucency * t * t * shadow * (1.0 - 0.75 * input.vol));

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
            #pragma instancing_options procedural:ChillRoadInstanceSetup
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
                Plant p = BuildPlant(input.positionOS.xyz, input.normalOS, input.uv1);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - p.positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(p.positionWS, p.normalWS, lightDirectionWS));
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv1 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformWorldToHClip(BuildPlant(input.positionOS.xyz, input.normalOS, input.uv1).positionWS);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            // Même normale que l'éclairage : les contours cartoon ne dessinent pas chaque feuille des plantes du pré
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
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings NormalsVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                Plant p = BuildPlant(input.positionOS.xyz, input.normalOS, input.uv1);
                o.positionCS = TransformWorldToHClip(p.positionWS);
                o.normalWS = lerp(p.terrainWS, p.normalWS, saturate(input.uv2.y));
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
