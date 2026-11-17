using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Salissure de la voiture : la boue (chemins de terre, herbe hors route) et la neige projetées par les roues montent
    /// peu à peu du bas de caisse, en aplats au bord déchiqueté (ChillRoad/Toon, _Grime*). Poussière claire par temps sec,
    /// boue sombre sous la pluie. L'eau lave : un gué rince d'abord les roues et le bas de caisse jusqu'à la ligne d'eau
    /// (la gerbe un peu plus haut à vive allure), une eau assez profonde lave tout. La pluie rince lentement sur le
    /// goudron, la neige fond doucement hors des zones enneigées. Remise à neuf à chaque changement de voiture.
    /// </summary>
    public class VehicleGrime : MonoBehaviour
    {
        struct Slot { public Renderer r; public int k; public Vector4 axis; public float scale; }

        VehicleController car;
        VehicleSplash splash;
        Transform model;
        readonly List<Slot> slots = new List<Slot>();
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock mpb;
        float height = 1.3f;

        float mud, snow;
        /// <summary>Lignes lavées (hauteur relative sur la voiture, -1 : rien de lavé).</summary>
        float mudWash = -1f, snowWash = -1f;
        Color mudColor = new Color(0.36f, 0.29f, 0.19f);
        Vector4 shown = new Vector4(-1f, -1f, -1f, -1f);
        Color shownColor;

        static readonly int GrimeId = Shader.PropertyToID("_Grime"), ColorId = Shader.PropertyToID("_GrimeColor"),
                            AxisId = Shader.PropertyToID("_GrimeAxis"), ScaleId = Shader.PropertyToID("_GrimeScale");

        // Couleurs déposées (sRGB) : chemin de terre / herbe, par temps sec / mouillé
        static readonly Color DirtDry = new Color(0.60f, 0.51f, 0.38f), DirtWet = new Color(0.34f, 0.25f, 0.16f);
        static readonly Color GrassDry = new Color(0.45f, 0.39f, 0.25f), GrassWet = new Color(0.29f, 0.23f, 0.13f);

        /// <summary>Boue et neige sur la voiture (0-1).</summary>
        public float Mud => mud;
        public float Snow => snow;

        public void Init(VehicleController c, VehicleSplash s)
        {
            car = c;
            splash = s;
            mpb = new MaterialPropertyBlock();
        }

        /// <summary>Voiture propre (nouvelle voiture).</summary>
        public void Clean()
        {
            mud = snow = 0f;
            mudWash = snowWash = -1f;
            shown = new Vector4(-1f, -1f, -1f, -1f);
        }

        /// <summary>Impose un état de salissure (outils de mise au point, captures).</summary>
        public void Set(float mudAmount, float snowAmount, Color? color = null)
        {
            mud = Mathf.Clamp01(mudAmount);
            snow = Mathf.Clamp01(snowAmount);
            mudWash = snowWash = -1f;
            if (color.HasValue) mudColor = color.Value;
        }

        /// <summary>
        /// Emplacements de matériau cartoon du modèle et, pour chacun, comment passer de sa position objet à la hauteur
        /// relative sur la voiture (0 bas de caisse, 1 toit). Les roues sont comptées juste sous la caisse : elles se
        /// salissent les premières et se lavent dans le moindre gué.
        /// </summary>
        void Rebuild()
        {
            model = car.Model;
            slots.Clear();
            Clean();
            if (!model) return;
            var b = car.LocalBounds;
            height = Mathf.Max(0.5f, b.size.y);
            Matrix4x4 toCar = car.transform.worldToLocalMatrix;
            model.GetComponentsInChildren(true, renderers);
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mats = r.sharedMaterials;
                Matrix4x4 m = toCar * r.transform.localToWorldMatrix;
                bool wheel = false;
                for (var t = r.transform; t && t != model; t = t.parent)
                    if (t.name.StartsWith("wheel")) { wheel = true; break; }
                Vector3 row = new Vector3(m.m10, m.m11, m.m12);
                Vector4 axis = new Vector4(row.x / height, row.y / height, row.z / height, (m.m13 - b.min.y) / height);
                if (wheel)
                {
                    // Roue : la boue tient d'abord sur une moitié (repère de la roue, elle tourne avec), puis partout
                    var mf = r.GetComponent<MeshFilter>();
                    Bounds wb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                    float radius = Mathf.Max(0.05f, Mathf.Max(wb.extents.y, wb.extents.z) * row.magnitude);
                    Vector3 ax = row * (0.3f / radius);
                    axis = new Vector4(ax.x, ax.y, ax.z, -0.12f - Vector3.Dot(ax, wb.center));
                }
                float scale = new Vector3(m.m00, m.m10, m.m20).magnitude;
                for (int k = 0; k < mats.Length; k++)
                    if (mats[k] && mats[k].HasProperty(GrimeId))
                        slots.Add(new Slot { r = r, k = k, axis = axis, scale = scale });
            }
            renderers.Clear();
        }

        void Update()
        {
            if (!car || car.Spec == null) return;
            if (car.Model != model) Rebuild();
            float dt = Time.deltaTime;
            if (dt <= 0f || car.Remote) return;

            float speed = car.Speed;
            float s01 = Mathf.Clamp01(speed / 20f);
            float depth = splash ? splash.Depth : 0f;
            bool inWater = depth > 0.02f;
            float grounded = car.GroundedWheels > 0 && !inWater ? 1f : 0f;
            float wet = car.Wetness;
            // Projections : rien à l'arrêt, davantage vite et en glisse
            float throw01 = grounded * (speed > 1f ? 0.15f + s01 + car.SkidAmount * 0.8f + car.WheelSpin * 0.5f : car.WheelSpin * 0.5f);
            float dirtShare = car.OnRoad ? car.SurfaceDirt : 1f;
            float snowShare = Mathf.Clamp01(car.SurfaceSnow);

            float mudAdd = throw01 * dirtShare * (1f - snowShare) * (1f + wet * 0.8f) / 50f * dt;
            if (mudAdd > 0f)
            {
                Color dep = car.OnRoad ? Color.Lerp(DirtDry, DirtWet, wet) : Color.Lerp(GrassDry, GrassWet, wet);
                mudColor = Color.Lerp(mudColor, dep, Mathf.Clamp01(mudAdd / Mathf.Max(mud + mudAdd, 0.02f)));
                mud = Mathf.Min(1f, mud + mudAdd);
                mudWash = Redirty(mudWash, mudAdd);
            }
            float snowAdd = throw01 * snowShare * (car.OnRoad ? 0.6f : 1f) / 35f * dt;
            if (snowAdd > 0f)
            {
                snow = Mathf.Min(1f, snow + snowAdd);
                snowWash = Redirty(snowWash, snowAdd);
            }

            // La pluie rince doucement sur le goudron ; la neige fond hors de la neige
            if (car.OnRoad && wet > 0f) mud = Mathf.Max(0f, mud - dt * wet * (1f - dirtShare) * 0.004f);
            if (snowShare < 0.05f) snow = Mathf.Max(0f, snow - dt * (1f + s01) / 240f);

            // Dans l'eau : rinçage jusqu'à la ligne d'eau (un peu au-dessus à vive allure, la gerbe arrose)
            if (inWater)
            {
                float line = (depth - (splash ? splash.Clearance : 0.25f)) / height + 0.03f + s01 * 0.12f;
                float rate = dt * (0.5f + 2f * s01 + Mathf.Clamp01(line) * 2.5f);
                Wash(ref mud, ref mudWash, line, rate);
                Wash(ref snow, ref snowWash, line, rate * 1.5f);
            }
            Push();
        }

        /// <summary>De nouvelles projections resalissent d'abord le bas (la ligne lavée redescend).</summary>
        static float Redirty(float wash, float add)
        {
            if (wash <= -1f) return wash;
            wash -= add * 8f;
            return wash < -0.3f ? -1f : wash;
        }

        /// <summary>Monte la ligne lavée vers la ligne d'eau ; lavée au-dessus de la couche entière : voiture propre.</summary>
        static void Wash(ref float amount, ref float wash, float line, float rate)
        {
            if (amount <= 0f) { wash = -1f; return; }
            if (wash < -0.3f) wash = -0.3f;
            if (line > wash) wash = Mathf.MoveTowards(wash, line, rate);
            // Haut de la couche (bord irrégulier compris, Toon.shader GrimeMask) et gouttes au-dessus
            float top = amount * 0.6f - 0.07f + 0.15f + amount * 0.2f;
            if (wash >= top) { amount = 0f; wash = -1f; }
        }

        void Push()
        {
            var g = new Vector4(mud, snow, mud > 0f ? mudWash : -1f, snow > 0f ? snowWash : -1f);
            if ((g - shown).sqrMagnitude < 1e-5f && mudColor == shownColor) return;
            shown = g;
            shownColor = mudColor;
            foreach (var s in slots)
            {
                if (!s.r) continue;
                s.r.GetPropertyBlock(mpb, s.k);
                mpb.SetVector(GrimeId, g);
                mpb.SetColor(ColorId, mudColor);
                mpb.SetVector(AxisId, s.axis);
                mpb.SetFloat(ScaleId, s.scale);
                s.r.SetPropertyBlock(mpb, s.k);
            }
        }
    }
}
