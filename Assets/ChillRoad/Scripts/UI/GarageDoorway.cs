using System;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Porte d'un garage (concession, tuning) : on y entre en voiture, sans touche. La voiture qui franchit le marquage au
    /// sol à allure modérée est prise en main, roule vers la porte et le menu s'ouvre ; en quittant le menu, elle ressort
    /// toute seule par la même porte et le joueur reprend le volant quelques mètres plus loin.
    /// La porte est donnée dans le repère du bâtiment, posé sur la carte (<see cref="MapData.PlaceJson"/>).
    /// </summary>
    public class GarageDoorway
    {
        enum Mode { Idle, RollIn, RollOut }

        const float MaxEnterSpeed = 17f, HalfWidth = 3.4f, Depth = 8f, RollInTime = 0.8f;
        const float ExitStart = 3f, ExitLength = 13f, ExitTime = 4.2f, ExitSpeed = 6f, HintRadius = 45f, RearmDistance = 13f, MarkerRange = 160f;

        readonly GameManager game;
        readonly string item;
        readonly Vector3 localDoor;
        readonly Color tint;
        bool searched, found, armed = true;
        Vector3 door, outward, right, exitFrom;
        Mode mode;
        float timer;
        MeshRenderer marker;
        Material markerMat;

        /// <summary>La voiture est aux abords du garage : le HUD indique d'entrer par la porte.</summary>
        public bool Near { get; private set; }
        /// <summary>La voiture entre ou sort toute seule.</summary>
        public bool Busy => mode != Mode.Idle;

        public GarageDoorway(GameManager game, string item, Vector3 localDoor, Color tint)
        {
            this.game = game;
            this.item = item;
            this.localDoor = localDoor;
            this.tint = tint;
        }

        bool Resolve()
        {
            if (searched) return found;
            if (game.Map == null || game.Map.Json == null || game.Map.Json.places == null) return false;
            searched = true;
            foreach (var p in game.Map.Json.places)
            {
                if (p.item != item) continue;
                var rot = Quaternion.Euler(0f, p.yaw, 0f);
                door = new Vector3(p.x, p.y, p.z) + rot * localDoor;
                door.y = game.Map.Height(door.x, door.z);
                outward = rot * Vector3.forward;
                right = rot * Vector3.right;
                found = true;
                BuildMarker();
                break;
            }
            return found;
        }

        /// <summary>À appeler chaque image, menu fermé. allowed : la voiture peut entrer ; open : ouvre le menu.</summary>
        public void Tick(bool allowed, Action open)
        {
            Near = false;
            if (!Resolve() || !game.Player) { ShowMarker(false); return; }
            var car = game.Player;
            float dt = Time.deltaTime;
            Vector3 rel = car.transform.position - door;
            rel.y = 0f;
            switch (mode)
            {
                case Mode.RollIn:
                    timer += dt;
                    Drive(car, door - outward * 4f - car.transform.position, 5f, 0f);
                    if (timer >= RollInTime || Vector3.Dot(rel, outward) < 1.5f)
                    {
                        Release(car);
                        mode = Mode.Idle;
                        armed = false;
                        open();
                    }
                    break;
                case Mode.RollOut:
                    timer += dt;
                    float gone = Vector3.Dot(car.transform.position - exitFrom, outward);
                    Drive(car, outward * 12f + right * -Vector3.Dot(rel, right), gone > ExitLength - 4f ? ExitSpeed * 0.6f : ExitSpeed, 0f);
                    if (gone >= ExitLength || timer >= ExitTime)
                    {
                        Release(car);
                        game.SetInputEnabled(true);
                        mode = Mode.Idle;
                    }
                    break;
                default:
                    float dist = rel.magnitude;
                    if (!armed && dist > RearmDistance) armed = true;
                    Near = allowed && dist < HintRadius;
                    float along = Vector3.Dot(rel, outward), side = Vector3.Dot(rel, right);
                    if (armed && allowed && car.InputEnabled && along > -1f && along < Depth && Mathf.Abs(side) < HalfWidth
                        && car.Speed < MaxEnterSpeed && Vector3.Dot(car.Velocity, -outward) > 0.8f)
                    {
                        game.SetInputEnabled(false);
                        car.Autopilot = true;
                        timer = 0f;
                        mode = Mode.RollIn;
                    }
                    break;
            }
            ShowMarker(mode == Mode.Idle && allowed && armed && rel.sqrMagnitude < MarkerRange * MarkerRange);
        }

        /// <summary>Le menu vient de se fermer : la voiture repart de la porte, vers l'extérieur.</summary>
        public void Leave()
        {
            var car = game.Player;
            if (!Resolve() || !car)
            {
                game.SetInputEnabled(true);
                return;
            }
            exitFrom = door + outward * ExitStart;
            exitFrom.y = game.Map.Height(exitFrom.x, exitFrom.z) + 0.35f;
            game.SetInputEnabled(false);
            car.Teleport(exitFrom, Quaternion.LookRotation(outward, Vector3.up), ExitSpeed * 0.7f);
            car.Autopilot = true;
            timer = 0f;
            armed = false;
            mode = Mode.RollOut;
        }

        /// <summary>Volant, accélérateur et frein vers une direction, à la vitesse demandée.</summary>
        static void Drive(VehicleController car, Vector3 dir, float speed, float minBrake)
        {
            dir.y = 0f;
            float angle = Vector3.SignedAngle(Flat(car.transform.forward), dir, Vector3.up);
            car.AutoSteer = Mathf.Clamp(angle / 26f, -1f, 1f);
            float gap = speed - car.Speed;
            car.AutoThrottle = gap > 0f ? Mathf.Clamp01(0.25f + gap * 0.2f) : 0f;
            car.AutoBrake = gap < -0.5f ? Mathf.Clamp01(-gap * 0.18f) : minBrake;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static void Release(VehicleController car)
        {
            car.Autopilot = false;
            car.AutoThrottle = car.AutoBrake = car.AutoSteer = 0f;
        }

        // ------------------------------------------------------------------ marquage au sol

        void ShowMarker(bool show)
        {
            if (!marker) return;
            marker.enabled = show;
            if (!show) return;
            float pulse = 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * 3f);
            var c = tint * (1.3f * pulse);
            c.a = 1f;
            markerMat.SetColor("_BaseColor", c);
        }

        /// <summary>Cadre et chevrons posés au sol devant la porte, pointés vers elle.</summary>
        void BuildMarker()
        {
            var spark = game.assets ? game.assets.spark : null;
            if (!spark) return;
            var mb = new RoadNetwork.MeshBuilder();
            void Quad(float s0, float a0, float s1, float a1, float s2, float a2, float s3, float a3)
            {
                mb.Quad(Ground(s0, a0), Ground(s1, a1), Ground(s3, a3), Ground(s2, a2));
            }
            const float w = HalfWidth, t = 0.16f, near = 0.6f, far = Depth;
            Quad(-w, near, w, near, w, near + t, -w, near + t);
            Quad(-w, far - t, w, far - t, w, far, -w, far);
            Quad(-w, near, -w + t, near, -w + t, far, -w, far);
            Quad(w - t, near, w, near, w, far, w - t, far);
            for (int k = 0; k < 3; k++)
            {
                float a = 2f + k * 1.8f;
                Quad(-1.5f, a + 1f, 0f, a, 0f, a + 0.42f, -1.5f, a + 1.42f);
                Quad(0f, a, 1.5f, a + 1f, 1.5f, a + 1.42f, 0f, a + 0.42f);
            }
            markerMat = new Material(spark) { name = "GarageMarker (runtime)" };
            if (markerMat.HasProperty("_BaseMap")) markerMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (markerMat.HasProperty("_Cull")) markerMat.SetFloat("_Cull", 0f);
            var go = new GameObject("GarageMarker_" + item, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mb.Build("GarageMarker", Color.white);
            marker = go.GetComponent<MeshRenderer>();
            marker.sharedMaterial = markerMat;
            marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.receiveShadows = false;
        }

        Vector3 Ground(float side, float along)
        {
            Vector3 p = door + right * side + outward * along;
            p.y = game.Map.Height(p.x, p.z) + 0.07f;
            return p;
        }
    }
}
