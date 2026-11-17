using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Effets ponctuels partagés : confettis, éclairs lumineux (radar, portes),
    /// traînées d'air au frôlement d'un véhicule.
    /// </summary>
    public class FxBursts : MonoBehaviour
    {
        public static FxBursts Instance { get; private set; }

        ParticleSystem confetti, streaks;
        readonly Light[] flashes = new Light[4];
        readonly float[] flashTime = new float[4];
        readonly float[] flashDuration = new float[4];
        readonly float[] flashPeak = new float[4];
        int nextFlash;

        public void Init(GameAssets assets)
        {
            Instance = this;
            if (!assets.spark) return;
            // Matériau additif blanc : la couleur de chaque particule donne la teinte
            var white = new Material(assets.spark) { name = "FxWhite (runtime)" };
            white.SetColor("_BaseColor", new Color(2.2f, 2.2f, 2.2f, 1f));

            confetti = System("Confetti", white, 600, 0.35f, ParticleSystemRenderMode.Billboard);
            var spin = confetti.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var drag = confetti.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.5f;
            Fade(confetti, 1f);

            streaks = System("AirStreaks", white, 300, 0f, ParticleSystemRenderMode.Stretch);
            var sr = streaks.GetComponent<ParticleSystemRenderer>();
            sr.velocityScale = 0.06f;
            sr.lengthScale = 1f;
            Fade(streaks, 0.6f);

            for (int i = 0; i < flashes.Length; i++)
            {
                var go = new GameObject("FlashLight");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.enabled = false;
                flashes[i] = l;
            }
        }

        ParticleSystem System(string name, Material material, int max, float gravity, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = mode;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static void Fade(ParticleSystem ps, float peak)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(peak, 0f), new GradientAlphaKey(peak, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        /// <summary>Pluie de confettis multicolores (victoire).</summary>
        public void Confetti(Vector3 pos, int count = 120, Vector3 carry = default)
        {
            if (!confetti) return;
            Color[] palette = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.8f, 0.2f), new Color(0.3f, 0.9f, 0.4f), new Color(0.3f, 0.6f, 1f), new Color(0.9f, 0.4f, 1f) };
            for (int i = 0; i < count; i++)
                confetti.Emit(new ParticleSystem.EmitParams
                {
                    position = pos + Random.insideUnitSphere * 2f,
                    velocity = carry + new Vector3(Random.Range(-6f, 6f), Random.Range(4f, 11f), Random.Range(-6f, 6f)),
                    startSize = Random.Range(0.12f, 0.25f),
                    startLifetime = Random.Range(1.5f, 2.8f),
                    startColor = palette[Random.Range(0, palette.Length)],
                    rotation = Random.Range(0f, 360f),
                }, 1);
        }

        /// <summary>Traînées d'air entre la voiture et le véhicule frôlé.</summary>
        public void AirStreaks(Vector3 pos, Vector3 carVelocity, int count = 16)
        {
            if (!streaks) return;
            for (int i = 0; i < count; i++)
                streaks.Emit(new ParticleSystem.EmitParams
                {
                    position = pos + Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(0.2f, 1.2f),
                    velocity = carVelocity * Random.Range(0.4f, 0.75f),
                    startSize = Random.Range(0.03f, 0.06f),
                    startLifetime = Random.Range(0.25f, 0.45f),
                    startColor = new Color(0.75f, 0.9f, 1f),
                }, 1);
        }

        /// <summary>Éclair lumineux bref (flash de radar, porte franchie).</summary>
        public void Flash(Vector3 pos, Color color, float intensity, float range, float duration)
        {
            int i = nextFlash;
            nextFlash = (nextFlash + 1) % flashes.Length;
            var l = flashes[i];
            if (!l) return;
            l.transform.position = pos;
            l.color = color;
            l.range = range;
            flashTime[i] = duration;
            flashDuration[i] = duration;
            flashPeak[i] = intensity;
            l.enabled = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < flashes.Length; i++)
            {
                if (!flashes[i] || flashTime[i] <= 0f) continue;
                flashTime[i] -= dt;
                float k = Mathf.Clamp01(flashTime[i] / Mathf.Max(0.01f, flashDuration[i]));
                flashes[i].intensity = flashPeak[i] * k * k;
                if (flashTime[i] <= 0f) flashes[i].enabled = false;
            }
        }
    }
}
