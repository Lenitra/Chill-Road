using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NoHesi.DebugTools
{
    // Build de capture uniquement (copie du projet) : prises de vues automatiques pour la page itch.io.
    // Lancement : Capture.exe -shots <dossier> -plan <plan.json> [-seed N]
    [Serializable]
    public class ShotSpec
    {
        public string name = "shot";
        public string road = "Highway";      // Highway, Country, Dirt, Roundabout, Crossroads, Overpass, Roadworks, Any
        public int car;
        public float hour = 16f;
        public float traffic = 1f;
        public string drive = "weave";       // weave, oncoming, cruise, none
        public float kmh = 210f;
        public string cam = "chase";         // chase, chasefar, hood, bumper, rel, trackside, aerial, lineup
        public Vector3 camOffset = new Vector3(-3f, 1f, 6f);
        public Vector3 lookOffset = new Vector3(0f, 0.7f, 0f);
        public float fov = 50f;
        public bool ui;
        public bool fx = true;               // effets d'écran de vitesse (aberration, traînées)
        public string trigger = "time";      // time, nearmiss, approach, police, distance
        public int count = 3;
        public float interval = 2.5f;
        public float warmup = 8f;
        public float timeout = 60f;
        public float triggerDist = 16f;
        public float lead = 70f;             // trackside : distance de la caméra devant la voiture
        public float side = 1f;              // trackside : côté (+1 droite, -1 gauche)
        public float height = 0.8f;
        public bool police;
        public bool hero;                    // enregistre aussi la version 4K
        public float bias = 0.9f;            // weave : décalage vers la voiture frôlée
        public float minKmh;                 // nearmiss / approach : vitesse minimale
        public float events;                 // fréquence des événements aléatoires (0 = aucun)
        public string weather = "clear";   // clear, storm
        public bool sunLit;                  // rel : caméra du côté du soleil (voiture éclairée)
    }

    [Serializable]
    public class ShotPlan { public int seed = 1234; public ShotSpec[] shots; }

    public class ShotDirector : MonoBehaviour
    {
        const int W = 3840, H = 2160;
        static string outDir, planPath;

        GameManager gm;
        Camera cam;
        CameraRig rig;
        RenderTexture worldRT, halfRT, uiRT, uiHalfRT;
        Camera uiCam;
        StreamWriter log;
        ShotSpec spec;
        int shotsTaken;
        float lastShot = -99f;
        bool pendingNearMiss;
        float nearMissAt;

        // conduite
        bool driving;
        int lane;
        float laneOffset, laneTimer, s;
        Vector3 fixedCamPos;
        bool fixedCamSet;
        readonly List<GameObject> props = new List<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            outDir = Arg(args, "-shots");
            planPath = Arg(args, "-plan");
            if (outDir == null || planPath == null) return;
            var plan = JsonUtility.FromJson<ShotPlan>(File.ReadAllText(planPath));
            // Réglages de partie : graine fixe, pas d'événements aléatoires ni de relais
            PlayerPrefs.SetInt("nohesi_opt_seed", plan.seed);
            PlayerPrefs.SetFloat("nohesi_opt_events", 0f);
            PlayerPrefs.SetInt("nohesi_opt_relay", 0);
            PlayerPrefs.SetFloat("nohesi_opt_music", 0f);
            PlayerPrefs.SetFloat("nohesi_opt_sfx", 0f);
            PlayerPrefs.SetInt("nohesi_car", 0);
            Application.runInBackground = true;
            var go = new GameObject("ShotDirector");
            DontDestroyOnLoad(go);
            go.AddComponent<ShotDirector>().plan = plan;
        }

        ShotPlan plan;

        static string Arg(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void Log(string msg)
        {
            log?.WriteLine($"[{Time.realtimeSinceStartup:0.0}] {msg}");
            log?.Flush();
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) Log($"{type}: {msg}\n{stack}");
        }

        float quitAt = 1500f;

        void FixedUpdate()
        {
            if (Time.realtimeSinceStartup > quitAt) { Log("watchdog quit"); log?.Close(); log = null; Application.Quit(); }
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(outDir);
            log = new StreamWriter(Path.Combine(outDir, "log.txt"), false);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            while (!(gm = FindAnyObjectByType<GameManager>()) || gm.State != GameManager.Phase.Title) yield return null;
            Log("title reached");
            SetupCapture();
            gm.Score.Protect(1e6f);
            gm.Score.NearMiss += (p, g, o) => { pendingNearMiss = true; nearMissAt = Time.time; };

            gm.StartCountdown();
            while (gm.State != GameManager.Phase.Playing) yield return null;
            yield return new WaitForSeconds(0.5f);
            HookHud();
            gm.Score.Protect(1e6f);

            foreach (var sh in plan.shots)
            {
                spec = sh;
                Log($"--- {sh.name}");
                IEnumerator run;
                try { run = sh.cam == "lineup" ? Lineup() : RunShot(); }
                catch (Exception e) { Log("ERR " + e); continue; }
                yield return run;
            }
            Log("done");
            log.Close();
            Application.Quit();
        }

        // ------------------------------------------------------------------ rendu

        void SetupCapture()
        {
            rig = gm.Camera;
            cam = rig.GetComponent<Camera>();
            worldRT = NewRT(W, H, 24);
            halfRT = NewRT(W / 2, H / 2, 0);
            uiRT = NewRT(W, H, 24);
            uiHalfRT = NewRT(W / 2, H / 2, 0);
            cam.targetTexture = worldRT;

            // Caméra d'interface : le HUD passe en « Screen Space - Camera », rendu à part (fond noir puis blanc)
            var ugo = new GameObject("UICam");
            ugo.transform.position = new Vector3(0f, -20000f, 0f);
            uiCam = ugo.AddComponent<Camera>();
            uiCam.enabled = false;
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.nearClipPlane = 0.05f;
            uiCam.farClipPlane = 5f;
            uiCam.allowHDR = false;
            uiCam.allowMSAA = false;
            uiCam.targetTexture = uiRT;
            var data = ugo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
        }

        /// <summary>Le HUD (inactif à l'accueil) passe en « Screen Space - Camera » sur la caméra d'interface.</summary>
        void HookHud()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas) continue;
                Log($"canvas {c.name} mode={c.renderMode} active={c.gameObject.activeInHierarchy}");
                if (c.name != "HUD") continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = uiCam;
                c.planeDistance = 1f;
                Log("canvas → camera : " + c.name);
            }
        }

        static RenderTexture NewRT(int w, int h, int depth)
        {
            var rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            return rt;
        }

        void Save(RenderTexture src, RenderTexture half, string file, bool full)
        {
            Graphics.Blit(src, half);
            Write(half, file + ".png");
            if (full) Write(src, file + "_4k.png");
        }

        static void Write(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        IEnumerator Capture(string tag = "")
        {
            yield return new WaitForEndOfFrame();
            string baseName = Path.Combine(outDir, $"{spec.name}_{shotsTaken:00}{tag}");
            Save(worldRT, halfRT, baseName, spec.hero);
            if (spec.ui)
            {
                uiCam.backgroundColor = new Color(0f, 0f, 0f, 1f);
                uiCam.Render();
                Save(uiRT, uiHalfRT, baseName + "_uib", false);
                uiCam.backgroundColor = new Color(1f, 1f, 1f, 1f);
                uiCam.Render();
                Save(uiRT, uiHalfRT, baseName + "_uiw", false);
            }
            Log($"shot {baseName} kmh={gm.Player.SpeedKmh:0} kind={gm.Road.KindAt(gm.PlayerS)} score={gm.Score.RunScore:0} x{gm.Score.Multiplier:0.0}");
            shotsTaken++;
            lastShot = Time.time;
        }

        // ------------------------------------------------------------------ plans

        bool FeatureShot => spec.road == "Roundabout" || spec.road == "Crossroads" || spec.road == "Overpass" || spec.road == "Roadworks";
        RoadFeature targetFeature;

        float FindStart()
        {
            var road = gm.Road;
            float from = gm.PlayerS + 400f;
            if (FeatureShot)
            {
                foreach (var f in road.Features)
                    if (f.GetType().Name == spec.road && f.s > from + 400f && f.s < road.Length - 800f) { targetFeature = f; return f.s - 450f; }
                return -1f;
            }
            if (spec.road == "Any") return from;
            var kind = (RoadKind)Enum.Parse(typeof(RoadKind), spec.road);
            float need = spec.cam == "lineup" ? 300f : 1400f;
            for (float at = from; at < road.Length - need - 200f; at += 50f)
            {
                bool ok = true;
                for (float k = 0f; k <= need && ok; k += 25f) ok = road.KindAt(at + k) == kind;
                if (ok) return at + 60f;
            }
            return -1f;
        }

        IEnumerator JumpToStart()
        {
            targetFeature = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float start = FindStart();
                if (start > 0f) { yield return Jump(start); yield break; }
                // Pas trouvé : on avance au bout de la route planifiée
                Log($"no {spec.road} in plan (len {gm.Road.Length:0}), jumping ahead");
                yield return Jump(gm.Road.Length - 300f);
                yield return new WaitForSeconds(1f);
            }
        }

        IEnumerator Jump(float at)
        {
            var road = gm.Road;
            var car = gm.Player;
            gm.TeleportTo(at);
            car.Body.isKinematic = true;
            gm.Traffic.ClearAll();
            for (float t = 0f; t < 3f; t += Time.deltaTime) yield return null;
            s = gm.PlayerS;
            int lanes = road.LanesAt(s, 1);
            lane = spec.road == "Highway" || lanes >= 3 ? 1 : lanes - 1;
            laneOffset = road.LaneOffset(s, lane, 1);
            Vector3 p = road.LanePosition(s, laneOffset, out var tangent);
            car.Body.isKinematic = false;
            car.Teleport(p + Vector3.up * 0.4f, Quaternion.LookRotation(tangent, Vector3.up), spec.kmh / 3.6f * 0.8f);
            Log($"jump s={s:0} kind={road.KindAt(s)} lanes={lanes}");
        }

        void ApplySpec()
        {
            var dn = gm.DayNight;
            dn.timeOfDay = spec.hour;
            dn.frozen = true;
            gm.Traffic.density = spec.traffic;
            gm.Events.frequency = spec.events;
            if (gm.Weather) gm.Weather.Force(spec.weather == "storm", true);
            gm.Events.relayInterval = 0f;
            if (spec.car >= 0)
            {
                int cur = PlayerPrefs.GetInt("nohesi_car", 0);
                int n = gm.assets.playerCars.Length;
                int delta = ((spec.car - cur) % n + n) % n;
                if (delta != 0) gm.CycleCar(delta, true);
            }
            SetFx(spec.fx);
            bool rigCam = spec.cam == "chase" || spec.cam == "chasefar" || spec.cam == "hood" || spec.cam == "bumper";
            rig.enabled = rigCam;
            if (rigCam) rig.SetView(spec.cam == "chase" ? 0 : spec.cam == "chasefar" ? 1 : spec.cam == "hood" ? 2 : 3);
            fixedCamSet = false;
        }

        void SetFx(bool on)
        {
            var fx = FindAnyObjectByType<ScreenFx>();
            if (fx) fx.enabled = on;
            var streaks = GameObject.Find("SpeedStreaks");
            if (streaks) streaks.SetActive(on);
            if (!on)
            {
                var volume = FindAnyObjectByType<Volume>();
                if (volume && volume.profile.TryGet(out ChromaticAberration ca)) ca.intensity.value = 0f;
                if (volume && volume.profile.TryGet(out LensDistortion ld)) ld.intensity.value = 0f;
            }
        }

        IEnumerator RunShot()
        {
            ApplySpec();
            yield return JumpToStart();
            ApplySpec();
            gm.Score.Protect(1e6f);
            driving = spec.drive != "none";
            shotsTaken = 0;
            pendingNearMiss = false;
            if (spec.police) { gm.Events.StartPolice("Patrouille de police !"); }

            float t0 = Time.time;
            while (Time.time - t0 < spec.warmup) yield return null;
            float shootStart = Time.time;
            while (shotsTaken < spec.count && Time.time - shootStart < spec.timeout)
            {
                if (spec.police && gm.Events.Police == null && Time.time - lastShot > 3f) gm.Events.StartPolice("Patrouille de police !");
                if (gm.Weather && gm.Weather.Stormy != (spec.weather == "storm")) gm.Weather.Force(spec.weather == "storm", true);
                bool kindOk = FeatureShot || spec.road == "Any" || gm.Road.KindAt(gm.PlayerS).ToString() == spec.road;
                if (!kindOk || gm.PlayerS > gm.Road.Length - 400f)
                {
                    if (FeatureShot && targetFeature != null && gm.PlayerS < targetFeature.s + 300f) { }
                    else
                    {
                        Log("leaving stretch, re-jump");
                        yield return JumpToStart();
                        ApplySpec();
                        yield return new WaitForSeconds(spec.warmup * 0.6f);
                        continue;
                    }
                }
                if (Time.time - lastShot >= spec.interval && Triggered())
                {
                    pendingNearMiss = false;
                    yield return Capture();
                    if (spec.cam == "trackside") fixedCamSet = false;
                }
                yield return null;
            }
            driving = false;
            if (gm.Events.Police != null) { Destroy(gm.Events.Police.gameObject); }
            Log($"end {spec.name}: {shotsTaken} shots");
        }

        bool Triggered()
        {
            var car = gm.Player;
            if ((spec.trigger == "nearmiss" || spec.trigger == "approach") && car.SpeedKmh < spec.minKmh) return false;
            switch (spec.trigger)
            {
                case "nearmiss":
                    return pendingNearMiss && Time.time - nearMissAt > 0.12f && Time.time - nearMissAt < 0.5f;
                case "approach":
                    return CarAlongside(3f, 12f, 1.6f);
                case "event":
                    return !string.IsNullOrEmpty(gm.Events.StatusText) && gm.Events.Police == null;
                case "headon":
                    return HeadOn();
                case "police":
                    return gm.Events.Police != null && gm.Events.Police.Gap < spec.triggerDist && gm.Events.Police.Gap > 5f;
                case "distance":
                    return fixedCamSet && (car.transform.position - fixedCamPos).magnitude < spec.triggerDist;
                case "feature":
                    return targetFeature != null && (car.transform.position - targetFeature.center).magnitude < spec.triggerDist;
                default:
                    return true;
            }
        }

        /// <summary>Contresens : on roule dans la voie d'en face et un véhicule arrive droit devant (35-80 m).</summary>
        bool HeadOn()
        {
            gm.Road.Project(gm.Player.transform.position, gm.PlayerS, out float lat);
            if (lat > -0.6f) return false;
            foreach (var c in gm.Traffic.Cars)
            {
                if (c.crashed || c.path != null || c.direction != -1) continue;
                float ds = c.s - gm.PlayerS;
                if (ds > 35f && ds < 80f && Mathf.Abs(c.lateral - lat) < 2f) return true;
            }
            return false;
        }

        static Light sunCache;
        static Light Sun()
        {
            if (sunCache) return sunCache;
            sunCache = RenderSettings.sun;
            if (!sunCache)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && (!sunCache || l.intensity > sunCache.intensity)) sunCache = l;
            return sunCache;
        }

        /// <summary>Un véhicule du trafic juste devant sur le côté, écart latéral faible (on est en train de le frôler).</summary>
        bool CarAlongside(float minAhead, float maxAhead, float maxGap)
        {
            var pt = gm.Player.transform;
            foreach (var c in gm.Traffic.Cars)
            {
                if (c.crashed || !c.go) continue;
                Vector3 rel = c.go.transform.position - pt.position;
                float along = Vector3.Dot(rel, pt.forward);
                float side = Mathf.Abs(Vector3.Dot(rel, pt.right));
                float gap = side - gm.Player.LocalBounds.extents.x - c.halfWidth;
                if (along > minAhead && along < maxAhead && gap < maxGap && gap > 0.1f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ conduite

        void Update()
        {
            if (!driving || gm == null || gm.State != GameManager.Phase.Playing) return;
            var car = gm.Player;
            var road = gm.Road;
            car.InputEnabled = false;
            car.Autopilot = true;
            float dt = Time.deltaTime;
            float kmh = car.SpeedKmh;
            s = gm.PlayerS;
            float lateralNow;
            road.Project(car.transform.position, s, out lateralNow);

            int lanes = Mathf.Min(road.LanesAt(s), road.LanesAt(s + Mathf.Max(60f, car.Speed * 3f)));
            lane = Mathf.Clamp(lane, 0, lanes - 1);
            float targetLat;
            bool blockedAhead = false, closeAhead = false, liftAhead = false;
            if (spec.drive == "oncoming" && road.KindAt(s) != RoadKind.Highway)
            {
                // Contresens : voie d'en face, retour à droite quand un véhicule arrive en face
                float left = road.LaneOffset(s, 0, -1), right = road.LaneOffset(s, 0, 1);
                bool threat = LaneThreat(left, car.Speed * 2.2f + 40f);
                bool rightBusy = LaneThreat(right, car.Speed * 1.2f + 20f);
                targetLat = threat && !rightBusy ? right : left;
                laneOffset = Mathf.MoveTowards(laneOffset, targetLat, 7f * dt);
                blockedAhead = threat && rightBusy;
            }
            else
            {
                // Slalom agressif : voie la plus dégagée parmi les voisines libres sur le côté
                laneTimer -= dt;
                float cur = Free(lane, false);
                if (laneTimer <= 0f && cur < Mathf.Max(60f, car.Speed * 2.2f))
                {
                    int best = lane;
                    float bestFree = cur + 15f;
                    foreach (int cand in new[] { lane - 1, lane + 1 })
                    {
                        if (cand < 0 || cand >= lanes) continue;
                        float f = Free(cand, true);
                        if (f > bestFree) { best = cand; bestFree = f; }
                    }
                    if (best != lane) { lane = best; laneTimer = 0.7f; }
                }
                float baseOff = road.LaneOffset(s, lane, 1);
                // Frôlement : se rapproche du véhicule qu'on dépasse dans la voie voisine
                if (spec.drive == "weave") baseOff += NearBias(baseOff);
                laneOffset = road.FeatureAt(s, 20f) != null ? baseOff : Mathf.MoveTowards(laneOffset, baseOff, 6f * dt);
                cur = Free(lane, false);
                closeAhead = cur < car.Speed * 0.9f + 6f;
                liftAhead = cur < car.Speed * 1.5f + 8f;
            }

            float look = Mathf.Lerp(12f, 45f, car.Speed / 70f);
            float shape = road.LaneOffset(s + look, Mathf.Max(0, lane), 1) - road.LaneOffset(s, Mathf.Max(0, lane), 1);
            if (spec.drive == "oncoming") shape = 0f;
            Vector3 target = road.LanePosition(s + look, laneOffset + shape, out _);
            Vector3 to = target - car.transform.position;
            float angle = Vector3.SignedAngle(car.transform.forward, to, Vector3.up);
            car.AutoSteer = Mathf.Clamp(angle / 12f, -1f, 1f);

            road.Evaluate(s + 10f, out var t0, out _);
            road.Evaluate(s + 10f + car.Speed * 2.5f, out var t1, out _);
            float curve = Vector3.Angle(t0, t1);
            float safeKmh = curve > 1f ? Mathf.Clamp(3600f / curve, 90f, spec.kmh) : spec.kmh;
            float limit = road.TrafficSpeedAhead(s, 1, 5f);
            if (limit < 200f) safeKmh = Mathf.Min(safeKmh, limit * 3.6f * 1.25f);
            float throttle = kmh < safeKmh ? 1f : kmh < safeKmh + 5f ? 0.3f : 0f;
            float brake = kmh > safeKmh + 15f ? 0.6f : 0f;
            if (blockedAhead && kmh > 80f) { throttle = 0f; brake = 0.5f; }
            if (liftAhead) throttle = Mathf.Min(throttle, 0.1f);
            if (closeAhead && kmh > 70f) { throttle = 0f; brake = 0.7f; }
            car.AutoThrottle = throttle;
            car.AutoBrake = brake;
        }

        float NearBias(float baseOff)
        {
            var pt = gm.Player.transform;
            foreach (var c in gm.Traffic.Cars)
            {
                if (c.crashed || c.path != null || c.direction != 1) continue;
                float ds = c.s - s;
                if (ds < -6f || ds > 30f) continue;
                float dl = c.lateral - baseOff;
                if (Mathf.Abs(dl) > 2.5f && Mathf.Abs(dl) < 5.5f) return Mathf.Sign(dl) * spec.bias;
            }
            return 0f;
        }

        /// <summary>Distance libre devant dans une voie ; -1 si un véhicule occupe le côté (changement impossible).</summary>
        float Free(int l, bool checkSide)
        {
            float off = gm.Road.LaneOffset(s, l, 1);
            float best = 500f;
            foreach (var c in gm.Traffic.Cars)
            {
                if (c.crashed || c.path != null) continue;
                if (Mathf.Abs(c.lateral - off) > 2.4f) continue;
                float ds = c.s - s;
                if (checkSide && ds > -8f && ds < 9f) return -1f;
                if (ds < 3f) continue;
                if (ds < best) best = ds;
            }
            return best;
        }

        bool Blocked(int l, float range)
        {
            float off = gm.Road.LaneOffset(s, l, 1);
            foreach (var c in gm.Traffic.Cars)
            {
                if (c.crashed) continue;
                float ds = c.s - s;
                if (ds < -5f || ds > range) continue;
                if (Mathf.Abs(c.lateral - off) < 2.6f) return true;
            }
            return false;
        }

        bool LaneThreat(float off, float range)
        {
            foreach (var c in gm.Traffic.Cars)
            {
                if (c.crashed || c.path != null) continue;
                float ds = c.s - s;
                if (ds < -4f || ds > range) continue;
                if (Mathf.Abs(c.lateral - off) < 2.4f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ caméras libres

        void LateUpdate()
        {
            if (spec == null || gm == null || rig == null || rig.enabled) return;
            var car = gm.Player.transform;
            Quaternion frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(car.forward, Vector3.up), Vector3.up);
            switch (spec.cam)
            {
                case "rel":
                {
                    Vector3 co = spec.camOffset, lo = spec.lookOffset;
                    if (spec.sunLit && Sun())
                    {
                        Vector3 toSun = Quaternion.Inverse(frame) * -Sun().transform.forward;
                        if (toSun.x * co.x < 0f) co.x = -co.x;
                        if (toSun.z * co.z < 0f) { co.z = -co.z; lo.z = -lo.z; }
                    }
                    Vector3 pos = car.position + frame * co;
                    Vector3 look = car.position + frame * lo;
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
                    cam.fieldOfView = spec.fov;
                    break;
                }
                case "police":
                {
                    var pol = gm.Events.Police;
                    if (pol == null) break;
                    Vector3 pp = pol.Position;
                    Vector3 dir = Vector3.ProjectOnPlane(car.position - pp, Vector3.up);
                    if (dir.sqrMagnitude < 1f) dir = car.forward;
                    Quaternion pf = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    Vector3 pos = pp + pf * spec.camOffset;
                    Vector3 look = Vector3.Lerp(pp, car.position, 0.55f) + Vector3.up * spec.lookOffset.y;
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
                    cam.fieldOfView = spec.fov;
                    break;
                }
                case "trackside":
                {
                    if (!fixedCamSet)
                    {
                        float at = gm.PlayerS + spec.lead;
                        float hw = gm.Road.HalfWidth(at);
                        Vector3 p = gm.Road.LanePosition(at, spec.side * (hw - 1.3f), out _);
                        fixedCamPos = p + Vector3.up * spec.height;
                        fixedCamSet = true;
                    }
                    Vector3 look = car.position + frame * spec.lookOffset;
                    cam.transform.SetPositionAndRotation(fixedCamPos, Quaternion.LookRotation(look - fixedCamPos, Vector3.up));
                    cam.fieldOfView = spec.fov;
                    // Dépassée : nouvelle position devant
                    if (Vector3.Dot(car.position - fixedCamPos, car.forward) > 5f) fixedCamSet = false;
                    break;
                }
                case "aerial":
                {
                    Vector3 center = targetFeature != null ? targetFeature.center : car.position;
                    Vector3 pos = center + frame * spec.camOffset;
                    Vector3 look = center + frame * spec.lookOffset;
                    if (targetFeature != null)
                    {
                        Quaternion ff = Quaternion.LookRotation(Vector3.ProjectOnPlane(targetFeature.forward, Vector3.up), Vector3.up);
                        pos = center + ff * spec.camOffset;
                        look = center + ff * spec.lookOffset;
                    }
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
                    cam.fieldOfView = spec.fov;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ garage (voitures jouables alignées)

        IEnumerator Lineup()
        {
            ApplySpec();
            yield return JumpToStart();
            driving = false;
            var car = gm.Player;
            gm.Traffic.density = 0f;
            gm.Traffic.ClearAll();
            float at = gm.PlayerS + 40f;
            var road = gm.Road;
            // La voiture du joueur est garée hors champ
            car.Body.isKinematic = true;
            foreach (var r in car.GetComponentsInChildren<Renderer>()) r.enabled = false;
            var cars = gm.assets.playerCars;
            Vector3 center = road.LanePosition(at, road.KindAt(at) == RoadKind.Highway ? road.LaneOffset(at, 1, 1) : 0f, out var tangent);
            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(tangent, Vector3.up), Vector3.up);
            bool hw = road.KindAt(at) == RoadKind.Highway;
            float mid = hw ? road.LaneOffset(at, 1, 1) : 0f;
            float spacing = hw ? (cars.Length > 5 ? 3.1f : 3.4f) : 2.7f;
            var placed = new List<(GameObject go, float size, string name)>();
            for (int i = 0; i < cars.Length; i++)
            {
                var go = Instantiate(cars[i].prefab);
                go.transform.localScale = Vector3.one * cars[i].modelScale;
                foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
                Bounds b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                placed.Add((go, b.size.x * b.size.z, cars[i].displayName));
            }
            // Plus de 5 voitures : deux rangées, les plus petites devant (côté caméra)
            var order = new List<int>();
            for (int i = 0; i < placed.Count; i++) order.Add(i);
            int frontCount = placed.Count > 5 ? placed.Count / 2 : 0;
            if (frontCount > 0) order.Sort((a, c) => placed[a].size.CompareTo(placed[c].size));
            for (int k = 0; k < order.Count; k++)
            {
                var (go, _, name) = placed[order[k]];
                bool front = k < frontCount;
                int idx = front ? k : k - frontCount;
                int rowCount = front ? frontCount : order.Count - frontCount;
                float lat = mid + (idx - (rowCount - 1) * 0.5f) * spacing;
                float along = frontCount == 0 ? -Mathf.Abs(idx - (rowCount - 1) * 0.5f) * 1.2f : (front ? 3.8f : -3.8f);
                Vector3 p = road.LanePosition(at + along, lat, out var tg);
                float yaw = frontCount == 0 ? -8f + idx * 5f : -9f;
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.ProjectOnPlane(tg, Vector3.up), Vector3.up) * Quaternion.Euler(0f, yaw, 0f));
                // Pose au sol : bas des roues sur la chaussée
                float minY = float.MaxValue;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) minY = Mathf.Min(minY, r.bounds.min.y);
                float groundY = p.y;
                if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 20f, ~(1 << 2))) groundY = hit.point.y;
                go.transform.position += Vector3.up * (groundY - minY);
                props.Add(go);
                Log($"lineup {name} lat {lat:0.0} along {along:0.0}");
            }
            rig.enabled = false;
            shotsTaken = 0;
            Vector3[] offsets = { spec.camOffset, new Vector3(-spec.camOffset.x, spec.camOffset.y, spec.camOffset.z), new Vector3(spec.camOffset.x * 0.4f, spec.camOffset.y + 2.2f, spec.camOffset.z * 1.25f) };
            for (int k = 0; k < Mathf.Min(spec.count, offsets.Length); k++)
            {
                Vector3 pos = center + rot * offsets[k];
                Vector3 look = center + rot * spec.lookOffset;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
                cam.fieldOfView = spec.fov;
                for (int f = 0; f < 20; f++) yield return null;
                yield return Capture();
            }
            foreach (var p in props) Destroy(p);
            props.Clear();
            foreach (var r in car.GetComponentsInChildren<Renderer>()) r.enabled = true;
            car.Body.isKinematic = false;
            rig.enabled = true;
        }
    }
}
