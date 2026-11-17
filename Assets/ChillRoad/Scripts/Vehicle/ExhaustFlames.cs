using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Retours de flamme aux sorties d'échappement d'un modèle (repères « exhaust-* », +Z local dans le sens du flux),
    /// selon le pop and bang et la couleur choisis au garage (<see cref="CarCosmetics"/>) : petit (d'origine), conséquent
    /// (grosses flammes et rafale de pétarades), gazole (énormes flammes qui durent, boules de feu, fumée noire), ou rien.
    /// Rendu cartoon, en couches :
    /// - dard : cœur chaud presque blanc, additif, étiré dans le sens du jet ;
    /// - langues : flamme de la couleur choisie, opaque (lisible de jour comme de nuit), en aplats (texture à deux tons),
    ///   étirées, qui refroidissent (plus sombres) avant de disparaître ;
    /// - boules de feu (conséquent, gazole) : flammes lobées qui gonflent, tournent et montent, puis virent au sombre ;
    /// - escarbilles (conséquent, gazole) et fumée noire (gazole).
    /// Les flammes vivent dans le repère de la voiture (elles restent au pot quand elle roule) ; la fumée, dans le monde.
    /// Utilisé par la voiture (<see cref="VehicleEffects"/>) et par l'atelier du garage (<see cref="WorkshopStage"/>).
    /// </summary>
    public class ExhaustFlames
    {
        struct Profile
        {
            public int core, tongues, balls, embers, smoke, crackleMin, crackleMax;
            public float sizeMin, sizeMax, speedMin, speedMax, lifeMin, lifeMax;
            public float ballSize, ballLife, flash, flashPower, flashRange, loud;
        }

        static readonly Profile Small = new Profile
        {
            core = 3, tongues = 7, sizeMin = 0.12f, sizeMax = 0.23f, speedMin = 4f, speedMax = 8f, lifeMin = 0.07f, lifeMax = 0.14f,
            flash = 0.08f, flashPower = 2.5f, flashRange = 4f, loud = 1f,
        };
        static readonly Profile Big = new Profile
        {
            core = 5, tongues = 11, balls = 2, embers = 6, sizeMin = 0.17f, sizeMax = 0.34f, speedMin = 6f, speedMax = 12f,
            lifeMin = 0.09f, lifeMax = 0.18f, ballSize = 0.45f, ballLife = 0.3f, crackleMin = 2, crackleMax = 4,
            flash = 0.12f, flashPower = 3.5f, flashRange = 5.5f, loud = 1.35f,
        };
        static readonly Profile Diesel = new Profile
        {
            core = 7, tongues = 18, balls = 6, embers = 12, smoke = 6, sizeMin = 0.26f, sizeMax = 0.55f, speedMin = 7f, speedMax = 14f,
            lifeMin = 0.18f, lifeMax = 0.34f, ballSize = 0.8f, ballLife = 0.75f, crackleMin = 3, crackleMax = 5,
            flash = 0.3f, flashPower = 4.5f, flashRange = 7f, loud = 1.6f,
        };

        static Profile Of(int pop) => pop == CarCosmetics.PopDiesel ? Diesel : pop == CarCosmetics.PopBig ? Big : Small;

        // Matériaux et textures partagés (copies des matériaux de particules du jeu)
        static Material coreMaterial, tongueMaterial, ballMaterial, emberMaterial;
        static Texture2D blobTexture, ballTexture;

        readonly ParticleSystem core, tongues, balls, embers, smoke;
        readonly Light flash;
        float flashTimer, flashLength = 0.08f, flashPower = 2.5f;
        IReadOnlyList<Transform> pipes;
        Transform car;
        int crackles;
        float crackleTimer;

        /// <summary>Accessoires de la voiture : pop and bang, couleur des flammes.</summary>
        public CarCosmetics.Look Look = CarCosmetics.Look.Stock;
        /// <summary>Vitesse de la voiture (la fumée part avec elle).</summary>
        public Vector3 Velocity;
        /// <summary>Une détonation (pour le son) : volume relatif (1 = petit pop and bang).</summary>
        public event System.Action<float> Popped;

        public ExhaustFlames(Transform parent, GameAssets a, int layer)
        {
            Materials(a);
            core = System(parent, "FlameCore", coreMaterial, 300, layer, true, ParticleSystemRenderMode.Stretch);
            Stretch(core, 0.03f, 1.6f);
            SizeCurve(core, new Keyframe(0f, 1f), new Keyframe(1f, 0.3f));
            Colors(core, new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });

            tongues = System(parent, "FlameTongues", tongueMaterial, 600, layer, true, ParticleSystemRenderMode.Stretch);
            Stretch(tongues, 0.045f, 1.4f);
            SizeCurve(tongues, new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.35f));
            // Flamme qui refroidit : sa couleur, puis plus sombre, avant de s'effacer
            Colors(tongues, new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0.45f), new GradientColorKey(new Color(0.42f, 0.3f, 0.3f), 1f) },
                            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.95f, 0.6f), new GradientAlphaKey(0f, 1f) });

            balls = System(parent, "Fireballs", ballMaterial, 200, layer, true, ParticleSystemRenderMode.Billboard);
            var bm = balls.main;
            bm.gravityModifier = -0.12f;
            SizeCurve(balls, new Keyframe(0f, 0.45f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.25f));
            Colors(balls, new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0.35f), new GradientColorKey(new Color(0.55f, 0.38f, 0.34f), 0.7f), new GradientColorKey(new Color(0.18f, 0.16f, 0.16f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.95f, 0.55f), new GradientAlphaKey(0f, 1f) });
            var spin = balls.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            var slow = balls.limitVelocityOverLifetime;
            slow.enabled = true;
            slow.drag = 3f;

            embers = System(parent, "Embers", emberMaterial, 300, layer, false, ParticleSystemRenderMode.Stretch);
            Stretch(embers, 0.02f, 1f);
            var em = embers.main;
            em.gravityModifier = 0.6f;
            Colors(embers, new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });

            smoke = System(parent, "DieselSmoke", a.particle, 200, layer, false, ParticleSystemRenderMode.Billboard);
            var sm = smoke.main;
            sm.gravityModifier = -0.05f;
            SizeCurve(smoke, new Keyframe(0f, 0.6f), new Keyframe(1f, 2.4f));
            Colors(smoke, new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.1f), new GradientAlphaKey(0f, 1f) });
            var drag = smoke.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.5f;

            var flashGo = new GameObject("BackfireLight") { layer = layer };
            flashGo.transform.SetParent(parent, false);
            flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.55f, 0.2f);
            flash.range = 4f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        // ------------------------------------------------------------------ construction

        static void Materials(GameAssets a)
        {
            if (coreMaterial) return;
            // Dard : additif, assez chaud pour un léger halo (sans boule de lumière qui mange la voiture)
            coreMaterial = Copy(a.spark, "FlameCore", BlobTexture, new Color(2.2f, 2.2f, 2.2f, 1f));
            emberMaterial = Copy(a.spark, "FlameEmber", null, new Color(2.5f, 2.5f, 2.5f, 1f));
            // Langues et boules de feu : transparentes non additives (aplats lisibles sur le goudron clair), un peu
            // au-delà de 1 pour que le bloom les fasse luire
            tongueMaterial = Copy(a.particle, "FlameTongue", BlobTexture, new Color(1.35f, 1.35f, 1.35f, 1f));
            ballMaterial = Copy(a.particle, "Fireball", BallTexture, new Color(1.25f, 1.25f, 1.25f, 1f));
        }

        static Material Copy(Material src, string name, Texture2D tex, Color baseColor)
        {
            var m = new Material(src) { name = name + " (runtime)" };
            if (m.HasProperty("_BaseMap") && tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", baseColor);
            return m;
        }

        static ParticleSystem System(Transform parent, string name, Material material, int max, int layer, bool local, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            // Flammes : repère de la voiture (Bind), elles restent au pot ; le reste dans le monde
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Custom : ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = 0f;
            main.playOnAwake = false;
            main.loop = true;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = mode;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = mode == ParticleSystemRenderMode.Stretch ? -1f : 0f;
            ps.Play();
            return ps;
        }

        static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = velocityScale;
            r.lengthScale = lengthScale;
        }

        static void SizeCurve(ParticleSystem ps, params Keyframe[] keys)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        static void Colors(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(colors, alphas);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = g;
        }

        /// <summary>Tache de flamme en aplats : cœur clair, bord plus foncé, liseré net (style cartoon).</summary>
        static Texture2D BlobTexture
        {
            get
            {
                if (blobTexture) return blobTexture;
                blobTexture = Make("FlameBlob", 64, (u, v) =>
                {
                    float d = Mathf.Sqrt(u * u + v * v);
                    return Toon(d, 1f);
                });
                return blobTexture;
            }
        }

        /// <summary>Boule de feu lobée (cinq lobes inégaux), en aplats comme la tache.</summary>
        static Texture2D BallTexture
        {
            get
            {
                if (ballTexture) return ballTexture;
                ballTexture = Make("FlameBall", 96, (u, v) =>
                {
                    float d = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Atan2(v, u);
                    float edge = 0.82f + 0.11f * Mathf.Sin(a * 5f + 0.7f) + 0.05f * Mathf.Sin(a * 3f + 2.1f);
                    return Toon(d, edge);
                });
                return ballTexture;
            }
        }

        /// <summary>Trois tons selon la distance au centre (rapportée au bord) : cœur, flamme, liseré ; bord net.</summary>
        static Color Toon(float d, float edge)
        {
            float x = d / Mathf.Max(edge, 1e-3f);
            float alpha = 1f - Smooth(0.88f, 1f, x);
            // Aplats à transitions à peine adoucies (pas de crénelage)
            float tone = Mathf.Lerp(1f, 0.78f, Smooth(0.38f, 0.44f, x));
            tone = Mathf.Lerp(tone, 0.6f, Smooth(0.72f, 0.78f, x));
            return new Color(tone, tone, tone, alpha);
        }

        /// <summary>Smoothstep (0 avant a, 1 après b) ; Mathf.SmoothStep interpole entre deux valeurs, ce n'est pas le même.</summary>
        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static Texture2D Make(string name, int n, System.Func<float, float, Color> shade)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = shade((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ utilisation

        /// <summary>Sorties d'échappement et voiture à suivre (repère des flammes).</summary>
        public void Bind(IReadOnlyList<Transform> exhausts, Transform carTransform)
        {
            pipes = exhausts;
            if (car == carTransform) return;
            car = carTransform;
            foreach (var ps in new[] { core, tongues, balls })
            {
                var main = ps.main;
                main.customSimulationSpace = car;
            }
        }

        /// <summary>Le pop and bang est actif et la voiture a un pot.</summary>
        public bool Active => Look.pop != CarCosmetics.PopOff && pipes != null && pipes.Count > 0 && car;

        /// <summary>Retour de flamme (lever de pied, passage de rapport) ; conséquent et gazole enchaînent une rafale de pétarades.</summary>
        public bool Fire(float strength)
        {
            if (!Active) return false;
            var p = Of(Look.pop);
            Emit(p, strength, true);
            if (p.crackleMax > 0)
            {
                crackles = Random.Range(p.crackleMin, p.crackleMax + 1);
                crackleTimer = Random.Range(0.07f, 0.15f);
            }
            return true;
        }

        public void Tick(float dt)
        {
            if (crackles > 0)
            {
                crackleTimer -= dt;
                if (crackleTimer <= 0f)
                {
                    crackles--;
                    crackleTimer = Random.Range(0.06f, 0.17f);
                    if (Active) Emit(Of(Look.pop), Random.Range(0.5f, 0.8f), false);
                    else crackles = 0;
                }
            }
            if (flashTimer > 0f)
            {
                flashTimer -= dt;
                flash.enabled = flashTimer > 0f;
                // Éclair bref puis décroissance douce
                float k = Mathf.Max(0f, flashTimer / flashLength);
                flash.intensity = k * k * flashPower;
            }
        }

        void Emit(Profile p, float strength, bool main)
        {
            Color color = CarCosmetics.FlameColor(Look.flame);
            // Cœur : la couleur blanchie (orange → jaune paille, bleu → bleu glacier)
            Color hot = Color.Lerp(color, Color.white, Look.flame == 0 ? 0.55f : 0.65f);
            Color hotOrange = Look.flame == 0 ? new Color(1f, 0.78f, 0.35f) : hot;
            float scale = strength * (main ? 1f : 0.7f);
            Vector3 center = Vector3.zero;
            int n = 0;
            foreach (var pipe in pipes)
            {
                if (!pipe) continue;
                // Sens du flux : +Z du repère, sinon droit vers l'arrière s'il est mal orienté
                Vector3 dir = pipe.forward;
                if (Vector3.Dot(dir, -car.forward) < 0.3f) dir = -car.forward;
                Vector3 mouth = pipe.position;
                // Repère de la voiture pour les flammes
                Vector3 lm = car.InverseTransformPoint(mouth), ld = car.InverseTransformDirection(dir);

                int count = Mathf.Max(1, Mathf.RoundToInt(p.core * (main ? 1f : 0.6f)));
                for (int k = 0; k < count; k++)
                    core.Emit(new ParticleSystem.EmitParams
                    {
                        position = lm + ld * Random.Range(0f, 0.05f),
                        velocity = (ld + Random.insideUnitSphere * 0.08f) * Random.Range(p.speedMin, p.speedMax) * 0.8f,
                        startSize = Random.Range(p.sizeMin, p.sizeMax) * 0.55f * scale,
                        startLifetime = Random.Range(p.lifeMin, p.lifeMax) * 0.7f,
                        startColor = hotOrange,
                    }, 1);

                count = Mathf.Max(2, Mathf.RoundToInt(p.tongues * (main ? 1f : 0.6f)));
                for (int k = 0; k < count; k++)
                {
                    float spread = Random.value;
                    tongues.Emit(new ParticleSystem.EmitParams
                    {
                        position = lm + ld * Random.Range(0.02f, 0.12f),
                        velocity = (ld + Random.insideUnitSphere * (0.12f + 0.2f * spread)) * Random.Range(p.speedMin, p.speedMax),
                        startSize = Random.Range(p.sizeMin, p.sizeMax) * scale,
                        startLifetime = Random.Range(p.lifeMin, p.lifeMax) * (main ? 1f : 0.75f),
                        startColor = Color.Lerp(color, hot, Random.value * 0.3f),
                    }, 1);
                }

                count = main ? p.balls : p.balls / 2;
                for (int k = 0; k < count; k++)
                    balls.Emit(new ParticleSystem.EmitParams
                    {
                        position = lm + ld * Random.Range(0.25f, 0.9f) * (1f + p.ballSize) + Random.insideUnitSphere * 0.1f,
                        velocity = (ld + Random.insideUnitSphere * 0.35f) * Random.Range(2f, 5f),
                        startSize = p.ballSize * Random.Range(0.75f, 1.25f) * scale,
                        startLifetime = p.ballLife * Random.Range(0.75f, 1.25f),
                        startColor = Color.Lerp(color, hot, Random.value * 0.25f),
                        rotation = Random.Range(0f, 360f),
                    }, 1);

                count = main ? p.embers : p.embers / 3;
                for (int k = 0; k < count; k++)
                    embers.Emit(new ParticleSystem.EmitParams
                    {
                        position = mouth + dir * Random.Range(0.05f, 0.3f),
                        velocity = Velocity + (dir + Random.insideUnitSphere * 0.5f + Vector3.up * 0.2f) * Random.Range(3f, 9f),
                        startSize = Random.Range(0.025f, 0.05f),
                        startLifetime = Random.Range(0.25f, 0.6f),
                        startColor = hotOrange,
                    }, 1);

                for (int k = 0; k < (main ? p.smoke : p.smoke / 3); k++)
                {
                    float g = Random.Range(0.06f, 0.16f);
                    smoke.Emit(new ParticleSystem.EmitParams
                    {
                        position = mouth + dir * Random.Range(0.3f, 0.8f),
                        velocity = Velocity * 0.6f + dir * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(0.3f, 1f) + Random.insideUnitSphere * 0.5f,
                        startSize = Random.Range(0.6f, 1.2f),
                        startLifetime = Random.Range(1f, 1.8f),
                        startColor = new Color(g, g, g * 1.05f),
                        rotation = Random.Range(0f, 360f),
                    }, 1);
                }
                center += mouth + dir * 0.4f;
                n++;
            }
            if (n == 0) return;
            flash.transform.position = center / n;
            flash.color = CarCosmetics.FlameLight(Look.flame);
            flash.range = p.flashRange;
            flash.enabled = true;
            flashLength = main ? p.flash : p.flash * 0.6f;
            flashPower = p.flashPower * (main ? 1f : 0.6f);
            flashTimer = flashLength;
            Popped?.Invoke(p.loud * (main ? 1f : 0.75f));
        }
    }
}
