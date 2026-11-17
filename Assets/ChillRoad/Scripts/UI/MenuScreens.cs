using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Écrans hors partie (construits en code), dans la DA du HUD (plaques inclinées anthracite, Bahnschrift, or et cyan) :
    /// - fond des deux écrans : une vallée alpine en 3D (<see cref="LoadingStage"/>) avec la voiture choisie, filmée en
    ///   plans de bande-annonce pendant le chargement, puis en orbite lente (voiture dans le tiers droit) sous l'accueil ;
    ///   au départ, il s'efface pendant le flash et laisse place au monde ;
    /// - chargement : logo, astuce, étape, pourcentage et barre de progression ;
    /// - accueil : logo, menu en plaques inclinées (Jouer / Multijoueur / Options / Langue / Quitter) qui entre en cascade,
    ///   voiture et cagnotte en pied de page, touches en bas à droite ;
    /// - mise en scène du départ : l'accueil s'éjecte, bandes or et cyan, flash, bandes cinéma, compte à rebours, « GO ! ».
    /// </summary>
    public class MenuScreens : MonoBehaviour
    {
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color White = new Color(0.97f, 0.97f, 0.97f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.62f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Ink = new Color(0.04f, 0.045f, 0.06f);
        static readonly Color PlateColor = new Color(0.03f, 0.04f, 0.06f, 0.86f);
        static readonly Color ButtonIdle = new Color(0.1f, 0.115f, 0.15f, 0.82f);
        static readonly Color ButtonSelected = new Color(0.95f, 0.66f, 0.14f, 1f);

        static readonly (string fr, string en)[] Tips =
        {
            ("Trois routes en lacets montent de la vallée jusqu'au col central, au cœur du massif.",
             "Three hairpin roads climb from the valley to the central pass, in the heart of the massif."),
            ("L'autoroute fait le tour du massif : quatorze kilomètres, un viaduc sur le lac et deux tunnels.",
             "The motorway loops around the massif: fourteen kilometres, a viaduct over the lake and two tunnels."),
            ("Les pistes de terre glissent : anticipe tes freinages.",
             "Dirt tracks are slippery: brake early."),
            ("Dans les épingles, freine avant de tourner : la descente ne pardonne pas.",
             "In hairpins, brake before you turn: the way down is unforgiving."),
            ("Perdu dans un pré ? Un appui sur Y (ou R) te replace sur la route la plus proche.",
             "Lost in a meadow? Press Y (or R) to get back on the nearest road."),
            ("Le joystick droit permet de regarder autour de la voiture.",
             "Use the right stick to look around the car."),
            ("La nuit, les phares et les lampadaires éclairent la route : l'heure avance au menu pause.",
             "At night, headlights and street lamps light the road: advance the time from the pause menu."),
            ("Sous la pluie, la route glisse : freine plus tôt et garde les roues droites.",
             "In the rain the road gets slippery: brake earlier and keep the wheels straight."),
            ("Un camion lambine devant toi ? R3 (ou H) donne un coup de klaxon.",
             "A truck dawdling ahead? R3 (or H) sounds the horn."),
            ("En course, maintiens Y (ou R) pour abandonner.",
             "In a race, hold Y (or R) to abandon."),
            ("Frôlements, drifts, sauts et vitesse rapportent de l'argent : il est encaissé quand le combo retombe, perdu en cas d'accident.",
             "Near misses, drifts, jumps and speed earn money: it's banked when the combo ends, lost if you crash."),
            ("La concession, à l'est du départ, vend toute la gamme : arrête-toi devant pour entrer.",
             "The car dealer, east of the start, sells the whole range: stop in front of it to enter."),
            ("Drift : un coup de frein à main en gardant les gaz, puis dose l'accélérateur.",
             "Drift: tap the handbrake while keeping the throttle on, then feather it."),
            ("À plusieurs : « Multijoueur » sur l'accueil, puis partage ton code d'invitation avec tes amis.",
             "With friends: pick “Multiplayer” on the title screen, then share your invitation code."),
            ("L3 (ou L) allume les phares : indispensable la nuit et sous l'orage.",
             "L3 (or L) toggles the headlights: a must at night and in storms."),
            ("À plusieurs, fais-toi remarquer : croix ↓ (ou F) pour un appel de phares, R3 (ou H) pour le klaxon.",
             "With friends, get noticed: D-pad ↓ (or F) flashes your headlights, R3 (or H) sounds the horn."),
        };

        /// <summary>Bouton du menu d'accueil : plaque inclinée, liseré or, libellé ; l'état « choisi » est animé.</summary>
        class MenuItem
        {
            public Button button;
            public RectTransform body;
            public HudShape plate, mark;
            public Text label, chevron;
            public CanvasGroup group;
            public float sel;
        }

        GameManager game;
        CanvasGroup loading, title;
        RectTransform titlePanel, wipeA, wipeB, barTop, barBottom;
        Image launchFlash;
        Outline countdownGlow;
        float launchT = -1f, launchLen = 3f, countdownPunch;
        RectTransform progressFill;
        Text stepText, countdownText;
        // Fond 3D (chargement et accueil)
        CanvasGroup backdrop;
        LoadingStage stage;
        RawImage stageView;
        // Écran de chargement : logo, astuce, pourcentage, reflet de la barre, roue d'attente
        RectTransform loadingLogo, progressSheen, spinner;
        CanvasGroup loadingLogoGroup;
        Text percentText, tipText;
        HudShape sheenShape;
        float loadingT, tipTimer = 6f;
        int tipIndex;
        // Accueil : entrée en cascade, pied de page, touches
        readonly List<MenuItem> items = new List<MenuItem>();
        RectTransform titleLogo, chipRow;
        CanvasGroup titleLogoGroup, footerGroup;
        Text carText, cashText;
        float titleT = -1f;
        bool chipsForPad, chipsFrench;
        Button playButton;
        NetLobby lobby;
        float countdownTimer;
        GameManager.Phase shown = (GameManager.Phase)(-1);

        public void Init(GameManager gm)
        {
            game = gm;
            var cgo = new GameObject("Menus", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvasComp = cgo.GetComponent<Canvas>();
            canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComp.sortingOrder = 10;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)cgo.transform;

            BuildBackdrop(root);
            BuildTitle(root);
            BuildLoading(root);
            BuildLaunch(root);
            countdownText = Label(root, 240, TextAnchor.MiddleCenter, White, FontStyle.BoldAndItalic, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(1200, 320));
            countdownGlow = countdownText.gameObject.AddComponent<Outline>();
            countdownGlow.effectDistance = new Vector2(6, -6);
            lobby = gameObject.AddComponent<NetLobby>();
            lobby.Init(game);
            Refresh();
        }

        // ------------------------------------------------------------------ logo

        /// <summary>
        /// Logo du jeu : surtitre cyan espacé, « CHILL » blanc et « ROAD » or en gras italique, souligné d'un trait or
        /// incliné prolongé d'un tiret cyan. Pivot à gauche (accueil) ou au centre (chargement).
        /// </summary>
        RectTransform Logo(RectTransform parent, Vector2 anchor, int size, bool centred, out CanvasGroup group)
        {
            float w = size * 4.6f, h = size * 1.75f;
            var rt = Rect(parent, "Logo", anchor, anchor, Vector2.zero, Vector2.zero);
            rt.sizeDelta = new Vector2(w, h);
            rt.pivot = new Vector2(centred ? 0.5f : 0f, 0.5f);
            group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var align = centred ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            float px = centred ? 0.5f : 0f;

            var kicker = Label(rt, Mathf.RoundToInt(size * 0.16f), align, Info, FontStyle.Bold, new Vector2(px, 1f), new Vector2(centred ? 0f : size * 0.06f, -size * 0.1f), new Vector2(w, size * 0.25f));
            kicker.rectTransform.pivot = new Vector2(px, 1f);
            Loc.Bind(kicker, Spaced("BALADE ALPINE") + "   ·   " + Spaced("MONDE OUVERT"), Spaced("ALPINE DRIVING") + "   ·   " + Spaced("OPEN WORLD"));

            var word = Label(rt, size, align, White, FontStyle.BoldAndItalic, new Vector2(px, 0.5f), new Vector2(0f, size * 0.04f), new Vector2(w, size * 1.15f));
            word.rectTransform.pivot = new Vector2(px, 0.5f);
            word.supportRichText = true;
            word.text = $"CHILL <color=#{ColorUtility.ToHtmlStringRGB(Accent)}>ROAD</color>";
            var sh = word.GetComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
            sh.effectDistance = new Vector2(size * 0.035f, -size * 0.035f);

            // Trait or + tiret cyan sous le mot
            float barW = size * 2.1f, dash = size * 0.42f, bh = Mathf.Max(5f, size * 0.05f), y = -size * 0.62f;
            float x0 = centred ? -(barW + dash + 14f) * 0.5f : size * 0.04f;
            var bar = Rect(rt, "Bar", new Vector2(px, 0.5f), new Vector2(px, 0.5f), new Vector2(x0, y - bh), new Vector2(x0 + barW, y));
            Plate(bar, Accent).Set(bh * 1.4f, 0f, 0f);
            var tick = Rect(rt, "Dash", new Vector2(px, 0.5f), new Vector2(px, 0.5f), new Vector2(x0 + barW + 14f, y - bh), new Vector2(x0 + barW + 14f + dash, y));
            Plate(tick, Info).Set(bh * 1.4f, 0f, 0f);
            return rt;
        }

        /// <summary>Lettres espacées (le Text de uGUI n'a pas d'interlettrage).</summary>
        static string Spaced(string s) => string.Join(" ", s.ToCharArray());

        // ------------------------------------------------------------------ chargement

        /// <summary>
        /// Écran de chargement (le premier que l'on voit) : décor 3D fait des assets du jeu (<see cref="LoadingStage"/> :
        /// la voiture choisie sur une nationale, filmée en plans enchaînés), logo, puis en bas l'astuce du moment sur sa
        /// plaque, l'étape en cours avec une roue d'attente, le pourcentage et une barre crantée. En fin de chargement,
        /// l'écran s'efface en fondu sur l'accueil.
        /// </summary>
        void BuildLoading(RectTransform root)
        {
            var panel = Rect(root, "Loading", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            loading = panel.gameObject.AddComponent<CanvasGroup>();
            // Voile en haut et en bas : le logo et la plaque restent lisibles sur le décor
            var veil = Rect(panel, "Veil", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            veil.texture = VerticalVeil();
            veil.raycastTarget = false;

            loadingLogo = Logo(panel, new Vector2(0.5f, 0.8f), 128, true, out loadingLogoGroup);
            var tagline = Label(loadingLogo, 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f), FontStyle.Italic, new Vector2(0.5f, 0f), new Vector2(0f, -26f), new Vector2(1200, 40));
            Loc.Bind(tagline, "Une vallée alpine, un col, des lacets : roule à ton rythme.", "An alpine valley, a mountain pass, hairpins: drive at your own pace.");

            // Bas de l'écran : astuce sur sa plaque, puis étape, pourcentage et barre
            var box = Rect(panel, "Progress", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-660f, 56f), new Vector2(660f, 236f));
            var tipPlate = Rect(box, "TipPlate", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), Vector2.zero);
            Plate(tipPlate, PlateColor).Set(22f, 180f, 180f);
            tipText = Label(tipPlate, 24, TextAnchor.MiddleCenter, White, FontStyle.Normal, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(1240, 34));
            tipText.supportRichText = true;
            tipIndex = Random.Range(0, Tips.Length);
            SetTip();

            // Roue d'attente (arc qui tourne) et étape en cours, à gauche ; pourcentage à droite
            spinner = Rect(box, "Spinner", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 50f), new Vector2(50f, 82f));
            spinner.gameObject.AddComponent<CanvasRenderer>();
            var arc = spinner.gameObject.AddComponent<Image>();
            arc.sprite = HudArt.Ring;
            arc.type = Image.Type.Filled;
            arc.fillMethod = Image.FillMethod.Radial360;
            arc.fillAmount = 0.3f;
            arc.color = Accent;
            arc.raycastTarget = false;
            stepText = Label(box, 24, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.82f), FontStyle.BoldAndItalic, new Vector2(0f, 0f), new Vector2(66f, 66f), new Vector2(900, 34));
            stepText.rectTransform.pivot = new Vector2(0f, 0.5f);
            percentText = Label(box, 44, TextAnchor.MiddleRight, Accent, FontStyle.BoldAndItalic, new Vector2(1f, 0f), new Vector2(-14f, 68f), new Vector2(260, 52));
            percentText.rectTransform.pivot = new Vector2(1f, 0.5f);

            // Barre : piste sombre crantée tous les 10 %, remplissage or, reflet qui la parcourt
            var bar = Rect(box, "Bar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 18f), new Vector2(-14f, 30f));
            Plate(bar, new Color(1f, 1f, 1f, 0.12f)).Set(10f, 0f, 0f);
            progressFill = Rect(bar, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            progressFill.pivot = new Vector2(0f, 0.5f);
            Plate(progressFill, Accent).Set(10f, 0f, 0f);
            progressFill.localScale = new Vector3(0f, 1f, 1f);
            for (int i = 1; i < 10; i++)
            {
                var notch = Rect(bar, "Notch", new Vector2(i / 10f, 0f), new Vector2(i / 10f, 1f), new Vector2(-2f, -1f), new Vector2(2f, 1f));
                Plate(notch, new Color(0.01f, 0.012f, 0.02f, 0.85f)).Set(10f, 0f, 0f);
            }
            progressSheen = Rect(bar, "Sheen", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-50f, -6f), new Vector2(50f, 6f));
            sheenShape = Plate(progressSheen, new Color(1f, 1f, 0.92f, 0f));
            sheenShape.Set(10f, 40f, 40f);
        }

        /// <summary>
        /// Fond des écrans hors partie : noir opaque (rien du monde en construction ne transparaît), et par-dessus le
        /// décor 3D (<see cref="LoadingStage"/> : la voiture choisie dans une vallée alpine, filmée par sa propre caméra).
        /// </summary>
        void BuildBackdrop(RectTransform root)
        {
            var panel = Rect(root, "Backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.012f, 0.02f, 1f);
            backdrop = panel.gameObject.AddComponent<CanvasGroup>();
            backdrop.blocksRaycasts = false;
            stage = gameObject.AddComponent<LoadingStage>();
            stage.Build(game.assets, game.Player.Spec, game.Camera.GetComponent<Camera>());
            if (game.Weather && stage.Camera) game.Weather.AddCamera(stage.Camera);
            stageView = Rect(panel, "Stage", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            stageView.texture = stage.Texture;
            stageView.raycastTarget = false;
        }

        /// <summary>Fond 3D : texture et fondus du décor ; passe en orbite de menu sur l'accueil ; libéré une fois effacé.</summary>
        void UpdateBackdrop(float dt)
        {
            if (!backdrop.gameObject.activeSelf) return;
            var state = game.State;
            if (stage)
            {
                if (stageView.texture != stage.Texture) stageView.texture = stage.Texture;
                float lit = 1f - stage.Fade;
                stageView.color = new Color(lit, lit, lit, 1f);
                if (state == GameManager.Phase.Title) stage.Menu = true;
            }
            // Au départ, le décor s'efface sous le flash (voir UpdateLaunch) ; sans mise en scène (partie rejointe
            // directement), il s'efface tout seul
            if (state != GameManager.Phase.Loading && state != GameManager.Phase.Title && launchT < 0f)
                backdrop.alpha = Mathf.MoveTowards(backdrop.alpha, 0f, dt * 3f);
            if (backdrop.alpha <= 0f)
            {
                backdrop.gameObject.SetActive(false);
                if (stage) stage.Release();
                stage = null;
            }
        }

        /// <summary>Dégradé vertical : sombre en haut et surtout en bas, transparent au milieu (sur la voiture).</summary>
        static Texture2D VerticalVeil()
        {
            var tex = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 128; y++)
            {
                float v = y / 127f;
                float a = Mathf.Max(Mathf.Pow(1f - Mathf.Clamp01(v / 0.36f), 1.5f) * 0.88f, Mathf.Pow(Mathf.Clamp01((v - 0.6f) / 0.4f), 1.7f) * 0.62f);
                tex.SetPixel(0, y, new Color(0.01f, 0.012f, 0.02f, a));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Plaque inclinée du HUD sur tout le rectangle (le composant CanvasRenderer n'est pas ajouté seul).</summary>
        static HudShape Plate(RectTransform rt, Color color)
        {
            rt.gameObject.AddComponent<CanvasRenderer>();
            var s = rt.gameObject.AddComponent<HudShape>();
            s.color = color;
            s.raycastTarget = false;
            return s;
        }

        void SetTip()
        {
            var tip = Tips[tipIndex];
            string hex = ColorUtility.ToHtmlStringRGB(Info);
            tipText.text = $"<color=#{hex}><b>{Loc.T("ASTUCE", "TIP")}</b></color>    {Loc.T(tip.fr, tip.en)}";
        }

        /// <summary>Animation de l'écran de chargement : fondus du décor, logo, roue, pourcentage, reflet, astuces.</summary>
        void UpdateLoading(float dt)
        {
            loadingT += dt;
            float progress = progressFill.localScale.x;
            int pct = Mathf.RoundToInt(progress * 100f);
            percentText.text = Loc.T($"{pct} %", $"{pct}%");
            spinner.localRotation = Quaternion.Euler(0f, 0f, -loadingT * 300f);

            // Reflet qui parcourt la partie remplie de la barre
            float fill = ((RectTransform)progressFill.parent).rect.width * progress;
            float k = Mathf.Repeat(loadingT * 0.75f, 1.35f);
            progressSheen.anchoredPosition = new Vector2(Mathf.Min(k, 1f) * fill, 0f);
            float sheenA = k < 1f && fill > 80f ? Mathf.Sin(k * Mathf.PI) * 0.75f : 0f;
            sheenShape.color = new Color(1f, 1f, 0.92f, sheenA);

            // Logo : arrivée (léger zoom et fondu), puis respiration lente
            float intro = Mathf.Clamp01((loadingT - 0.15f) / 0.9f);
            float ease = 1f - (1f - intro) * (1f - intro) * (1f - intro);
            loadingLogo.localScale = Vector3.one * (Mathf.Lerp(1.08f, 1f, ease) + Mathf.Sin(loadingT * 1.4f) * 0.006f);
            loadingLogoGroup.alpha = ease;

            // Astuces : une nouvelle toutes les 6 s, en fondu
            tipTimer -= dt;
            if (tipTimer <= 0f)
            {
                tipTimer = 6f;
                tipIndex = (tipIndex + 1) % Tips.Length;
                SetTip();
            }
            var c = tipText.color;
            c.a = Mathf.Clamp01(Mathf.Min((6f - tipTimer) / 0.35f, tipTimer / 0.35f));
            tipText.color = c;
        }

        // ------------------------------------------------------------------ accueil

        void BuildTitle(RectTransform root)
        {
            var panel = Rect(root, "Title", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            titlePanel = panel;
            title = panel.gameObject.AddComponent<CanvasGroup>();
            // Ombres : dégradé sombre à gauche (le menu) et en bas (le pied de page), la voiture reste dégagée à droite
            var shade = Rect(panel, "Shade", Vector2.zero, new Vector2(0.62f, 1f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            shade.texture = Ramp(true, 1.3f, 0.86f);
            shade.raycastTarget = false;
            var floor = Rect(panel, "Floor", Vector2.zero, new Vector2(1f, 0.24f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            floor.texture = Ramp(false, 1.6f, 0.6f);
            floor.raycastTarget = false;

            titleLogo = Logo(panel, new Vector2(0.065f, 0.765f), 150, false, out titleLogoGroup);
            var tagline = Label(titleLogo, 28, TextAnchor.MiddleLeft, Dim, FontStyle.Italic, new Vector2(0f, 0f), new Vector2(8f, -18f), new Vector2(1100, 40));
            tagline.rectTransform.pivot = new Vector2(0f, 0.5f);
            Loc.Bind(tagline, "Une vallée alpine, un col, des lacets : roule à ton rythme.", "An alpine valley, a mountain pass, hairpins: drive at your own pace.");

            // Menu : plaques inclinées en colonne, la première plus haute
            var box = Rect(panel, "Buttons", new Vector2(0.065f, 0.4f), new Vector2(0.065f, 0.4f), new Vector2(0, -190), new Vector2(540, 160));
            box.pivot = new Vector2(0f, 0.5f);
            var layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            playButton = Item(box, "JOUER", "PLAY", () => game.StartCountdown(), 78, 40).button;
            Item(box, "MULTIJOUEUR", "MULTIPLAYER", OpenLobby, 60, 32);
            Item(box, "OPTIONS", "OPTIONS", OpenOptions, 60, 32);
            // Langue : affiche celle en cours, un clic passe à l'autre
            Item(box, "LANGUE : FRANÇAIS", "LANGUAGE: ENGLISH", Loc.Toggle, 50, 24);
            Item(box, "QUITTER", "QUIT", () => game.Quit(), 60, 32);

            // Pied de page : la voiture du moment et la cagnotte
            var footer = Rect(panel, "Footer", new Vector2(0.065f, 0f), new Vector2(0.065f, 0f), new Vector2(-10f, 56f), new Vector2(630f, 134f));
            footerGroup = footer.gameObject.AddComponent<CanvasGroup>();
            footerGroup.blocksRaycasts = false;
            Plate(footer, PlateColor).Set(22f, 0f, 120f);
            var stripe = Rect(footer, "Stripe", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 10f), new Vector2(17f, -10f));
            Plate(stripe, Info).Set(7f, 0f, 0f);
            var carTitle = Label(footer, 18, TextAnchor.MiddleLeft, Info, FontStyle.Bold, new Vector2(0f, 1f), new Vector2(36f, -22f), new Vector2(300, 24));
            carTitle.rectTransform.pivot = new Vector2(0f, 0.5f);
            Loc.Bind(carTitle, Spaced("TA VOITURE"), Spaced("YOUR CAR"));
            carText = Label(footer, 32, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic, new Vector2(0f, 0f), new Vector2(36f, 28f), new Vector2(310, 40));
            carText.rectTransform.pivot = new Vector2(0f, 0.5f);
            carText.horizontalOverflow = HorizontalWrapMode.Wrap;
            carText.resizeTextForBestFit = true;
            carText.resizeTextMinSize = 20;
            carText.resizeTextMaxSize = 32;
            var cashTitle = Label(footer, 18, TextAnchor.MiddleRight, Info, FontStyle.Bold, new Vector2(1f, 1f), new Vector2(-130f, -22f), new Vector2(200, 24));
            cashTitle.rectTransform.pivot = new Vector2(1f, 0.5f);
            Loc.Bind(cashTitle, Spaced("CAGNOTTE"), Spaced("CASH"));
            cashText = Label(footer, 32, TextAnchor.MiddleRight, Accent, FontStyle.BoldAndItalic, new Vector2(1f, 0f), new Vector2(-130f, 28f), new Vector2(220, 40));
            cashText.rectTransform.pivot = new Vector2(1f, 0.5f);

            // Bas à droite : touches utiles, et version du jeu
            chipRow = Rect(panel, "Keys", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-900f, 70f), new Vector2(-60f, 110f));
            BuildChips(GameInput.UsingGamepad);
            var version = Label(panel, 18, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.35f), FontStyle.Normal, new Vector2(1f, 0f), new Vector2(-60f, 40f), new Vector2(400, 24));
            version.rectTransform.pivot = new Vector2(1f, 0.5f);
            version.text = "v" + Application.version;
        }

        /// <summary>Bouton du menu : plaque inclinée qui s'estompe à droite, liseré or à gauche, flèche quand il est choisi.</summary>
        MenuItem Item(Transform parent, string fr, string en, UnityEngine.Events.UnityAction onClick, float height, int size)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(LayoutElement), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            var item = new MenuItem { group = go.GetComponent<CanvasGroup>() };
            item.body = Rect(go.transform, "Body", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            item.plate = Plate(item.body, ButtonIdle).Set(18f, 0f, 110f);
            item.plate.raycastTarget = true;
            var mark = Rect(item.body, "Mark", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, 0f), new Vector2(9f, 0f));
            item.mark = Plate(mark, Accent).Set(18f, 0f, 0f);
            item.label = Label(item.body, size, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(460, height));
            item.label.rectTransform.pivot = new Vector2(0f, 0.5f);
            Loc.Bind(item.label, fr, en);
            item.chevron = Label(item.body, size, TextAnchor.MiddleRight, Ink, FontStyle.Bold, new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(60, height));
            item.chevron.rectTransform.pivot = new Vector2(1f, 0.5f);
            item.chevron.text = "›";
            var sh = item.chevron.GetComponent<Shadow>();
            sh.enabled = false;

            var b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = item.plate;
            b.onClick.AddListener(onClick);
            go.AddComponent<HoverSelect>();   // survol = sélection : un seul état « choisi », souris comme manette
            item.button = b;
            items.Add(item);
            return item;
        }

        /// <summary>Touches du bas de l'accueil : petites plaques claires avec la touche, puis l'action (reconstruites si la manette change).</summary>
        void BuildChips(bool pad)
        {
            chipsForPad = pad;
            chipsFrench = Loc.French;
            for (int i = chipRow.childCount - 1; i >= 0; i--) Destroy(chipRow.GetChild(i).gameObject);
            if (Application.isMobilePlatform) return;
            var keys = pad
                ? new[] { ("A", "Valider", "Select"), ("↑ ↓", "Choisir", "Navigate") }
                : new[] { (Loc.T("ENTRÉE", "ENTER"), "Valider", "Select"), ("↑ ↓", "Choisir", "Navigate") };
            float x = 0f;
            foreach (var (key, fr, en) in keys)
            {
                // De droite à gauche : l'action, puis la touche
                var action = Label(chipRow, 22, TextAnchor.MiddleRight, Dim, FontStyle.Normal, new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(200, 30));
                action.rectTransform.pivot = new Vector2(1f, 0.5f);
                action.text = Loc.T(fr, en);
                x -= action.preferredWidth + 12f;
                var chip = Rect(chipRow, "Key", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
                var keyText = Label(chip, 20, TextAnchor.MiddleCenter, Ink, FontStyle.Bold, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 30));
                keyText.text = key;
                keyText.GetComponent<Shadow>().enabled = false;
                float w = Mathf.Max(38f, keyText.preferredWidth + 22f);
                chip.pivot = new Vector2(1f, 0.5f);
                chip.sizeDelta = new Vector2(w, 34f);
                chip.anchoredPosition = new Vector2(x, 0f);
                Plate(chip, new Color(0.92f, 0.93f, 0.95f, 0.92f)).Set(8f, 0f, 0f);
                keyText.transform.SetAsLastSibling();
                x -= w + 34f;
            }
        }

        /// <summary>Animation de l'accueil : entrée en cascade, état des boutons, pied de page, touches.</summary>
        void UpdateTitle(float dt)
        {
            if (titleT >= 0f) titleT += dt;
            float T(float delay, float len) => titleT < 0f ? 1f : 1f - Mathf.Pow(1f - Mathf.Clamp01((titleT - delay) / len), 3f);

            float logo = T(0.05f, 0.7f);
            titleLogoGroup.alpha = logo;
            titleLogo.anchoredPosition = new Vector2(-(1f - logo) * 90f, 0f);
            footerGroup.alpha = T(0.55f, 0.6f);

            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float e = T(0.2f + i * 0.07f, 0.5f);
                it.group.alpha = e;
                bool on = selected == it.button.gameObject && title.interactable;
                it.sel = Mathf.MoveTowards(it.sel, on ? 1f : 0f, dt * 7f);
                float s = Mathf.SmoothStep(0f, 1f, it.sel);
                float x = s * 22f - (1f - e) * 120f;
                it.body.offsetMin = new Vector2(x, 0f);
                it.body.offsetMax = new Vector2(x, 0f);
                it.plate.color = Color.Lerp(ButtonIdle, ButtonSelected, s);
                it.label.color = Color.Lerp(White, Ink, s);
                it.mark.color = Color.Lerp(new Color(Accent.r, Accent.g, Accent.b, 0.85f), Ink, s);
                it.chevron.color = new Color(Ink.r, Ink.g, Ink.b, s);
                it.chevron.rectTransform.anchoredPosition = new Vector2(-150f + s * 10f, 0f);
            }

            // Pied de page : voiture et cagnotte du moment (changent au retour d'une partie)
            var spec = game.Player ? game.Player.Spec : null;
            string car = spec != null ? spec.displayName : "";
            if (carText.text != car) carText.text = car;
            string cash = Wallet.Format(Wallet.Cash);
            if (cashText.text != cash) cashText.text = cash;
            if (GameInput.UsingGamepad != chipsForPad || Loc.French != chipsFrench) BuildChips(GameInput.UsingGamepad);
        }

        /// <summary>Dégradé sombre : horizontal (fort à gauche) ou vertical (fort en bas).</summary>
        static Texture2D Ramp(bool horizontal, float power, float max)
        {
            int n = 256;
            var tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Pow(1f - i / (n - 1f), power) * max;
                var c = new Color(0.015f, 0.02f, 0.03f, a);
                if (horizontal) tex.SetPixel(i, 0, c); else tex.SetPixel(0, i, c);
            }
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ départ

        /// <summary>Éléments de la mise en scène du départ (cachés au repos).</summary>
        void BuildLaunch(RectTransform root)
        {
            // Bandes cinéma (haut et bas)
            barTop = Rect(root, "BarTop", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            barTop.pivot = new Vector2(0.5f, 1f);
            barTop.gameObject.AddComponent<Image>().color = Color.black;
            barBottom = Rect(root, "BarBottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            barBottom.pivot = new Vector2(0.5f, 0f);
            barBottom.gameObject.AddComponent<Image>().color = Color.black;
            // Deux bandes inclinées, or puis cyan, qui balaient l'écran
            wipeA = Wipe(root, "WipeA", Accent);
            wipeB = Wipe(root, "WipeB", Info);
            launchFlash = Rect(root, "LaunchFlash", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            launchFlash.color = new Color(1f, 1f, 1f, 0f);
            launchFlash.raycastTarget = false;
        }

        static RectTransform Wipe(RectTransform root, string name, Color color)
        {
            var rt = Rect(root, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-170, -1400), new Vector2(170, 1400));
            rt.localRotation = Quaternion.Euler(0f, 0f, -22f);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, 0.85f);
            img.raycastTarget = false;
            rt.anchoredPosition = new Vector2(-3000f, 0f);
            return rt;
        }

        /// <summary>Lance la mise en scène du départ (durée jusqu'au « GO »).</summary>
        public void PlayLaunch(float duration)
        {
            launchT = 0f;
            launchLen = duration;
            title.interactable = false;
            title.blocksRaycasts = false;
        }

        /// <summary>Bouton « Multijoueur » : salon pour héberger ou rejoindre une partie en réseau.</summary>
        void OpenLobby()
        {
            title.interactable = false;
            lobby.Open(() =>
            {
                title.interactable = game.State == GameManager.Phase.Title;
                if (EventSystem.current) EventSystem.current.SetSelectedGameObject(playButton.gameObject);
            });
        }

        void OpenOptions()
        {
            title.interactable = false;
            game.Options.Open(() =>
            {
                title.interactable = game.State == GameManager.Phase.Title;
                if (EventSystem.current) EventSystem.current.SetSelectedGameObject(playButton.gameObject);
            });
        }

        void UpdateLaunch(float dt)
        {
            if (launchT < 0f) return;
            launchT += dt;
            float t = launchT;
            // L'accueil s'éjecte vers la gauche en accélérant
            float out01 = Mathf.Clamp01(t / 0.45f);
            float shift = out01 * out01 * 1600f;
            titlePanel.offsetMin = new Vector2(-shift, 0f);
            titlePanel.offsetMax = new Vector2(-shift, 0f);
            title.alpha = 1f - out01;
            // Le fond 3D s'efface sous le flash : le monde apparaît, la caméra plonge vers la voiture
            if (backdrop.gameObject.activeSelf) backdrop.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.04f) / 0.3f));
            // Flash bref au départ, grand flash au « GO »
            float flashA = Mathf.Clamp01(1f - t / 0.22f) * 0.7f;
            float go = t - launchLen;
            if (go >= 0f) flashA = Mathf.Max(flashA, Mathf.Clamp01(1f - go / 0.35f) * 0.85f);
            launchFlash.color = new Color(1f, 1f, 1f, flashA);
            // Bandes inclinées : traversent l'écran en 0,5 s, la cyan juste derrière l'or
            wipeA.anchoredPosition = new Vector2(Mathf.Lerp(-2600f, 2600f, Mathf.Clamp01(t / 0.5f)), 0f);
            wipeB.anchoredPosition = new Vector2(Mathf.Lerp(-2600f, 2600f, Mathf.Clamp01((t - 0.09f) / 0.5f)), 0f);
            // Bandes cinéma : entrent pendant la plongée, puis se retirent et s'effacent en douceur après le « GO »
            float bars = go < 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.4f)) : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(go / 1.4f));
            float h = bars * 120f;
            barTop.sizeDelta = new Vector2(0f, h);
            barBottom.sizeDelta = new Vector2(0f, h);
            var barColor = new Color(0f, 0f, 0f, go < 0f ? 1f : Mathf.SmoothStep(0f, 1f, bars));
            barTop.GetComponent<Image>().color = barColor;
            barBottom.GetComponent<Image>().color = barColor;
            if (go > 1.6f) launchT = -1f;
        }

        // ------------------------------------------------------------------ mise à jour

        public void ShowCountdown(string text)
        {
            countdownText.text = text;
            bool go = text.StartsWith("GO");
            countdownTimer = go ? 1f : 0.55f;
            countdownPunch = 1f;
            // Chiffres alternés or / cyan, « GO ! » blanc cerclé de cyan
            Color glow = go ? Info : text == "2" ? Info : Accent;
            countdownGlow.effectColor = new Color(glow.r, glow.g, glow.b, 0.9f);
            countdownText.color = go ? White : Color.Lerp(glow, White, 0.45f);
            countdownText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, go ? 0f : Random.Range(-6f, 6f));
        }

        void Update()
        {
            if (game == null) return;
            float dt = Time.unscaledDeltaTime;
            if (game.State != shown) Refresh();

            if (loading.gameObject.activeSelf)
            {
                if (game.State == GameManager.Phase.Loading)
                {
                    stepText.text = game.LoadStep + "…";
                    float current = progressFill.localScale.x;
                    progressFill.localScale = new Vector3(Mathf.MoveTowards(current, game.LoadProgress, dt * 1.5f), 1f, 1f);
                }
                UpdateLoading(dt);
                // Le chargement s'efface en fondu sur l'accueil (le fond 3D reste)
                loading.alpha = Mathf.MoveTowards(loading.alpha, game.State == GameManager.Phase.Loading ? 1f : 0f, dt * 2.5f);
                loading.blocksRaycasts = loading.alpha > 0.5f;
                if (loading.alpha <= 0f && game.State != GameManager.Phase.Loading) loading.gameObject.SetActive(false);
            }
            bool titleShown = game.State == GameManager.Phase.Title && !OptionsMenu.IsOpen && !NetLobby.IsOpen;
            if (launchT < 0f) title.alpha = Mathf.MoveTowards(title.alpha, titleShown ? 1f : 0f, dt * 3f);
            UpdateTitle(dt);
            UpdateLaunch(dt);
            UpdateBackdrop(dt);

            // Compte à rebours : le chiffre claque (grossit d'un coup puis se pose), puis s'efface
            if (countdownTimer > 0f)
            {
                countdownTimer -= dt;
                countdownPunch = Mathf.MoveTowards(countdownPunch, 0f, dt * 5f);
                bool go = countdownText.text.StartsWith("GO");
                float scale = 1f + countdownPunch * countdownPunch * (go ? 1.4f : 1f);
                countdownText.rectTransform.localScale = Vector3.one * scale;
                var c = countdownText.color;
                c.a = Mathf.Clamp01(countdownTimer / 0.25f);
                countdownText.color = c;
            }
            else countdownText.text = "";

            // Manette : garde un bouton sélectionné sur l'écran d'accueil
            if (game.State == GameManager.Phase.Title && !NetLobby.IsOpen && EventSystem.current && EventSystem.current.currentSelectedGameObject == null)
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
        }

        void Refresh()
        {
            shown = game.State;
            bool onTitle = shown == GameManager.Phase.Title;
            title.interactable = onTitle;
            title.blocksRaycasts = onTitle;
            if (onTitle)
            {
                // Retour à l'accueil : le menu revient en place et rejoue son entrée
                titleT = 0f;
                titlePanel.offsetMin = titlePanel.offsetMax = Vector2.zero;
                if (EventSystem.current) EventSystem.current.SetSelectedGameObject(playButton.gameObject);
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

        /// <summary>Texte dans la police du HUD, avec une ombre portée discrète.</summary>
        static Text Label(Transform parent, int size, TextAnchor align, Color color, FontStyle style, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = HudArt.Font; t.fontSize = size; t.alignment = align; t.color = color;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.6f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }
    }
}
