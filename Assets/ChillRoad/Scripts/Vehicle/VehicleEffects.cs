using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Effets de la voiture du joueur : fumée et traces de pneus en glisse, étincelles contre les obstacles,
    /// débris aux chocs, retours de flamme à l'échappement (passage de rapport, lever de pied à haut régime ;
    /// <see cref="ExhaustFlames"/>, selon le pop and bang choisi au garage).
    /// </summary>
    public class VehicleEffects : MonoBehaviour
    {
        VehicleController car;
        GameAssets assets;
        ParticleSystem smoke, sparks, debris;
        SkidMarks skids;
        ExhaustFlames exhaust;
        float lastThrottle, backfireCooldown;
        readonly float[] smokeAccumulator = new float[4];
        float scrapeAccumulator;
        Transform fxRoot;

        /// <summary>Retour de flamme (pour le son) : volume relatif (1 = petit pop and bang d'origine).</summary>
        public event System.Action<float> Backfire;

        /// <summary>
        /// Accessoires imposés (voiture d'un autre joueur, d'après son état réseau) ; nul : ceux montés au garage sur
        /// la voiture conduite (<see cref="CarCosmetics.Fitted(VehicleSpec)"/>).
        /// </summary>
        public CarCosmetics.Look? Override;

        CarCosmetics.Look Look => Override ?? CarCosmetics.Fitted(car.Spec);

        public void Init(VehicleController c, GameAssets a)
        {
            car = c;
            assets = a;
            var root = new GameObject("PlayerFx").transform;
            fxRoot = root;
            smoke = MakeSystem(root, "TireSmoke", a.particle, 900, 0f, ParticleSystemRenderMode.Billboard);
            var smokeSize = smoke.sizeOverLifetime;
            smokeSize.enabled = true;
            smokeSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.6f));
            var smokeFade = smoke.colorOverLifetime;
            smokeFade.enabled = true;
            smokeFade.color = Fade(0.55f, 0.08f);
            var smokeDrag = smoke.limitVelocityOverLifetime;
            smokeDrag.enabled = true;
            smokeDrag.drag = 1.2f;

            sparks = MakeSystem(root, "Sparks", a.spark, 600, 1.2f, ParticleSystemRenderMode.Stretch);
            var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.velocityScale = 0.018f;
            sparkRenderer.lengthScale = 1f;
            var sparkFade = sparks.colorOverLifetime;
            sparkFade.enabled = true;
            sparkFade.color = Fade(1f, 0f);

            exhaust = new ExhaustFlames(root, a, 0);
            exhaust.Popped += loud => Backfire?.Invoke(loud);

            debris = MakeSystem(root, "Debris", a.metal, 150, 1f, ParticleSystemRenderMode.Mesh);
            var debrisRenderer = debris.GetComponent<ParticleSystemRenderer>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debrisRenderer.mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);
            var main = debris.main;
            main.startRotation3D = true;
            var collision = debris.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.bounce = 0.3f;
            collision.dampen = 0.4f;
            collision.lifetimeLoss = 0f;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            var spin = debris.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            if (a.skidMark) skids = new SkidMarks(a.skidMark);
            car.Impacted += OnImpact;
            car.Scraped += OnScrape;
            car.GearChanged += OnGear;
        }

        /// <summary>Les effets vivent hors de la voiture (repère du monde) : ils partent avec elle (joueur en réseau qui quitte la partie).</summary>
        void OnDestroy()
        {
            if (fxRoot) Destroy(fxRoot.gameObject);
        }

        static ParticleSystem MakeSystem(Transform parent, string name, Material material, int max, float gravity, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.playOnAwake = false;
            main.loop = true;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = mode;
            r.shadowCastingMode = mode == ParticleSystemRenderMode.Mesh ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static ParticleSystem.MinMaxGradient Fade(float peak, float fadeIn)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      fadeIn > 0f
                        ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, fadeIn), new GradientAlphaKey(0f, 1f) }
                        : new[] { new GradientAlphaKey(peak, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        void Update()
        {
            if (!car || car.Spec == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 v = car.Velocity;
            bool asphalt = car.OnRoad && car.SurfaceDirt < 0.5f;

            // Fumée et traces de pneus (4 pistes de traces : une par roue, 3 pour un tricycle)
            for (int i = 0; i < 4; i++)
            {
                if (i >= car.WheelCount || !car.WheelContact(i, out Vector3 p, out Vector3 n, out float skid))
                {
                    skids?.End(i);
                    continue;
                }
                if (asphalt && skid > 0.3f) skids?.Add(i, p, n, Mathf.InverseLerp(0.3f, 1f, skid) * 0.9f + 0.1f);
                else skids?.End(i);
                if (!asphalt || skid < 0.35f || car.Speed < 3f) continue;
                smokeAccumulator[i] += (skid - 0.3f) * 55f * dt;
                while (smokeAccumulator[i] >= 1f)
                {
                    smokeAccumulator[i] -= 1f;
                    var e = new ParticleSystem.EmitParams
                    {
                        position = p + Vector3.up * 0.25f + Random.insideUnitSphere * 0.3f,
                        velocity = v * 0.25f + Vector3.up * Random.Range(0.3f, 1.2f) + Random.insideUnitSphere * 0.8f,
                        startSize = Random.Range(1.2f, 2.2f),
                        startLifetime = Random.Range(1.6f, 3f),
                        startColor = new Color(0.88f, 0.88f, 0.9f),
                        rotation = Random.Range(0f, 360f),
                    };
                    smoke.Emit(e, 1);
                }
            }
            skids?.Flush();

            // Retour de flamme au lever de pied à haut régime (plus tôt avec un pop and bang conséquent ou au gazole)
            exhaust.Look = Look;
            exhaust.Velocity = v;
            exhaust.Bind(car.Exhausts, car.transform);
            backfireCooldown -= dt;
            float rpm = exhaust.Look.pop >= CarCosmetics.PopBig ? 0.55f : 0.65f;
            if (lastThrottle > 0.8f && car.Throttle < 0.3f && car.Rpm01 > rpm) TriggerBackfire(0.8f);
            lastThrottle = car.Throttle;
            exhaust.Tick(dt);
        }

        void OnGear(int gear)
        {
            if (gear > 1 && car.Throttle > 0.6f) TriggerBackfire(1f);
        }

        /// <summary>
        /// Retour de flamme à chaque sortie d'échappement du modèle (repères « exhaust-* »), selon le pop and bang monté.
        /// Une voiture sans pot (Vision 74 à hydrogène) ne pétarade pas.
        /// </summary>
        void TriggerBackfire(float strength)
        {
            if (backfireCooldown > 0f) return;
            exhaust.Look = Look;
            exhaust.Velocity = car.Velocity;
            exhaust.Bind(car.Exhausts, car.transform);
            if (exhaust.Fire(strength)) backfireCooldown = 0.25f;
        }

        void OnImpact(Collision c, float impact)
        {
            if (c.contactCount == 0) return;
            var contact = c.GetContact(0);
            EmitSparks(contact.point, contact.normal, Mathf.RoundToInt(Mathf.Clamp(impact * 3f, 8f, 70f)), impact);
            // Débris aux chocs violents (morceaux de carrosserie)
            if (impact > 8f)
            {
                int n = Mathf.RoundToInt(Mathf.Clamp(impact, 8f, 30f));
                for (int k = 0; k < n; k++)
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = contact.point + Random.insideUnitSphere * 0.4f,
                        velocity = car.Velocity * 0.5f + (contact.normal + Random.insideUnitSphere) * Random.Range(2f, 7f) + Vector3.up * 3f,
                        startSize = Random.Range(0.05f, 0.2f),
                        startLifetime = Random.Range(3f, 5f),
                        startColor = Random.value < 0.3f ? new Color(0.75f, 0.85f, 0.9f) : new Color(0.3f, 0.3f, 0.32f),
                        rotation3D = Random.insideUnitSphere * 180f,
                    }, 1);
            }
        }

        void OnScrape(Vector3 point, Vector3 normal, float slide)
        {
            scrapeAccumulator += slide * 0.7f * Time.fixedDeltaTime;
            int n = Mathf.FloorToInt(scrapeAccumulator);
            if (n <= 0) return;
            scrapeAccumulator -= n;
            EmitSparks(point, normal, n, slide);
        }

        void EmitSparks(Vector3 point, Vector3 normal, int count, float speed)
        {
            Vector3 v = car.Velocity;
            for (int k = 0; k < count; k++)
            {
                Vector3 dir = (normal * 0.6f + Random.insideUnitSphere + Vector3.up * 0.4f).normalized;
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = point,
                    velocity = v * Random.Range(0.2f, 0.6f) + dir * Random.Range(2f, 5f + speed * 0.1f),
                    startSize = Random.Range(0.04f, 0.09f),
                    startLifetime = Random.Range(0.25f, 0.7f),
                    startColor = Color.Lerp(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.45f, 0.1f), Random.value),
                }, 1);
            }
        }
    }
}
