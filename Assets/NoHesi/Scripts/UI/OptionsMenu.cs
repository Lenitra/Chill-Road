using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Menu Options (construit en code), ouvert depuis l'accueil ou la pause, dans le langage visuel du HUD et de la
    /// pause : voile sombre, deux plaques inclinées anthracite (<see cref="HudShape"/>), police du HUD, titre or,
    /// rubriques cyan, curseurs or.
    /// À gauche, audio et partie (appliqués tout de suite, dont la probabilité de pluie) ; à droite, génération de la
    /// route (appliqués à la prochaine route, bouton « Appliquer et générer »).
    /// Manette : haut/bas pour choisir (la ligne choisie s'éclaire), gauche/droite pour régler ; les boutons du bas
    /// relient les deux plaques.
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

            // Voile sombre de la pause, puis deux plaques côte à côte : réglages de jeu à gauche, route à droite
            var shade = Rect(cgo.transform, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.62f);
            var left = Panel(shade, "Game", new Vector2(-900, -380), new Vector2(-20, 390));
            var right = Panel(shade, "Road", new Vector2(20, -380), new Vector2(900, 390));

            var title = Label(left, "OPTIONS", 64, TextAnchor.MiddleLeft, Accent, FontStyle.BoldAndItalic);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 84;
            Section(left, "AUDIO", "AUDIO", "", "");
            var music = AddSlider(left, "Musique", "Music", 0f, 1f, 20, () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, Percent);
            var effects = AddSlider(left, "Effets et moteur", "Effects and engine", 0f, 1f, 20, () => GameSettings.EffectsVolume, v => GameSettings.EffectsVolume = v, Percent);
            Section(left, "PARTIE", "GAMEPLAY", "appliqué tout de suite", "applied immediately");
            var traffic = AddSlider(left, "Densité du trafic", "Traffic density", 0.2f, 2f, 18, () => GameSettings.TrafficDensity, v => GameSettings.TrafficDensity = v, Percent);
            var rain = AddSlider(left, "Probabilité de pluie", "Rain probability", 0f, 1f, 20, () => GameSettings.RainChance, v => GameSettings.RainChance = v, v => v < 0.01f ? Loc.T("Jamais", "Never") : v > 0.99f ? Loc.T("Toujours", "Always") : Percent(v));
            // Commandes tactiles (téléphone, tablette) : façon de diriger, ou désactivées
            var touch = AddSlider(left, "Commandes tactiles", "Touch controls", 0f, 3f, 3, () => GameSettings.TouchSteer, v => GameSettings.TouchSteer = Mathf.RoundToInt(v),
                v => v < 0.5f ? Loc.T("Désactivées", "Off") : v < 1.5f ? Loc.T("Glisser", "Slide") : v < 2.5f ? Loc.T("Boutons ‹ ›", "‹ › buttons") : Loc.T("Inclinaison", "Tilt"));
            Spacer(left);
            var bottom = ButtonRow(left, 62);
            var defaults = AddButton(bottom, () => { GameSettings.ResetAll(); Changed(); RefreshAll(); });
            Loc.Bind(defaults.text, "Par défaut", "Defaults");
            var back = AddButton(bottom, Close);
            Loc.Bind(back.text, "Retour", "Back");

            var roadTitle = Label(right, "", 34, TextAnchor.MiddleLeft, Info, FontStyle.BoldAndItalic);
            Loc.Bind(roadTitle, "LA CARTE", "THE MAP");
            roadTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 84;
            Section(right, "", "", "balade libre", "free roam");
            foreach (var (fr, en) in new[]
            {
                ("Un massif alpin de 5 km : la vallée qui en fait le tour, le lac, les cols et les alpages.", "A 5 km alpine massif: the valley around it, the lake, the passes and the pastures."),
                ("55 km de routes : autoroute en boucle, route de campagne, lacets de cols, chemins de terre.", "55 km of roads: motorway loop, country road, pass hairpins, dirt tracks."),
                ("Y (ou R) : replacer la voiture sur la route la plus proche.", "Y (or R): put the car back on the nearest road."),
                ("Y (ou R) maintenu : changer de voiture.", "Hold Y (or R): switch cars."),
            })
            {
                var line = Label(right, "", 24, TextAnchor.MiddleLeft, Dim, FontStyle.Normal);
                Loc.Bind(line, fr, en);
                line.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            }
            Spacer(right);
            var apply = AddButton(right, ApplyRoad);
            applyText = apply.text;
            Loc.Bind(applyText, "Revenir au départ (route de la vallée)", "Back to the start (valley road)");

            // Aide manette / clavier sous les plaques
            var hint = Label(shade, "", 21, TextAnchor.MiddleCenter, Dim, FontStyle.Normal);
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.anchoredPosition = new Vector2(0f, -422f);
            hintRt.sizeDelta = new Vector2(1600f, 40f);
            Loc.Bind(hint, "↑ ↓  choisir   ·   ← →  régler   ·   B / Échap  retour", "↑ ↓  select   ·   ← →  adjust   ·   B / Esc  back");

            // Navigation explicite : gauche/droite restent aux curseurs (réglage), haut/bas parcourent chaque plaque,
            // les boutons du bas passent d'une plaque à l'autre
            Column(music, effects, traffic, rain, touch, defaults.button);
            Nav(defaults.button, touch, music, null, back.button);
            Nav(back.button, touch, music, defaults.button, apply.button);
            Nav(apply.button, null, null, back.button, null);
            first = music;

            RefreshAll();
            root.SetActive(false);
        }

        static string Percent(float v) => Loc.T($"{Mathf.RoundToInt(v * 100f)} %", $"{Mathf.RoundToInt(v * 100f)}%");

        // ------------------------------------------------------------------ ouverture / fermeture

        /// <summary>Ouvre le menu ; <paramref name="closed"/> est appelé à la fermeture (retour à l'accueil ou à la pause).</summary>
        public void Open(Action closed)
        {
            onClose = closed;
            roadAtOpen = GameSettings.RoadSignature;
            RefreshAll();
            root.SetActive(true);
            IsOpen = true;
            openedFrame = Time.frameCount;
            EventSystem.current?.SetSelectedGameObject(first.gameObject);
            HighlightRows();
        }

        public void Close()
        {
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

        void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            if (GameInput.CancelPressed)
            {
                Close();
                return;
            }
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == null && GameInput.UsingGamepad)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
            HighlightRows();
        }

        /// <summary>Ligne du curseur choisi (manette ou clic) : plaque plus claire et trait or à gauche.</summary>
        void HighlightRows()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var r in rows)
            {
                bool on = r.target == selected;
                r.plate.enabled = on;
                r.bar.enabled = on;
            }
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

        /// <summary>Rubrique : nom en cyan, précision estompée, filet cyan qui s'efface vers la droite.</summary>
        void Section(RectTransform parent, string fr, string en, string noteFr, string noteEn)
        {
            var row = Rect(parent, "Section", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, 46);
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

        Slider AddSlider(RectTransform parent, string fr, string en, float min, float max, int steps, Func<float> get, Action<float> set, Func<float, string> format)
        {
            var row = Rect(parent, en, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
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
