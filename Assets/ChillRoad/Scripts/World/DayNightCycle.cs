using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ChillRoad
{
    /// <summary>
    /// Cycle jour/nuit : course du soleil et de la lune, ciel (lueur du couchant, étoiles), brume, lumière ambiante,
    /// reflets et post-traitement. La nuit, les lampadaires et les phares (joueur et trafic proche) éclairent la route.
    /// </summary>
    /// <summary>
    /// Éclairage de studio (<see cref="ShopStage"/> : hall de la concession, atelier de tuning) : lumière principale,
    /// ambiance et brume fixes, quelles que soient l'heure et la météo du monde.
    /// </summary>
    public struct StudioLighting
    {
        public Quaternion keyRotation;
        public Color keyColor;
        public float keyIntensity;
        public Color sky, equator, ground, fog;
        /// <summary>Densité de brume ; négative : on garde la brume du monde (paysage vu par les vitres).</summary>
        public float fogDensity, bloomBoost;
    }

    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("Durée d'une journée complète, en minutes réelles")] public float dayLengthMinutes = 18f;
        [Range(0f, 24f)] public float timeOfDay = 16f;
        public bool frozen;

        const int LampLightCount = 16;
        const int CarLightCount = 10;
        const int TailLightCount = 8;
        // Soleil : se lève à l'est (+x), culmine au sud, se couche vers 19 h
        static readonly Vector3 East = Vector3.right;
        static readonly Vector3 Culmination = new Vector3(0f, Mathf.Cos(35f * Mathf.Deg2Rad), -Mathf.Sin(35f * Mathf.Deg2Rad));

        /// <summary>0 = plein jour, 1 = nuit noire.</summary>
        public float Night { get; private set; }
        /// <summary>Obscurité pour l'éclairage public et les phares : la nuit, ou le ciel sombre d'un orage.</summary>
        public float Dark { get; private set; }
        /// <summary>Météo : l'orage couvre le ciel, grise la lumière, épaissit la brume ; éclairs.</summary>
        public WeatherSystem Weather { get; set; }
        public string Clock
        {
            get
            {
                int minutes = Mathf.FloorToInt(timeOfDay * 60f) % (24 * 60);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
        }

        Light sun;
        float sunIntensity;
        Material sky;
        float skyExposure = 1f;
        RoadNetwork road;
        TrafficSystem traffic;
        CarLights playerLights;
        ColorAdjustments colorAdjust;
        WhiteBalance whiteBalance;
        Bloom bloom;
        float baseExposure, baseTemperature, baseBloom, baseSaturation, baseFogDensity, baseCloudCover;
        Color baseCloudLight, baseCloudShade, baseZenith, baseHorizon, baseHaze, baseNightZenith, baseNightHorizon;
        Color dayHaze;
        LightPool<Transform> lampPool;
        LightPool<TrafficSystem.Car> headPool, tailPool;
        readonly List<TrafficSystem.Car> tailTargets = new List<TrafficSystem.Car>();
        readonly List<(float score, Transform head)> lampCandidates = new List<(float, Transform)>();
        readonly List<(float score, TrafficSystem.Car car)> carCandidates = new List<(float, TrafficSystem.Car)>();
        readonly List<Transform> lampTargets = new List<Transform>();
        readonly List<TrafficSystem.Car> carTargets = new List<TrafficSystem.Car>();
        float poolTimer;
        // Reflets du ciel : le ciel du moment rendu dans un petit cubemap. Le reflet par défaut, figé au chargement sur
        // un ciel de jour clair, rendait les surfaces lustrées (route mouillée, carrosseries) cyan la nuit et sous l'orage
        Camera reflectionCamera;
        RenderTexture reflection;
        float reflectionTimer;

        public void Init(RoadNetwork r, TrafficSystem t, CarLights player)
        {
            road = r;
            traffic = t;
            playerLights = player;
            sun = RenderSettings.sun;
            if (!sun)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            sunIntensity = sun ? sun.intensity : 1.6f;
            // Copie du ciel : les réglages animés ne modifient pas l'asset
            if (RenderSettings.skybox && RenderSettings.skybox.shader.name == "ChillRoad/SkyDayNight")
            {
                sky = new Material(RenderSettings.skybox);
                RenderSettings.skybox = sky;
                skyExposure = sky.GetFloat("_DayExposure");
                baseCloudCover = sky.GetFloat("_CloudCover");
                baseCloudLight = sky.GetColor("_CloudLight");
                baseCloudShade = sky.GetColor("_CloudShade");
                baseZenith = sky.GetColor("_DayZenith");
                baseHorizon = sky.GetColor("_DayHorizon");
                baseHaze = sky.GetColor("_Haze");
                baseNightZenith = sky.GetColor("_NightZenith");
                baseNightHorizon = sky.GetColor("_NightHorizon");
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            dayHaze = RenderSettings.fogColor;
            baseFogDensity = RenderSettings.fogDensity;
            var volume = FindAnyObjectByType<Volume>();
            if (volume)
            {
                var profile = volume.profile; // instance propre à la partie
                if (profile.TryGet(out colorAdjust))
                {
                    baseExposure = colorAdjust.postExposure.value;
                    baseSaturation = colorAdjust.saturation.value;
                    colorAdjust.saturation.overrideState = true;
                }
                if (profile.TryGet(out whiteBalance)) baseTemperature = whiteBalance.temperature.value;
                if (profile.TryGet(out bloom)) baseBloom = bloom.intensity.value;
            }
            var lamps = new List<Light>();
            var heads = new List<Light>();
            var tails = new List<Light>();
            for (int i = 0; i < LampLightCount; i++) lamps.Add(MakeSpot("LampLight", new Color(1f, 0.76f, 0.48f), 30f, 125f));
            for (int i = 0; i < CarLightCount; i++) heads.Add(MakeSpot("TrafficHeadlight", new Color(1f, 0.93f, 0.8f), 70f, 70f));
            for (int i = 0; i < TailLightCount; i++) tails.Add(MakeSpot("TrafficTailLight", new Color(1f, 0.07f, 0.04f), 14f, 120f));
            lampPool = new LightPool<Transform>(lamps);
            headPool = new LightPool<TrafficSystem.Car>(heads);
            tailPool = new LightPool<TrafficSystem.Car>(tails);
            // (pas de carte graphique : serveur dédié lancé sans affichage)
            if (sky && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) InitReflection();
            Apply();
            UpdateReflection(true);
        }

        void InitReflection()
        {
            var go = new GameObject("SkyReflectionCamera");
            go.transform.SetParent(transform, false);
            reflectionCamera = go.AddComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.cullingMask = 0;
            reflectionCamera.clearFlags = CameraClearFlags.Skybox;
            reflectionCamera.nearClipPlane = 0.1f;
            reflectionCamera.farClipPlane = 10f;
            reflection = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGBHalf)
            {
                dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = true, name = "SkyReflection (runtime)",
            };
            reflection.Create();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflection;
        }

        /// <summary>Rend le ciel dans le cubemap des reflets : toutes les 0,5 s, et à chaque image pendant un éclair.</summary>
        void UpdateReflection(bool force)
        {
            if (!reflectionCamera) return;
            reflectionTimer -= Time.unscaledDeltaTime;
            if (!force && reflectionTimer > 0f) return;
            reflectionTimer = GraphicsQuality.ReflectionInterval;      // 0,5 s (haute) à 2 s (basse)
            reflectionCamera.RenderToCubemap(reflection);
        }

        Light MakeSpot(string name, Color color, float range, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.range = range;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.45f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        /// <summary>Heure fixée (menu d'avant-course) : ciel, lumière et reflets refaits tout de suite.</summary>
        public void SetTime(float hour)
        {
            timeOfDay = Mathf.Repeat(hour, 24f);
            Apply();
            UpdateReflection(true);
        }

        /// <summary>Avance l'heure (bouton du menu pause).</summary>
        public void Advance(float hours)
        {
            timeOfDay = Mathf.Repeat(timeOfDay + hours, 24f);
            Apply();
            UpdateReflection(true);
        }

        /// <summary>Heure du serveur (partie en réseau rejointe) : on ne se recale que si l'écart devient sensible.</summary>
        public void SyncTime(float hour)
        {
            float gap = Mathf.Abs(Mathf.DeltaAngle(timeOfDay * 15f, hour * 15f)) / 15f;
            if (gap < 0.04f) return;
            timeOfDay = Mathf.Repeat(hour, 24f);
            // Saut d'heure (l'hôte avance le temps) : ciel et reflets refaits tout de suite
            if (gap > 0.25f)
            {
                Apply();
                UpdateReflection(true);
            }
        }

        void Update()
        {
            if (!frozen && dayLengthMinutes > 0.01f)
                timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime * 24f / (dayLengthMinutes * 60f), 24f);
            Apply();
            UpdateReflection(Weather && Weather.Flash > 0.02f);
            poolTimer -= Time.unscaledDeltaTime;
            if (poolTimer <= 0f)
            {
                poolTimer = 0.2f;
                ChooseLightTargets();
            }
            UpdatePools();
        }

        void Apply()
        {
            float theta = (timeOfDay - 12f) / 24f * Mathf.PI * 2f;
            Vector3 sunDir = (-Mathf.Sin(theta) * East + Mathf.Cos(theta) * Culmination + Vector3.up * 0.25f).normalized;
            Vector3 moonDir = (Mathf.Sin(theta) * East - Mathf.Cos(theta) * Culmination + Vector3.up * 0.3f).normalized;
            float el = sunDir.y;

            Night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, -0.14f, el));
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(el - 0.02f) / 0.24f);
            float dayK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.2f, el));
            float moonK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, -0.16f, el));
            // Orage : ciel couvert (lumière diffuse, ombres douces), éclairs qui illuminent tout un instant
            float storm = Weather ? Weather.Storm : 0f, flash = Weather ? Weather.Flash : 0f;

            // Lumière principale : le soleil, puis la lune quand il est couché
            if (sun)
            {
                bool useSun = el > -0.04f;
                Vector3 dir = useSun ? sunDir : moonDir;
                sun.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                sun.intensity = (useSun ? sunIntensity * dayK : 0.3f * moonK) * Mathf.Lerp(1f, 0.25f, storm) + flash * (useSun ? 1.2f : 1.6f);
                sun.color = useSun
                    ? Color.Lerp(new Color(1f, 0.5f, 0.26f), new Color(1f, 0.95f, 0.86f), Mathf.SmoothStep(0f, 1f, el / 0.35f))
                    : new Color(0.62f, 0.72f, 1f);
                sun.color = Color.Lerp(Color.Lerp(sun.color, new Color(0.8f, 0.85f, 0.95f), storm * 0.6f), new Color(0.85f, 0.9f, 1f), Mathf.Clamp01(flash * 2f));
                sun.shadowStrength = (useSun ? 1f : 0.75f) * Mathf.Lerp(1f, 0.35f, storm);
                sun.enabled = sun.intensity > 0.001f;
                RenderSettings.sun = sun;
            }

            if (sky)
            {
                sky.SetVector("_SunDir", sunDir);
                sky.SetVector("_MoonDir", moonDir);
                sky.SetFloat("_Night", Night);
                sky.SetFloat("_Glow", twilight * 1.3f * (1f - storm * 0.8f));
                // Orage : plafond gris et bas, nuages sombres ; la nuit, les éclairs éclairent les nuages
                sky.SetFloat("_CloudCover", Mathf.Lerp(baseCloudCover, 1f, storm));
                sky.SetColor("_CloudLight", Color.Lerp(baseCloudLight, new Color(0.53f, 0.55f, 0.6f), storm));
                sky.SetColor("_CloudShade", Color.Lerp(baseCloudShade, new Color(0.4f, 0.42f, 0.47f), storm));
                sky.SetColor("_DayZenith", Color.Lerp(baseZenith, new Color(0.4f, 0.43f, 0.48f), storm));
                sky.SetColor("_DayHorizon", Color.Lerp(baseHorizon, new Color(0.52f, 0.55f, 0.59f), storm));
                sky.SetColor("_Haze", Color.Lerp(baseHaze, new Color(0.47f, 0.5f, 0.54f), storm));
                Color flashSky = new Color(0.5f, 0.53f, 0.65f) * flash;
                sky.SetColor("_NightZenith", Color.Lerp(baseNightZenith, new Color(0.02f, 0.022f, 0.03f), storm) + flashSky * 0.6f);
                sky.SetColor("_NightHorizon", Color.Lerp(baseNightHorizon, new Color(0.045f, 0.05f, 0.062f), storm) + flashSky);
                sky.SetColor("_DayTint", Color.Lerp(Color.white, new Color(1f, 0.72f, 0.58f), twilight * 0.8f));
                sky.SetFloat("_DayExposure", skyExposure * Mathf.Lerp(1f, 0.55f, twilight) * Mathf.Lerp(1f, 0.8f, storm) * (1f + flash * 1.4f));
                sky.SetFloat("_SunDisk", (1f - Night) * (1f - storm));
                sky.SetFloat("_Stars", Night * (1f - storm));
            }

            // Brume et lumière ambiante
            Color twilightHaze = new Color(0.8f, 0.56f, 0.46f), nightHaze = new Color(0.02f, 0.028f, 0.05f);
            Color fog = Color.Lerp(Color.Lerp(dayHaze, twilightHaze, twilight * 0.85f), nightHaze, Night);
            Color stormFog = Color.Lerp(new Color(0.46f, 0.49f, 0.53f) * Mathf.Lerp(1f, 0.8f, twilight), new Color(0.03f, 0.034f, 0.045f), Night);
            Color flashAmbient = new Color(0.9f, 0.95f, 1.2f) * flash;
            RenderSettings.fogColor = Color.Lerp(fog, stormFog, storm) + flashAmbient * 0.25f;
            // Pluie : la brume épaissit et masque le lointain
            if (baseFogDensity > 0f) RenderSettings.fogDensity = baseFogDensity * Mathf.Lerp(1f, 2.3f, storm);
            RenderSettings.ambientSkyColor = Color.Lerp(
                Mix(new Color(0.56f, 0.67f, 0.82f), new Color(0.46f, 0.4f, 0.46f), new Color(0.035f, 0.05f, 0.1f), twilight),
                Mix(new Color(0.4f, 0.43f, 0.48f), new Color(0.36f, 0.35f, 0.38f), new Color(0.03f, 0.035f, 0.05f), twilight), storm) + flashAmbient;
            RenderSettings.ambientEquatorColor = Color.Lerp(
                Mix(new Color(0.62f, 0.68f, 0.7f), new Color(0.68f, 0.46f, 0.36f), new Color(0.03f, 0.04f, 0.07f), twilight),
                Mix(new Color(0.38f, 0.4f, 0.43f), new Color(0.34f, 0.32f, 0.33f), new Color(0.025f, 0.03f, 0.04f), twilight), storm) + flashAmbient * 0.8f;
            RenderSettings.ambientGroundColor = Color.Lerp(
                Mix(new Color(0.36f, 0.35f, 0.3f), new Color(0.26f, 0.2f, 0.18f), new Color(0.015f, 0.018f, 0.025f), twilight),
                Mix(new Color(0.22f, 0.22f, 0.22f), new Color(0.18f, 0.17f, 0.17f), new Color(0.012f, 0.014f, 0.018f), twilight), storm) + flashAmbient * 0.4f;
            // Reflets : le cubemap suit le ciel réel (sombre la nuit, gris sous l'orage) ; sans lui, on atténue le ciel figé
            RenderSettings.reflectionIntensity = reflection ? 1f : Mathf.Lerp(1f, 0.12f, Night) * Mathf.Lerp(1f, 0.55f, storm);

            // Post-traitement : un peu plus exposé et bleuté la nuit, halo plus fort sur les lumières
            // Orage : couleurs désaturées et plus froides
            if (colorAdjust)
            {
                colorAdjust.postExposure.value = baseExposure + Night * 0.55f + flash * 0.6f;
                colorAdjust.saturation.value = baseSaturation - storm * 25f;
            }
            if (whiteBalance) whiteBalance.temperature.value = Mathf.Lerp(baseTemperature, -14f, Night) - storm * 6f;
            if (bloom) bloom.intensity.value = baseBloom + Night * 0.7f;

            // Éclairage public et phares : la nuit, et aussi sous un ciel d'orage
            Dark = Mathf.Max(Night, storm * 0.55f);
            if (road && road.LampGlow)
                road.LampGlow.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.42f) * (Dark * 9f));
            // Pack alpin : fenêtres éclairées, lanternes et enseignes s'allument à la tombée de la nuit
            if (road && road.Assets && road.Assets.alp && road.Assets.alp.material)
                road.Assets.alp.material.SetColor("_EmissionColor", Color.white * (Mathf.SmoothStep(0f, 1f, Dark) * 1.9f));
            float lights = Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, -0.08f, el)), storm * 0.9f);
            if (traffic)
            {
                traffic.LightsLevel = lights;
                traffic.TimeDensity = TrafficFactor(timeOfDay);
            }
            if (studio.HasValue) ApplyStudio(studio.Value);
        }

        StudioLighting? studio;

        /// <summary>
        /// Éclairage de studio (décor intérieur filmé à la place du monde) ; null : retour à l'heure et à la météo
        /// du monde. Appliqué tout de suite : aucune image rendue avec l'ancien éclairage.
        /// </summary>
        public void SetStudio(StudioLighting? lighting)
        {
            studio = lighting;
            Apply();
        }

        /// <summary>Remplace ce que l'heure et la météo viennent de régler : lumière, ambiance, brume, post-traitement.</summary>
        void ApplyStudio(StudioLighting s)
        {
            if (sun)
            {
                sun.transform.rotation = s.keyRotation;
                sun.color = s.keyColor;
                sun.intensity = s.keyIntensity;
                sun.shadowStrength = 0.85f;
                sun.enabled = true;
            }
            if (s.fogDensity >= 0f)
            {
                RenderSettings.fogColor = s.fog;
                RenderSettings.fogDensity = s.fogDensity;
            }
            RenderSettings.ambientSkyColor = s.sky;
            RenderSettings.ambientEquatorColor = s.equator;
            RenderSettings.ambientGroundColor = s.ground;
            if (colorAdjust)
            {
                colorAdjust.postExposure.value = baseExposure;
                colorAdjust.saturation.value = baseSaturation;
            }
            if (whiteBalance) whiteBalance.temperature.value = baseTemperature;
            if (bloom) bloom.intensity.value = baseBloom + s.bloomBoost;
        }

        Color Mix(Color day, Color dusk, Color night, float twilight) => Color.Lerp(Color.Lerp(day, dusk, twilight * 0.8f), night, Night);

        // Densité du trafic au fil de la journée : routes presque vides la nuit, pointes vers 8 h et 17 h 30
        static readonly Vector2[] TrafficCurve =
        {
            new Vector2(0f, 0.25f), new Vector2(3.5f, 0.12f), new Vector2(5.5f, 0.3f), new Vector2(7f, 0.9f),
            new Vector2(8f, 1.3f), new Vector2(9.5f, 0.95f), new Vector2(12f, 1f), new Vector2(14f, 0.85f),
            new Vector2(16f, 1.2f), new Vector2(17.5f, 1.6f), new Vector2(19f, 1.05f), new Vector2(21f, 0.6f),
            new Vector2(23f, 0.35f), new Vector2(24f, 0.25f),
        };

        /// <summary>Multiplicateur de trafic selon l'heure (1 = journée normale).</summary>
        public static float TrafficFactor(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            for (int i = 1; i < TrafficCurve.Length; i++)
                if (hour <= TrafficCurve[i].x)
                {
                    float t = Mathf.InverseLerp(TrafficCurve[i - 1].x, TrafficCurve[i].x, hour);
                    return Mathf.Lerp(TrafficCurve[i - 1].y, TrafficCurve[i].y, Mathf.SmoothStep(0f, 1f, t));
                }
            return TrafficCurve[0].y;
        }

        /// <summary>Attribue les projecteurs disponibles aux lampadaires et aux voitures les plus proches devant la caméra.</summary>
        void ChooseLightTargets()
        {
            lampTargets.Clear();
            carTargets.Clear();
            tailTargets.Clear();
            var cam = Camera.main;
            if (!cam || Dark < 0.02f)
            {
                lampPool.Assign(lampTargets);
                headPool.Assign(carTargets);
                tailPool.Assign(tailTargets);
                return;
            }

            lampCandidates.Clear();
            foreach (var h in road.LampHeads)
            {
                if (!h) continue;
                float score = LightScore(cam, h.position, 380f, -15f, 200f);
                if (score < float.MaxValue) lampCandidates.Add((score, h));
            }
            lampCandidates.Sort((a, b) => a.score.CompareTo(b.score));
            for (int i = 0; i < lampCandidates.Count && i < LampLightCount; i++) lampTargets.Add(lampCandidates[i].head);

            carCandidates.Clear();
            foreach (var c in traffic.Cars)
            {
                if (c.crashed || c.veiled || !c.go) continue;
                float score = LightScore(cam, c.go.transform.position, 240f, -10f, 150f);
                if (score < float.MaxValue) carCandidates.Add((score, c));
            }
            carCandidates.Sort((a, b) => a.score.CompareTo(b.score));
            for (int i = 0; i < carCandidates.Count && i < CarLightCount; i++) carTargets.Add(carCandidates[i].car);
            // Feux arrière : voitures devant la caméra qui roulent dans le même sens (on voit leur arrière)
            foreach (var cc in carCandidates)
            {
                if (tailTargets.Count >= TailLightCount) break;
                if (SeenFromBehind(cam, cc.car)) tailTargets.Add(cc.car);
            }
            lampPool.Assign(lampTargets);
            headPool.Assign(carTargets);
            tailPool.Assign(tailTargets);
        }

        /// <summary>Priorité d'un projecteur : distance à la caméra, pénalisée derrière elle (float.MaxValue : trop loin).</summary>
        static float LightScore(Camera cam, Vector3 p, float range, float behind, float penalty)
        {
            if (!cam) return float.MaxValue;
            Vector3 d = p - cam.transform.position;
            float dist = d.magnitude;
            if (dist > range) return float.MaxValue;
            return dist + (Vector3.Dot(d, cam.transform.forward) < behind ? penalty : 0f);
        }

        /// <summary>Voiture devant la caméra qui roule dans le même sens : on voit ses feux arrière.</summary>
        static bool SeenFromBehind(Camera cam, TrafficSystem.Car car)
        {
            Vector3 cf = cam.transform.forward;
            return Vector3.Dot(car.go.transform.forward, cf) > 0.3f && Vector3.Dot(car.go.transform.position - cam.transform.position, cf) > 0f;
        }

        void UpdatePools()
        {
            float dt = Time.unscaledDeltaTime;
            lampPool.Update(dt, h => h,
                (l, h) => l.transform.SetPositionAndRotation(h.position - Vector3.up * 0.25f, Quaternion.LookRotation(Vector3.down, h.forward)),
                h => Dark * 34f);
            headPool.Update(dt, c => c.go && !c.crashed,
                (l, c) =>
                {
                    var t = c.go.transform;
                    Vector3 f = t.forward;
                    l.transform.SetPositionAndRotation(t.position + f * (c.length * 0.5f + 0.1f) + Vector3.up * 0.75f, Quaternion.LookRotation((f - Vector3.up * 0.1f).normalized));
                },
                c => Dark * 70f);
            tailPool.Update(dt, c => c.go && !c.crashed,
                (l, c) =>
                {
                    var t = c.go.transform;
                    Vector3 f = t.forward;
                    l.transform.SetPositionAndRotation(t.position - f * (c.length * 0.5f + 0.15f) + Vector3.up * 0.8f, Quaternion.LookRotation((-f - Vector3.up * 0.5f).normalized));
                },
                c => Dark * 4f);
        }
    }
}
