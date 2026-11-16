using System.Collections.Generic;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Trafic d'ambiance sur le réseau de la carte : véhicules cinématiques qui suivent leur voie (à droite), ralentissent
    /// dans les virages, gardent leurs distances, s'arrêtent derrière un obstacle, et choisissent une route à chaque
    /// carrefour (un seul véhicule engagé à la fois). Sur l'autoroute, ils entrent et sortent par les bretelles à sens
    /// unique (voies d'insertion et de décélération), sans s'arrêter. Ils apparaissent hors de vue autour du joueur
    /// et disparaissent au loin. Percutés, ils deviennent physiques.
    /// </summary>
    public class TrafficSystem : MonoBehaviour
    {
        public class Car
        {
            public GameObject go;
            public Rigidbody rb;
            public Vector3 cachedPos, cachedFwd;  // position et cap relus une fois par pas physique (optimisation)
            public RoadNetwork.Edge edge;
            public float s;
            public int direction;           // +1 : de a vers b, -1 : de b vers a
            public float speed, cruise;
            public float length, halfWidth;
            public bool truck;
            public bool farm;               // engin agricole (tracteur) : très lent
            public bool crashed;
            public float crashTimer;
            public GameObject lights;
            public TailLights.Slot[] tails;  // optiques arrière
            public TailLights.Slot[] heads;  // phares
            public float tailGlow = -1f, headGlow = -1f;
            public Vector3 velocity;
            // Traversée d'un carrefour : courbe entre la voie quittée et la voie prise
            public RoadNetwork.Node node;
            public RoadNetwork.Edge nextEdge;
            public int nextDirection;
            public float nextS, crossPos, crossLength;
            public Vector3 c0, c1, c2, c3;
            public bool crossing;
            public float stuck;
            // Autoroute : voie (0 = voie de gauche, le long du terre-plein), voie prise après le carrefour,
            // écart latéral restant pendant un changement de voie
            public int lane, nextLane;
            public float laneShift;
            // Allure propre au véhicule (part de la vitesse de la route), reprise à chaque changement de route
            public float pace;
            // Échangeur : bretelle de sortie choisie, nœud pour lequel le choix est fait, distance au nœud où commence
            // le déport vers la voie de décélération, écart latéral à atteindre
            public RoadNetwork.Edge exitRamp;
            public RoadNetwork.Node exitChecked;
            public float exitFrom, exitShift;
        }

        public readonly List<Car> Cars = new List<Car>();
        public float density = 1f;
        [Tooltip("Nombre de véhicules autour du joueur pour une densité de 1")] public int baseCount = 26;
        public float spawnMin = 260f, spawnMax = 800f, despawnDistance = 980f;
        /// <summary>Voitures pilotées (celle du joueur) : le trafic freine derrière elles.</summary>
        public readonly List<VehicleController> Drivers = new List<VehicleController>();
        /// <summary>Allumage des feux (0 = jour, 1 = nuit), fourni par le cycle jour/nuit.</summary>
        public float LightsLevel { get; set; }
        /// <summary>Densité selon l'heure (fournie par le cycle jour/nuit) : faible la nuit, forte aux heures de pointe.</summary>
        public float TimeDensity { get; set; } = 1f;
        /// <summary>Vitesse relative du trafic (pluie : on roule moins vite et on laisse plus de distance).</summary>
        public float WeatherSpeed { get; set; } = 1f;

        GameAssets assets;
        RoadNetwork road;
        Transform root;
        float spawnTimer;
        MaterialPropertyBlock mpb;
        readonly List<(RoadNetwork.Edge edge, float s)> spawnPoints = new List<(RoadNetwork.Edge, float)>();
        readonly List<Vector3> players = new List<Vector3>();

        public void Init(GameAssets a, RoadNetwork r)
        {
            assets = a;
            road = r;
            root = new GameObject("Traffic").transform;
            root.SetParent(transform, false);
            mpb = new MaterialPropertyBlock();
        }

        /// <summary>À appeler une fois le réseau lu : points d'apparition le long des routes (hors pistes et carrefours).</summary>
        public void Prepare()
        {
            spawnPoints.Clear();
            foreach (var e in road.Edges)
            {
                if (e.kind == RoadKind.Track) continue;
                // l'autoroute attire plus de trafic que les autres routes
                int weight = e.kind == RoadKind.Highway ? 3 : e.kind == RoadKind.Ramp ? 2 : 1;
                for (float s = e.trimA + 25f; s < e.Length - e.trimB - 25f; s += 30f)
                    for (int k = 0; k < weight; k++) spawnPoints.Add((e, s));
            }
        }

        /// <summary>Un choc du joueur contre un véhicule du trafic le rend physique.</summary>
        public void Watch(VehicleController player)
        {
            player.Impacted += (c, impact) =>
            {
                var car = Find(c.collider.gameObject);
                if (car == null || car.crashed) return;
                MakeCrashed(car, -c.impulse * 0.5f);
            };
        }

        public void Tick(IList<Vector3> playerPositions, float dt)
        {
            players.Clear();
            players.AddRange(playerPositions);
            spawnTimer -= dt;
            int target = Mathf.Clamp(Mathf.RoundToInt(baseCount * density * TimeDensity), 2, 60);
            if (spawnTimer <= 0f && Cars.Count < target)
            {
                spawnTimer = 0.12f;
                TrySpawn();
            }
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                var c = Cars[i];
                bool gone = !c.go || PlayerDistance(c.go.transform.position) > despawnDistance;
                if (c.crashed) { c.crashTimer += dt; gone |= c.crashTimer > 14f && PlayerDistance(c.go.transform.position) > 60f; }
                // Bloqué trop longtemps (carrefour encombré) : retiré s'il est loin des regards
                if (c.stuck > 25f && PlayerDistance(c.go.transform.position) > 150f) gone = true;
                if (gone) { Remove(c); Cars.RemoveAt(i); continue; }
                // Accidenté : sa vitesse réelle (le trafic s'arrête derrière lui)
                if (c.crashed) c.velocity = c.rb.linearVelocity;
                UpdateLamps(c);
            }
        }

        float PlayerDistance(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var q in players) best = Mathf.Min(best, (q - p).magnitude);
            return best;
        }

        void Remove(Car c)
        {
            if (c.node != null && c.node.occupant == c) c.node.occupant = null;
            if (c.go) Destroy(c.go);
        }

        /// <summary>Éclat des optiques : éteintes le jour, allumées la nuit (pas de feux stop sur le trafic).</summary>
        void UpdateLamps(Car c)
        {
            float tail = LightsLevel;
            if (Mathf.Abs(tail - c.tailGlow) > 0.02f)
            {
                c.tailGlow = tail;
                TailLights.Apply(c.tails, tail, mpb);
            }
            if (Mathf.Abs(LightsLevel - c.headGlow) > 0.02f)
            {
                c.headGlow = LightsLevel;
                TailLights.Apply(c.heads, LightsLevel, mpb);
            }
        }

        // ------------------------------------------------------------------ apparition

        void TrySpawn()
        {
            if (spawnPoints.Count == 0 || players.Count == 0) return;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var (e, s) = spawnPoints[Random.Range(0, spawnPoints.Count)];
                int direction = e.oneway || Random.value < 0.5f ? 1 : -1;
                int lane = Random.Range(0, e.lanes);
                Vector3 p = e.LanePosition(s, direction, out _, LaneExtra(e, lane));
                float d = PlayerDistance(p);
                if (d < spawnMin || d > spawnMax) continue;
                bool free = true;
                foreach (var o in Cars)
                    if (o.go && (o.go.transform.position - p).sqrMagnitude < 40f * 40f) { free = false; break; }
                if (!free) continue;
                bool rural = e.kind != RoadKind.Street;
                bool motorway = e.kind == RoadKind.Highway;
                bool farm = rural && !motorway && e.kind != RoadKind.Main && e.kind != RoadKind.Ramp && HasFarm && Random.value < assets.farmShare;
                // poids lourds : route de la vallée, et voie de droite de l'autoroute
                bool truck = !farm && (e.kind == RoadKind.Main || motorway && lane == e.lanes - 1) && assets.trafficTrucks.Length > 0
                             && Random.value < Mathf.Lerp(motorway ? 0.3f : 0.12f, motorway ? 0.45f : 0.25f, LightsLevel);
                bool bike = !truck && !farm && HasBikes && Random.value < assets.bikeShare * 1.5f;
                int kind = farm ? 3 : truck ? 1 : bike ? 2 : 0;
                Color32 paint = PickPaint(farm ? FarmColors : truck ? TruckColors : PaintColors);
                var car = CreateVehicle(kind, Random.Range(0, Models(kind).Length), paint);
                car.edge = e;
                car.s = s;
                car.direction = direction;
                car.lane = lane;
                // sur l'autoroute, on roule plus vite à gauche
                float pace = motorway ? Mathf.Lerp(1.14f, 0.86f, e.lanes > 1 ? lane / (e.lanes - 1f) : 0f) * Random.Range(0.95f, 1.06f) : Random.Range(0.82f, 1.08f);
                car.pace = truck ? Random.Range(0.72f, 0.85f) : pace;
                car.cruise = farm ? Random.Range(6.5f, 9.5f) : e.speed * car.pace;
                car.speed = Mathf.Min(car.cruise, e.LimitAt(s));
                Place(car, true);
                Cars.Add(car);
                return;
            }
        }

        bool HasBikes => assets.trafficBikes != null && assets.trafficBikes.Length > 0;
        bool HasFarm => assets.trafficFarm != null && assets.trafficFarm.Length > 0;
        static float FarmKmh() => Random.Range(25f, 40f);

        /// <summary>Modèles d'une famille : 0 voitures, 1 poids lourds, 2 deux-roues, 3 engins agricoles.</summary>
        GameObject[] Models(int kind) => kind == 3 ? assets.trafficFarm : kind == 1 ? assets.trafficTrucks : kind == 2 ? assets.trafficBikes : assets.trafficCars;

        /// <summary>Crée un véhicule d'une famille, d'un modèle et d'une teinte donnés.</summary>
        Car CreateVehicle(int kind, int model, Color32 paint)
        {
            var list = Models(kind);
            if (list == null || list.Length == 0) { kind = 0; list = assets.trafficCars; }
            bool truck = kind == 1, bike = kind == 2, farm = kind == 3;
            var prefab = list[Mathf.Clamp(model, 0, list.Length - 1)];
            var go = Instantiate(prefab, root);
            go.name = "Traffic";
            go.transform.localScale = Vector3.one * (truck ? assets.trafficTruckScale : assets.trafficCarScale);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            box.size = new Vector3(b.size.x / go.transform.localScale.x, b.size.y / go.transform.localScale.y, b.size.z / go.transform.localScale.z);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.mass = truck ? 9000f : farm ? 6000f : bike ? 260f : 1300f;

            // Couleurs variées (flotte de poids lourds surtout blanche, tracteurs aux teintes des constructeurs)
            Paint(renderers, paint, truck || farm);
            // Un tracteur se comporte comme un poids lourd : voie de droite, jamais de dépassement
            var car = new Car
            {
                go = go, rb = rb, length = b.size.z, halfWidth = b.size.x * 0.5f, truck = truck || farm, farm = farm,
            };
            // Les modèles du pack ont leurs propres optiques émissives
            car.lights = HasEmissive(renderers) ? null : CreateLights(go, box);
            car.tails = TailLights.Find(go);
            car.heads = TailLights.Find(go, false);
            return car;
        }

        const string PaintMaterial = "PK_Paint";
        const string PaletteMaterial = "PK_Palette";

        // Poids lourds : cabines et carrosseries majoritairement blanches
        static readonly (Color color, float weight)[] TruckColors =
        {
            (new Color(0.93f, 0.93f, 0.91f), 40f),  // blanc
            (new Color(0.68f, 0.70f, 0.72f), 10f),  // argent
            (new Color(0.10f, 0.22f, 0.50f), 10f),  // bleu
            (new Color(0.62f, 0.07f, 0.06f), 8f),   // rouge
            (new Color(0.06f, 0.06f, 0.07f), 6f),   // noir
            (new Color(0.36f, 0.37f, 0.39f), 6f),   // gris
            (new Color(0.16f, 0.36f, 0.20f), 5f),   // vert
            (new Color(0.88f, 0.50f, 0.12f), 4f),   // orange
            (new Color(0.90f, 0.78f, 0.22f), 3f),   // jaune
        };

        // Tracteurs : teintes des grands constructeurs, le vert domine
        static readonly (Color color, float weight)[] FarmColors =
        {
            (new Color(0.16f, 0.40f, 0.14f), 35f),  // vert
            (new Color(0.66f, 0.08f, 0.06f), 25f),  // rouge
            (new Color(0.10f, 0.25f, 0.55f), 15f),  // bleu
            (new Color(0.45f, 0.62f, 0.22f), 10f),  // vert clair
            (new Color(0.90f, 0.45f, 0.08f), 10f),  // orange
            (new Color(0.55f, 0.57f, 0.55f), 5f),   // gris
        };

        // Teintes courantes du parc automobile (sRGB) : blanc, noir, gris et argent dominent
        static readonly (Color color, float weight)[] PaintColors =
        {
            (new Color(0.92f, 0.92f, 0.90f), 24f),  // blanc
            (new Color(0.06f, 0.06f, 0.07f), 17f),  // noir
            (new Color(0.36f, 0.37f, 0.39f), 15f),  // gris
            (new Color(0.68f, 0.70f, 0.72f), 12f),  // argent
            (new Color(0.10f, 0.20f, 0.46f), 9f),   // bleu
            (new Color(0.62f, 0.07f, 0.06f), 8f),   // rouge
            (new Color(0.24f, 0.33f, 0.24f), 3f),   // vert
            (new Color(0.55f, 0.47f, 0.35f), 3f),   // beige
            (new Color(0.28f, 0.58f, 0.60f), 3f),   // bleu-vert (rappel de l'eau du décor)
            (new Color(0.88f, 0.50f, 0.12f), 2f),   // orange
            (new Color(0.90f, 0.78f, 0.22f), 1f),   // jaune
        };

        static Color PickPaint((Color color, float weight)[] colors)
        {
            float total = 0f;
            foreach (var p in colors) total += p.weight;
            float pick = Random.value * total;
            foreach (var p in colors)
            {
                pick -= p.weight;
                if (pick <= 0f) return p.color * Random.Range(0.93f, 1.05f);
            }
            return colors[0].color;
        }

        /// <summary>
        /// Teinte la carrosserie : matériau de peinture du pack s'il existe. Les modèles du pack à couleur fixe
        /// (taxi, ambulance, benne...) gardent la leur ; les voitures Kenney sont teintées en entier.
        /// </summary>
        void Paint(Renderer[] renderers, Color paint, bool fleet)
        {
            paint.a = 1f;
            bool painted = false, pack = false;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    if (mats[i].name == PaletteMaterial) pack = true;
                    if (mats[i].name != PaintMaterial) continue;
                    r.GetPropertyBlock(mpb, i);
                    mpb.SetColor("_BaseColor", paint);
                    r.SetPropertyBlock(mpb, i);
                    painted = true;
                }
            }
            if (painted || pack || fleet) return;
            Color tint = paint;
            foreach (var r in renderers)
            {
                if (r.transform.name.StartsWith("wheel")) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.Lerp(Color.white, tint, 0.6f));
                r.SetPropertyBlock(mpb);
            }
        }

        static bool HasEmissive(Renderer[] renderers)
        {
            foreach (var r in renderers)
                foreach (var m in r.sharedMaterials)
                    if (m && m.IsKeywordEnabled("_EMISSION")) return true;
            return false;
        }

        GameObject CreateLights(GameObject go, BoxCollider box)
        {
            var lights = new GameObject("Lights");
            lights.transform.SetParent(go.transform, false);
            Vector3 c = box.center, e = box.size * 0.5f;
            foreach (float side in new[] { -0.35f, 0.35f })
            {
                Cube(lights.transform, new Vector3(c.x + e.x * side * 2f, c.y - e.y * 0.3f, c.z + e.z), new Vector3(0.14f, 0.06f, 0.03f), assets.emissiveWhite);
                Cube(lights.transform, new Vector3(c.x + e.x * side * 2f, c.y - e.y * 0.2f, c.z - e.z), new Vector3(0.14f, 0.06f, 0.03f), assets.emissiveRed);
            }
            return lights;
        }

        static void Cube(Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }


        // ------------------------------------------------------------------ conduite

        void FixedUpdate()
        {
            if (road == null) return;
            float dt = Time.fixedDeltaTime;
            // Positions lues une seule fois par pas (ObstacleAhead compare chaque véhicule à tous les autres)
            for (int i = 0; i < Cars.Count; i++)
            {
                var c = Cars[i];
                if (!c.go) continue;
                var t = c.go.transform;
                c.cachedPos = t.position;
                c.cachedFwd = t.forward;
            }
            // Véhicules lointains (plus de FarSim m du joueur) : simulés un pas sur deux avec un pas double
            // (optimisation ; leur mouvement reste lissé par l'interpolation des corps rigides)
            step++;
            for (int i = 0; i < Cars.Count; i++)
            {
                var c = Cars[i];
                if (!c.go) continue;
                if (c.crashed) continue;
                float cdt = dt;
                if (FarFromDrivers(c.cachedPos))
                {
                    if (((i + step) & 1) == 1) continue;
                    cdt = dt * 2f;
                }
                if (c.crossing) UpdateCrossing(c, cdt);
                else UpdateOnEdge(c, cdt);
                c.stuck = c.speed < 0.3f ? c.stuck + cdt : 0f;
            }
        }

        const float FarSim = 140f;
        int step;

        bool FarFromDrivers(Vector3 p)
        {
            foreach (var d in Drivers)
                if (d && (d.transform.position - p).sqrMagnitude < FarSim * FarSim) return false;
            return true;
        }

        /// <summary>Distance restante jusqu'au bout de la chaussée (entrée du carrefour) dans le sens de marche.</summary>
        static float Remaining(Car c) => c.direction > 0 ? c.edge.Length - c.edge.trimB - c.s : c.s - c.edge.trimA;

        static RoadNetwork.Node Ahead(Car c) => c.direction > 0 ? c.edge.b : c.edge.a;

        void UpdateOnEdge(Car c, float dt)
        {
            var e = c.edge;
            float target = Mathf.Min(c.cruise, e.speed * 1.25f) * WeatherSpeed;
            const float decel = 3.2f;
            // Sortie d'autoroute : déport progressif vers la voie de décélération à l'approche de la bretelle ;
            // sinon l'écart d'un changement de voie (ou d'une insertion) se résorbe
            if (e.kind == RoadKind.Highway && Ahead(c).merge) PlanExit(c, Ahead(c), Remaining(c));
            if (c.exitRamp != null) c.laneShift = c.exitShift * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(c.exitFrom, 30f, Remaining(c)));
            else c.laneShift = Mathf.MoveTowards(c.laneShift, 0f, 1.5f * dt);
            // Virages à venir : vitesse confortable, en tenant compte de la distance de freinage
            int i = e.IndexAt(c.s);
            for (float d = 0f; d < 70f; d += 5f)
            {
                int k = e.IndexAt(c.s + d * c.direction);
                target = Mathf.Min(target, Mathf.Sqrt(e.limit[k] * e.limit[k] * WeatherSpeed * WeatherSpeed + 2f * decel * d));
                if (k == 1 || k == e.pts.Length - 1) break;
            }
            // Carrefour au bout : on lève le pied, on ne s'engage que s'il est libre
            float rem = Remaining(c);
            var n = Ahead(c);
            if (n.edges.Count == 1)
                target = Mathf.Min(target, Mathf.Sqrt(2f * decel * Mathf.Max(0f, rem - 3f)));
            else if (n.radius > 0f && !Through(c, n))
            {
                target = Mathf.Min(target, Mathf.Sqrt(6.5f * 6.5f + 2f * decel * Mathf.Max(0f, rem)));
                if (rem < 26f && !Reserve(n, c)) target = Mathf.Min(target, Mathf.Sqrt(2f * decel * Mathf.Max(0f, rem - 2f)));
            }
            // Véhicule devant dans la même voie
            Car ahead = null;
            float gap = float.MaxValue;
            foreach (var o in Cars)
            {
                if (o == c || o.crashed || o.crossing || o.edge != e || o.direction != c.direction || o.lane != c.lane) continue;
                float d = (o.s - c.s) * c.direction;
                if (d > 0f && d < gap) { gap = d; ahead = o; }
            }
            if (ahead != null)
            {
                gap -= (ahead.length + c.length) * 0.5f;
                float safe = 8f + c.speed * (1.2f + (1f - WeatherSpeed) * 2.5f);
                if (gap < safe) target = Mathf.Min(target, ahead.speed * Mathf.Clamp01(gap / safe) + (gap > 6f ? 1f : 0f));
                // Autoroute : on double par la gauche si la voie est libre
                if (e.lanes > 1 && c.lane > 0 && c.exitRamp == null && gap < safe * 1.6f && ahead.speed < c.cruise - 1.5f && Mathf.Abs(c.laneShift) < 0.1f
                    && rem > 120f && LaneFree(c, c.lane - 1)) ChangeLane(c, c.lane - 1);
            }
            else if (e.lanes > 1 && c.lane < e.lanes - 1 && Mathf.Abs(c.laneShift) < 0.1f && Random.value < 0.003f && LaneFree(c, c.lane + 1))
                ChangeLane(c, c.lane + 1);       // on se rabat à droite
            target = ObstacleAhead(c, target);

            c.speed = Mathf.MoveTowards(c.speed, target, (target < c.speed ? 6.5f : 1.8f) * dt);
            c.s += c.speed * c.direction * dt;
            if (Remaining(c) <= 0f)
            {
                if (n.merge) { PassMerge(c, n); return; }
                if (n.edges.Count == 1 && !e.oneway) { TurnBack(c); return; }
                EnterJunction(c, n);
                return;
            }
            Place(c, false);
        }

        /// <summary>Décalage de l'axe d'une voie par rapport à la voie de gauche (autoroute).</summary>
        static float LaneExtra(RoadNetwork.Edge e, int lane) => Mathf.Clamp(lane, 0, e.lanes - 1) * RoadNetwork.LaneWidth;

        /// <summary>Sur l'autoroute, on passe un nœud sans lever le pied quand elle continue de l'autre côté.</summary>
        static bool Through(Car c, RoadNetwork.Node n)
        {
            if (n.merge) return true;           // bretelle : on s'insère ou on sort sans s'arrêter
            if (c.edge.kind != RoadKind.Highway) return false;
            foreach (var o in n.edges) if (o != c.edge && o.kind == RoadKind.Highway) return true;
            return false;
        }

        /// <summary>
        /// À l'approche d'un échangeur, un véhicule de la voie de droite décide une fois pour toutes s'il prend la
        /// bretelle de sortie de son côté (un sur quatre environ).
        /// </summary>
        void PlanExit(Car c, RoadNetwork.Node n, float rem)
        {
            if (c.exitChecked == n || rem > 320f) return;
            c.exitChecked = n;
            c.exitRamp = null;
            var e = c.edge;
            if (c.farm || c.lane != e.lanes - 1 || Mathf.Abs(c.laneShift) > 0.1f || Random.value > 0.4f) return;
            float sNode = c.direction > 0 ? e.Length : 0f;
            Vector3 p = e.Evaluate(sNode, out _, out var right);
            foreach (var r in n.edges)
            {
                // Bretelle de sortie : part du nœud, à droite du sens de marche
                if (!r.oneway || r.a != n || r.b == n) continue;
                float lateral = Vector3.Dot(r.pts[0] - p, right) * c.direction;
                if (lateral <= 0f) continue;
                // Le déport suit la voie de décélération, du bout de son biseau jusqu'au nœud
                float length = 140f;
                if (e.aux != null)
                    foreach (var x in e.aux)
                        if (x.side == c.direction && Mathf.Abs(x.sa - sNode) < 2f) length = Mathf.Abs(x.sb - x.sa);
                if (rem < length + 5f || length < 60f) return;
                c.exitRamp = r;
                c.exitFrom = length;
                c.exitShift = lateral - (e.lane + LaneExtra(e, c.lane));
                return;
            }
        }

        /// <summary>
        /// Nœud d'échangeur (pas de carrefour) : le véhicule de l'autoroute continue tout droit ou prend la bretelle
        /// de sortie qu'il a choisie ; celui d'une bretelle d'entrée s'insère dans la voie de droite.
        /// </summary>
        void PassMerge(Car c, RoadNetwork.Node n)
        {
            RoadNetwork.Edge next = null;
            int lane = 0;
            if (c.exitRamp != null && c.exitRamp.a == n) next = c.exitRamp;
            else if (c.edge.kind == RoadKind.Highway)
            {
                foreach (var o in n.edges)
                    if (o != c.edge && o.kind == RoadKind.Highway) next = o;
                if (next != null) lane = Mathf.Min(c.lane, next.lanes - 1);
            }
            else
            {
                // Bretelle d'entrée : la chaussée qui quitte le nœud du côté où arrive la bretelle
                Vector3 end = c.direction > 0 ? c.edge.pts[c.edge.pts.Length - 1] : c.edge.pts[0];
                foreach (var o in n.edges)
                {
                    if (o.kind != RoadKind.Highway) continue;
                    int dir = o.a == n ? 1 : -1;
                    o.Evaluate(dir > 0 ? 0f : o.Length, out _, out var right);
                    if (Vector3.Dot(end - n.pos, right) * dir > 0f) next = o;
                }
                if (next != null) lane = next.lanes - 1;
            }
            c.exitRamp = null;
            if (next == null)
            {
                // Rien pour continuer (ne devrait pas arriver) : demi-tour sur une route à double sens, arrêt sinon
                if (!c.edge.oneway) TurnBack(c);
                else { c.speed = 0f; c.s = c.edge.Length; }
                return;
            }
            int d = next.a == n ? 1 : -1;
            Switch(c, next, d, d > 0 ? 0f : next.Length, lane);
        }

        /// <summary>
        /// Passe sur une autre arête sans carrefour : la position latérale reste continue (l'écart avec la nouvelle
        /// voie se résorbe ensuite), l'allure s'adapte à la nouvelle route.
        /// </summary>
        void Switch(Car c, RoadNetwork.Edge next, int direction, float s, int lane)
        {
            Vector3 old = c.edge.LanePosition(Mathf.Clamp(c.s, 0f, c.edge.Length), c.direction, out _, LaneExtra(c.edge, c.lane) + c.laneShift);
            float over = Mathf.Max(0f, -Remaining(c));
            c.edge = next;
            c.direction = direction;
            c.lane = lane;
            c.s = Mathf.Clamp(s + direction * (0.05f + over), 0f, next.Length);
            Vector3 axis = next.LanePosition(c.s, direction, out var f, LaneExtra(next, lane));
            Vector3 r = new Vector3(f.z, 0f, -f.x).normalized;
            c.laneShift = Vector3.Dot(old - axis, r);
            Retune(c, next);
            Place(c, false);
        }

        /// <summary>Allure de croisière sur une nouvelle route : la même part de sa vitesse (les tracteurs gardent la leur).</summary>
        static void Retune(Car c, RoadNetwork.Edge e)
        {
            if (!c.farm && c.pace > 0f) c.cruise = e.speed * c.pace;
        }

        bool LaneFree(Car c, int lane)
        {
            foreach (var o in Cars)
            {
                if (o == c || o.crossing || o.edge != c.edge || o.direction != c.direction || o.lane != lane) continue;
                float d = (o.s - c.s) * c.direction;
                if (d > -28f && d < 45f) return false;
            }
            return true;
        }

        /// <summary>Changement de voie progressif : la position latérale reste continue, l'écart se résorbe.</summary>
        void ChangeLane(Car c, int lane)
        {
            c.laneShift += (c.lane - lane) * RoadNetwork.LaneWidth;
            c.lane = lane;
        }

        /// <summary>Réserve le carrefour pour ce véhicule ; faux si un autre (ou un joueur) y est engagé.</summary>
        bool Reserve(RoadNetwork.Node n, Car c)
        {
            if (n.occupant == c) return true;
            if (n.occupant is Car other && other.go && !other.crashed && Time.time < n.occupiedUntil) return false;
            foreach (var p in players)
            {
                Vector3 d = p - n.pos;
                d.y = 0f;
                if (d.magnitude < n.radius + 5f) return false;
            }
            n.occupant = c;
            n.occupiedUntil = Time.time + 9f;
            return true;
        }

        /// <summary>Bout de route : demi-tour (le véhicule repart dans l'autre voie).</summary>
        void TurnBack(Car c)
        {
            c.direction = -c.direction;
            c.s += c.direction * 2f;
            c.speed = 0f;
            Place(c, true);
        }

        void EnterJunction(Car c, RoadNetwork.Node n)
        {
            // Choix de la route suivante : plutôt tout droit, rarement une piste, jamais demi-tour s'il y a mieux
            Vector3 p0 = c.edge.LanePosition(c.direction > 0 ? c.edge.Length - c.edge.trimB : c.edge.trimA, c.direction, out var f0, LaneExtra(c.edge, c.lane) + c.laneShift);
            RoadNetwork.Edge best = null;
            float total = 0f;
            var options = new List<(RoadNetwork.Edge e, float w)>();
            foreach (var e in n.edges)
            {
                if (e == c.edge) continue;
                if (e.oneway && e.a != n) continue;     // sens interdit : une bretelle ne se prend que de a vers b
                int dir = e.a == n ? 1 : -1;
                e.LanePosition(dir > 0 ? e.trimA : e.Length - e.trimB, dir, out var f);
                float straight = Mathf.Clamp01(Vector3.Dot(f0, f) * 0.5f + 0.5f);
                float w = 0.35f + straight * straight * 2f;
                if (e.kind == RoadKind.Track) w *= c.farm ? 1f : 0.08f;
                // sur l'autoroute on y reste le plus souvent ; les tracteurs n'y entrent pas
                if (e.kind == RoadKind.Highway) w *= c.farm ? 0f : c.edge.kind == RoadKind.Highway ? 8f : 1.5f;
                if (e.kind == RoadKind.Lane && c.truck && !c.farm) w *= 0.1f;
                // bretelle d'entrée : comme l'autoroute, interdite aux tracteurs
                if (e.kind == RoadKind.Ramp) w *= c.farm ? 0f : 1.5f;
                if (w <= 0f) continue;
                options.Add((e, w));
                total += w;
            }
            if (options.Count == 0)
            {
                // Bout d'une route à double sens : demi-tour ; au bout d'un sens unique sans issue, le véhicule attend
                // (il sera retiré loin des regards)
                if (!c.edge.oneway) TurnBack(c);
                else { c.speed = 0f; c.s = c.direction > 0 ? c.edge.Length - c.edge.trimB : c.edge.trimA; }
                return;
            }
            float pick = Random.value * total;
            foreach (var o in options)
            {
                best = o.e;
                pick -= o.w;
                if (pick <= 0f) break;
            }
            c.nextEdge = best;
            c.nextDirection = best.a == n ? 1 : -1;
            c.nextS = c.nextDirection > 0 ? best.trimA : best.Length - best.trimB;
            // même voie si l'autoroute continue ; voie de droite en y entrant
            c.nextLane = best.lanes <= 1 ? 0 : c.edge.kind == RoadKind.Highway ? Mathf.Min(c.lane, best.lanes - 1) : best.lanes - 1;
            if (n.radius <= 0f)
            {
                // Simple continuation : pas de carrefour
                c.edge = best;
                c.lane = c.nextLane;
                c.laneShift = 0f;
                c.direction = c.nextDirection;
                c.s = c.nextS + c.direction * 0.1f;
                Retune(c, best);
                Place(c, false);
                return;
            }
            Vector3 p3 = best.LanePosition(c.nextS, c.nextDirection, out var f3, LaneExtra(best, c.nextLane));
            float k = Vector3.Distance(p0, p3) * 0.4f;
            c.c0 = p0; c.c1 = p0 + f0 * k; c.c2 = p3 - f3 * k; c.c3 = p3;
            c.crossLength = 0f;
            Vector3 prev = p0;
            for (int j = 1; j <= 8; j++)
            {
                Vector3 q = Bezier(c, j / 8f);
                c.crossLength += Vector3.Distance(prev, q);
                prev = q;
            }
            c.crossLength = Mathf.Max(c.crossLength, 0.5f);
            c.crossPos = 0f;
            c.crossing = true;
            c.node = n;
            n.occupant = c;
            n.occupiedUntil = Time.time + 9f;
        }

        static Vector3 Bezier(Car c, float t)
        {
            float u = 1f - t;
            return u * u * u * c.c0 + 3f * u * u * t * c.c1 + 3f * u * t * t * c.c2 + t * t * t * c.c3;
        }

        void UpdateCrossing(Car c, float dt)
        {
            float target = ObstacleAhead(c, Mathf.Min(c.cruise, 6.5f) * WeatherSpeed);
            c.speed = Mathf.MoveTowards(c.speed, target, (target < c.speed ? 6.5f : 2.2f) * dt);
            c.crossPos += Mathf.Max(c.speed, 0.6f) * dt;
            float t = c.crossPos / c.crossLength;
            if (t >= 1f)
            {
                c.crossing = false;
                if (c.node != null && c.node.occupant == c) { c.node.occupant = null; }
                c.node = null;
                c.edge = c.nextEdge;
                c.lane = c.nextLane;
                c.laneShift = 0f;
                c.direction = c.nextDirection;
                c.s = c.nextS;
                Retune(c, c.edge);
                Place(c, false);
                return;
            }
            Vector3 p = Bezier(c, t);
            Vector3 fwd = Bezier(c, Mathf.Min(1f, t + 0.06f)) - Bezier(c, Mathf.Max(0f, t - 0.06f));
            Move(c, p, fwd, false);
        }

        /// <summary>Obstacle dans un cône devant (joueur, véhicule accidenté ou en travers) : ralentit ou s'arrête.</summary>
        float ObstacleAhead(Car c, float target)
        {
            Vector3 pos = c.cachedPos;
            Vector3 f = c.cachedFwd;
            f.y = 0f;
            f.Normalize();
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            foreach (var o in Cars)
            {
                if (o == c || !o.go) continue;
                // Sur la même voie, le suivi de file s'en charge déjà
                if (!c.crossing && !o.crossing && !o.crashed && o.edge == c.edge && o.direction == c.direction) continue;
                if (!c.crossing && !o.crossing && !o.crashed && o.edge == c.edge) continue;   // véhicule d'en face, dans sa voie
                Vector3 rel = o.cachedPos - pos;
                if (Mathf.Abs(rel.y) > 4f) continue;
                float along = Vector3.Dot(rel, f);
                if (along <= 0f || along > 40f) continue;
                if (Mathf.Abs(Vector3.Dot(rel, r)) > c.halfWidth + Extent(o, r) + 0.4f) continue;
                target = Brake(c, target, along - c.length * 0.5f - Extent(o, f), Mathf.Max(0f, Vector3.Dot(o.velocity, f)));
            }
            foreach (var driver in Drivers) target = PlayerAhead(c, driver, pos, f, r, target);
            return target;
        }

        static float Brake(Car c, float target, float gap, float obstacleSpeed)
        {
            float safe = 5f + c.speed * 0.9f;
            if (gap < safe) target = Mathf.Min(target, obstacleSpeed * Mathf.Clamp01(gap / safe) + (gap > 3f ? 0.5f : 0f));
            return target;
        }

        float PlayerAhead(Car c, VehicleController p, Vector3 pos, Vector3 f, Vector3 r, float target)
        {
            if (!p) return target;
            Vector3 rel = p.transform.position - pos;
            if (Mathf.Abs(rel.y) > 4f) return target;
            float along = Vector3.Dot(rel, f);
            if (along <= 0f || along > 45f) return target;
            if (Mathf.Abs(Vector3.Dot(rel, r)) > c.halfWidth + 1.3f) return target;
            return Brake(c, target, along - c.length * 0.5f - 2.3f, Mathf.Max(0f, Vector3.Dot(p.Velocity, f)));
        }

        /// <summary>Demi-encombrement d'un véhicule le long d'un axe horizontal.</summary>
        public static float Extent(Car o, Vector3 axis)
        {
            var t = o.go.transform;
            return Mathf.Abs(Vector3.Dot(t.forward, axis)) * o.length * 0.5f + Mathf.Abs(Vector3.Dot(t.right, axis)) * o.halfWidth;
        }

        void Place(Car c, bool snap)
        {
            Vector3 p = c.edge.LanePosition(c.s, c.direction, out var fwd, LaneExtra(c.edge, c.lane) + c.laneShift);
            Move(c, p, fwd, snap);
        }

        void Move(Car c, Vector3 p, Vector3 fwd, bool snap)
        {
            if (fwd.sqrMagnitude < 1e-6f) fwd = c.go.transform.forward;
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            c.velocity = fwd.normalized * c.speed;
            if (snap) { c.go.transform.SetPositionAndRotation(p, rot); c.rb.position = p; c.rb.rotation = rot; }
            else { c.rb.MovePosition(p); c.rb.MoveRotation(rot); }
        }

        /// <summary>Une voiture percutée devient un objet physique libre.</summary>
        public void MakeCrashed(Car c, Vector3 impulse)
        {
            if (c.crashed) return;
            c.crashed = true;
            if (c.node != null && c.node.occupant == c) c.node.occupant = null;
            c.rb.isKinematic = false;
            c.rb.useGravity = true;
            c.rb.linearVelocity = c.velocity;
            c.rb.AddForce(impulse, ForceMode.Impulse);
        }

        public Car Find(GameObject go)
        {
            foreach (var c in Cars) if (c.go && (c.go == go || go.transform.IsChildOf(c.go.transform))) return c;
            return null;
        }

        public void SetLights(bool on)
        {
            foreach (var c in Cars) if (c.lights) c.lights.SetActive(on);
        }

        /// <summary>Retire un véhicule.</summary>
        public void Despawn(Car c)
        {
            Remove(c);
            Cars.Remove(c);
        }

        /// <summary>Dégage les véhicules proches d'un point (replacement du joueur sur la route).</summary>
        public void ClearAround(Vector3 p, float radius)
        {
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                var c = Cars[i];
                if (!c.go || (c.go.transform.position - p).sqrMagnitude >= radius * radius) continue;
                Despawn(c);
            }
        }
    }
}
