using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Écrans hors partie (construits en code), dans le style verre de l'interface (<see cref="Glass"/> : panneaux
    /// sombres translucides arrondis, texte fin, or pour la sélection et l'argent, cyan pour les petits titres) :
    /// - fond des deux écrans : une vallée alpine en 3D (<see cref="LoadingStage"/>) avec la voiture choisie, filmée en
    ///   plans de bande-annonce pendant le chargement, puis en orbite lente (voiture dans le tiers droit) sous l'accueil ;
    ///   au départ, il s'efface pendant le flash et laisse place au monde ;
    /// - chargement : pilule « CHILL ROAD » en haut à gauche, bandeau de verre en bas (astuce, étape, pourcentage) et
    ///   fin trait de progression doré le long de son bord inférieur ;
    /// - accueil : carte du logo en haut à gauche, menu en pilules (Jouer / Multijoueur / Options / Langue / Quitter) qui
    ///   entre en cascade en bas à gauche, voiture et cagnotte sur une pilule en bas à droite, touches et version dessous ;
    /// - mise en scène du départ : l'accueil s'éjecte, bandes or et cyan, flash, bandes cinéma, compte à rebours, « GO ! ».
    /// </summary>
    public class MenuScreens : MonoBehaviour
    {
        static readonly Color Accent = Glass.Gold;
        static readonly Color White = Glass.White;
        static readonly Color Info = Glass.Cyan;

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

        /// <summary>Bouton du menu d'accueil : pilule de verre, libellé, valeur facultative (langue) ; l'état « choisi » est animé.</summary>
        class MenuItem
        {
            public Button button;
            public RectTransform body;
            public GlassRect plate;
            public Text label, value, chevron;
            public CanvasGroup group;
            public float sel, drawn = -1f;
            public bool strong;
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
        // Écran de chargement : pilule du logo, astuce, étape, pourcentage, roue d'attente
        RectTransform loadingLogo, spinner;
        CanvasGroup loadingLogoGroup;
        Text percentText, tipText, loadKicker;
        float loadingT, tipTimer = 6f;
        int tipIndex;
        // Accueil : entrée en cascade, pied de page, touches
        readonly List<MenuItem> items = new List<MenuItem>();
        RectTransform titleLogo;
        Vector2 titleLogoPos;
        CanvasGroup titleLogoGroup, footerGroup;
        Text carText, cashText, keysText;
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
            countdownText = Glass.Label(root, "", 240, Glass.Weight.Semilight, White, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(1200, 320));
            countdownGlow = countdownText.gameObject.AddComponent<Outline>();
            countdownGlow.effectDistance = new Vector2(6, -6);
            lobby = gameObject.AddComponent<NetLobby>();
            lobby.Init(game);
            Refresh();
        }

        // ------------------------------------------------------------------ logo

        /// <summary>« CHILL » fin blanc et « ROAD » or plus appuyé, côte à côte (deux textes : deux graisses).</summary>
        static RectTransform Wordmark(Transform parent, int size, float rowHeight)
        {
            var row = Rect(parent, "Word", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = size * 0.28f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
            Glass.Label(row, "CHILL", size, Glass.Weight.Light, Glass.White);
            Glass.Label(row, "ROAD", size, Glass.Weight.Regular, Glass.Gold);
            return row;
        }

        /// <summary>Panneau de verre qui prend la taille de son contenu (mise en page verticale ou horizontale).</summary>
        static GlassRect FitPanel(Transform parent, string name, float radius, bool vertical, RectOffset padding, float spacing, Vector2 anchor, Vector2 pos)
        {
            var g = Glass.Make(parent, name, radius, Glass.Panel, 1.5f);
            var rt = g.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            HorizontalOrVerticalLayoutGroup l = vertical
                ? g.gameObject.AddComponent<VerticalLayoutGroup>()
                : (HorizontalOrVerticalLayoutGroup)g.gameObject.AddComponent<HorizontalLayoutGroup>();
            l.padding = padding;
            l.spacing = spacing;
            l.childAlignment = vertical ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            l.childControlWidth = l.childControlHeight = true;
            l.childForceExpandWidth = l.childForceExpandHeight = false;
            var fit = g.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return g;
        }

        // ------------------------------------------------------------------ chargement

        /// <summary>
        /// Écran de chargement (le premier que l'on voit) : décor 3D fait des assets du jeu (<see cref="LoadingStage"/> :
        /// la voiture choisie sur une nationale, filmée en plans enchaînés), petite pilule « CHILL ROAD » en haut à gauche,
        /// puis en bas un bandeau de verre pleine largeur (coins hauts arrondis) : l'astuce du moment à gauche, l'étape en
        /// cours avec sa roue d'attente et le grand pourcentage à droite, un fin trait doré de progression le long du bord
        /// inférieur. En fin de chargement, l'écran s'efface en fondu sur l'accueil.
        /// </summary>
        void BuildLoading(RectTransform root)
        {
            var panel = Rect(root, "Loading", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            loading = panel.gameObject.AddComponent<CanvasGroup>();
            // Voile léger en haut et en bas : la pilule et le bandeau restent lisibles sur le décor
            var veil = Rect(panel, "Veil", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            veil.texture = VerticalVeil();
            veil.raycastTarget = false;

            // Pilule du logo
            var pill = FitPanel(panel, "Logo", 45f, false, new RectOffset(36, 36, 9, 9), 14f, new Vector2(0f, 1f), new Vector2(60f, -51f));
            Glass.Label(pill.transform, "CHILL", 42, Glass.Weight.Light, Glass.White);
            Glass.Label(pill.transform, "ROAD", 42, Glass.Weight.Regular, Glass.Gold);
            loadingLogo = pill.rectTransform;
            loadingLogoGroup = pill.gameObject.AddComponent<CanvasGroup>();
            loadingLogoGroup.blocksRaycasts = false;

            // Bandeau du bas : il déborde sous et sur les côtés de l'écran, seuls ses coins hauts arrondis se voient
            var band = Glass.Make(panel, "Band", 45f, Glass.Panel, 1.5f).rectTransform;
            band.anchorMin = Vector2.zero; band.anchorMax = new Vector2(1f, 0f);
            band.offsetMin = new Vector2(-4f, -60f); band.offsetMax = new Vector2(4f, 210f);
            var content = Rect(band, "Content", Vector2.zero, Vector2.one, new Vector2(94f, 96f), new Vector2(-94f, -36f));

            // Astuce : petit titre cyan, texte fin sur deux lignes au plus
            var tipKicker = Glass.Kicker(content, "", 16);
            Pose(tipKicker, new Vector2(0f, 1f), Vector2.zero, new Vector2(400f, 24f));
            Loc.Bind(tipKicker, Glass.Spaced("ASTUCE", 2), Glass.Spaced("TIP", 2));
            tipText = Glass.Label(content, "", 34, Glass.Weight.Light, Glass.White, TextAnchor.UpperLeft);
            var tr = tipText.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = Vector2.one; tr.pivot = new Vector2(0f, 1f);
            tr.offsetMin = new Vector2(0f, -132f); tr.offsetMax = new Vector2(-560f, -34f);
            tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tipText.lineSpacing = 1.05f;
            tipIndex = Random.Range(0, Tips.Length);
            SetTip();

            // À droite : « CHARGEMENT » précédé de l'étape en cours et de la roue d'attente, puis le pourcentage
            loadKicker = Glass.Kicker(content, "", 16);
            loadKicker.alignment = TextAnchor.MiddleRight;
            Pose(loadKicker, Vector2.one, Vector2.zero, new Vector2(400f, 24f));
            Loc.Bind(loadKicker, Glass.Spaced("CHARGEMENT", 2), Glass.Spaced("LOADING", 2));
            stepText = Glass.Label(content, "", 20, Glass.Weight.Light, Glass.Dim, TextAnchor.MiddleRight, Vector2.one, new Vector2(-220f, 0f), new Vector2(700f, 24f));
            spinner = Glass.Place(content, "Spinner", Vector2.one, new Vector2(-460f, -2f), new Vector2(20f, 20f));
            var arc = spinner.gameObject.AddComponent<Image>();
            arc.sprite = Glass.Ring(0.2f);
            arc.type = Image.Type.Filled;
            arc.fillMethod = Image.FillMethod.Radial360;
            arc.fillAmount = 0.3f;
            arc.color = Accent;
            arc.raycastTarget = false;
            spinner.pivot = new Vector2(0.5f, 0.5f);
            percentText = Glass.Label(content, "", 90, Glass.Weight.Light, Accent, TextAnchor.MiddleRight, Vector2.one, new Vector2(0f, -26f), new Vector2(520f, 100f));

            // Trait de progression : piste claire et remplissage or, au ras du bas de l'écran
            var track = Rect(band, "Track", Vector2.zero, new Vector2(1f, 0f), new Vector2(4f, 60f), new Vector2(-4f, 64.5f));
            track.gameObject.AddComponent<Image>().color = Glass.A(Color.white, 0.18f);
            track.GetComponent<Image>().raycastTarget = false;
            progressFill = Rect(track, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            progressFill.pivot = new Vector2(0f, 0.5f);
            var fill = progressFill.gameObject.AddComponent<Image>();
            fill.color = Accent;
            fill.raycastTarget = false;
            progressFill.localScale = new Vector3(0f, 1f, 1f);
        }

        /// <summary>Texte posé : ancre = pivot, position et boîte.</summary>
        static void Pose(Text t, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
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

        /// <summary>Dégradé vertical : assombrit le bas (sous le bandeau) et un peu le haut, transparent au milieu (sur la voiture).</summary>
        static Texture2D VerticalVeil()
        {
            var tex = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 128; y++)
            {
                float v = y / 127f;
                float a = Mathf.Max(Mathf.Pow(1f - Mathf.Clamp01(v / 0.34f), 1.5f) * 0.6f, Mathf.Pow(Mathf.Clamp01((v - 0.72f) / 0.28f), 1.7f) * 0.4f);
                tex.SetPixel(0, y, new Color(0.01f, 0.012f, 0.02f, a));
            }
            tex.Apply();
            return tex;
        }

        void SetTip()
        {
            var tip = Tips[tipIndex];
            tipText.text = Loc.T(tip.fr, tip.en);
        }

        /// <summary>Animation de l'écran de chargement : logo, roue, étape, pourcentage, astuces.</summary>
        void UpdateLoading(float dt)
        {
            loadingT += dt;
            float progress = progressFill.localScale.x;
            int pct = Mathf.RoundToInt(progress * 100f);
            percentText.text = Loc.T($"{pct} %", $"{pct}%");
            spinner.localRotation = Quaternion.Euler(0f, 0f, -loadingT * 300f);
            // L'étape et sa roue se rangent à gauche de « CHARGEMENT » (largeurs variables selon la langue et l'étape)
            float x = -(loadKicker.preferredWidth + 22f);
            stepText.rectTransform.anchoredPosition = new Vector2(x, 0f);
            spinner.anchoredPosition = new Vector2(x - stepText.preferredWidth - 22f, -12f);
            spinner.gameObject.SetActive(stepText.text.Length > 0);

            // Logo : arrivée (léger glissement et fondu)
            float intro = Mathf.Clamp01((loadingT - 0.15f) / 0.9f);
            float ease = 1f - (1f - intro) * (1f - intro) * (1f - intro);
            loadingLogo.anchoredPosition = new Vector2(60f - (1f - ease) * 40f, -51f);
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
            // Ombres légères : à gauche (le menu) et en bas (le pied de page), la voiture reste dégagée à droite ; les
            // panneaux de verre font le reste pour la lisibilité
            var shade = Rect(panel, "Shade", Vector2.zero, new Vector2(0.55f, 1f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            shade.texture = Ramp(true, 1.4f, 0.5f);
            shade.raycastTarget = false;
            var floor = Rect(panel, "Floor", Vector2.zero, new Vector2(1f, 0.24f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            floor.texture = Ramp(false, 1.6f, 0.42f);
            floor.raycastTarget = false;

            // Carte du logo en haut à gauche : surtitre cyan, « CHILL ROAD », trait or + tiret cyan, accroche
            titleLogoPos = new Vector2(90f, -66f);
            var card = FitPanel(panel, "Logo", 45f, true, new RectOffset(48, 48, 33, 36), 9f, new Vector2(0f, 1f), titleLogoPos);
            titleLogo = card.rectTransform;
            titleLogoGroup = card.gameObject.AddComponent<CanvasGroup>();
            titleLogoGroup.blocksRaycasts = false;
            var kicker = Glass.Kicker(card.transform, "", 18);
            Loc.Bind(kicker, Glass.Spaced("BALADE ALPINE", 2) + "   ·   " + Glass.Spaced("MONDE OUVERT", 2), Glass.Spaced("ALPINE DRIVING", 2) + "   ·   " + Glass.Spaced("OPEN WORLD", 2));
            Wordmark(card.transform, 123, 128f);
            var rule = Rect(card.transform, "Rule", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var rle = rule.gameObject.AddComponent<LayoutElement>();
            rle.preferredWidth = 330f; rle.preferredHeight = 4.5f;
            var goldRule = Glass.Make(rule, "Gold", 3f, Accent, 0f).rectTransform;
            goldRule.anchorMin = Vector2.zero; goldRule.anchorMax = new Vector2(0f, 1f);
            goldRule.offsetMin = Vector2.zero; goldRule.offsetMax = new Vector2(255f, 0f);
            var cyanRule = Glass.Make(rule, "Cyan", 3f, Info, 0f).rectTransform;
            cyanRule.anchorMin = Vector2.zero; cyanRule.anchorMax = new Vector2(0f, 1f);
            cyanRule.offsetMin = new Vector2(264f, 0f); cyanRule.offsetMax = new Vector2(330f, 0f);
            Rect(card.transform, "Gap", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 3f;
            var tagline = Glass.Label(card.transform, "", 26, Glass.Weight.Light, Glass.White);
            Loc.Bind(tagline, "Une vallée alpine, un col, des lacets : roule à ton rythme.", "An alpine valley, a mountain pass, hairpins: drive at your own pace.");

            // Menu : pilules en colonne, calées en bas à gauche, la première plus haute
            var box = Glass.Place(panel, "Buttons", Vector2.zero, new Vector2(90f, 72f), new Vector2(570f, 460f));
            var layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            playButton = Item(box, "JOUER", "PLAY", () => game.StartCountdown(), 90, 32).button;
            Item(box, "MULTIJOUEUR", "MULTIPLAYER", OpenLobby, 75, 26);
            Item(box, "OPTIONS", "OPTIONS", OpenOptions, 75, 26);
            // Langue : affiche celle en cours, un clic passe à l'autre
            Item(box, "LANGUE", "LANGUAGE", Loc.Toggle, 75, 26, "Français", "English");
            Item(box, "QUITTER", "QUIT", () => game.Quit(), 75, 26);

            // Bas à droite : pilule voiture du moment | cagnotte, puis touches utiles et version du jeu
            var footer = FitPanel(panel, "Footer", 45f, false, new RectOffset(36, 36, 15, 15), 27f, new Vector2(1f, 0f), new Vector2(-60f, 122f));
            footerGroup = footer.gameObject.AddComponent<CanvasGroup>();
            footerGroup.blocksRaycasts = false;
            carText = FooterColumn(footer.transform, "TA VOITURE", "YOUR CAR", Glass.White);
            var divider = Glass.Bar(footer.transform, "Divider", Glass.A(Color.white, 0.4f));
            var dle = divider.gameObject.AddComponent<LayoutElement>();
            dle.preferredWidth = 1.5f; dle.preferredHeight = 45f;
            cashText = FooterColumn(footer.transform, "CAGNOTTE", "CASH", Accent);

            keysText = Glass.Label(panel, "", 21, Glass.Weight.Light, Glass.White, TextAnchor.MiddleRight, new Vector2(1f, 0f), new Vector2(-60f, 72f), new Vector2(1400f, 32f));
            Glass.Soft(keysText, 0.8f);
            BuildChips(GameInput.UsingGamepad);
        }

        /// <summary>Colonne de la pilule du pied de page : petit titre cyan, valeur dessous.</summary>
        static Text FooterColumn(Transform parent, string fr, string en, Color valueColor)
        {
            var col = Rect(parent, "Column", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleLeft;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;
            var k = Glass.Kicker(col, "", 16);
            Loc.Bind(k, Glass.Spaced(fr, 2), Glass.Spaced(en, 2));
            return Glass.Label(col, "", 30, Glass.Weight.Regular, valueColor);
        }

        /// <summary>Bouton du menu : pilule de verre ; choisi, elle se remplit d'or (texte à l'encre, halo, chevron).</summary>
        MenuItem Item(Transform parent, string fr, string en, UnityEngine.Events.UnityAction onClick, float height, int size, string valueFr = null, string valueEn = null)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(LayoutElement), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            var item = new MenuItem { group = go.GetComponent<CanvasGroup>() };
            item.body = Rect(go.transform, "Body", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            item.plate = Glass.Make(item.body, "Pill", height * 0.5f, Glass.Panel, 1.5f);
            Glass.Stretch(item.plate.rectTransform);
            item.plate.raycastTarget = true;
            item.label = Glass.Label(item.body, "", size, Glass.Weight.Regular, White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(440f, height));
            Loc.Bind(item.label, Glass.Spaced(fr, 1), Glass.Spaced(en, 1));
            if (valueFr != null)
            {
                item.value = Glass.Label(item.body, "", 22, Glass.Weight.Regular, Info, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(260f, height));
                Loc.Bind(item.value, valueFr, valueEn);
            }
            item.chevron = Glass.Label(item.body, "›", size + 12, Glass.Weight.Semibold, Glass.Ink, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(-30f, 3f), new Vector2(40f, height));

            var b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = item.plate;
            b.onClick.AddListener(onClick);
            go.AddComponent<HoverSelect>();   // survol = sélection : un seul état « choisi », souris comme manette
            item.button = b;
            items.Add(item);
            return item;
        }

        /// <summary>Touches du bas de l'accueil : ligne fine, touche en or puis l'action, et la version (refaite si la manette change).</summary>
        void BuildChips(bool pad)
        {
            chipsForPad = pad;
            chipsFrench = Loc.French;
            string version = "v" + Application.version;
            if (Application.isMobilePlatform) { keysText.text = version; return; }
            string gold = ColorUtility.ToHtmlStringRGB(Accent);
            var keys = pad
                ? new[] { ("↑ ↓", "Choisir", "Navigate"), ("A", "Valider", "Select") }
                : new[] { ("↑ ↓", "Choisir", "Navigate"), (Loc.T("ENTRÉE", "ENTER"), "Valider", "Select") };
            var sb = new System.Text.StringBuilder();
            foreach (var (key, fr, en) in keys)
                sb.Append($"<color=#{gold}>{key}</color>  {Loc.T(fr, en)}   ·   ");
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGBA(Glass.Dim)}>{version}</color>");
            keysText.text = sb.ToString();
        }

        /// <summary>Animation de l'accueil : entrée en cascade, état des boutons, pied de page, touches.</summary>
        void UpdateTitle(float dt)
        {
            if (titleT >= 0f) titleT += dt;
            float T(float delay, float len) => titleT < 0f ? 1f : 1f - Mathf.Pow(1f - Mathf.Clamp01((titleT - delay) / len), 3f);

            float logo = T(0.05f, 0.7f);
            titleLogoGroup.alpha = logo;
            titleLogo.anchoredPosition = titleLogoPos + new Vector2(-(1f - logo) * 90f, 0f);
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
                float x = s * 12f - (1f - e) * 120f;
                it.body.offsetMin = new Vector2(x, 0f);
                it.body.offsetMax = new Vector2(x, 0f);
                // Pilule : verre → or plein avec halo (maillage refait seulement quand l'état bouge)
                if (Mathf.Abs(s - it.drawn) > 0.001f)
                {
                    it.drawn = s;
                    it.plate.color = Color.Lerp(Glass.Panel, Accent, s);
                    it.plate.borderColor = Color.Lerp(Glass.Edge, Glass.A(Accent, 0f), s);
                    it.plate.halo = 10.5f * s;
                    it.plate.haloColor = Glass.A(Accent, 0.25f * s);
                    it.plate.SetVerticesDirty();
                }
                it.label.color = Color.Lerp(White, Glass.Ink, s);
                bool strong = s > 0.5f;
                if (strong != it.strong)
                {
                    it.strong = strong;
                    it.label.font = Glass.FontOf(strong ? Glass.Weight.Semibold : Glass.Weight.Regular);
                }
                if (it.value)
                {
                    it.value.color = Color.Lerp(Info, Glass.Ink, s);
                    it.value.rectTransform.anchoredPosition = new Vector2(-36f - s * 34f, 0f);
                }
                it.chevron.color = Glass.A(Glass.Ink, s);
                it.chevron.rectTransform.anchoredPosition = new Vector2(-30f - (1f - s) * 10f, 3f);
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
    }
}
