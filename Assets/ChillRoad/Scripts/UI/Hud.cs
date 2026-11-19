using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// HUD de jeu (construit en code), discret et lisible : plaques inclinées aux bords fondus plutôt que des cadres,
    /// police de tableau de bord, chiffres en italique.
    /// - haut centre : argent du joueur ; pendant un combo, badge du multiplicateur, jauge du combo et cagnotte en jeu ;
    /// - haut gauche : heure ; haut droite : épreuve en cours ;
    /// - bas droite : compte-tours circulaire, vitesse, rapport, phares, distance ;
    /// - centre : gains (frôlements, drifts, vitesse, encaissement), bannières, compteur de drift, invite de la
    ///   concession ;
    /// - bas gauche : minicarte (pendant du compte-tours), à sa droite le morceau en cours et l'aide des commandes
    ///   (quelques secondes seulement) ;
    /// - menu pause avec le panneau des contrôles : liste, ou manette / clavier dessinés avec les commandes sur l'appareil
    ///   (<see cref="ControlsDiagram"/>) ; par défaut, le dessin de l'appareil utilisé en dernier.
    /// En réseau, le menu pause n'arrête pas le monde : la partie continue pour les autres joueurs.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Danger = new Color(1f, 0.32f, 0.26f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Plate = new Color(0.02f, 0.03f, 0.05f, 0.6f);
        static readonly Color DriftColor = new Color(1f, 0.55f, 0.2f);
        static readonly Color JumpColor = new Color(0.55f, 0.78f, 1f);
        static readonly Color MusicColor = new Color(1f, 0.45f, 0.78f);
        static readonly Color Money = new Color(0.5f, 1f, 0.6f);

        // Contrôles affichés dans la pause : action (fr, en), manette, clavier (fr, en), tactile (fr, en)
        static readonly string[][] Controls =
        {
            new[] { "Accélérer", "Throttle", "RT", "Z / W / ↑", "W / Z / ↑", "Pédale GAZ", "GAS pedal" },
            new[] { "Freiner · marche arrière", "Brake · reverse", "LT", "S / ↓", "S / ↓", "Pédale FREIN", "BRAKE pedal" },
            new[] { "Diriger", "Steer", "@LS", "Q / A · D   (← →)", "A / Q · D   (← →)", "@TS", "@TS" },
            new[] { "Frein à main", "Handbrake", "B / RB", "Espace", "Space", "FREIN À MAIN", "HANDBRAKE" },
            new[] { "Phares", "Headlights", "L3", "L", "L", "PHARES", "LIGHTS" },
            new[] { "Appel de phares", "Flash headlights", "@DD", "F", "F", "—", "—" },
            new[] { "Regarder autour", "Look around", "@RS", "Clic droit + souris", "Right-click + mouse", "Glisser sur l'écran", "Drag on screen" },
            new[] { "Changer de vue", "Change camera", "X", "C / V", "C / V", "VUE", "CAMERA" },
            new[] { "Klaxon", "Horn", "R3", "H", "H", "—", "—" },
            new[] { "Regarder derrière", "Look back", "LB", "B", "B", "DERRIÈRE", "LOOK BACK" },
            new[] { "Replacer · en course, maintenir : abandonner", "Reset · in a race, hold: abandon", "Y", "R", "R", "REPLACER", "RESET" },
            new[] { "Musique suivante", "Next song", "@DU", "M", "M", "MUSIQUE", "MUSIC" },
            new[] { "Carte", "Map", "Select", ",", ",", "—", "—" },
            new[] { "Pause", "Pause", "Start", "Échap / P", "Esc / P", "II", "II" },
        };

        // Commande réglable (GameInput.Bind) de chaque ligne de Controls : -1 = fixe (colonnes ci-dessus), -2 = direction
        static readonly int[] ControlBinds =
        {
            (int)GameInput.Bind.Throttle, (int)GameInput.Bind.Brake, -2, (int)GameInput.Bind.Handbrake, (int)GameInput.Bind.Lights,
            (int)GameInput.Bind.Flash, -1, (int)GameInput.Bind.Camera, (int)GameInput.Bind.Horn, (int)GameInput.Bind.LookBack,
            (int)GameInput.Bind.Reset, (int)GameInput.Bind.Music, (int)GameInput.Bind.Map, -1,
        };

        GameManager game;
        PlayerSlot slot;
        Font font;
        RectTransform screen;   // canevas plein écran (pause)
        RectTransform canvas;   // bande d'écran du joueur, où sont posés les éléments du HUD
        Image flash;
        float flashAlpha;

        // Argent et combo
        Text scoreText, bestText;
        RectTransform comboFill;
        float displayedScore = -1f, stakePunch, comboAlpha;
        CanvasGroup comboGroup;
        // Derniers gains et cascades : lignes discrètes sous le compteur du combo, la plus récente en haut
        class FeedLine { public RectTransform rt; public CanvasGroup group; public Text text; public float timer, time; }
        readonly List<FeedLine> feed = new List<FeedLine>();
        RectTransform feedRt;
        const int FeedLines = 3;
        const float FeedStep = 24f, FeedTime = 1.6f;
        // Annonces (haut centre, sous le combo) : titre en couleur et détail discret (écart au record, cascades…)
        RectTransform announceRt;
        CanvasGroup announceGroup;
        Text announceTitle, announceDetail;
        GlassRect announcePlate;
        Image announceStripe;
        float announceTimer, announceTime, announcePunch;
        int walletSlide = -1;
        static readonly Vector2 WalletHome = new Vector2(36f, -30f);        // pilule heure | argent, en haut à gauche
        static readonly Vector2 WalletTouchHome = new Vector2(338f, -30f);  // à droite du bouton pause tactile

        // Heure (dans la pilule de l'argent)
        Text clockText;
        GlassRect statusPill;
        Image statusDivider;

        // Compteur
        Text speedText, gearText, distText, lightsText, assistLamp;
        float assistLampT;
        Image rpmArc;

        // Bas centre : drift, invite d'entrée
        CanvasGroup driftGroup;
        Text driftText, driftChain, driftTitle;
        Image driftBarImage;
        RectTransform driftBar;
        float driftAlpha;
        CanvasGroup dealerGroup;
        Text dealerText;

        // Bas gauche
        CanvasGroup musicGroup;
        Text musicText;
        RectTransform musicRt;
        float musicTimer;
        int gaugeSlide = -1;
        bool gaugeTouch;
        static readonly Vector2 GaugeTouchHome = new Vector2(-140f, 478f);
        const float GaugeTouchScale = 0.72f;
        Vector2 musicHome;

        // Minicarte (bas gauche) ; commandes tactiles affichées : en haut à gauche, à côté des boutons, plus petite
        int miniSlide = -1;
        static readonly Vector2 MiniHome = new Vector2(26f + MiniMap.Size * 0.5f, 26f + MiniMap.Size * 0.5f);
        static readonly Vector2 MiniTouchHome = new Vector2(356f, -192f);
        const float MiniTouchScale = 0.62f;
        const float BottomLeftX = 26f + MiniMap.Size + 24f;   // le morceau et l'aide commencent à droite de la minicarte

        // Arrivée du HUD au « GO » : éléments qui glissent depuis les bords
        struct Slide { public RectTransform rt; public Vector2 home, from; }
        readonly List<Slide> slides = new List<Slide>();
        float slideT = -1f;

        // Pause
        GameObject pausePanel;
        Text netLabel, pauseTitle;
        bool inPause;
        Text controlsTitle;
        readonly List<(Text action, Text key)> controlRows = new List<(Text, Text)>();
        int controlsShown = -1;   // vue, appareil, langue, commandes affichés ; -1 = à construire
        // Vues du panneau des contrôles : liste, manette dessinée, clavier dessiné (onglets en haut à droite, LB / RB)
        const int ViewList = 0, ViewPad = 1, ViewKeys = 2, ViewPlayers = -10;
        static int controlsView = -1;   // -1 : selon l'appareil utilisé en dernier
        RectTransform controlsList, controlsArt;
        readonly List<(Button button, HudShape plate, Text text, int view)> viewChips = new List<(Button, HudShape, Text, int)>();
        Button firstButton;
        int openedFrame;
        // Tuiles du menu (pictogramme, nom, détail) ; la voiture ouvre le garage (CarPicker)
        // Tuiles (verre, pictogramme, bande de légende) et pilules du bas (pictogramme et nom)
        class PauseTile
        {
            public Button button; public GlassRect plate, caption; public RectTransform content; public HudIcon icon;
            public Text label, sub; public float on, x; public bool pill; public Vector2 size;
        }
        readonly List<PauseTile> pauseTiles = new List<PauseTile>();
        readonly List<PauseTile> pausePills = new List<PauseTile>();
        PauseTile carTile, timeTile, inviteTile, trophyTile, mapTile, controlsPill;
        Text trophyBadge, timeBadge, pauseKicker, pauseStatus, pauseHint;
        readonly Dictionary<Text, GlassRect> badgePlates = new Dictionary<Text, GlassRect>();
        RawImage mapPreview;
        RectTransform mapDot, pauseRoot, tilesRoot, pillsRoot;
        MenuBackdrop pauseBackdrop;
        bool controlsPage;
        CarPicker picker;
        AchievementsMenu achievementsMenu;
        TimeWeatherMenu timeWeather;
        static readonly Color TileFill = new Color(0.063f, 0.078f, 0.102f, 0.5f);
        static readonly Color TileNormal = new Color(0.13f, 0.15f, 0.19f, 0.96f);
        static readonly Color TileGold = new Color(0.95f, 0.62f, 0.12f, 1f);
        static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        // Menu pause (canevas 1920 × 1080) : en tête, grille de tuiles 4 × 2, rangée de pilules
        const float PauseW = 1560f, PauseH = 900f, GridTop = 140f, GridH = 660f, GridGap = 24f, PillH = 70f;
        /// <summary>Pente des plaques du menu (style verre : aucune).</summary>
        public const float MenuLean = 0f;
        const float ControlsW = 740f, ControlsLeft = -ControlsW * 0.5f;
        /// <summary>Centre horizontal du panneau des contrôles et du tableau des joueurs en réseau (au centre de l'écran).</summary>
        public const float ControlsCenterX = 0f;
        /// <summary>Hauteur du panneau des contrôles (et du tableau des joueurs dans le menu).</summary>
        public static float MenuHeight { get; private set; } = 736f;
        GlassRect controlsPlate;
        RectTransform controlsPanelRt;
        float netRefresh, inviteCopied = -10f, inviteClock;
        string inviteAnnounced;
        /// <summary>
        /// Menu pause (ou options ouvertes depuis la pause), carte : le joueur ne conduit pas. Seul, le monde est arrêté ;
        /// en réseau, il continue.
        /// </summary>
        /// <summary>Photo du radar, en haut à droite.</summary>
        public RadarPolaroid Polaroid { get; private set; }
        public bool Paused => WorldMap.IsOpen || inPause || Dealership.IsOpen || TuningGarage.IsOpen || RaceSetup.IsOpen;
        /// <summary>Menu pause ouvert sur ses tuiles (pas sur un sous-écran).</summary>
        public bool PauseTilesShown => inPause && pausePanel && pausePanel.activeSelf && !controlsPage;

        // Partie en réseau : à droite du menu, le tableau des joueurs (PlayerList) prend la place des contrôles
        GameObject controlsPanel;
        bool playersPage = true;
        /// <summary>Menu d'une partie en réseau ouvert sur le tableau des joueurs (à la place des contrôles).</summary>
        public bool PlayersInMenu => inPause && controlsPage && pausePanel && pausePanel.activeSelf && game.Online && playersPage;
        /// <summary>Menu d'une partie en réseau : montre à droite le tableau des joueurs, ou les contrôles.</summary>
        public void ShowPlayersInMenu(bool players) => playersPage = players;

        /// <summary>Masque le HUD (chargement, écran d'accueil).</summary>
        public void SetVisible(bool visible)
        {
            if (screen) screen.gameObject.SetActive(visible);
        }

        /// <summary>HUD du joueur de cette machine.</summary>
        public void Init(GameManager gm)
        {
            game = gm;
            slot = gm.Local;
            font = HudArt.Font;
            var eventSystem = FindAnyObjectByType<EventSystem>();
            if (!eventSystem)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                eventSystem = es.GetComponent<EventSystem>();
            }
            GameInput.LeftStickMenus(eventSystem.GetComponent<InputSystemUIInputModule>());
            var cgo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            screen = (RectTransform)cgo.transform;
            canvas = Rect(screen, "Region", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Flash plein écran (radar, accident)
            flash = Rect(canvas, "Flash", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            flash.color = new Color(1, 1, 1, 0);
            flash.raycastTarget = false;

            BuildScore();
            BuildGauge();
            BuildMiniMap();
            BuildCenter();
            BuildBottomLeft();
            BuildPause();
            Polaroid = gameObject.AddComponent<RadarPolaroid>();
            Polaroid.Init(gm, canvas);
            gameObject.AddComponent<ProvingGround>().Init(gm);
            gameObject.AddComponent<Airfield>().Init(gm);
            gameObject.AddComponent<CheatCodes>().Init(gm);
            // Succès : suivi en jeu, notification en bas à droite (et son carillon)
            gameObject.AddComponent<Achievements>().Init(gm);
            gameObject.AddComponent<AchievementToast>().Init();

            // Gains : la cagnotte en jeu réagit, une ligne discrète nomme le gain ; accident : flash rouge
            slot.Score.NearMiss += (pts, gap) =>
            {
                stakePunch = 0.25f;
                Feed(gap < 0.6f ? Loc.T("FRÔLEMENT EXTRÊME", "EXTREME NEAR MISS") : Loc.T("FRÔLEMENT", "NEAR MISS"), pts);
            };
            slot.Score.Bonus += (pts, label) => Feed(label, pts);
            slot.Score.DriftScored += pts => { stakePunch = 0.25f; Feed("DRIFT", pts); };
            slot.Score.JumpScored += (pts, air, dist) =>
            {
                stakePunch = 0.25f;
                string label = air >= 2.5f ? Loc.T("MÉGA SAUT", "MEGA JUMP") : air >= 1.5f ? Loc.T("GROS SAUT", "BIG JUMP") : Loc.T("SAUT", "JUMP");
                Feed(Loc.F($"{label} · {air:0.0} s · {dist:0} m"), pts);
            };
            slot.Score.Lost += (pts, why) => Flash(new Color(1f, 0.1f, 0.05f), 0.35f);
            if (game.Music) game.Music.TrackChanged += name => { if (!GameSettings.TrackNames) return; musicText.text = "<color=#6FDCFF>♪</color>   " + name; musicTimer = 5f; };
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Haut gauche : heure et argent dans une pilule de verre (à droite du bouton pause quand les commandes tactiles
        /// sont affichées). Haut centre, seulement pendant un combo : cagnotte en jeu (perdue en cas d'accident) et fine
        /// jauge dorée du combo, sans plaque ; dessous, les derniers gains puis les annonces sur une plaque de verre.
        /// </summary>
        void BuildScore()
        {
            var status = Glass.Place(canvas, "Status", new Vector2(0f, 1f), WalletHome, new Vector2(320f, 60f));
            statusPill = Glass.Backdrop(status, 30f);
            clockText = Glass.Label(status, "", 30, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(220f, 54f));
            statusDivider = Glass.Bar(status, "Divider", Glass.A(Color.white, 0.4f));
            var dr = statusDivider.rectTransform;
            dr.anchorMin = dr.anchorMax = dr.pivot = new Vector2(0f, 0.5f);
            dr.sizeDelta = new Vector2(1.5f, 26f);
            scoreText = Glass.Label(status, "", 30, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(300f, 54f));
            walletSlide = slides.Count;
            AddSlide(status, new Vector2(-420f, 0f));

            // Combo : n'apparaît que pendant un combo
            var combo = Rect(canvas, "Combo", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-260f, -100f), new Vector2(260f, -24f));
            comboGroup = combo.gameObject.AddComponent<CanvasGroup>();
            comboGroup.alpha = 0f;
            comboGroup.blocksRaycasts = false;
            bestText = Glass.Label(combo, "", 34, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(520f, 44f));
            bestText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Glass.Soft(bestText);

            // Jauge du combo : fine ligne dorée qui se vide
            var comboBar = Rect(combo, "Timer", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-135f, -55f), new Vector2(135f, -52f));
            comboBar.gameObject.AddComponent<Image>().color = Glass.A(Color.white, 0.3f);
            comboFill = Rect(comboBar, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            comboFill.pivot = new Vector2(0f, 0.5f);
            comboFill.gameObject.AddComponent<Image>().color = Glass.Gold;
            AddSlide(combo, new Vector2(0f, 220f));

            // Derniers gains, sous le combo (indépendants de son fondu : le texte reste lisible si le combo s'arrête)
            feedRt = Rect(canvas, "Feed", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            feedRt.pivot = new Vector2(0.5f, 1f);
            feedRt.sizeDelta = new Vector2(700f, FeedStep * FeedLines);
            feedRt.anchoredPosition = new Vector2(0f, -86f);
            for (int i = 0; i < FeedLines; i++)
            {
                var rt = Rect(feedRt, "Line", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(700f, FeedStep);
                var group = rt.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                var t = Glass.Label(rt, "", 22, Glass.Weight.Semilight, Glass.A(Color.white, 0.92f), TextAnchor.UpperCenter, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(700f, FeedStep));
                Glass.Soft(t);
                feed.Add(new FeedLine { rt = rt, group = group, text = t });
            }

            // Annonces : sous le combo et ses lignes de gains, sur une plaque de verre taillée sur le texte
            announceRt = Rect(canvas, "Announce", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            announceRt.pivot = new Vector2(0.5f, 1f);
            announceRt.sizeDelta = new Vector2(1000f, 72f);
            announceRt.anchoredPosition = new Vector2(0f, -200f);
            announceGroup = announceRt.gameObject.AddComponent<CanvasGroup>();
            announceGroup.alpha = 0f;
            announceGroup.blocksRaycasts = false;
            announcePlate = Glass.Make(announceRt, "Plate", 32f);
            var pr = announcePlate.rectTransform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 1f);
            pr.anchoredPosition = new Vector2(0f, 14f);
            announceStripe = Glass.Bar(announceRt, "Line", Glass.Gold);
            var sr = announceStripe.rectTransform;
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(0.5f, 1f);
            announceTitle = Glass.Label(announceRt, "", 38, Glass.Weight.Light, Glass.White, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1000f, 48f));
            announceDetail = Glass.Label(announceRt, "", 23, Glass.Weight.Semilight, Glass.Dim, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(1000f, 30f));
        }

        /// <summary>Pilule du haut à gauche : heure, séparateur, argent ; sa largeur suit les textes.</summary>
        void LayoutStatus()
        {
            float cw = Mathf.Ceil(clockText.preferredWidth);
            float x = 30f + cw + 22f;
            statusDivider.rectTransform.anchoredPosition = new Vector2(x, 0f);
            scoreText.rectTransform.anchoredPosition = new Vector2(x + 22f, 0f);
            float w = x + 22f + Mathf.Ceil(scoreText.preferredWidth) + 32f;
            var rt = (RectTransform)statusPill.transform.parent;
            rt.sizeDelta = new Vector2(Mathf.Max(200f, w), 60f);
        }

        /// <summary>
        /// Bas droite : compte-tours en anneau fin (arc de 270°, zone rouge en fin d'arc), vitesse en chiffres fins au
        /// centre ; dessous, phares, rapport (doré) et distance.
        /// </summary>
        void BuildGauge()
        {
            var root = Rect(canvas, "Gauge", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-361f, 31f), new Vector2(-41f, 351f));
            Img(root, "Back", HudArt.SoftDisc, new Vector2(330f, 330f), Vector2.zero, new Color(0.03f, 0.04f, 0.06f, 0.32f));
            var arcBack = Img(root, "ArcBack", Glass.Ring(0.022f), new Vector2(290f, 290f), Vector2.zero, Glass.A(Color.white, 0.3f));
            Arc(arcBack, 0f, 0.75f);
            var red = Img(root, "Redline", Glass.Ring(0.022f), new Vector2(290f, 290f), Vector2.zero, Glass.Bad);
            Arc(red, 0.9f * 0.75f, 0.1f * 0.75f);
            rpmArc = Img(root, "Rpm", Glass.Ring(0.052f), new Vector2(290f, 290f), Vector2.zero, Glass.White);
            Arc(rpmArc, 0f, 0f);
            speedText = Glass.Label(root, "0", 124, Glass.Weight.Light, Glass.White, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(300f, 130f));
            Glass.Soft(speedText, 0.4f);
            var unit = Glass.Label(root, Glass.Spaced("KM/H", 2), 21, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(160f, 28f));
            Glass.Soft(unit, 0.4f);
            // Dessous : phares, rapport, distance
            gearText = Glass.Label(root, "1", 46, Glass.Weight.Light, Glass.Gold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -124f), new Vector2(70f, 56f));
            Glass.Soft(gearText, 0.4f);
            lightsText = Glass.Label(root, "", 19, Glass.Weight.Regular, Glass.Faint, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f), new Vector2(-40f, -122f), new Vector2(130f, 26f));
            lightsText.rectTransform.pivot = new Vector2(1f, 0.5f);
            distText = Glass.Label(root, "", 19, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(40f, -122f), new Vector2(130f, 26f));
            distText.rectTransform.pivot = new Vector2(0f, 0.5f);
            Glass.Soft(distText, 0.4f);
            // Témoin d'ESP / d'antipatinage : clignote quand l'assistance intervient, invisible sinon
            assistLamp = Glass.Label(root, "", 19, Glass.Weight.Semibold, Glass.Gold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 88f), new Vector2(90f, 26f));
            gaugeSlide = slides.Count;
            AddSlide(root, new Vector2(420f, 0f));
        }

        /// <summary>Bas gauche : minicarte tournante (voir <see cref="MiniMap"/>), pendant du compte-tours.</summary>
        void BuildMiniMap()
        {
            var root = Rect(canvas, "MiniMap", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            root.sizeDelta = new Vector2(MiniMap.Size, MiniMap.Size);
            root.anchoredPosition = MiniHome;
            root.gameObject.AddComponent<MiniMap>().Init(game, slot);
            miniSlide = slides.Count;
            AddSlide(root, new Vector2(-420f, 0f));
        }

        void BuildCenter()
        {
            // Drift en cours (ou saut) : petite plaque fixe en bas au centre, entre la minicarte et le compte-tours
            // (hors de la vue de la route) : titre en couleur, points en attente en chiffres fins, jauge d'angle fine
            var drift = Rect(canvas, "Drift", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170f, 28f), new Vector2(170f, 114f));
            driftGroup = drift.gameObject.AddComponent<CanvasGroup>();
            driftGroup.alpha = 0f;
            Glass.Backdrop(drift, 26f, Glass.A(Glass.Panel, 0.5f));
            driftTitle = Glass.Label(drift, "DRIFT", 13, Glass.Weight.Semibold, DriftColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -9f), new Vector2(200f, 18f));
            driftText = Glass.Label(drift, "", 36, Glass.Weight.Light, Glass.White, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(300f, 44f));
            driftChain = Glass.Label(drift, "", 18, Glass.Weight.Regular, DriftColor, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(92f, -6f), new Vector2(90f, 24f));
            driftChain.rectTransform.pivot = new Vector2(0f, 0.5f);
            var barBg = Rect(drift, "AngleBg", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-80f, 10f), new Vector2(80f, 12.5f));
            barBg.gameObject.AddComponent<Image>().color = Glass.A(Color.white, 0.2f);
            driftBar = Rect(barBg, "Angle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            driftBar.pivot = new Vector2(0f, 0.5f);
            driftBarImage = driftBar.gameObject.AddComponent<Image>();
            driftBarImage.color = DriftColor;

            // Concession et garage de tuning : aux abords, rappel d'entrer en voiture par la porte
            var dealer = Rect(canvas, "DealerPrompt", new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(-360f, -35f), new Vector2(360f, 35f));
            dealerGroup = dealer.gameObject.AddComponent<CanvasGroup>();
            dealerGroup.alpha = 0f;
            dealerGroup.blocksRaycasts = dealerGroup.interactable = false;
            Glass.Backdrop(dealer, 35f);
            dealerText = Glass.Label(dealer, "", 26, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 60f));
        }

        void BuildBottomLeft()
        {
            // Morceau en cours : entre en glissant, puis s'efface (sans plaque, note cyan)
            musicRt = Rect(canvas, "Music", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(BottomLeftX, 30f), new Vector2(BottomLeftX + 420f, 70f));   // s'arrête avant la plaque de drift
            musicHome = musicRt.anchoredPosition;
            musicGroup = musicRt.gameObject.AddComponent<CanvasGroup>();
            musicGroup.alpha = 0f;
            musicText = Glass.Label(musicRt, "", 22, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(410f, 34f));
            Glass.Soft(musicText);

        }

        // ------------------------------------------------------------------ mise à jour

        void Update()
        {
            if (game == null) return;
            float dt = Time.unscaledDeltaTime;
            var car = slot.Car;
            var score = slot.Score;

            // Compteur
            speedText.text = Mathf.RoundToInt(car.SpeedKmh).ToString();
            gearText.text = car.Gear < 0 ? "R" : car.Gear.ToString();
            float rpm = car.Rpm01;
            rpmArc.fillAmount = Mathf.Clamp01(rpm) * 0.75f;
            rpmArc.color = rpm > 0.9f ? Glass.Bad : rpm > 0.72f ? Color.Lerp(Glass.White, Glass.Gold, (rpm - 0.72f) / 0.18f) : Glass.White;
            // Témoin de passage de rapport : le rapport clignote au rupteur
            bool shift = rpm > 0.93f && car.Gear > 0;
            gearText.color = shift && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f ? Glass.Bad : Glass.Gold;
            distText.text = Loc.F($"{score.RunDistance / 1000f:0.0} km");
            // Témoin des phares : allumé aussi le temps d'un appel de phares
            bool lightsOn = slot.Lights && (slot.Lights.On || slot.Lights.Flashing);
            lightsText.text = Loc.T("PHARES", "LIGHTS");
            lightsText.color = lightsOn ? Glass.Cyan : Glass.A(Color.white, 0.4f);
            bool esp = car.EspIntervention > 0.05f, tcs = car.TcsIntervention > 0.05f;
            if (esp || tcs) { assistLampT = 0.45f; assistLamp.text = esp ? "ESP" : "TCS"; }
            assistLampT -= dt;
            float lampOn = assistLampT > 0f && Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.6f ? 1f : 0f;
            assistLamp.color = Glass.A(Glass.Gold, lampOn * 0.95f);

            // Argent : le compteur monte jusqu'à la somme encaissée
            float cash = Wallet.Cash;
            if (displayedScore < 0f) displayedScore = cash;
            displayedScore = Mathf.Lerp(displayedScore, cash, 1f - Mathf.Exp(-dt * 6f));
            if (Mathf.Abs(displayedScore - cash) < 1f) displayedScore = cash;
            scoreText.text = Wallet.Format(displayedScore);
            scoreText.color = Mathf.Abs(displayedScore - cash) > 0.5f ? Glass.Good : Glass.Gold;
            // Combo : multiplicateur, temps restant, cagnotte en jeu
            bool combo = score.ComboActive;
            comboAlpha = Mathf.MoveTowards(comboAlpha, combo ? 1f : 0f, dt * (combo ? 6f : 2.5f));
            comboGroup.alpha = comboAlpha;
            stakePunch = Mathf.MoveTowards(stakePunch, 0f, dt * 1.2f);
            if (combo)
            {
                bool kept = score.Drifting || score.Speeding || score.Jumping;
                comboFill.localScale = new Vector3(kept ? 1f : Mathf.Clamp01(score.ComboTimer / score.comboWindow), 1f, 1f);
                bestText.text = "<color=#FFC233>+" + Wallet.Format(score.AtStake) + "</color>" + Loc.T("  en jeu", "  at stake");
            }
            bestText.rectTransform.localScale = Vector3.one * (1f + stakePunch);
            // Derniers gains : chaque ligne apparaît vite, reste, puis s'efface ; les anciennes descendent d'un cran
            for (int i = 0; i < feed.Count; i++)
            {
                var line = feed[i];
                if (line.timer > 0f)
                {
                    line.timer = Mathf.Max(0f, line.timer - dt);
                    float shown = line.time - line.timer;
                    line.group.alpha = Mathf.Min(Mathf.Clamp01(shown / 0.12f), Mathf.Clamp01(line.timer / 0.5f)) * (i == 0 ? 1f : 0.75f);
                    line.rt.localScale = Vector3.one * Mathf.Lerp(1.08f, 1f, Mathf.Clamp01(shown / 0.15f));
                }
                else line.group.alpha = 0f;
                float y = Mathf.Lerp(line.rt.anchoredPosition.y, -i * FeedStep, 1f - Mathf.Exp(-dt * 14f));
                line.rt.anchoredPosition = new Vector2(0f, y);
            }
            // Annonce : arrive en grand puis se pose, reste, s'efface
            if (announceTimer > 0f)
            {
                announceTimer = Mathf.Max(0f, announceTimer - dt);
                float shown = announceTime - announceTimer;
                announceGroup.alpha = Mathf.Min(Mathf.Clamp01(shown / 0.12f), Mathf.Clamp01(announceTimer / 0.5f));
                float settle = 1f - Mathf.Clamp01(shown / 0.22f);
                announceRt.localScale = Vector3.one * (1f + announcePunch * settle * settle);
            }
            else announceGroup.alpha = 0f;

            // Heure (icône soleil ou lune)
            if (clockText && game.DayNight)
                clockText.text = "<color=#FFFFFFB8>" + (game.Weather && game.Weather.Storm > 0.5f ? "☂" : game.DayNight.Night > 0.5f ? "☾" : "☀") + "</color>  " + game.DayNight.Clock;
            LayoutStatus();

            // Drift : ce que rapportera le drift (multiplicateur compris) ; le compteur grossit avec l'angle
            // En l'air, le même cadre suit le saut : gain à la réception, distance, temps en l'air
            bool showJump = score.Jumping && !Paused;
            bool showDrift = !showJump && score.Drifting && score.DriftPoints >= 8f && !Paused;
            if (showJump || showDrift)   // pendant le fondu de sortie, le cadre garde l'allure du dernier affiché
            {
                Color tint = showJump ? JumpColor : DriftColor;
                driftTitle.text = Glass.Spaced(showJump ? Loc.T("SAUT", "JUMP") : "DRIFT", 2);
                driftTitle.color = driftChain.color = driftBarImage.color = tint;
            }
            if (showJump)
            {
                driftText.text = "+" + Wallet.Format(score.JumpPoints);
                driftChain.text = Loc.F($"{score.JumpDistance:0} m");
                driftBar.localScale = new Vector3(Mathf.Clamp01(score.JumpTime / 3f), 1f, 1f);
                driftText.rectTransform.localScale = Vector3.one * (1f + Mathf.Clamp01(score.JumpTime / 3f) * 0.18f);
                // Cascade (saut depuis un tremplin) : distance parcourue sur la distance à franchir, vert une fois atteinte
                var stunt = game.Stunts ? game.Stunts.Active : null;
                if (stunt != null)
                {
                    float reach = Mathf.Clamp01(score.JumpDistance / stunt.Target);
                    Color tint = reach >= 1f ? StuntJumps.DoneTint : StuntJumps.Tint;
                    driftTitle.text = Glass.Spaced(stunt.done ? Loc.T("TREMPLIN", "RAMP") : Loc.T("CASCADE", "STUNT"), 2);
                    driftTitle.color = driftChain.color = driftBarImage.color = tint;
                    driftChain.text = Loc.F($"{Mathf.Floor(score.JumpDistance):0} / {stunt.Target:0} m");
                    driftBar.localScale = new Vector3(reach, 1f, 1f);
                }
            }
            else if (showDrift)
            {
                driftText.text = "+" + Wallet.Format(score.DriftPoints);
                driftChain.text = "";
                driftBar.localScale = new Vector3(Mathf.Clamp01((score.DriftAngle - 12f) / 48f), 1f, 1f);
                driftText.rectTransform.localScale = Vector3.one * (1f + Mathf.Clamp01((score.DriftAngle - 12f) / 48f) * 0.18f);
            }
            bool showPanel = showDrift || showJump;
            driftAlpha = Mathf.MoveTowards(driftAlpha, showPanel ? 1f : 0f, dt * (showPanel ? 8f : 3f));
            driftGroup.alpha = driftAlpha;

            // Concession ou garage de tuning : on y entre en voiture
            bool tuning = TuningGarage.Current && TuningGarage.Current.Near && !Paused;
            bool dealer = tuning || (game.Dealer && game.Dealer.Near && !Paused);
            if (dealer)
            {
                string hex = ColorUtility.ToHtmlStringRGB(tuning ? Glass.Gold : Glass.Cyan);
                dealerText.text = tuning
                    ? Loc.T($"<color=#{hex}>GARAGE DE TUNING</color>    roulez jusqu'à la porte", $"<color=#{hex}>TUNING GARAGE</color>    drive up to the door")
                    : Loc.T($"<color=#{hex}>CONCESSION</color>    roulez jusqu'à la porte", $"<color=#{hex}>CAR DEALER</color>    drive up to the door");
            }
            dealerGroup.alpha = Mathf.MoveTowards(dealerGroup.alpha, dealer ? 1f : 0f, dt * 5f);

            if (flashAlpha > 0f)
            {
                flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, dt * 2.2f);
                var c = flash.color; c.a = flashAlpha; flash.color = c;
            }
            UpdateTouchLayout();
            UpdateSlides(dt);
            UpdateBottomLeft(dt);
            UpdateInvite(dt);
            UpdatePause();
        }

        /// <summary>
        /// Partie hébergée : le code d'invitation est annoncé dès qu'il est obtenu (une fois le HUD arrivé, après le
        /// « GO »), et son absence aussi (pas d'Internet, services en panne, code expiré) : la partie reste alors
        /// ouverte en réseau local et par adresse IP.
        /// </summary>
        void UpdateInvite(float dt)
        {
            var net = game.Net;
            if (!net || !net.IsHost || GameManager.DedicatedServer || game.State != GameManager.Phase.Playing)
            {
                inviteClock = 0f;
                if (!net || !net.IsHost) inviteAnnounced = null;
                return;
            }
            inviteClock += dt;
            if (inviteClock < 1.5f || Paused) return;
            string now = net.InviteCode ?? (net.InviteError != null ? "!" + net.InviteError : null);
            if (now == null || now == inviteAnnounced) return;
            inviteAnnounced = now;
            if (net.InviteCode != null)
                Announce(Loc.T("CODE D'INVITATION", "INVITE CODE") + "  ·  " + net.InviteCode,
                    Loc.T("donne-le à tes amis pour qu'ils te rejoignent  ·  il se copie depuis le menu", "give it to your friends so they can join  ·  copy it from the menu"), Info, 9f);
            else
                Announce(Loc.T("PAS DE CODE D'INVITATION", "NO INVITE CODE"),
                    net.InviteError + Loc.T("  ·  la partie reste ouverte en réseau local et par adresse IP", "  ·  the game stays open on the local network and by IP address"), Danger, 7f);
        }

        void UpdateBottomLeft(float dt)
        {
            // Morceau : glisse depuis la gauche puis s'efface
            if (musicTimer > 0f)
            {
                musicTimer -= dt;
                float x = Mathf.Clamp01((5f - musicTimer) / 0.35f);
                musicRt.anchoredPosition = musicHome + new Vector2(Mathf.Lerp(-60f, 0f, 1f - (1f - x) * (1f - x)), 0f);
                musicGroup.alpha = Mathf.Min(x, Mathf.Clamp01(musicTimer / 0.8f));
            }
            else musicGroup.alpha = 0f;
        }

        /// <summary>
        /// Commandes tactiles affichées : le compteur remonte au-dessus de la pédale d'accélérateur, un peu plus petit ;
        /// la minicarte laisse la place de la direction et monte en haut à gauche, à côté des boutons.
        /// </summary>
        void UpdateTouchLayout()
        {
            bool touch = TouchInput.Showing;
            if (touch == gaugeTouch || gaugeSlide < 0) return;
            gaugeTouch = touch;
            var s = slides[gaugeSlide];
            s.home = touch ? GaugeTouchHome : new Vector2(-201f, 191f);
            slides[gaugeSlide] = s;
            s.rt.localScale = Vector3.one * (touch ? GaugeTouchScale : 1f);
            if (slideT < 0f) s.rt.anchoredPosition = s.home;
            if (walletSlide >= 0)
            {
                var w = slides[walletSlide];
                w.home = touch ? WalletTouchHome : WalletHome;
                slides[walletSlide] = w;
                if (slideT < 0f) w.rt.anchoredPosition = w.home;
            }
            if (miniSlide < 0) return;
            var m = slides[miniSlide];
            m.rt.anchorMin = m.rt.anchorMax = touch ? new Vector2(0f, 1f) : Vector2.zero;
            m.home = touch ? MiniTouchHome : MiniHome;
            slides[miniSlide] = m;
            m.rt.localScale = Vector3.one * (touch ? MiniTouchScale : 1f);
            if (slideT < 0f) m.rt.anchoredPosition = m.home;
        }

        void AddSlide(RectTransform rt, Vector2 from) => slides.Add(new Slide { rt = rt, home = rt.anchoredPosition, from = from });

        /// <summary>Le HUD arrive en glissant depuis les bords (départ de la partie).</summary>
        public void AnimateIn()
        {
            slideT = 0f;
            UpdateSlides(0f);
        }

        void UpdateSlides(float dt)
        {
            if (slideT < 0f) return;
            slideT += dt;
            for (int i = 0; i < slides.Count; i++)
            {
                // Décalage léger entre les éléments, arrivée avec un petit dépassement
                float x = Mathf.Clamp01((slideT - i * 0.05f) / 0.45f);
                const float c1 = 1.70158f, c3 = c1 + 1f;
                float e = x <= 0f ? 0f : 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
                slides[i].rt.anchoredPosition = slides[i].home + slides[i].from * (1f - e);
            }
            if (slideT > 0.45f + slides.Count * 0.05f + 0.05f)
            {
                foreach (var sl in slides) sl.rt.anchoredPosition = sl.home;
                slideT = -1f;
            }
        }

        /// <summary>Gain sous le combo : libellé discret, montant en couleur.</summary>
        void Feed(string label, float pts) => Feed(label, "+" + Wallet.Format(pts), Glass.Gold);

        /// <summary>
        /// Ligne discrète sous le combo : libellé, puis une valeur en couleur (facultative).
        /// Prend la place en haut ; les précédentes descendent, la plus ancienne disparaît.
        /// </summary>
        void Feed(string label, string value, Color valueColor, float time = FeedTime)
        {
            if (feed.Count == 0) return;
            var line = feed[feed.Count - 1];
            feed.RemoveAt(feed.Count - 1);
            feed.Insert(0, line);
            line.text.text = string.IsNullOrEmpty(value) ? label : $"{label}   <color=#{ColorUtility.ToHtmlStringRGB(valueColor)}>{value}</color>";
            line.timer = line.time = time;
            line.rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Annonce en haut au centre (titres et annonces : écart au record, course, cascades…) : titre dans la couleur
        /// donnée, détail discret en dessous (facultatif), sur une plaque sombre au liseré de la même couleur.
        /// <paramref name="big"/> : titre en très grand (écart au record d'une course). Remplace l'annonce en cours.
        /// </summary>
        public void Announce(string title, string detail, Color color, float time = 2.6f, bool big = false)
        {
            detail ??= "";
            float titleH = big ? 76f : 48f;
            announceTitle.fontSize = big ? 68 : 38;
            announceTitle.rectTransform.sizeDelta = new Vector2(1200f, titleH);
            announceTitle.text = title;
            // Titre en blanc sur le verre ; la couleur de l'annonce passe dans le filet sous le titre et dans le détail
            bool neutral = color.r > 0.9f && color.g > 0.9f && color.b > 0.9f;
            announceTitle.color = Glass.White;
            announceDetail.fontSize = big ? 25 : 23;
            announceDetail.rectTransform.anchoredPosition = new Vector2(0f, -titleH - 4f);
            announceDetail.text = detail;
            announceDetail.color = neutral ? Glass.Dim : Color.Lerp(color, Color.white, 0.35f);
            // Plaque taillée sur le plus large des deux textes
            float w = Mathf.Max(announceTitle.preferredWidth, detail.Length > 0 ? announceDetail.preferredWidth : 0f) + (big ? 110f : 90f);
            float h = titleH + (detail.Length > 0 ? 36f : 0f) + 34f;
            announcePlate.rectTransform.sizeDelta = new Vector2(w, h);
            announceStripe.rectTransform.sizeDelta = new Vector2(Mathf.Min(140f, w * 0.3f), 2f);
            announceStripe.rectTransform.anchoredPosition = new Vector2(0f, -titleH + 2f);
            announceStripe.color = neutral ? Glass.Gold : color;
            announcePunch = big ? 0.3f : 0.12f;
            announceTimer = announceTime = time;
        }

        public void Flash(Color c, float alpha)
        {
            c.a = alpha;
            flash.color = c;
            flashAlpha = alpha;
        }

        // ------------------------------------------------------------------ pause

        /// <summary>
        /// Menu pause du style verre, sur l'image du jeu floutée : en tête le titre (et la voiture), l'heure et l'argent ;
        /// une grande tuile Carte (la carte autour de la voiture) et quatre tuiles Voiture, Options, Heure et météo,
        /// Succès ; en bas, des pilules : Reprendre (choisie à l'ouverture), Commandes, Inviter (hôte en réseau), Menu
        /// principal, Quitter. Commandes ouvre le panneau des contrôles (liste, manette, clavier ; en réseau, les
        /// joueurs) à la place des tuiles ; B / Échap y revient.
        /// </summary>
        void BuildPause()
        {
            pausePanel = Rect(screen, "Pause", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            pauseBackdrop = MenuBackdrop.Attach((RectTransform)pausePanel.transform);
            var root = Glass.Place(pausePanel.transform, "Menu", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PauseW, PauseH));
            pauseRoot = root;
            root.pivot = new Vector2(0.5f, 0.5f);

            // En tête : petit titre (voiture, état en réseau), PAUSE en lettres fines ; à droite l'heure et l'argent
            pauseKicker = Glass.Kicker(root, "", 17);
            Place(pauseKicker.rectTransform, new Vector2(0f, 1f), new Vector2(4f, -4f), new Vector2(900f, 26f));
            pauseTitle = Glass.Label(root, "", 66, Glass.Weight.Light, Glass.White, TextAnchor.UpperLeft);
            Place(pauseTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -28f), new Vector2(900f, 84f));
            var status = Glass.Place(root, "Status", new Vector2(1f, 1f), new Vector2(0f, -22f), new Vector2(300f, 60f));
            Glass.Backdrop(status, 30f);
            pauseStatus = Glass.Label(status, "", 27, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(pauseStatus.rectTransform);
            // Partie en réseau : joueurs, adresse ou code d'invitation, collisions (sous l'heure, à droite)
            netLabel = Glass.Label(root, "", 19, Glass.Weight.Semilight, Glass.Dim, TextAnchor.UpperRight);
            Place(netLabel.rectTransform, new Vector2(1f, 1f), new Vector2(-6f, -92f), new Vector2(900f, 80f));
            netLabel.lineSpacing = 1.05f;

            // Tuiles : grille de 4 colonnes sur 2 rangées, la carte prend 2 × 2
            tilesRoot = Glass.Rect(root, "Tiles", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -GridTop - GridH), new Vector2(0f, -GridTop));
            float colW = (PauseW - 3f * GridGap) / 4f, rowH = (GridH - GridGap) * 0.5f;
            mapTile = Tile(tilesRoot, new Vector2(0f, 0f), new Vector2(colW * 2f + GridGap, GridH), HudIcon.Kind.Map, "Carte", "Map", OpenMap, true);
            Loc.Bind(mapTile.sub, "Lieux, courses, radars, tremplins · GPS", "Places, races, cameras, ramps · GPS");
            float x2 = (colW + GridGap) * 2f, x3 = x2 + colW + GridGap;
            carTile = Tile(tilesRoot, new Vector2(x2, 0f), new Vector2(colW, rowH), HudIcon.Kind.Car, "Voiture", "Car", OpenPicker, false);
            var options = Tile(tilesRoot, new Vector2(x3, 0f), new Vector2(colW, rowH), HudIcon.Kind.Gear, "Options", "Options", OpenOptions, false);
            Loc.Bind(options.sub, "Jeu, graphismes, audio, touches", "Game, graphics, audio, keys");
            timeTile = Tile(tilesRoot, new Vector2(x2, -(rowH + GridGap)), new Vector2(colW, rowH), HudIcon.Kind.Clock, "Heure et météo", "Time and weather", OpenTimeWeather, false);
            trophyTile = Tile(tilesRoot, new Vector2(x3, -(rowH + GridGap)), new Vector2(colW, rowH), HudIcon.Kind.Trophy, "Succès", "Achievements", OpenAchievements, false);
            BuildMapPreview(mapTile);
            trophyBadge = Badge(trophyTile, true);
            timeBadge = Badge(timeTile, false);

            // Pilules du bas
            pillsRoot = Glass.Rect(root, "Pills", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, PillH));
            float px = 0f;
            var resume = Pill(pillsRoot, ref px, HudIcon.Kind.Play, "Reprendre", "Resume", () => SetPaused(false));
            firstButton = resume.button;
            // Plus de pilule Commandes (les touches se voient et se changent dans Options › Touches) ; en réseau seulement,
            // « Joueurs » ouvre le tableau des joueurs à la place des tuiles
            controlsPill = Pill(pillsRoot, ref px, HudIcon.Kind.Invite, "Joueurs", "Players", () => { ShowControlsPage(true); ShowControlsView(ViewPlayers); });
            controlsPill.button.gameObject.SetActive(false);
            inviteTile = Pill(pillsRoot, ref px, HudIcon.Kind.Invite, "Inviter", "Invite", InviteAction);
            Pill(pillsRoot, ref px, HudIcon.Kind.Home, "Menu principal", "Main menu", game.ReturnToTitle);
            Pill(pillsRoot, ref px, HudIcon.Kind.Power, "Quitter", "Quit", game.Quit);
            LayoutPills();   // sans le trou de la pilule Joueurs, cachée hors réseau
            pauseHint = Glass.Label(pillsRoot, "", 21, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleRight);
            Place(pauseHint.rectTransform, new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(560f, PillH));

            BuildControls(pausePanel.transform);
            ApplyLean(MenuHeight);
            controlsPanel.SetActive(false);
            pausePanel.SetActive(false);
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Panneau des contrôles fixé au centre : hauteur fixe, sans pente (style verre).</summary>
        void ApplyLean(float height)
        {
            MenuHeight = height;
            controlsPanelRt.offsetMin = new Vector2(controlsPanelRt.offsetMin.x, -height * 0.5f);
            controlsPanelRt.offsetMax = new Vector2(controlsPanelRt.offsetMax.x, height * 0.5f);
            controlsTitle.rectTransform.anchoredPosition = new Vector2(50f, -22f);
            foreach (var row in controlRows)
                row.action.transform.parent.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 0, 0, 0);
        }

        /// <summary>
        /// Tuile du menu : verre, pictogramme (au centre, ou dans la bande du bas pour la grande tuile), bande de légende
        /// en bas (nom et détail). La tuile sélectionnée prend un liseré doré et un halo (animés dans <see cref="AnimateTiles"/>).
        /// </summary>
        PauseTile Tile(RectTransform parent, Vector2 pos, Vector2 size, HudIcon.Kind kind, string fr, string en, UnityEngine.Events.UnityAction onClick, bool hero)
        {
            var go = new GameObject("Tile", typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button), typeof(HoverSelect));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, new Vector2(0f, 1f), pos, size);
            var t = new PauseTile { size = size };
            t.plate = go.GetComponent<GlassRect>();
            t.plate.radius = hero ? 30f : 26f;
            t.plate.color = TileFill;
            t.plate.borderColor = Glass.Edge;
            t.plate.border = 1.5f;
            t.button = go.GetComponent<Button>();
            t.button.targetGraphic = t.plate;
            t.button.transition = Selectable.Transition.None;
            t.button.onClick.AddListener(onClick);

            float band = hero ? 120f : 96f;
            // Contenu (aperçu de la carte) sous la bande, découpé par un masque arrondi
            t.content = Glass.Rect(rt, "Content", Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            t.caption = Glass.Make(rt, "Caption", hero ? 27f : 23f, new Color(0.063f, 0.078f, 0.102f, 0.62f), 0f);
            var cr = t.caption.rectTransform;
            cr.anchorMin = Vector2.zero; cr.anchorMax = new Vector2(1f, 0f);
            cr.offsetMin = new Vector2(3f, 3f); cr.offsetMax = new Vector2(-3f, band);
            var ig = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
            ig.transform.SetParent(hero ? (Transform)cr : rt, false);
            var ir = (RectTransform)ig.transform;
            t.icon = ig.GetComponent<HudIcon>();
            t.icon.Shape = kind;
            t.icon.raycastTarget = false;
            float tx = 28f;
            if (hero)
            {
                ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0f, 0.5f);
                ir.anchoredPosition = new Vector2(28f, 0f);
                ir.sizeDelta = new Vector2(58f, 58f);
                tx = 110f;
            }
            else
            {
                ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0.5f, 0.5f);
                ir.anchoredPosition = new Vector2(0f, band * 0.5f);
                ir.sizeDelta = new Vector2(96f, 96f);
            }
            t.label = Glass.Label(cr, "", hero ? 40 : 28, Glass.Weight.Light, Glass.White, TextAnchor.LowerLeft);
            Fit(t.label.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(tx, -2f), new Vector2(-20f, hero ? 46f : 38f));
            t.sub = Glass.Label(cr, "", hero ? 21 : 18, Glass.Weight.Semilight, Glass.Dim, TextAnchor.UpperLeft);
            Fit(t.sub.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(tx + 1f, hero ? -36f : -32f), new Vector2(-20f, -4f));
            t.sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.sub.verticalOverflow = VerticalWrapMode.Truncate;
            Loc.Bind(t.label, fr, en);
            pauseTiles.Add(t);
            return t;
        }

        /// <summary>Pilule du bas du menu (pictogramme et nom), posée à la suite des précédentes.</summary>
        PauseTile Pill(RectTransform parent, ref float x, HudIcon.Kind kind, string fr, string en, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Pill", typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button), typeof(HoverSelect));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            var t = new PauseTile { pill = true };
            t.plate = go.GetComponent<GlassRect>();
            t.plate.radius = PillH * 0.5f;
            t.plate.color = Glass.Pill;
            t.plate.borderColor = Glass.Edge;
            t.plate.border = 1.5f;
            t.button = go.GetComponent<Button>();
            t.button.targetGraphic = t.plate;
            t.button.transition = Selectable.Transition.None;
            t.button.onClick.AddListener(onClick);
            var ig = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
            ig.transform.SetParent(rt, false);
            var ir = (RectTransform)ig.transform;
            Place(ir, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(30f, 30f));
            t.icon = ig.GetComponent<HudIcon>();
            t.icon.Shape = kind;
            t.icon.raycastTarget = false;
            t.label = Glass.Label(rt, "", 24, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft);
            Fit(t.label.rectTransform, Vector2.zero, Vector2.one, new Vector2(74f, 0f), new Vector2(-24f, 0f));
            t.sub = t.label;
            Loc.Bind(t.label, fr, en);
            t.x = x;
            Place(rt, new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(PillWidth(t), PillH));
            x += PillWidth(t) + 16f;
            pauseTiles.Add(t);
            pausePills.Add(t);
            return t;
        }

        float PillWidth(PauseTile t) => Mathf.Ceil(t.label.preferredWidth) + 74f + 30f;

        /// <summary>Pilules rangées de gauche à droite (largeur selon la langue ; Inviter : hôte en réseau seulement).</summary>
        void LayoutPills()
        {
            float x = 0f;
            foreach (var p in pausePills)
            {
                if (!p.button.gameObject.activeSelf) continue;
                var rt = (RectTransform)p.button.transform;
                float w = PillWidth(p);
                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta = new Vector2(w, PillH);
                x += w + 16f;
            }
        }

        /// <summary>Pastille en haut à droite d'une tuile (dorée : succès ; verre : heure et météo).</summary>
        Text Badge(PauseTile t, bool gold)
        {
            var rt = Glass.Place(t.button.transform, "Badge", new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(120f, 38f));
            var plate = Glass.Backdrop(rt, 19f, gold ? Glass.Gold : Glass.PanelStrong, gold ? 0f : 1.5f);
            var text = Glass.Label(rt, "", 19, gold ? Glass.Weight.Semibold : Glass.Weight.Regular, gold ? Glass.Ink : Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(text.rectTransform);
            badgePlates[text] = plate;
            return text;
        }

        /// <summary>Largeur d'une pastille d'après son texte.</summary>
        void FitBadge(Text badge)
        {
            var rt = (RectTransform)badge.transform.parent;
            rt.sizeDelta = new Vector2(Mathf.Ceil(badge.preferredWidth) + 34f, 38f);
        }

        /// <summary>Aperçu de la carte dans la grande tuile : la carte du massif autour de la voiture, point doré au centre.</summary>
        void BuildMapPreview(PauseTile t)
        {
            // Masque aux coins arrondis comme la tuile (rayon de la tuile moins le retrait du contenu)
            var clip = Glass.Make(t.content, "MapClip", 27f, Color.white, 0f);
            Glass.Stretch(clip.rectTransform);
            clip.raycastTarget = false;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            clip.transform.SetAsFirstSibling();
            var raw = new GameObject("Map", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            raw.transform.SetParent(clip.transform, false);
            Glass.Stretch(raw.rectTransform);
            raw.texture = Resources.Load<Texture2D>("Map/map_overview");
            raw.raycastTarget = false;
            raw.color = raw.texture ? Color.white : new Color(0.1f, 0.12f, 0.15f, 1f);
            mapPreview = raw;
            var dot = Img(clip.rectTransform, "Car", HudArt.Disc, new Vector2(22f, 22f), new Vector2(0f, 60f), Glass.Gold);
            var halo = Img(dot.rectTransform, "Halo", HudArt.SoftDisc, new Vector2(64f, 64f), Vector2.zero, Glass.A(Glass.Gold, 0.45f));
            halo.rectTransform.SetAsFirstSibling();
            Img(dot.rectTransform, "Core", HudArt.Disc, new Vector2(10f, 10f), Vector2.zero, Glass.Ink);
            mapDot = dot.rectTransform;
        }

        /// <summary>Carte de la tuile recentrée sur la voiture (un rectangle d'environ 2,4 km de large).</summary>
        void RefreshMapPreview()
        {
            if (!mapPreview || !mapPreview.texture || game.Map == null || !slot.Car) return;
            float half = game.Map.Half;
            var p = slot.Car.transform.position;
            var r = mapPreview.rectTransform.rect;
            float w = Mathf.Clamp(2400f / (half * 2f), 0.05f, 1f), h = w * (r.height / Mathf.Max(1f, r.width));
            // La voiture un peu au-dessus de la bande de légende
            float bandShare = 120f / Mathf.Max(1f, r.height);
            float u = (p.x + half) / (half * 2f), v = (p.z + half) / (half * 2f);
            float vc = v - (bandShare * 0.5f) * h;
            mapPreview.uvRect = new Rect(u - w * 0.5f, vc - h * 0.5f, w, h);
            mapDot.anchoredPosition = new Vector2(0f, r.height * bandShare * 0.5f);
        }

        /// <summary>
        /// État des tuiles et des pilules : la sélectionnée prend l'or (liseré et halo pour une tuile ; fond doré et
        /// texte à l'encre pour une pilule) ; grisées si indisponibles.
        /// </summary>
        void AnimateTiles(float dt)
        {
            var sel = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var t in pauseTiles)
            {
                if (!t.button.gameObject.activeInHierarchy) continue;
                bool usable = t.button.interactable;
                t.on = dt > 0f ? Mathf.MoveTowards(t.on, usable && sel == t.button.gameObject ? 1f : 0f, dt * 9f) : 0f;
                float e = t.on * t.on * (3f - 2f * t.on);
                if (t.pill)
                {
                    t.plate.color = usable ? Color.Lerp(Glass.Pill, Glass.Gold, e) : Glass.A(Color.white, 0.05f);
                    t.plate.borderColor = Color.Lerp(Glass.Edge, Glass.A(Glass.Gold, 0f), e);
                    t.plate.halo = 9f * e;
                    t.plate.haloColor = Glass.A(Glass.Gold, 0.3f * e);
                    Color fg = usable ? Color.Lerp(Glass.White, Glass.Ink, e) : Glass.Faint;
                    t.label.color = fg;
                    t.icon.SetColors(fg, Color.Lerp(new Color(0.18f, 0.2f, 0.24f), Glass.Gold, e));
                }
                else
                {
                    t.plate.color = usable ? TileFill : Glass.A(TileFill, 0.3f);
                    t.plate.borderColor = Color.Lerp(Glass.Edge, Glass.Gold, e);
                    t.plate.border = 1.5f + 1f * e;
                    t.plate.halo = 12f * e;
                    t.plate.haloColor = Glass.A(Glass.Gold, 0.3f * e);
                    t.label.color = usable ? Color.Lerp(Glass.White, Glass.Gold, e) : Glass.Faint;
                    t.sub.color = usable ? Glass.Dim : Glass.Faint;
                    t.icon.SetColors(usable ? Color.Lerp(Glass.White, Glass.Gold, e) : Glass.Faint, new Color(0.1f, 0.12f, 0.15f));
                }
                t.plate.SetVerticesDirty();
                t.button.transform.localScale = Vector3.one * (1f + (t.pill ? 0.02f : 0.012f) * e);
            }
        }

        /// <summary>Garage (tuile Voiture) : choisir la voiture parmi celles achetées ; on repart dès qu'une est prise.</summary>
        void OpenPicker()
        {
            if (game.OwnedCars <= 1 || (game.Races && game.Races.CarLocked)) return;
            if (!picker)
            {
                picker = gameObject.AddComponent<CarPicker>();
                picker.Init(game);
            }
            pausePanel.SetActive(false);
            canvas.gameObject.SetActive(false);
            picker.Open(chosen =>
            {
                if (chosen)
                {
                    SetPaused(false);
                    Announce(Loc.T("AU VOLANT", "DRIVING"), game.Player.Spec.displayName, Accent, 2.2f);
                }
                else
                {
                    pausePanel.SetActive(true);
                    openedFrame = Time.frameCount;
                    RefreshPause();
                    EventSystem.current?.SetSelectedGameObject(carTile.button.gameObject);
                }
            });
        }

        /// <summary>Heure et météo (tuile) : réglage de l'heure, du temps qui passe et du temps qu'il fait.</summary>
        void OpenTimeWeather()
        {
            if (game.Net && game.Net.IsClient) return;
            if (!timeWeather)
            {
                timeWeather = gameObject.AddComponent<TimeWeatherMenu>();
                timeWeather.Init(game);
            }
            pausePanel.SetActive(false);
            timeWeather.Open(() =>
            {
                pausePanel.SetActive(true);
                openedFrame = Time.frameCount;
                RefreshPause();
                EventSystem.current?.SetSelectedGameObject(timeTile.button.gameObject);
            });
        }

        /// <summary>Panneau des contrôles (pilule Commandes) à la place des tuiles ; false : retour aux tuiles.</summary>
        void ShowControlsPage(bool show)
        {
            controlsPage = show;
            if (game.Online && show) playersPage = false;
            pauseRoot.gameObject.SetActive(!show);
            controlsPanel.SetActive(show && !PlayersInMenu);
            openedFrame = Time.frameCount;
            if (show) { controlsShown = -1; RefreshControls(); EventSystem.current?.SetSelectedGameObject(null); }
            else EventSystem.current?.SetSelectedGameObject(controlsPill.button.gameObject);
        }

        static void Fit(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = oMin; rt.offsetMax = oMax;
        }

        /// <summary>Panneau des contrôles, à droite du menu pause.</summary>
        void BuildControls(Transform parent)
        {
            var panel = Rect(parent, "Controls", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(ControlsLeft, -MenuHeight * 0.5f), new Vector2(ControlsLeft + ControlsW, MenuHeight * 0.5f));
            controlsPlate = Glass.Backdrop(panel, 30f);
            controlsPanel = panel.gameObject;
            controlsPanelRt = panel;
            controlsTitle = Glass.Label(panel, "", 32, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(50f, -22f), new Vector2(300, 60));
            // Onglets : liste, manette, clavier (en réseau, d'abord le tableau des joueurs, qui prend la place du panneau)
            foreach (var (view, fr, en) in new[] { (ViewPlayers, "JOUEURS", "PLAYERS"), (ViewList, "LISTE", "LIST"), (ViewPad, "MANETTE", "GAMEPAD"), (ViewKeys, "CLAVIER", "KEYBOARD") })
            {
                var go = new GameObject("View", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button));
                go.transform.SetParent(panel, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                var plate = go.GetComponent<HudShape>();
                plate.Set(12f, 0f, 0f);
                plate.color = TileNormal;
                var button = go.GetComponent<Button>();
                button.targetGraphic = plate;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                int v = view;
                button.onClick.AddListener(() => ShowControlsView(v));
                var text = Label(rt, "", 17, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, FontStyle.BoldAndItalic);
                Fit(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Loc.Bind(text, fr, en);
                viewChips.Add((button, plate, text, view));
            }
            // Vue dessinée (manette ou clavier), reconstruite quand les commandes, la langue ou la disposition changent
            controlsArt = Rect(panel, "Diagram", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -96f));
            controlsList = Rect(panel, "List", Vector2.zero, Vector2.one, new Vector2(50f, 24f), new Vector2(-50f, -100f));
            var layout = controlsList.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            foreach (var _ in Controls)
            {
                // Quatorze lignes dans la hauteur du panneau
                var row = Rect(controlsList, "Row", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
                var action = Glass.Label(row, "", 22, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 40));
                action.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var key = Glass.Label(row, "", 22, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 40));
                key.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;
                controlRows.Add((action, key));
            }
        }

        /// <summary>Contrôles de l'appareil utilisé en dernier (manette ou clavier), dans la langue en cours.</summary>
        void RefreshControls()
        {
            bool touch = TouchInput.InUse && GameSettings.TouchSteer != 0;
            bool pad = !touch && GameInput.UsingGamepad;
            bool online = game.Online;
            int view = ControlsView(touch, pad);
            var kb = UnityEngine.InputSystem.Keyboard.current;
            int layoutHash = kb != null && kb.keyboardLayout != null ? kb.keyboardLayout.GetHashCode() : 0;
            int shown = unchecked((((((view * 3 + (touch ? 2 : pad ? 1 : 0)) * 2 + (Loc.French ? 1 : 0)) * 8 + GameSettings.TouchSteer) * 2
                + (online ? 1 : 0)) * 7919 + GameInput.BindingsVersion) * 31 + layoutHash);
            if (shown == controlsShown) return;
            controlsShown = shown;
            controlsTitle.text = Glass.Spaced(Loc.T("COMMANDES", "CONTROLS"), 2);
            LayoutViewChips(view, online);
            controlsList.gameObject.SetActive(view == ViewList);
            for (int i = controlsArt.childCount - 1; i >= 0; i--)
            {
                var child = controlsArt.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            if (view == ViewPad) ControlsDiagram.BuildPad(controlsArt, online);
            else if (view == ViewKeys) ControlsDiagram.BuildKeyboard(controlsArt, online);
            if (view != ViewList) return;
            for (int i = 0; i < Controls.Length; i++)
            {
                var c = Controls[i];
                controlRows[i].action.text = Loc.T(c[0], c[1]);
                string key = touch ? Loc.T(c[5], c[6]) : pad ? c[2] : Loc.T(c[3], c[4]);
                int bind = ControlBinds[i];
                if (!touch && bind >= 0)
                {
                    key = GameInput.Describe((GameInput.Bind)bind, pad);
                    if (key == "—" && bind == (int)GameInput.Bind.Map) key = Loc.T("Menu pause", "Pause menu");
                }
                else if (!touch && !pad && bind == -2)
                    key = GameInput.Describe(GameInput.Bind.Left, false) + "  ·  " + GameInput.Describe(GameInput.Bind.Right, false);
                if (key == "@LS") key = Loc.T("Joystick gauche", "Left stick");
                else if (key == "@RS") key = Loc.T("Joystick droit", "Right stick");
                else if (key == "@DU") key = Loc.T("Croix ↑", "D-pad ↑");
                else if (key == "@DD") key = Loc.T("Croix ↓", "D-pad ↓");
                else if (key == "@TS") key = GameSettings.TouchSteer == 3 ? Loc.T("Incliner l'écran", "Tilt the screen")
                    : GameSettings.TouchSteer == 2 ? Loc.T("Boutons ‹ ›", "‹ › buttons") : Loc.T("Glisser à gauche", "Slide on the left");
                controlRows[i].key.text = key;
            }
        }

        /// <summary>Vue des contrôles : celle choisie, sinon la liste (tactile) ou le dessin de l'appareil utilisé en dernier.</summary>
        static int ControlsView(bool touch, bool pad) => controlsView >= 0 ? controlsView : touch ? ViewList : pad ? ViewPad : ViewKeys;

        /// <summary>Onglets des vues, alignés à droite du titre ; l'onglet affiché passe à l'or. « Joueurs » : en réseau seulement.</summary>
        void LayoutViewChips(int view, bool online)
        {
            float x = -50f;
            for (int i = viewChips.Count - 1; i >= 0; i--)
            {
                var (button, plate, text, v) = viewChips[i];
                bool show = v != ViewPlayers || online;
                button.gameObject.SetActive(show);
                if (!show) continue;
                bool on = v == view;
                plate.color = on ? TileGold : TileNormal;
                text.color = on ? Ink : White;
                float w = Mathf.Ceil(text.preferredWidth) + 30f;
                var rt = (RectTransform)button.transform;
                rt.sizeDelta = new Vector2(w, 38f);
                rt.anchoredPosition = new Vector2(x, -33f);
                x -= w + 8f;
            }
        }

        /// <summary>Onglet choisi (clic) : une vue des contrôles, ou le tableau des joueurs en réseau.</summary>
        void ShowControlsView(int view)
        {
            if (view == ViewPlayers) { if (game.Online) { playersPage = true; controlsPanel.SetActive(false); } return; }
            controlsView = view;
            if (game.Online) playersPage = false;
            controlsShown = -1;
            RefreshControls();
        }

        /// <summary>LB / RB (Maj+Tab / Tab) : vue précédente ou suivante ; en réseau, le tableau des joueurs en fait partie.</summary>
        void CycleControlsView(int dir)
        {
            bool touch = TouchInput.InUse && GameSettings.TouchSteer != 0;
            bool online = game.Online;
            if (online) { ShowControlsView(ViewPlayers); return; }   // la page ne sert plus qu'au tableau des joueurs
            var views = new[] { ViewList, ViewPad, ViewKeys };
            int cur = online && playersPage ? ViewPlayers : ControlsView(touch, !touch && GameInput.UsingGamepad);
            int i = System.Array.IndexOf(views, cur);
            ShowControlsView(views[(i + dir + views.Length) % views.Length]);
        }

        void RefreshPause()
        {
            RefreshControls();
            // Une seule voiture achetée, ou course en cours : tuile grisée
            bool locked = game.Races && game.Races.CarLocked;
            carTile.button.interactable = game.OwnedCars > 1 && !locked;
            carTile.sub.text = locked ? Loc.T("Verrouillée pendant la course", "Locked during the race")
                : game.OwnedCars <= 1 ? Loc.T("Une seule voiture au garage", "Only one car in the garage") : Loc.T("Changer de voiture", "Change car");
            var net = game.Net;
            bool online = game.Online;
            bool client = online && net.IsClient;
            timeTile.button.interactable = !client;
            timeTile.sub.text = client ? Loc.T("Réglées par l'hôte", "Set by the host") : Loc.T("Moment de la journée, ciel", "Time of day, sky");
            bool storm = game.Weather && (game.Weather.Stormy || game.Weather.Rain > 0.05f);
            timeBadge.text = game.DayNight.Clock + "   " + (storm ? Loc.T("Orage", "Storm") : Loc.T("Beau temps", "Clear"));
            FitBadge(timeBadge);
            int got = Achievements.UnlockedCount, total = Achievements.All.Length;
            trophyBadge.text = Loc.F($"{got} / {total}");
            FitBadge(trophyBadge);
            trophyTile.sub.text = Loc.T("Défis de la route", "Road challenges");
            // En réseau, rien ne s'arrête : c'est un menu, pas une pause
            pauseTitle.text = Glass.Spaced(online ? "MENU" : "PAUSE", 3);
            pauseKicker.text = Glass.Spaced(game.Player.Spec.displayName.ToUpperInvariant(), 2);
            pauseStatus.text = game.DayNight.Clock + "    <color=#FFFFFF66>|</color>    <color=#FFC233>" + Wallet.Format(Wallet.Cash) + "</color>";
            var sr = (RectTransform)pauseStatus.transform.parent;
            sr.sizeDelta = new Vector2(Mathf.Ceil(pauseStatus.preferredWidth) + 64f, 60f);
            pauseHint.text = GameInput.UsingGamepad
                ? Loc.T("<color=#FFC233>A</color>  valider     <color=#FFC233>B</color>  reprendre", "<color=#FFC233>A</color>  select     <color=#FFC233>B</color>  resume")
                : Loc.T("<color=#FFC233>Entrée</color>  valider     <color=#FFC233>Échap</color>  reprendre", "<color=#FFC233>Enter</color>  select     <color=#FFC233>Esc</color>  resume");
            RefreshMapPreview();
            RefreshNet();
            LayoutPills();
        }

        /// <summary>
        /// Partie en réseau, dans le menu : joueurs présents, adresse et code d'invitation à donner aux autres (hôte) ou
        /// serveur rejoint et latence par le relais (joueur entré par code), option de collisions ; bouton du code
        /// d'invitation (hôte). La plaque du menu prend la hauteur de ce qu'elle montre.
        /// </summary>
        void RefreshNet()
        {
            var net = game.Net;
            bool online = game.Online, host = online && net.IsHost;
            netRefresh = 0.5f;
            netLabel.gameObject.SetActive(online);
            if (inviteTile.button.gameObject.activeSelf != host) { inviteTile.button.gameObject.SetActive(host); LayoutPills(); }
            if (controlsPill.button.gameObject.activeSelf != online) { controlsPill.button.gameObject.SetActive(online); LayoutPills(); }
            if (online)
            {
                // Option fixée par le serveur à l'ouverture de la partie : rappelée, pas modifiable ici
                string collisions = net.Collisions
                    ? Loc.T("Collisions entre joueurs : activées", "Player collisions: on")
                    : Loc.T("Collisions entre joueurs : désactivées", "Player collisions: off");
                string first, invite = "";
                if (host)
                {
                    first = Loc.T($"EN LIGNE  ·  joueurs : {net.PlayerCount}  ·  ton adresse : {net.LocalAddress}", $"ONLINE  ·  players: {net.PlayerCount}  ·  your address: {net.LocalAddress}");
                    var step = net.InviteState;
                    invite = step == NetRelay.Step.Ready ? Loc.T($"Code d'invitation : {net.InviteCode}", $"Invite code: {net.InviteCode}")
                        : step == NetRelay.Step.Pending ? Loc.T("Code d'invitation : demande en cours", "Invite code: requesting")
                        : step == NetRelay.Step.Failed ? Loc.T($"Pas de code : {net.InviteError}", $"No code: {net.InviteError}") : "";
                    bool copied = Time.unscaledTime - inviteCopied < 2.5f;
                    string pill = step == NetRelay.Step.Ready ? (copied ? Loc.T("Code copié !", "Code copied!") : Loc.T("Copier le code", "Copy the code"))
                        : step == NetRelay.Step.Pending ? Loc.T("Demande en cours…", "Requesting…")
                        : Loc.T("Demander un code", "Request a code");
                    if (inviteTile.label.text != pill) { inviteTile.label.text = pill; LayoutPills(); }
                }
                else
                {
                    string relay = !net.ViaRelay ? "" : net.RelayPing >= 0
                        ? Loc.T($"  ·  par code d'invitation, {net.RelayPing} ms", $"  ·  by invite code, {net.RelayPing} ms")
                        : Loc.T("  ·  par code d'invitation", "  ·  by invite code");
                    first = Loc.T($"EN LIGNE  ·  {net.ServerName}  ·  joueurs : {net.PlayerCount}{relay}", $"ONLINE  ·  {net.ServerName}  ·  players: {net.PlayerCount}{relay}");
                }
                netLabel.text = first + "\n" + (invite.Length > 0 ? invite + "\n" : "") + collisions;
            }
        }

        /// <summary>Bouton du code d'invitation (hôte) : le copie dans le presse-papiers ; sans code (panne, expiré), en demande un.</summary>
        void InviteAction()
        {
            var net = game.Net;
            if (!net || !net.IsHost) return;
            if (net.InviteCode != null)
            {
                GUIUtility.systemCopyBuffer = net.InviteCode;
                inviteCopied = Time.unscaledTime;
            }
            else net.OpenInvite();
            RefreshNet();
        }

        public void SetPaused(bool paused)
        {
            if (paused) MenuBackdrop.Capture(true);   // image du jeu avant que le menu ne la couvre
            pausePanel.SetActive(paused);
            canvas.gameObject.SetActive(!paused);
            inPause = paused;
            pauseBackdrop.live = game.Online;
            controlsPage = false;
            pauseRoot.gameObject.SetActive(true);
            // En réseau, le monde continue pour tous : seule notre voiture n'est plus conduite
            Time.timeScale = paused && !game.Online ? 0f : 1f;
            game.SetInputEnabled(!paused);
            openedFrame = Time.frameCount;
            if (paused)
            {
                controlsPanel.SetActive(false);
                RefreshPause();
                AnimateTiles(0f);
                EventSystem.current?.SetSelectedGameObject(firstButton.gameObject);
            }
        }

        /// <summary>Carte globale depuis la pause : la pause se ferme, la carte s'ouvre (on la referme pour reprendre la route).</summary>
        void OpenMap()
        {
            SetPaused(false);
            WorldMap.Toggle();
        }

        /// <summary>Écran des succès depuis la pause : le panneau de pause revient à la fermeture, sur la tuile Succès.</summary>
        void OpenAchievements()
        {
            if (!achievementsMenu)
            {
                achievementsMenu = gameObject.AddComponent<AchievementsMenu>();
                achievementsMenu.Init();
            }
            pausePanel.SetActive(false);
            achievementsMenu.Open(() =>
            {
                pausePanel.SetActive(true);
                openedFrame = Time.frameCount;
                RefreshPause();
                EventSystem.current?.SetSelectedGameObject(trophyTile.button.gameObject);
            });
        }

        /// <summary>Options depuis la pause : le panneau de pause revient à la fermeture.</summary>
        void OpenOptions()
        {
            pausePanel.SetActive(false);
            game.Options.Open(() =>
            {
                pausePanel.SetActive(true);
                openedFrame = Time.frameCount;
                RefreshPause();
                EventSystem.current?.SetSelectedGameObject(firstButton.gameObject);
            });
        }

        void UpdatePause()
        {
            if (OptionsMenu.IsOpen || Dealership.IsOpen || TuningGarage.IsOpen || RaceSetup.IsOpen || CarPicker.IsOpen || AchievementsMenu.IsOpen
                || TimeWeatherMenu.IsOpen || game.Demo || GameManager.DedicatedServer) return;
            // Carte globale ouverte (ou tout juste fermée par Échap) : la pause attend
            if (WorldMap.IsOpen || Time.frameCount == WorldMap.ChangedFrame || Time.frameCount == Dealership.ChangedFrame
                || Time.frameCount == TuningGarage.ChangedFrame || Time.frameCount == RaceSetup.ChangedFrame
                || Time.frameCount == CarPicker.ChangedFrame || Time.frameCount == AchievementsMenu.ChangedFrame
                || Time.frameCount == TimeWeatherMenu.ChangedFrame) return;
            if (Time.frameCount == openedFrame || game.State != GameManager.Phase.Playing) return;
            // Sous-écran des commandes : B / Échap revient aux tuiles
            if (inPause && controlsPage && GameInput.CancelPressed) { ShowControlsPage(false); return; }
            if (!Paused && GameInput.PausePressed) SetPaused(true);
            else if (Paused && (GameInput.PausePressed || GameInput.CancelPressed)) SetPaused(false);
            if (Paused && !controlsPage && EventSystem.current && EventSystem.current.currentSelectedGameObject == null && GameInput.UsingGamepad)
                EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
            // Les contrôles suivent l'appareil touché en dernier, même menu ouvert
            if (Paused) RefreshControls();
            // En réseau : Tab ou RB (LB) passe des contrôles au tableau des joueurs, qui prend leur place
            if (inPause && pausePanel.activeSelf)
            {
                AnimateTiles(Time.unscaledDeltaTime);
                if (controlsPage)
                {
                    int dir = GameInput.TabNextPressed ? 1 : GameInput.TabPrevPressed ? -1 : 0;
                    if (dir != 0) CycleControlsView(dir);
                    bool controls = !PlayersInMenu;
                    if (controlsPanel.activeSelf != controls) controlsPanel.SetActive(controls);
                }
                else RefreshMapPreview();
            }
            // En réseau, l'état de la partie change menu ouvert (joueurs, code d'invitation obtenu, latence)
            if (inPause && pausePanel.activeSelf && game.Online)
            {
                netRefresh -= Time.unscaledDeltaTime;
                if (netRefresh <= 0f) RefreshNet();
            }
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static HudShape Shape(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var s = go.GetComponent<HudShape>();
            s.color = color;
            s.raycastTarget = false;
            return s;
        }

        /// <summary>Fond d'un panneau à mise en page automatique : plaque inclinée hors de la mise en page, sur toute la surface.</summary>
        static HudShape Backdrop(RectTransform panel)
        {
            var s = Shape(panel, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.03f, 0.04f, 0.06f, 0.92f));
            s.Set(24f, 0f, 0f);
            s.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = s.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
            return s;
        }

        static Image Img(Transform parent, string name, Sprite sprite, Vector2 size, Vector2 pos, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Arc de jauge : remplissage radial d'un anneau depuis l'origine de la jauge (en bas à gauche, sens horaire).
        /// <paramref name="from"/> et <paramref name="length"/> en fraction de tour.
        /// </summary>
        static void Arc(Image img, float from, float length)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Bottom;
            img.fillClockwise = true;
            img.fillAmount = length;
            // Départ de l'arc à 7 h 30, puis décalage de « from » dans le sens horaire
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f - from * 360f);
        }

        static void Outline(Text t, Color color, float size)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(size, -size);
        }

        Text Label(Transform parent, string text, int size, TextAnchor align, Color color, Vector2 anchor, Vector2 pos, Vector2 box, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = color; t.text = text;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.55f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }
    }
}
