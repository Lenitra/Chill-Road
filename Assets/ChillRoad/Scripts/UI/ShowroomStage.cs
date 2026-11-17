using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Hall d'exposition de la concession : toute la gamme alignée dans un hall sombre, chaque voiture sur son podium
    /// tournant sous son projecteur, devant un mur de panneaux rétroéclairés. La caméra glisse le long de la rangée
    /// jusqu'à la voiture choisie, dont le podium tourne lentement (ou à la main : <see cref="Spin"/>).
    /// </summary>
    public class ShowroomStage : ShopStage
    {
        const float Spacing = 7f;          // d'un podium au suivant (m)
        const float PodiumTop = 0.3f;
        const float RestYaw = 145f;        // voitures au repos : de trois quarts, nez vers la caméra
        const float WallZ = 9f;
        const float SpotOn = 42f, SpotOff = 15f;
        static readonly Vector3 Origin = new Vector3(3000f, 2500f, -6000f);
        static readonly Vector3 GarageOrigin = new Vector3(6000f, 2500f, -6000f);
        static readonly Color Cyan = new Color(0.35f, 0.85f, 1f), Amber = new Color(1f, 0.76f, 0.2f);

        class Slot
        {
            public Transform table;
            public Light spot;
            public float x, yaw = RestYaw, dist = 9f, height = 1.3f;
        }

        readonly List<Slot> slots = new List<Slot>();
        Material floorMat, wallMat, panelMat, plinthMat, cyanGlow, amberGlow, podiumMat;
        int selected;
        /// <summary>Dessus des podiums (m) : les plateaux du garage privé sont presque au ras du sol.</summary>
        float top = PodiumTop;
        float camX, camVel, dist = 9f, distVel, lookY = 0.4f, lookVel, camHeight = 1.5f;

        /// <summary>
        /// Abaissement du point visé (fraction de la distance) : plus il est grand, plus la voiture monte dans l'image
        /// (l'interface du bas est plus haute).
        /// </summary>
        public float lookDrop = 0.07f;

        /// <summary>
        /// Construit le hall : order[k] = indice (dans le garage) de la voiture exposée à la place k. garage : le garage
        /// privé du joueur (garage-chalet à baie vitrée, <see cref="BuildGarage"/>), à l'écart du hall de la concession.
        /// </summary>
        public void Build(GameManager g, int[] order, bool garage = false)
        {
            if (garage)
            {
                BuildGarage(g, order);
                return;
            }
            lighting = new StudioLighting
            {
                keyRotation = Quaternion.Euler(52f, 22f, 0f),
                keyColor = new Color(1f, 0.97f, 0.93f),
                keyIntensity = 0.7f,
                sky = new Color(0.2f, 0.23f, 0.31f),
                equator = new Color(0.14f, 0.16f, 0.22f),
                ground = new Color(0.07f, 0.075f, 0.1f),
                fog = new Color(0.012f, 0.014f, 0.022f),
                fogDensity = 0.004f,
                bloomBoost = 0.25f,
            };
            // Hall du pack alpin (Tools/Blender/alp_shops.py) : clair, à la marque rouge de la concession
            bool pack = !garage && g.assets.alp && g.assets.alp.Prefab("showroom_bay_a");
            if (pack)
                lighting = new StudioLighting
                {
                    keyRotation = Quaternion.Euler(50f, 28f, 0f),
                    keyColor = new Color(1f, 0.97f, 0.92f),
                    keyIntensity = 1.15f,
                    sky = new Color(0.62f, 0.64f, 0.68f),
                    equator = new Color(0.5f, 0.5f, 0.52f),
                    ground = new Color(0.32f, 0.31f, 0.3f),
                    fog = new Color(0.86f, 0.87f, 0.88f),
                    fogDensity = -1f,
                    bloomBoost = 0.1f,
                };
            Create(g, garage ? "Garage" : "Showroom", garage ? GarageOrigin : Origin, lighting.fog);
            if (pack && BuildPack(order))
            {
                // Vitrines : le vrai paysage, vu depuis l'emplacement de la concession sur la carte
                var poi = g.DealerPoi;
                if (poi != null) viewPoint = new Vector3(poi.x, 0f, poi.z);
                Finish();
                return;
            }

            floorMat = Flat(new Color(0.1f, 0.105f, 0.125f));
            wallMat = Flat(new Color(0.065f, 0.075f, 0.105f));
            panelMat = Flat(new Color(0.15f, 0.175f, 0.235f));
            plinthMat = Flat(new Color(0.03f, 0.033f, 0.045f));
            cyanGlow = Glow(Cyan, 2.4f);
            amberGlow = Glow(Amber, 2.2f);
            var alp = g.assets.alp;
            if (alp && alp.material)
            {
                // Podiums du pack alpin, spots du biseau allumés en permanence
                podiumMat = Copy(alp.material);
                podiumMat.SetColor("_EmissionColor", Color.white * 1.9f);
            }

            int n = order.Length;
            const int margin = 3;           // travées vides au-delà des deux bouts de la rangée
            for (int k = -margin; k < n + margin; k++) Bay(k, k >= 0 && k < n);
            // Murs des deux bouts
            float left = (-margin - 0.5f) * Spacing, right = (n + margin - 0.5f) * Spacing;
            Box(root, new Vector3(left - 0.15f, 4.5f, -2.5f), new Vector3(0.3f, 9f, 23.6f), wallMat);
            Box(root, new Vector3(right + 0.15f, 4.5f, -2.5f), new Vector3(0.3f, 9f, 23.6f), wallMat);

            var cars = g.assets.playerCars;
            var podium = alp ? alp.Prefab("car_podium") : null;
            for (int k = 0; k < n; k++)
            {
                var slot = new Slot { x = k * Spacing };
                slot.table = Group(root, "Turntable", new Vector3(slot.x, 0f, 0f));
                if (!podium || !podiumMat || !PlacePack("car_podium", slot.table, Vector3.zero))
                {
                    Cyl(slot.table, new Vector3(0f, PodiumTop * 0.5f - 0.02f, 0f), 2.8f, PodiumTop - 0.04f, plinthMat);
                    Cyl(slot.table, new Vector3(0f, PodiumTop - 0.02f, 0f), 2.4f, 0.04f, panelMat);
                }
                PlaceCar(cars[order[k]], slot.table, new Vector3(0f, PodiumTop, 0f), out var b);
                float radius = Mathf.Max(b.extents.x, b.extents.z);
                slot.dist = radius * 2.6f + 3.4f;
                slot.height = b.size.y;
                slot.table.localRotation = Quaternion.Euler(0f, slot.yaw, 0f);
                slot.spot = Spot(new Vector3(slot.x, 7.6f, -3.2f), new Vector3(slot.x, 0.7f, 0f), new Color(1f, 0.96f, 0.9f), SpotOff, 17f, 62f);
                slots.Add(slot);
            }
            Finish();
        }

        /// <summary>
        /// Hall modélisé dans Blender : une travée du pack par place (vitrine, mur de marque, mur bois, en alternance),
        /// deux travées de plus à chaque bout, murs de bout (accueil, entrée), podiums du pack, spots sous le plafond.
        /// </summary>
        bool BuildPack(int[] order)
        {
            if (!HasPack("showroom_bay_a")) return false;
            string[] bays = { "showroom_bay_a", "showroom_bay_b", "showroom_bay_c" };
            int n = order.Length;
            const int margin = 2;
            for (int k = -margin; k < n + margin; k++)
                PlacePack(bays[((k % 3) + 3) % 3], root, new Vector3(k * Spacing, 0f, 0f));
            float left = (-margin - 0.5f) * Spacing, right = (n + margin - 0.5f) * Spacing;
            PlacePack("showroom_end", root, new Vector3(left, 0f, 0f));
            PlacePack("showroom_end", root, new Vector3(right, 0f, 0f), mirror: true);

            var cars = game.assets.playerCars;
            for (int k = 0; k < n; k++)
            {
                var slot = new Slot { x = k * Spacing };
                slot.table = Group(root, "Turntable", new Vector3(slot.x, 0f, 0f));
                if (!PlacePack("car_podium", slot.table, Vector3.zero))
                    Cyl(slot.table, new Vector3(0f, PodiumTop * 0.5f, 0f), 2.8f, PodiumTop, Flat(new Color(0.85f, 0.86f, 0.88f)));
                PlaceCar(cars[order[k]], slot.table, new Vector3(0f, PodiumTop, 0f), out var b);
                float radius = Mathf.Max(b.extents.x, b.extents.z);
                slot.dist = radius * 2.6f + 3.4f;
                slot.height = b.size.y;
                slot.table.localRotation = Quaternion.Euler(0f, slot.yaw, 0f);
                slot.spot = Spot(new Vector3(slot.x, 6.2f, -3.2f), new Vector3(slot.x, 0.7f, 0f), new Color(1f, 0.96f, 0.9f), SpotOff, 15f, 62f);
                slots.Add(slot);
            }
            return true;
        }

        // ------------------------------------------------------------------ garage privé

        const float GarageTop = 0.11f;     // plateaux tournants presque au ras du béton
        const float SillY = 0.9f, HeadY = 4.6f, Walk = 5.2f;
        Material concrete, walkway, paint, hazard, timber, pine, slate, deck, rubber, rim, steel, red, log, needles, pot;

        /// <summary>
        /// Garage privé du joueur, un chalet-garage : dalle de béton clair aux places peintes, plateaux tournants
        /// encastrés, une grande baie vitrée entre des poteaux de mélèze sombre qui donne sur le vrai paysage (le lac vu
        /// depuis le hameau des Rives), et entre deux places le bric-à-brac d'un garage de montagne (pneus, skis, servante,
        /// bûches, sapin en pot).
        /// </summary>
        void BuildGarage(GameManager g, int[] order)
        {
            lighting = new StudioLighting
            {
                keyRotation = Quaternion.Euler(48f, 30f, 0f),
                keyColor = new Color(1f, 0.95f, 0.87f),
                keyIntensity = 1.05f,
                sky = new Color(0.58f, 0.63f, 0.7f),
                equator = new Color(0.5f, 0.48f, 0.45f),
                ground = new Color(0.3f, 0.27f, 0.24f),
                fog = new Color(0.66f, 0.78f, 0.88f),   // fond (sans carte) : ciel pâle derrière la baie
                fogDensity = 0f,
                bloomBoost = 0.12f,
            };
            Create(g, "Garage", GarageOrigin, lighting.fog);
            top = GarageTop;
            if (g.Map != null && g.Map.Json.pois != null)
                foreach (var poi in g.Map.Json.pois)
                    if (poi.id == "h_rives") viewPoint = new Vector3(poi.x, 0f, poi.z);
            viewHeading = 285f;   // le lac, le viaduc et les sommets enneigés au fond
            viewLift = 30f;
            viewTilt = 9f;        // l'horizon et le ciel dans la baie, pas seulement le versant d'en face

            concrete = Flat(new Color(0.6f, 0.59f, 0.56f), 0.35f, 0.1f);
            walkway = Flat(new Color(0.5f, 0.49f, 0.47f));
            paint = Flat(new Color(0.93f, 0.92f, 0.88f));
            hazard = Flat(new Color(0.98f, 0.72f, 0.16f));
            timber = Flat(new Color(0.3f, 0.2f, 0.14f));
            pine = Flat(new Color(0.8f, 0.63f, 0.42f));
            slate = Flat(new Color(0.19f, 0.2f, 0.23f), 0.2f, 0.05f);
            deck = Flat(new Color(0.3f, 0.31f, 0.34f), 0.15f, 0.02f);
            rubber = Flat(new Color(0.07f, 0.075f, 0.085f));
            rim = Flat(new Color(0.62f, 0.64f, 0.68f), 0.4f, 0.3f);
            steel = Flat(new Color(0.36f, 0.38f, 0.42f), 0.4f, 0.25f);
            red = Flat(new Color(0.78f, 0.12f, 0.1f), 0.3f, 0.2f);
            log = Flat(new Color(0.45f, 0.3f, 0.18f));
            needles = Flat(new Color(0.2f, 0.42f, 0.27f));
            pot = Flat(new Color(0.72f, 0.4f, 0.26f));
            var ledAmber = Glow(Amber, 2.6f);
            var ledCyan = Glow(Cyan, 2.2f);

            int n = order.Length;
            const int margin = 2;
            float left = (-margin - 0.5f) * Spacing, right = (n + margin - 0.5f) * Spacing;
            float mid = (left + right) * 0.5f, width = right - left;

            // Dalle, allée surélevée le long de la baie (bord jaune), places peintes
            Box(root, new Vector3(mid, -0.1f, -2f), new Vector3(width, 0.2f, 24f), concrete);
            Box(root, new Vector3(mid, 0.04f, (Walk + WallZ) * 0.5f), new Vector3(width, 0.08f, WallZ - Walk), walkway);
            Box(root, new Vector3(mid, 0.081f, Walk + 0.09f), new Vector3(width, 0.004f, 0.16f), hazard);
            Box(root, new Vector3(mid, 0.004f, Walk - 0.45f), new Vector3(width, 0.008f, 0.1f), paint);
            for (int k = -margin; k <= n + margin; k++)
                Box(root, new Vector3((k - 0.5f) * Spacing, 0.004f, 0.3f), new Vector3(0.1f, 0.008f, 9.1f), paint);

            // Baie vitrée : allège et appui, linteau, poteaux à chaque place ; filet ambré sous l'appui
            Box(root, new Vector3(mid, SillY * 0.5f, WallZ + 0.15f), new Vector3(width, SillY, 0.3f), timber);
            Box(root, new Vector3(mid, SillY + 0.03f, WallZ - 0.05f), new Vector3(width, 0.06f, 0.5f), pine);
            Box(root, new Vector3(mid, SillY - 0.05f, WallZ - 0.29f), new Vector3(width, 0.03f, 0.03f), ledAmber);
            Box(root, new Vector3(mid, HeadY + 0.35f, WallZ), new Vector3(width, 0.7f, 0.5f), timber);
            Box(root, new Vector3(mid, HeadY + 2.7f, WallZ + 0.15f), new Vector3(width, 4f, 0.3f), pine);
            Box(root, new Vector3(mid, HeadY - 0.04f, WallZ - 0.26f), new Vector3(width, 0.03f, 0.03f), ledCyan);
            for (int k = -margin; k <= n + margin; k++)
            {
                float x = (k - 0.5f) * Spacing;
                Box(root, new Vector3(x, (SillY + HeadY) * 0.5f, WallZ), new Vector3(0.42f, HeadY - SillY, 0.46f), timber);
                // Meneaux fins au quart et aux trois quarts de chaque vitrage : la baie reste lisible comme une vitre
                if (k == n + margin) continue;
                foreach (float f in new[] { 0.25f, 0.75f })
                    Box(root, new Vector3(x + Spacing * f, (SillY + HeadY) * 0.5f, WallZ + 0.05f), new Vector3(0.07f, HeadY - SillY, 0.08f), slate);
            }

            // Murs de bout en lambris de pin, plinthe sombre
            foreach (float x in new[] { left - 0.15f, right + 0.15f })
            {
                Box(root, new Vector3(x, 4f, -1.5f), new Vector3(0.3f, 8f, 21.4f), pine);
                Box(root, new Vector3(x + (x < mid ? 0.17f : -0.17f), 0.2f, -1.5f), new Vector3(0.06f, 0.4f, 21.4f), timber);
            }

            // Entre deux places, contre la baie : un objet de garage de montagne
            for (int k = -margin + 1; k < n + margin; k++)
            {
                var spot = Group(root, "Prop", new Vector3((k - 0.5f) * Spacing, 0.08f, WallZ - 1.05f));
                switch (((k % 5) + 5) % 5)
                {
                    case 0: Tires(spot); break;
                    case 1: Skis(spot); break;
                    case 2: Chest(spot); break;
                    case 3: Logs(spot); break;
                    default: Fir(spot); break;
                }
            }

            var cars = g.assets.playerCars;
            for (int k = 0; k < n; k++)
            {
                var slot = new Slot { x = k * Spacing };
                slot.table = Group(root, "Turntable", new Vector3(slot.x, 0f, 0f));
                // Plateau d'ardoise cerclé d'un filet ambré, au ras de la dalle
                Cyl(slot.table, new Vector3(0f, 0.015f, 0f), 2.86f, 0.03f, ledAmber);
                Cyl(slot.table, new Vector3(0f, GarageTop * 0.5f, 0f), 2.78f, GarageTop - 0.01f, slate);
                Cyl(slot.table, new Vector3(0f, GarageTop - 0.005f, 0f), 2.5f, 0.012f, deck);
                PlaceCar(cars[order[k]], slot.table, new Vector3(0f, GarageTop, 0f), out var b);
                float radius = Mathf.Max(b.extents.x, b.extents.z);
                slot.dist = radius * 2.6f + 3.4f;
                slot.height = b.size.y;
                slot.table.localRotation = Quaternion.Euler(0f, slot.yaw, 0f);
                slot.spot = Spot(new Vector3(slot.x, 6.4f, -3.2f), new Vector3(slot.x, 0.6f, 0f), new Color(1f, 0.95f, 0.86f), SpotOff, 15f, 62f);
                slots.Add(slot);
            }
            Finish();
        }

        /// <summary>Pile de quatre pneus (le dernier à plat, jante grise).</summary>
        void Tires(Transform t)
        {
            for (int i = 0; i < 4; i++)
                Cyl(t, new Vector3(Mathf.Sin(i * 2.1f) * 0.04f, 0.12f + i * 0.24f, 0f), 0.36f, 0.23f, rubber, true);
            Cyl(t, new Vector3(0f, 0.97f, 0f), 0.2f, 0.02f, rim);
        }

        /// <summary>Râtelier de bois et quatre paires de skis de couleur debout contre la baie.</summary>
        void Skis(Transform t)
        {
            Box(t, new Vector3(0f, 0.08f, 0.12f), new Vector3(1.5f, 0.16f, 0.36f), pine);
            Box(t, new Vector3(0f, 1.35f, 0.32f), new Vector3(1.5f, 0.08f, 0.1f), pine);
            Color[] colors = { new Color(0.85f, 0.18f, 0.14f), new Color(0.98f, 0.76f, 0.2f), new Color(0.3f, 0.75f, 0.95f), new Color(0.93f, 0.93f, 0.9f) };
            for (int i = 0; i < 4; i++)
            {
                var m = Flat(colors[i], 0.4f, 0.3f);
                float x = -0.54f + i * 0.36f;
                Box(t, new Vector3(x - 0.05f, 0.95f, 0.22f), new Vector3(0.08f, 1.75f, 0.03f), m, true, new Vector3(8f, 0f, 0f));
                Box(t, new Vector3(x + 0.05f, 0.95f, 0.22f), new Vector3(0.08f, 1.75f, 0.03f), m, true, new Vector3(8f, 0f, 0f));
            }
        }

        /// <summary>Servante d'atelier rouge : tiroirs, poignées, plateau d'acier, roulettes.</summary>
        void Chest(Transform t)
        {
            Box(t, new Vector3(0f, 0.6f, 0f), new Vector3(1.1f, 0.96f, 0.55f), red, true);
            Box(t, new Vector3(0f, 1.1f, 0f), new Vector3(1.16f, 0.05f, 0.6f), steel);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.24f + i * 0.22f;
                Box(t, new Vector3(0f, y - 0.1f, -0.28f), new Vector3(1.1f, 0.02f, 0.01f), rubber);
                Box(t, new Vector3(0f, y, -0.29f), new Vector3(0.4f, 0.03f, 0.02f), rim);
            }
            foreach (float x in new[] { -0.45f, 0.45f })
                Cyl(t, new Vector3(x, 0.06f, 0f), 0.06f, 0.05f, rubber, false, new Vector3(0f, 0f, 90f));
        }

        /// <summary>Tas de bûches dans un cadre de bois.</summary>
        void Logs(Transform t)
        {
            Box(t, new Vector3(-0.78f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.5f), timber);
            Box(t, new Vector3(0.78f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.5f), timber);
            Box(t, new Vector3(0f, 0.03f, 0f), new Vector3(1.64f, 0.06f, 0.5f), timber);
            for (int row = 0; row < 5; row++)
                for (int i = 0; i < 7; i++)
                {
                    float x = -0.6f + i * 0.2f + (row % 2) * 0.05f;
                    if (x > 0.66f) continue;
                    Cyl(t, new Vector3(x, 0.15f + row * 0.19f, 0f), 0.095f, 0.46f, log, false, new Vector3(90f, 0f, 0f));
                }
        }

        /// <summary>Petit sapin en pot : trois étages de branches.</summary>
        void Fir(Transform t)
        {
            Cyl(t, new Vector3(0f, 0.25f, 0f), 0.34f, 0.5f, pot, true);
            Cyl(t, new Vector3(0f, 0.75f, 0f), 0.08f, 0.5f, log);
            Cyl(t, new Vector3(0f, 0.85f, 0f), 0.6f, 0.4f, needles, true);
            Cyl(t, new Vector3(0f, 1.3f, 0f), 0.45f, 0.4f, needles, true);
            Cyl(t, new Vector3(0f, 1.72f, 0f), 0.28f, 0.38f, needles, true);
            Cyl(t, new Vector3(0f, 2.05f, 0f), 0.12f, 0.3f, needles, true);
        }

        /// <summary>Une travée du hall : dalle, mur du fond, filets lumineux ; derrière une voiture, un panneau rétroéclairé.</summary>
        void Bay(int k, bool car)
        {
            float x = k * Spacing;
            var bay = Group(root, "Bay", new Vector3(x, 0f, 0f));
            Box(bay, new Vector3(0f, -0.1f, -2.5f), new Vector3(Spacing, 0.2f, 23f), floorMat);
            Box(bay, new Vector3(0f, 4.5f, WallZ + 0.15f), new Vector3(Spacing, 9f, 0.3f), wallMat);
            Box(bay, new Vector3(0f, 0.2f, WallZ - 0.04f), new Vector3(Spacing, 0.4f, 0.08f), plinthMat);
            // Filet vertical entre deux travées, lignes au sol devant et derrière les podiums
            Box(bay, new Vector3(-Spacing * 0.5f, 3.5f, WallZ - 0.03f), new Vector3(0.1f, 6.2f, 0.06f), cyanGlow);
            Box(bay, new Vector3(0f, 0.008f, -3.8f), new Vector3(Spacing, 0.016f, 0.07f), cyanGlow);
            Box(bay, new Vector3(0f, 0.008f, 3.8f), new Vector3(Spacing, 0.016f, 0.05f), amberGlow);
            if (!car) return;
            Box(bay, new Vector3(0f, 2.55f, WallZ - 0.03f), new Vector3(5.3f, 3.2f, 0.06f), panelMat);
            Box(bay, new Vector3(0f, 4.24f, WallZ - 0.05f), new Vector3(5.3f, 0.07f, 0.06f), amberGlow);
            Box(bay, new Vector3(0f, 0.86f, WallZ - 0.05f), new Vector3(5.3f, 0.05f, 0.06f), cyanGlow);
        }

        /// <summary>Voiture regardée (place dans la rangée). snap : la caméra y est tout de suite (ouverture du hall).</summary>
        public void Select(int slot, bool snap)
        {
            if (slots.Count == 0) return;
            selected = Mathf.Clamp(slot, 0, slots.Count - 1);
            if (!snap) return;
            var s = slots[selected];
            camX = s.x;
            dist = s.dist;
            lookY = LookY(s);
            camVel = distVel = lookVel = 0f;
            camHeight = 1.5f;
            foreach (var o in slots)
            {
                o.yaw = RestYaw;
                o.spot.intensity = o == s ? SpotOn : SpotOff;
            }
        }

        /// <summary>Fait tourner le podium de la voiture regardée (degrés) ; la caméra monte ou descend (m).</summary>
        public void Spin(float degrees, float lift)
        {
            if (slots.Count == 0) return;
            slots[selected].yaw += degrees;
            camHeight = Mathf.Clamp(camHeight + lift, 0.8f, 3.4f);
        }

        /// <summary>Point visé : sous la voiture, qui se retrouve ainsi dans la moitié haute de l'image (le bas porte l'interface).</summary>
        float LookY(Slot s) => top + s.height * 0.5f - s.dist * lookDrop;

        /// <summary>
        /// Vignette d'une voiture de la rangée, de trois quarts avant, rendue une fois dans une texture. Le décor doit
        /// être affiché (<see cref="ShopStage.Show"/>) depuis au moins une image : son éclairage de studio est en place.
        /// </summary>
        public void RenderThumb(int slot, RenderTexture target)
        {
            if (slot < 0 || slot >= slots.Count || !target || !cam) return;
            var s = slots[slot];
            float spot = s.spot.intensity;
            s.spot.intensity = SpotOn;
            s.table.localRotation = Quaternion.Euler(0f, RestYaw, 0f);
            var t = cam.transform;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            float fov = cam.fieldOfView;
            var rt = cam.targetTexture;
            float radius = (s.dist - 3.4f) / 2.6f;
            float d = radius * 2f + 1.6f;
            Vector3 eye = root.position + new Vector3(s.x - 0.2f, top + 1.35f, -d);
            Vector3 look = root.position + new Vector3(s.x, top + s.height * 0.42f, 0f);
            t.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = 26f;
            cam.targetTexture = target;
            cam.ResetAspect();
            cam.Render();
            cam.targetTexture = rt;
            cam.ResetAspect();
            cam.fieldOfView = fov;
            t.SetPositionAndRotation(pos, rot);
            s.table.localRotation = Quaternion.Euler(0f, s.yaw, 0f);
            s.spot.intensity = spot;
        }

        protected override void Tick(float dt)
        {
            if (slots.Count == 0) return;
            var s = slots[selected];
            for (int i = 0; i < slots.Count; i++)
            {
                var o = slots[i];
                bool on = i == selected;
                if (on) o.yaw += dt * 13f;
                else o.yaw = Mathf.LerpAngle(o.yaw, RestYaw, 1f - Mathf.Exp(-dt * 3f));
                o.table.localRotation = Quaternion.Euler(0f, o.yaw, 0f);
                o.spot.intensity = Mathf.MoveTowards(o.spot.intensity, on ? SpotOn + punch * 4f : SpotOff, dt * 90f);
            }
            camX = Mathf.SmoothDamp(camX, s.x, ref camVel, 0.3f, Mathf.Infinity, dt);
            dist = Mathf.SmoothDamp(dist, s.dist, ref distVel, 0.35f, Mathf.Infinity, dt);
            lookY = Mathf.SmoothDamp(lookY, LookY(s), ref lookVel, 0.35f, Mathf.Infinity, dt);
            Vector3 pos = root.position + new Vector3(camX, camHeight, -dist);
            Vector3 look = root.position + new Vector3(camX + camVel * 0.02f, lookY, 0f);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            cam.fieldOfView = 38f - punch;
        }
    }
}
