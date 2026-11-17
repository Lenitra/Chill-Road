using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Captures pour le site (copie du projet uniquement) : « Capture.exe -siteshots plan.json -out dossier ».
    /// Chaque prise : voiture, route (type, point, portion pont/tunnel), heure, orage, vitesse, caméra relative à la voiture.
    /// </summary>
    [DefaultExecutionOrder(5000)]
    public class SiteShots : MonoBehaviour
    {
        [System.Serializable]
        public class Shot
        {
            public string name;
            public string car = "";
            public float x, z;
            public string kind = "";       // Highway, Main, Mountain, Street, Lane, Track, Ramp ("" = toutes)
            public string span = "";       // bridge / tunnel ("" = n'importe où)
            public float spanOffset = 0f;  // abscisse relative au début de la portion
            public int dir = 1;
            public float hour = 15f;
            public bool storm;
            public float speed = 0f;       // km/h
            public float drive = 5f;       // secondes de route avant la prise
            public float traffic = 1.3f;
            public bool hud;               // vraie caméra de poursuite + HUD
            public Vector3 cam = new Vector3(4f, 1.4f, 7f);   // droite, haut, avant (repère voiture)
            public Vector3 look = new Vector3(0f, 0.7f, 0f);
            public float fov = 40f;
            public bool world;             // cam/look en coordonnées monde (relatives au point de la route)
            public bool useEye;            // caméra posée en un point absolu (eye), visant la voiture + look
            public Vector3 eye;
        }

        [System.Serializable] class Plan { public Shot[] shots; }

        static string Arg(string key)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void French()
        {
            if (Arg("-siteshots") == null) return;
            PlayerPrefs.SetInt("chillroad_opt_lang", 1);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Arg("-siteshots") == null) return;
            new GameObject("SiteShots").AddComponent<SiteShots>();
        }

        GameManager game;
        Shot current;
        RoadNetwork.Edge edge;
        float s;
        bool place;
        Vector3 anchor;
        Quaternion anchorRot;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Arg("-siteshots")));
            string outDir = Arg("-out") ?? "shots";
            Directory.CreateDirectory(outDir);
            while (!(game = FindFirstObjectByType<GameManager>())) yield return null;
            while (game.State != GameManager.Phase.Title && game.State != GameManager.Phase.Playing) yield return null;
            VegetationRenderer.NoFrustumCull = true;
            if (game.State == GameManager.Phase.Title) game.StartCountdown();
            while (game.State != GameManager.Phase.Playing) yield return null;
            yield return new WaitForSeconds(2f);
            var cars = game.assets.playerCars;
            for (int i = 0; i < cars.Length; i++) Debug.Log($"[SiteShots] car {i} {cars[i].displayName}");

            foreach (var shot in plan.shots)
            {
                current = null;
                place = false;
                int ci = -1;
                for (int i = 0; i < cars.Length; i++)
                    if (!string.IsNullOrEmpty(shot.car) && (cars[i].displayName ?? "").ToLowerInvariant().Contains(shot.car.ToLowerInvariant())) ci = i;
                if (ci >= 0) game.SelectCar(ci, false);
                game.Weather.Force(shot.storm, true);
                game.DayNight.timeOfDay = shot.hour;
                game.DayNight.frozen = true;
                game.Traffic.density = shot.traffic;
                if (!Find(shot)) { Debug.LogWarning("[SiteShots] no road for " + shot.name); continue; }
                float v = shot.speed / 3.6f;
                // départ en arrière pour arriver au point voulu au moment de la prise
                float start = s - shot.dir * v * shot.drive;
                start = Mathf.Clamp(start, 2f, edge.Length - 2f);
                s = start;
                Vector3 pos = edge.LanePosition(s, shot.dir, out var fwd);
                game.Traffic.ClearAround(pos, 14f);
                game.Player.Teleport(pos + Vector3.up * 0.4f, Quaternion.LookRotation(fwd, Vector3.up), v);
                game.Player.InputEnabled = false;
                game.Player.Parked = v < 0.1f;
                game.Player.Autopilot = v > 0.1f;
                current = shot;
                game.Hud.SetVisible(shot.hud);
                game.Camera.Directed = !shot.hud;
                if (shot.hud) game.Camera.SetView(0);
                yield return new WaitForSeconds(Mathf.Max(shot.drive, 2.5f));
                if (shot.world) { anchor = game.Player.transform.position; place = true; }
                yield return new WaitForSeconds(0.15f);
                yield return new WaitForEndOfFrame();
                string file = Path.Combine(outDir, shot.name + ".png");
                ScreenCapture.CaptureScreenshot(file, 2);
                Debug.Log("[SiteShots] " + file + " car=" + game.Player.Spec.displayName + " kmh=" + game.Player.SpeedKmh.ToString("F0"));
                yield return new WaitForSeconds(0.6f);
            }
            game.Player.Autopilot = false;
            Application.Quit();
        }

        bool Find(Shot shot)
        {
            edge = null;
            float best = float.MaxValue;
            foreach (var e in game.Road.Edges)
            {
                if (!string.IsNullOrEmpty(shot.kind) && e.kind.ToString() != shot.kind) continue;
                if (!string.IsNullOrEmpty(shot.span))
                {
                    if (e.spans == null) continue;
                    foreach (var sp in e.spans)
                    {
                        if (sp.kind != shot.span) continue;
                        float mid = (sp.s0 + sp.s1) * 0.5f;
                        Vector3 p = e.Evaluate(mid, out _, out _);
                        float d = new Vector2(p.x - shot.x, p.z - shot.z).sqrMagnitude;
                        if (d < best) { best = d; edge = e; s = shot.dir > 0 ? sp.s0 + shot.spanOffset : sp.s1 - shot.spanOffset; }
                    }
                    continue;
                }
                for (int i = 0; i < e.pts.Length; i++)
                {
                    float d = new Vector2(e.pts[i].x - shot.x, e.pts[i].z - shot.z).sqrMagnitude;
                    if (d < best) { best = d; edge = e; s = e.dist[i]; }
                }
            }
            if (edge != null && edge.oneway && shot.dir < 0) shot.dir = 1;
            return edge != null;
        }

        void FixedUpdate()
        {
            if (current == null || !game || edge == null) return;
            var car = game.Player;
            if (!car.Autopilot) return;
            // abscisse suivie : projection de la voiture sur l'axe
            Vector3 p = car.transform.position;
            float bestD = float.MaxValue, bestS = s;
            for (float t = s - 15f; t <= s + 25f; t += 1f)
            {
                Vector3 q = edge.LanePosition(Mathf.Clamp(t, 0f, edge.Length), current.dir, out _);
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; bestS = t; }
            }
            s = Mathf.Clamp(bestS, 0f, edge.Length);
            float v = car.Speed;
            float ahead = 8f + v * 0.6f;
            Vector3 target = edge.LanePosition(Mathf.Clamp(s + current.dir * ahead, 0f, edge.Length), current.dir, out _);
            Vector3 to = target - p; to.y = 0f;
            Vector3 f = car.transform.forward; f.y = 0f;
            float angle = Vector3.SignedAngle(f, to, Vector3.up);
            car.AutoSteer = Mathf.Clamp(angle / 25f, -1f, 1f);
            float want = current.speed / 3.6f;
            float err = want - v;
            car.AutoThrottle = Mathf.Clamp01(err * 0.25f + 0.15f);
            car.AutoBrake = err < -3f ? Mathf.Clamp01(-err * 0.1f) : 0f;
        }

        void LateUpdate()
        {
            if (current == null || current.hud || !game) return;
            var cam = game.Camera.GetComponent<Camera>();
            var t = game.Player.transform;
            Vector3 eye, at;
            if (current.useEye)
            {
                eye = current.eye;
                at = t.position + current.look;
            }
            else if (current.world && place)
            {
                eye = anchor + current.cam;
                at = anchor + current.look;
            }
            else if (current.world)
            {
                eye = t.position + current.cam;
                at = t.position + current.look;
            }
            else
            {
                Vector3 f = t.forward; f.y = 0f; f.Normalize();
                var frame = Quaternion.LookRotation(f, Vector3.up);
                eye = t.position + frame * current.cam;
                at = t.position + frame * current.look;
            }
            if (game.Map != null)
            {
                float ground = game.Map.Height(eye.x, eye.z) + 0.6f;
                if (eye.y < ground) eye.y = ground;
            }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
            cam.fieldOfView = current.fov;
        }
    }
}
