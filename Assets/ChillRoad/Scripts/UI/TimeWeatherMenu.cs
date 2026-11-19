using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Écran « Heure et météo », ouvert depuis la tuile du menu pause (style verre). La scène reste visible à droite :
    /// elle change à mesure qu'on règle (aperçu en direct).
    /// - HEURE : l'heure en grand et son moment de la journée, une frise des 24 h (← → : ±30 min, clic : à cet endroit),
    ///   des raccourcis Aube / Midi / Coucher / Nuit, l'interrupteur « Le temps passe » (heure figée sinon) ;
    /// - MÉTÉO : Au hasard (la météo suit les options), Beau temps ou Orage (tenus jusqu'au retour au hasard).
    /// Pendant une course, l'heure et la météo sont celles de la course : l'écran le dit et ne change rien.
    /// B / Échap : retour au menu pause.
    /// </summary>
    public class TimeWeatherMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        /// <summary>Image où l'écran s'est ouvert ou fermé (la touche de fermeture ne doit pas rouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;
        /// <summary>Choix de météo : 0 au hasard, 1 beau temps, 2 orage.</summary>
        static int weatherChoice;

        static readonly (float hour, string fr, string en)[] Presets =
        {
            (6.5f, "Aube", "Dawn"), (12f, "Midi", "Noon"), (19.2f, "Coucher", "Sunset"), (23f, "Nuit", "Night"),
        };

        GameManager game;
        GameObject root;
        Text clockText, momentText, lockText, hintText, flowState;
        RectTransform timeline, handle;
        readonly List<OptionSelector> rows = new List<OptionSelector>();
        readonly List<GlassRect> focus = new List<GlassRect>();
        readonly List<(GlassRect plate, Text text)> presetChips = new List<(GlassRect, Text)>();
        readonly List<(GlassRect plate, HudIcon icon, Text name, Text note)> weatherTiles = new List<(GlassRect, HudIcon, Text, Text)>();
        GlassRect flowSwitch;
        RectTransform flowKnob;
        Button backButton;
        GlassRect backPlate;
        Text backText;
        int openedFrame, preset = -1;
        System.Action onClose;

        public void Init(GameManager gm)
        {
            game = gm;
            Build();
            root.SetActive(false);
        }

        void OnDestroy()
        {
            IsOpen = false;
            if (root) Destroy(root);
        }

        bool Locked => game.Races && game.Races.CarLocked;

        public void Open(System.Action onClosed)
        {
            if (IsOpen) return;
            onClose = onClosed;
            IsOpen = true;
            openedFrame = ChangedFrame = Time.frameCount;
            if (game.Weather && !game.Weather.Held) weatherChoice = 0;
            root.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(rows[0].gameObject);
            Refresh();
        }

        void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ChangedFrame = Time.frameCount;
            root.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            var cb = onClose;
            onClose = null;
            cb?.Invoke();
        }

        // ------------------------------------------------------------------ réglages

        void AddHours(float h)
        {
            if (Locked || !game.DayNight) return;
            float t = Mathf.Round((game.DayNight.timeOfDay + h) * 2f) * 0.5f;
            game.DayNight.SetTime(t);
            preset = -1;
        }

        void SetPreset(int i)
        {
            if (Locked || !game.DayNight) return;
            preset = (i % Presets.Length + Presets.Length) % Presets.Length;
            game.DayNight.SetTime(Presets[preset].hour);
        }

        void ToggleFlow()
        {
            if (Locked || !game.DayNight) return;
            game.DayNight.frozen = !game.DayNight.frozen;
        }

        void SetWeather(int choice)
        {
            if (Locked || !game.Weather) return;
            weatherChoice = Mathf.Clamp(choice, 0, 2);
            if (weatherChoice == 0) game.Weather.Release();
            else game.Weather.Hold(weatherChoice == 2, true);
        }

        static string Moment(float h)
        {
            if (h >= 5f && h < 8f) return Loc.T("Aube", "Dawn");
            if (h >= 8f && h < 11.5f) return Loc.T("Matin", "Morning");
            if (h >= 11.5f && h < 14f) return Loc.T("Midi", "Noon");
            if (h >= 14f && h < 18f) return Loc.T("Après-midi", "Afternoon");
            if (h >= 18f && h < 20.5f) return Loc.T("Coucher du soleil", "Sunset");
            if (h >= 20.5f && h < 22f) return Loc.T("Crépuscule", "Dusk");
            return Loc.T("Nuit", "Night");
        }

        // ------------------------------------------------------------------ boucle

        void Update()
        {
            if (!IsOpen) return;
            if (Time.frameCount != openedFrame && (GameInput.CancelPressed || GameInput.PausePressed)) { Close(); return; }
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject == null && GameInput.UsingGamepad) es.SetSelectedGameObject(rows[0].gameObject);
            Refresh();
        }

        void Refresh()
        {
            var dn = game.DayNight;
            float hour = dn ? dn.timeOfDay : 12f;
            clockText.text = dn ? dn.Clock : "--:--";
            momentText.text = Moment(hour);
            handle.anchorMin = handle.anchorMax = new Vector2(hour / 24f, 0.5f);
            handle.anchoredPosition = Vector2.zero;
            bool locked = Locked;
            lockText.gameObject.SetActive(locked);
            var sel = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            for (int i = 0; i < rows.Count; i++)
            {
                bool on = sel == rows[i].gameObject;
                focus[i].color = on ? Glass.PanelStrong : Color.clear;   // fond sombre : l'or terni sur le décor clair faisait sale
                focus[i].borderColor = on ? Glass.A(Glass.Gold, 0.9f) : Color.clear;
                focus[i].SetVerticesDirty();
            }
            for (int i = 0; i < presetChips.Count; i++)
            {
                bool on = i == preset;
                var (plate, text) = presetChips[i];
                plate.color = on ? Glass.Gold : Glass.Pill;
                plate.borderColor = on ? Color.clear : Glass.Edge;
                plate.SetVerticesDirty();
                text.color = on ? Glass.Ink : Glass.White;
                text.font = Glass.FontOf(on ? Glass.Weight.Semibold : Glass.Weight.Regular);
            }
            bool flowing = dn && !dn.frozen;
            flowState.text = flowing ? Loc.T("Activé", "On") : Loc.T("Figée", "Frozen");
            flowState.color = flowing ? Glass.Gold : Glass.Dim;
            flowSwitch.color = flowing ? Glass.Gold : Glass.A(Color.white, 0.18f);
            flowSwitch.SetVerticesDirty();
            flowKnob.anchoredPosition = new Vector2(flowing ? 50f : 4f, 0f);
            flowKnob.GetComponent<Image>().color = flowing ? Glass.Ink : Glass.White;
            int current = weatherChoice;
            // Pluie visible (montée ou fin d'averse) = orage, même si la météo est en train de tourner
            bool stormy = game.Weather && (game.Weather.Stormy || game.Weather.Rain > 0.05f);
            for (int i = 0; i < weatherTiles.Count; i++)
            {
                var (plate, icon, name, note) = weatherTiles[i];
                bool on = i == current;
                plate.color = on ? Glass.PanelStrong : Glass.Panel;
                plate.borderColor = on ? Glass.Gold : Glass.Edge;
                plate.border = on ? 2.5f : 1.5f;
                plate.halo = on ? 10f : 0f;
                plate.SetVerticesDirty();
                icon.SetColors(on ? Glass.Gold : Glass.White, new Color(0.12f, 0.14f, 0.17f));
                name.color = on ? Glass.White : Glass.Dim;
                if (i == 0) note.text = on ? (stormy ? Loc.T("en ce moment : orage", "right now: storm") : Loc.T("en ce moment : beau temps", "right now: clear")) : Loc.T("selon les options", "per the options");
            }
            bool sb = sel == backButton.gameObject;
            backPlate.color = sb ? Glass.Gold : Glass.Pill;
            backPlate.borderColor = sb ? Color.clear : Glass.Edge;
            backPlate.SetVerticesDirty();
            backText.color = sb ? Glass.Ink : Glass.White;
            hintText.text = GameInput.UsingGamepad
                ? Loc.T("<color=#FFC233>← →</color>  régler     <color=#FFC233>B</color>  retour", "<color=#FFC233>← →</color>  adjust     <color=#FFC233>B</color>  back")
                : Loc.T("<color=#FFC233>← →</color>  régler     <color=#FFC233>Échap</color>  retour", "<color=#FFC233>← →</color>  adjust     <color=#FFC233>Esc</color>  back");
        }

        /// <summary>Clic sur la frise : l'heure sous le pointeur (à la demi-heure).</summary>
        void ClickTimeline()
        {
            if (Locked || !game.DayNight || Mouse.current == null) return;
            var cam = (Camera)null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(timeline, Mouse.current.position.ReadValue(), cam, out var p)) return;
            var r = timeline.rect;
            float t = Mathf.Clamp01((p.x - r.xMin) / r.width) * 24f;
            game.DayNight.SetTime(Mathf.Round(t * 2f) * 0.5f);
            preset = -1;
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            root = new GameObject("TimeWeatherMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var screen = (RectTransform)root.transform;

            // Voile à gauche, qui s'efface vers la droite : la scène reste visible pour l'aperçu
            var shade = new GameObject("Shade", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            shade.transform.SetParent(screen, false);
            var sr = shade.rectTransform;
            sr.anchorMin = Vector2.zero; sr.anchorMax = new Vector2(0f, 1f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.sizeDelta = new Vector2(1150f, 0f);
            shade.texture = Gradient(new[] { (0f, new Color(0.04f, 0.05f, 0.07f, 0.9f)), (0.72f, new Color(0.04f, 0.05f, 0.07f, 0.74f)), (1f, new Color(0.04f, 0.05f, 0.07f, 0f)) });
            shade.raycastTarget = true;

            var col = Glass.Place(screen, "Column", new Vector2(0f, 1f), new Vector2(120f, -60f), new Vector2(860f, 960f));
            var kicker = Glass.Kicker(col, "PAUSE", 17);
            Place(kicker.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(600f, 26f));
            var title = Glass.Label(col, "", 58, Glass.Weight.Light, Glass.White, TextAnchor.UpperLeft);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -24f), new Vector2(860f, 74f));
            Loc.Bind(title, Glass.Spaced("HEURE ET MÉTÉO", 2), Glass.Spaced("TIME AND WEATHER", 2));

            lockText = Glass.Label(col, "", 20, Glass.Weight.Semilight, Glass.Bad, TextAnchor.UpperLeft);
            Place(lockText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -102f), new Vector2(860f, 28f));
            Loc.Bind(lockText, "Course en cours : l'heure et la météo sont celles de la course", "Race in progress: time and weather belong to the race");

            // HEURE
            var k1 = Glass.Kicker(col, Loc.T("HEURE", "TIME"), 16);
            Place(k1.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -140f), new Vector2(400f, 24f));
            var timeRow = Row(col, -168f, 230f);
            timeRow.Move = d => AddHours(d * 0.5f);
            timeRow.Submit = () => SetPreset(preset + 1);
            timeRow.Click = ClickTimeline;
            clockText = Glass.Label(timeRow.transform, "", 110, Glass.Weight.Light, Glass.White, TextAnchor.UpperLeft);
            Place(clockText.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -4f), new Vector2(360f, 124f));
            momentText = Glass.Label(timeRow.transform, "", 24, Glass.Weight.Semilight, Glass.Dim, TextAnchor.LowerLeft);
            Place(momentText.rectTransform, new Vector2(0f, 1f), new Vector2(330f, -82f), new Vector2(400f, 34f));
            var bar = new GameObject("Timeline", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            bar.transform.SetParent(timeRow.transform, false);
            timeline = bar.rectTransform;
            Place(timeline, new Vector2(0f, 1f), new Vector2(18f, -138f), new Vector2(824f, 48f));
            bar.texture = Gradient(new[]
            {
                (0f, new Color(0.11f, 0.14f, 0.25f)), (0.18f, new Color(0.17f, 0.23f, 0.42f)), (0.27f, new Color(0.91f, 0.64f, 0.42f)),
                (0.38f, new Color(0.56f, 0.77f, 0.93f)), (0.55f, new Color(0.62f, 0.83f, 0.96f)), (0.66f, new Color(0.56f, 0.77f, 0.93f)),
                (0.79f, new Color(0.94f, 0.56f, 0.35f)), (0.87f, new Color(0.23f, 0.2f, 0.38f)), (1f, new Color(0.11f, 0.14f, 0.25f)),
            });
            bar.raycastTarget = true;
            bar.gameObject.AddComponent<OptionHit>().Click = ClickTimeline;
            var mask = Glass.Make(timeline, "Edge", 24f, Color.clear, 2f);
            mask.borderColor = Glass.A(Color.white, 0.35f);
            Glass.Stretch(mask.rectTransform);
            handle = Glass.Rect(timeline, "Handle", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            handle.sizeDelta = new Vector2(10f, 58f);
            var hp = Glass.Make(handle, "Knob", 5f, Color.white, 0f);
            hp.halo = 8f;
            Glass.Stretch(hp.rectTransform);
            string[] ticks = { "0 h", "6 h", "12 h", "18 h", "24 h" };
            for (int i = 0; i < ticks.Length; i++)
            {
                var t = Glass.Label(timeRow.transform, ticks[i], 17, Glass.Weight.Semilight, Glass.Faint, TextAnchor.UpperCenter);
                Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(18f + 824f * i / 4f - 40f, -192f), new Vector2(80f, 24f));
            }

            // Raccourcis et temps qui passe
            var presetRow = Row(col, -410f, 70f);
            presetRow.Move = d => SetPreset(preset < 0 ? (d > 0 ? 0 : Presets.Length - 1) : preset + d);
            presetRow.Submit = () => SetPreset(preset + 1);
            float x = 18f;
            for (int i = 0; i < Presets.Length; i++)
            {
                var chip = Glass.Place(presetRow.transform, "Chip", new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(150f, 48f));
                var plate = Glass.Backdrop(chip, 24f, Glass.Pill);
                var text = Glass.Label(chip, "", 20, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
                Glass.Stretch(text.rectTransform);
                Loc.Bind(text, Presets[i].fr, Presets[i].en);
                int k = i;
                plate.raycastTarget = true;
                chip.gameObject.AddComponent<OptionHit>().Click = () => SetPreset(k);
                presetChips.Add((plate, text));
                x += 162f;
            }
            var flowRow = Row(col, -492f, 70f);
            flowRow.Move = _ => ToggleFlow();
            flowRow.Submit = ToggleFlow;
            flowRow.Click = ToggleFlow;
            var flowLabel = Glass.Label(flowRow.transform, "", 22, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleLeft);
            Place(flowLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(500f, 40f));
            Loc.Bind(flowLabel, "Le temps passe", "Time flows");
            flowState = Glass.Label(flowRow.transform, "", 19, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleRight);
            Place(flowState.rectTransform, new Vector2(1f, 0.5f), new Vector2(-110f, 0f), new Vector2(200f, 40f));
            var sw = Glass.Place(flowRow.transform, "Switch", new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(80f, 34f));
            flowSwitch = Glass.Backdrop(sw, 17f, Glass.Gold, 0f);
            flowKnob = Glass.Place(sw, "Knob", new Vector2(0f, 0.5f), new Vector2(50f, 0f), new Vector2(26f, 26f));
            var knob = flowKnob.gameObject.AddComponent<Image>();
            knob.sprite = HudArt.Disc;
            knob.raycastTarget = false;

            // MÉTÉO
            var k2 = Glass.Kicker(col, Loc.T("MÉTÉO", "WEATHER"), 16);
            Place(k2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -590f), new Vector2(400f, 24f));
            var wxRow = Row(col, -618f, 222f);
            wxRow.Move = d => SetWeather(weatherChoice + d);
            wxRow.Submit = () => SetWeather((weatherChoice + 1) % 3);
            var kinds = new[] { (HudIcon.Kind.Road, "Au hasard", "Random"), (HudIcon.Kind.Clock, "Beau temps", "Clear"), (HudIcon.Kind.Radar, "Orage", "Storm") };
            float tw = (824f - 2f * 18f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var tile = Glass.Place(wxRow.transform, "Weather", new Vector2(0f, 1f), new Vector2(18f + i * (tw + 18f), -12f), new Vector2(tw, 198f));
                var plate = Glass.Backdrop(tile, 26f, Glass.Pill);
                plate.raycastTarget = true;
                int k = i;
                tile.gameObject.AddComponent<OptionHit>().Click = () => SetWeather(k);
                var ig = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
                ig.transform.SetParent(tile, false);
                var icon = ig.GetComponent<HudIcon>();
                icon.Shape = kinds[i].Item1;
                icon.raycastTarget = false;
                Place((RectTransform)ig.transform, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(66f, 66f));
                var name = Glass.Label(tile, "", 26, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
                Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(tw, 36f));
                Loc.Bind(name, kinds[i].Item2, kinds[i].Item3);
                var note = Glass.Label(tile, "", 17, Glass.Weight.Semilight, Glass.Faint, TextAnchor.MiddleCenter);
                Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -148f), new Vector2(tw - 16f, 26f));
                if (i == 1) Loc.Bind(note, "tenu jusqu'au retour au hasard", "held until set back to random");
                if (i == 2) Loc.Bind(note, "pluie et éclairs", "rain and lightning");
                weatherTiles.Add((plate, icon, name, note));
            }
            // Pictogrammes de météo dessinés à part (soleil, nuage) : pas dans HudIcon ; on garde ceux du jeu en attendant
            weatherTiles[1].icon.gameObject.AddComponent<SunGlyph>();
            weatherTiles[2].icon.gameObject.AddComponent<StormGlyph>();

            // Retour et aide
            var back = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button), typeof(HoverSelect));
            back.transform.SetParent(col, false);
            Place((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(0f, -886f), new Vector2(200f, 64f));
            backPlate = back.GetComponent<GlassRect>();
            backPlate.radius = 32f;
            backPlate.color = Glass.Pill;
            backButton = back.GetComponent<Button>();
            backButton.targetGraphic = backPlate;
            backButton.transition = Selectable.Transition.None;
            backButton.onClick.AddListener(Close);
            backText = Glass.Label(back.transform, "", 23, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(backText.rectTransform);
            Loc.Bind(backText, "Retour", "Back");
            hintText = Glass.Label(col, "", 20, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleLeft);
            Place(hintText.rectTransform, new Vector2(0f, 1f), new Vector2(230f, -886f), new Vector2(620f, 64f));

            var preview = Glass.Place(screen, "Preview", new Vector2(1f, 0f), new Vector2(-60f, 60f), new Vector2(430f, 96f));
            Glass.Backdrop(preview, 28f);
            var pk = Glass.Kicker(preview, Loc.T("APERÇU EN DIRECT", "LIVE PREVIEW"), 15);
            Place(pk.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(380f, 22f));
            var pt = Glass.Label(preview, "", 19, Glass.Weight.Semilight, Glass.Dim, TextAnchor.UpperLeft);
            Place(pt.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -46f), new Vector2(380f, 40f));
            Loc.Bind(pt, "La scène change à mesure que tu règles.", "The scene changes as you adjust.");

            // Navigation verticale explicite entre les lignes puis le bouton Retour
            var all = new List<Selectable>(rows) { backButton };
            for (int i = 0; i < all.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = all[(i - 1 + all.Count) % all.Count];
                nav.selectOnDown = all[(i + 1) % all.Count];
                all[i].navigation = nav;
            }
        }

        /// <summary>Ligne réglable : zone de verre (visible quand elle est choisie) sur toute la largeur de la colonne.</summary>
        OptionSelector Row(RectTransform col, float top, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(OptionSelector), typeof(HoverSelect));
            go.transform.SetParent(col, false);
            Place((RectTransform)go.transform, new Vector2(0f, 1f), new Vector2(-18f, top), new Vector2(896f, height));
            var plate = go.GetComponent<GlassRect>();
            plate.radius = 24f;
            plate.border = 1.5f;
            plate.color = Color.clear;
            plate.borderColor = Color.clear;
            var sel = go.GetComponent<OptionSelector>();
            sel.targetGraphic = plate;
            sel.transition = Selectable.Transition.None;
            // Les enfants se placent comme si la ligne commençait au bord de la colonne
            rows.Add(sel);
            focus.Add(plate);
            return sel;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Dégradé horizontal (texture 256 × 1) à partir de couleurs posées.</summary>
        static Texture2D Gradient((float at, Color c)[] stops)
        {
            var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                int k = 0;
                while (k < stops.Length - 2 && t > stops[k + 1].at) k++;
                float span = Mathf.Max(0.0001f, stops[k + 1].at - stops[k].at);
                px[i] = Color.Lerp(stops[k].c, stops[k + 1].c, Mathf.Clamp01((t - stops[k].at) / span));
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }

    /// <summary>Soleil (disque et rayons) dessiné à la place du pictogramme de la tuile Beau temps.</summary>
    public class SunGlyph : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var g = graphic;
            vh.Clear();
            Rect r = g.rectTransform.rect;
            Vector2 c = r.center;
            float s = Mathf.Min(r.width, r.height) * 0.5f;
            Color col = g.color;
            GlyphDraw.Disc(vh, c, s * 0.36f, col);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                GlyphDraw.Bar(vh, c + d * s * 0.56f, c + d * s * 0.86f, s * 0.09f, col);
            }
        }
    }

    /// <summary>Nuage et éclair dessinés à la place du pictogramme de la tuile Orage.</summary>
    public class StormGlyph : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var g = graphic;
            vh.Clear();
            Rect r = g.rectTransform.rect;
            Vector2 c = r.center;
            float s = Mathf.Min(r.width, r.height) * 0.5f;
            Color col = g.color;
            GlyphDraw.Disc(vh, c + new Vector2(-0.3f, 0.12f) * s, s * 0.3f, col);
            GlyphDraw.Disc(vh, c + new Vector2(0.08f, 0.3f) * s, s * 0.4f, col);
            GlyphDraw.Disc(vh, c + new Vector2(0.45f, 0.08f) * s, s * 0.28f, col);
            GlyphDraw.Quad(vh, c + new Vector2(-0.6f, -0.15f) * s, c + new Vector2(0.72f, -0.15f) * s, c + new Vector2(0.72f, 0.12f) * s, c + new Vector2(-0.6f, 0.12f) * s, col);
            GlyphDraw.Quad(vh, c + new Vector2(0.02f, -0.22f) * s, c + new Vector2(0.24f, -0.22f) * s, c + new Vector2(0.06f, -0.55f) * s, c + new Vector2(-0.12f, -0.55f) * s, col);
            GlyphDraw.Quad(vh, c + new Vector2(-0.12f, -0.55f) * s, c + new Vector2(0.18f, -0.55f) * s, c + new Vector2(-0.02f, -0.95f) * s, c + new Vector2(-0.02f, -0.95f) * s, col);
        }
    }

    static class GlyphDraw
    {
        public static void Disc(VertexHelper vh, Vector2 c, float r, Color col)
        {
            int start = vh.currentVertCount;
            vh.AddVert(c, col, Vector4.zero);
            const int n = 28;
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, col, Vector4.zero);
            }
            for (int i = 0; i < n; i++) vh.AddTriangle(start, start + 1 + i, start + 2 + i);
        }

        public static void Bar(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col)
        {
            var n = (b - a).normalized;
            var p = new Vector2(-n.y, n.x) * w;
            Quad(vh, a - p, b - p, b + p, a + p, col);
        }

        public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
        {
            int s = vh.currentVertCount;
            vh.AddVert(a, col, Vector4.zero);
            vh.AddVert(b, col, Vector4.zero);
            vh.AddVert(c, col, Vector4.zero);
            vh.AddVert(d, col, Vector4.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }
    }
}
