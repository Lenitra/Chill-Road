using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Caméra de poursuite : suit la voiture avec un léger retard, s'élargit avec la vitesse, tremble à très haute vitesse.
    /// Vues : poursuite proche, poursuite loin, capot, pare-chocs. Vue arrière maintenue.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        struct View
        {
            public string name;
            public Vector3 offset;       // position relative (x droite, y haut, z avant)
            public Vector3 lookOffset;   // point visé relatif
            public float follow;         // raideur de suivi (0 = rigide)
            public float baseFov;
        }

        static readonly View[] Views =
        {
            new View { name = "Poursuite",  offset = new Vector3(0f, 2.0f, -6.2f), lookOffset = new Vector3(0f, 1.1f, 4f), follow = 6f,  baseFov = 60f },
            new View { name = "Poursuite loin", offset = new Vector3(0f, 2.8f, -9.5f), lookOffset = new Vector3(0f, 1.2f, 5f), follow = 5f, baseFov = 58f },
            new View { name = "Capot",      offset = new Vector3(0f, 1.35f, 0.4f),  lookOffset = new Vector3(0f, 1.2f, 20f), follow = 0f,  baseFov = 68f },
            new View { name = "Pare-chocs", offset = new Vector3(0f, 0.55f, 2.2f),  lookOffset = new Vector3(0f, 0.6f, 20f), follow = 0f,  baseFov = 70f },
        };

        public VehicleController target;
        /// <summary>Écran d'accueil : la caméra tourne lentement autour de la voiture.</summary>
        public bool Showcase { get; set; }
        /// <summary>Caméra pilotée par la réalisation du mode démo (<see cref="DemoCamera"/>) : le rig ne la place plus.</summary>
        public bool Directed { get; set; }
        float showcaseAngle;
        public int ViewIndex { get; private set; }
        public string ViewName => Views[ViewIndex].name;

        Camera cam;
        Quaternion smoothedRot;
        Vector3 smoothedPos;
        float trauma;
        bool initialized;
        static readonly RaycastHit[] blockHits = new RaycastHit[24];
        float clearFraction = 1f;

        /// <summary>
        /// Position de caméra dégagée entre la voiture et la position voulue : si le relief, une route ou un bâtiment
        /// coupe le chemin, la caméra s'arrête juste devant (vite), puis recule doucement quand la voie se libère.
        /// Les poteaux et les troncs (capsules) et les véhicules ne comptent pas : ils la feraient sauter sans cesse.
        /// </summary>
        Vector3 Unobstructed(Vector3 pivot, Vector3 wanted, float dt)
        {
            Vector3 d = wanted - pivot;
            float dist = d.magnitude;
            if (dist < 0.05f) return wanted;
            Vector3 dir = d / dist;
            const float radius = 0.45f;
            float free = dist;
            int n = Physics.SphereCastNonAlloc(pivot, radius, dir, blockHits, dist, ~(1 << 2), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = blockHits[i];
                if (h.distance <= 0f || h.rigidbody || h.collider is CapsuleCollider) continue;
                free = Mathf.Min(free, h.distance);
            }
            float target = Mathf.Clamp(free / dist, 0.2f, 1f);
            clearFraction = target < clearFraction ? Mathf.Lerp(clearFraction, target, 1f - Mathf.Exp(-dt * 30f))
                : Mathf.MoveTowards(clearFraction, target, dt * 1.4f);
            Vector3 p = pivot + dir * (dist * clearFraction);
            // Jamais sous le terrain (là où il n'a pas encore de collision)
            var road = RoadNetwork.Instance;
            if (road && road.Map != null) p.y = Mathf.Max(p.y, road.Map.Height(p.x, p.z) + 0.55f);
            return p;
        }
        // Regard libre (joystick droit / souris) : angles actuels et visés, en degrés
        float orbitYaw, orbitPitch, mouseYaw, mousePitch;
        // Transition de départ : trajectoire autour de la voiture, puis coup de focale au « GO »
        float introT = -1f, introLen = 1f, fovKick, fovBase = 60f, rawFov = 60f;
        Vector3 introFrom;

        void Awake() => cam = GetComponent<Camera>();

        /// <summary>Champ de vision vertical (degrés).</summary>
        void SetFov(float degrees)
        {
            rawFov = degrees;
            if (cam) cam.fieldOfView = degrees;
        }

        public void NextView() { ViewIndex = (ViewIndex + 1) % Views.Length; initialized = false; }
        public void SetView(int i) { ViewIndex = Mathf.Clamp(i, 0, Views.Length - 1); initialized = false; }
        public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        /// <summary>Coup de focale (degrés), qui retombe en une fraction de seconde.</summary>
        public void Kick(float degrees) => fovKick = Mathf.Max(fovKick, degrees);

        /// <summary>
        /// Transition de départ : depuis la vue d'accueil, plongée devant la calandre (téléobjectif), passage rasant
        /// sur le flanc (grand angle, caméra inclinée), puis arrivée derrière la voiture dans la vue de jeu.
        /// </summary>
        public void PlayIntro(float duration)
        {
            var t = target.transform;
            Quaternion frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up), Vector3.up);
            // Départ : la position actuelle (vue tournante), ou une vue de trois quarts avant si l'accueil a été sauté
            introFrom = Showcase ? Quaternion.Inverse(frame) * (transform.position - t.position) : new Vector3(4.5f, 1.6f, 6.5f);
            Showcase = false;
            introLen = Mathf.Max(0.5f, duration);
            introT = 0f;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        void UpdateIntro(float dt)
        {
            introT += dt;
            float t = Mathf.Clamp01(introT / introLen);
            float e = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
            var view = Views[ViewIndex];
            // Côté gauche : la chaussée (à droite, le bas-côté et ses obstacles)
            Vector3[] keys = { introFrom, new Vector3(-2.3f, 0.5f, 5.2f), new Vector3(-3.6f, 0.75f, -0.8f), view.offset };
            float seg = e * 3f;
            int i = Mathf.Min(2, (int)seg);
            float u = seg - i;
            Vector3 local = CatmullRom(keys[Mathf.Max(i - 1, 0)], keys[i], keys[i + 1], keys[Mathf.Min(i + 2, 3)], u);

            var car = target.transform;
            Quaternion frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(car.forward, Vector3.up), Vector3.up);
            Vector3 pos = car.position + frame * local;
            Vector3 lookCar = car.position + Vector3.up * 0.7f;
            Vector3 lookGame = car.position + frame * view.lookOffset;
            Vector3 look = Vector3.Lerp(lookCar, lookGame, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, e)));
            float roll = Mathf.Sin(e * Mathf.PI) * 9f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
            SetFov(e < 0.33f ? Mathf.Lerp(42f, 30f, e / 0.33f)
                    : e < 0.66f ? Mathf.Lerp(30f, 82f, (e - 0.33f) / 0.33f)
                    : Mathf.Lerp(82f, view.baseFov, (e - 0.66f) / 0.34f));
            if (t >= 1f)
            {
                introT = -1f;
                initialized = false;
            }
        }

        /// <summary>
        /// Regard libre façon Forza : le joystick droit indique la direction à regarder autour de la voiture
        /// (haut = devant, côtés = 90°, bas = derrière), la caméra y revient en douceur une fois relâché.
        /// À la souris : bouton droit maintenu et déplacement.
        /// </summary>
        void UpdateOrbit(float dt, bool chase)
        {
            Vector2 stick = GameInput.LookStick;
            float targetYaw = 0f, targetPitch = 0f;
            if (stick != Vector2.zero)
            {
                float amount = Mathf.Clamp01((stick.magnitude - 0.18f) / 0.6f);
                targetYaw = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg * amount;
                // Un peu plus haut en regardant derrière et sur les côtés : on voit la route
                targetPitch = chase ? Mathf.Abs(targetYaw) / 180f * 6f : 0f;
            }
            if (GameInput.MouseLookHeld)
            {
                Vector2 d = GameInput.MouseDelta;
                mouseYaw = Mathf.Clamp(mouseYaw + d.x * 0.25f, -180f, 180f);
                mousePitch = Mathf.Clamp(mousePitch - d.y * 0.2f, -10f, 35f);
                targetYaw = mouseYaw;
                targetPitch = chase ? mousePitch : Mathf.Clamp(mousePitch, -20f, 20f);
            }
            else { mouseYaw = 0f; mousePitch = 0f; }
            if (!chase) targetYaw = Mathf.Clamp(targetYaw, -110f, 110f);
            // Rotation rapide vers la cible, retour un peu plus doux
            bool returning = Mathf.Abs(targetYaw) < Mathf.Abs(orbitYaw);
            float k = 1f - Mathf.Exp(-dt * (returning ? 6f : 12f));
            orbitYaw = Mathf.LerpAngle(orbitYaw, targetYaw, k);
            orbitPitch = Mathf.Lerp(orbitPitch, targetPitch, k);
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.unscaledDeltaTime;
            if (introT >= 0f) { UpdateIntro(dt); return; }
            if (Directed) return;
            if (Showcase)
            {
                showcaseAngle += dt * 7f;
                Vector3 center = target.transform.position + Vector3.up * 0.6f;
                Quaternion around = Quaternion.LookRotation(Vector3.ProjectOnPlane(target.transform.forward, Vector3.up), Vector3.up) * Quaternion.Euler(0f, 150f + showcaseAngle, 0f);
                Vector3 camPos = center + around * new Vector3(0f, 0.8f, -6.8f);
                transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(center + Vector3.up * 0.2f - camPos, Vector3.up));
                SetFov(42f);
                initialized = false;
                return;
            }
            var v = Views[ViewIndex];
            // Vues embarquées réglées par voiture (sinon positions par défaut)
            var spec = target.Spec;
            if (spec != null && ViewIndex == 2 && spec.hoodCamera != Vector3.zero)
            {
                v.offset = spec.hoodCamera;
                v.lookOffset = spec.hoodCamera + new Vector3(0f, -0.15f, 20f);
            }
            else if (spec != null && ViewIndex == 3 && spec.bumperCamera != Vector3.zero)
            {
                v.offset = spec.bumperCamera;
                v.lookOffset = spec.bumperCamera + new Vector3(0f, 0.05f, 20f);
            }
            Transform t = target.transform;
            float speed = target.Speed;

            // Référence de rotation : le lacet suit la trajectoire à vitesse (le drift se voit de côté), sans roulis
            Vector3 vel = target.Velocity;
            Vector3 fwd = t.forward;
            if (vel.sqrMagnitude > 25f && v.follow > 0f) fwd = Vector3.Slerp(fwd, vel.normalized, 0.5f);
            fwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
            if (GameInput.LookBack && v.follow > 0f) fwd = -fwd;
            Quaternion yaw = Quaternion.LookRotation(fwd, Vector3.up);
            // Tangage : suit la pente de la route
            float pitch = Vector3.SignedAngle(Vector3.ProjectOnPlane(t.forward, t.right), Vector3.ProjectOnPlane(fwd, t.right), t.right);
            Quaternion frame = v.follow > 0f ? yaw * Quaternion.Euler(-pitch * 0.6f, 0f, 0f) : t.rotation;

            if (!initialized) { smoothedRot = frame; smoothedPos = t.position; initialized = true; fovBase = rawFov; }
            UpdateOrbit(dt, v.follow > 0f);
            if (v.follow > 0f)
            {
                smoothedRot = Quaternion.Slerp(smoothedRot, frame, 1f - Mathf.Exp(-dt * v.follow));
                smoothedPos = Vector3.Lerp(smoothedPos, t.position, 1f - Mathf.Exp(-dt * 18f));
            }
            else { smoothedRot = frame; smoothedPos = t.position; }

            // Recul de la caméra avec la vitesse (sensation d'aspiration)
            float speedFactor = Mathf.Clamp01(speed / 80f);
            Vector3 offset = v.offset;
            if (v.follow > 0f) offset.z -= speedFactor * 1.2f;
            // Vues poursuite : la caméra tourne autour de la voiture ; vues embarquées : on tourne la tête
            Quaternion orbit = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 pos, look;
            if (v.follow > 0f)
            {
                Quaternion r = smoothedRot * orbit;
                pos = smoothedPos + r * offset;
                look = smoothedPos + r * v.lookOffset;
                // Ne descend pas sous la voiture en regardant de côté
                pos.y = Mathf.Max(pos.y, smoothedPos.y + 0.8f);
                // Ni dans un talus, ni dans une façade : la caméra se rapproche de la voiture plutôt que de traverser
                pos = Unobstructed(smoothedPos + Vector3.up * 1.15f, pos, dt);
            }
            else
            {
                pos = smoothedPos + smoothedRot * offset;
                look = pos + smoothedRot * (orbit * (v.lookOffset - offset));
            }

            // Tremblement : léger à très haute vitesse + chocs
            float shake = trauma * trauma + Mathf.Clamp01((speed - 55f) / 30f) * 0.04f;
            trauma = Mathf.MoveTowards(trauma, 0f, dt * 1.5f);
            float n = Time.time * 22f;
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(n, 0.1f) - 0.5f, Mathf.PerlinNoise(0.4f, n) - 0.5f, 0f) * shake * 0.6f;

            transform.position = pos + smoothedRot * jitter;
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            fovKick = Mathf.MoveTowards(fovKick, 0f, dt * 22f);
            fovBase = Mathf.Lerp(fovBase, v.baseFov + speedFactor * 18f, 1f - Mathf.Exp(-dt * 4f));
            SetFov(fovBase + fovKick);
        }
    }
}
