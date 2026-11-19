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
    /// Style verre : en haut à gauche, « GARAGE · n / N », le nom en grand et les puces (état, transmission) ; en haut à
    /// droite, la fiche technique ; en bas, un plateau de verre avec les vignettes des voitures et ses flèches.
    /// ← → (ou molette, flèches à l'écran) pour passer d'une voiture à l'autre, Entrée / A (ou un deuxième clic sur la
    /// tuile) pour prendre le volant et repartir, Échap / B pour revenir au menu pause.
    /// </summary>
    public class CarPicker : MonoBehaviour
    {
        const float ThumbW = 240f, ThumbH = 135f, NameH = 32f;
        const float TileW = ThumbW, TileH = ThumbH + 9f + NameH, TileGap = 21f;
        const float TrayY = 96f, TrayPadTop = 24f, TrayPadBottom = 18f, TrayH = TrayPadTop + TileH + TrayPadBottom;
        const float ViewInset = 89f, ViewPad = 6f;

        public static bool IsOpen { get; private set; }
        /// <summary>Image où l'écran s'est ouvert ou fermé (la touche de fermeture ne doit pas rouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;

        class Tile
        {
            public Button button;
            public RectTransform rt;
            public GlassRect frame;
            public RawImage thumb;
            public RenderTexture texture;
            public Text name;
            public GameObject badgeGo;
            public float lift, drawn = -1f;
        }

        /// <summary>Bouton pilule (souris, doigt) : liseré doré au survol.</summary>
        class Pill
        {
            public RectTransform rt;
            public GlassRect plate;
            public Text label;
            public int hover = -1;
        }

        GameManager game;
        GameObject root;
        ShowroomStage stage;
        RectTransform screenRt, viewRt, contentRt, headRt, cardRt;
        Text countText, nameText, subText, stateText, driveText, hintText;
        GlassRect stateChip, driveChip;
        Pill action, back;
        Text prevText, nextText;
        RectTransform prevRt, nextRt;
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
            foreach (var t in tiles) { t.lift = 0f; t.drawn = -1f; }
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
            if (total <= width - 24f) return (width - total) * 0.5f;
            float x = width * 0.5f - (k * (TileW + TileGap) + TileW * 0.5f);
            return Mathf.Clamp(x, width - total - 12f, 12f);
        }

        void Refresh(float dt)
        {
            var cars = game.assets.playerCars;
            // Écrans plus étroits que 16:9 : l'en-tête et la fiche rétrécissent pour ne pas se chevaucher
            float fit = screenRt.rect.width < 1f ? 1f : Mathf.Min(1f, Mathf.Min(screenRt.rect.width / 1700f, screenRt.rect.height / 900f));
            headRt.localScale = cardRt.localScale = back.rt.localScale = Vector3.one * fit;
            bool pad = GameInput.UsingGamepad;
            string ok = pad ? "A" : Loc.T("Entrée", "Enter");

            // Rangée : la vignette regardée prend le liseré doré et son halo, la rangée défile pour la garder à l'écran
            scroll = dt > 0f ? Mathf.SmoothDamp(scroll, TargetScroll(shown), ref scrollVel, 0.18f, Mathf.Infinity, dt) : TargetScroll(shown);
            contentRt.anchoredPosition = new Vector2(scroll, TrayPadBottom - ViewPad);
            float ease = dt > 0f ? 1f - Mathf.Exp(-dt * 14f) : 1f;
            for (int k = 0; k < tiles.Count; k++)
            {
                var t = tiles[k];
                bool on = k == shown, driving = order[k] == game.Local.CarIndex;
                t.lift = Mathf.Lerp(t.lift, on ? 1f : 0f, ease);
                t.rt.anchoredPosition = new Vector2(k * (TileW + TileGap) + TileW * 0.5f, 0f);
                t.rt.localScale = Vector3.one * (1f + t.lift * 0.03f);
                // Maillage refait seulement pendant la transition
                if (Mathf.Abs(t.lift - t.drawn) > 0.002f)
                {
                    t.drawn = t.lift;
                    t.frame.Set(Color.clear, Color.Lerp(Glass.Edge, Glass.Gold, t.lift), Mathf.Lerp(1.5f, 3f, t.lift), 9f * t.lift, Glass.A(Glass.GoldHalo, Glass.GoldHalo.a * t.lift));
                }
                t.name.color = on ? Glass.Gold : Glass.Dim;
                t.name.font = Glass.FontOf(on ? Glass.Weight.Regular : Glass.Weight.Light);
                t.badgeGo.SetActive(driving);
            }

            // Flèches : dorées tant qu'il reste des voitures de ce côté
            prevText.color = Hover(prevRt) ? Glass.White : shown > 0 ? Glass.Gold : Glass.Faint;
            nextText.color = Hover(nextRt) ? Glass.White : shown < tiles.Count - 1 ? Glass.Gold : Glass.Faint;
            HoverPill(back);
            HoverPill(action);

            countText.text = Glass.Spaced(Loc.F($"GARAGE · {shown + 1} / {tiles.Count}"), 2);
            if (shown < 0 || shown >= order.Length) return;
            var basic = cars[order[shown]];
            var s = CarTuning.Apply(basic);
            nameText.text = basic.displayName;
            float e = 1f - (1f - nameAnim) * (1f - nameAnim);
            nameText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.05f, 1f, e);
            nameText.color = Glass.A(Glass.White, e);

            // Puces : état (doré), transmission (verre), puis la fiche courte
            bool driving2 = order[shown] == game.Local.CarIndex;
            stateText.text = Glass.Spaced(driving2 ? Loc.T("AU VOLANT", "DRIVING") : Loc.T("AU GARAGE", "IN THE GARAGE"), 1);
            stateText.color = driving2 ? Glass.Ink : Glass.Gold;
            stateChip.Set(driving2 ? Glass.Gold : Glass.GoldSoft, driving2 ? Color.clear : Glass.Gold, driving2 ? 0f : 1.5f);
            float sw = stateText.preferredWidth + 54f;
            stateChip.rectTransform.sizeDelta = new Vector2(sw, 38f);
            string drive = s.drivetrain == Drivetrain.AWD ? Loc.T("Intégrale", "AWD")
                : s.drivetrain == Drivetrain.FWD ? Loc.T("Traction", "FWD") : Loc.T("Propulsion", "RWD");
            driveText.text = drive;
            float dw = driveText.preferredWidth + 54f;
            var dr = driveChip.rectTransform;
            dr.anchoredPosition = new Vector2(sw + 15f, dr.anchoredPosition.y);
            dr.sizeDelta = new Vector2(dw, 38f);
            int gears = s.gearRatios != null ? s.gearRatios.Length : 0;
            subText.text = Loc.F($"{gears} rapports  ·  {s.mass:N0} kg", $"{gears} speeds  ·  {s.mass:N0} kg");
            var sr = subText.rectTransform;
            sr.anchoredPosition = new Vector2(sw + 15f + dw + 20f, sr.anchoredPosition.y);
            action.label.text = driving2
                ? Loc.T($"<color=#FFC233>{ok}</color>   repartir avec", $"<color=#FFC233>{ok}</color>   drive on")
                : Loc.T($"<color=#FFC233>{ok}</color>   prendre le volant", $"<color=#FFC233>{ok}</color>   drive it");
            action.rt.sizeDelta = new Vector2(action.label.preferredWidth + 64f, 60f);

            float power = Power(s), top = s.TopSpeed * 3.6f, ratio = power / Mathf.Max(s.mass, 1f) * 1000f;
            SetBar(0, power / maxPower, Loc.F($"{power:0} ch", $"{power:0} hp"));
            SetBar(1, top / maxTop, Loc.F($"{top:0} km/h"));
            SetBar(2, ratio / maxRatio, Loc.F($"{ratio:0} ch/t", $"{ratio:0} hp/t"));
            SetBar(3, s.tireGrip / maxGrip, Loc.F($"{s.tireGrip:0.00}"));

            const string G = "<color=#FFC233>", E = "</color>";
            hintText.text = pad
                ? Loc.T($"{G}← →{E}  choisir     ·     {G}A{E}  conduire     ·     {G}B{E}  retour     ·     {G}joystick droit{E}  tourner",
                        $"{G}← →{E}  browse     ·     {G}A{E}  drive     ·     {G}B{E}  back     ·     {G}right stick{E}  rotate")
                : Loc.T($"{G}← →{E} / {G}molette{E}  choisir     ·     {G}Entrée{E}  conduire     ·     {G}Échap{E}  retour     ·     {G}clic droit{E}  tourner",
                        $"{G}← →{E} / {G}wheel{E}  browse     ·     {G}Enter{E}  drive     ·     {G}Esc{E}  back     ·     {G}right click{E}  rotate");
        }

        void SetBar(int i, float v, string value)
        {
            bars[i].fill.localScale = new Vector3(Mathf.Clamp01(v), 1f, 1f);
            bars[i].value.text = value;
        }

        /// <summary>Puissance estimée (ch) : couple max tenu jusqu'à 80 % du rupteur (comme à la concession).</summary>
        static float Power(VehicleSpec s) => s.maxTorque * s.redline * 0.8f / 9549f * 1.36f;

        /// <summary>Pointeur de la souris au-dessus du rectangle (pas de survol à la manette).</summary>
        static bool Hover(RectTransform rt)
        {
            var mouse = Mouse.current;
            return mouse != null && !GameInput.UsingGamepad && rt && rt.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse.position.ReadValue(), null);
        }

        static void HoverPill(Pill p)
        {
            bool on = Hover(p.rt);
            if ((on ? 1 : 0) == p.hover) return;
            p.hover = on ? 1 : 0;
            p.plate.Set(on ? Glass.GoldSoft : Glass.Pill, on ? Glass.Gold : Glass.Edge, on ? 2f : 1.5f, on ? 6f : 0f);
        }

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

            // En-tête (en haut à gauche, sur la scène) : « GARAGE · n / N », nom en grand, puces, action
            headRt = Glass.Place(screenRt, "Head", new Vector2(0f, 1f), new Vector2(90f, -66f), new Vector2(1100f, 320f));
            countText = Glass.Kicker(headRt, "", 17);
            Pose(countText.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(900f, 26f));
            Glass.Soft(countText, 0.35f);
            nameText = Glass.Label(headRt, "", 102, Glass.Weight.Light, Glass.White, TextAnchor.UpperLeft);
            Pose(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(-4f, -30f), new Vector2(1100f, 120f));
            Glass.Soft(nameText);

            stateChip = Glass.Make(headRt, "State", 19f, Glass.Gold, 0f);
            Pose(stateChip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -176f), new Vector2(180f, 38f));
            stateText = Glass.Label(stateChip.transform, "", 18, Glass.Weight.Semibold, Glass.Ink, TextAnchor.MiddleCenter);
            Glass.Stretch(stateText.rectTransform);
            driveChip = Glass.Make(headRt, "Drive", 19f, Glass.Panel);
            Pose(driveChip.rectTransform, new Vector2(0f, 1f), new Vector2(195f, -176f), new Vector2(160f, 38f));
            driveText = Glass.Label(driveChip.transform, "", 21, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(driveText.rectTransform);
            subText = Glass.Label(headRt, "", 21, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleLeft);
            Pose(subText.rectTransform, new Vector2(0f, 1f), new Vector2(380f, -176f), new Vector2(500f, 38f));
            Glass.Soft(subText);

            action = MakePill(headRt, "Action", new Vector2(0f, 1f), new Vector2(0f, -240f), new Vector2(340f, 60f), () => Choose(shown));
            action.label.fontSize = 22;

            // Retour (souris, doigt), en haut à droite, hors de la navigation
            back = MakePill(screenRt, "Back", new Vector2(1f, 1f), new Vector2(-90f, -66f), new Vector2(190f, 60f), () => Close(false));
            var backIcon = Icon(back.rt, HudIcon.Kind.Back, new Vector2(0f, 0.5f), new Vector2(42f, 0f), 26f);
            backIcon.SetColors(Glass.White, Color.clear);
            back.label.alignment = TextAnchor.MiddleLeft;
            back.label.rectTransform.offsetMin = new Vector2(70f, 0f);
            Loc.Bind(back.label, "Retour", "Back");

            // Fiche technique (en haut à droite, sous Retour)
            cardRt = Glass.Place(screenRt, "Card", new Vector2(1f, 1f), new Vector2(-90f, -150f), new Vector2(600f, 228f));
            Glass.Backdrop(cardRt, 30f);
            var cardTitle = Glass.Kicker(cardRt, "", 16);
            Pose(cardTitle.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(500f, 24f));
            Loc.Bind(cardTitle, Glass.Spaced("FICHE TECHNIQUE", 2), Glass.Spaced("SPECS", 2));
            string[,] labels = { { "Puissance", "Power" }, { "Vitesse max", "Top speed" }, { "Rapport poids/puissance", "Power to weight" }, { "Adhérence", "Grip" } };
            for (int i = 0; i < 4; i++)
            {
                float y = -80f - i * 38f;
                var label = Glass.Label(cardRt, "", 19, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleLeft);
                Pose(label.rectTransform, new Vector2(0f, 1f), new Vector2(30f, y + 15f), new Vector2(262f, 30f));
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;   // sinon la taille auto ne réduit pas « Rapport poids/puissance »
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = 19;
                Loc.Bind(label, labels[i, 0], labels[i, 1]);
                var track = Glass.Bar(cardRt, "Bar", Glass.Track);
                Pose(track.rectTransform, new Vector2(0f, 1f), new Vector2(306f, y + 1.5f), new Vector2(124f, 3f));
                var fill = Glass.Bar(track.transform, "Fill", Glass.White).rectTransform;
                Glass.Stretch(fill);
                fill.pivot = new Vector2(0f, 0.5f);
                var value = Glass.Label(cardRt, "", 19, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleRight);
                Pose(value.rectTransform, new Vector2(1f, 1f), new Vector2(-30f, y + 15f), new Vector2(130f, 30f));
                bars.Add((fill, value));
            }

            // Plateau de verre en bas : flèches et vignettes des voitures, qui défilent
            var tray = Glass.Rect(screenRt, "Tray", Vector2.zero, new Vector2(1f, 0f), new Vector2(111f, TrayY), new Vector2(-111f, TrayY + TrayH));
            Glass.Backdrop(tray, 45f);
            var prev = MakeArrow(tray, "Previous", "‹", new Vector2(0f, 0.5f), new Vector2(27f, 14f), () => Step(-1));
            prevRt = prev.rt; prevText = prev.label;
            var next = MakeArrow(tray, "Next", "›", new Vector2(1f, 0.5f), new Vector2(-27f, 14f), () => Step(1));
            nextRt = next.rt; nextText = next.label;
            // Fenêtre de défilement un peu plus haute que les tuiles : le halo doré n'est pas rogné
            viewRt = Glass.Rect(tray, "Strip", Vector2.zero, Vector2.one, new Vector2(ViewInset, ViewPad), new Vector2(-ViewInset, -ViewPad));
            viewRt.gameObject.AddComponent<RectMask2D>();
            contentRt = Glass.Rect(viewRt, "Row", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            contentRt.pivot = Vector2.zero;
            contentRt.sizeDelta = new Vector2(owned.Length * (TileW + TileGap), TileH);
            contentRt.anchoredPosition = new Vector2(0f, TrayPadBottom - ViewPad);
            for (int k = 0; k < owned.Length; k++) tiles.Add(BuildTile(k, cars[owned[k]]));
            for (int k = 0; k < tiles.Count; k++)
                tiles[k].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = tiles[(k - 1 + tiles.Count) % tiles.Count].button,
                    selectOnRight = tiles[(k + 1) % tiles.Count].button,
                };

            // Aide des touches, centrée tout en bas (touches en or)
            hintText = Glass.Label(screenRt, "", 21, Glass.Weight.Light, Glass.White, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1800f, 32f));
            Glass.Soft(hintText, 0.8f);
            root.SetActive(false);
        }

        Tile BuildTile(int k, VehicleSpec spec)
        {
            var t = new Tile();
            var go = new GameObject("Tile " + spec.id, typeof(RectTransform), typeof(Button));
            go.transform.SetParent(contentRt, false);
            t.rt = (RectTransform)go.transform;
            t.rt.anchorMin = t.rt.anchorMax = Vector2.zero;
            t.rt.pivot = new Vector2(0.5f, 0f);
            t.rt.sizeDelta = new Vector2(TileW, TileH);
            t.rt.anchoredPosition = new Vector2(k * (TileW + TileGap) + TileW * 0.5f, 0f);

            // Vignette arrondie : un masque de verre (invisible) découpe l'image rendue
            var holder = Glass.Make(go.transform, "Thumb", 24f, Color.white, 0f);
            Pose(holder.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(ThumbW, ThumbH));
            holder.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            t.texture = new RenderTexture(472, 232, 24, RenderTextureFormat.DefaultHDR) { name = "CarThumb " + spec.id };
            t.texture.Create();
            var img = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            img.transform.SetParent(holder.transform, false);
            Glass.Stretch((RectTransform)img.transform);
            t.thumb = img.GetComponent<RawImage>();
            t.thumb.texture = t.texture;
            t.thumb.color = new Color(0f, 0f, 0f, 0.4f);
            t.thumb.raycastTarget = false;

            // Liseré par-dessus la vignette (doré + halo sur la voiture regardée) ; c'est lui qui reçoit les clics
            t.frame = Glass.Make(go.transform, "Frame", 24f, Color.clear);
            Pose(t.frame.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(ThumbW, ThumbH));
            t.frame.raycastTarget = true;
            t.button = go.GetComponent<Button>();
            t.button.targetGraphic = t.frame;
            t.button.transition = Selectable.Transition.None;
            int index = k;
            t.button.onClick.AddListener(() => Clicked(index));
            // Appui à la souris ou au doigt : noter si la tuile était déjà regardée avant que la sélection ne change
            var trigger = go.AddComponent<EventTrigger>();
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => { pointerPending = true; pointerArmed = shown == index; });
            trigger.triggers.Add(down);

            t.name = Glass.Label(go.transform, spec.displayName, 21, Glass.Weight.Light, Glass.Dim, TextAnchor.MiddleCenter);
            var nr = t.name.rectTransform;
            nr.anchorMin = Vector2.zero;
            nr.anchorMax = new Vector2(1f, 0f);
            nr.pivot = new Vector2(0.5f, 0f);
            nr.offsetMin = new Vector2(4f, 0f);
            nr.offsetMax = new Vector2(-4f, NameH);
            t.name.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.name.verticalOverflow = VerticalWrapMode.Truncate;
            t.name.resizeTextForBestFit = true;
            t.name.resizeTextMinSize = 14;
            t.name.resizeTextMaxSize = 21;

            // Pastille « au volant » sur la vignette de la voiture conduite
            var badge = Glass.Make(go.transform, "Badge", 13f, Glass.Gold, 0f);
            Pose(badge.rectTransform, new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(116f, 26f));
            t.badgeGo = badge.gameObject;
            var bt = Glass.Label(badge.transform, "", 13, Glass.Weight.Semibold, Glass.Ink, TextAnchor.MiddleCenter);
            Glass.Stretch(bt.rectTransform);
            Loc.Bind(bt, Glass.Spaced("AU VOLANT", 1), Glass.Spaced("DRIVING", 1));
            return t;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Bouton pilule de verre (souris, doigt), hors de la navigation au clavier et à la manette.</summary>
        Pill MakePill(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button));
            go.transform.SetParent(parent, false);
            var p = new Pill { rt = (RectTransform)go.transform, plate = go.GetComponent<GlassRect>() };
            Pose(p.rt, anchor, pos, size);
            p.plate.radius = size.y * 0.5f;
            p.plate.color = Glass.Pill;
            var b = go.GetComponent<Button>();
            b.targetGraphic = p.plate;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(onClick);
            p.label = Glass.Label(go.transform, "", 23, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(p.label.rectTransform);
            return p;
        }

        /// <summary>Flèche du plateau (souris, doigt) : simple chevron fin, zone de clic plus large que le trait.</summary>
        (RectTransform rt, Text label) MakeArrow(Transform parent, string name, string glyph, Vector2 anchor, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Pose(rt, anchor, pos, new Vector2(52f, 140f));
            var hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            var b = go.GetComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(onClick);
            var label = Glass.Label(go.transform, glyph, 54, Glass.Weight.Light, Glass.Gold, TextAnchor.MiddleCenter);
            Glass.Stretch(label.rectTransform);
            return (rt, label);
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

        /// <summary>Ancre et pivot au même point, position et taille.</summary>
        static void Pose(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
