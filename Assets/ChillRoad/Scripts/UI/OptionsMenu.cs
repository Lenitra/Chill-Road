using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Menu Options (construit en code), ouvert depuis l'accueil ou la pause, dans le style verre : plein écran sur
    /// l'image floutée du jeu (<see cref="MenuBackdrop"/>), titre fin et espacé à gauche d'une barre d'onglets
    /// (onglet ouvert souligné d'or), une plaque de verre arrondie qui porte les lignes, petits titres cyan espacés.
    /// La ligne choisie prend un fond or léger et un liseré or. Chaque réglage a le contrôle qui lui va : curseur fin
    /// pour les volumes et pourcentages, interrupteur pour activé / désactivé, pastilles pour les choix segmentés
    /// (langue, qualité, mode d'affichage…), carrousel « ‹ valeur › » pour la résolution.
    /// Rangé par onglets : Jeu (langue, trafic, pluie, secousses, vibrations, commandes tactiles si l'écran est
    /// tactile, retour au départ), Graphismes (écran : mode d'affichage, résolution et VSync sur ordinateur, limite
    /// d'images ; rendu : qualité, distance des détails, flou de mouvement), Audio (volume général puis une voie par
    /// famille de sons, titre des morceaux, son en arrière-plan), Touches (deux touches et deux boutons de manette par
    /// commande, <see cref="GameInput.Bind"/>). Tout s'applique tout de suite (mode d'affichage et résolution : un
    /// instant après le dernier changement).
    /// Manette : LB/RB (clavier : Tab, Maj+Tab) pour changer d'onglet, haut/bas pour choisir, gauche/droite pour
    /// régler, A / Entrée pour passer au choix suivant.
    /// </summary>
    public class OptionsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        GameManager game;
        GameObject root;
        RectTransform screen, frame;
        MenuBackdrop backdrop;
        Action onClose;
        Selectable first;
        Text applyText;
        string roadAtOpen;
        int openedFrame;
        readonly List<Action> refreshers = new List<Action>();
        // Animations des contrôles (pastille des interrupteurs, survol, ligne choisie en or), chaque image tant que c'est ouvert
        readonly List<Action<float>> animators = new List<Action<float>>();
        // Lignes : cible sélectionnable, fond de sélection, filet du bas, libellé (une ligne de Touches a quatre cibles)
        readonly List<(GameObject target, GlassRect plate, Graphic line, Text label)> rows = new List<(GameObject, GlassRect, Graphic, Text)>();
        // Filets toujours masqués (dernière ligne d'une rubrique ou d'une page), et dernier filet posé dans chaque page
        readonly HashSet<Graphic> trimmed = new HashSet<Graphic>();
        readonly Dictionary<Transform, Graphic> lastLine = new Dictionary<Transform, Graphic>();

        // Onglets : pages (une seule visible), lignes réglables de chaque page, boutons d'onglet
        static int lastTab;
        int tab;
        RectTransform[] pages;
        Selectable[][] pageItems;
        (Button button, Text text, Graphic mark)[] tabs;
        Button defaultsButton, backButton;

        // Mise en page (repère 1920 × 1080, maquette U-Options × 1,5) : cadre centré, barre du haut, plaque, bas
        const float FrameWidth = 1350f, FrameHeight = 1080f;
        const float BarTop = 48f, BarBottom = 124f, PanelTop = 183f;
        const float RowHeight = 72f, KeyRowHeight = 38f, Pad = 30f;

        // Mode d'affichage et définition : appliqués à cet instant (0 = rien en attente), pour ne pas changer de mode
        // d'écran à chaque cran quand on fait défiler les choix
        const float DisplayDelay = 0.6f;
        float displayAt;

        // Onglet Touches : grille commandes × emplacements (clavier 1, clavier 2, manette 1, manette 2) ; un clic sur un
        // emplacement attend la touche ou le bouton suivant (Échap / Start : annuler, Suppr / Retour arrière : vider).
        // Pendant l'attente, la navigation des menus est coupée ; elle revient deux images après, pour que la touche
        // choisie (A, Entrée, B…) n'agisse pas aussi sur le menu.
        const int GameTab = 0, GraphicsTab = 1, AudioTab = 2, KeysTab = 3;
        static readonly (float min, float max)[] SlotColumns = { (0.36f, 0.508f), (0.518f, 0.666f), (0.694f, 0.842f), (0.852f, 0.985f) };
        Button[,] keyGrid;
        bool capturing, navBlocked;
        GameInput.Bind captureBind;
        int captureSlot, captureFrame, captureEndFrame = -10;
        bool capturePad;

        public void Init(GameManager gm)
        {
            game = gm;
            var cgo = new GameObject("Options", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = cgo;
            screen = (RectTransform)cgo.transform;

            // Image du jeu floutée sous un voile, puis un cadre centré de la taille de la maquette (réduit si l'écran
            // est moins haut, voir FitFrame) : barre du haut, plaque de verre des réglages, boutons et aide en bas
            backdrop = MenuBackdrop.Attach(screen, 0.5f);
            frame = Glass.Place(screen, "Frame", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(FrameWidth, FrameHeight));

            // Barre du haut : « OPTIONS » fin et espacé à gauche, onglets à droite entre LB et RB, filet dessous
            var bar = Glass.Rect(frame, "TopBar", new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -BarBottom), new Vector2(0f, -BarTop));
            var title = Glass.Label(bar, Glass.Spaced("OPTIONS", 1), 63, Glass.Weight.Light, Glass.White, TextAnchor.LowerLeft);
            Glass.Stretch(title.rectTransform);
            var sep = Glass.Bar(bar, "Line", Glass.A(Color.white, 0.22f));
            var srt = sep.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = new Vector2(1f, 0f);
            srt.offsetMin = new Vector2(0f, -1.5f);
            srt.offsetMax = Vector2.zero;

            var tabRow = Glass.Rect(bar, "Tabs", new Vector2(1f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
            tabRow.pivot = new Vector2(1f, 0.5f);
            var th = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 39;
            th.childAlignment = TextAnchor.LowerRight;
            th.childControlWidth = true; th.childControlHeight = true;
            th.childForceExpandWidth = false; th.childForceExpandHeight = true;
            tabRow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Plaque de verre : sa hauteur suit la page ouverte (la plus longue, Touches, tient dans le budget)
            var panel = Glass.Rect(frame, "Panel", new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -PanelTop);
            panel.sizeDelta = new Vector2(0f, 700f);
            Glass.Backdrop(panel, 42f, Glass.Panel);
            var pl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            pl.padding = new RectOffset(18, 18, 24, 15);
            pl.childControlHeight = true; pl.childControlWidth = true;
            pl.childForceExpandHeight = false; pl.childForceExpandWidth = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var tabNames = new[] { ("JEU", "GAMEPLAY"), ("GRAPHISMES", "GRAPHICS"), ("AUDIO", "AUDIO"), ("TOUCHES", "CONTROLS") };
            TabGlyph(tabRow, "LB");
            tabs = new (Button, Text, Graphic)[tabNames.Length];
            pages = new RectTransform[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                tabs[i] = AddTab(tabRow, tabNames[i].Item1, tabNames[i].Item2, i);
                pages[i] = Page(panel, tabNames[i].Item2);
            }
            TabGlyph(tabRow, "RB");

            // Jeu : langue, monde (trafic, pluie), caméra, commandes tactiles, puis le retour au départ
            var play = pages[GameTab];
            Section(play, "GÉNÉRAL", "GENERAL", "", "");
            var language = AddChoice(play, "Langue", "Language", 2, () => Loc.French ? 0 : 1,
                i => Loc.Set(i == 0 ? Language.French : Language.English), i => i == 0 ? "Français" : "English");
            var traffic = AddSlider(play, "Densité du trafic", "Traffic density", 0.2f, 2f, 18, () => GameSettings.TrafficDensity, v => GameSettings.TrafficDensity = v, Percent);
            var rain = AddSlider(play, "Probabilité de pluie", "Rain probability", 0f, 1f, 20, () => GameSettings.RainChance, v => GameSettings.RainChance = v, v => v < 0.01f ? Loc.T("Jamais", "Never") : v > 0.99f ? Loc.T("Toujours", "Always") : Percent(v));
            var shake = AddToggle(play, "Secousses de caméra", "Camera shake", () => GameSettings.CameraShake, v => GameSettings.CameraShake = v);
            // Vibrations de la manette : intensité, puis les déclencheurs sur une ligne (l'onglet est plein), chacun avec
            // un échantillon quand on l'active
            var rumble = AddSlider(play, "Vibrations de la manette", "Controller vibration", 0f, 1f, 10, () => GameSettings.Rumble,
                v => { GameSettings.Rumble = v; GamepadRumble.Preview(); }, v => v < 0.01f ? Loc.T("Désactivées", "Off") : Percent(v));
            var rumbleTriggers = AddFlags(play, "Vibrer pour", "Vibrate on",
                new[] { ("Drift", "Drift"), ("Vitesse", "Speed"), ("Collisions", "Collisions") },
                i => i == 0 ? GameSettings.RumbleDrift : i == 1 ? GameSettings.RumbleSpeed : GameSettings.RumbleCrash,
                (i, v) =>
                {
                    if (i == 0) GameSettings.RumbleDrift = v;
                    else if (i == 1) GameSettings.RumbleSpeed = v;
                    else GameSettings.RumbleCrash = v;
                    GamepadRumble.Preview(i == 0 ? GamepadRumble.Trigger.Drift : i == 1 ? GamepadRumble.Trigger.Speed : GamepadRumble.Trigger.Collision);
                });
            // Commandes tactiles (téléphone, tablette) : façon de diriger, ou désactivées. Absentes sur un PC sans
            // écran tactile, où elles ne s'affichent jamais. Sans rubrique à elles : l'onglet est plein.
            Selectable touch = null;
            if (TouchScreen)
            {
                touch = AddChoice(play, "Commandes tactiles", "Touch controls", 4, () => GameSettings.TouchSteer, i => GameSettings.TouchSteer = i,
                    i => i == 0 ? Loc.T("Désactivées", "Off") : i == 1 ? Loc.T("Glisser", "Slide") : i == 2 ? Loc.T("Boutons ‹ ›", "‹ › buttons") : Loc.T("Inclinaison", "Tilt"));
            }
            Section(play, "CARTE", "MAP", "", "");
            var apply = AddAction(play, "Revenir au départ (route de la vallée)", "Back to the start (valley road)",
                "Replacer la voiture", "Reset the car", ApplyRoad);
            applyText = apply.label;

            // Graphismes : l'écran (mode d'affichage, définition et synchro verticale sur ordinateur seulement ; mode et
            // définition appliqués un instant après le dernier changement, voir Update), puis le rendu
            var gfx = pages[GraphicsTab];
            Section(gfx, "ÉCRAN", "DISPLAY", "", "");
            Selectable window = null, resolution = null, vsync = null;
            if (GraphicsQuality.HasDisplayOptions)
            {
                window = AddChoice(gfx, "Mode d'affichage", "Display mode", GraphicsQuality.WindowModes.Length,
                    () => GameSettings.WindowMode, m =>
                    {
                        if (m == GameSettings.WindowMode) return;
                        GameSettings.WindowMode = m;
                        displayAt = Time.unscaledTime + DisplayDelay;
                    },
                    GraphicsQuality.WindowModeName);
                resolution = AddCarousel(gfx, "Résolution", "Resolution", () => GraphicsQuality.Resolutions.Length,
                    () => GraphicsQuality.ResolutionIndex, i =>
                    {
                        if (i == GraphicsQuality.ResolutionIndex) return;
                        GraphicsQuality.SetResolutionIndex(i);
                        displayAt = Time.unscaledTime + DisplayDelay;
                    },
                    GraphicsQuality.ResolutionName);
                vsync = AddToggle(gfx, "Synchro verticale (VSync)", "Vertical sync (VSync)", () => GameSettings.VSync, v => GameSettings.VSync = v);
            }
            // (VSync activée : la limite suit l'écran, les choix laissent place à la mention)
            var fps = AddChoice(gfx, "Limite d'images", "Frame rate limit", GraphicsQuality.FpsCaps.Length,
                () => GameSettings.FpsCap, i => GameSettings.FpsCap = i, GraphicsQuality.FpsName,
                () => GameSettings.VSync && GraphicsQuality.HasDisplayOptions ? Loc.T("Suit l'écran (VSync)", "Follows display (VSync)") : null);

            // Rendu : changer la qualité remet la distance des détails au niveau conseillé
            Section(gfx, "RENDU", "RENDERING", "", "");
            var quality = AddChoice(gfx, "Qualité graphique", "Graphics quality", 4, () => GameSettings.Quality, q =>
                {
                    if (q == GameSettings.Quality) return;
                    GameSettings.Quality = q;
                    GameSettings.DetailDistance = GraphicsQuality.DefaultDetail(q);
                },
                GraphicsQuality.Name);
            var detail = AddChoice(gfx, "Distance des détails", "Detail distance", GraphicsQuality.DetailLevels,
                () => GameSettings.DetailDistance, i => GameSettings.DetailDistance = i, GraphicsQuality.DetailName);
            var blur = AddToggle(gfx, "Flou de mouvement", "Motion blur", () => GameSettings.MotionBlur, v => GameSettings.MotionBlur = v);

            // Audio : volume général puis une voie par famille de sons ; la musique ; le son quand la fenêtre est
            // en arrière-plan (ordinateur seulement)
            var audio = pages[AudioTab];
            Section(audio, "VOLUME", "VOLUME", "", "");
            var master = AddSlider(audio, "Volume général", "Master volume", 0f, 1f, 20, () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, Percent);
            var music = AddSlider(audio, "Musique", "Music", 0f, 1f, 20, () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, Percent);
            var engine = AddSlider(audio, "Moteur", "Engine", 0f, 1f, 20, () => GameSettings.EngineVolume, v => GameSettings.EngineVolume = v, Percent);
            var effects = AddSlider(audio, "Effets", "Effects", 0f, 1f, 20, () => GameSettings.EffectsVolume, v => GameSettings.EffectsVolume = v, Percent);
            var ambience = AddSlider(audio, "Ambiance", "Ambience", 0f, 1f, 20, () => GameSettings.AmbienceVolume, v => GameSettings.AmbienceVolume = v, Percent);
            var ui = AddSlider(audio, "Sons du score", "Score sounds", 0f, 1f, 20, () => GameSettings.UiVolume, v => GameSettings.UiVolume = v, Percent);
            Section(audio, "LECTURE", "PLAYBACK", "", "");
            var trackNames = AddToggle(audio, "Titre des morceaux", "Track titles", () => GameSettings.TrackNames, v => GameSettings.TrackNames = v,
                ("Affiché", "Shown"), ("Masqué", "Hidden"));
            Selectable background = null;
            if (GraphicsQuality.HasDisplayOptions)
                background = AddToggle(audio, "Son en arrière-plan", "Sound in background", () => GameSettings.BackgroundAudio, v => GameSettings.BackgroundAudio = v);

            // Touches
            BuildKeys(pages[KeysTab]);
            // Pas de filet sous la dernière ligne d'une page
            foreach (var l in lastLine.Values) trimmed.Add(l);

            // Bas : « Par défaut » et « Retour » en pilules de verre à gauche, aide manette / clavier à droite
            var bottom = Glass.Rect(frame, "Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 51f), new Vector2(0f, 126f));
            var bh = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.spacing = 21;
            bh.childAlignment = TextAnchor.MiddleLeft;
            bh.childControlWidth = true; bh.childControlHeight = true;
            bh.childForceExpandWidth = false; bh.childForceExpandHeight = true;
            var defaults = PillButton(bottom, "Defaults", 24, Glass.Weight.Regular, new RectOffset(42, 42, 0, 0), TextAnchor.MiddleCenter,
                () => { GameSettings.ResetAll(); GameInput.ResetBindings(); displayAt = Time.unscaledTime + 0.01f; Changed(); RefreshAll(); });
            Loc.Bind(defaults.text, "Par défaut", "Defaults");
            PillLook(defaults.button, defaults.plate, defaults.text);
            var back = PillButton(bottom, "Back", 24, Glass.Weight.Regular, new RectOffset(42, 42, 0, 0), TextAnchor.MiddleCenter, Close);
            Loc.Bind(back.text, "Retour", "Back");
            PillLook(back.button, back.plate, back.text);
            defaultsButton = defaults.button;
            backButton = back.button;

            var hint = Glass.Label(bottom, "", 21, Glass.Weight.Light, Glass.Dim, TextAnchor.MiddleRight);
            hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Loc.Bind(hint, Hint("LB RB / Tab", "catégorie") + Dot + Hint("↑ ↓", "choisir") + Dot + Hint("← →", "régler") + Dot + Hint("B / Échap", "retour"),
                           Hint("LB RB / Tab", "category") + Dot + Hint("↑ ↓", "select") + Dot + Hint("← →", "adjust") + Dot + Hint("B / Esc", "back"));

            // Navigation explicite par page (voir ShowTab) : gauche/droite restent aux curseurs (réglage)
            var gameItems = new List<Selectable> { language, traffic, rain, shake, rumble, rumbleTriggers };
            if (touch) gameItems.Add(touch);
            gameItems.Add(apply.button);
            var audioItems = new List<Selectable> { master, music, engine, effects, ambience, ui, trackNames };
            if (background) audioItems.Add(background);
            pageItems = new[]
            {
                gameItems.ToArray(),
                window ? new Selectable[] { window, resolution, vsync, fps, quality, detail, blur } : new Selectable[] { fps, quality, detail, blur },
                audioItems.ToArray(),
                KeyColumn(),
            };
            ShowTab(lastTab);

            RefreshAll();
            root.SetActive(false);
        }

        /// <summary>Téléphone, tablette, ou PC muni d'un écran tactile : seuls appareils où les commandes tactiles s'affichent.</summary>
        static bool TouchScreen => Application.isMobilePlatform || UnityEngine.InputSystem.Touchscreen.current != null;

        static string Percent(float v) => Loc.T($"{Mathf.RoundToInt(v * 100f)} %", $"{Mathf.RoundToInt(v * 100f)}%");

        const string Dot = "   ·   ";
        static string Hint(string keys, string what) => $"<color=#FFC233>{keys}</color>  {what}";

        // ------------------------------------------------------------------ ouverture / fermeture

        /// <summary>Ouvre le menu ; <paramref name="closed"/> est appelé à la fermeture (retour à l'accueil ou à la pause).</summary>
        public void Open(Action closed)
        {
            onClose = closed;
            roadAtOpen = GameSettings.RoadSignature;
            // En réseau le monde continue : l'image floutée derrière est refaite régulièrement
            if (backdrop) backdrop.live = game && game.Online;
            ShowTab(lastTab);
            RefreshAll();
            root.SetActive(true);
            IsOpen = true;
            openedFrame = Time.frameCount;
            FitFrame();
            EventSystem.current?.SetSelectedGameObject(first.gameObject);
            HighlightRows();
            // Contrôles posés tout de suite à leur état (pas d'image avec les couleurs de construction)
            foreach (var a in animators) a(10f);
        }

        public void Close()
        {
            if (capturing) EndCapture();
            RestoreNavigation();
            FlushDisplay();
            GameSettings.Save();
            root.SetActive(false);
            IsOpen = false;
            onClose?.Invoke();
        }

        void ApplyRoad()
        {
            GameSettings.Save();
            // En réseau, la partie continue : la voiture revient au départ et le menu se referme
            if (game.Online)
            {
                game.Restart();
                Close();
                return;
            }
            IsOpen = false;
            game.Restart();
        }

        void Changed()
        {
            // (l'éditeur rappelle les curseurs à chaque reconstruction du canvas, même hors partie)
            if (game) game.ApplyLiveSettings();
        }

        void RefreshAll()
        {
            foreach (var r in refreshers) r();
        }

        void FlushDisplay()
        {
            if (displayAt <= 0f) return;
            displayAt = 0f;
            GraphicsQuality.ApplyDisplay();
        }

        /// <summary>Cadre réduit quand l'écran est moins haut ou moins large que la maquette (écran ultra-large, téléphone).</summary>
        void FitFrame()
        {
            if (!screen || !frame) return;
            var r = screen.rect;
            if (r.height <= 0f || r.width <= 0f) return;
            float s = Mathf.Min(1f, r.height / FrameHeight, (r.width - 32f) / FrameWidth);
            if (Mathf.Abs(frame.localScale.x - s) > 0.001f) frame.localScale = new Vector3(s, s, 1f);
        }

        void Update()
        {
            if (displayAt > 0f && Time.unscaledTime >= displayAt) FlushDisplay();
            if (!IsOpen) return;
            FitFrame();
            foreach (var a in animators) a(Time.unscaledDeltaTime);
            if (Time.frameCount == openedFrame) return;
            if (capturing) { UpdateCapture(); return; }
            if (Time.frameCount <= captureEndFrame + 1) { HighlightRows(); return; }
            RestoreNavigation();
            if (GameInput.CancelPressed)
            {
                Close();
                return;
            }
            if (GameInput.TabPrevPressed) ShowTab(tab - 1);
            else if (GameInput.TabNextPressed) ShowTab(tab + 1);
            // Onglet cliqué à la souris puis manette : revenir sur une ligne de la page
            var current = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (current && GameInput.UsingGamepad)
                foreach (var t in tabs)
                    if (current == t.button.gameObject) EventSystem.current.SetSelectedGameObject(first.gameObject);
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == null && GameInput.UsingGamepad)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
            HighlightRows();
        }

        /// <summary>Affiche la page d'un onglet, relie sa colonne aux boutons du bas et choisit sa première ligne.</summary>
        void ShowTab(int index)
        {
            tab = lastTab = (index % pages.Length + pages.Length) % pages.Length;
            for (int i = 0; i < pages.Length; i++)
            {
                bool on = i == tab;
                pages[i].gameObject.SetActive(on);
                tabs[i].mark.enabled = on;
                tabs[i].text.color = on ? Glass.Gold : Glass.Dim;
                tabs[i].text.font = Glass.FontOf(on ? Glass.Weight.Regular : Glass.Weight.Semilight);
            }

            // Colonne de la page puis « Par défaut », en boucle ; « Retour » à droite de « Par défaut »
            var items = pageItems[tab];
            var column = new Selectable[items.Length + 1];
            items.CopyTo(column, 0);
            column[items.Length] = defaultsButton;
            Column(column);
            Selectable last = items[items.Length - 1];
            first = items[0];
            Nav(defaultsButton, last, first, null, backButton);
            Nav(backButton, last, first, defaultsButton, null);

            if (tab == KeysTab) WireKeyGrid();

            if (IsOpen && EventSystem.current) EventSystem.current.SetSelectedGameObject(first.gameObject);
            HighlightRows();
        }

        static bool Focused(GameObject g) => EventSystem.current && g && EventSystem.current.currentSelectedGameObject == g;

        /// <summary>Ligne du réglage choisi (manette ou clic) : fond or léger, liseré or, libellé un peu plus appuyé, sans filet.</summary>
        void HighlightRows()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            // (une ligne de l'onglet Touches a quatre cibles pour le même fond : on cherche d'abord le fond choisi)
            GlassRect chosen = null;
            foreach (var r in rows)
                if (r.target == selected) chosen = r.plate;
            foreach (var r in rows)
            {
                bool on = chosen != null && r.plate == chosen;
                r.plate.enabled = on;
                r.line.enabled = !on && !trimmed.Contains(r.line);
                var f = Glass.FontOf(on ? Glass.Weight.Regular : Glass.Weight.Semilight);
                if (r.label.font != f) r.label.font = f;
            }
        }

        // ------------------------------------------------------------------ touches

        /// <summary>Rubriques de l'onglet Touches : première commande de chaque groupe et son titre.</summary>
        static readonly (GameInput.Bind first, string fr, string en)[] KeyGroups =
        {
            (GameInput.Bind.Throttle, "CONDUITE", "DRIVING"), (GameInput.Bind.LookBack, "CAMÉRA", "CAMERA"),
            (GameInput.Bind.Reset, "VOITURE", "CAR"), (GameInput.Bind.Music, "JEU", "GAME"),
        };

        /// <summary>
        /// Onglet Touches : commandes groupées en rubriques (titres cyan ; la première porte aussi les en-têtes Clavier et
        /// Manette), une ligne par commande, deux touches et deux boutons. Touches du clavier en touches carrées, boutons de
        /// la manette en pastilles rondes ; un emplacement libre n'est qu'un « + » discret.
        /// </summary>
        void BuildKeys(RectTransform page)
        {
            keyGrid = new Button[GameInput.BindCount, 4];
            for (int b = 0; b < GameInput.BindCount; b++)
            {
                var bind = (GameInput.Bind)b;
                foreach (var g in KeyGroups)
                    if (g.first == bind) KeySection(page, g.fr, g.en, b == 0);
                var row = Glass.Rect(page, bind.ToString(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Fixed(row, KeyRowHeight);
                var plate = SelectionPlate(row, 23f);
                var rowLine = RowLine(row);
                lastLine[page] = rowLine;
                var name = Glass.Label(row, "", 22, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleLeft);
                Stretch(name.rectTransform, 0f, SlotColumns[0].min, Pad, 8f);
                refreshers.Add(() => name.text = GameInput.BindName(bind));
                for (int c = 0; c < 4; c++)
                {
                    var slot = KeySlot(row, bind, c);
                    keyGrid[b, c] = slot;
                    rows.Add((slot.gameObject, plate, rowLine, name));
                }
            }
        }

        /// <summary>Titre de rubrique de l'onglet Touches (plus bas que Section, pour que les 14 commandes tiennent).</summary>
        void KeySection(RectTransform page, string fr, string en, bool first)
        {
            if (lastLine.TryGetValue(page, out var prev)) trimmed.Add(prev);
            var row = Glass.Rect(page, "Section", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, first ? 34f : 38f);
            HeadLabel(row, fr, en, 0f, 0.35f, TextAnchor.LowerLeft);
            if (!first) return;
            HeadLabel(row, "CLAVIER", "KEYBOARD", SlotColumns[0].min, SlotColumns[1].max, TextAnchor.LowerCenter);
            HeadLabel(row, "MANETTE", "GAMEPAD", SlotColumns[2].min, SlotColumns[3].max, TextAnchor.LowerCenter);
        }

        void HeadLabel(RectTransform parent, string fr, string en, float xMin, float xMax, TextAnchor align)
        {
            var t = Loc.Bind(Glass.Kicker(parent, "", 16), Glass.Spaced(fr, 2), Glass.Spaced(en, 2));
            t.alignment = align;
            Stretch(t.rectTransform, xMin, xMax, align == TextAnchor.LowerLeft ? Pad : 0f, 0f);
            t.rectTransform.offsetMin = new Vector2(t.rectTransform.offsetMin.x, 10f);
        }

        /// <summary>Emplacement : pastille de verre arrondie portant la touche ou le bouton choisi (« … » en or pendant l'attente).</summary>
        Button KeySlot(RectTransform row, GameInput.Bind bind, int column)
        {
            bool pad = column >= 2;
            int slot = column % 2;
            var (button, plate, text) = PillButton(row, "Slot", 20, Glass.Weight.Regular, null, TextAnchor.MiddleCenter, () => StartCapture(bind, slot, pad));
            plate.radius = pad ? 16f : 8f;   // clavier : touche carrée ; manette : bouton rond
            var hit = button.GetComponent<OptionHit>();
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(SlotColumns[column].min, 0.5f);
            rt.anchorMax = new Vector2(SlotColumns[column].max, 0.5f);
            rt.offsetMin = new Vector2(0f, -16f);
            rt.offsetMax = new Vector2(0f, 16f);
            bool Waiting() => capturing && captureBind == bind && captureSlot == slot && capturePad == pad;
            bool Empty() => pad ? string.IsNullOrEmpty(GameInput.GetPad(bind, slot)) : GameInput.GetKey(bind, slot) == Key.None;
            refreshers.Add(() =>
            {
                text.text = Waiting() ? Loc.T("Appuie…", "Press…") : Empty() ? "+"
                    : pad ? GameInput.PadName(GameInput.GetPad(bind, slot)) : GameInput.KeyName(GameInput.GetKey(bind, slot));
            });
            animators.Add(_ =>
            {
                // Attente : liseré et halo or ; emplacement choisi : or plein, encre ; sinon verre
                if (Waiting())
                {
                    Look(plate, Glass.GoldSoft, Glass.Gold, 8f);
                    Ink(text, Glass.Gold, Glass.Weight.Semibold);
                }
                else if (Focused(button.gameObject))
                {
                    Look(plate, Glass.Gold, Color.clear, 0f);
                    Ink(text, Glass.Ink, Glass.Weight.Semibold);
                }
                else if (Empty())
                {
                    // Libre : pas de fond, liseré à peine visible, « + » estompé
                    Look(plate, hit.Hover ? Glass.Pill : Color.clear, hit.Hover ? Glass.Edge : Glass.Line, 0f);
                    Ink(text, Glass.Faint, Glass.Weight.Light);
                }
                else
                {
                    Look(plate, hit.Hover ? Glass.A(Color.white, 0.16f) : Glass.Pill, Glass.Edge, 0f);
                    Ink(text, Glass.White, Glass.Weight.Regular);
                }
            });
            return button;
        }

        /// <summary>Première colonne de la grille (chaîne haut/bas générique de ShowTab, complétée par WireKeyGrid).</summary>
        Selectable[] KeyColumn()
        {
            var column = new Selectable[GameInput.BindCount];
            for (int b = 0; b < column.Length; b++) column[b] = keyGrid[b, 0];
            return column;
        }

        /// <summary>Navigation en grille : haut/bas dans la colonne (« Par défaut » aux deux bouts), gauche/droite entre emplacements.</summary>
        void WireKeyGrid()
        {
            int n = GameInput.BindCount;
            for (int b = 0; b < n; b++)
                for (int c = 0; c < 4; c++)
                    Nav(keyGrid[b, c], b > 0 ? keyGrid[b - 1, c] : defaultsButton, b < n - 1 ? keyGrid[b + 1, c] : defaultsButton,
                        c > 0 ? keyGrid[b, c - 1] : null, c < 3 ? keyGrid[b, c + 1] : null);
        }

        void StartCapture(GameInput.Bind bind, int slot, bool pad)
        {
            capturing = true;
            captureBind = bind;
            captureSlot = slot;
            capturePad = pad;
            captureFrame = Time.frameCount;
            if (EventSystem.current) { EventSystem.current.sendNavigationEvents = false; navBlocked = true; }
            RefreshAll();
        }

        void UpdateCapture()
        {
            if (Time.frameCount == captureFrame) return;
            var kb = Keyboard.current;
            bool cancel = kb != null && kb.escapeKey.wasPressedThisFrame;
            foreach (var p in Gamepad.all) if (p.startButton.wasPressedThisFrame) cancel = true;
            if (cancel) { EndCapture(); return; }
            bool clear = kb != null && (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame);
            if (capturePad)
            {
                if (clear) { GameInput.SetPad(captureBind, captureSlot, ""); EndCapture(); return; }
                string control = GameInput.PressedPadControl();
                if (control != null) { GameInput.SetPad(captureBind, captureSlot, control); EndCapture(); }
            }
            else
            {
                if (clear) { GameInput.SetKey(captureBind, captureSlot, Key.None); EndCapture(); return; }
                Key key = GameInput.PressedKey();
                if (key != Key.None) { GameInput.SetKey(captureBind, captureSlot, key); EndCapture(); return; }
                // Manette pendant l'attente d'une touche : B annule
                foreach (var p in Gamepad.all) if (p.buttonEast.wasPressedThisFrame) { EndCapture(); return; }
            }
        }

        void EndCapture()
        {
            capturing = false;
            captureEndFrame = Time.frameCount;
            RefreshAll();
        }

        void RestoreNavigation()
        {
            if (!navBlocked) return;
            navBlocked = false;
            if (EventSystem.current) EventSystem.current.sendNavigationEvents = true;
        }

        // ------------------------------------------------------------------ barre du haut et pages

        /// <summary>Page d'un onglet : colonne de lignes, la plaque prend sa hauteur.</summary>
        static RectTransform Page(RectTransform parent, string name)
        {
            var page = Glass.Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 0;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            return page;
        }

        /// <summary>Onglet : texte espacé, or et souligné d'or quand il est ouvert (voir ShowTab) ; toute la hauteur de la barre est cliquable.</summary>
        (Button button, Text text, Graphic mark) AddTab(Transform parent, string fr, string en, int index)
        {
            var (button, plate, text) = PillButton(parent, "Tab", 25, Glass.Weight.Semilight, new RectOffset(6, 6, 0, 18), TextAnchor.LowerCenter, () => ShowTab(index));
            plate.color = Color.clear;
            plate.border = 0f;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Loc.Bind(text, Glass.Spaced(fr, 1), Glass.Spaced(en, 1));
            var mark = Glass.Make(button.transform, "Active", 2f, Glass.Gold, 0f);
            mark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = mark.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, 4.5f);
            return (button, text, mark);
        }

        /// <summary>Repère de bouton manette (LB, RB) au bord de la rangée d'onglets : petite pastille cernée.</summary>
        static void TabGlyph(Transform parent, string glyph)
        {
            var cell = Glass.Rect(parent, glyph, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            cell.gameObject.AddComponent<LayoutElement>().preferredWidth = 50;
            var chip = Glass.Make(cell, "Chip", 16f, Color.clear, 1.5f);
            chip.borderColor = Glass.A(Color.white, 0.4f);
            Put(chip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(50f, 32f));
            var t = Glass.Label(chip.transform, glyph, 20, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(t.rectTransform);
        }

        /// <summary>Rubrique : petit titre cyan espacé (précision estompée à côté si besoin) ; la ligne d'avant perd son filet.</summary>
        void Section(RectTransform parent, string fr, string en, string noteFr, string noteEn)
        {
            if (lastLine.TryGetValue(parent, out var prev)) trimmed.Add(prev);
            bool top = parent.childCount == 0;
            var row = Glass.Rect(parent, "Section", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, top ? 36f : 57f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18;
            h.childAlignment = TextAnchor.LowerLeft;
            h.padding = new RectOffset((int)Pad, (int)Pad, 0, 9);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            if (fr.Length > 0) Loc.Bind(Glass.Kicker(row, "", 17), Glass.Spaced(fr, 2), Glass.Spaced(en, 2));
            if (noteFr.Length > 0)
            {
                string dot = fr.Length > 0 ? "·  " : "";
                Loc.Bind(Glass.Label(row, "", 20, Glass.Weight.Semilight, Glass.Faint, TextAnchor.LowerLeft), dot + noteFr, dot + noteEn);
            }
        }

        // ------------------------------------------------------------------ lignes

        // Contrôles de réglage : curseur pour les valeurs continues (volumes, pourcentages), interrupteur pour
        // activé / désactivé, pastilles quand toutes les valeurs tiennent sur la ligne, carrousel « ‹ valeur › »
        // pour les longues listes (résolution). Tous occupent la partie droite de la ligne, à partir de ControlLeft.
        const float ControlLeft = 0.42f;
        static readonly Color ChipEdge = new Color(1f, 1f, 1f, 0.3f);
        static readonly Color ChipHover = new Color(1f, 1f, 1f, 0.6f);

        /// <summary>Fond de sélection d'une ligne : or léger et liseré or arrondi (allumé par HighlightRows).</summary>
        static GlassRect SelectionPlate(RectTransform row, float radius)
        {
            var plate = Glass.Make(row, "Selected", radius, Glass.GoldSoft, 1.5f);
            plate.borderColor = Glass.Gold;
            Glass.Stretch(plate.rectTransform);
            plate.enabled = false;
            return plate;
        }

        /// <summary>Filet discret sous une ligne.</summary>
        static Graphic RowLine(RectTransform row)
        {
            var line = Glass.Bar(row, "Line", Glass.A(Color.white, 0.1f));
            var lrt = line.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = new Vector2(0f, 1.5f);
            return line;
        }

        /// <summary>Ligne de réglage : nom à gauche, fond et liseré or quand elle est choisie (HighlightRows), filet dessous.</summary>
        RectTransform OptionRow(RectTransform parent, string fr, string en, out GlassRect plate, out Graphic line, out Text label)
        {
            var row = Glass.Rect(parent, en, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            plate = SelectionPlate(row, 27f);
            line = RowLine(row);
            lastLine[parent] = line;
            label = Loc.Bind(Glass.Label(row, "", 27, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleLeft), fr, en);
            Stretch(label.rectTransform, 0f, ControlLeft, Pad, 12f);
            return row;
        }

        /// <summary>Ligne à choix (<see cref="OptionSelector"/>) : toute la ligne capte la souris et devient la ligne choisie.</summary>
        OptionSelector SelectorRow(RectTransform parent, string fr, string en, out RectTransform row)
        {
            row = OptionRow(parent, fr, en, out var plate, out var line, out var label);
            var hit = Glass.Make(row, "Hit", 0f, Color.clear, 0f);
            hit.raycastTarget = true;
            hit.transform.SetAsFirstSibling();
            Glass.Stretch(hit.rectTransform);
            var selector = row.gameObject.AddComponent<OptionSelector>();
            selector.transition = Selectable.Transition.None;
            rows.Add((row.gameObject, plate, line, label));
            return selector;
        }

        /// <summary>Pastille de choix cliquable : cernée de blanc, or plein et encre quand elle est prise ; largeur au texte.</summary>
        static (GlassRect plate, Text text, OptionHit hit) Chip(Transform parent, string name, int size)
        {
            var plate = Glass.Make(parent, name, 22f, Color.clear, 1.5f);
            plate.borderColor = ChipEdge;
            plate.raycastTarget = true;
            var h = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(24, 24, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = false;
            var text = Glass.Label(plate.transform, "", size, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            return (plate, text, plate.gameObject.AddComponent<OptionHit>());
        }

        /// <summary>Rangée de pastilles calée à droite de la ligne.</summary>
        static RectTransform ChipArea(RectTransform row, string name)
        {
            var area = Glass.Rect(row, name, new Vector2(ControlLeft, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -22f), new Vector2(-Pad, 22f));
            var h = area.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 9;
            h.childAlignment = TextAnchor.MiddleRight;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            return area;
        }

        /// <summary>
        /// Interrupteur : pilule (or quand c'est activé, pastille encre ; estompée sinon), état écrit à sa gauche en or
        /// ou estompé. Gauche = désactivé, droite = activé, A / Entrée ou un clic sur la ligne bascule.
        /// </summary>
        Selectable AddToggle(RectTransform parent, string fr, string en, Func<bool> get, Action<bool> set,
            (string fr, string en)? onName = null, (string fr, string en)? offName = null)
        {
            var on = onName ?? ("Activé", "On");
            var off = offName ?? ("Désactivé", "Off");
            var selector = SelectorRow(parent, fr, en, out var row);
            const float W = 75f, H = 39f, Knob = 30f, Inset = 4.5f;
            var offTrack = Glass.A(Color.white, 0.18f);
            var track = Glass.Make(row, "Switch", H / 2f, offTrack, 0f);
            Put(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(-Pad, 0f), new Vector2(W, H));
            var knob = Glass.Make(track.transform, "Knob", Knob / 2f, Glass.White, 0f);
            var krt = knob.rectTransform;
            Put(krt, new Vector2(0f, 0.5f), new Vector2(Inset, 0f), new Vector2(Knob, Knob));
            var state = Glass.Label(row, "", 22, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleRight);
            Stretch(state.rectTransform, ControlLeft, 1f, 0f, Pad + W + 24f);

            float t = get() ? 1f : 0f;
            void Place()
            {
                krt.anchoredPosition = new Vector2(Inset + t * (W - Knob - 2f * Inset), 0f);
                track.color = Color.Lerp(offTrack, Glass.Gold, t);
                knob.color = Color.Lerp(Glass.White, Glass.Ink, t);
            }
            void Set(bool v)
            {
                if (v == get()) return;
                set(v);
                Changed();
                RefreshAll();
            }
            selector.Move = d => Set(d > 0);
            selector.Submit = selector.Click = () => Set(!get());
            refreshers.Add(() =>
            {
                bool v = get();
                state.text = v ? Loc.T(on.fr, on.en) : Loc.T(off.fr, off.en);
                state.color = v ? Glass.Gold : Glass.Dim;
                if (!IsOpen) { t = v ? 1f : 0f; Place(); }
            });
            animators.Add(dt =>
            {
                t = Mathf.MoveTowards(t, get() ? 1f : 0f, dt * 7f);
                Place();
            });
            Place();
            return selector;
        }

        /// <summary>
        /// Choix segmentés : une pastille par valeur, la valeur prise en or plein. Gauche/droite passe à la voisine (sans
        /// boucler), A / Entrée à la suivante (en boucle), un clic choisit. <paramref name="locked"/> rend un texte quand
        /// le réglage ne s'applique pas (les pastilles laissent place à ce texte).
        /// </summary>
        Selectable AddChoice(RectTransform parent, string fr, string en, int count, Func<int> get, Action<int> set,
            Func<int, string> name, Func<string> locked = null)
        {
            var selector = SelectorRow(parent, fr, en, out var row);
            var area = ChipArea(row, "Choices");
            var note = Glass.Label(row, "", 22, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleRight);
            Stretch(note.rectTransform, ControlLeft, 1f, 0f, Pad);

            void Pick(int i)
            {
                if (locked?.Invoke() != null) return;
                i = Mathf.Clamp(i, 0, count - 1);
                if (i == get()) return;
                set(i);
                Changed();
                RefreshAll();
            }
            var chips = new (GlassRect plate, Text text, OptionHit hit)[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                chips[i] = Chip(area, "Choice", 22);
                chips[i].hit.Click = () => Pick(index);
            }
            selector.Move = d => Pick(get() + d);
            selector.Submit = () => Pick((get() + 1) % count);
            refreshers.Add(() =>
            {
                for (int i = 0; i < count; i++) chips[i].text.text = name(i);
                string lockText = locked?.Invoke();
                area.gameObject.SetActive(lockText == null);
                note.text = lockText ?? "";
            });
            animators.Add(_ =>
            {
                int current = get();
                for (int i = 0; i < count; i++)
                {
                    bool active = i == current, hover = chips[i].hit.Hover;
                    Look(chips[i].plate, active ? Glass.Gold : hover ? Glass.Pill : Color.clear, active ? Color.clear : hover ? ChipHover : ChipEdge, 0f);
                    Ink(chips[i].text, active ? Glass.Ink : Glass.White, active ? Glass.Weight.Semibold : Glass.Weight.Regular);
                }
            });
            return selector;
        }

        /// <summary>
        /// Cases à cocher en pastilles sur une ligne (plusieurs actives à la fois) : or plein = activée. Gauche/droite
        /// déplacent le curseur de pastille (plus claire), A / Entrée ou un clic sur une pastille la bascule.
        /// </summary>
        Selectable AddFlags(RectTransform parent, string fr, string en, (string fr, string en)[] names, Func<int, bool> get, Action<int, bool> set)
        {
            var selector = SelectorRow(parent, fr, en, out var row);
            var area = ChipArea(row, "Flags");
            int count = names.Length, cursor = 0;

            void Flip(int i)
            {
                cursor = i;
                set(i, !get(i));
                Changed();
                RefreshAll();
            }
            var chips = new (GlassRect plate, Text text, OptionHit hit)[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                chips[i] = Chip(area, "Flag", 22);
                chips[i].hit.Click = () => Flip(index);
            }
            selector.Move = d => cursor = Mathf.Clamp(cursor + d, 0, count - 1);
            selector.Submit = () => Flip(cursor);
            refreshers.Add(() =>
            {
                for (int i = 0; i < count; i++) chips[i].text.text = Loc.T(names[i].fr, names[i].en);
            });
            animators.Add(_ =>
            {
                bool focused = Focused(selector.gameObject);
                for (int i = 0; i < count; i++)
                {
                    bool on = get(i), lit = chips[i].hit.Hover || (focused && i == cursor);
                    if (on) Look(chips[i].plate, lit ? Color.Lerp(Glass.Gold, Color.white, 0.3f) : Glass.Gold, Color.clear, 0f);
                    else Look(chips[i].plate, lit ? Glass.Pill : Color.clear, lit ? ChipHover : ChipEdge, 0f);
                    Ink(chips[i].text, on ? Glass.Ink : Glass.White, on ? Glass.Weight.Semibold : Glass.Weight.Regular);
                }
            });
            return selector;
        }

        /// <summary>
        /// Carrousel « ‹ valeur › » pour les longues listes : flèches cliquables (estompées en bout de liste, or sur la
        /// ligne choisie), valeur au centre et repères de position dessous. Gauche/droite sans boucler, A / Entrée en boucle.
        /// </summary>
        Selectable AddCarousel(RectTransform parent, string fr, string en, Func<int> count, Func<int> get, Action<int> set, Func<int, string> name)
        {
            var selector = SelectorRow(parent, fr, en, out var row);
            var area = Glass.Rect(row, "Carousel", new Vector2(ControlLeft, 0f), Vector2.one, new Vector2(0f, 6f), new Vector2(-Pad, -6f));
            const float Arrow = 48f;
            var prev = Arrowhead(area, "Prev", "‹", 0f);
            var next = Arrowhead(area, "Next", "›", 1f);
            var value = Glass.Label(area, "", 25, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Fill(value.rectTransform, new Vector2(Arrow, 10f), new Vector2(-Arrow, 0f));
            var pipRow = Glass.Rect(area, "Pips", new Vector2(0.3f, 0f), new Vector2(0.7f, 0f), new Vector2(0f, 3f), new Vector2(0f, 7f));
            var pips = new List<GlassRect>();

            void Pick(int i)
            {
                int n = count();
                if (n <= 0) return;
                i = Mathf.Clamp(i, 0, n - 1);
                if (i == get()) return;
                set(i);
                Changed();
                RefreshAll();
            }
            prev.hit.Click = () => Pick(get() - 1);
            next.hit.Click = () => Pick(get() + 1);
            selector.Move = d => Pick(get() + d);
            selector.Submit = () => { int n = count(); if (n > 0) Pick((get() + 1) % n); };
            refreshers.Add(() =>
            {
                int n = count();
                value.text = name(get());
                // Repères : un par valeur, au-delà de 24 valeurs une seule barre de position
                int wanted = n > 24 ? 1 : n;
                while (pips.Count < wanted) pips.Add(Glass.Make(pipRow, "Pip", 2f, Glass.Faint, 0f));
                for (int i = 0; i < pips.Count; i++) pips[i].gameObject.SetActive(i < wanted && n > 1);
                for (int i = 0; i < wanted; i++)
                {
                    var rt = pips[i].rectTransform;
                    float a = wanted == 1 ? Mathf.Max(0f, get() / (float)n) : i / (float)n;
                    float b = wanted == 1 ? Mathf.Min(1f, (get() + 1f) / n) : (i + 1f) / n;
                    rt.anchorMin = new Vector2(a, 0f);
                    rt.anchorMax = new Vector2(b, 1f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = new Vector2(wanted == 1 ? 0f : -3f, 0f);
                }
            });
            animators.Add(_ =>
            {
                int n = count(), i = get();
                bool focused = Focused(selector.gameObject);
                bool canPrev = i > 0, canNext = i < n - 1;
                Look(prev.plate, canPrev && prev.hit.Hover ? Glass.Pill : Color.clear, Color.clear, 0f);
                Look(next.plate, canNext && next.hit.Hover ? Glass.Pill : Color.clear, Color.clear, 0f);
                prev.text.color = canPrev ? (focused ? Glass.Gold : Glass.White) : Glass.Faint;
                next.text.color = canNext ? (focused ? Glass.Gold : Glass.White) : Glass.Faint;
                Ink(value, focused ? Glass.Gold : Glass.White, focused ? Glass.Weight.Semibold : Glass.Weight.Regular);
                int wanted = n > 24 ? 1 : n;
                for (int p = 0; p < wanted && p < pips.Count; p++) pips[p].color = wanted == 1 || p == i ? Glass.Gold : Glass.Faint;
            });
            return selector;
        }

        /// <summary>Flèche « ‹ » ou « › » cliquable au bord d'un carrousel (x = 0 : à gauche, 1 : à droite).</summary>
        static (GlassRect plate, Text text, OptionHit hit) Arrowhead(RectTransform area, string name, string glyph, float x)
        {
            var plate = Glass.Make(area, name, 24f, Color.clear, 0f);
            plate.raycastTarget = true;
            Put(plate.rectTransform, new Vector2(x, 0.5f), Vector2.zero, new Vector2(48f, 48f));
            var text = Glass.Label(plate.transform, glyph, 36, Glass.Weight.Light, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(0f, 3f);
            return (plate, text, plate.gameObject.AddComponent<OptionHit>());
        }

        /// <summary>
        /// Curseur (valeurs continues) : trait fin, remplissage blanc et pastille ronde, valeur à droite ; sur la ligne
        /// choisie, remplissage et pastille en or (avec halo) et flèches « ‹ › » or de part et d'autre.
        /// </summary>
        Slider AddSlider(RectTransform parent, string fr, string en, float min, float max, int steps, Func<float> get, Action<float> set, Func<float, string> format)
        {
            var row = OptionRow(parent, fr, en, out var plate, out var line, out var label);
            const float ValueW = 96f, ArrowW = 36f, Gap = 6f, AreaH = 34f;
            var value = Glass.Label(row, "", 25, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleRight);
            Stretch(value.rectTransform, 1f, 1f, -(Pad + ValueW), Pad);

            // Rail fin estompé, remplissage, pastille ; une zone transparente plus haute capte la souris
            float right = Pad + ValueW + Gap + ArrowW;
            var sliderRt = Glass.Rect(row, "Slider", new Vector2(ControlLeft, 0.5f), new Vector2(1f, 0.5f), new Vector2(ArrowW, -AreaH / 2f), new Vector2(-right, AreaH / 2f));
            var slider = sliderRt.gameObject.AddComponent<Slider>();
            var hit = Glass.Make(sliderRt, "Hit", 0f, Color.clear, 0f);
            hit.raycastTarget = true;
            Glass.Stretch(hit.rectTransform);
            var track = Glass.Make(sliderRt, "Track", 1.5f, Glass.Track, 0f);
            ThinLine(track.rectTransform);
            var fillArea = Glass.Rect(sliderRt, "FillArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ThinLine(fillArea);
            var fill = Glass.Make(fillArea, "Fill", 1.5f, Glass.White, 0f);
            Fill(fill.rectTransform, Vector2.zero, Vector2.zero);
            var handleArea = Glass.Rect(sliderRt, "HandleArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handle = Glass.Make(handleArea, "Handle", 999f, Glass.White, 0f);
            handle.raycastTarget = true;
            handle.haloColor = Glass.A(Glass.Gold, 0.25f);
            var handleRt = handle.rectTransform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = new Vector2(0f, 1f);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            slider.transition = Selectable.Transition.None;
            slider.wholeNumbers = true;
            slider.minValue = 0;
            slider.maxValue = steps;

            // Flèches or de part et d'autre du rail, visibles sur la ligne choisie
            var lessRt = Glass.Rect(row, "Less", new Vector2(ControlLeft, 0f), new Vector2(ControlLeft, 1f), Vector2.zero, new Vector2(ArrowW, 0f));
            var moreRt = Glass.Rect(row, "More", new Vector2(1f, 0f), Vector2.one, new Vector2(-right, 0f), new Vector2(-right + ArrowW, 0f));
            var less = SliderArrow(lessRt, "‹");
            var more = SliderArrow(moreRt, "›");

            bool updating = false;
            Action refresh = () =>
            {
                updating = true;
                slider.value = Mathf.Round(Mathf.InverseLerp(min, max, get()) * steps);
                value.text = format(get());
                updating = false;
            };
            slider.onValueChanged.AddListener(x =>
            {
                if (updating) return;
                set(Mathf.Lerp(min, max, x / steps));
                value.text = format(get());
                Changed();
                RefreshAll();
            });
            refreshers.Add(refresh);
            float knob = 24f;
            animators.Add(dt =>
            {
                bool focused = Focused(slider.gameObject);
                knob = Mathf.MoveTowards(knob, focused ? 33f : 24f, dt * 90f);
                handleRt.sizeDelta = new Vector2(knob, knob - AreaH);
                handleRt.anchoredPosition = Vector2.zero;
                handle.color = focused ? Glass.Gold : Glass.White;
                if (Mathf.Abs(handle.halo - (focused ? 9f : 0f)) > 0.01f) { handle.halo = focused ? 9f : 0f; handle.SetVerticesDirty(); }
                fill.color = focused ? Glass.Gold : Glass.White;
                Ink(value, focused ? Glass.Gold : Glass.White, focused ? Glass.Weight.Semibold : Glass.Weight.Regular);
                if (less.activeSelf != focused) { less.SetActive(focused); more.SetActive(focused); }
            });
            rows.Add((slider.gameObject, plate, line, label));
            return slider;
        }

        /// <summary>
        /// Flèche or d'un curseur, simple repère (pas de clic : la ligne n'est pas sélectionnable, un clic ici ôterait
        /// la sélection du curseur et la flèche disparaîtrait avant d'être relâchée).
        /// </summary>
        static GameObject SliderArrow(RectTransform rt, string glyph)
        {
            var t = Glass.Label(rt, glyph, 33, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleCenter);
            Glass.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(0f, 3f);
            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        /// <summary>Trait de 3 px centré dans la hauteur du parent, sur toute sa largeur.</summary>
        static void ThinLine(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(0f, -1.5f);
            rt.offsetMax = new Vector2(0f, 1.5f);
        }

        /// <summary>
        /// Ligne d'action : nom à gauche, pilule cernée à droite (or plein quand la ligne est choisie) ; toute la ligne est
        /// le bouton.
        /// </summary>
        (Button button, Text label) AddAction(RectTransform parent, string fr, string en, string pillFr, string pillEn, UnityEngine.Events.UnityAction onClick)
        {
            var row = OptionRow(parent, fr, en, out var plate, out var line, out var label);
            Stretch(label.rectTransform, 0f, 0.72f, Pad, 12f);
            var hitPlate = Glass.Make(row, "Hit", 0f, Color.clear, 0f);
            hitPlate.raycastTarget = true;
            hitPlate.transform.SetAsFirstSibling();
            Glass.Stretch(hitPlate.rectTransform);
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = hitPlate;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
            var hover = row.gameObject.AddComponent<OptionHit>();

            var pill = Glass.Make(row, "Pill", 999f, Color.clear, 1.5f);
            pill.borderColor = ChipEdge;
            var prt = pill.rectTransform;
            Put(prt, new Vector2(1f, 0.5f), new Vector2(-Pad, 0f), new Vector2(240f, 45f));
            var h = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(27, 27, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = false;
            pill.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = Loc.Bind(Glass.Label(pill.transform, "", 22, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter), pillFr, pillEn);
            animators.Add(_ =>
            {
                bool sel = Focused(row.gameObject);
                Look(pill, sel ? Glass.Gold : hover.Hover ? Glass.Pill : Color.clear, sel ? Color.clear : hover.Hover ? ChipHover : ChipEdge, 0f);
                Ink(text, sel ? Glass.Ink : Glass.White, sel ? Glass.Weight.Semibold : Glass.Weight.Regular);
            });
            rows.Add((row.gameObject, plate, line, label));
            return (button, label);
        }

        /// <summary>Hauteur fixe : une rangée à mise en page propre ne doit pas absorber la place libre de la plaque.</summary>
        static void Fixed(RectTransform row, float height)
        {
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        /// <summary>Chaîne haut/bas d'une plaque, en boucle ; gauche/droite laissés libres (réglage des curseurs).</summary>
        static void Column(params Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
                Nav(items[i], items[(i + items.Length - 1) % items.Length], items[(i + 1) % items.Length], null, null);
        }

        static void Nav(Selectable s, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            s.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right,
            };
        }

        // ------------------------------------------------------------------ helpers

        static void Fill(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        static void Stretch(RectTransform rt, float xMin, float xMax, float padLeft, float padRight)
        {
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = new Vector2(padLeft, 0f);
            rt.offsetMax = new Vector2(-padRight, 0f);
        }

        /// <summary>Rectangle posé : ancre et pivot au même point, position et taille.</summary>
        static void Put(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Change le fond, le liseré et le halo d'un rectangle de verre, sans reconstruire le maillage s'il n'a pas changé.</summary>
        static void Look(GlassRect g, Color fill, Color edge, float halo)
        {
            bool dirty = g.borderColor != edge || !Mathf.Approximately(g.halo, halo);
            g.color = fill;
            if (!dirty) return;
            g.borderColor = edge;
            g.halo = halo;
            g.SetVerticesDirty();
        }

        /// <summary>Couleur et graisse d'un texte (la police ne change que si besoin : la mise en page suit sa largeur).</summary>
        static void Ink(Text t, Color color, Glass.Weight weight)
        {
            t.color = color;
            var f = Glass.FontOf(weight);
            if (t.font != f) t.font = f;
        }

        /// <summary>Bouton pilule de verre ; padding non nul : la largeur suit le libellé (rangées à mise en page).</summary>
        static (Button button, GlassRect plate, Text text) PillButton(Transform parent, string name, int size, Glass.Weight weight, RectOffset padding,
            TextAnchor align, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button), typeof(OptionHit));
            go.transform.SetParent(parent, false);
            var plate = go.GetComponent<GlassRect>();
            plate.radius = 999f;
            plate.color = Glass.Pill;
            plate.border = 1.5f;
            plate.borderColor = Glass.Edge;
            plate.raycastTarget = true;
            var b = go.GetComponent<Button>();
            b.targetGraphic = plate;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
            var text = Glass.Label(go.transform, "", size, weight, Glass.White, align);
            if (padding != null)
            {
                var h = go.AddComponent<HorizontalLayoutGroup>();
                h.padding = padding;
                h.childAlignment = align;
                h.childControlWidth = true; h.childControlHeight = true;
                h.childForceExpandWidth = true; h.childForceExpandHeight = false;
            }
            else Glass.Stretch(text.rectTransform);
            return (b, plate, text);
        }

        /// <summary>Pilule du bas : or plein et encre une fois choisie, verre plus clair au survol.</summary>
        void PillLook(Button b, GlassRect plate, Text text)
        {
            var hit = b.GetComponent<OptionHit>();
            animators.Add(_ =>
            {
                bool sel = Focused(b.gameObject);
                Look(plate, sel ? Glass.Gold : hit.Hover ? Glass.A(Color.white, 0.18f) : Glass.Pill, sel ? Color.clear : Glass.Edge, 0f);
                Ink(text, sel ? Glass.Ink : Glass.White, sel ? Glass.Weight.Semibold : Glass.Weight.Regular);
            });
        }
    }
}
