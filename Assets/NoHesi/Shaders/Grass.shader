// Herbe des prés (GrassField) : champ de brins fins et serrés, façon grandes plaines de jeu d'aventure cartoon.
// - chaque brin est construit dans le vertex shader à partir d'un « carré » de 2 m (même maillage partout) :
//   position de la racine + hauteur le long du brin + côté ; tout le reste (taille, orientation, courbure) vient
//   d'un hachage de la racine et des données du carré (tampon _GrassPatches) ;
// - couleur prise au sol (même calcul que NoHesi/Terrain) : pied sombre, milieu = sol, pointe claire jaune-vert ;
// - éclairage cartoon avec la normale du terrain (le pré s'éclaire comme une seule nappe, pas brin par brin),
//   contre-jour lumineux vers les pointes ;
// - vent : les rafales traversent le pré en vagues qui couchent les brins et éclaircissent leurs pointes ;
// - la voiture couche l'herbe autour d'elle (capsules _NoHesiGrassPush) ;
// - au loin, les brins s'éclaircissent en nombre (rang du brin), s'élargissent et prennent la couleur du sol, puis
//   rétrécissent jusqu'à disparaître : pas de frontière visible avec le terrain.
Shader "NoHesi/Grass"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Bruits (copiés du terrain)", 2D) = "gray" {}
        _NoiseScale ("Taille de la tuile de bruit (m)", Float) = 32
        _Grass ("Prairie", Color) = (0.33, 0.56, 0.22, 1)
        _GrassLush ("Herbe grasse", Color) = (0.25, 0.48, 0.19, 1)
        _GrassDry ("Herbe sèche", Color) = (0.62, 0.62, 0.3, 1)
        _Forest ("Sous-bois", Color) = (0.36, 0.32, 0.2, 1)
        _RootColor ("Pied des brins (multiplie le sol)", Color) = (0.42, 0.5, 0.46, 1)
        _TipColor ("Pointe des brins", Color) = (0.8, 0.84, 0.5, 1)
        _TipAmount ("Pointe : part de la couleur claire", Range(0, 1)) = 0.3
        _TipBright ("Pointe : éclat", Range(0.8, 1.6)) = 1.05
        _Saturation ("Saturation", Range(0, 1.2)) = 0.88
        _BladeVariation ("Variation de teinte par brin", Range(0, 0.4)) = 0.1
        _SideShade ("Ombre d'un côté du brin", Range(0, 0.5)) = 0.16
        _BladeWidth ("Largeur des brins (m)", Float) = 0.042
        _MaxWiden ("Élargissement max au loin", Float) = 2.6
        _Lean ("Inclinaison au hasard", Range(0, 1)) = 0.14
        _Comb ("Peignage : inclinaison commune dans le sens du vent", Range(0, 1)) = 0.6
        _ClumpSize ("Taille des touffes (m)", Float) = 0.55
        _ClumpSpread ("Ouverture des touffes", Range(0, 1)) = 0.12
        _WindStrength ("Vent : amplitude", Float) = 0.5
        _WindScale ("Vent : taille des rafales (m, diviseur de 960)", Float) = 26
        _WindFlutter ("Vent : frémissement", Range(0, 1)) = 0.2
        _GustBright ("Vent : éclat des rafales", Range(0, 1)) = 0.24
        _Translucency ("Translucidité à contre-jour", Range(0, 2)) = 0.5
        _TransColor ("Teinte de la lumière traversante", Color) = (0.95, 0.95, 0.72, 1)
        _ShadeColor ("Cartoon : teinte de l'ombre", Color) = (0.6, 0.64, 0.8, 1)
        _ShadeLevel ("Cartoon : clarté de l'ombre", Range(0, 1)) = 0.68
        _RampSoft ("Cartoon : douceur du bord", Range(0.01, 0.6)) = 0.22
        _BladeNormal ("Part de la normale propre du brin", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

        CBUFFER_START(UnityPerMaterial)
            float _NoiseScale, _TipAmount, _TipBright, _Saturation, _BladeVariation, _BladeWidth, _MaxWiden, _Lean;
            float _ClumpSize, _ClumpSpread, _Comb, _SideShade;
            float _WindStrength, _WindScale, _WindFlutter, _GustBright, _Translucency, _ShadeLevel, _RampSoft, _BladeNormal;
            half4 _Grass, _GrassLush, _GrassDry, _Forest, _RootColor, _TipColor, _TransColor, _ShadeColor;
        CBUFFER_END

        // Carré d'herbe (GrassField.Patch) : a = centre (x, y, z) + graine ; b = pente (dh/dx, dh/dz), hauteur (m),
        // inutilisé ; c = herbe sèche, sous-bois, inutilisés ; d = densité aux quatre coins (x0z0, x1z0, x0z1, x1z1),
        // interpolée brin par brin : les bords du pré sont progressifs, pas en carrés
        struct GrassPatch { float4 a; float4 b; float4 c; float4 d; };
        StructuredBuffer<GrassPatch> _GrassPatches;
        // Fourni par GrassField : taille d'un carré (m) ; chaque lot a son propre tampon de carrés
        float _GrassPatchSize;
        // Distances (m) : fin de la densité pleine, début du fondu vers le sol, fin du champ ; exposant de l'éclaircie
        float4 _GrassRange;
        // Densité globale (réglage graphique)
        float _GrassDensity;
        // Capsules qui couchent l'herbe : (ax, az, bx, bz) et (y, rayon, force, 0)
        float4 _NoHesiGrassPush[4];
        float4 _NoHesiGrassPushInfo[4];

        // Origine des coordonnées de bruit (RoadNetwork), sol mouillé, vent (GroundFlora)
        float4 _NoHesiTexOrigin;
        float _NoHesiWet;
        float4 _NoHesiWind;
        float4 _NoHesiWindScroll;

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float2 Hash22(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.xx + p3.yz) * p3.zy);
        }

        half Edge(float x, float e) { return smoothstep(e - 0.03, e + 0.03, x); }

        // Couleur du sol à la racine (même recette que NoHesi/Terrain, partie prairie)
        half3 GroundColor(float2 p, half dry, half forest)
        {
            float2 xz = p - _NoHesiTexOrigin.xz;
            half4 n = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, xz / _NoiseScale, 0);
            half huge = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, xz / (_NoiseScale * 5.0) + 0.37, 0).r;
            half giant = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, xz / (_NoiseScale * 15.0) + 0.71, 0).g;
            half3 col = _Grass.rgb;
            col = lerp(col, _GrassLush.rgb, Edge(huge * 0.6 + giant * 0.6 + (n.g - 0.5) * 0.25, 0.66));
            col = lerp(col, _GrassDry.rgb, Edge(dry + (huge - 0.5) * 0.5 + (n.g - 0.5) * 0.3, 0.58));
            col = lerp(col, _Forest.rgb * (1.0 + (n.r - 0.5) * 0.2), Edge(forest + (n.b - 0.5) * 0.45, 0.5));
            return col * (1.0 + (giant - 0.5) * 0.1);
        }

        struct Blade
        {
            float3 positionWS;
            half3 normalWS;     // normale du terrain, un peu mêlée à celle du brin
            half3 ground;       // couleur du sol à la racine
            half t;             // 0 au pied, 1 à la pointe
            half gust;          // rafale qui passe (0-1)
            half far;           // 0 près, 1 au bout du champ (couleur du sol)
            half shade;         // variation du brin (-1, 1)
            half side;          // côté du brin (-1, 1)
        };

        // positionOS = (racine x, t, racine z) dans le carré ; uv = (côté -1..1, rang 0..1)
        Blade BuildBlade(float3 positionOS, float2 uv, uint instanceID)
        {
            Blade o = (Blade)0;
            GrassPatch p = _GrassPatches[instanceID];
            float S = _GrassPatchSize;

            // Le même carré tourné d'un quart de tour et/ou retourné selon la graine : pas de motif répété
            float2 c = positionOS.xz - S * 0.5;
            float seed = p.a.w;
            uint variant = (uint)(seed * 8.0) & 7u;
            if (variant & 4u) c.x = -c.x;
            if (variant & 1u) c = float2(-c.y, c.x);
            if (variant & 2u) c = -c;
            float2 root = p.a.xz + c;
            float rootY = p.a.y + c.x * p.b.x + c.y * p.b.y - 0.03;

            float2 h1 = Hash22(root * 3.17 + seed * 41.0);
            float2 h2 = Hash22(root * 5.71 - seed * 17.0);

            // Densité du milieu aux coins du carré, interpolée à la racine
            float2 u = saturate(c / S + 0.5);
            float k = lerp(lerp(p.d.x, p.d.y, u.x), lerp(p.d.z, p.d.w, u.x), u.y);

            // Touffes : les brins poussent autour de centres répartis au hasard et s'ouvrent vers l'extérieur ;
            // là où le milieu est clairsemé, ce sont des touffes entières qui manquent (pas un semis uniforme)
            float2 cell = floor(root / _ClumpSize);
            float2 centre = (cell + 0.2 + 0.6 * Hash22(cell + 0.37)) * _ClumpSize;
            float2 out2 = root - centre;
            float dc = length(out2);
            float2 outDir = dc > 1e-4 ? out2 / dc : float2(1, 0);
            float clumpRand = Hash12(cell * 1.31 + 9.7);
            float clumpH = lerp(0.7, 1.25, Hash12(cell * 0.77 + 3.1));
            float thr = clumpRand * 0.78 + h1.y * 0.22;
            float biome = saturate((saturate(k * 1.5 - 0.2) - thr) / 0.12);

            // Éclaircie avec la distance : les brins de rang élevé disparaissent d'abord (en rétrécissant)
            float3 rootWS = float3(root.x, rootY, root.y);
            float dist = distance(rootWS, _WorldSpaceCameraPos);
            float keepDist = dist < _GrassRange.x ? 1.0 : pow(saturate(_GrassRange.x / max(dist, 1e-3)), _GrassRange.w);
            float keep = keepDist * _GrassDensity;
            float rank = uv.y;
            float alive = saturate((keep - rank) / max(keep * 0.25, 0.004));
            // Fin du champ
            alive *= saturate((_GrassRange.z - dist) / max(_GrassRange.z - _GrassRange.y, 1.0));
            o.far = (half)smoothstep(_GrassRange.y * 0.55, _GrassRange.z, dist);

            float rel = saturate(dc / (_ClumpSize * 0.6));
            float height = p.b.z * clumpH * lerp(1.05, 0.62, rel) * lerp(0.75, 1.15, h1.x) * lerp(0.55, 1.0, saturate(k)) * alive * biome;
            // Au loin, moins de brins mais plus larges (le pré reste couvert)
            float widen = min(rsqrt(max(keepDist * _GrassDensity, 1e-3)), _MaxWiden);
            float width = _BladeWidth * lerp(0.75, 1.25, h2.y) * widen * saturate(alive * 3.0) * saturate(biome * 3.0);

            // Orientation : le brin s'arque vers l'extérieur de sa touffe, sa face tournée vers ce côté
            float twist = (h2.x - 0.5) * 1.6;
            float cs = cos(twist), sn = sin(twist);
            float2 face = float2(outDir.x * cs - outDir.y * sn, outDir.x * sn + outDir.y * cs);
            float2 side = float2(-face.y, face.x);
            // Vent : rafales qui défilent (même texture et même défilement que la flore)
            float2 dir = _NoHesiWind.xy;
            dir = dot(dir, dir) > 1e-4 ? normalize(dir) : float2(0.8, 0.6);
            // Inclinaison : tout le pré est « peigné » dans le sens du vent, un peu de hasard et d'ouverture des touffes
            float2 lean = dir * (_Comb * (0.75 + 0.5 * h2.y)) + outDir * (_ClumpSpread * (0.35 + 0.65 * rel))
                        + (h1 - 0.5) * 2.0 * _Lean;
            float force = _NoHesiWind.z > 0.0 ? _NoHesiWind.z : 1.0;
            float2 nuv = (root - _NoHesiTexOrigin.xz) / _WindScale - _NoHesiWindScroll.xy;
            float gust = smoothstep(0.3, 0.85, SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, nuv, 0).r);
            float tt = _Time.y;
            float wave = sin(tt * 1.7 + dot(root, dir) * 0.6 + clumpRand * 2.0);
            float flutter = sin(tt * 6.3 + h2.y * 6.2831853) * _WindFlutter * (0.35 + gust);
            float2 bend = lean + (dir * (0.15 + gust * 0.9 + wave * 0.12) + float2(-dir.y, dir.x) * flutter) * (_WindStrength * force);
            o.gust = (half)(gust * saturate(force * 0.8));

            // La voiture couche l'herbe autour d'elle
            [unroll]
            for (int i = 0; i < 4; i++)
            {
                float4 cap = _NoHesiGrassPush[i];
                float4 info = _NoHesiGrassPushInfo[i];
                if (info.y <= 0.0) continue;
                float2 ab = cap.zw - cap.xy;
                float kk = saturate(dot(root - cap.xy, ab) / max(dot(ab, ab), 1e-4));
                float2 away = root - (cap.xy + ab * kk);
                float d = length(away);
                float r = info.y * 0.82;
                if (d < r && abs(rootY - info.x) < 2.5)
                {
                    float push = smoothstep(r, r * 0.35, d) * info.z;
                    bend += (d > 1e-3 ? away / d : face) * push * 1.3;
                }
            }

            // Courbe du brin : la pointe s'arque, la tige garde à peu près sa longueur ; profil de feuille arrondi
            float bl = length(bend);
            bend *= bl > 0.95 ? 0.95 / bl : 1.0;
            bl = min(bl, 0.95);
            float t = positionOS.y;
            float up = height * t * (1.0 - 0.6 * bl * bl * t);
            float2 off = bend * height * t * t;
            // Profil de feuille : large vers le tiers inférieur, effilé vers la pointe
            float w = width * saturate(1.0 - t * t) * (0.85 + 0.6 * t) * uv.x;
            float3 pos = float3(root.x + off.x + side.x * w, rootY + up, root.y + off.y + side.y * w);
            o.positionWS = pos;

            half3 nTerrain = normalize(half3(-p.b.x, 1.0, -p.b.y));
            half3 nBlade = half3(face.x, 0.0, face.y);
            o.normalWS = normalize(nTerrain + nBlade * _BladeNormal + half3(off.x, 0.0, off.y) * 0.3);
            o.ground = GroundColor(root, (half)p.c.x, (half)p.c.y);
            o.t = (half)t;
            o.side = (half)uv.x;
            o.shade = (half)(Hash12(root * 9.13) * 2.0 - 1.0);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 albedo : TEXCOORD2;
                half4 extra : TEXCOORD3;     // x = t, y = rafale, z = brouillard, w = côté du brin
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                Blade b = BuildBlade(input.positionOS.xyz, input.uv, input.instanceID);
                o.positionWS = b.positionWS;
                o.positionCS = TransformWorldToHClip(b.positionWS);
                o.normalWS = b.normalWS;

                // Dégradé du brin : pied sombre, milieu couleur du sol, pointe claire
                half t = b.t;
                half3 ground = b.ground;
                half lum = dot(ground, half3(0.3, 0.59, 0.11));
                half3 tip = lerp(ground, _TipColor.rgb * (lum / 0.45), _TipAmount) * _TipBright;
                half3 col = lerp(ground * _RootColor.rgb, ground, smoothstep(0.0, 0.5, t));
                col = lerp(col, tip, smoothstep(0.45, 1.0, t));
                col *= 1.0 + b.shade * _BladeVariation;
                // Rafale : les pointes couchées accrochent un peu la lumière
                col = lerp(col, col * 1.15 + 0.02, b.gust * _GustBright * smoothstep(0.2, 1.0, t));
                // Teinte plus sobre que le sol, puis au loin la couleur du sol (raccord avec le terrain)
                col = lerp(dot(col, half3(0.3, 0.59, 0.11)).xxx, col, _Saturation) * 0.93;
                col = lerp(col, ground * lerp(0.92, 1.03, t), b.far);
                o.albedo = col;
                o.extra = half4(t, b.gust, ComputeFogFactor(o.positionCS.z), b.side);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Un côté du brin plus sombre que l'autre (nervure) : du relief dans la nappe
                half3 albedo = input.albedo * lerp(1.0, 0.82, _NoHesiWet) * (1.0 - _SideShade * saturate(input.extra.w * 0.5 + 0.5));
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half t = input.extra.x;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 0);
                #endif
                Light main = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half shadow = smoothstep(0.15, 0.85, main.shadowAttenuation * main.distanceAttenuation);
                half ao = lerp(0.72, 1.0, t);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(screenUV);
                    ao *= lerp(1.0, ssao.indirectAmbientOcclusion, 0.6);
                #endif

                // Aplats cartoon, comme le terrain (même rampe, même ombre teintée)
                half lit = smoothstep(0.05 - _RampSoft, 0.05 + _RampSoft, dot(n, main.direction)) * shadow;
                half3 color = albedo * main.color * lerp(_ShadeColor.rgb * _ShadeLevel, 1.0, lit);
                color += albedo * lerp(SampleSH(n), SampleSH(half3(0, 1, 0)), 0.5) * (0.85 * ao);

                // Contre-jour : les pointes s'illuminent face au soleil
                half back = pow(saturate(dot(v, -main.direction)), 4.0);
                color += albedo * _TransColor.rgb * main.color * (back * _Translucency * t * t * shadow);

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

                color = MixFog(color, InitializeInputDataFog(float4(input.positionWS, 1.0), input.extra.z));
                return half4(color, 1.0);
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
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(BuildBlade(input.positionOS.xyz, input.uv, input.instanceID).positionWS);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            // Normale du terrain : les contours cartoon ne dessinent pas chaque brin
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings NormalsVert(Attributes input)
            {
                Varyings o;
                Blade b = BuildBlade(input.positionOS.xyz, input.uv, input.instanceID);
                o.positionCS = TransformWorldToHClip(b.positionWS);
                o.normalWS = b.normalWS;
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
