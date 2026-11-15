using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Nuage de poussière soulevé par la voiture sur les chemins de terre (et un peu hors route) ; sur route mouillée,
    /// gerbe d'eau pulvérisée par les roues (la pluie colle la poussière).
    /// </summary>
    public class VehicleDust : MonoBehaviour
    {
        VehicleController car;
        ParticleSystem ps;
        ParticleSystem.EmissionModule emission;
        ParticleSystem.MainModule main;

        public void Init(VehicleController c, Material material)
        {
            car = c;
            var go = new GameObject("Dust");
            go.transform.SetParent(c.transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.8f, 3.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.gravityModifier = -0.02f;
            main.playOnAwake = false;

            emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.8f, 0.2f, 0.6f);

            // Le nuage suit d'abord la voiture puis ralentit et reste en suspension : visible depuis la caméra
            var inherit = ps.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = 0.75f;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.9f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;

            var colors = ps.colorOverLifetime;
            colors.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.1f), new GradientAlphaKey(0f, 1f) });
            colors.color = g;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        void Update()
        {
            if (!car || car.Spec == null) return;
            // Placée derrière la voiture, au ras du sol
            ps.transform.localPosition = new Vector3(0f, car.LocalBounds.min.y + 0.35f, car.LocalBounds.min.z + 0.2f);
            float speed01 = Mathf.Clamp01((car.Speed - 3f) / 22f);
            float dirt = car.OnRoad ? car.SurfaceDirt : 0.35f;
            float grounded = car.GroundedWheels > 0 ? 1f : 0f;
            float dust = grounded * dirt * (1f - car.Wetness * 0.8f) * (speed01 * 70f + car.SkidAmount * 50f);
            float spray = grounded * car.Wetness * (car.OnRoad ? 1f - car.SurfaceDirt * 0.6f : 0.3f) * (Mathf.Clamp01((car.Speed - 8f) / 30f) * 80f + car.SkidAmount * 40f);
            emission.rateOverTime = dust + spray;
            Color dustColor = car.OnRoad ? new Color(0.62f, 0.54f, 0.42f) : new Color(0.5f, 0.47f, 0.36f);
            main.startColor = Color.Lerp(dustColor, new Color(0.8f, 0.83f, 0.87f, 0.5f), spray / Mathf.Max(1f, dust + spray));
        }
    }
}
