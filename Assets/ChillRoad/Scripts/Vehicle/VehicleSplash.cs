using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Voiture dans l'eau (gué, rivière, bord de lac) : gerbes projetées par chaque roue, vague d'étrave quand la caisse
    /// touche l'eau, sillage d'écume à la surface, grande gerbe à l'entrée dans l'eau et gouttes qui tombent de la caisse
    /// en sortant. Donne aussi la hauteur d'eau sous la voiture (<see cref="VehicleGrime"/> : lavage).
    /// </summary>
    public class VehicleSplash : MonoBehaviour
    {
        VehicleController car;
        Func<Vector3, float> waterLevel;
        ParticleSystem drops, foam;
        Transform model;
        readonly List<Transform> wheels = new List<Transform>();
        readonly List<float> radii = new List<float>();
        float[] sprayAcc = new float[0], foamAcc = new float[0];
        float bowAcc, dripAcc, dripTime;

        /// <summary>Hauteur d'eau au-dessus du bas des roues (m, la plus forte des roues ; 0 hors de l'eau).</summary>
        public float Depth { get; private set; }
        /// <summary>Garde au sol : du bas des roues au bas de caisse (m).</summary>
        public float Clearance { get; private set; } = 0.25f;

        const float DripDuration = 3.5f;

        static Material dropMaterial;

        /// <summary>
        /// Matériau des gouttes : celui des particules du jeu avec un disque à bord net (le point flou de la poussière
        /// donnait une brume lumineuse, pas des gouttes dessinées).
        /// </summary>
        static Material DropMaterial(Material source)
        {
            if (dropMaterial) return dropMaterial;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "WaterDrop", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((0.92f - d) * N * 0.5f);
                // Reflet clair en haut à gauche, un peu plus sombre en bas : volume en deux tons
                byte v = (byte)(dx * 0.6f + dy * 0.8f > 0.35f && d < 0.6f ? 255 : 228);
                px[y * N + x] = new Color32(v, v, v, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            dropMaterial = new Material(source) { name = "WaterDrop" };
            dropMaterial.SetTexture("_BaseMap", tex);
            return dropMaterial;
        }

        public void Init(VehicleController c, Material material, Func<Vector3, float> level)
        {
            car = c;
            waterLevel = level;
            material = DropMaterial(material);
            drops = MakeSystem("WaterDrops", material, ParticleSystemRenderMode.Billboard, 1.5f, 2000);
            var size = drops.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.55f));
            foam = MakeSystem("WaterFoam", material, ParticleSystemRenderMode.HorizontalBillboard, 0f, 700);
            var grow = foam.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.2f));
            var slow = foam.limitVelocityOverLifetime;
            slow.enabled = true;
            slow.drag = 1.5f;
            slow.multiplyDragByParticleSize = false;
            slow.multiplyDragByParticleVelocity = false;
        }

        ParticleSystem MakeSystem(string name, Material material, ParticleSystemRenderMode mode, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = max;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            // Opaque au départ, s'efface en fin de vie
            var colors = ps.colorOverLifetime;
            colors.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colors.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>Roues du modèle (rayon tiré de leur maillage) ; sans roues, quatre points sous la caisse.</summary>
        void Rebuild()
        {
            model = car.Model;
            wheels.Clear();
            radii.Clear();
            float bottom = float.MaxValue;
            foreach (var t in car.WheelVisuals)
            {
                if (!t) continue;
                float r = 0.3f;
                var mf = t.GetComponentInChildren<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    Vector3 e = Vector3.Scale(mf.sharedMesh.bounds.extents, mf.transform.lossyScale);
                    r = Mathf.Max(0.1f, Mathf.Max(e.y, e.z));
                }
                wheels.Add(t);
                radii.Add(r);
                bottom = Mathf.Min(bottom, car.transform.InverseTransformPoint(t.position).y - r);
            }
            if (wheels.Count == 0) bottom = car.LocalBounds.min.y - 0.25f;
            Clearance = Mathf.Max(0.05f, car.LocalBounds.min.y - bottom);
            sprayAcc = new float[Mathf.Max(4, wheels.Count)];
            foamAcc = new float[sprayAcc.Length];
        }

        static float Rand(float a, float b) => UnityEngine.Random.Range(a, b);

        void Update()
        {
            if (!car || car.Spec == null || waterLevel == null) return;
            if (car.Model != model) Rebuild();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Transform tr = car.transform;
            Vector3 v = car.Velocity, fwd = tr.forward, right = tr.right;
            float speed = v.magnitude;
            float s01 = Mathf.Clamp01((speed - 1f) / 24f);
            float before = Depth, depth = 0f, surface = float.NegativeInfinity;
            int count = wheels.Count > 0 ? wheels.Count : 4;
            for (int i = 0; i < count; i++)
            {
                Vector3 c;
                float r;
                if (wheels.Count > 0) { c = wheels[i].position; r = radii[i]; }
                else
                {
                    var b = car.LocalBounds;
                    c = tr.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, b.min.y, (i & 2) == 0 ? b.min.z : b.max.z));
                    r = 0.25f;
                }
                float level = waterLevel(c);
                float d = level - (c.y - r);
                if (d <= 0.015f || d > 6f) { sprayAcc[i] = foamAcc[i] = 0f; continue; }
                depth = Mathf.Max(depth, d);
                surface = Mathf.Max(surface, level);
                float side = Vector3.Dot(c - tr.position, right) >= 0f ? 1f : -1f;
                float deep = Mathf.Clamp01(d / 0.35f);
                Vector3 contact = new Vector3(c.x, level + 0.02f, c.z);

                // Gerbe de la roue : vers l'extérieur, vers le haut et vers l'arrière, d'autant plus haute que ça va vite
                // (une roue qui patine dans l'eau arrose aussi)
                if (speed > 0.8f || car.WheelSpin > 0.1f)
                {
                    sprayAcc[i] += dt * (10f + 170f * s01 + car.WheelSpin * 70f) * (0.35f + deep);
                    while (sprayAcc[i] >= 1f)
                    {
                        sprayAcc[i] -= 1f;
                        Vector3 vel = v * 0.35f + right * (side * Rand(0.8f, 3.4f) * (0.4f + s01))
                                    + Vector3.up * (Rand(1.8f, 6.5f) * (0.35f + s01 * 0.9f) * (0.6f + deep * 0.6f))
                                    - fwd * (Rand(0f, 3f) * (s01 + car.WheelSpin));
                        Emit(drops, contact + fwd * Rand(-r * 0.7f, r * 0.2f) + right * (side * Rand(0f, 0.15f)), vel,
                             Rand(0.12f, 0.38f) * (0.7f + deep * 0.6f), Rand(0.5f, 0.95f), new Color(0.93f, 0.97f, 1f, 0.95f));
                    }
                }
                // Sillage : écume qui s'étale à la surface, et quelques rides même à l'arrêt
                foamAcc[i] += dt * (1f + 26f * s01) * (0.5f + deep);
                while (foamAcc[i] >= 1f)
                {
                    foamAcc[i] -= 1f;
                    Vector3 vel = v * 0.08f + right * (side * Rand(0.2f, 1.4f) * (0.3f + s01));
                    vel.y = 0f;
                    Emit(foam, contact + new Vector3(Rand(-0.2f, 0.2f), 0.02f, Rand(-0.2f, 0.2f)), vel,
                         Rand(0.45f, 0.9f) * (0.6f + deep * 0.5f), Rand(1.4f, 2.4f), new Color(0.96f, 0.99f, 1f, 0.75f));
                }
            }
            Depth = depth;

            // La caisse fend l'eau : vague d'étrave sur les côtés de l'avant
            if (depth > Clearance * 0.8f && speed > 3f && !float.IsInfinity(surface))
            {
                var b = car.LocalBounds;
                bowAcc += dt * 150f * s01 * Mathf.Clamp01((depth - Clearance * 0.8f) / 0.3f + 0.3f);
                while (bowAcc >= 1f)
                {
                    bowAcc -= 1f;
                    float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                    Vector3 p = tr.TransformPoint(new Vector3(side * b.extents.x * Rand(0.6f, 1f), b.min.y, b.max.z - Rand(0f, 0.5f)));
                    p.y = surface + 0.05f;
                    Vector3 vel = v * 0.55f + right * (side * Rand(2f, 5f) * (0.5f + s01)) + Vector3.up * Rand(1.5f, 4.5f) * (0.4f + s01);
                    Emit(drops, p, vel, Rand(0.2f, 0.55f), Rand(0.5f, 1f), new Color(0.93f, 0.97f, 1f, 0.95f));
                }
            }
            else bowAcc = 0f;

            // Entrée dans l'eau lancée (ou retombée d'un saut) : grande gerbe et cercle d'écume
            float fall = Mathf.Max(0f, -v.y);
            if (before < 0.03f && depth >= 0.03f && (speed > 5f || fall > 2f) && !float.IsInfinity(surface))
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(speed * 3f + fall * 12f), 20, 180);
                for (int k = 0; k < n; k++)
                {
                    float a = Rand(0f, Mathf.PI * 2f);
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = tr.position + fwd * Rand(-1f, 1.5f) + dir * Rand(0.5f, 1.2f);
                    p.y = surface + 0.05f;
                    Vector3 vel = v * 0.3f + dir * Rand(1.5f, 4.5f) + Vector3.up * Rand(2.5f, 7f) * (0.5f + Mathf.Clamp01((speed + fall) / 25f));
                    Emit(drops, p, vel, Rand(0.2f, 0.6f), Rand(0.6f, 1.2f), new Color(0.93f, 0.97f, 1f, 0.95f));
                }
                for (int k = 0; k < 10; k++)
                {
                    float a = k / 10f * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = tr.position + dir * 1.5f;
                    p.y = surface + 0.04f;
                    Emit(foam, p, dir * Rand(1.5f, 3f) + v * 0.1f, Rand(1f, 1.6f), Rand(1.8f, 2.8f), new Color(0.96f, 0.99f, 1f, 0.75f));
                }
            }

            // Sortie de l'eau : la caisse ruisselle quelques secondes
            if (depth > 0.05f) dripTime = DripDuration;
            else if (dripTime > 0f)
            {
                dripTime -= dt;
                var b = car.LocalBounds;
                dripAcc += dt * 45f * Mathf.Clamp01(dripTime / DripDuration);
                while (dripAcc >= 1f)
                {
                    dripAcc -= 1f;
                    Vector3 p = tr.TransformPoint(new Vector3(Rand(b.min.x, b.max.x) * 0.9f, b.min.y + 0.05f, Rand(b.min.z, b.max.z) * 0.9f));
                    Emit(drops, p, v * 0.85f + Vector3.down * Rand(0.2f, 1f), Rand(0.06f, 0.13f), Rand(0.35f, 0.6f), new Color(0.85f, 0.92f, 1f, 0.9f));
                }
            }
            else dripAcc = 0f;
        }

        static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = Rand(0f, 360f),
            };
            ps.Emit(p, 1);
        }
    }
}
