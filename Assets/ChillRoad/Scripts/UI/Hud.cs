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
        HudShape announcePlate, announceStripe;
        float announceTimer, announceTime, announcePunch;
        int walletSlide = -1;
        static readonly Vector2 WalletHome = new Vector2(26f, -84f);        // sous l'heure
        static readonly Vector2 WalletTouchHome = new Vector2(338f, -30f);  // à droite du bouton pause tactile

        // Heure
        Text clockText;

        // Compteur
        Text speedText, gearText, distText, lightsText, assistLamp;
        float assistLampT;
        Image rpmArc, gearDisc;

        // Bas centre : drift, invite d'entrée
        CanvasGroup driftGroup;
        Text driftText, driftChain, driftTitle;
        Outline driftOutline;
        Image driftBarImage;
        RectTransform driftBar;
        float driftAlpha;
        CanvasGroup dealerGroup;
        Text dealerText;
        HudShape dealerStripe;

        // Bas gauche
        CanvasGroup hintGroup, musicGroup;
        Text hintText, musicText;
        RectTransform musicRt;
        float hintTimer = 12f, musicTimer;
        bool hintPad, hintTouch;
        int hintBuilt = -1;   // appareil + langue + version des commandes de l'aide affichée
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
        // Partie en réseau : plaque du menu (elle grandit avec ses lignes), ligne d'état, bouton du code d'invitation
        RectTransform pauseBox;
        LayoutElement netLayout;
        // Tuiles du menu (pictogramme, nom, détail) ; la voiture ouvre le garage (CarPicker)
        class PauseTile { public Button button; public HudShape plate; public HudIcon icon; public Text label, sub; public float on; }
        readonly List<PauseTile> pauseTiles = new List<PauseTile>();
        PauseTile carTile, timeTile, inviteTile, trophyTile;
        CarPicker picker;
        AchievementsMenu achievementsMenu;
        static readonly Color TileNormal = new Color(0.13f, 0.15f, 0.19f, 0.96f);
        static readonly Color TileOff = new Color(0.09f, 0.1f, 0.12f, 0.9f);
        static readonly Color TileGold = new Color(0.95f, 0.62f, 0.12f, 1f);
        static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        static readonly Color InkDim = new Color(0.06f, 0.07f, 0.09f, 0.72f);
        static readonly Color Muted = new Color(0.46f, 0.48f, 0.52f);
        const float HeroH = 132f, MidH = 214f, SetH = 104f, ExitH = 80f, RowGap = 14f;
        // Disposition du menu pause : deux plaques de même hauteur, centrées ensemble (tuiles à gauche, contrôles ou
        // joueurs à droite), toutes penchées du même angle (décalage horizontal par pixel de hauteur) ; les tuiles
        // ont ce même angle et leurs rangées suivent la pente, d'où des bords droits et parallèles à ceux des plaques
        public const float MenuLean = 0.1f;
        const float PauseW = 720f, ControlsW = 740f, MenuGap = 36f;
        const float MenuLeft = -(PauseW + MenuGap + ControlsW) * 0.5f, ControlsLeft = MenuLeft + PauseW + MenuGap;
        /// <summary>Centre horizontal de la plaque de droite (contrôles, tableau des joueurs en réseau).</summary>
        public const float ControlsCenterX = ControlsLeft + ControlsW * 0.5f;
        /// <summary>Hauteur commune des plaques du menu pause (elle grandit en réseau avec la ligne d'état).</summary>
        public static float MenuHeight { get; private set; } = 60f + 90f + HeroH + MidH + SetH + ExitH + RowGap * 4f;
        HudShape pausePlate, controlsPlate;
        RectTransform controlsPanelRt;
        GameObject netRow;
        // Rangées du menu dans l'ordre (titre, état réseau, tuiles) : chacune décalée selon sa hauteur (voir ApplyLean)
        readonly List<HorizontalLayoutGroup> leanRows = new List<HorizontalLayoutGroup>();
        float netRefresh, inviteCopied = -10f, inviteClock;
        string inviteAnnounced;
        /// <summary>
        /// Menu pause (ou options ouvertes depuis la pause), carte : le joueur ne conduit pas. Seul, le monde est arrêté ;
        /// en réseau, il continue.
        /// </summary>
        /// <summary>Photo du radar, en haut à droite.</summary>
        public RadarPolaroid Polaroid { get; private set; }
        public bool Paused => WorldMap.IsOpen || inPause || Dealership.IsOpen || TuningGarage.IsOpen || RaceSetup.IsOpen;

        // Partie en réseau : à droite du menu, le tableau des joueurs (PlayerList) prend la place des contrôles
        GameObject controlsPanel;
        bool playersPage = true;
        /// <summary>Menu d'une partie en réseau ouvert sur le tableau des joueurs (à la place des contrôles).</summary>
        public bool PlayersInMenu => inPause && pausePanel && pausePanel.activeSelf && game.Online && playersPage;
        /// <summary>Menu d'une partie en réseau : montre à droite le tableau des joueurs, ou les contrôles.</summary>
        public void ShowPlayersInMenu(bool players) => playersPage = players;

        /// <summary>Masque le HUD (chargement, écran d'accueil).</summary>
        public void SetVisible(bool visible)
        {
            if (screen) screen.gameObject.SetActive(visible);
            if (visible) hintTimer = 12f;
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
            BuildClock();
            BuildGauge();
            BuildMiniMap();
            BuildCenter();
            BuildBottomLeft();
            BuildPause();
            Polaroid = gameObject.AddComponent<RadarPolaroid>();
            Polaroid.Init(gm, canvas);
            gameObject.AddComponent<ProvingGround>().Init(gm);
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
            if (game.Music) game.Music.TrackChanged += name => { if (!GameSettings.TrackNames) return; musicText.text = "♪  " + name; musicTimer = 5f; };
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Haut gauche, sous l'heure : argent du joueur, sur une petite plaque. Haut centre, seulement pendant un combo :
        /// badge du multiplicateur, cagnotte en jeu (perdue en cas d'accident) et fine jauge du combo, sans plaque.
        /// </summary>
        void BuildScore()
        {
            // Argent : à côté du bouton pause quand les commandes tactiles sont affichées (voir UpdateTouchLayout)
            var wallet = Rect(canvas, "Wallet", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            wallet.pivot = new Vector2(0f, 1f);
            wallet.sizeDelta = new Vector2(230f, 40f);
            wallet.anchoredPosition = WalletHome;
            Shape(wallet, "Plate", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(230f, 40f), Plate).Set(12f, 0f, 70f);
            scoreText = Label(wallet, "", 26, TextAnchor.MiddleLeft, White, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(206f, 38f), FontStyle.Bold);
            walletSlide = slides.Count;
            AddSlide(wallet, new Vector2(-300f, 0f));

            // Combo : n'apparaît que pendant un combo
            var combo = Rect(canvas, "Combo", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-220f, -96f), new Vector2(220f, -24f));
            comboGroup = combo.gameObject.AddComponent<CanvasGroup>();
            comboGroup.alpha = 0f;
            comboGroup.blocksRaycasts = false;
            // Cagnotte en jeu, centrée
            bestText = Label(combo, "", 26, TextAnchor.MiddleCenter, Accent, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(400f, 34f), FontStyle.BoldAndItalic);
            bestText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Outline(bestText, new Color(0f, 0f, 0f, 0.35f), 1.5f);

            // Jauge du combo : fine ligne qui se vide
            var comboBar = Rect(combo, "Timer", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120f, -45f), new Vector2(120f, -42f));
            comboBar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            comboFill = Rect(comboBar, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            comboFill.gameObject.AddComponent<Image>().color = Accent;
            AddSlide(combo, new Vector2(0f, 220f));

            // Derniers gains, sous le combo (indépendants de son fondu : le texte reste lisible si le combo s'arrête)
            feedRt = Rect(canvas, "Feed", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            feedRt.pivot = new Vector2(0.5f, 1f);
            feedRt.sizeDelta = new Vector2(700f, FeedStep * FeedLines);
            feedRt.anchoredPosition = new Vector2(0f, -76f);
            for (int i = 0; i < FeedLines; i++)
            {
                var rt = Rect(feedRt, "Line", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(700f, FeedStep);
                var group = rt.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                var t = Label(rt, "", 18, TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.8f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(700f, FeedStep), FontStyle.Italic);
                t.supportRichText = true;
                feed.Add(new FeedLine { rt = rt, group = group, text = t });
            }

            // Annonces : sous le combo et ses lignes de gains
            announceRt = Rect(canvas, "Announce", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            announceRt.pivot = new Vector2(0.5f, 1f);
            announceRt.sizeDelta = new Vector2(1000f, 72f);
            announceRt.anchoredPosition = new Vector2(0f, -192f);
            announceGroup = announceRt.gameObject.AddComponent<CanvasGroup>();
            announceGroup.alpha = 0f;
            announceGroup.blocksRaycasts = false;
            // Plaque sombre inclinée (taillée sur le texte à chaque annonce) et liseré de la couleur du titre
            announcePlate = Shape(announceRt, "Plate", new Vector2(0.5f, 1f), new Vector2(0f, 8f), new Vector2(400f, 80f), new Color(0.02f, 0.03f, 0.05f, 0.72f));
            announcePlate.Set(22f, 90f, 90f);
            announceStripe = Shape(announceRt, "Stripe", new Vector2(0.5f, 1f), new Vector2(0f, 8f), new Vector2(240f, 4f), Accent);
            announceStripe.Set(0f, 80f, 80f);
            announceTitle = Label(announceRt, "", 36, TextAnchor.UpperCenter, White, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1000f, 44f), FontStyle.BoldAndItalic);
            Outline(announceTitle, new Color(0f, 0f, 0f, 0.45f), 2f);
            announceDetail = Label(announceRt, "", 19, TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.85f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(1000f, 26f), FontStyle.Bold);
        }

        void BuildClock()
        {
            // Heure (haut gauche)
            var clock = Rect(canvas, "Clock", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -72f), new Vector2(226f, -28f));
            Shape(clock, "Plate", new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(200f, 44f), Plate).Set(12f, 0f, 70f);
            clockText = Label(clock, "", 26, TextAnchor.MiddleLeft, White, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(190f, 40f), FontStyle.Bold);
            AddSlide(clock, new Vector2(-300f, 0f));
        }

        /// <summary>Bas droite : compte-tours circulaire (arc de 270°), vitesse au centre, rapport en pastille.</summary>
        void BuildGauge()
        {
            var root = Rect(canvas, "Gauge", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-326f, 26f), new Vector2(-26f, 326f));
            Img(root, "Back", HudArt.SoftDisc, new Vector2(300f, 300f), Vector2.zero, new Color(0.02f, 0.03f, 0.05f, 0.62f));
            var arcBack = Img(root, "ArcBack", HudArt.Ring, new Vector2(244f, 244f), Vector2.zero, new Color(1f, 1f, 1f, 0.12f));
            Arc(arcBack, 0f, 0.75f);
            var red = Img(root, "Redline", HudArt.Ring, new Vector2(244f, 244f), Vector2.zero, new Color(1f, 0.25f, 0.2f, 0.45f));
            Arc(red, 0.9f * 0.75f, 0.1f * 0.75f);
            rpmArc = Img(root, "Rpm", HudArt.Ring, new Vector2(244f, 244f), Vector2.zero, White);
            Arc(rpmArc, 0f, 0f);
            // Graduations
            for (int i = 0; i <= 10; i++)
            {
                float deg = 225f - i * 27f;
                var tick = Rect(root, "Tick", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                tick.sizeDelta = new Vector2(3f, i % 5 == 0 ? 16f : 10f);
                tick.anchoredPosition = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * 104f;
                tick.localRotation = Quaternion.Euler(0f, 0f, deg - 90f);
                tick.gameObject.AddComponent<Image>().color = i >= 9 ? new Color(1f, 0.35f, 0.3f, 0.8f) : new Color(1f, 1f, 1f, 0.45f);
            }
            speedText = Label(root, "0", 86, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(240f, 100f), FontStyle.BoldAndItalic);
            Label(root, "km/h", 20, TextAnchor.MiddleCenter, Dim, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(120f, 26f), FontStyle.Bold);
            // Rapport : pastille dans l'ouverture de l'arc
            gearDisc = Img(root, "Gear", HudArt.Disc, new Vector2(56f, 56f), new Vector2(0f, -104f), Accent);
            gearText = Label(gearDisc.rectTransform, "1", 32, TextAnchor.MiddleCenter, new Color(0.06f, 0.06f, 0.08f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(56f, 56f), FontStyle.BoldAndItalic);
            gearText.GetComponent<Shadow>().enabled = false;
            lightsText = Label(root, "", 17, TextAnchor.MiddleRight, Dim, new Vector2(0.5f, 0.5f), new Vector2(-46f, -106f), new Vector2(120f, 24f), FontStyle.Bold);
            lightsText.rectTransform.pivot = new Vector2(1f, 0.5f);
            distText = Label(root, "", 17, TextAnchor.MiddleLeft, Dim, new Vector2(0.5f, 0.5f), new Vector2(46f, -106f), new Vector2(120f, 24f), FontStyle.Bold);
            // Témoin d'ESP / d'antipatinage : clignote quand l'assistance intervient, invisible sinon
            assistLamp = Label(root, "", 17, TextAnchor.MiddleCenter, Accent, new Vector2(0.5f, 0.5f), new Vector2(0f, 74f), new Vector2(80f, 24f), FontStyle.Bold);
            distText.rectTransform.pivot = new Vector2(0f, 0.5f);
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
            // Drift en cours : points en attente (encaissés à la sortie du drift) et jauge d'angle
            var drift = Rect(canvas, "Drift", new Vector2(0.5f, 0.27f), new Vector2(0.5f, 0.27f), new Vector2(-300f, -50f), new Vector2(300f, 50f));
            driftGroup = drift.gameObject.AddComponent<CanvasGroup>();
            driftGroup.alpha = 0f;
            Shape(drift, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 100f), new Color(0.02f, 0.03f, 0.05f, 0.45f)).Set(20f, 160f, 160f);
            driftTitle = Label(drift, "DRIFT", 20, TextAnchor.MiddleCenter, DriftColor, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(200f, 24f), FontStyle.BoldAndItalic);
            driftText = Label(drift, "", 50, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(500f, 60f), FontStyle.BoldAndItalic);
            Outline(driftText, new Color(DriftColor.r, DriftColor.g, DriftColor.b, 0.45f), 2f);
            driftOutline = driftText.GetComponent<Outline>();
            driftChain = Label(drift, "", 24, TextAnchor.MiddleLeft, DriftColor, new Vector2(0.5f, 0.5f), new Vector2(150f, -4f), new Vector2(120f, 30f), FontStyle.BoldAndItalic);
            driftChain.rectTransform.pivot = new Vector2(0f, 0.5f);
            var barBg = Rect(drift, "AngleBg", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-120f, 10f), new Vector2(120f, 14f));
            barBg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            driftBar = Rect(barBg, "Angle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            driftBarImage = driftBar.gameObject.AddComponent<Image>();
            driftBarImage.color = DriftColor;

            // Concession et garage de tuning : aux abords, rappel d'entrer en voiture par la porte
            var dealer = Rect(canvas, "DealerPrompt", new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(-330f, -32f), new Vector2(330f, 32f));
            dealerGroup = dealer.gameObject.AddComponent<CanvasGroup>();
            dealerGroup.alpha = 0f;
            dealerGroup.blocksRaycasts = dealerGroup.interactable = false;
            var plate = Shape(dealer, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(660f, 64f), new Color(0.02f, 0.03f, 0.05f, 0.75f));
            plate.Set(18f, 0f, 0f);
            dealerStripe = Shape(dealer, "Stripe", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(420f, 3f), Info);
            dealerStripe.Set(0f, 120f, 120f);
            dealerText = Label(dealer, "", 28, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 56f), FontStyle.BoldAndItalic);
            dealerText.supportRichText = true;
        }

        void BuildBottomLeft()
        {
            // Morceau en cours : entre en glissant, puis s'efface
            musicRt = Rect(canvas, "Music", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(BottomLeftX, 76f), new Vector2(BottomLeftX + 500f, 116f));
            musicHome = musicRt.anchoredPosition;
            musicGroup = musicRt.gameObject.AddComponent<CanvasGroup>();
            musicGroup.alpha = 0f;
            Shape(musicRt, "Plate", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(500f, 40f), Plate).Set(12f, 0f, 160f);
            Shape(musicRt, "Stripe", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, 40f), MusicColor).Set(12f, 0f, 0f);
            musicText = Label(musicRt, "", 22, TextAnchor.MiddleLeft, MusicColor, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(470f, 34f), FontStyle.BoldAndItalic);

            // Aide des commandes : affichée au départ puis quand on change d'appareil, puis s'efface
            // (entre la minicarte et le compte-tours : la police rétrécit si la ligne est trop longue)
            var hint = Rect(canvas, "Hint", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(BottomLeftX, 26f), new Vector2(BottomLeftX + 1200f, 60f));
            hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
            hintGroup.alpha = 0f;
            Shape(hint, "Plate", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1200f, 34f), new Color(0.02f, 0.03f, 0.05f, 0.45f)).Set(10f, 0f, 260f);
            hintText = Label(hint, "", 18, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.8f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(1150f, 30f), FontStyle.Normal);
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText.verticalOverflow = VerticalWrapMode.Truncate;
            hintText.resizeTextForBestFit = true;
            hintText.resizeTextMinSize = 12;
            hintText.resizeTextMaxSize = 18;
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
            rpmArc.color = rpm > 0.9f ? Danger : rpm > 0.72f ? Color.Lerp(White, Accent, (rpm - 0.72f) / 0.18f) : White;
            // Témoin de passage de rapport : la pastille clignote au rupteur
            bool shift = rpm > 0.93f && car.Gear > 0;
            gearDisc.color = shift && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f ? Danger : Accent;
            distText.text = Loc.F($"{score.RunDistance / 1000f:0.0} km");
            // Témoin des phares : allumé aussi le temps d'un appel de phares
            bool lightsOn = slot.Lights && (slot.Lights.On || slot.Lights.Flashing);
            lightsText.text = Loc.T("PHARES", "LIGHTS");
            lightsText.color = lightsOn ? Info : new Color(1f, 1f, 1f, 0.28f);
            bool esp = car.EspIntervention > 0.05f, tcs = car.TcsIntervention > 0.05f;
            if (esp || tcs) { assistLampT = 0.45f; assistLamp.text = esp ? "ESP" : "TCS"; }
            assistLampT -= dt;
            float lampOn = assistLampT > 0f && Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.6f ? 1f : 0f;
            assistLamp.color = new Color(Accent.r, Accent.g, Accent.b, lampOn * 0.95f);

            // Argent : le compteur monte jusqu'à la somme encaissée
            float cash = Wallet.Cash;
            if (displayedScore < 0f) displayedScore = cash;
            displayedScore = Mathf.Lerp(displayedScore, cash, 1f - Mathf.Exp(-dt * 6f));
            if (Mathf.Abs(displayedScore - cash) < 1f) displayedScore = cash;
            scoreText.text = Wallet.Format(displayedScore);
            scoreText.color = Mathf.Abs(displayedScore - cash) > 0.5f ? Money : White;
            // Combo : multiplicateur, temps restant, cagnotte en jeu
            bool combo = score.ComboActive;
            comboAlpha = Mathf.MoveTowards(comboAlpha, combo ? 1f : 0f, dt * (combo ? 6f : 2.5f));
            comboGroup.alpha = comboAlpha;
            stakePunch = Mathf.MoveTowards(stakePunch, 0f, dt * 1.2f);
            if (combo)
            {
                bool kept = score.Drifting || score.Speeding || score.Jumping;
                comboFill.localScale = new Vector3(kept ? 1f : Mathf.Clamp01(score.ComboTimer / score.comboWindow), 1f, 1f);
                bestText.text = "+" + Wallet.Format(score.AtStake) + Loc.T("  en jeu", "  at stake");
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
                clockText.text = (game.Weather && game.Weather.Storm > 0.5f ? "☂  " : game.DayNight.Night > 0.5f ? "☾  " : "☀  ") + game.DayNight.Clock;

            // Drift : ce que rapportera le drift (multiplicateur compris) ; le compteur grossit avec l'angle
            // En l'air, le même cadre suit le saut : gain à la réception, distance, temps en l'air
            bool showJump = score.Jumping && !Paused;
            bool showDrift = !showJump && score.Drifting && score.DriftPoints >= 8f && !Paused;
            if (showJump || showDrift)   // pendant le fondu de sortie, le cadre garde l'allure du dernier affiché
            {
                Color tint = showJump ? JumpColor : DriftColor;
                driftTitle.text = showJump ? Loc.T("SAUT", "JUMP") : "DRIFT";
                driftTitle.color = driftChain.color = driftBarImage.color = tint;
                driftOutline.effectColor = new Color(tint.r, tint.g, tint.b, 0.45f);
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
                    driftTitle.text = stunt.done ? Loc.T("TREMPLIN", "RAMP") : Loc.T("CASCADE", "STUNT");
                    driftTitle.color = driftChain.color = driftBarImage.color = tint;
                    driftOutline.effectColor = new Color(tint.r, tint.g, tint.b, 0.45f);
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
                Color tint = tuning ? new Color(1f, 0.42f, 0.82f) : Info;
                dealerStripe.color = tint;
                string hex = ColorUtility.ToHtmlStringRGB(tint);
                dealerText.text = tuning
                    ? Loc.T($"<color=#{hex}>GARAGE DE TUNING</color>   ·   roulez jusqu'à la porte", $"<color=#{hex}>TUNING GARAGE</color>   ·   drive up to the door")
                    : Loc.T($"<color=#{hex}>CONCESSION</color>   ·   roulez jusqu'à la porte", $"<color=#{hex}>CAR DEALER</color>   ·   drive up to the door");
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

            // Aide : au départ, puis de nouveau quelques secondes quand on change d'appareil
            bool pad = GameInput.UsingGamepad, touch = TouchInput.Showing;
            if (pad != hintPad || touch != hintTouch) { hintPad = pad; hintTouch = touch; hintTimer = Mathf.Max(hintTimer, 6f); }
            int hintKey = (touch ? 2 : pad ? 1 : 0) + (Loc.French ? 4 : 0) + GameInput.BindingsVersion * 8;
            if (hintKey != hintBuilt)
            {
                hintBuilt = hintKey;
                hintText.text = touch
                    ? Loc.T("Gauche : diriger · droite : pédales · glisser sur l'écran : regarder autour · REPLACER : retour sur la route · II : pause",
                            "Left: steer · right: pedals · drag on screen: look around · RESET: back on the road · II: pause")
                    : DrivingHint(pad);
            }
            bool visible = hintTimer > 0f && !Paused && !game.Demo && game.State == GameManager.Phase.Playing;
            if (visible) hintTimer -= dt;
            hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, visible ? 1f : 0f, dt * 2f);
        }

        /// <summary>Aide d'une ligne pendant la conduite, d'après les commandes choisies dans les options (onglet Touches).</summary>
        static string DrivingHint(bool pad)
        {
            string K(GameInput.Bind b) => GameInput.Short(b, pad);
            string steer = pad ? "" : Loc.T($"{K(GameInput.Bind.Left)} {K(GameInput.Bind.Right)} direction · ", $"{K(GameInput.Bind.Left)} {K(GameInput.Bind.Right)} steer · ");
            return Loc.T(
                $"{K(GameInput.Bind.Throttle)} gaz · {K(GameInput.Bind.Brake)} frein · {steer}{K(GameInput.Bind.Handbrake)} frein à main · {K(GameInput.Bind.Lights)} phares · " +
                $"{K(GameInput.Bind.Horn)} klaxon · {K(GameInput.Bind.Camera)} vue · {K(GameInput.Bind.LookBack)} regarder derrière · {K(GameInput.Bind.Reset)} replacer · " +
                $"{K(GameInput.Bind.Music)} musique · {(pad ? "Start" : "Échap")} pause",
                $"{K(GameInput.Bind.Throttle)} throttle · {K(GameInput.Bind.Brake)} brake · {steer}{K(GameInput.Bind.Handbrake)} handbrake · {K(GameInput.Bind.Lights)} lights · " +
                $"{K(GameInput.Bind.Horn)} horn · {K(GameInput.Bind.Camera)} camera · {K(GameInput.Bind.LookBack)} look back · {K(GameInput.Bind.Reset)} reset · " +
                $"{K(GameInput.Bind.Music)} music · {(pad ? "Start" : "Esc")} pause");
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
            s.home = touch ? GaugeTouchHome : new Vector2(-176f, 176f);
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
        void Feed(string label, float pts) => Feed(label, "+" + Wallet.Format(pts), Accent);

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
            float titleH = big ? 70f : 44f;
            announceTitle.fontSize = big ? 66 : 36;
            announceTitle.rectTransform.sizeDelta = new Vector2(1000f, titleH);
            announceTitle.text = title;
            announceTitle.color = color;
            announceDetail.fontSize = big ? 21 : 19;
            announceDetail.rectTransform.anchoredPosition = new Vector2(0f, -titleH);
            announceDetail.text = detail;
            // Plaque taillée sur le plus large des deux textes
            float w = Mathf.Max(announceTitle.preferredWidth, detail.Length > 0 ? announceDetail.preferredWidth : 0f) + (big ? 200f : 170f);
            float h = titleH + (detail.Length > 0 ? 30f : 0f) + 14f;
            announcePlate.rectTransform.sizeDelta = new Vector2(w, h);
            announceStripe.rectTransform.sizeDelta = new Vector2(w * 0.55f, 4f);
            announceStripe.rectTransform.anchoredPosition = new Vector2(0f, 8f - h);
            announceStripe.color = color;
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
        /// Menu pause en tuiles (pictogramme + nom + détail), par groupes : Reprendre (et Inviter, hôte d'une partie en
        /// réseau) ; Voiture (ouvre le garage), Carte, Succès ; Options, Heure ; Menu principal, Quitter. Contrôles à droite.
        /// </summary>
        void BuildPause()
        {
            pausePanel = Rect(screen, "Pause", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            pausePanel.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.62f);
            // Menu à gauche, contrôles à droite
            var box = Rect(pausePanel.transform, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(MenuLeft, -MenuHeight * 0.5f), new Vector2(MenuLeft + PauseW, MenuHeight * 0.5f));
            pausePlate = Backdrop(box);
            pauseBox = box;
            var layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(46, 46, 30, 30);
            layout.spacing = RowGap;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            var titleRow = TileRow(box, 90f);
            var title = Label(titleRow, "PAUSE", 64, TextAnchor.MiddleLeft, Accent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 90), FontStyle.BoldAndItalic);
            pauseTitle = title;
            // Partie en réseau : nombre de joueurs, adresse et code d'invitation à donner aux autres (hôte) ; dessous,
            // l'option de collisions
            var netRt = TileRow(box, 56f);
            netRow = netRt.gameObject;
            netLabel = Label(netRt, "", 22, TextAnchor.MiddleLeft, Info, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 56), FontStyle.Bold);
            netLayout = netRt.GetComponent<LayoutElement>();
            netRow.SetActive(false);

            var hero = TileRow(box, HeroH);
            var resume = Tile(hero, HeroH, HudIcon.Kind.Play, "Reprendre", "Resume", true, 1.45f, () => SetPaused(false));
            Loc.Bind(resume.sub, "Retour sur la route", "Back on the road");
            resume.label.fontSize = 34;
            firstButton = resume.button;
            // Hôte : copie le code d'invitation dans le presse-papiers (ou en demande un s'il n'y en a pas)
            inviteTile = Tile(hero, HeroH, HudIcon.Kind.Invite, "Inviter", "Invite", true, 1f, InviteAction);

            // Garage, monde et progression
            var mid = TileRow(box, MidH);
            carTile = Tile(mid, MidH, HudIcon.Kind.Car, "Voiture", "Car", false, 1f, OpenPicker);
            var map = Tile(mid, MidH, HudIcon.Kind.Map, "Carte", "Map", false, 1f, OpenMap);
            Loc.Bind(map.sub, "Lieux, courses, GPS", "Places, races, GPS");
            trophyTile = Tile(mid, MidH, HudIcon.Kind.Trophy, "Succès", "Achievements", false, 1f, OpenAchievements);

            // Réglages : options, heure (celle d'une partie rejointe est celle du serveur)
            var settings = TileRow(box, SetH);
            var options = Tile(settings, SetH, HudIcon.Kind.Gear, "Options", "Options", true, 1f, OpenOptions);
            Loc.Bind(options.sub, "Son, jeu, graphismes", "Sound, game, graphics");
            timeTile = Tile(settings, SetH, HudIcon.Kind.Clock, "Heure", "Time", true, 1f,
                () => { if (!(game.Net && game.Net.IsClient)) game.DayNight.Advance(3f); RefreshPause(); });

            var exits = TileRow(box, ExitH);
            var home = Tile(exits, ExitH, HudIcon.Kind.Home, "Menu principal", "Main menu", true, 1f, game.ReturnToTitle);
            Loc.Bind(home.sub, "Écran d'accueil", "Title screen");
            var quit = Tile(exits, ExitH, HudIcon.Kind.Power, "Quitter", "Quit", true, 1f, game.Quit);
            Loc.Bind(quit.sub, "Fermer le jeu", "Close the game");
            BuildControls(pausePanel.transform);
            ApplyLean(MenuHeight);
            pausePanel.SetActive(false);
        }

        /// <summary>
        /// Plaques du menu à la hauteur donnée : même hauteur et même pente pour les deux ; chaque rangée de tuiles (et
        /// chaque ligne de la liste des contrôles) glisse selon sa hauteur, le titre et les onglets des contrôles aussi.
        /// </summary>
        void ApplyLean(float height)
        {
            MenuHeight = height;
            float skew = MenuLean * height;
            foreach (var rt in new[] { pauseBox, controlsPanelRt })
            {
                rt.offsetMin = new Vector2(rt.offsetMin.x, -height * 0.5f);
                rt.offsetMax = new Vector2(rt.offsetMax.x, height * 0.5f);
            }
            pausePlate.Set(skew, 0f, 0f);
            controlsPlate.Set(skew, 0f, 0f);
            // Tuiles : du haut (marge de 30) vers le bas, les rangées affichées et leur espacement
            float y = 30f;
            foreach (var row in leanRows)
            {
                if (!row.gameObject.activeSelf) continue;
                float h = row.GetComponent<LayoutElement>().preferredHeight;
                int off = Mathf.RoundToInt(MenuLean * (height * 0.5f - (y + h * 0.5f)));
                row.padding = new RectOffset(off, -off, 0, 0);
                y += h + RowGap;
            }
            // Contrôles : titre (centre 52 px sous le haut), lignes de la liste (sous les 100 px du haut, 43 px chacune)
            controlsTitle.rectTransform.anchoredPosition = new Vector2(50f + MenuLean * (height * 0.5f - 52f), -22f);
            for (int i = 0; i < controlRows.Count; i++)
            {
                var h = controlRows[i].action.transform.parent.GetComponent<HorizontalLayoutGroup>();
                int off = Mathf.RoundToInt(MenuLean * (height * 0.5f - 100f - i * 43f - 20f));
                h.padding = new RectOffset(off, -off, 0, 0);
            }
        }

        /// <summary>Rangée de tuiles de hauteur fixe (elle n'absorbe pas la place libre de la plaque), penchée avec la plaque.</summary>
        RectTransform TileRow(Transform parent, float height)
        {
            var row = Rect(parent, "Row", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = height;
            le.flexibleHeight = 0f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = RowGap;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            leanRows.Add(h);
            return row;
        }

        /// <summary>
        /// Tuile du menu : plaque inclinée, pictogramme et nom (dessous : un détail). wide : pictogramme à gauche et
        /// textes à sa droite ; sinon pictogramme en haut, textes centrés dessous. Survol = sélection ; la tuile
        /// sélectionnée passe à l'or (animée dans <see cref="AnimateTiles"/>).
        /// </summary>
        PauseTile Tile(Transform row, float height, HudIcon.Kind kind, string fr, string en, bool wide, float flex, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Tile", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button), typeof(LayoutElement), typeof(HoverSelect));
            go.transform.SetParent(row, false);
            go.GetComponent<LayoutElement>().flexibleWidth = flex;
            var t = new PauseTile();
            t.plate = go.GetComponent<HudShape>();
            t.plate.Set(height * MenuLean, 0f, 0f);
            t.plate.color = TileNormal;
            t.button = go.GetComponent<Button>();
            t.button.targetGraphic = t.plate;
            t.button.transition = Selectable.Transition.None;
            t.button.onClick.AddListener(onClick);

            var ig = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
            ig.transform.SetParent(go.transform, false);
            var ir = (RectTransform)ig.transform;
            t.icon = ig.GetComponent<HudIcon>();
            t.icon.Shape = kind;
            t.icon.raycastTarget = false;
            t.label = Label(go.transform, "", 30, wide ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, White, Vector2.zero, Vector2.zero, Vector2.zero, FontStyle.BoldAndItalic);
            t.sub = Label(go.transform, "", 19, wide ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Dim, Vector2.zero, Vector2.zero, Vector2.zero, FontStyle.Bold);
            t.sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.sub.verticalOverflow = VerticalWrapMode.Truncate;
            t.sub.resizeTextForBestFit = true;
            t.sub.resizeTextMinSize = 13;
            t.sub.resizeTextMaxSize = 19;
            Loc.Bind(t.label, fr, en);
            if (wide)
            {
                float size = Mathf.Min(height * 0.48f, 60f), x = 30f + size * 0.5f, tx = 30f + size + 24f;
                ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
                ir.sizeDelta = new Vector2(size, size);
                ir.anchoredPosition = new Vector2(x, 0f);
                Fit(t.label.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(tx, -8f), new Vector2(-14f, 34f));
                Fit(t.sub.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(tx + 2f, -34f), new Vector2(-14f, -8f));
            }
            else
            {
                const float textTop = 76f;
                float size = Mathf.Min(height * 0.34f, (height - textTop) * 0.7f);
                ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0f);
                ir.sizeDelta = new Vector2(size, size);
                ir.anchoredPosition = new Vector2(0f, textTop + (height - textTop) * 0.5f);
                Fit(t.label.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(10f, 42f), new Vector2(-10f, 78f));
                Fit(t.sub.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(14f, 12f), new Vector2(-14f, 42f));
            }
            pauseTiles.Add(t);
            return t;
        }

        static void Fit(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = oMin; rt.offsetMax = oMax;
        }

        /// <summary>État des tuiles : la sélectionnée passe à l'or (textes à l'encre) et grossit un peu ; grisées si indisponibles.</summary>
        void AnimateTiles(float dt)
        {
            var sel = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var t in pauseTiles)
            {
                bool usable = t.button.interactable;
                t.on = dt > 0f ? Mathf.MoveTowards(t.on, usable && sel == t.button.gameObject ? 1f : 0f, dt * 9f) : 0f;
                float e = t.on * t.on * (3f - 2f * t.on);
                Color plate = usable ? Color.Lerp(TileNormal, TileGold, e) : TileOff;
                t.plate.color = plate;
                plate.a = 1f;
                t.icon.SetColors(usable ? Color.Lerp(Accent, Ink, e) : Muted, plate);
                t.label.color = usable ? Color.Lerp(White, Ink, e) : Muted;
                t.sub.color = usable ? Color.Lerp(Dim, InkDim, e) : Muted;
                t.button.transform.localScale = Vector3.one * (1f + 0.035f * e);
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

        /// <summary>Panneau des contrôles, à droite du menu pause.</summary>
        void BuildControls(Transform parent)
        {
            var panel = Rect(parent, "Controls", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(ControlsLeft, -MenuHeight * 0.5f), new Vector2(ControlsLeft + ControlsW, MenuHeight * 0.5f));
            controlsPlate = Backdrop(panel);
            controlsPanel = panel.gameObject;
            controlsPanelRt = panel;
            controlsTitle = Label(panel, "", 30, TextAnchor.MiddleLeft, Info, new Vector2(0f, 1f), new Vector2(50f, -22f), new Vector2(300, 60), FontStyle.BoldAndItalic);
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
                var action = Label(row, "", 23, TextAnchor.MiddleLeft, White, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 40), FontStyle.Normal);
                action.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var key = Label(row, "", 23, TextAnchor.MiddleRight, Accent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 40), FontStyle.Bold);
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
            controlsTitle.text = Loc.T("CONTRÔLES", "CONTROLS");
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
            float x = -50f + MenuLean * (MenuHeight * 0.5f - 33f);
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
            if (view == ViewPlayers) { if (game.Online) playersPage = true; return; }
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
            var views = online ? new[] { ViewPlayers, ViewList, ViewPad, ViewKeys } : new[] { ViewList, ViewPad, ViewKeys };
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
            carTile.sub.text = locked ? Loc.T("Verrouillée pendant la course", "Locked during the race") : game.Player.Spec.displayName;
            var net = game.Net;
            bool online = game.Online;
            bool client = online && net.IsClient;
            timeTile.button.interactable = !client;
            timeTile.sub.text = client
                ? Loc.T($"{game.DayNight.Clock}  ·  réglée par l'hôte", $"{game.DayNight.Clock}  ·  set by the host")
                : Loc.F($"{game.DayNight.Clock}  ·  +3 h");
            int got = Achievements.UnlockedCount, total = Achievements.All.Length;
            trophyTile.sub.text = Loc.F($"{got} / {total} obtenus", $"{got} / {total} unlocked");
            // En réseau, rien ne s'arrête : c'est un menu, pas une pause
            pauseTitle.text = online ? "MENU" : "PAUSE";
            RefreshNet();
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
            int lines = 0;
            netRow.SetActive(online);
            inviteTile.button.gameObject.SetActive(host);
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
                    inviteTile.sub.text = step == NetRelay.Step.Ready ? (copied ? Loc.T("Code copié !", "Code copied!") : Loc.T("Copier le code", "Copy the code"))
                        : step == NetRelay.Step.Pending ? Loc.T("Demande en cours…", "Requesting…")
                        : Loc.T("Demander un code", "Request a code");
                }
                else
                {
                    string relay = !net.ViaRelay ? "" : net.RelayPing >= 0
                        ? Loc.T($"  ·  par code d'invitation, {net.RelayPing} ms", $"  ·  by invite code, {net.RelayPing} ms")
                        : Loc.T("  ·  par code d'invitation", "  ·  by invite code");
                    first = Loc.T($"EN LIGNE  ·  {net.ServerName}  ·  joueurs : {net.PlayerCount}{relay}", $"ONLINE  ·  {net.ServerName}  ·  players: {net.PlayerCount}{relay}");
                }
                lines = invite.Length > 0 ? 3 : 2;
                netLabel.text = first + "\n" + (invite.Length > 0 ? invite + "\n" : "") + collisions;
                netLayout.preferredHeight = netLayout.minHeight = lines * 28f;
            }
            // Hauteur de la plaque : marges, titre, ligne d'état (en réseau), quatre rangées de tuiles et leurs espaces
            float height = 60f + 90f + HeroH + MidH + SetH + ExitH + RowGap * 4f + (online ? lines * 28f + RowGap : 0f);
            ApplyLean(height);
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
            pausePanel.SetActive(paused);
            canvas.gameObject.SetActive(!paused);
            inPause = paused;
            // En réseau, le monde continue pour tous : seule notre voiture n'est plus conduite
            Time.timeScale = paused && !game.Online ? 0f : 1f;
            game.SetInputEnabled(!paused);
            openedFrame = Time.frameCount;
            if (paused)
            {
                controlsPanel.SetActive(!PlayersInMenu);
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
                || game.Demo || GameManager.DedicatedServer) return;
            // Carte globale ouverte (ou tout juste fermée par Échap) : la pause attend
            if (WorldMap.IsOpen || Time.frameCount == WorldMap.ChangedFrame || Time.frameCount == Dealership.ChangedFrame
                || Time.frameCount == TuningGarage.ChangedFrame || Time.frameCount == RaceSetup.ChangedFrame
                || Time.frameCount == CarPicker.ChangedFrame || Time.frameCount == AchievementsMenu.ChangedFrame) return;
            if (Time.frameCount == openedFrame || game.State != GameManager.Phase.Playing) return;
            if (!Paused && GameInput.PausePressed) SetPaused(true);
            else if (Paused && (GameInput.PausePressed || GameInput.CancelPressed)) SetPaused(false);
            if (Paused && EventSystem.current && EventSystem.current.currentSelectedGameObject == null && GameInput.UsingGamepad)
                EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
            // Les contrôles suivent l'appareil touché en dernier, même menu ouvert
            if (Paused) RefreshControls();
            // En réseau : Tab ou RB (LB) passe des contrôles au tableau des joueurs, qui prend leur place
            if (inPause && pausePanel.activeSelf)
            {
                AnimateTiles(Time.unscaledDeltaTime);
                int dir = GameInput.TabNextPressed ? 1 : GameInput.TabPrevPressed ? -1 : 0;
                if (dir != 0) CycleControlsView(dir);
                bool controls = !PlayersInMenu;
                if (controlsPanel.activeSelf != controls) controlsPanel.SetActive(controls);
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
