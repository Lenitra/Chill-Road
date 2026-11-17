using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Menu Options (construit en code), ouvert depuis l'accueil ou la pause, dans le langage visuel du HUD et de la
    /// pause : voile sombre, plaque inclinée anthracite (<see cref="HudShape"/>), police du HUD, titre or, rubriques
    /// cyan, curseurs or.
    /// Rangé par onglets : Jeu (langue, trafic, pluie, secousses, commandes tactiles si l'écran est tactile, retour au
    /// départ), Graphismes (écran : mode d'affichage, résolution et VSync sur ordinateur, limite d'images ; rendu :
    /// qualité, distance des détails, flou de mouvement), Audio (volume général puis une voie par famille de sons, titre
    /// des morceaux, son en arrière-plan), Touches (deux touches et deux boutons de manette par commande,
    /// <see cref="GameInput.Bind"/>). Tout s'applique tout de suite (mode d'affichage et résolution : une fois le curseur lâché).
    /// Manette : LB/RB (clavier : Tab, Maj+Tab) pour changer d'onglet, haut/bas pour choisir (la ligne choisie
    /// s'éclaire), gauche/droite pour régler.
    /// </summary>
    public class OptionsMenu : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color RowOn = new Color(0.14f, 0.16f, 0.2f, 0.95f);

        public static bool IsOpen { get; private set; }

        GameManager game;
        Font font;
        GameObject root;
        Action onClose;
        Selectable first;
        Text applyText, seedText;
        string roadAtOpen;
        int openedFrame;
        readonly List<Action> refreshers = new List<Action>();
        readonly List<(GameObject target, Graphic plate, Graphic bar)> rows = new List<(GameObject, Graphic, Graphic)>();

        // Onglets : pages (une seule visible), lignes réglables de chaque page, boutons d'onglet
        static int lastTab;
        int tab;
        RectTransform[] pages;
        Selectable[][] pageItems;
        (Button button, Text text, Graphic mark)[] tabs;
        Button defaultsButton, backButton;

        // Mode d'affichage et définition : appliqués à cet instant (0 = rien en attente), pour ne pas changer de mode
        // d'écran à chaque cran quand on fait glisser le curseur
        const float DisplayDelay = 0.6f;
        float displayAt;

        // Onglet Touches : grille commandes × emplacements (clavier 1, clavier 2, manette 1, manette 2) ; un clic sur un
        // emplacement attend la touche ou le bouton suivant (Échap / Start : annuler, Suppr / Retour arrière : vider).
        // Pendant l'attente, la navigation des menus est coupée ; elle revient deux images après, pour que la touche
        // choisie (A, Entrée, B…) n'agisse pas aussi sur le menu.
        const int GameTab = 0, GraphicsTab = 1, AudioTab = 2, KeysTab = 3;
        static readonly (float min, float max)[] SlotColumns = { (0.37f, 0.515f), (0.525f, 0.67f), (0.705f, 0.85f), (0.86f, 1f) };
        Button[,] keyGrid;
        bool capturing, navBlocked;
        GameInput.Bind captureBind;
        int captureSlot, captureFrame, captureEndFrame = -10;
        bool capturePad;

        public void Init(GameManager gm)
        {
            game = gm;
            font = HudArt.Font;
            var cgo = new GameObject("Options", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = cgo;

            // Voile sombre de la pause, puis une plaque : titre, onglets, page de l'onglet, boutons du bas
            var shade = Rect(cgo.transform, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.62f);
            var panel = Panel(shade, "Options", new Vector2(-720, -410), new Vector2(720, 430));

            var title = Label(panel, "OPTIONS", 64, TextAnchor.MiddleLeft, Accent, FontStyle.BoldAndItalic);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 84;
            var tabRow = Rect(panel, "Tabs", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(tabRow, 58);
            var th = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 14;
            th.childControlWidth = true; th.childControlHeight = true;
            th.childForceExpandWidth = false; th.childForceExpandHeight = true;
            var tabNames = new[] { ("JEU", "GAMEPLAY"), ("GRAPHISMES", "GRAPHICS"), ("AUDIO", "AUDIO"), ("TOUCHES", "CONTROLS") };
            TabGlyph(tabRow, "LB", TextAnchor.MiddleRight);
            tabs = new (Button, Text, Graphic)[tabNames.Length];
            pages = new RectTransform[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                tabs[i] = AddTab(tabRow, tabNames[i].Item1, tabNames[i].Item2, i);
                pages[i] = Page(panel, tabNames[i].Item2);
            }
            TabGlyph(tabRow, "RB", TextAnchor.MiddleLeft);

            // Jeu : langue, monde (trafic, pluie), caméra, commandes tactiles, puis le retour au départ
            var play = pages[GameTab];
            Section(play, "GÉNÉRAL", "GENERAL", "", "");
            var language = AddSlider(play, "Langue", "Language", 0f, 1f, 1, () => Loc.French ? 1f : 0f,
                v => Loc.Set(v > 0.5f ? Language.French : Language.English), v => v > 0.5f ? "Français" : "English");
            var traffic = AddSlider(play, "Densité du trafic", "Traffic density", 0.2f, 2f, 18, () => GameSettings.TrafficDensity, v => GameSettings.TrafficDensity = v, Percent);
            var rain = AddSlider(play, "Probabilité de pluie", "Rain probability", 0f, 1f, 20, () => GameSettings.RainChance, v => GameSettings.RainChance = v, v => v < 0.01f ? Loc.T("Jamais", "Never") : v > 0.99f ? Loc.T("Toujours", "Always") : Percent(v));
            var shake = AddToggle(play, "Secousses de caméra", "Camera shake", () => GameSettings.CameraShake, v => GameSettings.CameraShake = v);
            // Commandes tactiles (téléphone, tablette) : façon de diriger, ou désactivées. Absentes sur un PC sans
            // écran tactile, où elles ne s'affichent jamais.
            Slider touch = null;
            if (TouchScreen)
            {
                Section(play, "COMMANDES TACTILES", "TOUCH CONTROLS", "", "");
                touch = AddSlider(play, "Commandes tactiles", "Touch controls", 0f, 3f, 3, () => GameSettings.TouchSteer, v => GameSettings.TouchSteer = Mathf.RoundToInt(v),
                    v => v < 0.5f ? Loc.T("Désactivées", "Off") : v < 1.5f ? Loc.T("Glisser", "Slide") : v < 2.5f ? Loc.T("Boutons ‹ ›", "‹ › buttons") : Loc.T("Inclinaison", "Tilt"));
            }
            Section(play, "CARTE", "MAP", "", "");
            var gap = Rect(play, "Gap", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(gap, 6);
            var apply = AddButton(play, ApplyRoad);
            apply.button.GetComponent<LayoutElement>().preferredHeight = 54;
            applyText = apply.text;
            Loc.Bind(applyText, "Revenir au départ (route de la vallée)", "Back to the start (valley road)");

            // Graphismes : l'écran (mode d'affichage, définition et synchro verticale sur ordinateur seulement ; mode et
            // définition appliqués une fois le curseur lâché, voir Update), puis le rendu
            var gfx = pages[GraphicsTab];
            Section(gfx, "ÉCRAN", "DISPLAY", "", "");
            Slider window = null, resolution = null, vsync = null;
            if (GraphicsQuality.HasDisplayOptions)
            {
                window = AddSlider(gfx, "Mode d'affichage", "Display mode", 0f, GraphicsQuality.WindowModes.Length - 1, GraphicsQuality.WindowModes.Length - 1,
                    () => GameSettings.WindowMode, v =>
                    {
                        int m = Mathf.RoundToInt(v);
                        if (m == GameSettings.WindowMode) return;
                        GameSettings.WindowMode = m;
                        displayAt = Time.unscaledTime + DisplayDelay;
                    },
                    v => GraphicsQuality.WindowModeName(Mathf.RoundToInt(v)));
                int resSteps = Mathf.Max(1, GraphicsQuality.Resolutions.Length - 1);
                resolution = AddSlider(gfx, "Résolution", "Resolution", 0f, resSteps, resSteps,
                    () => GraphicsQuality.ResolutionIndex, v =>
                    {
                        int i = Mathf.RoundToInt(v);
                        if (i == GraphicsQuality.ResolutionIndex) return;
                        GraphicsQuality.SetResolutionIndex(i);
                        displayAt = Time.unscaledTime + DisplayDelay;
                    },
                    v => GraphicsQuality.ResolutionName(Mathf.RoundToInt(v)));
                vsync = AddToggle(gfx, "Synchro verticale (VSync)", "Vertical sync (VSync)", () => GameSettings.VSync, v => GameSettings.VSync = v);
            }
            var fps = AddSlider(gfx, "Limite d'images", "Frame rate limit", 0f, GraphicsQuality.FpsCaps.Length - 1, GraphicsQuality.FpsCaps.Length - 1,
                () => GameSettings.FpsCap, v => GameSettings.FpsCap = Mathf.RoundToInt(v),
                v => GameSettings.VSync && GraphicsQuality.HasDisplayOptions ? Loc.T("Suit l'écran (VSync)", "Follows display (VSync)") : GraphicsQuality.FpsName(Mathf.RoundToInt(v)));

            // Rendu : changer la qualité remet la distance des détails au niveau conseillé
            Section(gfx, "RENDU", "RENDERING", "", "");
            var quality = AddSlider(gfx, "Qualité graphique", "Graphics quality", 0f, 3f, 3, () => GameSettings.Quality, v =>
                {
                    // (l'éditeur rappelle les curseurs sans changement de valeur : ne pas écraser la distance choisie)
                    int q = Mathf.RoundToInt(v);
                    if (q == GameSettings.Quality) return;
                    GameSettings.Quality = q;
                    GameSettings.DetailDistance = GraphicsQuality.DefaultDetail(q);
                },
                v => GraphicsQuality.Name(Mathf.RoundToInt(v)));
            var detail = AddSlider(gfx, "Distance des détails", "Detail distance", 0f, GraphicsQuality.DetailLevels - 1, GraphicsQuality.DetailLevels - 1,
                () => GameSettings.DetailDistance, v => GameSettings.DetailDistance = Mathf.RoundToInt(v), v => GraphicsQuality.DetailName(Mathf.RoundToInt(v)));
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
            var trackNames = AddSlider(audio, "Titre des morceaux", "Track titles", 0f, 1f, 1, () => GameSettings.TrackNames ? 1f : 0f, v => GameSettings.TrackNames = v > 0.5f,
                v => v > 0.5f ? Loc.T("Affiché", "Shown") : Loc.T("Masqué", "Hidden"));
            Slider background = null;
            if (GraphicsQuality.HasDisplayOptions)
                background = AddToggle(audio, "Son en arrière-plan", "Sound in background", () => GameSettings.BackgroundAudio, v => GameSettings.BackgroundAudio = v);

            // Touches
            BuildKeys(pages[KeysTab]);

            var bottom = ButtonRow(panel, 62);
            var defaults = AddButton(bottom, () => { GameSettings.ResetAll(); GameInput.ResetBindings(); displayAt = Time.unscaledTime + 0.01f; Changed(); RefreshAll(); });
            Loc.Bind(defaults.text, "Par défaut", "Defaults");
            var back = AddButton(bottom, Close);
            Loc.Bind(back.text, "Retour", "Back");
            defaultsButton = defaults.button;
            backButton = back.button;

            // Aide manette / clavier sous la plaque
            var hint = Label(shade, "", 21, TextAnchor.MiddleCenter, Dim, FontStyle.Normal);
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.anchoredPosition = new Vector2(0f, -452f);
            hintRt.sizeDelta = new Vector2(1600f, 40f);
            Loc.Bind(hint, "LB RB / Tab  catégorie   ·   ↑ ↓  choisir   ·   ← →  régler   ·   B / Échap  retour",
                           "LB RB / Tab  category   ·   ↑ ↓  select   ·   ← →  adjust   ·   B / Esc  back");

            // Navigation explicite par page (voir ShowTab) : gauche/droite restent aux curseurs (réglage)
            var gameItems = new List<Selectable> { language, traffic, rain, shake };
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

        // ------------------------------------------------------------------ ouverture / fermeture

        /// <summary>Ouvre le menu ; <paramref name="closed"/> est appelé à la fermeture (retour à l'accueil ou à la pause).</summary>
        public void Open(Action closed)
        {
            onClose = closed;
            roadAtOpen = GameSettings.RoadSignature;
            ShowTab(lastTab);
            RefreshAll();
            root.SetActive(true);
            IsOpen = true;
            openedFrame = Time.frameCount;
            EventSystem.current?.SetSelectedGameObject(first.gameObject);
            HighlightRows();
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

        void Update()
        {
            if (displayAt > 0f && Time.unscaledTime >= displayAt) FlushDisplay();
            if (!IsOpen || Time.frameCount == openedFrame) return;
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
                tabs[i].text.color = on ? Accent : new Color(White.r, White.g, White.b, 0.7f);
                var colors = tabs[i].button.colors;
                colors.normalColor = on ? new Color(0.24f, 0.27f, 0.33f, 1f) : new Color(0.14f, 0.16f, 0.2f, 0.95f);
                tabs[i].button.colors = colors;
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

        /// <summary>Ligne du curseur choisi (manette ou clic) : plaque plus claire et trait or à gauche.</summary>
        void HighlightRows()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            // (une ligne de l'onglet Touches a quatre cibles pour la même plaque : éteindre tout, puis allumer)
            foreach (var r in rows) r.plate.enabled = r.bar.enabled = false;
            foreach (var r in rows)
                if (r.target == selected) r.plate.enabled = r.bar.enabled = true;
        }

        // ------------------------------------------------------------------ touches

        /// <summary>Onglet Touches : en-tête (commande, clavier, manette) puis une ligne par commande, quatre emplacements.</summary>
        void BuildKeys(RectTransform page)
        {
            page.GetComponent<VerticalLayoutGroup>().spacing = 2;
            var head = Rect(page, "Head", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(head, 40);
            HeadLabel(head, "COMMANDE", "ACTION", 0f, 0.35f, TextAnchor.LowerLeft);
            HeadLabel(head, "CLAVIER", "KEYBOARD", SlotColumns[0].min, SlotColumns[1].max, TextAnchor.LowerCenter);
            HeadLabel(head, "MANETTE", "GAMEPAD", SlotColumns[2].min, SlotColumns[3].max, TextAnchor.LowerCenter);
            var line = Shape(head, "Line", new Color(Info.r, Info.g, Info.b, 0.4f)).Set(0f, 0f, 260f);
            var lrt = line.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.offsetMin = new Vector2(0f, -3f);
            lrt.offsetMax = new Vector2(0f, -1f);

            keyGrid = new Button[GameInput.BindCount, 4];
            for (int b = 0; b < GameInput.BindCount; b++)
            {
                var bind = (GameInput.Bind)b;
                var row = Rect(page, bind.ToString(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Fixed(row, 32);
                var plate = Shape(row, "Selected", RowOn).Set(12f, 0f, 0f);
                Fill(plate.rectTransform, new Vector2(-18, 0), new Vector2(14, 0));
                var bar = Shape(row, "Mark", Accent).Set(6f, 0f, 0f);
                var barRt = bar.rectTransform;
                barRt.anchorMin = Vector2.zero;
                barRt.anchorMax = new Vector2(0f, 1f);
                barRt.offsetMin = new Vector2(-18, 5);
                barRt.offsetMax = new Vector2(-13, -5);
                plate.enabled = bar.enabled = false;
                var name = Label(row, "", 22, TextAnchor.MiddleLeft, White, FontStyle.Normal);
                Stretch(name.rectTransform, 0f, 0.36f, 6f, 0f);
                refreshers.Add(() => name.text = GameInput.BindName(bind));
                for (int c = 0; c < 4; c++)
                {
                    var slot = KeySlot(row, bind, c);
                    keyGrid[b, c] = slot;
                    rows.Add((slot.gameObject, plate, bar));
                }
            }
        }

        void HeadLabel(RectTransform parent, string fr, string en, float xMin, float xMax, TextAnchor align)
        {
            var t = Loc.Bind(Label(parent, "", 21, align, Info, FontStyle.BoldAndItalic), fr, en);
            Stretch(t.rectTransform, xMin, xMax, align == TextAnchor.LowerLeft ? 6f : 0f, 0f);
            t.rectTransform.offsetMin = new Vector2(t.rectTransform.offsetMin.x, 6f);
        }

        /// <summary>Emplacement : petit bouton incliné portant la touche ou le bouton choisi (« … » pendant l'attente).</summary>
        Button KeySlot(RectTransform row, GameInput.Bind bind, int column)
        {
            bool pad = column >= 2;
            int slot = column % 2;
            var (button, text) = AddButton(row, () => StartCapture(bind, slot, pad));
            button.GetComponent<HudShape>().Set(10f, 0f, 0f);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(SlotColumns[column].min, 0f);
            rt.anchorMax = new Vector2(SlotColumns[column].max, 1f);
            rt.offsetMin = new Vector2(0f, 2f);
            rt.offsetMax = new Vector2(0f, -2f);
            text.fontSize = 20;
            text.fontStyle = FontStyle.Bold;
            refreshers.Add(() =>
            {
                bool waiting = capturing && captureBind == bind && captureSlot == slot && capturePad == pad;
                text.text = waiting ? "…" : pad ? GameInput.PadName(GameInput.GetPad(bind, slot)) : GameInput.KeyName(GameInput.GetKey(bind, slot));
                text.color = waiting ? Accent : White;
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

        // ------------------------------------------------------------------ lignes

        /// <summary>Plaque inclinée anthracite (comme la pause) portant une colonne de lignes.</summary>
        static RectTransform Panel(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var panel = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), min, max);
            var plate = Shape(panel, "Plate", new Color(0.03f, 0.04f, 0.06f, 0.92f)).Set(24f, 0f, 0f);
            plate.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fill(plate.rectTransform, Vector2.zero, Vector2.zero);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(58, 52, 30, 34);
            layout.spacing = 8;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            return panel;
        }

        /// <summary>Page d'un onglet : colonne de lignes qui prend la place libre de la plaque.</summary>
        static RectTransform Page(RectTransform parent, string name)
        {
            var page = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            page.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            return page;
        }

        /// <summary>Onglet : bouton incliné, libellé or et trait or dessous quand il est ouvert (voir ShowTab).</summary>
        (Button button, Text text, Graphic mark) AddTab(Transform parent, string fr, string en, int index)
        {
            var (button, text) = AddButton(parent, () => ShowTab(index));
            button.GetComponent<LayoutElement>().flexibleWidth = 1f;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            text.fontSize = 25;
            Loc.Bind(text, fr, en);
            var mark = Shape(button.transform, "Active", Accent).Set(4f, 0f, 0f);
            var rt = mark.rectTransform;
            rt.anchorMin = new Vector2(0.08f, 0f);
            rt.anchorMax = new Vector2(0.92f, 0f);
            rt.offsetMin = new Vector2(0f, 3f);
            rt.offsetMax = new Vector2(0f, 7f);
            return (button, text, mark);
        }

        /// <summary>Repère de bouton manette (LB, RB) au bord de la rangée d'onglets.</summary>
        void TabGlyph(Transform parent, string glyph, TextAnchor align)
        {
            var t = Label(parent, glyph, 21, align, Dim, FontStyle.Bold);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 44;
        }

        /// <summary>Rubrique : nom en cyan, précision estompée, filet cyan qui s'efface vers la droite.</summary>
        void Section(RectTransform parent, string fr, string en, string noteFr, string noteEn)
        {
            var row = Rect(parent, "Section", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, 42);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14;
            h.childAlignment = TextAnchor.LowerLeft;
            h.padding = new RectOffset(0, 0, 0, 6);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            if (fr.Length > 0) Loc.Bind(Label(row, "", 26, TextAnchor.LowerLeft, Info, FontStyle.BoldAndItalic), fr, en);
            if (noteFr.Length > 0)
            {
                string dot = fr.Length > 0 ? "·  " : "";
                Loc.Bind(Label(row, "", 20, TextAnchor.LowerLeft, Dim, FontStyle.Italic), dot + noteFr, dot + noteEn);
            }
            var line = Shape(row, "Line", new Color(Info.r, Info.g, Info.b, 0.4f)).Set(0f, 0f, 260f);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var lrt = line.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = new Vector2(0f, 2f);
        }

        /// <summary>Interrupteur : curseur à deux crans, Activé / Désactivé.</summary>
        Slider AddToggle(RectTransform parent, string fr, string en, Func<bool> get, Action<bool> set) =>
            AddSlider(parent, fr, en, 0f, 1f, 1, () => get() ? 1f : 0f, v => set(v > 0.5f), v => v > 0.5f ? Loc.T("Activé", "On") : Loc.T("Désactivé", "Off"));

        Slider AddSlider(RectTransform parent, string fr, string en, float min, float max, int steps, Func<float> get, Action<float> set, Func<float, string> format)
        {
            var row = Rect(parent, en, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
            // Ligne choisie : plaque inclinée plus claire et trait or à gauche (allumés par HighlightRows)
            var plate = Shape(row, "Selected", RowOn).Set(18f, 0f, 0f);
            Fill(plate.rectTransform, new Vector2(-18, 1), new Vector2(14, -1));
            var bar = Shape(row, "Mark", Accent).Set(8f, 0f, 0f);
            var barRt = bar.rectTransform;
            barRt.anchorMin = Vector2.zero;
            barRt.anchorMax = new Vector2(0f, 1f);
            barRt.offsetMin = new Vector2(-18, 8);
            barRt.offsetMax = new Vector2(-12, -8);
            plate.enabled = bar.enabled = false;

            var name = Loc.Bind(Label(row, "", 25, TextAnchor.MiddleLeft, White, FontStyle.Normal), fr, en);
            Stretch(name.rectTransform, 0f, 0.47f, 6f, 0f);
            var value = Label(row, "", 25, TextAnchor.MiddleRight, Accent, FontStyle.Bold);
            Stretch(value.rectTransform, 0.8f, 1f, 0f, 4f);

            // Curseur : rail blanc estompé, remplissage or, languette inclinée
            var sliderRt = Rect(row, "Slider", new Vector2(0.49f, 0.5f), new Vector2(0.78f, 0.5f), new Vector2(0, -7), new Vector2(0, 7));
            var slider = sliderRt.gameObject.AddComponent<Slider>();
            var track = Shape(sliderRt, "Track", new Color(1f, 1f, 1f, 0.14f)).Set(6f, 0f, 0f);
            track.raycastTarget = true;
            Fill(track.rectTransform, Vector2.zero, Vector2.zero);
            var fillArea = Rect(sliderRt, "FillArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fill = Shape(fillArea, "Fill", Accent).Set(6f, 0f, 0f);
            Fill(fill.rectTransform, Vector2.zero, Vector2.zero);
            var handleArea = Rect(sliderRt, "HandleArea", Vector2.zero, Vector2.one, new Vector2(9, 0), new Vector2(-9, 0));
            var handle = Shape(handleArea, "Handle", Color.white).Set(8f, 0f, 0f);
            handle.raycastTarget = true;
            var handleRt = handle.rectTransform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = new Vector2(0f, 1f);
            handleRt.offsetMin = new Vector2(-9, -9);
            handleRt.offsetMax = new Vector2(9, 9);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            var colors = slider.colors;
            colors.normalColor = new Color(0.85f, 0.87f, 0.9f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Accent;
            colors.pressedColor = new Color(1f, 0.8f, 0.4f);
            slider.colors = colors;
            slider.wholeNumbers = true;
            slider.minValue = 0;
            slider.maxValue = steps;

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
            rows.Add((slider.gameObject, plate, bar));
            return slider;
        }

        static RectTransform ButtonRow(RectTransform parent, float height)
        {
            var row = Rect(parent, "Buttons", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, height);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return row;
        }

        /// <summary>Hauteur fixe : une rangée à mise en page propre ne doit pas absorber la place libre de la plaque.</summary>
        static void Fixed(RectTransform row, float height)
        {
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        static void Spacer(RectTransform parent)
        {
            var s = Rect(parent, "Spacer", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            s.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
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

        static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

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

        static HudShape Shape(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape));
            go.transform.SetParent(parent, false);
            var s = go.GetComponent<HudShape>();
            s.color = color;
            s.raycastTarget = false;
            return s;
        }

        Text Label(Transform parent, string text, int size, TextAnchor align, Color color, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
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

        /// <summary>Bouton du HUD : plaque inclinée anthracite, or une fois choisi ; libellé posé par l'appelant.</summary>
        (Button button, Text text) AddButton(Transform parent, UnityEngine.Events.UnityAction onClick, TextAnchor align = TextAnchor.MiddleCenter)
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
            var t = Label(go.transform, "", 27, align, White, FontStyle.BoldAndItalic);
            Stretch(t.rectTransform, 0f, 1f, align == TextAnchor.MiddleLeft ? 26f : 0f, 0f);
            return (b, t);
        }
    }
}
