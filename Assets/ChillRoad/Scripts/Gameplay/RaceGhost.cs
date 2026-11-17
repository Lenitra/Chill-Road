using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Fantôme du record personnel d'une course (<see cref="Races"/>) : la voiture du meilleur temps refait son
    /// parcours pendant que l'on court, pour voir où l'on gagne et où l'on perd.
    /// Pendant chaque course, la position de la voiture est relevée <see cref="Rate"/> fois par seconde
    /// (<see cref="Run"/>) ; si le record tombe, ce relevé devient le fantôme du tracé et il est écrit sur la machine
    /// (un fichier par tracé dans le dossier des données du jeu). Un record plus ancien que cette fonction n'a pas de
    /// relevé : son fantôme est reconstitué d'après ses temps de passage (<see cref="Races"/>), dans la voie du
    /// parcours, chaque porte franchie à l'instant du record ; le vrai relevé le remplace dès que le record tombe.
    /// Le fantôme n'a rien de solide : le modèle de la voiture du record, en coque translucide d'une seule teinte
    /// (shader ChillRoad/Ghost), sans collision ni ombre. Il s'efface quand il chevauche la voiture du joueur ou passe
    /// tout près de la caméra : il ne cache jamais la route.
    /// </summary>
    public class RaceGhost
    {
        /// <summary>Relevés par seconde.</summary>
        public const float Rate = 20f;
        const int Magic = 0x31474843;           // « CHG1 »
        const int MaxSamples = 20 * 60 * 30;    // une demi-heure
        const float Jump = 30f;                 // au-delà entre deux relevés : replacement sur la route, pas de glissade

        /// <summary>Parcours relevé : la position de la voiture à intervalles réguliers depuis le départ du chrono.</summary>
        public class Run
        {
            /// <summary>Voiture (<see cref="VehicleSpec.id"/>) et temps de la course.</summary>
            public string car = "";
            public float time;
            readonly List<Vector3> pos = new List<Vector3>();
            readonly List<Quaternion> rot = new List<Quaternion>();
            Vector3 lastPos;
            Quaternion lastRot;
            float lastTime;

            public int Count => pos.Count;

            /// <summary>Ajoute un relevé tel quel (fantôme reconstitué, un relevé tous les 1/<see cref="Rate"/> s).</summary>
            public void Add(Vector3 p, Quaternion r)
            {
                if (pos.Count >= MaxSamples) return;
                pos.Add(p);
                rot.Add(r);
            }

            /// <summary>
            /// Voiture à l'instant <paramref name="t"/> du chrono (0 : départ). Les relevés tombés depuis le dernier
            /// appel sont ajoutés, interpolés entre les deux positions : leur cadence ne dépend pas de celle des images.
            /// </summary>
            public void Record(float t, Transform car)
            {
                Vector3 p = car.position;
                Quaternion r = car.rotation;
                if (pos.Count == 0)
                {
                    pos.Add(p);
                    rot.Add(r);
                }
                else
                {
                    bool jump = (p - lastPos).sqrMagnitude > Jump * Jump;
                    while (pos.Count < MaxSamples && pos.Count / Rate <= t)
                    {
                        float k = jump || t <= lastTime ? 1f : Mathf.Clamp01((pos.Count / Rate - lastTime) / (t - lastTime));
                        pos.Add(Vector3.Lerp(lastPos, p, k));
                        rot.Add(Quaternion.Slerp(lastRot, r, k));
                    }
                }
                lastPos = p;
                lastRot = r;
                lastTime = t;
            }

            /// <summary>Position à l'instant t : entre deux relevés, sauf par-dessus un replacement sur la route.</summary>
            public void Sample(float t, out Vector3 p, out Quaternion r)
            {
                float f = Mathf.Clamp(t * Rate, 0f, pos.Count - 1);
                int i = Mathf.Min((int)f, pos.Count - 1), j = Mathf.Min(i + 1, pos.Count - 1);
                float k = f - i;
                if ((pos[j] - pos[i]).sqrMagnitude > Jump * Jump) k = k < 0.5f ? 0f : 1f;
                p = Vector3.Lerp(pos[i], pos[j], k);
                r = Quaternion.Slerp(rot[i], rot[j], k);
            }

            static string FileOf(string id) => Path.Combine(Application.persistentDataPath, "ghost_" + id + ".bin");

            public void Save(string id)
            {
                try
                {
                    using var w = new BinaryWriter(File.Create(FileOf(id)));
                    w.Write(Magic);
                    w.Write(car ?? "");
                    w.Write(time);
                    w.Write(pos.Count);
                    for (int i = 0; i < pos.Count; i++)
                    {
                        w.Write(pos[i].x); w.Write(pos[i].y); w.Write(pos[i].z);
                        w.Write(rot[i].x); w.Write(rot[i].y); w.Write(rot[i].z); w.Write(rot[i].w);
                    }
                }
                catch (Exception e) { Debug.LogWarning("[RaceGhost] Fantôme non enregistré : " + e.Message); }
            }

            /// <summary>Fantôme enregistré du tracé, s'il est bien celui du record <paramref name="best"/> ; sinon null.</summary>
            public static Run Load(string id, float best)
            {
                try
                {
                    string file = FileOf(id);
                    if (!File.Exists(file)) return null;
                    using var r = new BinaryReader(File.OpenRead(file));
                    if (r.ReadInt32() != Magic) return null;
                    var run = new Run { car = r.ReadString(), time = r.ReadSingle() };
                    int n = r.ReadInt32();
                    if (n < 2 || n > MaxSamples || Mathf.Abs(run.time - best) > 0.01f) return null;
                    for (int i = 0; i < n; i++)
                    {
                        run.pos.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                        run.rot.Add(new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                    }
                    return run;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[RaceGhost] Fantôme illisible : " + e.Message);
                    return null;
                }
            }

            public static void Delete(string id)
            {
                try { File.Delete(FileOf(id)); }
                catch (Exception e) { Debug.LogWarning("[RaceGhost] Fantôme non effacé : " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ affichage

        static readonly int TintId = Shader.PropertyToID("_Tint"), AlphaId = Shader.PropertyToID("_Alpha");
        static readonly Color Tint = new Color(0.45f, 0.85f, 1f, 1f);
        // Fondu (début, fin, en mètres) : à la voiture du joueur, à la caméra
        const float CarNear = 3f, CarFar = 9f, EyeNear = 3f, EyeFar = 8f;

        readonly GameManager game;
        readonly Transform root;
        readonly Material material;
        GameObject model;
        string modelCar;
        float fade;

        public RaceGhost(Transform parent, GameManager game)
        {
            this.game = game;
            root = new GameObject("RaceGhost").transform;
            root.SetParent(parent, false);
            root.gameObject.SetActive(false);
            var shader = game.assets.ghost ? game.assets.ghost : Shader.Find("ChillRoad/Ghost");
            if (shader)
            {
                material = new Material(shader) { name = "Race ghost (runtime)" };
                material.SetColor(TintId, Tint);
            }
            MiniMap.Track(this, () => root && root.gameObject.activeSelf ? root : null, Tint);
        }

        /// <summary>Modèle de la voiture du record (celle du joueur si elle n'existe plus), tout en matériau fantôme.</summary>
        void Build(string car)
        {
            if (model) UnityEngine.Object.Destroy(model);
            modelCar = car;
            VehicleSpec spec = null;
            foreach (var s in game.assets.playerCars)
                if (s != null && s.id == car) { spec = s; break; }
            if (spec == null) spec = game.Player ? game.Player.Spec : null;
            if (spec == null || !spec.prefab) return;
            model = UnityEngine.Object.Instantiate(spec.prefab, root);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * spec.modelScale;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
            foreach (var l in model.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                // Couche « Ignore Raycast », comme les voitures : ni les suspensions ni la caméra ne le touchent
                r.gameObject.layer = 2;
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
        }

        /// <summary>Place le fantôme à l'instant <paramref name="time"/> du chrono ; une fois arrivé, il s'efface.</summary>
        public void Show(Run run, float time, float dt)
        {
            if (!material || run == null || run.Count < 2) { Hide(); return; }
            if (!model || modelCar != run.car) Build(run.car);
            if (!model) { Hide(); return; }
            run.Sample(time, out var p, out var r);
            root.SetPositionAndRotation(p, r);
            fade = Mathf.MoveTowards(fade, time < run.time ? 1f : 0f, dt * 2.5f);
            float toCar = game.Player ? Vector3.Distance(p, game.Player.transform.position) : CarFar;
            float toEye = game.Camera ? Vector3.Distance(p, game.Camera.transform.position) : EyeFar;
            float alpha = fade * Mathf.Min(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CarNear, CarFar, toCar)),
                                           Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EyeNear, EyeFar, toEye)));
            material.SetFloat(AlphaId, alpha);
            // Invisible : rien n'est dessiné (la passe de profondeur cacherait ce qui est derrière)
            if (model.activeSelf != alpha > 0.01f) model.SetActive(alpha > 0.01f);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            fade = 0f;
            if (root && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }
    }
}
