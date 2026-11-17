using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Atelier du garage de tuning : la voiture du joueur sur son pont, dans un atelier fermé (porte sectionnelle,
    /// établi et panneau d'outils, servante, râtelier de pneus, étagères, compresseur, néons). La caméra tourne autour
    /// de la voiture et vient cadrer la pièce choisie dans le menu (<see cref="Focus"/>) : moteur, roue, arrière…
    /// </summary>
    public class WorkshopStage : ShopStage
    {
        const float HX = 7.5f, HZ = 9f, H = 5.6f;      // demi-largeur, demi-profondeur, hauteur sous plafond (m)
        const float Lift = 0.06f;                       // plateaux du pont, sous les roues
        static readonly Vector3 Origin = new Vector3(3000f, 2500f, -6300f);
        static readonly Color Pink = new Color(1f, 0.42f, 0.82f), Cyan = new Color(0.35f, 0.85f, 1f);

        /// <summary>Pièce effective (atelier du pack ou décor de repli) : demi-largeur, demi-profondeur, dessus du pont.</summary>
        float roomX = HX, roomZ = HZ, lift = Lift;
        Transform carPivot;
        GameObject carPrefab;
        Bounds car = new Bounds(new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 1.3f, 4.2f));
        Vector3 wheelL, wheelR;
        Light spotA, spotB;
        int focus = -1;
        float angle = 35f, angleVel, radius = 6f, radiusVel, height = 1.7f, heightVel, orbit, orbitIdle, drift, time;
        Vector3 look, lookVel;
        Material floor, wallLow, wallHigh, ceiling, steel, dark, red, yellow, blue, wood, peg, rubber, stripe, tube, pink, cyan;

        public void Build(GameManager g)
        {
            lighting = new StudioLighting
            {
                keyRotation = Quaternion.Euler(64f, -30f, 0f),
                keyColor = new Color(1f, 0.96f, 0.9f),
                keyIntensity = 0.85f,
                sky = new Color(0.4f, 0.42f, 0.5f),
                equator = new Color(0.28f, 0.3f, 0.36f),
                ground = new Color(0.15f, 0.15f, 0.18f),
                fog = new Color(0.01f, 0.01f, 0.015f),
                fogDensity = 0f,
                bloomBoost = 0.2f,
            };
            // Atelier du pack alpin (Tools/Blender/alp_shops.py) : sombre à la marque orange, néons bleus, comme la façade
            if (g.assets.alp && g.assets.alp.Prefab("tuning_workshop"))
            {
                lighting = new StudioLighting
                {
                    keyRotation = Quaternion.Euler(62f, -32f, 0f),
                    keyColor = new Color(1f, 0.97f, 0.92f),
                    keyIntensity = 1.0f,
                    sky = new Color(0.56f, 0.58f, 0.62f),
                    equator = new Color(0.44f, 0.45f, 0.48f),
                    ground = new Color(0.26f, 0.26f, 0.27f),
                    fog = new Color(0.06f, 0.065f, 0.075f),
                    fogDensity = -1f,
                    bloomBoost = 0.15f,
                };
                Create(g, "Workshop", Origin, lighting.fog);
                if (PlacePack("tuning_workshop", root, Vector3.zero))
                {
                    // Fenêtres hautes : le vrai paysage, vu depuis l'emplacement du garage sur la carte
                    if (g.Map != null && g.Map.Json.pois != null)
                        foreach (var poi in g.Map.Json.pois)
                            if (poi.id == "tuning") viewPoint = new Vector3(poi.x, 0f, poi.z);
                    roomX = 6.5f; roomZ = 8f; lift = 0.2f;
                    spotA = Spot(new Vector3(2.4f, 4.9f, 2.2f), new Vector3(0f, 0.6f, 0.4f), new Color(1f, 0.96f, 0.9f), 22f, 11f, 80f);
                    spotB = Spot(new Vector3(-2.4f, 4.9f, -2.2f), new Vector3(0f, 0.6f, -0.4f), new Color(0.9f, 0.94f, 1f), 16f, 11f, 80f);
                    Glint(new Vector3(0f, 3.9f, -roomZ + 1f), new Color(1f, 0.55f, 0.15f), 3f, 7f);
                    Glint(new Vector3(0f, 4.6f, roomZ - 1f), Cyan, 2.5f, 7f);
                    Finish();
                    return;
                }
            }
            else Create(g, "Workshop", Origin, lighting.fog);

            floor = Flat(new Color(0.33f, 0.34f, 0.36f));
            wallLow = Flat(new Color(0.16f, 0.18f, 0.23f));
            wallHigh = Flat(new Color(0.36f, 0.38f, 0.43f));
            ceiling = Flat(new Color(0.09f, 0.1f, 0.12f));
            steel = Flat(new Color(0.5f, 0.52f, 0.56f), 0.6f, 0.6f);
            dark = Flat(new Color(0.07f, 0.075f, 0.09f));
            red = Flat(new Color(0.78f, 0.13f, 0.11f), 0.4f, 0.4f);
            yellow = Flat(new Color(0.96f, 0.76f, 0.1f));
            blue = Flat(new Color(0.13f, 0.32f, 0.66f), 0.3f, 0.3f);
            wood = Flat(new Color(0.6f, 0.42f, 0.25f));
            peg = Flat(new Color(0.7f, 0.5f, 0.3f));
            rubber = Flat(new Color(0.045f, 0.045f, 0.055f));
            stripe = Flat(Pink * 0.85f);
            tube = Glow(new Color(1f, 0.97f, 0.9f), 3.2f);
            pink = Glow(Pink, 3f);
            cyan = Glow(Cyan, 2.6f);

            Shell();
            Door();
            Bay();
            Bench();
            Cart();
            TireRack();
            Shelves();
            BackWall();
            Corners();

            spotA = Spot(new Vector3(2.4f, H - 0.5f, 2.2f), new Vector3(0f, 0.6f, 0.4f), new Color(1f, 0.96f, 0.9f), 26f, 12f, 80f);
            spotB = Spot(new Vector3(-2.4f, H - 0.5f, -2.2f), new Vector3(0f, 0.6f, -0.4f), new Color(0.85f, 0.92f, 1f), 20f, 12f, 80f);
            Glint(new Vector3(0f, 3.4f, -HZ + 1.2f), Pink, 5f, 9f);
            Glint(new Vector3(0f, 4.3f, HZ - 1.2f), Cyan, 3.5f, 8f);
            Finish();
        }

        // ------------------------------------------------------------------ voiture et caméra

        /// <summary>Voiture sur le pont (fiche d'origine ou améliorée : même modèle).</summary>
        public void SetCar(VehicleSpec spec)
        {
            if (spec == null || !root || (carPivot && spec.prefab == carPrefab)) return;
            if (carPivot) Destroy(carPivot.gameObject);
            // Encombrement mesuré sur le modèle : le décor doit être actif à cet instant
            bool active = root.gameObject.activeSelf;
            root.gameObject.SetActive(true);
            carPrefab = spec.prefab;
            carPivot = PlaceCar(spec, root, new Vector3(0f, lift, 0f), out car);
            foreach (var r in carPivot.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            float ex = car.extents.x, ez = car.extents.z;
            wheelL = new Vector3(-ex * 0.9f, 0.33f, ez * 0.62f);
            wheelR = new Vector3(ex * 0.9f, 0.33f, ez * 0.62f);
            foreach (var t in carPivot.GetComponentsInChildren<Transform>())
            {
                if (!t.name.StartsWith("wheel") || !t.name.Contains("front")) continue;
                Vector3 p = carPivot.InverseTransformPoint(t.position);
                if (p.x < -0.1f) wheelL = p; else if (p.x > 0.1f) wheelR = p;
            }
            root.gameObject.SetActive(active);
        }

        /// <summary>Peint la voiture sur le pont (couleur du nuancier de <see cref="CarPaint"/> : essai ou couleur achetée).</summary>
        public void Paint(int swatch) => CarPaint.Apply(carPivot, swatch);

        /// <summary>Pièce cadrée (indice de <see cref="CarTuning.Part"/>, -1 : vue d'ensemble). snap : sans transition.</summary>
        public void Focus(int part, bool snap = false)
        {
            focus = part;
            if (!snap) return;
            Target(part, out angle, out radius, out height, out look);
            angleVel = radiusVel = heightVel = 0f;
            lookVel = Vector3.zero;
            orbit = drift = 0f;
        }

        /// <summary>Tourne la caméra autour de la voiture (degrés) ; elle revient d'elle-même au cadrage de la pièce.</summary>
        public void Orbit(float degrees)
        {
            if (Mathf.Abs(degrees) < 1e-4f) return;
            orbit = Mathf.Clamp(orbit + degrees, -360f, 360f);
            orbitIdle = 0f;
        }

        /// <summary>Cadrage d'une pièce autour de la voiture : cap (0° = devant), distance, hauteur, point visé.</summary>
        void Target(int part, out float a, out float r, out float h, out Vector3 l)
        {
            float ex = car.extents.x, ez = car.extents.z, top = car.size.y;
            Vector3 p;
            switch (part)
            {
                case (int)CarTuning.Part.Engine:
                    p = new Vector3(1.5f, top + 0.8f, ez + 2.5f); l = new Vector3(0f, top * 0.55f, ez * 0.5f); break;
                case (int)CarTuning.Part.Gearbox:
                    p = new Vector3(-(ex + 3.2f), 0.6f, -0.4f); l = new Vector3(0f, 0.42f, 0f); break;
                case (int)CarTuning.Part.Tires:
                    p = wheelL + new Vector3(-2f, 0.3f, 1.2f); l = wheelL; break;
                case (int)CarTuning.Part.Brakes:
                    p = wheelR + new Vector3(1.7f, 0.15f, -1.5f); l = wheelR; break;
                case (int)CarTuning.Part.Weight:
                    p = new Vector3(-(ex + 2.4f), 3.3f, -(ez + 0.8f)); l = new Vector3(0f, top * 0.45f, 0f); break;
                case (int)CarTuning.Part.Suspension:
                    // De profil, au ras du sol : roues et passages de roue
                    p = new Vector3(-(ex + 3.4f), 0.45f, -0.3f); l = new Vector3(0f, 0.4f, 0f); break;
                case (int)CarTuning.Part.Aero:
                    p = new Vector3(1.3f, top + 0.55f, -(ez + 2.9f)); l = new Vector3(0f, top * 0.7f, -ez * 0.7f); break;
                default:
                    float d = Mathf.Min(Mathf.Max(ex, ez) * 1.7f + 3f, 6.8f);
                    p = new Vector3(Mathf.Sin(35f * Mathf.Deg2Rad) * d, 1.7f, Mathf.Cos(35f * Mathf.Deg2Rad) * d);
                    l = new Vector3(0f, top * 0.42f, 0f);
                    break;
            }
            a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
            r = new Vector2(p.x, p.z).magnitude;
            h = p.y;
        }

        protected override void Tick(float dt)
        {
            time += dt;
            Target(focus, out float ta, out float tr, out float th, out Vector3 tl);
            // Vue d'ensemble : la caméra fait lentement le tour de la voiture (et de l'atelier)
            if (focus < 0) drift += dt * 5f;
            else drift = Mathf.Lerp(drift, 0f, 1f - Mathf.Exp(-dt * 0.5f));
            if (focus < 0) ta += drift;
            angle =Mathf.SmoothDampAngle(angle, ta, ref angleVel, 0.45f, Mathf.Infinity, dt);
            radius = Mathf.SmoothDamp(radius, tr, ref radiusVel, 0.45f, Mathf.Infinity, dt);
            height = Mathf.SmoothDamp(height, th, ref heightVel, 0.45f, Mathf.Infinity, dt);
            look = Vector3.SmoothDamp(look, tl, ref lookVel, 0.4f, Mathf.Infinity, dt);
            orbitIdle += dt;
            if (orbitIdle > 2.5f) orbit = Mathf.Lerp(orbit, 0f, 1f - Mathf.Exp(-dt * 1.2f));

            // Vue d'ensemble : la caméra se balance lentement ; sur une pièce, à peine
            float a = (angle + orbit + Mathf.Sin(time * 0.35f) * 3f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Sin(a) * radius, height, Mathf.Cos(a) * radius);
            // Jamais dans un mur ni dans le mobilier qui le longe
            p.x = Mathf.Clamp(p.x, -(roomX - 1.4f), roomX - 1.4f);
            p.z = Mathf.Clamp(p.z, -(roomZ - 1.4f), roomZ - 1.4f);
            p.y = Mathf.Clamp(p.y, 0.25f, H - 0.9f);
            Vector3 origin = root.position + Vector3.up * lift;
            cam.transform.SetPositionAndRotation(origin + p, Quaternion.LookRotation(look - p, Vector3.up));
            cam.fieldOfView = 46f - punch;
            if (spotA) spotA.intensity = 26f + punch * 6f;
            if (spotB) spotB.intensity = 20f + punch * 6f;
        }

        // ------------------------------------------------------------------ décor

        /// <summary>Sol, murs à soubassement sombre et filet rose, plafond, poutres et tubes d'éclairage.</summary>
        void Shell()
        {
            const float band = 1.5f;
            Box(root, new Vector3(0f, -0.1f, 0f), new Vector3(HX * 2f + 0.4f, 0.2f, HZ * 2f + 0.4f), floor);
            Box(root, new Vector3(0f, H + 0.1f, 0f), new Vector3(HX * 2f + 0.4f, 0.2f, HZ * 2f + 0.4f), ceiling);
            foreach (float s in new[] { -1f, 1f })
            {
                Box(root, new Vector3(s * (HX + 0.1f), band * 0.5f, 0f), new Vector3(0.2f, band, HZ * 2f + 0.4f), wallLow);
                Box(root, new Vector3(s * (HX + 0.1f), (band + H) * 0.5f, 0f), new Vector3(0.2f, H - band, HZ * 2f + 0.4f), wallHigh);
                Box(root, new Vector3(s * (HX - 0.015f), band, 0f), new Vector3(0.03f, 0.09f, HZ * 2f), stripe);
                Box(root, new Vector3(0f, band * 0.5f, s * (HZ + 0.1f)), new Vector3(HX * 2f, band, 0.2f), wallLow);
                Box(root, new Vector3(0f, (band + H) * 0.5f, s * (HZ + 0.1f)), new Vector3(HX * 2f, H - band, 0.2f), wallHigh);
                Box(root, new Vector3(0f, band, s * (HZ - 0.015f)), new Vector3(HX * 2f, 0.09f, 0.03f), stripe);
            }
            foreach (float z in new[] { -6.75f, -2.25f, 2.25f, 6.75f })
                Box(root, new Vector3(0f, H - 0.16f, z), new Vector3(HX * 2f, 0.32f, 0.22f), dark);
            foreach (float z in new[] { -4.5f, 0f, 4.5f })
                foreach (float x in new[] { -3.2f, 3.2f })
                {
                    Box(root, new Vector3(x, H - 0.38f, z), new Vector3(2.6f, 0.1f, 0.5f), dark);
                    Box(root, new Vector3(x, H - 0.45f, z), new Vector3(2.4f, 0.05f, 0.32f), tube);
                }
        }

        /// <summary>Porte sectionnelle face à la voiture, néon au-dessus, bandes de sécurité au sol.</summary>
        void Door()
        {
            var door = Group(root, "Door", new Vector3(0f, 0f, HZ));
            for (int k = 0; k < 13; k++)
                Box(door, new Vector3(0f, 0.15f + k * 0.3f, -0.05f), new Vector3(5.2f, 0.27f, 0.08f), k % 2 == 0 ? steel : wallHigh);
            foreach (float s in new[] { -1f, 1f })
                Box(door, new Vector3(s * 2.75f, 2f, -0.1f), new Vector3(0.3f, 4f, 0.22f), dark);
            Box(door, new Vector3(0f, 4.1f, -0.1f), new Vector3(5.8f, 0.3f, 0.22f), dark);
            Box(door, new Vector3(0f, 4.78f, -0.06f), new Vector3(3.6f, 0.12f, 0.06f), pink);
            Box(door, new Vector3(0f, 4.54f, -0.06f), new Vector3(2.4f, 0.07f, 0.06f), cyan);
            for (int k = 0; k < 10; k++)
                Box(door, new Vector3((k - 4.5f) * 0.58f, 0.007f, -0.55f), new Vector3(0.58f, 0.014f, 0.45f), k % 2 == 0 ? yellow : dark);
        }

        /// <summary>Emplacement de la voiture : plateaux du pont sous les roues, tracé jaune autour.</summary>
        void Bay()
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Box(root, new Vector3(s * 0.72f, Lift * 0.5f, 0f), new Vector3(0.9f, Lift, 4.9f), dark);
                Box(root, new Vector3(s * 1.2f, Lift * 0.5f, 0f), new Vector3(0.06f, Lift + 0.004f, 4.9f), yellow);
                Box(root, new Vector3(s * 1.9f, 0.006f, 0f), new Vector3(0.08f, 0.012f, 6.8f), yellow);
                Box(root, new Vector3(0f, 0.006f, s * 3.4f), new Vector3(3.88f, 0.012f, 0.08f), yellow);
            }
        }

        /// <summary>Mur de gauche : établi sur caissons rouges, panneau d'outils, étau, bidons.</summary>
        void Bench()
        {
            var b = Group(root, "Bench", new Vector3(-HX, 0f, -1.5f));
            Box(b, new Vector3(0.5f, 0.95f, 0f), new Vector3(1f, 0.08f, 4.2f), wood);
            for (int k = 0; k < 3; k++)
            {
                float z = -1.4f + k * 1.4f;
                Box(b, new Vector3(0.48f, 0.455f, z), new Vector3(0.9f, 0.91f, 1.3f), red);
                for (int d = 0; d < 3; d++) Box(b, new Vector3(0.94f, 0.22f + d * 0.27f, z), new Vector3(0.03f, 0.04f, 0.8f), steel);
            }
            Box(b, new Vector3(0.04f, 2.15f, 0f), new Vector3(0.06f, 1.5f, 4f), peg);
            for (int k = 0; k < 9; k++)
            {
                float z = -1.7f + k * 0.42f;
                float len = 0.35f + (k * 37 % 5) * 0.09f;
                Box(b, new Vector3(0.09f, 2.6f - len * 0.5f, z), new Vector3(0.03f, len, 0.06f), k % 3 == 0 ? red : steel);
                if (k % 2 == 0) Cyl(b, new Vector3(0.09f, 2.6f, z), 0.07f, 0.03f, steel, false, new Vector3(0f, 0f, 90f));
            }
            Box(b, new Vector3(0.09f, 1.68f, -0.9f), new Vector3(0.03f, 0.2f, 0.8f), steel);
            Box(b, new Vector3(0.09f, 1.68f, 0.9f), new Vector3(0.03f, 0.26f, 0.5f), yellow);
            // Étau, caisse à outils, bidons
            Box(b, new Vector3(0.5f, 1.07f, 1.5f), new Vector3(0.3f, 0.16f, 0.22f), blue);
            Box(b, new Vector3(0.5f, 1.2f, 1.5f), new Vector3(0.12f, 0.12f, 0.34f), steel);
            Box(b, new Vector3(0.45f, 1.06f, 0.2f), new Vector3(0.5f, 0.14f, 0.32f), red);
            Cyl(b, new Vector3(0.4f, 1.14f, -1.6f), 0.11f, 0.3f, yellow);
            Cyl(b, new Vector3(0.62f, 1.11f, -1.3f), 0.09f, 0.24f, blue);
        }

        /// <summary>Servante d'atelier, près de la voiture.</summary>
        void Cart()
        {
            var c = Group(root, "Cart", new Vector3(-3.9f, 0f, 2.7f), 25f);
            Box(c, new Vector3(0f, 0.55f, 0f), new Vector3(0.75f, 0.8f, 0.5f), red);
            Box(c, new Vector3(0f, 0.97f, 0f), new Vector3(0.8f, 0.04f, 0.55f), steel);
            for (int d = 0; d < 3; d++) Box(c, new Vector3(0f, 0.32f + d * 0.22f, 0.26f), new Vector3(0.5f, 0.03f, 0.02f), steel);
            foreach (float x in new[] { -0.3f, 0.3f })
                foreach (float z in new[] { -0.2f, 0.2f })
                    Cyl(c, new Vector3(x, 0.075f, z), 0.075f, 0.05f, rubber, false, new Vector3(0f, 0f, 90f));
            Cyl(c, new Vector3(-0.15f, 1.07f, 0f), 0.07f, 0.16f, blue);
            Box(c, new Vector3(0.15f, 1.02f, 0.05f), new Vector3(0.3f, 0.05f, 0.12f), steel);
        }

        /// <summary>Mur de droite : râtelier de pneus sur deux niveaux.</summary>
        void TireRack()
        {
            var r = Group(root, "TireRack", new Vector3(HX, 0f, -3.4f));
            foreach (float z in new[] { -1.55f, 1.55f })
            {
                Box(r, new Vector3(-0.08f, 1.3f, z), new Vector3(0.08f, 2.6f, 0.08f), steel);
                Box(r, new Vector3(-0.74f, 1.3f, z), new Vector3(0.08f, 2.6f, 0.08f), steel);
            }
            for (int s = 0; s < 2; s++)
            {
                float y = 0.52f + s * 1.05f;
                Box(r, new Vector3(-0.41f, y - 0.36f, 0f), new Vector3(0.7f, 0.05f, 3.1f), steel);
                for (int k = 0; k < 9; k++)
                {
                    float z = -1.28f + k * 0.32f;
                    Cyl(r, new Vector3(-0.41f, y, z), 0.34f, 0.27f, rubber, false, new Vector3(90f, 0f, 0f));
                    Cyl(r, new Vector3(-0.41f, y, z), 0.2f, 0.275f, (k + s) % 3 == 0 ? dark : steel, false, new Vector3(90f, 0f, 0f));
                }
            }
        }

        /// <summary>Mur de droite : étagère de cartons, bidons et pièces.</summary>
        void Shelves()
        {
            var s = Group(root, "Shelves", new Vector3(HX, 0f, 3.6f));
            foreach (float z in new[] { -1.75f, 1.75f })
            {
                Box(s, new Vector3(-0.06f, 1.5f, z), new Vector3(0.06f, 3f, 0.06f), steel);
                Box(s, new Vector3(-0.56f, 1.5f, z), new Vector3(0.06f, 3f, 0.06f), steel);
            }
            var rng = new System.Random(7);
            Material[] paint = { red, blue, yellow, wood, steel, dark };
            for (int level = 0; level < 4; level++)
            {
                float y = 0.35f + level * 0.75f;
                Box(s, new Vector3(-0.31f, y, 0f), new Vector3(0.58f, 0.05f, 3.5f), steel);
                for (int k = 0; k < 7; k++)
                {
                    float z = -1.45f + k * 0.48f;
                    var m = paint[rng.Next(paint.Length)];
                    float size = 0.22f + (float)rng.NextDouble() * 0.2f;
                    int kind = rng.Next(4);
                    if (kind == 0) continue;
                    if (kind == 1) Cyl(s, new Vector3(-0.31f, y + 0.025f + size * 0.6f, z), 0.1f + size * 0.12f, size * 1.2f, m);
                    else Box(s, new Vector3(-0.31f, y + 0.025f + size * 0.5f, z), new Vector3(0.4f, size, size * 1.3f), m);
                }
            }
        }

        /// <summary>Mur du fond : enseigne au néon (cadre et traits de vitesse), meubles bas, bloc moteur et cartons.</summary>
        void BackWall()
        {
            var sign = Group(root, "Sign", new Vector3(0f, 3.5f, -HZ));
            Box(sign, new Vector3(0f, 0f, 0.02f), new Vector3(4.9f, 1.8f, 0.04f), dark);
            foreach (float s in new[] { -1f, 1f })
            {
                Box(sign, new Vector3(0f, s * 0.75f, 0.06f), new Vector3(4.6f, 0.09f, 0.06f), pink);
                Box(sign, new Vector3(s * 2.3f, 0f, 0.06f), new Vector3(0.09f, 1.59f, 0.06f), pink);
            }
            Box(sign, new Vector3(-0.3f, 0.32f, 0.06f), new Vector3(3.2f, 0.1f, 0.06f), cyan);
            Box(sign, new Vector3(0.1f, 0f, 0.06f), new Vector3(2.6f, 0.1f, 0.06f), cyan);
            Box(sign, new Vector3(0.5f, -0.32f, 0.06f), new Vector3(1.8f, 0.1f, 0.06f), cyan);

            var c = Group(root, "Cabinets", new Vector3(0f, 0f, -HZ));
            Box(c, new Vector3(0f, 0.45f, 0.4f), new Vector3(6.4f, 0.9f, 0.8f), dark);
            Box(c, new Vector3(0f, 0.93f, 0.42f), new Vector3(6.5f, 0.06f, 0.86f), steel);
            for (int k = -3; k <= 3; k++) Box(c, new Vector3(k * 0.91f, 0.45f, 0.81f), new Vector3(0.03f, 0.8f, 0.02f), steel);
            // Bloc moteur sur le plan de travail : carter, culasse rouge, quatre cornets
            Box(c, new Vector3(-1.8f, 1.24f, 0.42f), new Vector3(0.9f, 0.55f, 0.6f), steel);
            Box(c, new Vector3(-1.8f, 1.57f, 0.42f), new Vector3(0.8f, 0.1f, 0.36f), red);
            for (int k = 0; k < 4; k++) Cyl(c, new Vector3(-2.1f + k * 0.2f, 1.7f, 0.42f), 0.07f, 0.18f, dark);
            Box(c, new Vector3(1.5f, 1.16f, 0.4f), new Vector3(0.6f, 0.4f, 0.5f), wood);
            Box(c, new Vector3(2.35f, 1.09f, 0.45f), new Vector3(0.5f, 0.26f, 0.4f), blue);
            Cyl(c, new Vector3(0.4f, 1.12f, 0.35f), 0.12f, 0.32f, yellow);
            Cyl(c, new Vector3(0.72f, 1.09f, 0.5f), 0.1f, 0.26f, red);
        }

        /// <summary>Coins : piles de pneus, compresseur, fûts.</summary>
        void Corners()
        {
            var tires = Group(root, "Tires", new Vector3(-HX + 1.1f, 0f, -HZ + 1.3f));
            for (int k = 0; k < 4; k++) Tire(tires, new Vector3(0f, 0.135f + k * 0.27f, 0f));
            for (int k = 0; k < 2; k++) Tire(tires, new Vector3(0.85f, 0.135f + k * 0.27f, 0.25f));

            var comp = Group(root, "Compressor", new Vector3(HX - 1.3f, 0f, -HZ + 1.1f));
            Cyl(comp, new Vector3(0f, 0.55f, 0f), 0.32f, 1.2f, red, false, new Vector3(0f, 0f, 90f));
            foreach (float x in new[] { -0.4f, 0.4f }) Box(comp, new Vector3(x, 0.12f, 0f), new Vector3(0.08f, 0.24f, 0.5f), dark);
            Box(comp, new Vector3(0f, 1f, 0f), new Vector3(0.5f, 0.28f, 0.34f), dark);
            Cyl(comp, new Vector3(0.38f, 0.98f, 0f), 0.09f, 0.05f, steel, false, new Vector3(0f, 0f, 90f));

            Cyl(root, new Vector3(HX - 0.6f, 0.45f, HZ - 0.9f), 0.3f, 0.9f, blue);
            Cyl(root, new Vector3(HX - 1.35f, 0.45f, HZ - 0.7f), 0.3f, 0.9f, red);
            Cyl(root, new Vector3(-HX + 0.7f, 0.45f, HZ - 0.8f), 0.3f, 0.9f, yellow);
        }

        void Tire(Transform parent, Vector3 center)
        {
            Cyl(parent, center, 0.34f, 0.26f, rubber);
            Cyl(parent, center, 0.2f, 0.265f, steel);
        }
    }
}
