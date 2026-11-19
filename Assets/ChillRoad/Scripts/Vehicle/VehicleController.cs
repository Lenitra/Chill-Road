using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Voiture en unités réelles, physique de simulation (pas de couple « magique ») :
    /// - suspension par rayons (ressort + amortisseur + barres antiroulis) : le transfert de charge est réel ;
    /// - pneus : glissement combiné (dérive + patinage/blocage dans une même enveloppe d'adhérence), pic vers 7° puis
    ///   chute vers ~80 % en pleine glisse, sensibilité à la charge ; plus progressifs sur la terre ;
    /// - roues en rotation : les roues motrices (pont autobloquant) sont liées au moteur et patinent quand le couple
    ///   dépasse l'adhérence ; le régime suit les roues (rupteur en glisse) ; le frein à main bloque l'arrière et débraye,
    ///   le rembrayage moteur lancé fait patiner (coup d'embrayage) ;
    /// - moteur à courbe de couple, frein moteur, boîte auto 6 rapports avec coupure de couple, marche arrière ;
    /// - aérodynamique (traînée + appui), résistance au roulement ;
    /// - assistances (réglage unique) : direction assistée (stick = dérive des pneus avant autour de la trajectoire),
    ///   ABS, antipatinage (coupé en glisse), ESP qui, comme un vrai, freine la roue avant extérieure et coupe les gaz
    ///   quand la voiture part en tête-à-queue (permissif à basse vitesse, strict à haute vitesse).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        const float AirDensity = 1.225f;
        const int SubSteps = 4;

        [Header("Suspension")]
        public float suspensionTravel = 0.3f;
        [Tooltip("Enfoncement statique (m) : définit la raideur des ressorts")] public float staticSag = 0.055f;
        [Range(0.1f, 1f)] public float dampingRatio = 0.6f;
        [Tooltip("Raideur des barres antiroulis relative aux ressorts")] public float antiRoll = 1.6f;
        [Tooltip("Hauteur d'application des forces de pneu au-dessus du sol (centre de roulis) : réduit roulis et tangage")] public float rollCenterHeight = 0.38f;
        [Tooltip("Aide qui garde la caisse à plat (couple de rappel)")] public float uprightAssist = 6f;

        [Header("Pneus")]
        [Tooltip("Glissement au pic d'adhérence sur le goudron (tangente de la dérive : 0,13 ≈ 7,5°)")] public float peakSlip = 0.13f;
        [Tooltip("Glissement au pic sur la terre : les pneus creusent, le pic arrive plus tard")] public float peakSlipDirt = 0.18f;
        [Tooltip("Adhérence restante en pleine glisse, relative au pic (goudron)")] public float slideGrip = 0.8f;
        [Tooltip("Adhérence restante en pleine glisse sur la terre (plus progressive)")] public float slideGripDirt = 0.8f;
        [Tooltip("Largeur de la chute d'adhérence après le pic (en multiples du glissement au pic)")] public float gripFalloff = 1.6f;
        [Tooltip("Sensibilité à la charge : une roue très chargée adhère proportionnellement moins")] public float loadSensitivity = 0.12f;
        [Tooltip("Adhérence arrière relative (>1 = voiture stable, léger sous-virage)")] public float rearGripBias = 1.13f;
        public float offRoadGrip = 0.65f;
        [Tooltip("Force du frein à main (fraction du poids) : assez pour bloquer les roues arrière")] public float handbrakeForce = 0.9f;
        [Tooltip("Puissance des freins relative (l'ABS limite toujours chaque roue à son adhérence)")] public float brakePower = 1.45f;

        [Header("Transmission")]
        [Tooltip("Inertie d'une roue avec son disque (kg·m²)")] public float wheelInertia = 1.0f;
        [Tooltip("Inertie du moteur et du volant moteur (kg·m²)")] public float engineInertia = 0.13f;
        [Tooltip("Frein moteur pied levé (fraction du couple max)")] public float engineBraking = 0.1f;

        [Header("Châssis")]
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.3f, 0.05f);
        [Tooltip("Inertie en lacet relative : plus élevée = la caisse met plus de temps à pivoter (sensation de poids)")] public float yawInertia = 1.35f;
        [Tooltip("Stabilisation en l'air (0 = aucune) : la caisse reste à plat et suit sa trajectoire pendant un saut")] public float airStability = 1f;

        [Header("Direction")]
        [Tooltip("Temps de réponse du volant (s) à basse et à haute vitesse : l'inertie de la direction")]
        public float steerResponseLow = 0.07f, steerResponseHigh = 0.22f;
        [Tooltip("Dérive maximale demandée aux pneus avant (degrés), à basse et à haute vitesse")]
        public float maxFrontSlipLow = 13f, maxFrontSlipHigh = 3.6f;

        [Header("Assistances")]
        [Range(0f, 1f)] public float assist = 0.6f;

        public VehicleSpec Spec { get; private set; }
        public Rigidbody Body => rb;
        public bool InputEnabled { get; set; } = true;
        /// <summary>
        /// Freins serrés tant que les commandes sont coupées : en réseau, le monde continue quand un menu est ouvert, et
        /// la voiture ne doit pas partir en roue libre dans la pente.
        /// </summary>
        public bool Parked { get; set; }
        public float Speed { get; private set; }
        public float SpeedKmh => Speed * 3.6f;
        public float ForwardSpeed { get; private set; }
        public int Gear { get; private set; } = 1;
        public float Rpm { get; private set; } = 900f;
        public float Rpm01 => Spec == null ? 0f : Mathf.InverseLerp(IdleRpm, Spec.redline, Rpm);
        public bool Shifting => shiftTimer > 0f;
        public float SlipAngle { get; private set; }
        /// <summary>Patinage des roues motrices (0-1).</summary>
        public float WheelSpin { get; private set; }
        public float SkidAmount { get; private set; }
        public bool HandbrakeOn { get; private set; }
        /// <summary>Intervention de l'ESP (0-1) : freinage d'une roue et coupure des gaz pour éviter le tête-à-queue.</summary>
        public float EspIntervention { get; private set; }
        /// <summary>Intervention de l'antipatinage (0-1).</summary>
        public float TcsIntervention { get; private set; }
        public bool OnRoad { get; set; } = true;
        /// <summary>Adhérence relative de la route sous la voiture (1 = goudron, moins sur la terre).</summary>
        public float RoadGrip { get; set; } = 1f;
        /// <summary>Part de terre sous la voiture (0 = goudron, 1 = chemin de terre).</summary>
        public float SurfaceDirt { get; set; }
        /// <summary>Neige sous la voiture (0-1) et enfoncement des roues dans la neige (m).</summary>
        public float SurfaceSnow { get; set; }
        public float SnowSink { get; set; }
        /// <summary>Route mouillée (0-1, fournie par la météo) : moins d'adhérence, glisse plus franche, aquaplaning à haute vitesse.</summary>
        public float Wetness { get; set; }
        public int GroundedWheels { get; private set; }
        public float Throttle => throttle;
        /// <summary>
        /// Pilote automatique (départ lancé) : ces commandes remplacent celles du joueur quand elles sont coupées.
        /// </summary>
        public bool Autopilot { get; set; }
        public float AutoThrottle, AutoBrake, AutoSteer;
        float InThrottle => InputEnabled ? GameInput.Throttle : Autopilot ? AutoThrottle : 0f;
        float InBrake => InputEnabled ? GameInput.Brake : Autopilot ? AutoBrake : 0f;
        /// <summary>Frein demandé (joueur ou pilote automatique), pour les feux stop.</summary>
        public float BrakeInput => Remote ? remoteBrake : InBrake;
        float InSteer => InputEnabled ? GameInput.Steer : Autopilot ? AutoSteer : 0f;
        public float SteerAngle => steerAngle;
        public Vector3 Velocity => Remote ? remoteVelocity : rb ? rb.linearVelocity : Vector3.zero;
        /// <summary>
        /// Voiture d'un autre joueur (partie en réseau) : aucune physique ici, son état arrive par le réseau
        /// (<see cref="ApplyRemote"/>) et elle est déplacée comme un corps cinématique.
        /// </summary>
        public bool Remote { get; private set; }
        /// <summary>Nombre de téléportations (départ, replacement) : transmis en réseau pour que la voiture saute au lieu de glisser.</summary>
        public int TeleportCount { get; private set; }
        /// <summary>Glisse la plus forte des roues avant et arrière (0-1), transmise en réseau pour la fumée et les traces.</summary>
        public float SkidFront { get; private set; }
        public float SkidRear { get; private set; }
        Vector3 remoteVelocity;
        float remoteBrake;
        bool remoteGrounded;
        public Bounds LocalBounds { get; private set; }
        /// <summary>Sorties d'échappement du modèle (repères « exhaust-* », +Z local dans le sens du flux). Vide : pas de pot.</summary>
        public IReadOnlyList<Transform> Exhausts => exhausts;
        public IReadOnlyList<Transform> WheelVisuals => wheelVisuals;
        /// <summary>Nombre de roues (4, ou 3 pour un tricycle comme la Peel P50).</summary>
        public int WheelCount => wheels.Count;

        public event Action<int> GearChanged;
        public event Action<Collision, float> Impacted;

        const float IdleRpm = 900f;

        class Wheel
        {
            public Vector3 mount;          // point d'ancrage local (en haut de la course)
            public bool front, left;
            public bool center;            // roue seule sur son essieu (tricycle)
            public bool driven;
            public float springK, damperC; // selon la part de charge statique de la roue
            public float brakeShare;       // part de la force de freinage totale
            public float nominalLoad;      // charge statique (sensibilité à la charge)
            public Wheel mate;             // roue opposée du même essieu (barre antiroulis), aucune pour une roue centrale
            public Transform visual;
            public float radius = 0.34f;
            public float compression, prevCompression;
            public bool grounded;
            public RaycastHit hit;
            public float load;
            public float spin;             // angle de rotation du modèle (degrés)
            public float omegaR;           // vitesse périphérique de la roue (m/s) : ω·r
            public bool locked;
            public float slip;             // tangente de la dérive
            public float skid;             // intensité de glisse 0-1 (fumée, traces)
            // Contact du pas en cours
            public Vector3 wf, wr;
            public float vLong, vLat, denom, cap, brakeF;
            public bool abs;
            public float fx, fy;           // forces moyennes sur les sous-pas
            // Frein de stationnement : point du sol où le pneu s'est posé à l'arrêt
            public bool held;
            public Vector3 anchor;
        }

        /// <summary>
        /// Frein de stationnement : déplacement (m) du pneu par rapport à son point d'arrêt pour lequel il retient
        /// autant que le poids qu'il porte. Il tient ainsi la pente au millimètre près, sans à-coup.
        /// </summary>
        const float HoldStretch = 0.02f;

        Rigidbody rb;
        Transform model;
        /// <summary>Modèle affiché (peinture : <see cref="CarPaint.Apply"/>).</summary>
        public Transform Model => model;
        readonly List<Wheel> wheels = new List<Wheel>();
        readonly List<Transform> wheelVisuals = new List<Transform>();
        readonly List<Transform> exhausts = new List<Transform>();
        float frontAxleZ;
        float throttle, brake, steerInput, steerVelocity, steerAngle;
        float shiftTimer, shiftCooldown;
        int layerMask = ~0;
        // Transmission : vitesse périphérique des roues motrices (pont), embrayage, pédale du pas
        float driveW, drivePedalNow, tcsNow, espSide;
        bool clutchOut;
        // Pneus : glissement au pic et adhérence en glisse de la surface courante
        float sPeak = 0.13f, slideMu = 0.8f;
        // Réglages de la voiture montée : suspension et pneus choisis au garage (CarTuning) appliqués aux réglages de base
        float travel = 0.3f, sag = 0.055f, arb = 1.6f;
        CarTuning.TireProfile tire;
        CarTuning.SuspensionProfile susp;
        float lastSlipAngle, slipRate;
        float airTime;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.maxAngularVelocity = 10f;
            // Les rayons de suspension ignorent la voiture elle-même
            layerMask = ~(1 << gameObject.layer);
        }

        // ------------------------------------------------------------------ configuration

        public void Setup(VehicleSpec spec)
        {
            Spec = spec;
            tire = CarTuning.TireOf(spec);
            susp = CarTuning.SuspensionOf(spec);
            travel = suspensionTravel * susp.travel;
            sag = staticSag * susp.sag;
            arb = antiRoll * susp.arb;
            if (model) { model.gameObject.SetActive(false); Destroy(model.gameObject); }
            var pivot = new GameObject("Model").transform;
            pivot.SetParent(transform, false);
            var go = Instantiate(spec.prefab, pivot);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * spec.modelScale;
            model = pivot;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);

            // Roues du modèle (wheel-front-left, wheel-back-center...) et sorties d'échappement (exhaust-*)
            wheelVisuals.Clear();
            exhausts.Clear();
            foreach (var t in go.GetComponentsInChildren<Transform>())
            {
                if (t.name.StartsWith("exhaust")) { exhausts.Add(t); continue; }
                if (t.name.StartsWith("wheel")) wheelVisuals.Add(t);
            }

            // Encombrement de la caisse (hors roues) → collider. Calculé dans le repère de la voiture à partir des
            // maillages : juste même si la voiture est tournée (changement de voiture en pleine course)
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            Bounds body = new Bounds();
            bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || mf.transform.name.StartsWith("wheel")) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (first) { body = new Bounds(p, Vector3.zero); first = false; } else body.Encapsulate(p);
                }
            }
            var box = GetComponent<BoxCollider>();
            if (!box) box = gameObject.AddComponent<BoxCollider>();
            Vector3 c0 = body.center;
            Vector3 size = body.size;
            // Le bas de caisse est relevé : seules les roues touchent le sol
            float bottom = c0.y - size.y * 0.5f + 0.25f;
            float top = c0.y + size.y * 0.5f;
            box.center = new Vector3(c0.x, (bottom + top) * 0.5f, c0.z);
            box.size = new Vector3(size.x * 0.95f, top - bottom, size.z * 0.97f);
            LocalBounds = new Bounds(c0, size);

            // Roues du modèle : 4, ou 3 pour un tricycle (« center » = roue seule dans l'axe) ; sinon 4 roues par défaut
            wheels.Clear();
            if (wheelVisuals.Count >= 3)
            {
                foreach (var t in wheelVisuals)
                    AddWheel(transform.InverseTransformPoint(t.position), t.name.Contains("front"), t.name.Contains("left"),
                             t.name.Contains("center"), t);
            }
            else
            {
                float hx = size.x * 0.4f, hz = size.z * 0.33f;
                AddWheel(new Vector3(-hx, 0.34f, hz), true, true, false, null);
                AddWheel(new Vector3(hx, 0.34f, hz), true, false, false, null);
                AddWheel(new Vector3(-hx, 0.34f, -hz), false, true, false, null);
                AddWheel(new Vector3(hx, 0.34f, -hz), false, false, false, null);
            }

            // Masse, centre de gravité bas (entre les essieux), inertie d'une caisse de voiture
            rb.mass = spec.mass;
            float zf = 0f, yf = 0f, zr = 0f;
            int nf = 0, nr = 0;
            foreach (var w in wheels)
            {
                if (w.front) { zf += w.mount.z; yf += w.mount.y; nf++; }
                else { zr += w.mount.z; nr++; }
            }
            zf /= Mathf.Max(1, nf);
            yf /= Mathf.Max(1, nf);
            zr /= Mathf.Max(1, nr);
            frontAxleZ = zf;
            rb.centerOfMass = new Vector3(0f, yf - travel * 0.2f, (zf + zr) * 0.5f) + centerOfMassOffset;
            float L = Mathf.Max(3.8f, size.z), Wd = Mathf.Max(1.7f, size.x), H = 1.3f;
            rb.inertiaTensor = new Vector3(spec.mass / 12f * (H * H + L * L), spec.mass / 12f * (Wd * Wd + L * L) * 1.1f * yawInertia, spec.mass / 12f * (Wd * Wd + H * H));
            rb.inertiaTensorRotation = Quaternion.identity;

            // Moitié de la masse sur chaque essieu, partagée entre ses roues (une roue arrière seule porte le double) ;
            // freinage 64/36 avant/arrière réparti de même ; barre antiroulis entre les deux roues d'un essieu
            foreach (var w in wheels)
            {
                int onAxle = Mathf.Max(1, w.front ? nf : nr);
                float cornerMass = spec.mass * 0.5f / onAxle;
                w.springK = cornerMass * 9.81f / sag;
                w.damperC = 2f * dampingRatio * susp.damp * Mathf.Sqrt(w.springK * cornerMass);
                w.brakeShare = (w.front ? 0.64f : 0.36f) / onAxle;
                w.nominalLoad = cornerMass * 9.81f;
                w.driven = IsDriven(w.front);
                w.mate = null;
                if (w.center) continue;
                foreach (var o in wheels)
                    if (o != w && !o.center && o.front == w.front && o.left != w.left &&
                        (w.mate == null || Mathf.Abs(o.mount.z - w.mount.z) < Mathf.Abs(w.mate.mount.z - w.mount.z)))
                        w.mate = o;
            }
            Gear = 1;
            Rpm = IdleRpm;
            // Changement de voiture en roulant : les roues tournent déjà à la vitesse de la voiture
            float roll = Vector3.Dot(rb.linearVelocity, transform.forward);
            driveW = roll;
            foreach (var w in wheels) w.omegaR = roll;
            clutchOut = false;
        }

        void AddWheel(Vector3 center, bool front, bool left, bool centered, Transform visual)
        {
            var w = new Wheel { front = front, left = left, center = centered, visual = visual, radius = Spec.wheelRadius };
            if (visual)
            {
                // Rayon d'après le maillage de la roue (juste même si la voiture est penchée au changement de voiture)
                var mf = visual.GetComponentInChildren<MeshFilter>();
                if (mf && mf.sharedMesh) w.radius = Mathf.Max(0.1f, mf.sharedMesh.bounds.extents.y * mf.transform.lossyScale.y);
            }
            // L'ancrage est au-dessus du centre de roue : à l'enfoncement statique la roue retombe à sa place (la caisse
            // garde sa hauteur quelle que soit la suspension)
            w.mount = center + Vector3.up * (travel - sag);
            w.compression = w.prevCompression = sag;
            wheels.Add(w);
        }

        public void Teleport(Vector3 pos, Quaternion rot, float speed = 0f)
        {
            TeleportCount++;
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            rb.linearVelocity = rot * Vector3.forward * speed;
            rb.angularVelocity = Vector3.zero;
            steerAngle = 0f;
            steerInput = 0f;
            steerVelocity = 0f;
            // Roues qui roulent déjà à la vitesse de la voiture, moteur embrayé
            driveW = speed;
            foreach (var w in wheels) { w.omegaR = speed; w.held = false; }
            clutchOut = false;
            lastSlipAngle = slipRate = 0f;
            // Lancé : 4e (ou le dernier rapport des boîtes plus courtes, ex. 3 rapports de la Peel P50)
            Gear = speed > 20f ? Mathf.Min(4, Spec != null ? Spec.gearRatios.Length : 4) : 1;
            if (Spec != null && Gear >= 1) Rpm = Mathf.Clamp(RpmForSpeed(speed, Gear), IdleRpm, Spec.redline);
        }

        // ------------------------------------------------------------------ réseau

        /// <summary>Fait de cette voiture celle d'un autre joueur : corps cinématique, sans commandes ni physique.</summary>
        public void SetRemote()
        {
            Remote = true;
            InputEnabled = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
        }

        /// <summary>État reçu du réseau (voiture d'un autre joueur) : ce que lisent les feux, les effets, la caméra et le trafic.</summary>
        public void ApplyRemote(in NetCarState s, Vector3 velocity)
        {
            remoteVelocity = velocity;
            Speed = velocity.magnitude;
            ForwardSpeed = Vector3.Dot(velocity, transform.forward);
            if (Spec != null) Rpm = Mathf.Lerp(IdleRpm, Spec.redline, s.rpm01);
            throttle = s.throttle;
            steerAngle = s.steer;
            remoteBrake = s.braking ? 1f : 0f;
            HandbrakeOn = s.handbrake;
            OnRoad = s.onRoad;
            SurfaceDirt = s.dirt;
            remoteGrounded = s.grounded;
            GroundedWheels = s.grounded ? wheels.Count : 0;
            SkidAmount = s.skid;
            SkidFront = s.skidFront;
            SkidRear = s.skidRear;
            foreach (var w in wheels) w.omegaR = HandbrakeOn && !w.front ? 0f : ForwardSpeed;
            int gear = s.gear == 0 ? 1 : s.gear;
            if (gear == Gear) return;
            bool up = gear > Gear && Gear >= 1;
            Gear = gear;
            if (up) GearChanged?.Invoke(gear);
        }

        // ------------------------------------------------------------------ moteur

        static float TorqueCurve(float rpm01)
        {
            // Monte jusqu'à ~60 % du régime, plateau puis chute douce vers la zone rouge
            if (rpm01 < 0.6f) return Mathf.Lerp(0.55f, 1f, rpm01 / 0.6f);
            return Mathf.Lerp(1f, 0.8f, (rpm01 - 0.6f) / 0.4f);
        }

        float RpmForSpeed(float speed, int gear) =>
            speed / Spec.wheelRadius * Spec.gearRatios[gear - 1] * Spec.finalDrive * 60f / (2f * Mathf.PI);

        /// <summary>Démultiplication totale du rapport engagé (marche arrière : celle de la 1re).</summary>
        float TotalRatio => Spec.gearRatios[Mathf.Max(1, Gear) - 1] * Spec.finalDrive;

        void UpdateGearbox(float vz, float dt)
        {
            shiftCooldown -= dt;
            if (shiftTimer > 0f) shiftTimer -= dt;
            float rawThrottle = InThrottle;
            float rawBrake = InBrake;

            // Marche arrière : à l'arrêt, frein maintenu
            if (Gear >= 1 && vz < 0.5f && rawBrake > 0.3f && rawThrottle < 0.1f) Gear = -1;
            else if (Gear == -1 && rawThrottle > 0.1f && vz > -0.5f) Gear = 1;

            float target;
            if (Gear >= 1)
            {
                // Passages selon la vitesse de la voiture (pas celle des roues) : un patinage ou un drift ne fait pas
                // passer le rapport supérieur, on reste sur le rapport… et sur le rupteur si on insiste
                float gv = vz > 0f ? Mathf.Max(vz, Speed) : vz;
                int n = Spec.gearRatios.Length;
                if (shiftCooldown <= 0f && !clutchOut)
                {
                    if (Gear < n && RpmForSpeed(Mathf.Max(0f, gv), Gear) > Spec.redline * 0.94f && rawThrottle > 0.2f) Shift(Gear + 1);
                    else if (Gear > 1 && RpmForSpeed(gv, Gear - 1) < Spec.redline * (rawThrottle > 0.6f ? 0.72f : 0.55f)) Shift(Gear - 1);
                }
                // Régime : lié aux roues motrices ; embrayage qui patine au démarrage
                target = RpmForSpeed(Mathf.Max(0f, driveW), Gear);
                if (vz < 8f) target = Mathf.Max(target, Mathf.Lerp(IdleRpm, Spec.redline * 0.55f, rawThrottle));
                target = Mathf.Max(target, IdleRpm);
            }
            else target = Mathf.Clamp(RpmForSpeed(Mathf.Max(0f, -driveW), 1), IdleRpm, Spec.redline);
            // Débrayé (frein à main) : le moteur tourne librement, il monte avec les gaz
            if (clutchOut) target = Mathf.Lerp(IdleRpm * 1.1f, Spec.redline * 0.97f, rawThrottle);
            Rpm = Mathf.Lerp(Rpm, Mathf.Min(target, Spec.redline * 1.02f), 1f - Mathf.Exp(-dt * (clutchOut ? 7f : 16f)));
        }

        void Shift(int gear)
        {
            Gear = gear;
            shiftTimer = 0.16f;
            shiftCooldown = 0.45f;
            GearChanged?.Invoke(gear);
        }

        /// <summary>
        /// Force motrice au sol (pour tout le pont) à une vitesse de roue donnée : couple moteur au régime correspondant
        /// (embrayage qui patine au démarrage), rupteur, frein moteur pied levé, antipatinage et coupure de l'ESP.
        /// </summary>
        float DriveAt(float W, float vRef)
        {
            float pedal = drivePedalNow;
            if (Gear == -1)
                return vRef < -12f ? 0f : -Spec.maxTorque * Spec.gearRatios[0] * Spec.finalDrive / Spec.wheelRadius * 0.9f * 0.45f * pedal;
            float ratio = TotalRatio;
            float rpm = W / Spec.wheelRadius * ratio * 60f / (2f * Mathf.PI);
            if (vRef < 8f) rpm = Mathf.Max(rpm, Mathf.Lerp(IdleRpm, Spec.redline * 0.55f, pedal));
            rpm = Mathf.Max(rpm, IdleRpm);
            float torque = Spec.maxTorque * TorqueCurve(rpm / Spec.redline) * pedal;
            // Rupteur : coupure franche au régime maxi (sans à-coup numérique)
            torque *= Mathf.Clamp01((Spec.redline - rpm) / (Spec.redline * 0.015f));
            // Frein moteur pied levé
            torque -= Spec.maxTorque * engineBraking * (1f - pedal) * Mathf.Clamp01((rpm - IdleRpm * 1.3f) / Spec.redline);
            float force = torque * ratio / Spec.wheelRadius * 0.9f;
            if (force > 0f)
            {
                // Antipatinage : réduit le couple dès que les roues patinent au-delà du pic d'adhérence
                float spin = (W - vRef) / Mathf.Max(Mathf.Abs(vRef), 3f);
                force *= 1f - tcsNow * Mathf.Clamp01((spin - sPeak * 1.1f) / sPeak);
                force *= 1f - EspIntervention * 0.85f;
            }
            return force;
        }

        /// <summary>
        /// Rembrayage après le frein à main : le moteur lancé et les roues motrices s'accordent d'un coup (conservation du
        /// moment cinétique). Moteur haut dans les tours → les roues patinent : le « coup d'embrayage » des drifteurs.
        /// </summary>
        void ClutchKick()
        {
            if (Gear < 1) return;
            float r = Spec.wheelRadius, ratio = TotalRatio;
            int n = 0;
            foreach (var w in wheels) if (w.driven) n++;
            float mw = n * wheelInertia / (r * r);
            float me = engineInertia * ratio * ratio / (r * r);
            float engineW = Rpm * 2f * Mathf.PI / 60f / ratio * r;
            driveW = (mw * driveW + me * engineW) / (mw + me);
        }

        // ------------------------------------------------------------------ pneus

        /// <summary>
        /// Adhérence relative selon le glissement normalisé (1 = pic) : montée quasi linéaire, pic, puis chute douce
        /// vers l'adhérence de glisse.
        /// </summary>
        float Grip(float x)
        {
            if (x <= 1f) return x * (2f - x);
            float d = (x - 1f) / gripFalloff;
            return slideMu + (1f - slideMu) / (1f + d * d);
        }

        /// <summary>
        /// Force d'un pneu (repère roue) pour une vitesse de roue W : le glissement combiné (patinage/blocage + dérive)
        /// donne une seule adhérence, orientée à l'opposé du glissement du contact (ellipse de friction naturelle).
        /// </summary>
        void TireForce(Wheel w, float W, out float fx, out float fy)
        {
            float sx = (W - w.vLong) / w.denom, sy = w.vLat / w.denom;
            float s = Mathf.Sqrt(sx * sx + sy * sy);
            if (s < 1e-6f) { fx = fy = 0f; return; }
            float f = Grip(s / sPeak) * w.cap / s;
            fx = f * sx;
            fy = -f * sy;
        }

        float GroupForce(bool drivenGroup, Wheel single, float W, bool accumulate)
        {
            float sum = 0f;
            foreach (var w in wheels)
            {
                if ((drivenGroup ? !w.driven : w != single) || !w.grounded) continue;
                TireForce(w, W, out float fx, out float fy);
                sum += fx;
                if (accumulate) { w.fx += fx / SubSteps; w.fy += fy / SubSteps; }
            }
            return sum;
        }

        /// <summary>
        /// Rotation d'un groupe de roues liées (le pont moteur, ou une roue libre) pendant un pas, en sous-pas implicites
        /// (stable même pour une roue légère) : force motrice, frein (frottement sec : il arrête la roue sans jamais
        /// l'inverser), ABS, réaction des pneus. Les forces des pneus sont moyennées sur les sous-pas.
        /// </summary>
        float SpinWheels(bool drivenGroup, Wheel single, float W, float mass, bool driveOn, float dt)
        {
            float h = dt / SubSteps;
            float brakeF = 0f, vRef = 0f;
            bool abs = false;
            int n = 0;
            foreach (var w in wheels)
            {
                if (drivenGroup ? !w.driven : w != single) continue;
                w.fx = w.fy = 0f;
                brakeF += w.brakeF;
                abs |= w.abs;
                if (w.grounded) { vRef += w.vLong; n++; }
            }
            vRef = n > 0 ? vRef / n : W;
            const float eps = 0.05f;
            for (int k = 0; k < SubSteps; k++)
            {
                float drive = driveOn ? DriveAt(W, vRef) : 0f;
                float tire = GroupForce(drivenGroup, single, W, false);
                float slope = (GroupForce(drivenGroup, single, W + eps, false) - tire) / eps;
                if (driveOn) slope -= (DriveAt(W + eps, vRef) - drive) / eps;
                float m = mass + h * Mathf.Max(0f, slope);
                W += h * (drive - tire) / m;
                W = Mathf.MoveTowards(W, 0f, h * brakeF / m);
                // ABS : la roue freinée ne descend pas sous le glissement du pic d'adhérence
                if (abs && n > 0 && Mathf.Abs(vRef) > 1f)
                {
                    float floor = Mathf.Abs(vRef) - sPeak * 0.9f * Mathf.Max(Mathf.Abs(vRef), 3f);
                    if (Mathf.Abs(W) < floor) W = Mathf.Sign(vRef) * floor;
                }
                GroupForce(drivenGroup, single, W, true);
            }
            return W;
        }

        bool IsDriven(bool front) => Spec.drivetrain == Drivetrain.AWD || (Spec.drivetrain == Drivetrain.RWD ? !front : front);

        // ------------------------------------------------------------------ physique

        void FixedUpdate()
        {
            if (Spec == null || Remote) return;
            float dt = Time.fixedDeltaTime;
            float rawThrottle = InThrottle;
            float rawBrake = InBrake;
            HandbrakeOn = InputEnabled && GameInput.Handbrake;
            throttle = Mathf.MoveTowards(throttle, rawThrottle, dt * 5f);
            brake = Mathf.MoveTowards(brake, rawBrake, dt * 7f);

            Vector3 up = transform.up, fwd = transform.forward, right = transform.right;
            Vector3 v = rb.linearVelocity;
            preStepVelocity = v;
            preStepAngular = rb.angularVelocity;
            float vz = Vector3.Dot(v, fwd), vx = Vector3.Dot(v, right);
            Speed = v.magnitude;
            ForwardSpeed = vz;

            // Surface : pic d'adhérence plus tardif et glisse plus progressive sur la terre
            float dirt = OnRoad ? SurfaceDirt : 1f;
            // Pneus montés : pic plus ou moins progressif, adhérence gardée en glisse
            sPeak = Mathf.Lerp(peakSlip, peakSlipDirt, dirt) * tire.peak;
            slideMu = Mathf.Lerp(slideGrip, slideGripDirt, dirt) * Mathf.Lerp(1f, 0.92f, Wetness) * tire.slide;

            // Embrayage : débrayé au frein à main quand il bloque les roues motrices (comme un pilote) ;
            // au relâchement, le moteur lancé entraîne d'un coup les roues (coup d'embrayage)
            bool declutch = HandbrakeOn && IsDriven(false);
            if (clutchOut && !declutch) ClutchKick();
            clutchOut = declutch;

            UpdateGearbox(vz, dt);
            bool reverse = Gear == -1;
            float drivePedal = reverse ? brake : throttle;
            float brakePedal = reverse ? throttle : brake;
            // Voiture laissée seule (menu ouvert pendant une partie en réseau) : freins serrés, quel que soit le rapport
            bool parked = Parked && !InputEnabled && !Autopilot;
            if (parked)
            {
                drivePedal = 0f;
                brakePedal = 1f;
            }
            drivePedalNow = drivePedal;

            float beta = Speed > 3f ? Mathf.Atan2(vx, Mathf.Abs(vz) < 0.01f ? 0.01f : vz) * Mathf.Rad2Deg : 0f;
            SlipAngle = Mathf.Abs(beta);
            slipRate = Mathf.Lerp(slipRate, (SlipAngle - lastSlipAngle) / dt, 1f - Mathf.Exp(-dt * 10f));
            lastSlipAngle = SlipAngle;
            UpdateSteering(InSteer, vz, dt);

            // --- Suspension
            GroundedWheels = 0;
            foreach (var w in wheels)
            {
                Vector3 origin = transform.TransformPoint(w.mount);
                float length = travel + w.radius;
                w.prevCompression = w.compression;
                w.grounded = Physics.Raycast(origin, -up, out w.hit, length, layerMask, QueryTriggerInteraction.Ignore);
                w.compression = w.grounded ? Mathf.Clamp(length - w.hit.distance - SnowSink, 0f, travel) : 0f;
                if (w.grounded) GroundedWheels++;
            }
            foreach (var w in wheels)
            {
                if (!w.grounded) { w.load = 0f; continue; }
                float spring = w.compression * w.springK;
                float damper = (w.compression - w.prevCompression) / dt * w.damperC;
                // Barre antiroulis avec la roue opposée de l'essieu (aucune pour une roue centrale)
                float bar = w.mate != null ? (w.compression - w.mate.compression) * w.springK * arb : 0f;
                w.load = Mathf.Max(0f, spring + damper + bar);
                // Butée de fin de course : empêche de talonner
                if (w.compression > travel * 0.95f) w.load += (w.compression - travel * 0.95f) * w.springK * 8f;
                rb.AddForceAtPosition(up * w.load, transform.TransformPoint(w.mount));
            }

            // --- Assistances : antipatinage (coupé dès que la voiture glisse, pour laisser drifter) et ESP
            tcsNow = Mathf.Clamp01(assist * 1.6f) * Mathf.Clamp01(1f - (SlipAngle - 6f) / 8f);
            UpdateEsp(beta, vz);

            // --- Contact de chaque roue : repère, vitesses de glissement, adhérence disponible, freins
            float surface = OnRoad ? RoadGrip : offRoadGrip;
            // Pneus et suspension montés : terre, hors route et neige (sol meuble ou accidenté) contre goudron lisse
            float rough = Mathf.Max(dirt, SurfaceSnow);
            surface *= Mathf.Lerp(1f, tire.dirt, dirt) * Mathf.Lerp(1f, tire.snow, SurfaceSnow) * Mathf.Lerp(susp.smooth, susp.rough, rough);
            if (Wetness > 0f)
            {
                // Pluie : l'enrobé perd ~30 % d'adhérence, la terre détrempée et l'herbe ~20 % (plus ou moins selon les
                // pneus) ; au-delà de 150 km/h, le film d'eau en retire encore jusqu'à 15 % (aquaplaning)
                float wetGrip = Mathf.Max(0.3f, 1f - (1f - (OnRoad ? Mathf.Lerp(0.7f, 0.8f, SurfaceDirt) : 0.8f)) * tire.wetLoss);
                float aquaplaning = Mathf.Lerp(1f, 0.85f, Mathf.InverseLerp(42f, 75f, Speed));
                surface *= Mathf.Lerp(1f, wetGrip * aquaplaning, Wetness);
            }
            bool absOn = assist > 0.15f;
            int nDriven = 0;
            foreach (var w in wheels)
            {
                if (w.driven) nDriven++;
                // Freins : pédale (répartie avant/arrière), frein à main à l'arrière
                w.brakeF = brakePedal * Spec.brakeForce * brakePower * w.brakeShare;
                w.abs = absOn && w.brakeF > 0f;
                if (HandbrakeOn && !w.front)
                {
                    int rear = 0;
                    foreach (var o in wheels) if (!o.front) rear++;
                    w.brakeF += handbrakeForce * Spec.mass * 9.81f / Mathf.Max(1, rear);
                    w.abs = false;
                }
                w.cap = 0f;
                if (!w.grounded) continue;
                Vector3 n = w.hit.normal;
                Quaternion steer = w.front ? Quaternion.AngleAxis(steerAngle, up) : Quaternion.identity;
                w.wf = Vector3.ProjectOnPlane(steer * fwd, n).normalized;
                w.wr = Vector3.Cross(n, w.wf);
                Vector3 pv = rb.GetPointVelocity(w.hit.point);
                w.vLong = Vector3.Dot(pv, w.wf);
                w.vLat = Vector3.Dot(pv, w.wr);
                w.denom = Mathf.Max(Mathf.Abs(w.vLong), 3f);
                w.slip = w.vLat / w.denom;
                // Sensibilité à la charge : la roue extérieure, très chargée, n'adhère pas proportionnellement plus
                float sensitivity = Mathf.Clamp(1f - loadSensitivity * (w.load / w.nominalLoad - 1f), 0.75f, 1.15f);
                w.cap = Spec.tireGrip * surface * w.load * sensitivity * (w.front ? 1f : rearGripBias * tire.rear);
                // ESP : freine la roue avant extérieure au virage (couple qui ramène le nez dans la trajectoire)
                if (EspIntervention > 0f && w.front && !w.center && Mathf.Sign(w.mount.x) == espSide)
                {
                    w.brakeF += EspIntervention * w.cap * 0.9f;
                    w.abs = absOn;
                }
            }

            // --- Rotation des roues et forces des pneus : le pont moteur (lié au moteur s'il est embrayé), puis chaque
            // roue libre
            bool coupled = !Shifting && !clutchOut;
            float r = Spec.wheelRadius;
            float driveMass = (nDriven * wheelInertia + (coupled ? engineInertia * TotalRatio * TotalRatio : 0f)) / (r * r);
            if (nDriven > 0) driveW = SpinWheels(true, null, driveW, driveMass, coupled, dt);
            float spinAmount = 0f, vDriven = 0f;
            int drivenGrounded = 0;
            foreach (var w in wheels)
            {
                if (w.driven)
                {
                    w.omegaR = driveW;
                    if (w.grounded) { vDriven += w.vLong; drivenGrounded++; }
                    continue;
                }
                w.omegaR = SpinWheels(false, w, w.omegaR, wheelInertia / (r * r), false, dt);
            }
            if (drivenGrounded > 0)
            {
                vDriven /= drivenGrounded;
                spinAmount = Mathf.Clamp01((driveW - vDriven) / Mathf.Max(Mathf.Abs(vDriven), 3f) / (sPeak * 4f));
            }
            WheelSpin = Mathf.Lerp(WheelSpin, spinAmount, 1f - Mathf.Exp(-dt * 10f));
            TcsIntervention = drivenGrounded > 0 && driveW - vDriven > sPeak * 1.1f * Mathf.Max(Mathf.Abs(vDriven), 3f) ? tcsNow : 0f;

            float skidSum = 0f;
            foreach (var w in wheels)
            {
                if (!w.grounded) { w.locked = false; w.skid = 0f; w.held = false; continue; }
                float fx = w.fx, fy = w.fy;
                // Immobilisation à l'arrêt
                if (Mathf.Abs(w.vLong) < 0.5f && drivePedal < 0.01f) fx = -w.vLong * w.load / 9.81f / dt * 0.5f;
                // À très basse vitesse, la formule de glissement est instable : on annule directement la glisse latérale
                float stop = Mathf.Clamp(-w.vLat * w.load / 9.81f / dt * 0.3f, -w.cap, w.cap);
                fy = Mathf.Lerp(stop, fy, Mathf.Clamp01(Mathf.Abs(w.vLong) / 3f));
                // Frein de stationnement. Les deux forces ci-dessus ne font que freiner : proportionnelles à la vitesse,
                // elles laissent la voiture descendre une pente à vitesse lente mais constante (2 cm/s sur celle du
                // départ). Voiture laissée seule, chaque pneu arrêté retient donc aussi le point du sol où il s'est posé,
                // comme un ressort court, dans la limite de son adhérence : poussé plus fort (choc), il glisse et se
                // repose plus loin.
                if (parked && Mathf.Abs(w.vLong) < 0.5f && Mathf.Abs(w.vLat) < 0.5f && w.load > 0f)
                {
                    if (!w.held) { w.held = true; w.anchor = w.hit.point; }
                    float stiffness = w.load / HoldStretch;
                    Vector3 pull = Vector3.ProjectOnPlane(w.anchor - w.hit.point, w.hit.normal);
                    float reach = w.cap / stiffness;
                    if (pull.sqrMagnitude > reach * reach)
                    {
                        pull = pull.normalized * reach;
                        w.anchor = w.hit.point + pull;
                    }
                    var hold = Vector2.ClampMagnitude(new Vector2(fx + Vector3.Dot(pull, w.wf) * stiffness, fy + Vector3.Dot(pull, w.wr) * stiffness), w.cap);
                    fx = hold.x;
                    fy = hold.y;
                }
                else w.held = false;
                rb.AddForceAtPosition(w.wf * fx + w.wr * fy, w.hit.point + up * rollCenterHeight);

                // Glisse réelle (au-delà du pic d'adhérence, avec une vraie vitesse de glissement) : fumée, traces, crissement
                float su = w.omegaR - w.vLong;
                float slipSpeed = Mathf.Sqrt(su * su + w.vLat * w.vLat);
                float x = slipSpeed / w.denom / sPeak;
                w.locked = Mathf.Abs(w.omegaR) < 0.3f && Mathf.Abs(w.vLong) > 2f;
                w.skid = Mathf.Clamp01((x - 1.3f) / 1.7f) * Mathf.Clamp01(slipSpeed / 2.5f);
                skidSum += Mathf.Clamp01((x - 0.6f) / 2.2f) * Mathf.Clamp01(slipSpeed / 2f);
            }
            SkidAmount = Mathf.Clamp01(skidSum / Mathf.Max(1, wheels.Count));
            float skidFront = 0f, skidRear = 0f;
            foreach (var w in wheels)
            {
                if (w.front) skidFront = Mathf.Max(skidFront, w.skid);
                else skidRear = Mathf.Max(skidRear, w.skid);
            }
            SkidFront = skidFront;
            SkidRear = skidRear;

            // --- Caisse à plat : couple de rappel contre le roulis et le tangage excessifs
            Vector3 tilt = Vector3.Cross(up, Vector3.up);
            if (GroundedWheels >= 2) rb.AddTorque(tilt * uprightAssist * rb.mass - Vector3.ProjectOnPlane(rb.angularVelocity, Vector3.up) * rb.mass * 0.4f);

            // --- En l'air (saut, tremplin) : la caisse suit sa trajectoire, à plat en roulis, le nez dans le sens de
            // la vitesse (montée puis descente, plongée limitée) ; le lacet reste libre. Sans cela, la vitesse de
            // tangage prise sur un tremplin fait retomber la voiture sur le nez ou sur le toit.
            airTime = GroundedWheels == 0 ? airTime + dt : 0f;
            if (airStability > 0f && airTime > 0.12f && Speed > 6f)
            {
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                Vector3 wantUp = Vector3.up;
                if (flat.sqrMagnitude > 9f)
                {
                    float climb = Mathf.Clamp(Mathf.Atan2(v.y, flat.magnitude), -20f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad);
                    Vector3 path = flat.normalized * Mathf.Cos(climb) + Vector3.up * Mathf.Sin(climb);
                    wantUp = Vector3.ProjectOnPlane(Vector3.up, path).normalized;
                }
                Vector3 w = rb.angularVelocity;
                Vector3 spin = w - up * Vector3.Dot(w, up);          // roulis et tangage, sans le lacet
                float k = Mathf.Clamp01((airTime - 0.12f) / 0.25f) * airStability;
                rb.AddTorque((Vector3.Cross(up, wantUp) * 7f - spin * 3f) * k, ForceMode.Acceleration);
            }

            // --- Aérodynamique et roulement
            float v2 = Speed * Speed;
            rb.AddForce(-v * Speed * 0.5f * AirDensity * Spec.dragArea);
            if (GroundedWheels > 0) rb.AddForce(-up * 0.5f * AirDensity * Spec.downforceArea * v2);
            rb.AddForce(-v * Spec.mass * 0.015f * (OnRoad ? 1f + SurfaceDirt * 1.5f + SurfaceSnow * 0.8f : 6f + SurfaceSnow * 3f));
        }

        /// <summary>
        /// ESP : la dérive (et sa vitesse de croissance) dépasse ce que l'assistance autorise → intervention (0-1).
        /// Permissif à basse vitesse (on peut drifter), strict à haute vitesse ; coupé au frein à main (geste volontaire).
        /// </summary>
        void UpdateEsp(float beta, float vz)
        {
            EspIntervention = 0f;
            if (assist <= 0f || Speed < 6f || vz <= 0f || HandbrakeOn || GroundedWheels < 2 || SlipAngle > 120f) return;
            float a = Mathf.Clamp01(assist / 0.6f);
            float allow = Mathf.Lerp(Mathf.Lerp(80f, 30f, a), Mathf.Lerp(40f, 9f, a), Mathf.InverseLerp(25f, 60f, Speed));
            float predicted = SlipAngle + Mathf.Max(0f, slipRate) * 0.25f;
            EspIntervention = Mathf.Clamp01((predicted - allow) / 12f);
            // Roue à freiner : côté vers lequel pointe la vitesse (l'extérieur du virage quand l'arrière décroche)
            espSide = Mathf.Sign(beta);
        }

        void UpdateSteering(float input, float vz, float dt)
        {
            float v = Mathf.Abs(vz);
            float speed01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 65f, v));
            // Inertie du volant : il tourne d'autant plus lentement que la vitesse est élevée,
            // et revient au centre un peu plus vite qu'il ne braque
            bool turningIn = Mathf.Abs(input) > Mathf.Abs(steerInput) + 0.01f;
            float response = Mathf.Lerp(steerResponseLow, steerResponseHigh, speed01) * (turningIn ? 1f : 0.6f);
            steerInput = Mathf.SmoothDamp(steerInput, input, ref steerVelocity, response, Mathf.Infinity, dt);
            // Braquage direct : fortement réduit avec la vitesse (≈ 4° de roue à 200 km/h)
            float speedFactor = 1f / (1f + Mathf.Pow(v / 16f, 1.5f));
            float rawRange = Spec.steerLock * Mathf.Max(0.07f, speedFactor);
            float target = steerInput * rawRange;
            if (vz > 3f)
            {
                // Chasse : relâchées, les roues avant suivent la trajectoire de l'avant (contre-braquage naturel en glisse,
                // plus ou moins complet selon l'assistance : sans aide, c'est au pilote de contre-braquer)
                Vector3 frontLocal = new Vector3(0f, 0f, frontAxleZ);
                Vector3 pv = rb.GetPointVelocity(transform.TransformPoint(frontLocal));
                float flow = Mathf.Atan2(Vector3.Dot(pv, transform.right), Mathf.Max(1f, Vector3.Dot(pv, transform.forward))) * Mathf.Rad2Deg;
                float align = Mathf.Lerp(0.55f, 0.85f, Mathf.Clamp01(assist / 0.6f)) * (HandbrakeOn ? 0.5f : 1f);
                // Direction assistée : le stick demande une dérive aux pneus avant (jusqu'au pic d'adhérence) ;
                // à haute vitesse, un petit geste ne demande qu'une petite dérive
                float slipMax = Mathf.Min(Mathf.Lerp(maxFrontSlipLow, maxFrontSlipHigh, speed01), Mathf.Atan(sPeak) * Mathf.Rad2Deg);
                float range = Mathf.Lerp(rawRange, slipMax, assist);
                float blend = Mathf.Clamp01((vz - 3f) / 8f);
                target = Mathf.Lerp(target, flow * align + steerInput * range, blend);
            }
            steerAngle = Mathf.MoveTowards(steerAngle, Mathf.Clamp(target, -Spec.steerLock, Spec.steerLock), dt * 220f);
        }

        // ------------------------------------------------------------------ visuel

        void Update()
        {
            if (Spec == null) return;
            float dt = Time.deltaTime;
            foreach (var w in wheels)
            {
                if (!w.visual) continue;
                Vector3 origin = transform.TransformPoint(w.mount);
                float down = travel - w.compression;
                w.visual.position = origin - transform.up * down;
                // Rotation réelle de la roue : patine, bloque au frein à main
                w.spin += w.omegaR / w.radius * Mathf.Rad2Deg * dt;
                w.visual.localRotation = Quaternion.Euler(w.spin, w.front ? steerAngle : 0f, 0f);
            }
        }

        /// <summary>Contact d'une roue avec le sol et intensité de glisse (0-1) pour les effets.</summary>
        public bool WheelContact(int i, out Vector3 point, out Vector3 normal, out float skid)
        {
            var w = wheels[i];
            if (Remote)
            {
                // Voiture d'un autre joueur : roue à sa place de repos, glisse de son essieu reçue du réseau
                point = transform.TransformPoint(w.mount) - transform.up * (travel - sag + w.radius);
                normal = transform.up;
                skid = remoteGrounded ? (w.front ? SkidFront : SkidRear) : 0f;
                return remoteGrounded;
            }
            point = w.grounded ? w.hit.point : Vector3.zero;
            normal = w.grounded ? w.hit.normal : Vector3.up;
            skid = w.grounded ? w.skid : 0f;
            return w.grounded;
        }

        public bool WheelIsRear(int i) => !wheels[i].front;

        /// <summary>Frottement contre un obstacle (glissière, mur, voiture) : point de contact et vitesse de glissement.</summary>
        public event Action<Vector3, Vector3, float> Scraped;

        /// <summary>Vitesses avant le pas de physique en cours : celles d'avant un choc, lues dans OnCollisionEnter.</summary>
        Vector3 preStepVelocity, preStepAngular;

        void OnCollisionEnter(Collision c)
        {
            var prop = c.collider.GetComponentInParent<BreakableProp>();
            if (prop)
            {
                // Débris d'un panneau déjà cassé : roulé dessus ou poussé, pas un choc
                if (prop.Broken) return;
                // Petit panneau (ou pneus, percutés assez vite) : il casse et la voiture est seulement ralentie (le
                // solveur l'a arrêtée net contre l'obstacle pendant ce pas : on rend la vitesse d'avant, réduite)
                if (!Remote && preStepVelocity.magnitude >= prop.BreakSpeed)
                {
                    prop.Break(preStepVelocity, GetComponentsInChildren<Collider>(), c.contactCount > 0 ? c.GetContact(0).point : prop.transform.position);
                    rb.linearVelocity = prop.SpeedAfterBreak(preStepVelocity);
                    rb.angularVelocity = preStepAngular;
                    // Bruit, étincelles et petite secousse, sans l'intensité d'un vrai accident
                    Impacted?.Invoke(c, Mathf.Clamp(preStepVelocity.magnitude * 0.2f, 2f, 5.5f));
                    return;
                }
            }
            float impact = c.relativeVelocity.magnitude;
            if (impact > 1.5f) Impacted?.Invoke(c, impact);
        }

        void OnCollisionStay(Collision c)
        {
            if (Scraped == null || c.contactCount == 0) return;
            var contact = c.GetContact(0);
            // Ignore le sol : seulement les contacts latéraux de la caisse
            if (Vector3.Dot(contact.normal, transform.up) > 0.6f) return;
            float slide = Vector3.ProjectOnPlane(c.relativeVelocity, contact.normal).magnitude;
            if (slide > 4f && !c.collider.GetComponentInParent<BreakableProp>()) Scraped(contact.point, contact.normal, slide);
        }
    }
}
