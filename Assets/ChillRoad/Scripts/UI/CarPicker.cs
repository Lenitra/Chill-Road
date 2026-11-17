using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Garage du joueur, ouvert depuis le menu pause (tuile « Voiture ») : ses voitures alignées sur leurs plateaux tournants
    /// dans son garage-chalet à baie vitrée (<see cref="ShowroomStage"/>, variante garage), la voiture regardée tourne au centre de l'écran.
    /// En bas, une rangée de tuiles (vignette rendue de chaque voiture, nom, « au volant ») ; au-dessus, le nom, la
    /// transmission et la fiche technique de la voiture regardée.
    /// ← → (ou molette, flèches à l'écran) pour passer d'une voiture à l'autre, Entrée / A (ou un deuxième clic sur la
    /// tuile) pour prendre le volant et repartir, Échap / B pour revenir au menu pause.
    /// </summary>
    public class CarPicker : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Gold = new Color(0.95f, 0.62f, 0.12f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color Money = new Color(0.5f, 1f, 0.6f);
        static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        static readonly Color TilePlate = new Color(0.1f, 0.12f, 0.15f, 0.94f);
        static readonly Color PanelPlate = new Color(0.025f, 0.03f, 0.045f, 0.8f);

        const float Slope = 0.14f;
        const float TileW = 236f, TileH = 158f, TileGap = 16f, StripY = 26f;
        const float ThumbH = 116f;
        const float InfoH = 214f, CardH = 214f, PanelY = StripY + TileH + 26f;

        public static bool IsOpen { get; private set; }
        /// <summary>Image où l'écran s'est ouvert ou fermé (la touche de fermeture ne doit pas rouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;

        class Tile
        {
            public Button button;
            public RectTransform rt;
            public HudShape plate, bar;
            public RawImage thumb;
            public RenderTexture texture;
            public Text name, badge;
            public GameObject badgeGo;
            public float lift;
        }

        GameManager game;
        Font font;
        GameObject root;
        ShowroomStage stage;
        RectTransform screenRt, viewRt, contentRt, infoRt, cardRt;
        Text countText, nameText, subText, stateText, hintText, actionText;
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<(RectTransform fill, Text value)> bars = new List<(RectTransform, Text)>();
        /// <summary>order[k] = indice (dans le garage) de la voiture à la place k ; builtFor : rangée construite.</summary>
        int[] order = new int[0];
        string builtFor;
        int shown = -1, openedFrame, thumbNext = -1, thumbWait;
        float scroll, scrollVel, nameAnim = 1f;
        float maxPower, maxTop, maxGrip, maxRatio;
        // Clic sur une tuile : le premier la regarde, le second (sur la tuile déjà regardée) prend le volant
        bool pointerPending, pointerArmed;
        System.Action<bool> onClose;

        public void Init(GameManager g)
        {
            game = g;
            font = HudArt.Font;
        }

        void OnDestroy()
        {
            IsOpen = false;
            foreach (var t in tiles) if (t.texture) { t.texture.Release(); Destroy(t.texture); }
        }

        /// <summary>Ouvre le garage ; à la fermeture, onClosed(vrai) si une voiture a été prise (on repart), faux pour revenir.</summary>
        public void Open(System.Action<bool> onClosed)
        {
            if (IsOpen) return;
            onClose = onClosed;
            var cars = game.assets.playerCars;
            var owned = new List<int>();
            for (int i = 0; i < cars.Length; i++) if (Wallet.Owns(cars[i])) owned.Add(i);
            owned.Sort((a, b) => Wallet.Price(cars[a]) != Wallet.Price(cars[b]) ? Wallet.Price(cars[a]).CompareTo(Wallet.Price(cars[b])) : a.CompareTo(b));
            string key = string.Join(",", owned);
            if (key != builtFor) Build(owned.ToArray(), key);

            IsOpen = true;
            root.SetActive(true);
            openedFrame = ChangedFrame = Time.frameCount;
            stage.Repaint();
            stage.Show();
            int current = Mathf.Max(0, System.Array.IndexOf(order, game.Local.CarIndex));
            shown = current;
            stage.Select(current, true);
            scroll = TargetScroll(current);
            scrollVel = 0f;
            nameAnim = 0f;
            pointerPending = false;
            foreach (var t in tiles) t.lift = 0f;
            // Vignettes : rendues une à une dès l'image suivante (l'éclairage du studio est alors en place)
            thumbNext = 0;
            thumbWait = 1;
            ShopFade.Cut(0.4f);
            EventSystem.current?.SetSelectedGameObject(tiles[current].button.gameObject);
            Refresh(0f);
        }

        void Close(bool chosen)
        {
            if (!IsOpen) return;
            IsOpen = false;
            ChangedFrame = Time.frameCount;
            root.SetActive(false);
            stage.Hide();
            ShopFade.Cut(0.35f);
            EventSystem.current?.SetSelectedGameObject(null);
            var cb = onClose;
            onClose = null;
            cb?.Invoke(chosen);
        }

        void Update()
        {
            if (!IsOpen) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (Time.frameCount != openedFrame && (GameInput.CancelPressed || GameInput.PausePressed)) { Close(false); return; }

            // Vignettes manquantes : deux par image
            if (thumbNext >= 0)
            {
                if (thumbWait > 0) thumbWait--;
                else
                    for (int n = 0; n < 2 && thumbNext >= 0; n++)
                    {
                        stage.RenderThumb(thumbNext, tiles[thumbNext].texture);
                        tiles[thumbNext].thumb.color = Color.white;
                        if (++thumbNext >= tiles.Count) thumbNext = -1;
                    }
            }

            // Toujours une voiture regardée : la tuile sélectionnée (manette, clavier, souris)
            var es = EventSystem.current;
            if (es)
            {
                int selected = -1;
                for (int i = 0; i < tiles.Count; i++)
                    if (es.currentSelectedGameObject == tiles[i].button.gameObject) selected = i;
                if (selected < 0) es.SetSelectedGameObject(tiles[Mathf.Clamp(shown, 0, tiles.Count - 1)].button.gameObject);
                else if (selected != shown) Look(selected);
            }

            // Molette : voiture voisine
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0.1f) Step(-1);
                else if (wheel < -0.1f) Step(1);
            }

            // Podium tourné à la main : joystick droit, ou clic droit maintenu
            Vector2 stick = GameInput.LookStick;
            float spin = stick.x * 150f * dt, lift = stick.y * 1.4f * dt;
            if (GameInput.MouseLookHeld)
            {
                Vector2 d = GameInput.MouseDelta;
                spin += d.x * 0.35f;
                lift += d.y * 0.006f;
            }
            stage.Spin(-spin, lift);
            nameAnim = Mathf.MoveTowards(nameAnim, 1f, dt * 4f);
            Refresh(dt);
        }

        void Look(int k)
        {
            shown = k;
            nameAnim = 0f;
            stage.Select(k, false);
        }

        /// <summary>Voiture voisine de la rangée, en boucle.</summary>
        void Step(int delta)
        {
            if (tiles.Count == 0) return;
            int k = ((shown + delta) % tiles.Count + tiles.Count) % tiles.Count;
            EventSystem.current?.SetSelectedGameObject(tiles[k].button.gameObject);
        }

        /// <summary>Tuile validée : clavier et manette prennent le volant ; à la souris, seulement la tuile déjà regardée.</summary>
        void Clicked(int k)
        {
            bool pointer = pointerPending, armed = pointerArmed;
            pointerPending = false;
            if (pointer && !armed) return;
            Choose(k);
        }

        void Choose(int k)
        {
            // L'appui qui a ouvert l'écran ne doit pas valider une voiture
            if (Time.frameCount <= openedFrame + 1 || k < 0 || k >= order.Length) return;
            if (k != shown) { EventSystem.current?.SetSelectedGameObject(tiles[k].button.gameObject); return; }
            int car = order[k];
            if (game.Local.CarIndex != car)
            {
                if (game.Races && game.Races.CarLocked) return;
                game.SelectCar(car);
            }
            Close(true);
        }

        // ------------------------------------------------------------------ affichage

        float TargetScroll(int k)
        {
            float width = viewRt.rect.width, total = tiles.Count * (TileW + TileGap) - TileGap;
            if (total <= width - 40f) return (width - total) * 0.5f;
            float x = width * 0.5f - (k * (TileW + TileGap) + TileW * 0.5f);
            return Mathf.Clamp(x, width - total - 40f, 40f);
        }

        void Refresh(float dt)
        {
            var cars = game.assets.playerCars;
            float fit = screenRt.rect.width < 1f ? 1f : Mathf.Min(1f, Mathf.Min(screenRt.rect.width / 1500f, screenRt.rect.height / 820f));
            infoRt.localScale = cardRt.localScale = Vector3.one * fit;
            bool pad = GameInput.UsingGamepad;
            string ok = pad ? "A" : Loc.T("Entrée", "Enter");

            // Rangée : la tuile regardée se lève et passe à l'or, la rangée défile pour la garder à l'écran
            scroll = dt > 0f ? Mathf.SmoothDamp(scroll, TargetScroll(shown), ref scrollVel, 0.18f, Mathf.Infinity, dt) : TargetScroll(shown);
            contentRt.anchoredPosition = new Vector2(scroll, 16f);
            float ease = dt > 0f ? 1f - Mathf.Exp(-dt * 14f) : 1f;
            for (int k = 0; k < tiles.Count; k++)
            {
                var t = tiles[k];
                bool on = k == shown, driving = order[k] == game.Local.CarIndex;
                t.lift = Mathf.Lerp(t.lift, on ? 1f : 0f, ease);
                t.rt.anchoredPosition = new Vector2(k * (TileW + TileGap) + TileW * 0.5f, t.lift * 12f);
                t.rt.localScale = Vector3.one * (1f + t.lift * 0.04f);
                t.plate.color = Color.Lerp(TilePlate, Gold, t.lift);
                t.name.color = Color.Lerp(White, Ink, t.lift);
                t.bar.color = driving ? Money : new Color(1f, 1f, 1f, 0f);
                t.badgeGo.SetActive(driving);
            }

            countText.text = Loc.F($"{shown + 1} / {tiles.Count}  ·  {tiles.Count} voitures au garage", $"{shown + 1} / {tiles.Count}  ·  {tiles.Count} cars owned");
            if (shown < 0 || shown >= order.Length) return;
            var basic = cars[order[shown]];
            var s = CarTuning.Apply(basic);
            nameText.text = basic.displayName;
            float e = 1f - (1f - nameAnim) * (1f - nameAnim);
            nameText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.07f, 1f, e);
            nameText.color = new Color(White.r, White.g, White.b, e);
            string drive = s.drivetrain == Drivetrain.AWD ? Loc.T("Intégrale", "AWD")
                : s.drivetrain == Drivetrain.FWD ? Loc.T("Traction", "FWD") : Loc.T("Propulsion", "RWD");
            int gears = s.gearRatios != null ? s.gearRatios.Length : 0;
            subText.text = Loc.F($"{drive}  ·  {gears} rapports  ·  {s.mass:N0} kg", $"{drive}  ·  {gears} speeds  ·  {s.mass:N0} kg");
            bool driving2 = order[shown] == game.Local.CarIndex;
            stateText.text = driving2 ? Loc.T("AU VOLANT", "DRIVING") : Loc.T("AU GARAGE", "IN THE GARAGE");
            stateText.color = driving2 ? Money : Accent;
            actionText.text = driving2 ? Loc.T($"{ok} : repartir avec", $"{ok}: drive on") : Loc.T($"{ok} : prendre le volant", $"{ok}: drive it");

            float power = Power(s), top = s.TopSpeed * 3.6f, ratio = power / Mathf.Max(s.mass, 1f) * 1000f;
            SetBar(0, power / maxPower, Loc.F($"{power:0} ch", $"{power:0} hp"));
            SetBar(1, top / maxTop, Loc.F($"{top:0} km/h"));
            SetBar(2, ratio / maxRatio, Loc.F($"{ratio:0} ch/t", $"{ratio:0} hp/t"));
            SetBar(3, s.tireGrip / maxGrip, Loc.F($"{s.tireGrip:0.00}"));

            hintText.text = pad
                ? Loc.T("← → choisir  ·  A conduire  ·  B retour  ·  joystick droit : tourner", "← → browse  ·  A drive  ·  B back  ·  right stick: rotate")
                : Loc.T("← → / molette choisir  ·  Entrée conduire  ·  Échap retour  ·  clic droit : tourner", "← → / wheel browse  ·  Enter drive  ·  Esc back  ·  right click: rotate");
        }

        void SetBar(int i, float v, string value)
        {
            bars[i].fill.localScale = new Vector3(Mathf.Clamp01(v), 1f, 1f);
            bars[i].value.text = value;
        }

        /// <summary>Puissance estimée (ch) : couple max tenu jusqu'à 80 % du rupteur (comme à la concession).</summary>
        static float Power(VehicleSpec s) => s.maxTorque * s.redline * 0.8f / 9549f * 1.36f;

        // ------------------------------------------------------------------ construction

        /// <summary>Hall et interface pour cette rangée de voitures (reconstruits quand le garage change).</summary>
        void Build(int[] owned, string key)
        {
            if (stage) Destroy(stage);
            if (root) Destroy(root);
            foreach (var t in tiles) if (t.texture) { t.texture.Release(); Destroy(t.texture); }
            tiles.Clear();
            bars.Clear();
            order = owned;
            builtFor = key;

            var cars = game.assets.playerCars;
            maxPower = maxTop = maxGrip = maxRatio = 0.01f;
            foreach (var c0 in cars)
            {
                var c = CarTuning.Apply(c0);
                maxPower = Mathf.Max(maxPower, Power(c));
                maxTop = Mathf.Max(maxTop, c.TopSpeed * 3.6f);
                maxGrip = Mathf.Max(maxGrip, c.tireGrip);
                maxRatio = Mathf.Max(maxRatio, Power(c) / Mathf.Max(c.mass, 1f) * 1000f);
            }

            stage = gameObject.AddComponent<ShowroomStage>();
            stage.lookDrop = 0.12f;   // interface du bas plus haute qu'à la concession : la voiture monte
            stage.Build(game, order, garage: true);

            root = new GameObject("CarPicker", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            screenRt = (RectTransform)root.transform;

            // Bas d'écran assombri : la rangée et les fiches restent lisibles sur le sol clair du hall
            var shadeShape = Shape(screenRt, "Shade", Vector2.zero, Vector2.zero, Vector2.zero, new Color(0.01f, 0.012f, 0.02f, 1f));
            shadeShape.Set(0f, 0f, 0f, 0f, 0.85f);
            var shade = shadeShape.rectTransform;
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = new Vector2(1f, 0f);
            shade.offsetMin = Vector2.zero;
            shade.offsetMax = new Vector2(0f, PanelY + InfoH + 60f);

            // Enseigne (en haut à gauche) : titre, nombre de voitures, aide des touches
            const float headH = 132f;
            var head = Panel(screenRt, "Head", new Vector2(0f, 1f), new Vector2(40f, -32f), new Vector2(720f, headH));
            Edge(head, headH);
            var icon = Icon(head, HudIcon.Kind.Car, new Vector2(0f, 1f), new Vector2(38f + Lean(headH - 46f, headH), -46f), 64f);
            icon.SetColors(Accent, PanelPlate);
            var title = Place(Label(head, "", 50, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), new Vector2(120f, -12f), new Vector2(560f, 64f), headH);
            Loc.Bind(title, "GARAGE", "GARAGE");
            countText = Place(Label(head, "", 22, TextAnchor.MiddleLeft, Dim, FontStyle.Bold), new Vector2(122f, -78f), new Vector2(560f, 30f), headH);

            // Retour (souris, doigt) : hors de la navigation
            var back = Plate(screenRt, "Back", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -32f), new Vector2(230f, 60f), () => Close(false));
            var backIcon = Icon(back.rt, HudIcon.Kind.Back, new Vector2(0f, 0.5f), new Vector2(46f, 0f), 30f);
            backIcon.SetColors(White, Color.clear);
            back.label.alignment = TextAnchor.MiddleLeft;
            back.label.rectTransform.offsetMin = new Vector2(78f, 0f);
            Loc.Bind(back.label, "Retour", "Back");
            hintText = Label(screenRt, "", 19, TextAnchor.MiddleRight, Dim, FontStyle.Bold);
            var hr = hintText.rectTransform;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1f, 1f);
            hr.sizeDelta = new Vector2(900f, 30f);
            hr.anchoredPosition = new Vector2(-44f, -104f);

            // Flèches de la rangée (souris, doigt), de part et d'autre de la voiture
            var prev = Plate(screenRt, "Previous", new Vector2(0f, 0.62f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(64f, 96f), () => Step(-1));
            prev.label.text = "‹";
            prev.label.fontSize = 64;
            var next = Plate(screenRt, "Next", new Vector2(1f, 0.62f), new Vector2(1f, 0.5f), new Vector2(-34f, 0f), new Vector2(64f, 96f), () => Step(1));
            next.label.text = "›";
            next.label.fontSize = 64;

            // Voiture regardée (à gauche, au-dessus de la rangée) : nom, fiche courte, état, action
            infoRt = Panel(screenRt, "Car", new Vector2(0f, 0f), new Vector2(40f, PanelY), new Vector2(720f, InfoH));
            Edge(infoRt, InfoH);
            nameText = Low(Label(infoRt, "", 48, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), 36f, 164f, 660f, 62f, InfoH);
            subText = Low(Label(infoRt, "", 22, TextAnchor.MiddleLeft, Dim, FontStyle.Bold), 38f, 116f, 660f, 30f, InfoH);
            stateText = Low(Label(infoRt, "", 22, TextAnchor.MiddleLeft, Money, FontStyle.BoldAndItalic), 38f, 46f, 200f, 30f, InfoH);
            var action = Plate(infoRt, "Action", Vector2.zero, new Vector2(0f, 0.5f), new Vector2(238f + Lean(46f, InfoH), 46f), new Vector2(440f, 58f), () => Choose(shown));
            actionText = action.label;
            actionText.fontSize = 24;

            // Fiche technique (à droite, au-dessus de la rangée)
            cardRt = Panel(screenRt, "Card", new Vector2(1f, 0f), new Vector2(-40f, PanelY), new Vector2(620f, CardH));
            Edge(cardRt, CardH);
            var cardTitle = Place(Label(cardRt, "", 22, TextAnchor.MiddleLeft, Info, FontStyle.BoldAndItalic), new Vector2(34f, -14f), new Vector2(540f, 30f), CardH);
            Loc.Bind(cardTitle, "FICHE TECHNIQUE", "SPECS");
            string[,] labels = { { "Puissance", "Power" }, { "Vitesse max", "Top speed" }, { "Rapport poids/puissance", "Power to weight" }, { "Adhérence", "Grip" } };
            for (int i = 0; i < 4; i++)
            {
                float y = -64f - i * 38f;
                var label = Place(Label(cardRt, "", 20, TextAnchor.MiddleLeft, White, FontStyle.Normal), new Vector2(34f, y), new Vector2(250f, 28f), CardH);
                Loc.Bind(label, labels[i, 0], labels[i, 1]);
                float lean = Lean(CardH + y - 14f, CardH);
                var bg = Rect(cardRt, "Bar", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(286f + lean, y - 18f), new Vector2(462f + lean, y - 10f));
                bg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
                var fill = Rect(bg, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.gameObject.AddComponent<Image>().color = Info;
                var value = Place(Label(cardRt, "", 20, TextAnchor.MiddleRight, Accent, FontStyle.Bold), new Vector2(466f, y), new Vector2(124f, 28f), CardH);
                bars.Add((fill, value));
            }

            // Rangée des voitures (en bas, sur toute la largeur), qui défile
            viewRt = Rect(screenRt, "Strip", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, StripY - 16f), new Vector2(0f, StripY + TileH + 22f));
            viewRt.gameObject.AddComponent<RectMask2D>();
            contentRt = Rect(viewRt, "Row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            contentRt.pivot = Vector2.zero;
            contentRt.sizeDelta = new Vector2(owned.Length * (TileW + TileGap), TileH);
            contentRt.anchoredPosition = new Vector2(0f, 16f);
            for (int k = 0; k < owned.Length; k++) tiles.Add(BuildTile(k, cars[owned[k]]));
            for (int k = 0; k < tiles.Count; k++)
                tiles[k].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = tiles[(k - 1 + tiles.Count) % tiles.Count].button,
                    selectOnRight = tiles[(k + 1) % tiles.Count].button,
                };
            root.SetActive(false);
        }

        Tile BuildTile(int k, VehicleSpec spec)
        {
            var t = new Tile();
            var go = new GameObject("Tile " + spec.id, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button));
            go.transform.SetParent(contentRt, false);
            t.rt = (RectTransform)go.transform;
            t.rt.anchorMin = t.rt.anchorMax = Vector2.zero;
            t.rt.pivot = new Vector2(0.5f, 0f);
            t.rt.sizeDelta = new Vector2(TileW, TileH);
            t.rt.anchoredPosition = new Vector2(k * (TileW + TileGap) + TileW * 0.5f, 0f);
            t.plate = go.GetComponent<HudShape>();
            t.plate.Set(TileH * Slope * 0.5f, 0f, 0f);
            t.plate.color = TilePlate;
            t.button = go.GetComponent<Button>();
            t.button.targetGraphic = t.plate;
            t.button.transition = Selectable.Transition.None;
            int index = k;
            t.button.onClick.AddListener(() => Clicked(index));
            // Appui à la souris ou au doigt : noter si la tuile était déjà regardée avant que la sélection ne change
            var trigger = go.AddComponent<EventTrigger>();
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => { pointerPending = true; pointerArmed = shown == index; });
            trigger.triggers.Add(down);

            // Vignette (rendue à l'ouverture), nom dessous
            t.texture = new RenderTexture(472, 232, 24, RenderTextureFormat.DefaultHDR) { name = "CarThumb " + spec.id };
            t.texture.Create();
            var img = new GameObject("Thumb", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            img.transform.SetParent(go.transform, false);
            var ir = (RectTransform)img.transform;
            ir.anchorMin = new Vector2(0f, 1f);
            ir.anchorMax = new Vector2(1f, 1f);
            ir.pivot = new Vector2(0.5f, 1f);
            ir.offsetMin = new Vector2(14f, -8f - ThumbH);
            ir.offsetMax = new Vector2(-14f, -8f);
            t.thumb = img.GetComponent<RawImage>();
            t.thumb.texture = t.texture;
            t.thumb.color = new Color(0f, 0f, 0f, 0.4f);
            t.thumb.raycastTarget = false;
            t.name = Label(go.transform, spec.displayName, 19, TextAnchor.MiddleCenter, White, FontStyle.BoldAndItalic);
            var nr = t.name.rectTransform;
            nr.anchorMin = Vector2.zero;
            nr.anchorMax = new Vector2(1f, 0f);
            nr.pivot = new Vector2(0.5f, 0f);
            nr.offsetMin = new Vector2(10f, 4f);
            nr.offsetMax = new Vector2(-10f, 32f);
            t.name.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.name.verticalOverflow = VerticalWrapMode.Truncate;
            t.name.resizeTextForBestFit = true;
            t.name.resizeTextMinSize = 13;
            t.name.resizeTextMaxSize = 19;
            t.name.GetComponent<Shadow>().enabled = false;
            // Liseré vert sous la voiture au volant, et son badge sur la vignette
            t.bar = Shape(go.transform, "Driving", new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(TileW * 0.6f, 4f), Money);
            t.bar.Set(2f, 0f, 0f);
            t.badgeGo = new GameObject("Badge", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape));
            t.badgeGo.transform.SetParent(go.transform, false);
            var br = (RectTransform)t.badgeGo.transform;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(0f, 1f);
            br.sizeDelta = new Vector2(112f, 26f);
            br.anchoredPosition = new Vector2(20f, -14f);
            var bs = t.badgeGo.GetComponent<HudShape>();
            bs.Set(26f * Slope, 0f, 0f);
            bs.color = Money;
            bs.raycastTarget = false;
            t.badge = Label(t.badgeGo.transform, "", 15, TextAnchor.MiddleCenter, Ink, FontStyle.BoldAndItalic);
            Stretch(t.badge.rectTransform, 4f, 4f);
            t.badge.GetComponent<Shadow>().enabled = false;
            Loc.Bind(t.badge, "AU VOLANT", "DRIVING");
            return t;
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Panel(Transform parent, string name, Vector2 corner, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, corner, corner, Vector2.zero, Vector2.zero);
            rt.pivot = corner;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var s = Shape(rt, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PanelPlate);
            s.Set(size.y * Slope, 0f, 0f);
            var pr = s.rectTransform;
            pr.anchorMin = Vector2.zero;
            pr.anchorMax = Vector2.one;
            pr.offsetMin = pr.offsetMax = Vector2.zero;
            pr.SetAsFirstSibling();
            return rt;
        }

        /// <summary>Décalage horizontal du bord penché d'un panneau à la hauteur y (depuis le bas) : 0 au milieu.</summary>
        static float Lean(float y, float panelHeight) => Slope * (y - panelHeight * 0.5f);

        static void Edge(RectTransform panel, float height)
        {
            Shape(panel, "Stripe", Vector2.zero, Vector2.zero, new Vector2(4f, height), new Color(Info.r, Info.g, Info.b, 0.7f)).Set(height * Slope, 0f, 0f);
        }

        static HudIcon Icon(Transform parent, HudIcon.Kind kind, Vector2 anchor, Vector2 pos, float size)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            var icon = go.GetComponent<HudIcon>();
            icon.Shape = kind;
            icon.raycastTarget = false;
            return icon;
        }

        /// <summary>Bouton à plaque inclinée (souris, doigt), hors de la navigation au clavier et à la manette.</summary>
        (RectTransform rt, Text label) Plate(Transform parent, string name, Vector2 corner, Vector2 pivot, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var shape = go.GetComponent<HudShape>();
            shape.Set(size.y * Slope, 0f, 0f);
            shape.color = Color.white;
            var b = go.GetComponent<Button>();
            b.targetGraphic = shape;
            var colors = b.colors;
            colors.normalColor = new Color(0.14f, 0.16f, 0.2f, 0.92f);
            colors.highlightedColor = new Color(0.2f, 0.42f, 0.55f, 1f);
            colors.selectedColor = new Color(0.14f, 0.16f, 0.2f, 0.92f);
            colors.pressedColor = Info;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(onClick);
            var label = Label(go.transform, "", 26, TextAnchor.MiddleCenter, White, FontStyle.BoldAndItalic);
            Stretch(label.rectTransform, 16f, 16f);
            return (rt, label);
        }

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

        /// <summary>Texte posé depuis le haut à gauche d'un panneau de hauteur panelHeight : sa marge suit le bord penché.</summary>
        static Text Place(Text t, Vector2 pos, Vector2 size, float panelHeight)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = size;
            float lean = Lean(panelHeight + pos.y - size.y * 0.5f, panelHeight);
            rt.anchoredPosition = pos + new Vector2(lean, -size.y * 0.5f);
            return t;
        }

        /// <summary>Texte posé depuis le bas à gauche d'un panneau (y : milieu de la ligne) ; sa marge suit le bord penché.</summary>
        static Text Low(Text t, float x, float y, float width, float height, float panelHeight)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x + Lean(y, panelHeight), y);
            return t;
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
    }
}
