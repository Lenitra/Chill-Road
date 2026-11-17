using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Écrans hors partie (construits en code) : chargement avec barre de progression, écran d'accueil
    /// (Jouer / Multijoueur / Options / Langue / Quitter) par-dessus la vue tournante de la voiture,
    /// et mise en scène du départ :
    /// l'accueil s'éjecte, bandes néon et flash, bandes cinéma, compte à rebours néon, « GO ! ».
    /// </summary>
    public class MenuScreens : MonoBehaviour
    {
        static readonly Color Accent = new Color(1f, 0.78f, 0.25f);
        static readonly Color White = new Color(0.97f, 0.97f, 0.97f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color NeonPink = new Color(1f, 0.18f, 0.55f);
        static readonly Color NeonCyan = new Color(0.1f, 0.9f, 1f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);

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
            ("À plusieurs : « Multijoueur » sur l'accueil, pour héberger une partie ou en rejoindre une sur le réseau local.",
             "With friends: pick “Multiplayer” on the title screen to host a game or join one on the local network."),
            ("L3 (ou L) allume les phares : indispensable la nuit et sous l'orage.",
             "L3 (or L) toggles the headlights: a must at night and in storms."),
            ("À plusieurs, fais-toi remarquer : croix ↓ (ou F) pour un appel de phares, R3 (ou H) pour le klaxon.",
             "With friends, get noticed: D-pad ↓ (or F) flashes your headlights, R3 (or H) sounds the horn."),
        };

        GameManager game;
        Font font;
        CanvasGroup loading, title;
        RectTransform titlePanel, wipeA, wipeB, barTop, barBottom;
        Image launchFlash;
        Outline countdownGlow;
        float launchT = -1f, launchLen = 3f, countdownPunch;
        RectTransform progressFill;
        Text stepText, countdownText;
        // Écran de chargement : décor animé, logo, astuce, pourcentage, reflet de la barre
        LoadingStage stage;
        RawImage stageView;
        RectTransform loadingLogo, progressSheen;
        Text loadingLogoText, percentText, tipText;
        HudShape sheenShape;
        float loadingT, tipTimer = 5f;
        int tipIndex;
        Button playButton;
        NetLobby lobby;
        float countdownTimer;
        GameManager.Phase shown = (GameManager.Phase)(-1);

        public void Init(GameManager gm)
        {
            game = gm;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var cgo = new GameObject("Menus", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvasComp = cgo.GetComponent<Canvas>();
            canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComp.sortingOrder = 10;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)cgo.transform;

            BuildTitle(root);
            BuildLoading(root);
            BuildLaunch(root);
            countdownText = Label(root, "", 240, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(1200, 320), true);
            countdownText.fontStyle = FontStyle.BoldAndItalic;
            countdownGlow = countdownText.gameObject.AddComponent<Outline>();
            countdownGlow.effectDistance = new Vector2(6, -6);
            lobby = gameObject.AddComponent<NetLobby>();
            lobby.Init(game);
            Refresh();
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Écran de chargement (le premier que l'on voit) : décor 3D fait des assets du jeu (<see cref="LoadingStage"/> :
        /// la voiture choisie sur une nationale, prairie, arbres, sous le ciel et la météo du jeu), logo, et plaque du HUD
        /// avec l'astuce du moment, l'étape en cours, le pourcentage et la barre de progression. En fin de chargement,
        /// l'écran s'efface en fondu sur l'accueil.
        /// </summary>
        void BuildLoading(RectTransform root)
        {
            var panel = Rect(root, "Loading", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            // Fond opaque sous le décor : rien du monde en construction ne transparaît
            panel.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.035f, 1f);
            loading = panel.gameObject.AddComponent<CanvasGroup>();

            // Décor 3D : la voiture choisie dans le monde du jeu, filmée par sa propre caméra
            stage = gameObject.AddComponent<LoadingStage>();
            stage.Build(game.assets, game.Player.Spec, game.Camera.GetComponent<Camera>());
            if (game.Weather && stage.Camera) game.Weather.AddCamera(stage.Camera);
            stageView = Rect(panel, "Stage", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            stageView.texture = stage.Texture;
            stageView.raycastTarget = false;
            // Voile léger en haut et en bas : le logo et la plaque restent lisibles sur le décor
            var veil = Rect(panel, "Veil", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            veil.texture = VerticalVeil();
            veil.raycastTarget = false;

            var logo = Logo(panel, new Vector2(0.5f, 0.74f), 160);
            loadingLogo = logo.rectTransform;
            loadingLogoText = logo;
            var tagline = LoadingLabel(panel, 30, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.78f), FontStyle.Italic, new Vector2(0.5f, 0.635f), Vector2.zero, new Vector2(1200, 44));
            Loc.Bind(tagline, "Une vallée alpine, un col, des lacets : roule à ton rythme.", "An alpine valley, a mountain pass, hairpins: drive at your own pace.");

            // Plaque du bas : astuce, étape et pourcentage, barre de progression
            var box = Rect(panel, "Progress", new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.12f), new Vector2(-640f, -82f), new Vector2(640f, 82f));
            Plate(box, new Color(0.02f, 0.025f, 0.04f, 0.82f)).Set(26f, 150f, 150f);
            tipText = LoadingLabel(box, 24, TextAnchor.MiddleCenter, White, FontStyle.Normal, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(1180, 34));
            tipText.supportRichText = true;
            tipIndex = Random.Range(0, Tips.Length);
            SetTip();
            stepText = LoadingLabel(box, 22, TextAnchor.MiddleLeft, Dim, FontStyle.Bold, new Vector2(0.5f, 0.5f), new Vector2(-560f, -4f), new Vector2(800, 30));
            stepText.rectTransform.pivot = new Vector2(0f, 0.5f);
            percentText = LoadingLabel(box, 32, TextAnchor.MiddleRight, Accent, FontStyle.BoldAndItalic, new Vector2(0.5f, 0.5f), new Vector2(560f, -4f), new Vector2(200, 40));
            percentText.rectTransform.pivot = new Vector2(1f, 0.5f);
            var bar = Rect(box, "Bar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-560f, -48f), new Vector2(560f, -36f));
            Plate(bar, new Color(1f, 1f, 1f, 0.1f)).Set(10f, 0f, 0f);
            progressFill = Rect(bar, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            progressFill.pivot = new Vector2(0f, 0.5f);
            Plate(progressFill, Accent).Set(10f, 0f, 0f);
            progressFill.localScale = new Vector3(0f, 1f, 1f);
            progressSheen = Rect(bar, "Sheen", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-45f, -6f), new Vector2(45f, 6f));
            sheenShape = Plate(progressSheen, new Color(1f, 1f, 0.92f, 0f));
            sheenShape.Set(10f, 36f, 36f);
        }

        /// <summary>Dégradé vertical : sombre en haut et surtout en bas, transparent au milieu (sur la voiture).</summary>
        static Texture2D VerticalVeil()
        {
            var tex = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 128; y++)
            {
                float v = y / 127f;
                float a = Mathf.Max(Mathf.Pow(1f - Mathf.Clamp01(v / 0.38f), 1.6f) * 0.78f, Mathf.Pow(Mathf.Clamp01((v - 0.62f) / 0.38f), 1.8f) * 0.5f);
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

        /// <summary>Texte de l'écran de chargement, dans la police du HUD.</summary>
        Text LoadingLabel(Transform parent, int size, TextAnchor align, Color color, FontStyle style, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var t = Label(parent, "", size, align, color, anchor, pos, box, false);
            t.font = HudArt.Font;
            t.fontStyle = style;
            return t;
        }

        void SetTip()
        {
            var tip = Tips[tipIndex];
            string hex = ColorUtility.ToHtmlStringRGB(Info);
            tipText.text = $"<color=#{hex}><b>{Loc.T("ASTUCE", "TIP")}</b></color>    {Loc.T(tip.fr, tip.en)}";
        }

        /// <summary>Animation de l'écran de chargement : logo, pourcentage, vitesse de la route, reflet, astuces.</summary>
        void UpdateLoading(float dt)
        {
            loadingT += dt;
            float progress = progressFill.localScale.x;
            int pct = Mathf.RoundToInt(progress * 100f);
            percentText.text = Loc.T($"{pct} %", $"{pct}%");
            if (stage && stageView.texture != stage.Texture) stageView.texture = stage.Texture;

            // Reflet qui parcourt la partie remplie de la barre
            float fill = ((RectTransform)progressFill.parent).rect.width * progress;
            float k = Mathf.Repeat(loadingT * 0.75f, 1.35f);
            progressSheen.anchoredPosition = new Vector2(Mathf.Min(k, 1f) * fill, 0f);
            float sheenA = k < 1f && fill > 80f ? Mathf.Sin(k * Mathf.PI) * 0.75f : 0f;
            sheenShape.color = new Color(1f, 1f, 0.92f, sheenA);

            // Logo : arrivée (léger zoom et fondu), puis respiration lente
            float intro = Mathf.Clamp01(loadingT / 0.8f);
            float ease = 1f - (1f - intro) * (1f - intro);
            loadingLogo.localScale = Vector3.one * (Mathf.Lerp(1.12f, 1f, ease) + Mathf.Sin(loadingT * 1.7f) * 0.008f);
            loadingLogoText.color = new Color(White.r, White.g, White.b, ease);

            // Astuces : une nouvelle toutes les 5 s, en fondu
            tipTimer -= dt;
            if (tipTimer <= 0f)
            {
                tipTimer = 5f;
                tipIndex = (tipIndex + 1) % Tips.Length;
                SetTip();
            }
            var c = tipText.color;
            c.a = Mathf.Clamp01(Mathf.Min((5f - tipTimer) / 0.35f, tipTimer / 0.35f));
            tipText.color = c;
        }

        void BuildTitle(RectTransform root)
        {
            var panel = Rect(root, "Title", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            titlePanel = panel;
            title = panel.gameObject.AddComponent<CanvasGroup>();
            // Dégradé sombre à gauche : lisibilité des boutons sur la vue de la voiture
            var shade = Rect(panel, "Shade", Vector2.zero, new Vector2(0.6f, 1f), Vector2.zero, Vector2.zero);
            var img = shade.gameObject.AddComponent<RawImage>();
            img.texture = Gradient();
            img.raycastTarget = false;

            var logo = Logo(panel, new Vector2(0.08f, 0.72f), 170);
            logo.alignment = TextAnchor.MiddleLeft;
            logo.rectTransform.pivot = new Vector2(0f, 0.5f);
            var tagline = Label(panel, "", 32, TextAnchor.MiddleLeft, Dim, new Vector2(0.08f, 0.6f), Vector2.zero, new Vector2(1100, 50), false);
            Loc.Bind(tagline, "Une vallée alpine, un col, des lacets : roule à ton rythme.", "An alpine valley, a mountain pass, hairpins: drive at your own pace.");
            tagline.rectTransform.pivot = new Vector2(0f, 0.5f);

            var box = Rect(panel, "Buttons", new Vector2(0.08f, 0.39f), new Vector2(0.08f, 0.39f), new Vector2(0, -218), new Vector2(460, 218));
            box.pivot = new Vector2(0f, 0.5f);
            var layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            playButton = Button(box, "JOUER", "PLAY", () => game.StartCountdown());
            // Partie en réseau : héberger, ou rejoindre une partie du réseau local ou d'une adresse IP
            Button(box, "MULTIJOUEUR", "MULTIPLAYER", OpenLobby);
            Button(box, "OPTIONS", "OPTIONS", OpenOptions);
            // Langue : affiche celle en cours, un clic passe à l'autre
            var language = Button(box, "LANGUE : FRANÇAIS", "LANGUAGE: ENGLISH", Loc.Toggle);
            language.GetComponent<LayoutElement>().preferredHeight = 56;
            language.GetComponentInChildren<Text>().fontSize = 26;
            Button(box, "QUITTER", "QUIT", () => game.Quit());

            var record = Label(panel, "", 30, TextAnchor.MiddleLeft, Accent, new Vector2(0.08f, 0.15f), Vector2.zero, new Vector2(900, 50), true);
            Loc.Bind(record, "Balade libre  ·  une vallée alpine de 5 km", "Free roam  ·  a 5 km alpine valley");
            record.rectTransform.pivot = new Vector2(0f, 0.5f);
            var hint = Label(panel, "", 22, TextAnchor.MiddleLeft, Dim, new Vector2(0.08f, 0.07f), Vector2.zero, new Vector2(900, 40), false);
            if (Application.isMobilePlatform) Loc.Bind(hint, "Touche un bouton pour valider", "Tap a button to select");
            else Loc.Bind(hint, "Manette : A pour valider  ·  Clavier : Entrée", "Gamepad: A to select  ·  Keyboard: Enter");
            hint.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

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
            // Deux bandes néon inclinées qui balaient l'écran
            wipeA = Wipe(root, "WipeA", NeonPink);
            wipeB = Wipe(root, "WipeB", NeonCyan);
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
            // Flash bref au départ, grand flash au « GO »
            float flashA = Mathf.Clamp01(1f - t / 0.22f) * 0.7f;
            float go = t - launchLen;
            if (go >= 0f) flashA = Mathf.Max(flashA, Mathf.Clamp01(1f - go / 0.35f) * 0.85f);
            launchFlash.color = new Color(1f, 1f, 1f, flashA);
            // Bandes néon : traversent l'écran en 0,5 s, la cyan juste derrière la rose
            wipeA.anchoredPosition = new Vector2(Mathf.Lerp(-2600f, 2600f, Mathf.Clamp01(t / 0.5f)), 0f);
            wipeB.anchoredPosition = new Vector2(Mathf.Lerp(-2600f, 2600f, Mathf.Clamp01((t - 0.09f) / 0.5f)), 0f);
            // Bandes cinéma : entrent pendant la plongée, ressortent au « GO »
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

        Text Logo(RectTransform parent, Vector2 anchor, int size)
        {
            var t = Label(parent, "NO HESI", size, TextAnchor.MiddleCenter, White, anchor, Vector2.zero, new Vector2(1400, size * 1.3f), true);
            t.fontStyle = FontStyle.BoldAndItalic;
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(1f, 0.65f, 0.1f, 0.55f);
            o.effectDistance = new Vector2(4, -4);
            return t;
        }

        static Texture2D Gradient()
        {
            var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 256; x++)
            {
                float a = Mathf.Pow(1f - x / 255f, 1.4f) * 0.8f;
                tex.SetPixel(x, 0, new Color(0.02f, 0.025f, 0.04f, a));
            }
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ mise à jour

        public void ShowCountdown(string text)
        {
            countdownText.text = text;
            bool go = text.StartsWith("GO");
            countdownTimer = go ? 1f : 0.55f;
            countdownPunch = 1f;
            // Chiffres néon alternés rose / cyan, « GO ! » blanc cerclé de cyan
            Color glow = go ? NeonCyan : text == "2" ? NeonCyan : NeonPink;
            countdownGlow.effectColor = new Color(glow.r, glow.g, glow.b, 0.9f);
            countdownText.color = go ? White : Color.Lerp(glow, White, 0.55f);
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
                // Le chargement s'efface en fondu ; une fois invisible, son décor animé s'arrête
                loading.alpha = Mathf.MoveTowards(loading.alpha, game.State == GameManager.Phase.Loading ? 1f : 0f, dt * 2.5f);
                loading.blocksRaycasts = loading.alpha > 0.5f;
                if (loading.alpha <= 0f && game.State != GameManager.Phase.Loading)
                {
                    loading.gameObject.SetActive(false);
                    if (stage) stage.Release();
                    stage = null;
                }
            }
            if (launchT < 0f) title.alpha = Mathf.MoveTowards(title.alpha, game.State == GameManager.Phase.Title && !OptionsMenu.IsOpen && !NetLobby.IsOpen ? 1f : 0f, dt * 3f);
            UpdateLaunch(dt);

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
            if (onTitle && EventSystem.current) EventSystem.current.SetSelectedGameObject(playButton.gameObject);
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

        Text Label(Transform parent, string text, int size, TextAnchor align, Color color, Vector2 anchor, Vector2 pos, Vector2 box, bool bold)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = color; t.text = text;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.6f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        Button Button(Transform parent, string fr, string en, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 64;
            go.GetComponent<Image>().color = Color.white;
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.normalColor = new Color(0.12f, 0.13f, 0.16f, 0.85f);
            colors.highlightedColor = new Color(0.25f, 0.27f, 0.32f, 0.95f);
            colors.selectedColor = new Color(0.95f, 0.65f, 0.15f, 1f);
            colors.pressedColor = new Color(1f, 0.8f, 0.4f, 1f);
            b.colors = colors;
            b.onClick.AddListener(onClick);
            Loc.Bind(Label(go.transform, "", 34, TextAnchor.MiddleCenter, White, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 64), true), fr, en);
            return b;
        }
    }
}
