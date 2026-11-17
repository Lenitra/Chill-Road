using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Météo : temps clair environ 75 % du temps, orage environ 25 %, par périodes de quelques minutes (clair 2 min 30 à
    /// 5 min, orage 50 s à 1 min 40). La part d'orage se règle dans les options (probabilité de pluie,
    /// <see cref="GameSettings.RainChance"/>) : les périodes claires s'allongent ou raccourcissent, 0 % = jamais,
    /// 100 % = toujours. L'orage couvre et assombrit le ciel (appliqué par <see cref="DayNightCycle"/>), fait
    /// tomber la pluie (gouttes autour de la caméra, bruit de pluie), lance des éclairs suivis du tonnerre. La route se
    /// mouille peu à peu et sèche lentement après l'averse : reflets et flaques (shaders de route et de terrain),
    /// adhérence réduite (<see cref="VehicleController.Wetness"/>).
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public const float StormShare = 0.25f;

        /// <summary>Intensité de l'orage (0 = clair, 1 = plein orage) : nuages, lumière, brume.</summary>
        public float Storm { get; private set; }
        /// <summary>Pluie qui tombe (0-1), par averses plus ou moins fortes pendant l'orage.</summary>
        public float Rain { get; private set; }
        /// <summary>Route mouillée (0-1) : se mouille sous la pluie, sèche lentement ensuite.</summary>
        public float Wetness { get; private set; }
        /// <summary>Lueur d'éclair en cours (0-1).</summary>
        public float Flash { get; private set; }
        /// <summary>Orage en cours ou qui arrive (la période de temps actuelle).</summary>
        public bool Stormy => stormy;
        /// <summary>Changement de période : true = l'orage arrive, false = éclaircie.</summary>
        public event Action<bool> Changed;

        bool stormy;
        bool remote;                               // partie en réseau rejointe : le temps est celui du serveur
        float chance = StormShare;                 // probabilité de pluie en vigueur (part du temps sous l'orage)
        float periodLeft, nextStrike, flashT = 99f, flashPeak, gustSeed;
        Camera cam;
        Vector3 lastCamPos;
        Vector3 camVelocity;
        GameAudio audioFx;
        DayNightCycle dayNight;
        ParticleSystem rain;
        ParticleSystem snow;
        ParticleSystem.EmissionModule snowEmission;
        /// <summary>Enneigement du sol sous la caméra (0-1), fourni par le jeu : dans la station de ski, la pluie
        /// tombe en neige et quelques flocons volent par beau temps.</summary>
        public float SnowZone { get; set; }
        bool rainAdditive;
        // Seconde caméra (décor de l'écran de chargement) : la pluie tombe aussi autour d'elle
        Camera cam2;
        ParticleSystem rain2;
        Vector3 lastCam2Pos, cam2Velocity;
        ParticleSystem.MainModule rainMain;
        ParticleSystem.EmissionModule rainEmission;
        float rainRate, emitCarry;
        ParticleSystem.Particle[] rainParticles;
        /// <summary>Caméra à l'abri d'un tunnel (0 = à l'air libre, 1 = loin des têtes) : pas d'éclair qui illumine
        /// l'intérieur, averse étouffée.</summary>
        public float Shelter { get; private set; }
        AudioSource rainSource;
        AudioSource[] thunderSources;
        AudioClip[] thunderFar, thunderClose;
        LineRenderer bolt;
        float boltT = 99f;
        readonly List<(float at, AudioClip clip, float volume)> pendingThunder = new List<(float, AudioClip, float)>();
        int nextThunderSource;

        public void Init(Camera camera, GameAssets assets, GameAudio audio, DayNightCycle cycle)
        {
            cam = camera;
            audioFx = audio;
            dayNight = cycle;
            gustSeed = UnityEngine.Random.value * 100f;
            // Départ : orage avec la probabilité réglée (une fois sur quatre par défaut), à un moment quelconque de la
            // période ; lue ici car les options s'appliquent (SetChance) après l'initialisation
            chance = Mathf.Clamp01(GameSettings.RainChance);
            stormy = chance >= 0.999f || UnityEngine.Random.value < chance;
            periodLeft = PeriodLength(stormy) * UnityEngine.Random.Range(0.3f, 1f);
            Storm = stormy ? 1f : 0f;
            Rain = stormy ? 0.8f : 0f;
            Wetness = stormy ? 1f : 0f;
            nextStrike = UnityEngine.Random.Range(3f, 8f);
            if (assets.spark) BuildRain(assets.spark, true);
            else if (assets.particle) BuildRain(assets.particle, false);
            if (assets.particle) BuildSnow(assets.particle);
            if (assets.spark) BuildBolt(assets.spark);
            BuildAudio();
            Shader.SetGlobalFloat("_ChillRoadWet", Wetness);
        }

        /// <summary>Force le temps (tests) : orage ou clair tout de suite, pour une période complète.</summary>
        public void Force(bool storm, bool instant)
        {
            if (stormy != storm) Changed?.Invoke(storm);
            stormy = storm;
            periodLeft = PeriodLength(storm);
            if (instant)
            {
                Storm = storm ? 1f : 0f;
                Rain = storm ? 0.8f : 0f;
                Wetness = storm ? 1f : 0f;
            }
        }

        /// <summary>Temps tenu (course chronométrée) : plus aucun changement de période tant qu'il n'est pas relâché.</summary>
        public bool Held { get; private set; }

        /// <summary>
        /// Tient le temps (course chronométrée, menu d'avant-course) : orage ou clair jusqu'à <see cref="Release"/>.
        /// instant : ciel, pluie et route mouillée y sont tout de suite (choix du menu) ; sinon la transition en cours
        /// s'achève et le temps en reste là. Sans annonce (pas de bannière « orage en approche »).
        /// </summary>
        public void Hold(bool storm, bool instant)
        {
            Held = true;
            stormy = storm;
            periodLeft = float.MaxValue;
            if (!instant) return;
            Storm = storm ? 1f : 0f;
            Rain = storm ? 0.8f : 0f;
            Wetness = storm ? 1f : 0f;
            Shader.SetGlobalFloat("_ChillRoadWet", Wetness);
        }

        /// <summary>Fin de la course : le temps reprend son cours (nouvelle période tirée à partir du temps actuel).</summary>
        public void Release()
        {
            if (!Held) return;
            Held = false;
            periodLeft = PeriodLength(stormy);
        }

        /// <summary>
        /// Nouvelle probabilité de pluie (menu Options, en cours de partie) : 0 % éclaircit et 100 % fait venir l'orage
        /// tout de suite ; sinon la période en cours est seulement raccourcie si elle dépasse la nouvelle durée tirée.
        /// </summary>
        public void SetChance(float p)
        {
            p = Mathf.Clamp01(p);
            if (Mathf.Abs(p - chance) < 0.0001f) return;
            chance = p;
            // Temps tenu (course) : la nouvelle probabilité vaudra après la course
            if (Held) return;
            bool want = chance <= 0.001f ? false : chance >= 0.999f ? true : stormy;
            if (want != stormy)
            {
                stormy = want;
                Changed?.Invoke(stormy);
            }
            float fresh = PeriodLength(stormy);
            periodLeft = fresh >= float.MaxValue ? fresh : Mathf.Min(periodLeft, fresh);
        }

        /// <summary>Partie en réseau rejointe : le temps qu'il fait est celui du serveur (reçu plusieurs fois par seconde).</summary>
        public void SetRemote(bool stormyNow, float storm, float rainNow, float wetness)
        {
            remote = true;
            if (stormyNow != stormy)
            {
                stormy = stormyNow;
                Changed?.Invoke(stormy);
            }
            Storm = storm;
            Rain = rainNow;
            Wetness = wetness;
        }

        /// <summary>Retour à la météo locale (partie en réseau quittée ou perdue) : elle reprend d'où en était le serveur.</summary>
        public void ClearRemote()
        {
            if (!remote) return;
            remote = false;
            periodLeft = PeriodLength(stormy);
        }

        // Orage de 50 s à 1 min 40 (75 s en moyenne) ; période claire de durée moyenne 75 s × (1 − p) / p pour que
        // l'orage occupe la part p du temps (225 s à 25 %, soit 2 min 30 à 5 min), ±33 %. Jamais / toujours : sans fin.
        float PeriodLength(bool storm)
        {
            if (storm) return chance >= 0.999f ? float.MaxValue : UnityEngine.Random.Range(50f, 100f);
            if (chance <= 0.001f) return float.MaxValue;
            float mean = 75f * (1f - chance) / chance;
            return UnityEngine.Random.Range(mean * 0.667f, mean * 1.333f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (remote)
            {
                // Nuages, pluie et route mouillée viennent du serveur ; les éclairs restent tirés ici
                Shader.SetGlobalFloat("_ChillRoadWet", Wetness);
                UpdateLightning(dt);
                return;
            }
            if (!Held) periodLeft -= dt;
            if (!Held && periodLeft <= 0f)
            {
                stormy = !stormy;
                periodLeft = PeriodLength(stormy);
                Changed?.Invoke(stormy);
            }

            // Les nuages arrivent en ~25 s ; la pluie commence quand le ciel est bien couvert, par averses
            Storm = Mathf.MoveTowards(Storm, stormy ? 1f : 0f, dt / 25f);
            float gust = Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(gustSeed, Time.time * 0.04f));
            float rainTarget = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.9f, Storm)) * gust;
            Rain = Mathf.MoveTowards(Rain, rainTarget, dt / 5f);
            // La route se mouille en ~30 s sous l'averse et sèche en ~1 min 40 après
            if (Rain > 0.05f) Wetness = Mathf.MoveTowards(Wetness, 1f, dt * Rain / 30f);
            else Wetness = Mathf.MoveTowards(Wetness, 0f, dt / 100f);
            Shader.SetGlobalFloat("_ChillRoadWet", Wetness);

            UpdateLightning(dt);
        }

        /// <summary>Seconde caméra (décor de l'écran de chargement) : la pluie tombe aussi autour d'elle.</summary>
        public void AddCamera(Camera camera)
        {
            cam2 = camera;
            lastCam2Pos = camera.transform.position;
            if (rain && !rain2)
            {
                rain2 = Instantiate(rain, transform);
                rain2.Play();
            }
        }

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            if (!cam) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 p = cam.transform.position;
            Vector3 v = (p - lastCamPos) / dt;
            if (v.sqrMagnitude > 200f * 200f) v = Vector3.zero; // téléportation
            camVelocity = Vector3.Lerp(camVelocity, v, 1f - Mathf.Exp(-dt * 5f));
            lastCamPos = p;

            NearCovers(p + camVelocity * 0.4f, 60f);
            Shelter = Mathf.MoveTowards(Shelter, ShelterAt(p), dt * 3f);

            if (rain)
            {
                // Gouttes semées autour de la caméra, en avance sur son déplacement (on roule vers elles), jamais
                // sous un tunnel ; celles que le vent y pousserait disparaissent dans la coque
                rain.transform.position = p + camVelocity * 0.4f + Vector3.up * 6f;
                rainRate = Rain * 5200f * (1f - SnowZone);
                EmitRain(rain.transform.position, Time.deltaTime);
                if (near.Count > 0) CullRain();
                float night = dayNight ? dayNight.Night : 0f;
                if (rainAdditive)
                {
                    // Traits lumineux : plus discrets la nuit, illuminés par les éclairs
                    float k = Mathf.Lerp(0.42f, 0.16f, night) + Flash * 0.6f;
                    rainMain.startColor = new Color(0.62f * k, 0.68f * k, 0.8f * k, 1f);
                }
                else
                {
                    float light = Mathf.Lerp(0.85f, 0.22f, night) + Flash * 0.8f;
                    rainMain.startColor = new Color(0.72f * light, 0.76f * light, 0.84f * light, 0.32f);
                }
            }

            if (rain && rain2 && cam2)
            {
                Vector3 p2 = cam2.transform.position;
                Vector3 v2 = (p2 - lastCam2Pos) / dt;
                if (v2.sqrMagnitude > 200f * 200f) v2 = Vector3.zero;
                cam2Velocity = Vector3.Lerp(cam2Velocity, v2, 1f - Mathf.Exp(-dt * 5f));
                lastCam2Pos = p2;
                rain2.transform.position = p2 + cam2Velocity * 0.4f + Vector3.up * 6f;
                var emission2 = rain2.emission;
                emission2.rateOverTime = rainRate;
                var main2 = rain2.main;
                main2.startColor = rainMain.startColor;
            }
            else if (rain2)
            {
                var emission2 = rain2.emission;
                emission2.rateOverTime = 0f;
            }

            if (snow)
            {
                snow.transform.position = p + camVelocity * 0.6f + Vector3.up * 5f;
                snowEmission.rateOverTime = SnowZone * Mathf.Lerp(260f, 1500f, Rain);
            }

            bool paused = Time.timeScale < 0.01f;
            float master = audioFx ? audioFx.masterVolume : 0.8f;
            if (rainSource) rainSource.volume = paused ? 0f : master * Rain * 0.42f * (1f - 0.75f * Shelter);
            if (bolt)
            {
                boltT += Time.deltaTime;
                bolt.enabled = boltT < 0.28f && (boltT < 0.07f || boltT > 0.12f);
            }
        }

        // ------------------------------------------------------------------ éclairs et tonnerre

        void UpdateLightning(float dt)
        {
            if (Storm > 0.75f && Rain > 0.3f)
            {
                nextStrike -= dt;
                if (nextStrike <= 0f)
                {
                    nextStrike = UnityEngine.Random.Range(5f, 15f);
                    Strike();
                }
            }
            // Lueur : un premier arc, un creux, un second arc, puis extinction
            flashT += dt;
            float f = flashT < 0.07f ? 1f : flashT < 0.13f ? 0.2f : flashT < 0.2f ? 0.75f : Mathf.Exp(-(flashT - 0.2f) * 14f) * 0.75f;
            // Sous un tunnel, l'éclair n'illumine pas l'intérieur (il éclaire ciel, soleil et ambiante de toute la scène)
            Flash = flashT > 1.2f ? 0f : f * flashPeak * (1f - Shelter);

            float master = audioFx ? audioFx.masterVolume : 0.8f;
            for (int i = pendingThunder.Count - 1; i >= 0; i--)
            {
                var (at, clip, volume) = pendingThunder[i];
                if (Time.time < at) continue;
                pendingThunder.RemoveAt(i);
                var src = thunderSources[nextThunderSource];
                nextThunderSource = (nextThunderSource + 1) % thunderSources.Length;
                src.pitch = UnityEngine.Random.Range(0.85f, 1.1f);
                src.PlayOneShot(clip, volume * master);
            }
        }

        void Strike()
        {
            // Foudre surtout devant la caméra, parfois tout près (claquement sec) ; tonnerre retardé par la distance
            bool close = UnityEngine.Random.value < 0.2f;
            float distance = close ? UnityEngine.Random.Range(220f, 450f) : UnityEngine.Random.Range(600f, 1600f);
            flashT = 0f;
            flashPeak = close ? 1f : Mathf.Lerp(0.8f, 0.4f, Mathf.InverseLerp(600f, 1600f, distance));
            float delay = distance / 343f;
            var clips = close ? thunderClose : thunderFar;
            pendingThunder.Add((Time.time + delay, clips[UnityEngine.Random.Range(0, clips.Length)], close ? 0.9f : Mathf.Lerp(0.75f, 0.4f, Mathf.InverseLerp(600f, 1600f, distance))));
            if (bolt && cam) PlaceBolt(distance);
        }

        void PlaceBolt(float distance)
        {
            Transform c = cam.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(c.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(-55f, 55f), Vector3.up) * fwd;
            Vector3 ground = c.position + dir * distance;
            ground.y = c.position.y - 15f;
            Vector3 top = ground + Vector3.up * 420f + dir * 60f;
            int n = bolt.positionCount;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 p = Vector3.Lerp(top, ground, t);
                if (i > 0 && i < n - 1) p += side * UnityEngine.Random.Range(-28f, 28f) + dir * UnityEngine.Random.Range(-12f, 12f);
                bolt.SetPosition(i, p);
            }
            float w = distance * 0.0045f;
            bolt.widthMultiplier = w;
            boltT = 0f;
        }

        // ------------------------------------------------------------------ abri des tunnels

        /// <summary>Tronçon de tunnel (≈ 6 m) : boîte qui suit l'axe de la route, coque comprise.</summary>
        struct Cover
        {
            public Vector3 a, fwd, right, center;   // fwd, right : horizontaux
            public float len, slope, inner, outer, inHeight, outHeight, radius;
            public float s, spanLength;             // abscisse du début du tronçon dans le tunnel, longueur du tunnel
        }

        readonly List<Cover> covers = new List<Cover>();
        readonly List<int> near = new List<int>();
        bool coversBuilt;
        const float CoverStep = 6f, RainBoxW = 36f, RainBoxH = 20f;

        /// <summary>Découpe les tunnels du réseau en tronçons (une fois le réseau chargé).</summary>
        void BuildCovers()
        {
            var rn = RoadNetwork.Instance;
            if (!rn || rn.Edges.Count == 0) return;
            coversBuilt = true;
            foreach (var e in rn.Edges)
            {
                if (e.spans == null) continue;
                float inner = e.TotalHalfWidth + 1f;
                float hin = e.kind == RoadKind.Highway ? RoadNetwork.TunnelHeightHighway : RoadNetwork.TunnelHeight;
                foreach (var sp in e.spans)
                {
                    if (sp.kind != "tunnel") continue;
                    float length = sp.s1 - sp.s0;
                    int n = Mathf.Max(1, Mathf.CeilToInt(length / CoverStep));
                    for (int i = 0; i < n; i++)
                    {
                        float s0 = sp.s0 + length * i / n, s1 = sp.s0 + length * (i + 1) / n;
                        Vector3 a = e.Evaluate(s0, out _, out var right), b = e.Evaluate(s1, out _, out _);
                        Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
                        float len = flat.magnitude;
                        if (len < 0.01f) continue;
                        float roof = e.RoofAt((s0 + s1) * 0.5f);
                        float outer = inner + RoadNetwork.TunnelShell, outHeight = hin + roof + 1f;
                        covers.Add(new Cover
                        {
                            a = a, fwd = flat / len, right = right, len = len, slope = (b.y - a.y) / len,
                            inner = inner, outer = outer, inHeight = hin, outHeight = outHeight,
                            s = s0 - sp.s0, spanLength = length,
                            center = (a + b) * 0.5f,
                            radius = new Vector3(len * 0.5f, outHeight, outer).magnitude,
                        });
                    }
                }
            }
        }

        /// <summary>Tronçons de tunnel à moins de « reach » mètres (en plus de leur taille) du point p.</summary>
        void NearCovers(Vector3 p, float reach)
        {
            near.Clear();
            if (!coversBuilt) BuildCovers();
            for (int i = 0; i < covers.Count; i++)
            {
                float r = covers[i].radius + reach;
                if ((covers[i].center - p).sqrMagnitude < r * r) near.Add(i);
            }
        }

        /// <summary>
        /// Le point q est-il dans ce tronçon ? tube : seulement le gabarit intérieur (là où roulent les voitures),
        /// sinon toute la coque (une goutte qui y entre est dans la roche : elle disparaît avant d'atteindre le tube).
        /// t : distance parcourue depuis le début du tronçon.
        /// </summary>
        static bool Inside(in Cover c, Vector3 q, bool tube, out float t)
        {
            Vector3 v = q - c.a;
            t = v.x * c.fwd.x + v.z * c.fwd.z;
            if (t < 0f || t > c.len) return false;
            float lateral = Mathf.Abs(v.x * c.right.x + v.z * c.right.z);
            if (lateral > (tube ? c.inner : c.outer)) return false;
            float h = v.y - c.slope * t;
            return h > -2f && h < (tube ? c.inHeight : c.outHeight);
        }

        bool UnderCover(Vector3 q)
        {
            foreach (int i in near)
                if (Inside(covers[i], q, false, out _)) return true;
            return false;
        }

        /// <summary>Abri à la position de la caméra : 0 à la tête du tunnel, 1 à partir de 25 m à l'intérieur.</summary>
        float ShelterAt(Vector3 p)
        {
            foreach (int i in near)
            {
                var c = covers[i];
                if (!Inside(c, p, true, out float t)) continue;
                float s = c.s + t;
                return Mathf.SmoothStep(0f, 1f, Mathf.Min(s, c.spanLength - s) / 25f);
            }
            return 0f;
        }

        /// <summary>Sème les gouttes de l'image dans la boîte de pluie centrée en « center », hors des tunnels.</summary>
        void EmitRain(Vector3 center, float dt)
        {
            emitCarry += rainRate * dt;
            int count = Mathf.Min((int)emitCarry, 600);
            emitCarry = Mathf.Min(emitCarry - (int)emitCarry, 1f);
            var ep = new ParticleSystem.EmitParams();
            bool check = near.Count > 0;
            for (int k = 0; k < count; k++)
            {
                Vector3 q = center + new Vector3((UnityEngine.Random.value - 0.5f) * RainBoxW, (UnityEngine.Random.value - 0.5f) * RainBoxH, (UnityEngine.Random.value - 0.5f) * RainBoxW);
                if (check && UnderCover(q)) continue;
                ep.position = q;
                rain.Emit(ep, 1);
            }
        }

        /// <summary>Supprime les gouttes entrées dans la coque d'un tunnel (poussées par le vent ou le déplacement).</summary>
        void CullRain()
        {
            if (rainParticles == null || rainParticles.Length < rainMain.maxParticles) rainParticles = new ParticleSystem.Particle[rainMain.maxParticles];
            int n = rain.GetParticles(rainParticles);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                if (!UnderCover(rainParticles[i].position)) continue;
                rainParticles[i].remainingLifetime = -1f;
                changed = true;
            }
            if (changed) rain.SetParticles(rainParticles, n);
        }

        // ------------------------------------------------------------------ construction

        void BuildRain(Material source, bool additive)
        {
            rainAdditive = additive;
            var material = new Material(source) { name = "Rain (runtime)" };
            if (additive) material.SetColor("_BaseColor", Color.white);
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            rain = go.AddComponent<ParticleSystem>();
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rainMain = rain.main;
            rainMain.loop = true;
            rainMain.duration = 1f;
            rainMain.startLifetime = 1.15f;
            rainMain.startSpeed = 0f;
            rainMain.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.055f);
            rainMain.simulationSpace = ParticleSystemSimulationSpace.World;
            rainMain.maxParticles = 9000;
            rainMain.playOnAwake = false;
            rainEmission = rain.emission;
            rainEmission.rateOverTime = 0f;
            var shape = rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(36f, 20f, 36f);
            // Chute à ~15 m/s, légèrement poussée par le vent
            var vel = rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(2.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-15f);
            vel.z = new ParticleSystem.MinMaxCurve(0.8f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.055f;
            r.lengthScale = 1f;
            r.cameraVelocityScale = 0.018f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            rain.Play();
        }

        /// <summary>Flocons : gros points blancs qui tombent lentement en dérivant, autour de la caméra.</summary>
        void BuildSnow(Material source)
        {
            var material = new Material(source) { name = "Snow (runtime)" };
            var go = new GameObject("Snowfall");
            go.transform.SetParent(transform, false);
            snow = go.AddComponent<ParticleSystem>();
            snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = snow.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 4.5f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 7000;
            main.playOnAwake = false;
            snowEmission = snow.emission;
            snowEmission.rateOverTime = 0f;
            var shape = snow.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 16f, 44f);
            var vel = snow.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 1.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-2.6f, -1.6f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var noise = snow.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.35f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            snow.Play();
        }

        void BuildBolt(Material spark)
        {
            var go = new GameObject("LightningBolt");
            go.transform.SetParent(transform, false);
            bolt = go.AddComponent<LineRenderer>();
            var m = new Material(spark) { name = "Lightning (runtime)" };
            m.SetColor("_BaseColor", new Color(3.2f, 3.4f, 4.2f, 1f));
            bolt.sharedMaterial = m;
            bolt.positionCount = 11;
            bolt.useWorldSpace = true;
            bolt.numCapVertices = 2;
            bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bolt.receiveShadows = false;
            bolt.widthCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 1f);
            bolt.enabled = false;
        }

        void BuildAudio()
        {
            const int rate = 44100;
            var rainGo = new GameObject("RainAudio");
            rainGo.transform.SetParent(transform, false);
            rainSource = rainGo.AddComponent<AudioSource>();
            rainSource.clip = RainClip(rate);
            rainSource.loop = true;
            rainSource.volume = 0f;
            rainSource.spatialBlend = 0f;
            rainSource.Play();
            thunderSources = new AudioSource[3];
            for (int i = 0; i < thunderSources.Length; i++)
            {
                thunderSources[i] = rainGo.AddComponent<AudioSource>();
                thunderSources[i].playOnAwake = false;
                thunderSources[i].spatialBlend = 0f;
            }
            thunderFar = new[] { ThunderClip(rate, 31, false), ThunderClip(rate, 47, false), ThunderClip(rate, 59, false) };
            thunderClose = new[] { ThunderClip(rate, 71, true), ThunderClip(rate, 83, true) };
        }

        /// <summary>Pluie : souffle clair (bruit passe-haut adouci) et impacts de gouttes épars.</summary>
        static AudioClip RainClip(int rate)
        {
            int n = rate * 3;
            var d = new float[n];
            var rng = new System.Random(17);
            float lp = 0f, hp = 0f, drop = 0f, dropPhase = 0f, dropF = 3000f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)rng.NextDouble() * 2f - 1f;
                hp += (w - hp) * 0.06f;             // passe-bas lent → w - hp = passe-haut
                lp += ((w - hp) - lp) * 0.45f;      // adoucit l'aigu
                if (rng.NextDouble() < 0.0022) { drop = 0.25f + (float)rng.NextDouble() * 0.35f; dropF = 1800f + (float)rng.NextDouble() * 3500f; }
                dropPhase += 2f * Mathf.PI * dropF / rate;
                float x = lp * 0.55f + Mathf.Sin(dropPhase) * drop;
                drop *= 0.9975f;
                d[i] = x;
            }
            // Fondu enchaîné de la boucle
            int fade = rate / 10;
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                d[i] = d[i] * t + d[n - fade + i] * (1f - t);
            }
            var c = AudioClip.Create("Rain", n - fade, 1, rate, false);
            var trimmed = new float[n - fade];
            Array.Copy(d, trimmed, n - fade);
            c.SetData(trimmed, 0);
            return c;
        }

        /// <summary>
        /// Tonnerre : grondement grave (bruit brun) qui roule par vagues et s'éteint en quelques secondes ;
        /// un impact proche commence par un claquement sec.
        /// </summary>
        static AudioClip ThunderClip(int rate, int seed, bool close)
        {
            float len = close ? 5.5f : 7f;
            int n = (int)(rate * len);
            var d = new float[n];
            var rng = new System.Random(seed);
            float brown = 0f, lp = 0f, crack = 0f, roll = 0.6f, rollTarget = 1f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float w = (float)rng.NextDouble() * 2f - 1f;
                brown = (brown + w * 0.03f) * 0.9985f;
                lp += (brown - lp) * 0.08f;
                if (i % (rate / 12) == 0) rollTarget = 0.25f + (float)rng.NextDouble() * (t < 2.5f ? 1.1f : 0.6f);
                roll += (rollTarget - roll) * 0.0009f;
                float attack = close ? 0.03f : 0.35f;
                float env = t < attack ? t / attack : Mathf.Exp(-(t - attack) * (close ? 0.8f : 0.6f));
                float x = lp * roll * env;
                if (close && t < 0.6f)
                {
                    crack += (w - crack) * 0.7f;
                    x += crack * Mathf.Exp(-t * 9f) * 0.12f;
                }
                d[i] = x;
                peak = Mathf.Max(peak, Mathf.Abs(x));
            }
            for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.95f;
            // Fin en douceur
            int tail = rate / 2;
            for (int i = 0; i < tail; i++) d[n - 1 - i] *= i / (float)tail;
            var c = AudioClip.Create(close ? "ThunderClose" : "Thunder", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
