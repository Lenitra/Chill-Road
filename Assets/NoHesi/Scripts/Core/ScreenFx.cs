using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NoHesi
{
    /// <summary>
    /// Effets d'écran liés à la vitesse : traînées de vent autour de la caméra au-delà de ~180 km/h,
    /// aberration chromatique et légère distorsion qui montent avec la vitesse, impulsions aux frôlements et aux chocs.
    /// </summary>
    public class ScreenFx : MonoBehaviour
    {
        VehicleController car;
        ChromaticAberration chroma;
        LensDistortion lens;
        ParticleSystem streaks;
        ParticleSystem.EmissionModule streakEmission;
        float pulse;

        public void Init(VehicleController c, ScoreSystem score, Camera cam, Material streakMaterial)
        {
            car = c;
            var volume = FindAnyObjectByType<Volume>();
            if (volume)
            {
                var profile = volume.profile;
                if (!profile.TryGet(out chroma)) chroma = profile.Add<ChromaticAberration>(true);
                if (!profile.TryGet(out lens)) lens = profile.Add<LensDistortion>(true);
                chroma.intensity.overrideState = true;
                lens.intensity.overrideState = true;
            }
            score.NearMiss += (pts, gap) => pulse = Mathf.Max(pulse, gap < 0.6f ? 0.8f : 0.5f);
            car.Impacted += (col, impact) => { if (impact > 6f) pulse = 1f; };
            if (cam && streakMaterial) streaks = MakeStreaks(cam.transform, streakMaterial);
        }

        /// <summary>Impulsion d'aberration chromatique et de distorsion (transition de départ, « GO »).</summary>
        public void Punch(float amount) => pulse = Mathf.Max(pulse, amount);

        ParticleSystem MakeStreaks(Transform cam, Material material)
        {
            var go = new GameObject("SpeedStreaks");
            go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, 45f);
            go.transform.localRotation = Quaternion.identity;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 0.55f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new Color(1f, 1f, 1f, 0.35f);
            main.maxParticles = 400;
            main.playOnAwake = false;
            // Anneau autour de l'axe de visée : les traînées passent sur les bords de l'écran
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 11f;
            shape.radiusThickness = 0.45f;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-95f, -80f);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            streakEmission = ps.emission;
            streakEmission.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.07f;
            r.lengthScale = 1f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        void Update()
        {
            if (!car) return;
            float dt = Time.unscaledDeltaTime;
            float speed01 = Mathf.Clamp01((car.Speed - 45f) / 35f); // 160 → 290 km/h
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 2.2f);
            bool paused = Time.timeScale < 0.01f;
            if (chroma) chroma.intensity.value = paused ? 0f : Mathf.Clamp01(speed01 * 0.35f + pulse * 0.6f);
            if (lens) lens.intensity.value = paused ? 0f : -(speed01 * 0.12f + pulse * 0.18f);
            if (streaks) streakEmission.rateOverTime = paused ? 0f : speed01 * speed01 * 160f;
        }
    }
}
