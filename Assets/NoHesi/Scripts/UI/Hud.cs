using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// HUD de jeu (construit en code), discret et lisible : plaques inclinées aux bords fondus plutôt que des cadres,
    /// police de tableau de bord, chiffres en italique.
    /// - haut centre : argent du joueur ; pendant un combo, badge du multiplicateur, jauge du combo et cagnotte en jeu ;
    /// - haut gauche : heure ; haut droite : épreuve en cours ;
    /// - bas droite : compte-tours circulaire, vitesse, rapport, phares, distance ;
    /// - centre : gains (frôlements, drifts, vitesse, encaissement), bannières, compteur de drift, maintien du bouton
    ///   de replacement, invite de la concession ;
    /// - bas gauche : minicarte (pendant du compte-tours), à sa droite le morceau en cours et l'aide des commandes
    ///   (quelques secondes seulement) ;
    /// - menu pause avec le panneau des contrôles (manette ou clavier selon le dernier appareil utilisé).
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
            new[] { "Regarder autour", "Look around", "@RS", "Clic droit + souris", "Right-click + mouse", "Glisser sur l'écran", "Drag on screen" },
            new[] { "Changer de vue", "Change camera", "R3", "C / V", "C / V", "VUE", "CAMERA" },
            new[] { "Regarder derrière", "Look back", "LB", "B", "B", "DERRIÈRE", "LOOK BACK" },
            new[] { "Replacer · maintenir : changer de voiture", "Reset · hold: change car", "Y", "R", "R", "REPLACER", "RESET" },
            new[] { "Musique suivante", "Next song", "@DU", "M", "M", "MUSIQUE", "MUSIC" },
            new[] { "Carte", "Map", "Select", ",", ",", "—", "—" },
            new[] { "Pause", "Pause", "Start", "Échap / P", "Esc / P", "II", "II" },
        };

        GameManager game;
        PlayerSlot slot;
        Font font;
        RectTransform screen;   // canevas plein écran (pause)
        RectTransform canvas;   // bande d'écran du joueur, où sont posés les éléments du HUD
        Image flash;
        float flashAlpha;

        // Argent et combo
        Text scoreText, multText, bestText, oncomingText;
        RectTransform multBadge, comboFill;
        HudShape multShape;
        float displayedScore = -1f, multPunch, comboAlpha;
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

        // Bas centre : drift, maintien du replacement, invite d'entrée
        CanvasGroup driftGroup;
        Text driftText, driftChain, driftTitle;
        Outline driftOutline;
        Image driftBarImage;
        RectTransform driftBar;
        float driftAlpha;
        CanvasGroup holdGroup;
        Image holdRing;
        CanvasGroup dealerGroup;
        Text dealerText;
        HudShape dealerStripe;

        // Bas gauche
        CanvasGroup hintGroup, musicGroup;
        Text hintText, musicText;
        RectTransform musicRt;
        float hintTimer = 12f, musicTimer;
        bool hintPad, hintTouch;
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
        Text carLabel, timeLabel, netLabel, pauseTitle;
        bool inPause;
        Text controlsTitle;
        readonly List<(Text action, Text key)> controlRows = new List<(Text, Text)>();
        int controlsShown = -1;   // appareil + langue affichés (0-3), -1 = à construire
        Button firstButton;
        int openedFrame;
        /// <summary>
        /// Menu pause (ou options ouvertes depuis la pause), carte : le joueur ne conduit pas. Seul, le monde est arrêté ;
        /// en réseau, il continue.
        /// </summary>
        public bool Paused => WorldMap.IsOpen || inPause || Dealership.IsOpen || TuningGarage.IsOpen;

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
            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
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
            gameObject.AddComponent<CheatCodes>().Init(gm);

            // Gains : le badge du multiplicateur réagit, une ligne discrète nomme le gain ; accident : flash rouge
            slot.Score.NearMiss += (pts, gap, onc) =>
            {
                multPunch = 0.35f;
                string label = gap < 0.6f ? Loc.T("FRÔLEMENT EXTRÊME", "EXTREME NEAR MISS") : Loc.T("FRÔLEMENT", "NEAR MISS");
                Feed(label + (onc ? Loc.T(" · CONTRESENS", " · ONCOMING") : ""), pts);
            };
            slot.Score.Bonus += (pts, label) => Feed(label, pts);
            slot.Score.DriftScored += pts => { multPunch = 0.35f; Feed("DRIFT", pts); };
            slot.Score.JumpScored += (pts, air, dist) =>
            {
                multPunch = 0.35f;
                string label = air >= 2.5f ? Loc.T("MÉGA SAUT", "MEGA JUMP") : air >= 1.5f ? Loc.T("GROS SAUT", "BIG JUMP") : Loc.T("SAUT", "JUMP");
                Feed(Loc.F($"{label} · {air:0.0} s · {dist:0} m"), pts);
            };
            slot.Score.Lost += (pts, why) => Flash(new Color(1f, 0.1f, 0.05f), 0.35f);
            if (game.Music) game.Music.TrackChanged += name => { musicText.text = "♪  " + name; musicTimer = 5f; };
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
            // Badge du multiplicateur, à gauche de la cagnotte en jeu
            multBadge = Rect(combo, "Mult", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            multBadge.sizeDelta = new Vector2(74f, 32f);
            multBadge.anchoredPosition = new Vector2(-84f, -20f);
            multShape = Shape(multBadge, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(74f, 32f), Accent);
            multShape.Set(12f, 0f, 0f);
            multText = Label(multBadge, "x1.0", 21, TextAnchor.MiddleCenter, new Color(0.06f, 0.06f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(74f, 32f), FontStyle.BoldAndItalic);
            multText.GetComponent<Shadow>().enabled = false;
            bestText = Label(combo, "", 24, TextAnchor.MiddleLeft, Accent, new Vector2(0.5f, 1f), new Vector2(-38f, -20f), new Vector2(260f, 32f), FontStyle.BoldAndItalic);
            bestText.rectTransform.pivot = new Vector2(0f, 0.5f);
            Outline(bestText, new Color(0f, 0f, 0f, 0.35f), 1.5f);

            // Jauge du combo : fine ligne qui se vide ; en dessous, le contresens
            var comboBar = Rect(combo, "Timer", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120f, -45f), new Vector2(120f, -42f));
            comboBar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            comboFill = Rect(comboBar, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            comboFill.gameObject.AddComponent<Image>().color = Accent;
            oncomingText = Label(combo, "", 16, TextAnchor.UpperCenter, Danger, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(240f, 22f), FontStyle.Bold);
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

            // Maintien du bouton de replacement : anneau qui se remplit avant le changement de voiture
            var hold = Rect(canvas, "Hold", new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.15f), new Vector2(-220f, -40f), new Vector2(220f, 40f));
            holdGroup = hold.gameObject.AddComponent<CanvasGroup>();
            holdGroup.alpha = 0f;
            Img(hold, "RingBack", HudArt.Ring, new Vector2(54f, 54f), new Vector2(-150f, 0f), new Color(1f, 1f, 1f, 0.18f));
            holdRing = Img(hold, "Ring", HudArt.Ring, new Vector2(54f, 54f), new Vector2(-150f, 0f), Accent);
            holdRing.type = Image.Type.Filled;
            holdRing.fillMethod = Image.FillMethod.Radial360;
            holdRing.fillOrigin = (int)Image.Origin360.Top;
            holdRing.fillClockwise = true;
            var holdLabel = Label(hold, "", 24, TextAnchor.MiddleLeft, White, new Vector2(0.5f, 0.5f), new Vector2(-112f, 0f), new Vector2(340f, 30f), FontStyle.BoldAndItalic);
            holdLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            Loc.Bind(holdLabel, "CHANGER DE VOITURE", "CHANGE CAR");

            // Concession : invite à entrer, à l'arrêt dans la zone (bouton : clic ou doigt)
            var dealer = Rect(canvas, "DealerPrompt", new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(-330f, -32f), new Vector2(330f, 32f));
            dealerGroup = dealer.gameObject.AddComponent<CanvasGroup>();
            dealerGroup.alpha = 0f;
            dealerGroup.blocksRaycasts = dealerGroup.interactable = false;
            var plate = Shape(dealer, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(660f, 64f), new Color(0.02f, 0.03f, 0.05f, 0.75f));
            plate.Set(18f, 0f, 0f);
            plate.raycastTarget = true;
            dealerStripe = Shape(dealer, "Stripe", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(420f, 3f), Info);
            dealerStripe.Set(0f, 120f, 120f);
            var enter = dealer.gameObject.AddComponent<Button>();
            enter.targetGraphic = plate;
            enter.transition = Selectable.Transition.None;
            enter.navigation = new Navigation { mode = Navigation.Mode.None };
            enter.onClick.AddListener(() =>
            {
                // Même invite pour le garage de tuning
                if (TuningGarage.Current && TuningGarage.Current.CanEnter) TuningGarage.Current.Open();
                else if (game.Dealer) game.Dealer.Open();
            });
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
            bool lightsOn = slot.Lights && slot.Lights.On;
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
            multPunch = Mathf.MoveTowards(multPunch, 0f, dt * 1.5f);
            if (combo)
            {
                multText.text = Loc.F($"x{score.Multiplier:0.0}");
                multShape.color = score.Oncoming ? Danger : Accent;
                oncomingText.text = score.Oncoming ? Loc.T("CONTRESENS x2", "ONCOMING x2") : "";
                bool kept = score.Drifting || score.Speeding || score.Jumping;
                comboFill.localScale = new Vector3(kept ? 1f : Mathf.Clamp01(score.ComboTimer / score.comboWindow), 1f, 1f);
                bestText.text = "+" + Wallet.Format(score.AtStake) + Loc.T("  en jeu", "  at stake");
            }
            multBadge.localScale = Vector3.one * (1f + multPunch);
            // Derniers gains : chaque ligne apparaît vite, reste, puis s'efface ; les anciennes descendent d'un cran.
            // Le tout descend sous la ligne du contresens quand elle est là
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
            float feedY = combo && score.Oncoming ? -100f : -76f;
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
            feedRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(feedRt.anchoredPosition.y, feedY, 1f - Mathf.Exp(-dt * 12f)));

            // Heure (icône soleil ou lune)
            if (clockText && game.DayNight)
                clockText.text = (game.Weather && game.Weather.Storm > 0.5f ? "☂  " : game.DayNight.Night > 0.5f ? "☾  " : "☀  ") + game.DayNight.Clock;

            // Drift : ce que rapportera le drift (multiplicateur compris) ; le compteur grossit avec l'angle
            // En l'air, le même cadre suit le saut : gain à la réception, distance, temps en l'air
            bool showJump = score.Jumping && !Paused;
            bool showDrift = !showJump && score.Drifting && score.DriftPoints * score.Multiplier >= 8f && !Paused;
            if (showJump || showDrift)   // pendant le fondu de sortie, le cadre garde l'allure du dernier affiché
            {
                Color tint = showJump ? JumpColor : DriftColor;
                driftTitle.text = showJump ? Loc.T("SAUT", "JUMP") : "DRIFT";
                driftTitle.color = driftChain.color = driftBarImage.color = tint;
                driftOutline.effectColor = new Color(tint.r, tint.g, tint.b, 0.45f);
            }
            if (showJump)
            {
                driftText.text = "+" + Wallet.Format(score.JumpPoints * score.Multiplier);
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
                driftText.text = "+" + Wallet.Format(score.DriftPoints * score.Multiplier);
                driftChain.text = Loc.F($"x{score.DriftChain:0.0}");
                driftBar.localScale = new Vector3(Mathf.Clamp01((score.DriftAngle - 12f) / 48f), 1f, 1f);
                driftText.rectTransform.localScale = Vector3.one * (1f + Mathf.Clamp01((score.DriftAngle - 12f) / 48f) * 0.18f);
            }
            bool showPanel = showDrift || showJump;
            driftAlpha = Mathf.MoveTowards(driftAlpha, showPanel ? 1f : 0f, dt * (showPanel ? 8f : 3f));
            driftGroup.alpha = driftAlpha;

            // Maintien du bouton de replacement
            float hold = slot.ResetHold;
            holdRing.fillAmount = hold;
            holdGroup.alpha = Mathf.MoveTowards(holdGroup.alpha, hold > 0.01f ? 1f : 0f, dt * 8f);

            // Concession ou garage de tuning : invite à entrer
            bool tuning = TuningGarage.Current && TuningGarage.Current.CanEnter && !Paused;
            bool dealer = tuning || (game.Dealer && game.Dealer.CanEnter && !Paused);
            if (dealer)
            {
                Color tint = tuning ? new Color(1f, 0.42f, 0.82f) : Info;
                dealerStripe.color = tint;
                string hex = ColorUtility.ToHtmlStringRGB(tint);
                string key = TouchInput.Showing ? Loc.T("touche ici", "tap here") : GameInput.UsingGamepad ? "A" : Loc.T("E ou Entrée", "E or Enter");
                dealerText.text = tuning
                    ? Loc.T($"<color=#{hex}>GARAGE DE TUNING</color>   ·   {key} : entrer", $"<color=#{hex}>TUNING GARAGE</color>   ·   {key}: enter")
                    : Loc.T($"<color=#{hex}>CONCESSION</color>   ·   {key} : entrer", $"<color=#{hex}>CAR DEALER</color>   ·   {key}: enter");
            }
            dealerGroup.alpha = Mathf.MoveTowards(dealerGroup.alpha, dealer ? 1f : 0f, dt * 5f);
            dealerGroup.blocksRaycasts = dealerGroup.interactable = dealer;

            if (flashAlpha > 0f)
            {
                flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, dt * 2.2f);
                var c = flash.color; c.a = flashAlpha; flash.color = c;
            }
            UpdateTouchLayout();
            UpdateSlides(dt);
            UpdateBottomLeft(dt);
            UpdatePause();
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
            if (touch)
                hintText.text = Loc.T("Gauche : diriger · droite : pédales · glisser sur l'écran : regarder autour · maintenir REPLACER : changer de voiture · II : pause",
                                      "Left: steer · right: pedals · drag on screen: look around · hold RESET: change car · II: pause");
            else hintText.text = pad
                ? Loc.T("RT gaz · LT frein · B frein à main · L3 phares · R3 vue · LB regarder derrière · Y replacer (maintenir : voiture) · ↑ musique · Start pause",
                        "RT throttle · LT brake · B handbrake · L3 lights · R3 camera · LB look back · Y reset (hold: car) · ↑ music · Start pause")
                : Loc.T("Z/W gaz · S frein · Q/A D direction · Espace frein à main · L phares · C vue · B regarder derrière · R replacer (maintenir : voiture) · M musique · Échap pause",
                        "W/Z throttle · S brake · A/Q D steer · Space handbrake · L lights · C camera · B look back · R reset (hold: car) · M music · Esc pause");
            bool visible = hintTimer > 0f && !Paused && !game.Demo && game.State == GameManager.Phase.Playing;
            if (visible) hintTimer -= dt;
            hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, visible ? 1f : 0f, dt * 2f);
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

        void BuildPause()
        {
            pausePanel = Rect(screen, "Pause", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            pausePanel.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.62f);
            // Menu à gauche, contrôles à droite
            var box = Rect(pausePanel.transform, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-760, -340), new Vector2(-40, 340));
            Backdrop(box);
            var layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(50, 50, 30, 30);
            layout.spacing = 12;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            var title = Label(box, "PAUSE", 64, TextAnchor.MiddleLeft, Accent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 90), FontStyle.BoldAndItalic);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 90;
            pauseTitle = title;
            // Partie en réseau : nombre de joueurs, adresse à donner aux autres (hôte)
            netLabel = Label(box, "", 22, TextAnchor.MiddleLeft, Info, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 30), FontStyle.Bold);
            netLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            netLabel.gameObject.SetActive(false);

            var resume = Button(box, "", () => SetPaused(false));
            Loc.Bind(resume.text, "Reprendre", "Resume");
            firstButton = resume.button;
            carLabel = Button(box, "", () => { game.CycleCar(1); RefreshPause(); }).text;
            // L'heure d'une partie rejointe est celle du serveur
            timeLabel = Button(box, "", () => { if (!(game.Net && game.Net.IsClient)) game.DayNight.Advance(3f); RefreshPause(); }).text;
            Button(box, "Options", OpenOptions);
            Loc.Bind(Button(box, "", game.ReturnToTitle).text, "Menu principal", "Main menu");
            Loc.Bind(Button(box, "", game.Quit).text, "Quitter le jeu", "Quit game");
            BuildControls(pausePanel.transform);
            pausePanel.SetActive(false);
        }

        /// <summary>Panneau des contrôles, à droite du menu pause.</summary>
        void BuildControls(Transform parent)
        {
            var panel = Rect(parent, "Controls", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40, -340), new Vector2(780, 340));
            Backdrop(panel);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(50, 50, 30, 30);
            layout.spacing = 4;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            controlsTitle = Label(panel, "", 30, TextAnchor.MiddleLeft, Info, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 60), FontStyle.BoldAndItalic);
            controlsTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
            foreach (var _ in Controls)
            {
                var row = Rect(panel, "Row", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
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
            int shown = (touch ? 2 : pad ? 1 : 0) + (Loc.French ? 3 : 0) + GameSettings.TouchSteer * 6;
            if (shown == controlsShown) return;
            controlsShown = shown;
            controlsTitle.text = touch ? Loc.T("CONTRÔLES  ·  TACTILE", "CONTROLS  ·  TOUCH")
                : pad ? Loc.T("CONTRÔLES  ·  MANETTE", "CONTROLS  ·  GAMEPAD") : Loc.T("CONTRÔLES  ·  CLAVIER", "CONTROLS  ·  KEYBOARD");
            for (int i = 0; i < Controls.Length; i++)
            {
                var c = Controls[i];
                controlRows[i].action.text = Loc.T(c[0], c[1]);
                string key = touch ? Loc.T(c[5], c[6]) : pad ? c[2] : Loc.T(c[3], c[4]);
                if (key == "@LS") key = Loc.T("Joystick gauche", "Left stick");
                else if (key == "@RS") key = Loc.T("Joystick droit", "Right stick");
                else if (key == "@DU") key = Loc.T("Croix ↑", "D-pad ↑");
                else if (key == "@TS") key = GameSettings.TouchSteer == 3 ? Loc.T("Incliner l'écran", "Tilt the screen")
                    : GameSettings.TouchSteer == 2 ? Loc.T("Boutons ‹ ›", "‹ › buttons") : Loc.T("Glisser à gauche", "Slide on the left");
                controlRows[i].key.text = key;
            }
        }

        void RefreshPause()
        {
            RefreshControls();
            // Une seule voiture achetée : pas de bouton de choix
            carLabel.transform.parent.gameObject.SetActive(game.OwnedCars > 1);
            carLabel.text = Loc.T($"Voiture : {game.Player.Spec.displayName}  >", $"Car: {game.Player.Spec.displayName}  >");
            var net = game.Net;
            bool online = game.Online;
            timeLabel.text = online && net.IsClient
                ? Loc.T($"Heure : {game.DayNight.Clock}  (réglée par l'hôte)", $"Time: {game.DayNight.Clock}  (set by the host)")
                : Loc.T($"Heure : {game.DayNight.Clock}  (+3 h)  >", $"Time: {game.DayNight.Clock}  (+3 h)  >");
            // En réseau, rien ne s'arrête : c'est un menu, pas une pause
            pauseTitle.text = online ? "MENU" : "PAUSE";
            netLabel.gameObject.SetActive(online);
            if (online)
                netLabel.text = net.IsHost
                    ? Loc.T($"EN LIGNE  ·  joueurs : {net.PlayerCount}  ·  ton adresse : {net.LocalAddress}", $"ONLINE  ·  players: {net.PlayerCount}  ·  your address: {net.LocalAddress}")
                    : Loc.T($"EN LIGNE  ·  {net.ServerName}  ·  joueurs : {net.PlayerCount}", $"ONLINE  ·  {net.ServerName}  ·  players: {net.PlayerCount}");
        }

        public void SetPaused(bool paused)
        {
            pausePanel.SetActive(paused);
            inPause = paused;
            // En réseau, le monde continue pour tous : seule notre voiture n'est plus conduite
            Time.timeScale = paused && !game.Online ? 0f : 1f;
            game.SetInputEnabled(!paused);
            openedFrame = Time.frameCount;
            if (paused)
            {
                RefreshPause();
                EventSystem.current?.SetSelectedGameObject(firstButton.gameObject);
            }
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
            if (OptionsMenu.IsOpen || Dealership.IsOpen || TuningGarage.IsOpen || game.Demo || GameManager.DedicatedServer) return;
            // Carte globale ouverte (ou tout juste fermée par Échap) : la pause attend
            if (WorldMap.IsOpen || Time.frameCount == WorldMap.ChangedFrame || Time.frameCount == Dealership.ChangedFrame
                || Time.frameCount == TuningGarage.ChangedFrame) return;
            if (Time.frameCount == openedFrame || game.State != GameManager.Phase.Playing) return;
            if (!Paused && GameInput.PausePressed) SetPaused(true);
            else if (Paused && (GameInput.PausePressed || GameInput.CancelPressed)) SetPaused(false);
            if (Paused && EventSystem.current && EventSystem.current.currentSelectedGameObject == null && GameInput.UsingGamepad)
                EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
            // Les contrôles suivent l'appareil touché en dernier, même menu ouvert
            if (Paused) RefreshControls();
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
        static void Backdrop(RectTransform panel)
        {
            var s = Shape(panel, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.03f, 0.04f, 0.06f, 0.92f));
            s.Set(24f, 0f, 0f);
            s.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = s.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
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

        (Button button, Text text) Button(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 62;
            var shape = go.GetComponent<HudShape>();
            shape.Set(18f, 0f, 0f);
            shape.color = Color.white;
            var b = go.GetComponent<Button>();
            b.targetGraphic = shape;
            var colors = b.colors;
            colors.normalColor = new Color(0.14f, 0.16f, 0.2f, 0.95f);
            colors.highlightedColor = new Color(0.24f, 0.27f, 0.33f, 1f);
            colors.selectedColor = new Color(0.95f, 0.62f, 0.12f, 1f);
            colors.pressedColor = new Color(1f, 0.8f, 0.4f, 1f);
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = Label(go.transform, label, 28, TextAnchor.MiddleLeft, White, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(600, 56), FontStyle.BoldAndItalic);
            return (b, t);
        }
    }
}
