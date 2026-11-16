// Route : revêtements dans la direction artistique des voitures (aplats de couleur, formes nettes, pas de grain).
// Autoroute, nationale, chemin de terre, accotements et marquages ; une seule texture de bruits doux
// (Tools/RoadTextures/road_textures.py) sert à découper les formes, les couleurs sont des aplats.
//
// Données fournies par les maillages de route :
//   uv0 = (décalage latéral depuis l'axe en m, distance le long de la route en m)
//         accotements : (distance au bord de la chaussée en m, distance le long de la route en m)
//   uv1 = (autoroute 0-1, chemin 0-1, demi-largeur de chaussée en m, 1 = voies connues : tracé principal et bras)
Shader "NoHesi/Road"
{
    Properties
    {
        [KeywordEnum(Asphalt, Dirt, Verge, Marking)] _Surface ("Revêtement", Float) = 0
        [NoScaleOffset] _Noise ("Bruits (R plaques, G formes, B détours, A éclats)", 2D) = "gray" {}
        _NoiseScale ("Taille de la tuile de bruit (m)", Float) = 32

        [Header(Enrobe)]
        _HighwayColor ("Autoroute", Color) = (0.2, 0.205, 0.215, 1)
        _CountryColor ("Nationale", Color) = (0.33, 0.33, 0.335, 1)
        _ShoulderColor ("Bande d'arrêt d'urgence", Color) = (0.26, 0.262, 0.27, 1)
        _PatchColor ("Rapiéçages", Color) = (0.25, 0.252, 0.26, 1)
        _CrackColor ("Fissures pontées", Color) = (0.2, 0.2, 0.21, 1)
        _TrackShade ("Traces de roues (autoroute sombre, nationale claire)", Range(0, 0.2)) = 0.07
        _Smoothness ("Enrobé : lissé", Range(0, 1)) = 0.16
        _DepthOffset ("Décalage de profondeur (négatif : passe devant ; marquages)", Float) = 0
        _Age ("Âge des nationales", Range(0, 1)) = 0.8

        [Header(Terre et herbe)]
        _GravelColor ("Gravier (accotements, bords)", Color) = (0.6, 0.57, 0.5, 1)
        _EarthColor ("Chemin : terre", Color) = (0.6, 0.52, 0.4, 1)
        _RutColor ("Chemin : ornières", Color) = (0.47, 0.39, 0.3, 1)
        _GrassColor ("Herbe", Color) = (0.27, 0.5, 0.24, 1)
        _GrassDark ("Herbe : touffes sombres", Color) = (0.21, 0.42, 0.2, 1)
        _BareColor ("Terre nue des talus", Color) = (0.57, 0.49, 0.36, 1)

        [Header(Peinture)]
        _PaintColor ("Peinture", Color) = (0.93, 0.93, 0.9, 1)

        [Header(Voies)]
        _Lanes ("Largeur de voie, bord du terre-plein, rive autoroute, rive nationale (m)", Vector) = (3.9, 2.4, 14.1, 4.0)
        _DirtLane ("Chemin : axe des voies (m)", Float) = 1.65
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

        CBUFFER_START(UnityPerMaterial)
            float _NoiseScale;
            half4 _HighwayColor, _CountryColor, _ShoulderColor, _PatchColor, _CrackColor;
            half _TrackShade, _Smoothness, _Age;
            half4 _GravelColor, _EarthColor, _RutColor, _GrassColor, _GrassDark, _BareColor, _PaintColor;
            float4 _Lanes;
            float _DirtLane;
        CBUFFER_END

        // Origine des coordonnées de bruit : multiple de 960 m proche de la caméra (précision loin de l'origine).
        // Fournie par RoadNetwork ; la taille de tuile (32 m, et 160 m pour les très grandes plaques) divise 960 m.
        float4 _NoHesiTexOrigin;
        // Route mouillée (0-1), fournie par la météo (WeatherSystem)
        float _NoHesiWet;

        // Neige (carte de la station de ski) : enneigement du sol lu dans la carte (MapTerrain), 0-1
        TEXTURE2D(_NoHesiSnowMap);
        float4 _NoHesiSnowRect;      // xy = coin sud-ouest de la carte, z = 1 / côté
        float _NoHesiSnowOn;
        half SnowAt(float3 pos)
        {
            return (half)_NoHesiSnowOn * SAMPLE_TEXTURE2D(_NoHesiSnowMap, sampler_LinearClamp, (pos.xz - _NoHesiSnowRect.xy) * _NoHesiSnowRect.z).r;
        }

        // Hachage entier (PCG 3D) : valeurs aléatoires stables par cellule de l'espace route
        uint3 Pcg3(uint3 v)
        {
            v = v * 1664525u + 1013904223u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            v ^= v >> 16u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            return v;
        }

        float4 Rand4(float3 cell)
        {
            uint3 a = Pcg3(asuint(int3(floor(cell))));
            uint3 b = Pcg3(a.zxy ^ 0x9E3779B9u);
            return float4(a.xy, b.xy) * (1.0 / 4294967296.0);
        }

        // Seuil net : 0 → 1 quand x dépasse e, sur environ un pixel (px = fwidth(x), calculé hors des branches)
        half Crisp(float x, float e, float px)
        {
            return saturate((x - e) / max(px, 1e-5) + 0.5);
        }

        // Trait net d'épaisseur 2·w à la distance d ; s'estompe quand il devient plus fin qu'un pixel
        half Line(float d, float w, float px)
        {
            px = max(px, 1e-5);
            return saturate((w - abs(d)) / px + 0.5) * saturate(2.0 * w / px);
        }

        struct Noise { half4 n; half4 px; half huge; };

        Noise SampleNoise(float3 pos)
        {
            Noise o;
            float2 xz = pos.xz - _NoHesiTexOrigin.xz;
            o.n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, xz / _NoiseScale);
            o.px = fwidth(o.n);
            o.huge = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, xz / (_NoiseScale * 5.0) + 0.37).r;
            return o;
        }

        struct RoadSurface { half3 albedo; half smoothness; };

        // ------------------------------------------------------------------ enrobé
        RoadSurface Asphalt(float3 pos, float2 road, float4 info)
        {
            RoadSurface s;
            Noise nz = SampleNoise(pos);
            half4 n = nz.n;
            float h = saturate(info.x);
            float hw = max(info.z, 0.5);
            float lanes = step(0.5, info.w);
            float lat = road.x, along = road.y, ax = abs(lat);
            float age = (1.0 - h) * _Age;
            float pxA = fwidth(along), pxL = fwidth(lat), pxM = max(pxA, pxL);

            // Aplat : autoroute sombre, nationale grise ; très grandes plaques de teinte, à peine visibles
            half3 col = lerp(_CountryColor.rgb, _HighwayColor.rgb, h) * (1.0 + (nz.huge - 0.5) * 0.14 + (n.r - 0.5) * 0.06);

            // Bande d'arrêt d'urgence de l'autoroute, au-delà de la ligne de rive
            half bau = lanes * h * Crisp(ax, _Lanes.z + 0.2, pxL);
            col = lerp(col, _ShoulderColor.rgb * (1.0 + (n.r - 0.5) * 0.08), bau);

            // Traces de roues : bandes nettes à ±0,85 m de l'axe de chaque voie
            // (gomme plus sombre sur l'autoroute, enrobé poli plus clair sur les nationales)
            float laneHw = _Lanes.y + _Lanes.x * (min(floor(max(ax - _Lanes.y, 0.0) / _Lanes.x), 2.0) + 0.5);
            float dx = ax - lerp(_Lanes.x * 0.5, laneHw, h);
            half track = lanes * (1.0 - bau) * Line(abs(dx) - 0.85, 0.3, pxL);
            col *= 1.0 + track * _TrackShade * lerp(0.8, -1.0, h);

            half patch = 0, crack = 0;
            UNITY_BRANCH
            if (lanes > 0.5)
            {
                // Rapiéçages : rectangles nets d'enrobé plus récent, au plus un par cellule de 12 m × demi-voie
                float cellA = floor(along / 12.0);
                float c0 = floor(ax / 1.95) * 1.95;
                float4 r = Rand4(float3(cellA, floor(ax / 1.95) + (lat >= 0.0 ? 0.0 : 31.0), 3.0));
                if (r.x < lerp(0.08, 0.015, h) * saturate(age * 1.3 + h))
                {
                    float a0 = cellA * 12.0 + 0.2 + r.y * 6.0;
                    float a1 = min(a0 + 1.4 + r.z * 5.0, cellA * 12.0 + 11.8);
                    float l0 = c0 + 0.05 + r.w * 0.5;
                    float l1 = min(l0 + 0.9 + frac(r.w * 7.13) * 1.2, c0 + 1.9);
                    float d = min(min(along - a0, a1 - along), min(ax - l0, l1 - ax));
                    patch = Crisp(d, 0.0, pxM);
                }

                // Fissures transversales pontées : segments droits, légèrement obliques, sur une partie de la largeur
                float cellT = floor(along / 9.0);
                float4 t = Rand4(float3(cellT, 5.0, 7.0));
                if (t.x < lerp(0.45, 0.0, h) * saturate(age * 1.4))
                {
                    float at = cellT * 9.0 + 1.0 + t.y * 7.0;
                    float x0 = -hw * t.w, x1 = hw * frac(t.w * 5.7 + 0.3);
                    float d = along - at - lat * (t.z - 0.5) * 0.35;
                    crack = max(crack, step(x0, lat) * step(lat, x1) * Line(d, 0.03, pxA));
                }

                // Fissure longitudinale près de l'axe (joint de construction) : segments droits
                float cellC = floor(along / 23.0);
                float4 q = Rand4(float3(cellC, 9.0, 11.0));
                if (q.x < 0.6 * saturate(age * 1.4))
                {
                    float a0 = cellC * 23.0 + q.y * 9.0, a1 = a0 + 4.0 + q.z * 10.0;
                    float d = lat - (q.w - 0.5) * 0.5 - (along - a0) * (frac(q.w * 3.7) - 0.5) * 0.02;
                    crack = max(crack, step(a0, along) * step(along, a1) * Line(d, 0.03, pxL));
                }
            }

            col = lerp(col, _PatchColor.rgb, patch);
            col = lerp(col, _CrackColor.rgb, crack);

            // Bords des nationales : bande de gravier au contour irrégulier, enrobé un peu assombri en approche
            float edge = hw - ax;
            half gravelEdge = lanes * (1.0 - h) * (1.0 - Crisp(edge, 0.08 + 0.3 * n.b * age, pxL + nz.px.b * 0.3));
            col *= lerp(0.93, 1.0, saturate(edge / 0.7));
            col = lerp(col, _GravelColor.rgb * (1.0 + (n.g - 0.5) * 0.1), gravelEdge);

            s.albedo = col;
            s.smoothness = _Smoothness + patch * 0.05 + crack * 0.15 - gravelEdge * 0.1;
            return s;
        }

        // ------------------------------------------------------------------ chemin de terre
        RoadSurface Dirt(float3 pos, float2 road, float4 info)
        {
            RoadSurface s;
            Noise nz = SampleNoise(pos);
            half4 n = nz.n;
            float hw = max(info.z, 0.5);
            float ax = abs(road.x);
            float pxL = fwidth(road.x);
            half3 col = _EarthColor.rgb * (1.0 + (nz.huge - 0.5) * 0.14 + (n.r - 0.5) * 0.08);

            // Ornières : deux bandes nettes par sens (roues à ±0,8 m de l'axe de la voie), à peine sinueuses
            float dx = ax - _DirtLane + (n.r - 0.5) * 0.3;
            half rut = Line(abs(dx) - 0.8, 0.21, pxL);
            col = lerp(col, _RutColor.rgb * (1.0 + (n.g - 0.5) * 0.08), rut);

            // Herbe : bande entre les deux sens, bords, quelques touffes entre les roues ; deux tons nets
            half center = 0.62 - ax + (n.g - 0.5) * 0.5;
            half edge = ax - (hw - 0.7) + (n.g - 0.5) * 0.9;
            half tufts = (n.b - 0.72) * 3.0 - abs(dx) * 2.0;
            half field = max(max(center, edge), tufts);
            half grass = Crisp(field, 0.0, fwidth(field)) * (1.0 - rut);
            half dark = Crisp(n.r, 0.58, nz.px.r);
            col = lerp(col, lerp(_GrassColor.rgb, _GrassDark.rgb, dark), grass);

            s.albedo = col;
            s.smoothness = 0.05 + rut * 0.05;
            return s;
        }

        // ------------------------------------------------------------------ accotements et talus
        RoadSurface Verge(float3 pos, float2 road, float4 info)
        {
            RoadSurface s;
            Noise nz = SampleNoise(pos);
            half4 n = nz.n;
            float e = road.x;                        // distance au bord de la chaussée
            // Accotement en gravier (aucun le long des chemins), puis talus enherbé à deux tons, plaques de terre nue
            half width = lerp(1.0, -0.4, saturate(info.y));
            float gField = width + (n.b - 0.5) * 0.5 - e;
            half gravel = Crisp(gField, 0.0, fwidth(gField));
            half dark = Crisp(n.r, 0.58, nz.px.r);
            half3 col = lerp(_GrassColor.rgb, _GrassDark.rgb, dark);
            half bare = Crisp(n.g + (n.b - 0.5) * 0.3, 0.74, nz.px.g + nz.px.b * 0.3) * (1.0 - gravel);
            col = lerp(col, _BareColor.rgb, bare);
            col = lerp(col, _GravelColor.rgb * (1.0 + (n.g - 0.5) * 0.1), gravel);
            s.albedo = col;
            s.smoothness = 0.05;
            return s;
        }

        // ------------------------------------------------------------------ marquages
        RoadSurface Marking(float3 pos, float4 info)
        {
            RoadSurface s;
            Noise nz = SampleNoise(pos);
            half h = saturate(info.x);
            // Peinture nette ; sur les nationales, quelques éclats où l'enrobé réapparaît
            float wear = nz.n.a * 0.6 + nz.n.g * 0.4;
            half chip = (1.0 - h) * _Age * (1.0 - Crisp(wear, 0.26, nz.px.a * 0.6 + nz.px.g * 0.4));
            half3 asphalt = lerp(_CountryColor.rgb, _HighwayColor.rgb, h);
            s.albedo = lerp(_PaintColor.rgb, asphalt, chip);
            s.smoothness = 0.28;
            return s;
        }

        RoadSurface Evaluate(float3 pos, float2 road, float4 info)
        {
            #if defined(_SURFACE_DIRT)
                return Dirt(pos, road, info);
            #elif defined(_SURFACE_VERGE)
                return Verge(pos, road, info);
            #elif defined(_SURFACE_MARKING)
                return Marking(pos, info);
            #else
                return Asphalt(pos, road, info);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Offset [_DepthOffset], [_DepthOffset]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _SURFACE_ASPHALT _SURFACE_DIRT _SURFACE_VERGE _SURFACE_MARKING
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
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 road : TEXCOORD2;
                float4 info : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half3 vertexSH : TEXCOORD5;
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
                o.road = input.uv;
                o.info = input.info;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.vertexSH = SampleSH(o.normalWS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = normalize(input.normalWS);
                RoadSurface rs = Evaluate(input.positionWS, input.road, input.info);

                // Route enneigée : neige tassée sur la chaussée, traces de roues grises (l'enrobé réapparaît là où
                // la neige s'amincit), accotements blancs, marquages recouverts
                half snow = SnowAt(input.positionWS);
                {
                    float2 sxz = input.positionWS.xz - _NoHesiTexOrigin.xz;
                    half4 sn = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, sxz / _NoiseScale + 0.23);
                    float coverField = snow + (sn.g - 0.5) * 0.5 + (sn.r - 0.5) * 0.2;
                    half cover = Crisp(coverField, 0.5, fwidth(coverField));
                    half3 fresh = half3(0.88, 0.92, 0.97) * (1.0 + (sn.r - 0.5) * 0.05);
                    half3 packed = lerp(half3(0.78, 0.83, 0.9), half3(0.68, 0.74, 0.83), Crisp(sn.b, 0.55, fwidth(sn.b)));
                    #if defined(_SURFACE_VERGE)
                        rs.albedo = lerp(rs.albedo, fresh, cover);
                    #elif defined(_SURFACE_MARKING)
                        rs.albedo = lerp(rs.albedo, packed, cover);
                        rs.smoothness = lerp(rs.smoothness, 0.3, cover);
                    #else
                        float hwS = max(input.info.z, 0.5);
                        float axS = abs(input.road.x);
                        // deux traces par voie (voie unique par sens : axe à mi-largeur ; chemins : _DirtLane)
                        #if defined(_SURFACE_DIRT)
                            float laneS = _DirtLane;
                        #else
                            float laneS = lerp(hwS * 0.47, _Lanes.y + _Lanes.x * (min(floor(max(axS - _Lanes.y, 0.0) / _Lanes.x), 2.0) + 0.5), saturate(input.info.x));
                        #endif
                        float tr = abs(abs(axS - laneS) - 0.8) + (sn.a - 0.5) * 0.14;
                        half track = step(0.5, input.info.w) * Line(tr, 0.27, fwidth(input.road.x));
                        // au cœur de la zone la trace reste de la neige grise ; en lisière elle montre l'enrobé mouillé
                        half3 slush = lerp(rs.albedo * 0.8, half3(0.5, 0.55, 0.63), saturate(snow * 2.0 - 0.9) * 0.7);
                        // bourrelets blancs sur les rives et entre les traces
                        half3 body = lerp(packed, fresh, saturate((axS - (hwS - 0.9)) * 2.0));
                        rs.albedo = lerp(rs.albedo, lerp(body, slush, track), cover);
                        rs.smoothness = lerp(rs.smoothness, lerp(0.22, 0.5, track), cover);
                    #endif
                }

                // Route mouillée : revêtement assombri et lustré (reflets du ciel, des lampadaires et des phares), flaques
                // nettes dans les creux de l'enrobé et les ornières ; la terre devient boue, l'herbe fonce à peine
                half wet = (half)_NoHesiWet;
                UNITY_BRANCH
                if (wet > 0.001)
                {
                    #if defined(_SURFACE_DIRT)
                        half darken = 0.62, gloss = 0.42, puddles = 1.0;
                    #elif defined(_SURFACE_VERGE)
                        half darken = 0.82, gloss = 0.18, puddles = 0.0;
                    #elif defined(_SURFACE_MARKING)
                        half darken = 0.88, gloss = 0.62, puddles = 0.0;
                    #else
                        half darken = 0.66, gloss = 0.72, puddles = 1.0;
                    #endif
                    rs.albedo *= lerp(1.0, darken, wet);
                    rs.smoothness = lerp(rs.smoothness, gloss, wet);
                    float2 pxz = input.positionWS.xz - _NoHesiTexOrigin.xz;
                    half pn = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, pxz / (_NoiseScale * 0.5) + 0.61).g;
                    half puddle = puddles * Crisp(pn, lerp(1.05, 0.7, wet), fwidth(pn));
                    rs.albedo *= lerp(1.0, 0.75, puddle);
                    rs.smoothness = lerp(rs.smoothness, 0.95, puddle);
                }

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
                inputData.bakedGI = input.vertexSH;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = rs.albedo;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = rs.smoothness;
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
