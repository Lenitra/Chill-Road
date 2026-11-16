using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Voiture d'un autre joueur (partie en réseau). Elle n'est pas simulée ici : sa position, sa vitesse et l'état de
    /// ses commandes arrivent par le réseau, et elle est montrée avec un léger retard, entre deux états reçus. C'est un
    /// corps cinématique : on peut la percuter, mais chaque joueur ne calcule que sa propre voiture. Elle garde ses
    /// phares, ses feux stop, sa poussière, sa fumée et ses traces de pneus, et on entend son moteur de près.
    /// Tant qu'elle chevauche la voiture locale (départ, replacement sur la route), les deux se traversent.
    /// </summary>
    public class RemoteCar : MonoBehaviour
    {
        /// <summary>Couleur de chaque joueur (étiquette au-dessus de la voiture, carte, minicarte).</summary>
        public static readonly Color[] Colors =
        {
            new Color(0.35f, 0.85f, 1f), new Color(1f, 0.45f, 0.62f), new Color(0.55f, 1f, 0.55f), new Color(1f, 0.6f, 0.25f),
            new Color(0.75f, 0.6f, 1f), new Color(1f, 0.92f, 0.4f), new Color(0.4f, 1f, 0.85f), new Color(1f, 0.5f, 0.45f),
        };

        const float EngineRange = 150f;

        public byte Id { get; private set; }
        public string Name = "";
        public VehicleController Car { get; private set; }
        public Color Color => Colors[Id % Colors.Length];
        /// <summary>Un premier état est arrivé : la voiture est en place.</summary>
        public bool Ready => hasState;
        /// <summary>Repère de mise à jour (liste des joueurs encore présents).</summary>
        public int Stamp;

        GameManager game;
        CarLights lights;
        readonly NetBuffer buffer = new NetBuffer();
        NetClock clock;
        NetCarState state;
        bool hasState;
        uint lastMs;
        int carIndex = -1, paint = -1;
        // Chevauchement avec la voiture locale : collisions coupées jusqu'à ce qu'elles soient séparées
        bool ghost = true, ghostApplied;
        int localTeleports = -1;
        Collider box, localBox;
        EngineSynth engine;

        public static RemoteCar Create(GameManager game, byte id, string name)
        {
            // Couche « Ignore Raycast », comme la voiture locale : les rayons de suspension et la caméra l'ignorent
            var go = new GameObject("Remote " + id) { layer = 2 };
            go.AddComponent<Rigidbody>();
            var remote = go.AddComponent<RemoteCar>();
            remote.game = game;
            remote.Id = id;
            remote.Name = name ?? "";
            remote.Car = go.AddComponent<VehicleController>();
            remote.Car.SetRemote();
            MiniMap.Track(remote, () => remote && remote.hasState ? remote.transform : null, remote.Color);
            return remote;
        }

        void OnDestroy() => MiniMap.Untrack(this);

        void SetCar(int index)
        {
            var cars = game.assets.playerCars;
            carIndex = index;
            Car.Setup(cars[Mathf.Clamp(index, 0, cars.Length - 1)]);
            if (!lights)
            {
                lights = gameObject.AddComponent<CarLights>();
                lights.Init(Car, game.assets);
                if (game.assets.particle) gameObject.AddComponent<VehicleDust>().Init(Car, game.assets.particle);
                if (game.assets.particle && game.assets.spark) gameObject.AddComponent<VehicleEffects>().Init(Car, game.assets);
            }
            else lights.Rebuild();
            paint = -1;
            box = null;
            ghost = true;
        }

        /// <summary>Nouvel état reçu, daté par l'horloge (ms) de la machine du joueur.</summary>
        public void Apply(in NetCarState s, uint ms)
        {
            if (hasState && (int)(ms - lastMs) <= 0) return;
            lastMs = ms;
            double t = clock.ToLocal(ms / 1000.0, Time.timeAsDouble);
            if (!hasState || s.teleport != state.teleport)
            {
                // Arrivée, replacement sur la route : la voiture saute à sa place au lieu d'y glisser
                buffer.Clear();
                transform.SetPositionAndRotation(s.pos, s.rot);
                Car.Body.position = s.pos;
                Car.Body.rotation = s.rot;
                ghost = true;
            }
            if (s.carIndex != carIndex) SetCar(s.carIndex);
            if (s.paint != paint) { paint = s.paint; CarPaint.Apply(Car.Model, paint); }
            buffer.Push(t, s.pos, s.rot, s.vel);
            state = s;
            hasState = true;
            if (lights) lights.Set(s.lights);
        }

        void FixedUpdate()
        {
            if (!hasState) return;
            if (buffer.Sample(Time.fixedTimeAsDouble - NetProtocol.PlayerDelay, out var p, out var r, out var v))
            {
                Car.Body.MovePosition(p);
                Car.Body.MoveRotation(r);
                Car.ApplyRemote(state, v);
            }
            UpdateGhost();
        }

        void UpdateGhost()
        {
            var me = game.Player;
            if (!me || !me.gameObject.activeInHierarchy) return;
            if (me.TeleportCount != localTeleports) { localTeleports = me.TeleportCount; ghost = true; }
            else if (ghost && (me.transform.position - transform.position).sqrMagnitude > 7f * 7f) ghost = false;
            if (!box) { box = GetComponent<BoxCollider>(); ghostApplied = false; }
            if (!localBox) { localBox = me.GetComponent<BoxCollider>(); ghostApplied = false; }
            if (!box || !localBox || ghost == ghostApplied) return;
            Physics.IgnoreCollision(localBox, box, ghost);
            ghostApplied = ghost;
        }

        void Update()
        {
            if (!hasState) return;
            if (game.Weather) Car.Wetness = game.Weather.Wetness;
            UpdateEngine();
        }

        /// <summary>Moteur de la voiture, entendu de près : volume selon la distance à la caméra.</summary>
        void UpdateEngine()
        {
            var cam = Camera.main;
            if (!cam || Car.Spec == null || GameManager.DedicatedServer) return;
            float d = Vector3.Distance(cam.transform.position, transform.position);
            if (d > EngineRange)
            {
                if (engine) { Destroy(engine.gameObject); engine = null; }
                return;
            }
            if (!engine)
            {
                var go = new GameObject("Engine");
                go.transform.SetParent(transform, false);
                go.AddComponent<AudioSource>();
                engine = go.AddComponent<EngineSynth>();
            }
            engine.SetVoice(Car.Spec.Voice);
            float near = 1f / (1f + d * d / (16f * 16f));
            float fade = Mathf.Clamp01((EngineRange - d) / 30f);
            bool silent = Time.timeScale < 0.01f || game.State == GameManager.Phase.Loading;
            engine.SetState(Car.Rpm, Car.Throttle, Car.Rpm01, false, silent ? 0f : GameSettings.EffectsVolume * 0.3f * near * fade, Time.unscaledDeltaTime);
        }
    }
}
