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
        float camX, camVel, dist = 9f, distVel, lookY = 0.4f, lookVel, camHeight = 1.5f;

        /// <summary>Construit le hall : order[k] = indice (dans le garage) de la voiture exposée à la place k.</summary>
        public void Build(GameManager g, int[] order)
        {
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
            bool pack = g.assets.alp && g.assets.alp.Prefab("showroom_bay_a");
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
            Create(g, "Showroom", Origin, lighting.fog);
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
        static float LookY(Slot s) => PodiumTop + s.height * 0.5f - s.dist * 0.07f;

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
