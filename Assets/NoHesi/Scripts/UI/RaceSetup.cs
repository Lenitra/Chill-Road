using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Menu d'avant-course (ouvert depuis la porte de départ, <see cref="Races"/>) : la voiture est posée sur la ligne
    /// et la caméra tourne autour d'elle. Le menu présente le tracé (genre, nom, distance, points de passage), le
    /// record et la voiture qui l'a établi, et des réglages gardés d'une course à l'autre : le fantôme du record
    /// (affiché ou masqué), le trafic pendant la course (normal, léger, aucun), la météo (actuelle, beau temps, orage)
    /// et l'heure. Météo et heure s'appliquent tout de suite (on les voit sur la voiture) et tiennent toute la course ;
    /// « Annuler » les remet comme avant. « Lancer la course » démarre le décompte ; Échap / B rend la main sur la ligne.
    /// En partie réseau, « Lancer la course » propose la course à tous les joueurs (<see cref="Races.Launch"/>) : la
    /// météo et l'heure, qui sont celles du serveur, ne se règlent pas, et une ligne de plus choisit la voiture (libre,
    /// ou la voiture du lanceur imposée à tous).
    /// </summary>
    public class RaceSetup : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color InkDim = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color Good = new Color(0.45f, 0.95f, 0.5f);

        const string GhostKey = "nohesi_race_ghost", TrafficKey = "nohesi_race_traffic";
        const string WeatherKey = "nohesi_race_weather", TimeKey = "nohesi_race_time", ImposeKey = "nohesi_race_impose";

        /// <summary>Heures proposées (-1 : l'heure du moment) et leurs noms.</summary>
        static readonly float[] Hours = { -1f, 8f, 13f, 17.5f, 19.2f, 23f };
        static readonly string[,] HourNames =
        {
            { "Actuelle", "Current" }, { "Matin", "Morning" }, { "Midi", "Noon" },
            { "Fin d'après-midi", "Late afternoon" }, { "Coucher du soleil", "Sunset" }, { "Nuit", "Night" },
        };

        /// <summary>Trafic pendant la course : facteur appliqué à la densité (normal, léger, aucun).</summary>
        static readonly float[] TrafficFactors = { 1f, 0.35f, 0f };

        public static bool IsOpen { get; private set; }
        /// <summary>Image où le menu s'est ouvert ou fermé (la touche de fermeture ne doit pas ouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;

        /// <summary>Fantôme du record affiché pendant la course.</summary>
        public static bool ShowGhost
        {
            get => PlayerPrefs.GetInt(GhostKey, 1) == 1;
            set { PlayerPrefs.SetInt(GhostKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Réglage du trafic : 0 normal, 1 léger, 2 aucun.</summary>
        public static int TrafficLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(TrafficKey, 0), 0, TrafficFactors.Length - 1);
            set { PlayerPrefs.SetInt(TrafficKey, Mathf.Clamp(value, 0, TrafficFactors.Length - 1)); PlayerPrefs.Save(); }
        }

        /// <summary>Météo de la course : 0 celle du moment, 1 beau temps, 2 orage.</summary>
        public static int WeatherChoice
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(WeatherKey, 0), 0, 2);
            set { PlayerPrefs.SetInt(WeatherKey, Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); }
        }

        /// <summary>Heure de la course : indice dans <see cref="Hours"/> (0 : l'heure du moment).</summary>
        public static int TimeChoice
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(TimeKey, 0), 0, Hours.Length - 1);
            set { PlayerPrefs.SetInt(TimeKey, Mathf.Clamp(value, 0, Hours.Length - 1)); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Course à plusieurs : la voiture du lanceur est imposée à tous les coureurs (prêtée, avec sa fiche d'origine) ;
        /// sinon chacun court avec la sienne.
        /// </summary>
        public static bool ImposeCar
        {
            get => PlayerPrefs.GetInt(ImposeKey, 0) == 1;
            set { PlayerPrefs.SetInt(ImposeKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Facteur de densité du trafic choisi pour la course.</summary>
        public static float TrafficFactor => TrafficFactors[TrafficLevel];

        /// <summary>Facteur de densité d'un réglage du trafic (0 normal, 1 léger, 2 aucun).</summary>
        public static float FactorOf(int level) => TrafficFactors[Mathf.Clamp(level, 0, TrafficFactors.Length - 1)];

        class Row
        {
            public Button button;
            public Text name, value;
        }

        GameManager game;
        Races races;
        Races.Track track;
        Font font;
        GameObject root;
        RectTransform screenRt, panelRt;
        HudShape accent, underline;
        Text kindText, nameText, hintText;
        readonly List<(Text label, Text value)> facts = new List<(Text, Text)>();
        readonly List<Row> rows = new List<Row>();
        int openedFrame, shown;
        float stickPrev;
        // Temps et heure trouvés à l'ouverture (rendus si l'on annule)
        bool startStorm;
        float startHour;
        // Partie en réseau à l'ouverture du menu : météo et heure du serveur, ligne « Voiture »
        bool online;

        const int RowGhost = 0, RowTraffic = 1, RowWeather = 2, RowTime = 3, RowCar = 4, RowStart = 5, RowCancel = 6, RowCount = 7;

        public void Init(GameManager g, Races r)
        {
            game = g;
            races = r;
            font = HudArt.Font;
        }

        void OnDestroy() => IsOpen = false;

        /// <summary>Ouvre le menu d'un tracé : voiture posée sur la ligne, caméra tournante, HUD masqué.</summary>
        public void Open(Races.Track t)
        {
            if (IsOpen || t == null || game.State != GameManager.Phase.Playing) return;
            if (!root) Build();
            track = t;
            IsOpen = true;
            root.SetActive(true);
            openedFrame = ChangedFrame = Time.frameCount;
            races.PlaceOnLine(t);
            game.SetInputEnabled(false);
            online = game.Online;
            rows[RowCar].button.gameObject.SetActive(online);
            rows[RowWeather].button.interactable = rows[RowTime].button.interactable = !online;
            Wire();
            startStorm = game.Weather && game.Weather.Stormy;
            startHour = game.DayNight ? game.DayNight.timeOfDay : 12f;
            ApplyConditions(true);
            if (game.Hud) game.Hud.SetVisible(false);
            game.Camera.Showcase = true;
            Select(RowStart);
            Refresh();
        }

        /// <summary>Ligne que l'on peut choisir : affichée et active (en réseau, la météo et l'heure ne le sont pas).</summary>
        bool Usable(int row) => row >= 0 && row < rows.Count && rows[row].button.gameObject.activeSelf && rows[row].button.interactable;

        /// <summary>Navigation haut / bas entre les lignes que l'on peut choisir, en boucle.</summary>
        void Wire()
        {
            for (int k = 0; k < rows.Count; k++)
            {
                int up = k, down = k;
                do up = (up - 1 + rows.Count) % rows.Count; while (up != k && !Usable(up));
                do down = (down + 1) % rows.Count; while (down != k && !Usable(down));
                rows[k].button.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = rows[up].button, selectOnDown = rows[down].button };
            }
        }

        /// <summary>
        /// Ferme le menu. go : la course démarre (décompte ; en réseau, elle est proposée aux autres joueurs), sinon on
        /// reprend la main sur la ligne.
        /// </summary>
        void Close(bool go)
        {
            if (!IsOpen) return;
            IsOpen = false;
            ChangedFrame = Time.frameCount;
            root.SetActive(false);
            game.Camera.Showcase = false;
            if (game.Hud) game.Hud.SetVisible(true);
            game.SetInputEnabled(true);
            EventSystem.current?.SetSelectedGameObject(null);
            var t = track;
            track = null;
            if (go)
            {
                races.Launch(t);
                return;
            }
            // Annulé : le temps et l'heure reviennent comme à l'ouverture, et reprennent leur cours (en réseau, on n'y a pas touché)
            if (online) return;
            if (game.Weather) { game.Weather.Hold(startStorm, true); game.Weather.Release(); }
            if (game.DayNight) { game.DayNight.SetTime(startHour); game.DayNight.frozen = false; }
        }

        void Update()
        {
            if (!IsOpen) return;
            if (game.State != GameManager.Phase.Playing) { Close(false); return; }
            if (Time.frameCount != openedFrame && (GameInput.CancelPressed || GameInput.PausePressed)) { Close(false); return; }

            // Toujours une ligne choisie (manette, clavier)
            var es = EventSystem.current;
            if (es)
            {
                int selected = -1;
                for (int i = 0; i < rows.Count; i++)
                    if (es.currentSelectedGameObject == rows[i].button.gameObject) selected = i;
                // Une ligne inactive survolée à la souris ne se choisit pas
                if (!Usable(selected)) Select(Usable(shown) ? shown : RowStart);
                else shown = selected;
            }
            // Gauche / droite : change le réglage de la ligne choisie
            int dir = Horizontal();
            if (dir != 0 && Time.frameCount > openedFrame + 1) Change(shown, dir);
            Refresh();
        }

        void Select(int row)
        {
            shown = Mathf.Clamp(row, 0, rows.Count - 1);
            EventSystem.current?.SetSelectedGameObject(rows[shown].button.gameObject);
        }

        /// <summary>Validation d'une ligne : réglage suivant, lancement ou annulation.</summary>
        void Choose(int row)
        {
            // L'appui qui a ouvert le menu ne doit pas valider une ligne
            if (Time.frameCount <= openedFrame + 1) return;
            if (row != shown) { Select(row); return; }
            switch (row)
            {
                case RowGhost:
                case RowTraffic:
                case RowWeather:
                case RowTime:
                case RowCar: Change(row, 1); break;
                case RowStart: Close(true); break;
                default: Close(false); break;
            }
        }

        void Change(int row, int dir)
        {
            if (row == RowGhost)
            {
                if (track != null && track.best > 0f) ShowGhost = !ShowGhost;
            }
            else if (row == RowTraffic)
            {
                int n = TrafficFactors.Length;
                TrafficLevel = ((TrafficLevel + dir) % n + n) % n;
            }
            else if (row == RowWeather && !online)
            {
                WeatherChoice = ((WeatherChoice + dir) % 3 + 3) % 3;
                ApplyConditions(false);
            }
            else if (row == RowTime && !online)
            {
                int n = Hours.Length;
                TimeChoice = ((TimeChoice + dir) % n + n) % n;
                ApplyConditions(false);
            }
            else if (row == RowCar && online) ImposeCar = !ImposeCar;
            else return;
            if (game.Local.Audio) game.Local.Audio.Chime();
        }

        /// <summary>
        /// Météo et heure choisies, appliquées tout de suite et tenues (plus de changement de temps, horloge arrêtée)
        /// jusqu'à la fin de la course. « Actuelle » : le temps et l'heure trouvés à l'ouverture.
        /// Sans effet en partie réseau : la météo et l'heure y sont celles du serveur, pour tous les joueurs.
        /// </summary>
        void ApplyConditions(bool opening)
        {
            if (game.Online) return;
            if (game.Weather)
            {
                int w = WeatherChoice;
                // Temps du moment : la transition en cours s'achève sans à-coup ; sinon tout de suite
                if (w == 0) game.Weather.Hold(startStorm, !opening);
                else game.Weather.Hold(w == 2, true);
            }
            if (game.DayNight)
            {
                float h = Hours[TimeChoice];
                game.DayNight.frozen = true;
                game.DayNight.SetTime(h < 0f ? startHour : h);
            }
        }

        static string HourLabel(int i)
        {
            string name = Loc.T(HourNames[i, 0], HourNames[i, 1]);
            if (Hours[i] < 0f) return name;
            int m = Mathf.RoundToInt(Hours[i] * 60f);
            return name + $"  {m / 60:00}:{m % 60:00}";
        }

        /// <summary>Gauche / droite appuyé à cette image : -1, 1 ou 0 (flèches, A/D/Q, croix, joystick gauche).</summary>
        int Horizontal()
        {
            int d = 0;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame) d = -1;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) d = 1;
            }
            foreach (var p in Gamepad.all)
            {
                if (p.dpad.left.wasPressedThisFrame) d = -1;
                if (p.dpad.right.wasPressedThisFrame) d = 1;
            }
            var pad = Gamepad.current;
            float x = pad != null ? pad.leftStick.x.ReadValue() : 0f;
            if (Mathf.Abs(x) > 0.6f && Mathf.Abs(stickPrev) <= 0.6f) d = x > 0f ? 1 : -1;
            stickPrev = x;
            return d;
        }

        // ------------------------------------------------------------------ affichage

        void Refresh()
        {
            var t = track;
            if (t == null) return;
            panelRt.localScale = Vector3.one * Mathf.Min(1f, (screenRt.rect.height - 80f) / 1000f);
            accent.color = t.Tint;
            underline.color = new Color(t.Tint.r, t.Tint.g, t.Tint.b, 0.5f);
            kindText.text = t.Kind;
            kindText.color = t.Tint;
            nameText.text = t.Name;

            bool hasBest = t.best > 0f;
            string carName = game.Player && game.Player.Spec != null ? game.Player.Spec.displayName : "";
            SetFact(0, Loc.T("Distance", "Distance"), Loc.F($"{t.Distance / 1000f:0.0} km") + (t.Loop ? Loc.F($"  ({t.laps} × {t.length / 1000f:0.0} km)", $"  ({t.laps} × {t.length / 1000f:0.0} km)") : ""), White);
            SetFact(1, Loc.T("Points de passage", "Checkpoints"), t.GateCount.ToString(), White);
            SetFact(2, Loc.T("Record", "Best time"), hasBest ? Races.Clock(t.best) : Loc.T("Aucun", "None yet"), hasBest ? Good : Dim);
            SetFact(3, Loc.T("Voiture du record", "Best time car"), hasBest && !string.IsNullOrEmpty(t.bestCar) ? t.bestCar : "—", hasBest ? White : Dim);
            SetFact(4, Loc.T("Ta voiture", "Your car"), carName, White);

            string[] traffic = { Loc.T("Normal", "Normal"), Loc.T("Léger", "Light"), Loc.T("Aucun", "None") };
            rows[RowGhost].name.text = Loc.T("Fantôme du record", "Best time ghost");
            rows[RowGhost].value.text = !hasBest ? Loc.T("pas de record", "no best time") : "‹  " + (ShowGhost ? Loc.T("Affiché", "Shown") : Loc.T("Masqué", "Hidden")) + "  ›";
            rows[RowTraffic].name.text = Loc.T("Trafic", "Traffic");
            rows[RowTraffic].value.text = "‹  " + traffic[TrafficLevel] + "  ›";
            string[] weather = { Loc.T("Actuelle", "Current"), Loc.T("Beau temps", "Clear"), Loc.T("Orage", "Storm") };
            rows[RowWeather].name.text = Loc.T("Météo", "Weather");
            rows[RowWeather].value.text = "‹  " + weather[WeatherChoice] + "  ›";
            rows[RowTime].name.text = Loc.T("Heure", "Time of day");
            rows[RowTime].value.text = "‹  " + HourLabel(TimeChoice) + "  ›";
            rows[RowStart].name.text = Loc.T("Lancer la course", "Start the race");
            rows[RowStart].value.text = "";
            rows[RowCancel].name.text = Loc.T("Annuler", "Cancel");
            rows[RowCancel].value.text = "";
            if (online)
            {
                // Partie en réseau : météo et heure du serveur ; voiture libre ou imposée ; la course est proposée à tous
                rows[RowWeather].value.text = rows[RowTime].value.text = Loc.T("réglée par le serveur", "set by the server");
                rows[RowCar].name.text = Loc.T("Voiture", "Car");
                rows[RowCar].value.text = "‹  " + (ImposeCar ? Loc.T("Imposée : ", "Same for all: ") + carName : Loc.T("Libre", "Free choice")) + "  ›";
                var net = game.Net;
                int others = net.PlayerCount - 1;
                if (net.Race.Exists)
                {
                    // Une seule course à la fois par serveur : pendant celle des autres, on court pour son chrono
                    rows[RowStart].name.text = Loc.T("Lancer en solo", "Start solo");
                    rows[RowStart].value.text = Loc.T("course en cours sur le serveur", "a race is running on the server");
                }
                else if (others > 0)
                    rows[RowStart].value.text = others > 1 ? Loc.T($"{others} joueurs invités", $"{others} players invited") : Loc.T("1 joueur invité", "1 player invited");
            }
            for (int i = 0; i < rows.Count; i++)
            {
                bool on = i == shown;
                bool off = online && (i == RowWeather || i == RowTime);
                rows[i].name.color = off ? Dim : on ? Ink : White;
                bool muted = off || i == RowGhost && !hasBest;
                rows[i].value.color = on ? (muted ? InkDim : Ink) : (muted ? Dim : White);
            }

            bool pad = GameInput.UsingGamepad;
            hintText.text = pad ? Loc.T("↑ ↓ choisir  ·  ← → régler  ·  A valider  ·  B annuler", "↑ ↓ browse  ·  ← → change  ·  A select  ·  B cancel")
                : Loc.T("↑ ↓ choisir  ·  ← → régler  ·  Entrée valider  ·  Échap annuler", "↑ ↓ browse  ·  ← → change  ·  Enter select  ·  Esc cancel");
        }

        void SetFact(int i, string label, string value, Color color)
        {
            facts[i].label.text = label;
            facts[i].value.text = value;
            facts[i].value.color = color;
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            root = new GameObject("RaceSetup", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Même échelle que les menus de la concession et du garage (sobres, pas trop grands)
            scaler.referenceResolution = new Vector2(2400, 1350);
            scaler.matchWidthOrHeight = 0.5f;
            screenRt = (RectTransform)root.transform;

            // Panneau à gauche, centré en hauteur ; sa hauteur suit son contenu
            var panel = Rect(screenRt, "Panel", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.sizeDelta = new Vector2(620f, 0f);
            panel.anchoredPosition = new Vector2(70f, 0f);
            panelRt = panel;
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Bandeau : genre (à la couleur du tracé) et nom
            var banner = Bar(panel, 108f, new Color(0.02f, 0.025f, 0.04f, 0.9f));
            accent = Fill(banner, "Accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(5f, 0f));
            underline = Fill(banner, "Underline", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f));
            kindText = Place(Label(banner, 22, TextAnchor.MiddleLeft, White, FontStyle.Bold), new Vector2(28f, -12f), new Vector2(560f, 30f));
            nameText = Place(Label(banner, 42, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), new Vector2(26f, -44f), new Vector2(560f, 52f));

            // Fiche du tracé : libellé à gauche, valeur à droite
            var info = Bar(panel, 5 * 38f + 20f, new Color(0.025f, 0.03f, 0.045f, 0.78f));
            for (int i = 0; i < 5; i++)
            {
                float y = -12f - i * 38f;
                var label = Place(Label(info, 21, TextAnchor.MiddleLeft, Dim, FontStyle.Normal), new Vector2(28f, y), new Vector2(300f, 34f));
                var value = Place(Label(info, 22, TextAnchor.MiddleRight, White, FontStyle.Bold), new Vector2(240f, y), new Vector2(352f, 34f));
                facts.Add((label, value));
            }

            var section = Bar(panel, 40f, new Color(0.01f, 0.012f, 0.02f, 0.9f));
            var sectionText = Label(section, 20, TextAnchor.MiddleLeft, Dim, FontStyle.Bold);
            Stretch(sectionText.rectTransform, 28f, 20f);
            Loc.Bind(sectionText, "RÉGLAGES DE LA COURSE", "RACE SETTINGS");

            for (int i = 0; i < RowCount; i++)
            {
                int index = i;
                rows.Add(MakeRow(panel, () => Choose(index), i == RowStart ? 56f : 48f, i == RowStart ? 26 : 23));
            }
            Wire();
            hintText = Label(panel, 18, TextAnchor.MiddleLeft, Dim, FontStyle.Bold);
            hintText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            root.SetActive(false);
        }

        static RectTransform Bar(Transform parent, float height, Color color)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var shape = go.GetComponent<HudShape>();
            shape.Set(0f, 0f, 0f);
            shape.color = color;
            shape.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        static HudShape Fill(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var rt = Rect(parent, name, aMin, aMax, oMin, oMax);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var s = rt.gameObject.AddComponent<HudShape>();
            s.Set(0f, 0f, 0f);
            s.raycastTarget = false;
            return s;
        }

        /// <summary>Ligne du menu : sombre, blanche à texte foncé quand elle est choisie (comme le garage de tuning).</summary>
        Row MakeRow(Transform parent, UnityEngine.Events.UnityAction onClick, float height, int size)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button), typeof(LayoutElement), typeof(HoverSelect));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var shape = go.GetComponent<HudShape>();
            shape.Set(0f, 0f, 0f);
            shape.color = Color.white;
            var b = go.GetComponent<Button>();
            b.targetGraphic = shape;
            var colors = b.colors;
            colors.normalColor = new Color(0.04f, 0.05f, 0.07f, 0.8f);
            colors.highlightedColor = new Color(0.16f, 0.18f, 0.23f, 0.92f);
            colors.selectedColor = new Color(0.95f, 0.96f, 0.97f, 1f);
            colors.pressedColor = new Color(0.8f, 0.82f, 0.86f, 1f);
            colors.disabledColor = new Color(0.04f, 0.05f, 0.07f, 0.55f);
            colors.fadeDuration = 0.06f;
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var name = Label(go.transform, size, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic);
            Stretch(name.rectTransform, 26f, 250f);
            name.GetComponent<Shadow>().enabled = false;
            var value = Label(go.transform, 21, TextAnchor.MiddleRight, White, FontStyle.Bold);
            Stretch(value.rectTransform, 280f, 24f);
            value.GetComponent<Shadow>().enabled = false;
            return new Row { button = b, name = name, value = value };
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

        static void Stretch(RectTransform rt, float left, float right)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
        }

        /// <summary>Texte posé depuis le haut à gauche d'un bandeau, à une position donnée.</summary>
        static Text Place(Text t, Vector2 pos, Vector2 size)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos + new Vector2(0f, -size.y * 0.5f);
            return t;
        }

        Text Label(Transform parent, int size, TextAnchor align, Color color, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = color; t.text = "";
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
