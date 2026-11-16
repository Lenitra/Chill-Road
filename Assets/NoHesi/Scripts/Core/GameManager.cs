using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoHesi
{
    /// <summary>
    /// Point d'entrée : charge la carte (ville de montagne : relief, routes, forêts, bâtiments), crée le joueur,
    /// le trafic d'ambiance, le cycle jour/nuit, la météo, la caméra, le son et le HUD.
    /// Déroulé : chargement → écran d'accueil → compte à rebours → balade libre sur la carte.
    /// À plusieurs : partie en réseau (<see cref="NetGame"/>), hébergée par un joueur ou par un serveur dédié
    /// (jeu lancé avec « -server » : pas d'accueil, pas de voiture, la caméra suit les joueurs).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum Phase { Loading, Title, Countdown, Playing }

        /// <summary>Après rechargement de la scène, on passe directement du chargement à la partie.</summary>
        static bool skipTitle;

        const string CarKey = "nohesi_car";
        /// <summary>Départ à plusieurs : les joueurs sont rangés en file, espacés de cette distance (m).</summary>
        const float GridSpacing = 9f;

        /// <summary>
        /// Serveur dédié (ligne de commande « -server », avec au besoin « -port 47777 », « -name MonServeur »,
        /// « -players 8 ») : héberge une partie sans y jouer.
        /// </summary>
        public static bool DedicatedServer { get; private set; }

        public GameAssets assets;
        public CameraRig cameraRig;
        [HideInInspector] public int worldSeed;

        public VehicleController Player { get; private set; }
        public ScoreSystem Score { get; private set; }
        public TrafficSystem Traffic { get; private set; }
        public RoadNetwork Road { get; private set; }
        public MapTerrain Terrain { get; private set; }
        public MapData Map { get; private set; }
        public DayNightCycle DayNight { get; private set; }
        public SynthwaveMusic Music { get; private set; }
        public OptionsMenu Options { get; private set; }
        /// <summary>Partie en réseau (hébergée, rejointe, ou inactive en solo).</summary>
        public NetGame Net { get; private set; }
        /// <summary>GPS : destination choisie sur la carte globale et itinéraire le plus rapide.</summary>
        public RouteGuide Nav { get; private set; }
        /// <summary>Partie en réseau en cours : le monde ne s'arrête plus quand un menu est ouvert.</summary>
        public bool Online => Net && Net.Active;
        /// <summary>Phares du joueur (allumés / éteints au L3).</summary>
        public CarLights Lights => carLights;
        public CameraRig Camera => cameraRig;
        public Hud Hud => hud;
        public Phase State { get; private set; } = Phase.Loading;
        /// <summary>Mode démo : retiré avec la carte (toujours faux), gardé pour les écrans qui le consultent.</summary>
        public bool Demo => false;
        /// <summary>Météo : temps clair ou orage (route mouillée).</summary>
        public WeatherSystem Weather { get; private set; }
        /// <summary>Le joueur de cette machine : sa voiture, sa caméra, son HUD, son son.</summary>
        public PlayerSlot Local => local;
        /// <summary>Progression de l'appui long sur le bouton de replacement (0-1), pour le HUD.</summary>
        public float ResetHold => local.ResetHold;
        const float HoldToChangeCar = 0.7f;
        /// <summary>Avancement du chargement (0-1) et étape en cours.</summary>
        public float LoadProgress { get; private set; }
        public string LoadStep { get; private set; } = "";
        /// <summary>Lieu-dit où se trouve le joueur (vide hors des lieux nommés).</summary>
        public string Place { get; private set; } = "";
        /// <summary>Concession (lieu « dealer » de la carte) : on y achète les voitures. Nulle si la carte n'en a pas.</summary>
        public MapData.PoiJson DealerPoi { get; private set; }
        /// <summary>Écran de la concession.</summary>
        public Dealership Dealer { get; private set; }
        /// <summary>Cascades uniques : tremplins de la carte, distance à parcourir en l'air.</summary>
        public StuntJumps Stunts { get; private set; }
        /// <summary>Courses chronométrées sur la carte (sprints, tours) et records personnels.</summary>
        public Races Races { get; private set; }

        readonly PlayerSlot local = new PlayerSlot();
        /// <summary>Position de la voiture locale (terrain fin, décor) et positions de tous les joueurs (trafic).</summary>
        readonly List<Vector3> around = new List<Vector3>(), positions = new List<Vector3>();
        MapProps props;
        CarLights carLights;
        Hud hud;
        MenuScreens menus;
        ScreenFx screenFx;
        FxBursts fx;
        float musicMuffle = 0.6f, launchT, launchLen = 1f, placeTimer;
        MapData.PoiJson currentPoi;

        void Awake()
        {
            DedicatedServer = HasArgument("-server");
#if UNITY_EDITOR
            // Essai du serveur dédié dans l'éditeur : PlayerPrefs « nohesi_server_test » à 1
            if (PlayerPrefs.GetInt("nohesi_server_test", 0) == 1) DedicatedServer = true;
#endif
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 100f; // physique véhicule plus stable à haute vitesse
            GameSettings.Load();

            Road = gameObject.AddComponent<RoadNetwork>();
            Terrain = gameObject.AddComponent<MapTerrain>();
            props = gameObject.AddComponent<MapProps>();
            Traffic = gameObject.AddComponent<TrafficSystem>();
            Traffic.Init(assets, Road);

            // Joueur (couche « Ignore Raycast » : les rayons de suspension ne se touchent pas eux-mêmes)
            var playerGo = new GameObject("Player") { layer = 2 };
            playerGo.AddComponent<Rigidbody>();
            Player = playerGo.AddComponent<VehicleController>();
            local.Car = Player;
            // Voiture gardée de la dernière partie, si elle a été achetée ; sinon la voiture de départ (Ami)
            int saved = PlayerPrefs.GetInt(CarKey, -1);
            local.CarIndex = saved >= 0 && saved < assets.playerCars.Length && Wallet.Owns(assets.playerCars[saved])
                ? saved : Wallet.StarterIndex(assets.playerCars);
            Player.Setup(CarTuning.Apply(assets.playerCars[local.CarIndex]));
            CarPaint.Apply(Player.Model, CarPaint.Selected(assets.playerCars[local.CarIndex]));
            Player.assist = 0.6f; // assistance fixe (non réglable)
            // Immobile tant que le monde n'est pas chargé
            Player.Body.isKinematic = true;
            Player.InputEnabled = false;
            Traffic.Drivers.Add(Player);
            Traffic.Watch(Player);
            carLights = playerGo.AddComponent<CarLights>();
            carLights.Init(Player, assets);
            local.Lights = carLights;
            if (assets.particle) playerGo.AddComponent<VehicleDust>().Init(Player, assets.particle);
            VehicleEffects effects = null;
            if (assets.particle && assets.spark)
            {
                effects = playerGo.AddComponent<VehicleEffects>();
                effects.Init(Player, assets);
            }

            Score = gameObject.AddComponent<ScoreSystem>();
            Score.Init(Player, Traffic);
            local.Score = Score;

            cameraRig.target = Player;
            local.Rig = cameraRig;
            // La carte se voit de loin : les sommets d'en face sont à plusieurs kilomètres
            var mainCam = cameraRig.GetComponent<Camera>();
            mainCam.farClipPlane = Mathf.Max(mainCam.farClipPlane, 9000f);
            var audioFx = cameraRig.gameObject.AddComponent<GameAudio>();
            audioFx.Init(Player, Score);
            local.Audio = audioFx;
            // Musique synthwave de fond (objet à part : son filtre audio ne doit traiter qu'elle)
            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(cameraRig.transform, false);
            musicGo.AddComponent<AudioSource>();
            Music = musicGo.AddComponent<SynthwaveMusic>();
            if (effects) effects.Backfire += audioFx.PlayBackfire;
            // Effets d'écran liés à la vitesse (traînées, aberration chromatique)
            Material streak = null;
            if (assets.spark)
            {
                streak = new Material(assets.spark) { name = "Streak (runtime)" };
                streak.SetColor("_BaseColor", new Color(1.3f, 1.35f, 1.5f, 1f));
            }
            screenFx = gameObject.AddComponent<ScreenFx>();
            screenFx.Init(Player, Score, mainCam, streak);
            fx = gameObject.AddComponent<FxBursts>();
            fx.Init(assets);
            // Secousses de caméra aux chocs
            Player.Impacted += (c, impact) => { if (impact > 3f) cameraRig.Shake(Mathf.Clamp01(impact / 20f)); };

            // Air de montagne : on voit l'autre versant et les sommets à plusieurs kilomètres
            RenderSettings.fogDensity = 0.00026f;
            DayNight = gameObject.AddComponent<DayNightCycle>();
            DayNight.Init(Road, Traffic, carLights);

            hud = gameObject.AddComponent<Hud>();
            local.Hud = hud;

            // Météo : clair la plupart du temps, orage parfois (pluie, éclairs, route mouillée et glissante)
            Weather = gameObject.AddComponent<WeatherSystem>();
            Weather.Init(mainCam, assets, audioFx, DayNight);
            DayNight.Weather = Weather;
            Weather.Changed += storm =>
            {
                if (storm) Notify(Loc.T("ORAGE EN APPROCHE : ROUTE GLISSANTE", "STORM INCOMING: SLIPPERY ROAD"), new Color(0.45f, 0.65f, 1f));
                else Notify(Loc.T("ÉCLAIRCIE : LA ROUTE SÈCHE", "CLEARING UP: ROAD DRYING"), new Color(1f, 0.8f, 0.35f));
            };

            Net = gameObject.AddComponent<NetGame>();
            Net.Init(this);
            ApplyLiveSettings();
        }

        /// <summary>Réglages appliqués en cours de partie (menu Options) : trafic, pluie, volumes.</summary>
        public void ApplyLiveSettings()
        {
            Traffic.density = GameSettings.TrafficDensity;
            // Partie rejointe : la météo est celle du serveur
            if (Weather && !(Net && Net.IsClient)) Weather.SetChance(GameSettings.RainChance);
            if (local.Audio) local.Audio.masterVolume = GameSettings.EffectsVolume;
            GraphicsQuality.Apply(this);
        }

        /// <summary>Annonce en bannière sur le HUD (météo, arrivée ou départ d'un joueur), pendant la partie.</summary>
        public void Notify(string text, Color color)
        {
            if (State == Phase.Playing && hud && !DedicatedServer) hud.Banner(text.ToUpperInvariant(), color);
        }

        void Start()
        {
            Options = gameObject.AddComponent<OptionsMenu>();
            Options.Init(this);
            hud.Init(this);
            hud.SetVisible(false);
            // Commandes tactiles (téléphone, tablette ; sur PC dès que l'écran est touché)
            gameObject.AddComponent<TouchControls>().Init(this);
            // Carte globale (touche « , » ou Select)
            Nav = gameObject.AddComponent<RouteGuide>();
            Nav.Init(this);
            gameObject.AddComponent<WorldMap>().Init(this);
            Dealer = gameObject.AddComponent<Dealership>();
            Dealer.Init(this);
            gameObject.AddComponent<TuningGarage>().Init(this);
            Stunts = gameObject.AddComponent<StuntJumps>();
            Stunts.Init(this);
            Races = gameObject.AddComponent<Races>();
            Races.Init(this);
            menus = gameObject.AddComponent<MenuScreens>();
            menus.Init(this);
            if (DedicatedServer) AudioListener.volume = 0f;
            GraphicsQuality.Apply(this);
            StartCoroutine(Load());
            // Banc d'essai des performances : « Chill Road.exe -benchmark »
            if (HasArgument("-benchmark") && !DedicatedServer) gameObject.AddComponent<Benchmark>().Init(this);
        }

        /// <summary>Charge la carte par étapes (écran de chargement), puis affiche l'accueil ou lance la partie.</summary>
        IEnumerator Load()
        {
            State = Phase.Loading;
            float started = Time.realtimeSinceStartup;
            LoadStep = Loc.T("Lecture de la carte", "Reading the map");
            LoadProgress = 0.02f;
            yield return null;
            yield return null;
            Map = MapData.Load();
            if (Map == null) yield break;
            Road.Init(assets, Map);
            Terrain.Init(assets, Map);
            if (Map.Json.pois != null)
                foreach (var poi in Map.Json.pois)
                    if (poi.id == "dealer") DealerPoi = poi;
            Traffic.Prepare();

            LoadStep = Loc.T("Routes et ponts", "Roads and bridges");
            yield return Road.Build(p => LoadProgress = Mathf.Lerp(0.05f, 0.3f, p));

            LoadStep = Loc.T("Montagnes et forêts", "Mountains and forests");
            yield return Terrain.Build(p => LoadProgress = Mathf.Lerp(0.3f, 0.7f, p));

            LoadStep = Loc.T("Ville et villages", "Town and villages");
            yield return props.Build(assets, Map, Road, Terrain.Vegetation, p => LoadProgress = Mathf.Lerp(0.7f, 0.85f, p));

            // Le joueur est posé au départ ; le terrain fin (avec collision) se construit autour de lui
            Player.Body.isKinematic = false;
            PlaceAtStart(0f);
            around.Clear();
            around.Add(Player.transform.position);
            int guard = 0;
            while (Terrain.UpdateAround(around, 2) > 0 && guard++ < 200)
            {
                LoadProgress = Mathf.Lerp(0.85f, 0.97f, guard / 12f);
                yield return null;
            }

            LoadStep = Loc.T("Circulation", "Traffic");
            LoadProgress = 0.98f;
            yield return null;
            if (!DedicatedServer)
                for (int i = 0; i < 80; i++) Traffic.Tick(around, 0.2f);
            LoadProgress = 1f;
            GraphicsQuality.Apply(this);       // distances de la végétation et de la flore (créées pendant le chargement)
            // Laisse l'écran de chargement visible au moins un instant
            while (Time.realtimeSinceStartup - started < 1.5f) yield return null;

            if (DedicatedServer) StartDedicated();
            else if (skipTitle) StartCountdown();
            else
            {
                State = Phase.Title;
                cameraRig.Showcase = true;
            }
        }

        /// <summary>Bouton « Jouer » : compte à rebours puis départ.</summary>
        public void StartCountdown()
        {
            if (State == Phase.Countdown || State == Phase.Playing) return;
            State = Phase.Countdown;
            StartCoroutine(Countdown());
        }

        // ------------------------------------------------------------------ partie en réseau

        /// <summary>
        /// Partie en réseau hébergée ou rejointe depuis l'accueil : la voiture prend sa place dans la file de départ
        /// (une par joueur), puis compte à rebours et départ.
        /// </summary>
        public void StartOnline()
        {
            if (State != Phase.Title) return;
            PlaceAtStart(-GridSpacing * Net.LocalId);
            StartCountdown();
        }

        /// <summary>
        /// Serveur dédié : pas d'accueil ni de voiture locale. La partie est ouverte tout de suite ; la caméra montre
        /// la place du départ, puis suit les joueurs à tour de rôle.
        /// </summary>
        void StartDedicated()
        {
            State = Phase.Playing;
            Player.InputEnabled = false;
            Player.Body.isKinematic = true;
            Traffic.Drivers.Remove(Player);
            Player.gameObject.SetActive(false);
            cameraRig.Showcase = true;
            string name = Argument("-name") ?? Loc.T("Serveur Chill Road", "Chill Road server");
            int port = int.TryParse(Argument("-port"), out int p) && p > 0 && p < 65536 ? p : NetProtocol.DefaultPort;
            int players = int.TryParse(Argument("-players"), out int n) ? n : NetProtocol.DefaultMaxPlayers;
            if (!Net.Host(name, port, players, true, out string error)) Debug.LogError("[Net] " + error);
        }

        static bool HasArgument(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

        static string Argument(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Active ou coupe les commandes du joueur (pause, carte).</summary>
        public void SetInputEnabled(bool enabled)
        {
            if (DedicatedServer) return;
            Player.InputEnabled = enabled;
            // Menu ouvert : la voiture freine (utile en réseau, où le temps ne s'arrête pas)
            Player.Parked = !enabled;
        }

        // ------------------------------------------------------------------ départ

        /// <summary>
        /// Transition accueil → partie : l'accueil s'éjecte (bandes néon, flash), la caméra plonge devant la voiture,
        /// rase son flanc et vient se placer derrière elle ; la musique monte pendant que le moteur rugit à chaque
        /// chiffre ; « GO ! » : drop de la musique, flash, coup de focale, secousse, arrivée du HUD.
        /// </summary>
        IEnumerator Countdown()
        {
            const float swoop = 1.5f, beat = 0.55f;
            float total = swoop + beat * 3f;
            launchT = 0f;
            launchLen = total;
            menus.PlayLaunch(total);
            cameraRig.PlayIntro(total);
            Music.Riser(total);
            screenFx.Punch(0.8f);
            yield return new WaitForSeconds(swoop);
            for (int i = 3; i >= 1; i--)
            {
                menus.ShowCountdown(i.ToString());
                local.Audio.Rev(i == 1 ? 1f : 0.7f);
                cameraRig.Shake(0.12f);
                yield return new WaitForSeconds(beat);
            }
            menus.ShowCountdown(Loc.T("GO !", "GO!"));
            Music.Drop();
            screenFx.Punch(1f);
            cameraRig.Kick(16f);
            cameraRig.Shake(0.45f);
            hud.SetVisible(true);
            hud.AnimateIn();
            Player.Autopilot = false;
            Player.InputEnabled = true;
            State = Phase.Playing;
        }

        /// <summary>
        /// Point de départ : sur la route de la vallée (lieu « depart » de la carte s'il existe, son rayon donnant
        /// le cap en degrés). back : recul le long de la voie (en réseau, les joueurs sont
        /// rangés en file l'un derrière l'autre).
        /// </summary>
        void PlaceAtStart(float back)
        {
            Vector3 near = Vector3.zero;
            float yaw = float.NaN;
            if (Map.Json.pois != null)
                foreach (var poi in Map.Json.pois)
                    if (poi.id == "depart") { near = new Vector3(poi.x, Map.Height(poi.x, poi.z), poi.z); yaw = poi.r; }
            if (float.IsNaN(yaw) && Road.Nodes.TryGetValue("place", out var node)) near = node.pos + new Vector3(-40f, 0f, 0f);
            var hit = Road.Nearest(near, 2000f);
            if (!hit.Found) { Player.Teleport(near + Vector3.up * 2f, Quaternion.identity, 0f); return; }
            Vector3 want = float.IsNaN(yaw) ? Vector3.right : Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            hit.edge.Evaluate(hit.s, out var tangent, out _);
            // Bretelle à sens unique : toujours dans le sens de la circulation (de a vers b)
            int direction = hit.edge.oneway || Vector3.Dot(tangent, want) >= 0f ? 1 : -1;
            float s = Mathf.Clamp(hit.s + back * direction, hit.edge.trimA + 2f, hit.edge.Length - hit.edge.trimB - 2f);
            Vector3 pos = hit.edge.LanePosition(s, direction, out var forward);
            Traffic.ClearAround(pos, 14f);
            Player.Teleport(pos + Vector3.up * 0.5f, Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z), Vector3.up), 0f);
            local.UpsideDown = 0f;
            // Le départ est en pente : freins serrés tant que le joueur n'a pas la main (accueil, compte à rebours)
            Player.Parked = true;
        }

        /// <summary>Musique : filtrée au menu et en pause, s'ouvre pendant la transition, pleine en partie.</summary>
        void UpdateMusic()
        {
            float dt = Time.unscaledDeltaTime;
            float target;
            if (State == Phase.Countdown)
            {
                launchT += dt;
                target = Mathf.Lerp(0.5f, 0.15f, Mathf.Clamp01(launchT / launchLen));
                musicMuffle = target;
            }
            else
            {
                target = State == Phase.Playing ? (hud.Paused || OptionsMenu.IsOpen ? 0.7f : 0f) : 0.5f;
                musicMuffle = Mathf.MoveTowards(musicMuffle, target, dt * 1.5f);
            }
            Music.Set(GameSettings.MusicVolume * 0.75f, musicMuffle);
        }

        /// <summary>Quitte le jeu (arrête la lecture dans l'éditeur).</summary>
        public void Quit()
        {
            if (Net) Net.Leave();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            UpdateMusic();
            if (State == Phase.Loading || Map == null) return;
            float dt = Time.deltaTime;

            // Surface sous la voiture (goudron, terre, hors route), pluie
            Vector3 pos = Player.transform.position;
            var surface = Road.SurfaceAt(pos, out var roadHit);
            Player.OnRoad = surface.onRoad;
            Player.RoadGrip = surface.grip;
            Player.SurfaceDirt = surface.onRoad ? surface.dirt : 0f;
            Player.Wetness = Weather.Wetness;
            around.Clear();
            around.Add(pos);
            Road.UpdateOrigin(pos);
            Terrain.UpdateAround(around, 1);
            // Le trafic vit autour de tous les joueurs (en réseau : ceux des autres machines aussi)
            positions.Clear();
            if (!DedicatedServer) positions.Add(pos);
            Net.AddPlayers(positions);
            // Pluie : trafic plus prudent
            Traffic.WeatherSpeed = Mathf.Lerp(1f, 0.86f, Weather.Wetness);

            // Accueil et compte à rebours : le monde vit (trafic)
            if (State != Phase.Playing)
            {
                Traffic.Tick(positions, dt);
                return;
            }

            // Menu ouvert (pause, carte) : seul, le monde s'arrête ; en réseau, il continue sans nous
            bool menu = hud.Paused;
            if (!menu || Online) Traffic.Tick(positions, dt);
            if (DedicatedServer) return;
            if (!menu)
            {
                Score.Tick(dt);
                Stunts.Tick(dt);
                UpdateResetButton();
                if (GameInput.CameraPressed) cameraRig.NextView();
                if (GameInput.NextTrackPressed) Music.Next();
                UpdatePlace(dt);
            }
            else
            {
                local.ResetHeld = 0f;
                local.ResetConsumed = false;
                local.ResetHold = 0f;
            }

            // Retournée, tombée sous la carte, au fond de l'eau ou sortie des limites : on la replace sur la route
            float limit = Map.Half - 120f;
            if (Vector3.Dot(Player.transform.up, Vector3.up) < 0.3f) local.UpsideDown += dt; else local.UpsideDown = 0f;
            bool lost;
            if (Map.UnderRelief(pos))
            {
                // Sous la montagne, au-dessus d'un tunnel : perdue seulement si elle n'est pas dans le tube (enterrée à
                // côté, au-dessus de la voûte ou sous la chaussée)
                lost = !roadHit.Found || Mathf.Abs(roadHit.lateral) > roadHit.edge.HalfWidthAt(roadHit.s, roadHit.lateral) + 4f
                    || pos.y < roadHit.point.y - 6f || pos.y > roadHit.point.y + RoadNetwork.TunnelHeightHighway + 3f;
            }
            else lost = pos.y < Map.Height(pos.x, pos.z) - 30f;
            lost |= Mathf.Abs(pos.x) > limit || Mathf.Abs(pos.z) > limit;
            int water = Map.Water(pos.x, pos.z);
            if (water > 0 && water <= Map.Json.waters.Length && pos.y < Map.Json.waters[water - 1].level - 1.6f) lost = true;
            if (local.UpsideDown > 2.5f || lost) ResetPlayer();
        }

        /// <summary>Annonce le lieu-dit quand le joueur y entre (ville, col, station, barrage…).</summary>
        void UpdatePlace(float dt)
        {
            placeTimer -= dt;
            if (placeTimer > 0f || Map.Json.pois == null) return;
            placeTimer = 0.5f;
            Vector3 pos = Player.transform.position;
            MapData.PoiJson found = null;
            foreach (var poi in Map.Json.pois)
            {
                if (poi.id == "depart" || poi.r <= 0f) continue;
                float dx = pos.x - poi.x, dz = pos.z - poi.z;
                // Un peu de marge pour sortir : pas d'annonce répétée en longeant la limite
                float r = poi == currentPoi ? poi.r * 1.25f : poi.r;
                if (dx * dx + dz * dz < r * r) { found = poi; break; }
            }
            if (found == currentPoi) return;
            currentPoi = found;
            Place = found != null ? Loc.T(found.fr, found.en) : "";
            if (found != null) hud.Banner(Place.ToUpperInvariant(), new Color(1f, 0.86f, 0.5f));
        }

        /// <summary>
        /// Bouton de replacement : un appui court replace la voiture sur la route (au relâchement),
        /// un appui maintenu 0,7 s passe à la voiture suivante du garage.
        /// </summary>
        void UpdateResetButton()
        {
            var p = local;
            if (GameInput.ResetHeld)
            {
                p.ResetHeld += Time.unscaledDeltaTime;
                if (!p.ResetConsumed && p.ResetHeld >= HoldToChangeCar)
                {
                    p.ResetConsumed = true;
                    if (CycleCar(1, true))
                        hud.Banner(Loc.T("VOITURE : ", "CAR: ") + Player.Spec.displayName, new Color(1f, 0.76f, 0.2f));
                    else
                        hud.Banner(Loc.T("ACHÈTE D'AUTRES VOITURES À LA CONCESSION", "BUY MORE CARS AT THE CAR DEALER"), new Color(0.35f, 0.85f, 1f));
                }
            }
            else
            {
                if (p.ResetHeld > 0f && !p.ResetConsumed) ResetPlayer();
                p.ResetHeld = 0f;
                p.ResetConsumed = false;
            }
            p.ResetHold = p.ResetConsumed ? 0f : Mathf.Clamp01((p.ResetHeld - 0.12f) / (HoldToChangeCar - 0.12f));
        }

        /// <summary>Replace la voiture sur la route la plus proche, dans sa voie, dans le sens où elle regardait.</summary>
        public void ResetPlayer()
        {
            Vector3 from = Player.transform.position;
            var hit = Road.Nearest(from, 4000f);
            if (!hit.Found) return;
            var e = hit.edge;
            float s = Mathf.Clamp(hit.s, Mathf.Min(e.trimA + 3f, e.Length * 0.5f), Mathf.Max(e.Length - e.trimB - 3f, e.Length * 0.5f));
            e.Evaluate(s, out var tangent, out _);
            Vector3 heading = Player.transform.forward;
            heading.y = 0f;
            // Bretelle à sens unique : toujours dans le sens de la circulation (de a vers b)
            int direction = e.oneway || Vector3.Dot(tangent, heading) >= 0f ? 1 : -1;
            Vector3 pos = e.LanePosition(s, direction, out var forward);
            Traffic.ClearAround(pos, 14f);
            Player.Teleport(pos + Vector3.up * 0.5f, Quaternion.LookRotation(forward, Vector3.up), 0f);
            local.UpsideDown = 0f;
        }

        /// <summary>Replace le joueur sur la route la plus proche d'un point de la carte (outils, captures).</summary>
        public void TeleportTo(Vector3 point)
        {
            var hit = Road.Nearest(new Vector3(point.x, Map.Height(point.x, point.z), point.z), 4000f);
            if (!hit.Found) return;
            Vector3 pos = hit.edge.LanePosition(hit.s, 1, out var forward);
            Traffic.ClearAround(pos, 14f);
            Player.Teleport(pos + Vector3.up * 0.5f, Quaternion.LookRotation(forward, Vector3.up), 0f);
            local.UpsideDown = 0f;
        }

        /// <summary>
        /// Voiture possédée suivante (ou précédente) du garage, sans s'arrêter : même position, même vitesse (glisse
        /// comprise). Faux si le joueur n'a qu'une voiture.
        /// </summary>
        public bool CycleCar(int delta, bool remember = true)
        {
            var cars = assets.playerCars;
            int step = delta >= 0 ? 1 : -1;
            for (int n = 1; n < cars.Length; n++)
            {
                int i = ((local.CarIndex + step * n) % cars.Length + cars.Length) % cars.Length;
                if (!Wallet.Owns(cars[i])) continue;
                SelectCar(i, remember);
                return true;
            }
            return false;
        }

        /// <summary>Prend le volant d'une voiture du garage (achetée).</summary>
        public void SelectCar(int index, bool remember = true)
        {
            var p = local;
            p.CarIndex = Mathf.Clamp(index, 0, assets.playerCars.Length - 1);
            if (remember) PlayerPrefs.SetInt(CarKey, p.CarIndex);
            ShowCar(p.CarIndex);
        }

        /// <summary>
        /// Met une voiture du garage à la place de celle du joueur sans la lui attribuer (vitrine de la concession) ;
        /// <see cref="SelectCar"/> ou <c>ShowCar(Local.CarIndex)</c> pour revenir à la sienne.
        /// </summary>
        public void ShowCar(int index)
        {
            // Fiche avec les améliorations du garage de tuning (la même tant qu'elles ne changent pas), et sa peinture
            var basic = assets.playerCars[Mathf.Clamp(index, 0, assets.playerCars.Length - 1)];
            var spec = CarTuning.Apply(basic);
            var car = Player;
            if (car.Spec != spec) Swap(car, spec);
            CarPaint.Apply(car.Model, CarPaint.Selected(basic));
        }

        /// <summary>Change le modèle et la fiche de la voiture sans l'arrêter (même position, même vitesse).</summary>
        void Swap(VehicleController car, VehicleSpec spec)
        {
            var t = car.transform;
            Vector3 v = car.Velocity;
            car.Setup(spec);
            car.Teleport(t.position + Vector3.up * 0.3f, t.rotation, v.magnitude);
            // Une voiture plus lente repart à sa vitesse de pointe au plus
            float top = car.Spec.TopSpeed;
            if (v.magnitude > top) v = v.normalized * top;
            car.Body.linearVelocity = v;
            carLights.Rebuild();
        }

        /// <summary>Nombre de voitures achetées (Ami comprise).</summary>
        public int OwnedCars
        {
            get
            {
                int n = 0;
                foreach (var c in assets.playerCars) if (Wallet.Owns(c)) n++;
                return n;
            }
        }

        /// <summary>Voiture possédée suivante du garage.</summary>
        public string NextCarName
        {
            get
            {
                var cars = assets.playerCars;
                for (int n = 1; n <= cars.Length; n++)
                {
                    var c = cars[(local.CarIndex + n) % cars.Length];
                    if (Wallet.Owns(c)) return c.displayName;
                }
                return Player.Spec.displayName;
            }
        }

        /// <summary>Retour au menu principal : rechargement et écran d'accueil (la partie en réseau est quittée).</summary>
        public void ReturnToTitle()
        {
            if (Net) Net.Leave();
            skipTitle = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// Recommence au point de départ, sans repasser par l'accueil. En réseau, la partie continue : la voiture est
        /// seulement ramenée à sa place de départ.
        /// </summary>
        public void Restart()
        {
            if (Online)
            {
                if (!DedicatedServer) PlaceAtStart(-GridSpacing * Net.LocalId);
                return;
            }
            skipTitle = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
