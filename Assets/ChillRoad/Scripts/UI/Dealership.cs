using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Concession (lieu « dealer » de la carte) : à l'arrêt dans la zone, A / E / Entrée (ou l'invite du HUD) fait
    /// entrer dans le hall d'exposition (<see cref="ShowroomStage"/>) : toute la gamme alignée sur ses podiums, de la
    /// moins chère à la plus chère. ← → passe d'une voiture à l'autre (la caméra glisse le long de la rangée), le
    /// podium de la voiture regardée tourne (joystick droit ou clic droit pour le tourner à la main). Une voiture
    /// achetée (<see cref="Wallet"/>) rejoint le garage et on prend son volant ; une voiture déjà possédée se choisit
    /// directement. En réseau, le monde continue pendant ce temps (voiture garée).
    /// </summary>
    public class Dealership : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Danger = new Color(1f, 0.32f, 0.26f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color Money = new Color(0.5f, 1f, 0.6f);
        static readonly Color TooDear = new Color(1f, 0.45f, 0.4f);

        /// <summary>Vitesse au-dessous de laquelle on peut entrer (m/s).</summary>
        const float EnterSpeed = 5.5f;
        /// <summary>
        /// Pente commune de toutes les plaques (décalage horizontal par pixel de hauteur, environ 8°) : grandes et
        /// petites plaques ont le même angle, et les textes suivent le bord penché de leur panneau.
        /// </summary>
        const float Slope = 0.14f;
        const float HeadH = 128f, WalletH = 72f, InfoH = 392f, CardH = 290f;

        public static bool IsOpen { get; private set; }
        /// <summary>Image où l'écran s'est ouvert ou fermé (la touche de fermeture ne doit pas ouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;

        /// <summary>Le joueur est arrêté dans la zone de la concession : l'invite s'affiche.</summary>
        public bool CanEnter { get; private set; }

        GameManager game;
        Font font;
        GameObject root;
        ShowroomStage stage;
        RectTransform screenRt, infoRt, cardRt;
        Text cashText, countText, nameText, subText, priceText, actionText, hintText;
        /// <summary>Repères de la rangée : pips[i] = voiture i du garage.</summary>
        readonly List<(Button button, HudShape mark)> pips = new List<(Button, HudShape)>();
        readonly List<(RectTransform fill, Text value)> bars = new List<(RectTransform, Text)>();
        /// <summary>order[k] = voiture exposée à la place k (de la moins chère à la plus chère) ; slotOf = l'inverse.</summary>
        int[] order, slotOf;
        int openedFrame, shown = -1, confirm = -1;
        float messageTimer, nameAnim = 1f;
        string message;
        Color messageColor;
        float maxPower, maxTop, maxGrip, maxRatio;

        public void Init(GameManager g)
        {
            game = g;
            font = HudArt.Font;
        }

        void OnDestroy()
        {
            IsOpen = false;
        }

        void Update()
        {
            if (!game || game.Map == null) return;
            if (!IsOpen)
            {
                CanEnter = InZone();
                if (CanEnter && GameInput.InteractPressed) Open();
                return;
            }
            CanEnter = false;
            float dt = Time.unscaledDeltaTime;
            if (Time.frameCount != openedFrame && (GameInput.CancelPressed || GameInput.PausePressed)) { Close(); return; }

            // Toujours une voiture choisie (manette, clavier) : la caméra du hall glisse jusqu'à elle
            var es = EventSystem.current;
            int selected = -1;
            if (es)
            {
                for (int i = 0; i < pips.Count; i++)
                    if (es.currentSelectedGameObject == pips[i].button.gameObject) selected = i;
                if (selected < 0 && pips.Count > 0) { selected = shown >= 0 ? shown : game.Local.CarIndex; es.SetSelectedGameObject(pips[selected].button.gameObject); }
            }
            if (selected != shown)
            {
                shown = selected;
                confirm = -1;
                message = null;
                nameAnim = 0f;
                if (shown >= 0) stage.Select(slotOf[shown], false);
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
            if (messageTimer > 0f) { messageTimer -= dt; if (messageTimer <= 0f) message = null; }
            Refresh();
        }

        bool InZone()
        {
            var poi = game.DealerPoi;
            if (poi == null || GameManager.DedicatedServer || game.State != GameManager.Phase.Playing) return false;
            if ((game.Hud && game.Hud.Paused) || OptionsMenu.IsOpen || WorldMap.IsOpen) return false;
            // Invitation à une course affichée : la touche d'entrée y répond
            if (game.Races && game.Races.InvitePending) return false;
            var car = game.Player;
            if (!car || car.Speed > EnterSpeed) return false;
            Vector3 p = car.transform.position;
            float dx = p.x - poi.x, dz = p.z - poi.z, r = Mathf.Max(poi.r, 30f);
            return dx * dx + dz * dz < r * r;
        }

        public void Open()
        {
            if (IsOpen || !InZone()) return;
            if (!root) Build();
            IsOpen = true;
            root.SetActive(true);
            openedFrame = ChangedFrame = Time.frameCount;
            game.SetInputEnabled(false);
            if (game.Hud) game.Hud.SetVisible(false);
            confirm = -1;
            message = null;
            nameAnim = 0f;
            // On entre devant sa propre voiture
            shown = Mathf.Clamp(game.Local.CarIndex, 0, pips.Count - 1);
            stage.Repaint();
            stage.Show();
            stage.Select(slotOf[shown], true);
            ShopFade.Cut(0.5f);
            EventSystem.current?.SetSelectedGameObject(pips[shown].button.gameObject);
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ChangedFrame = Time.frameCount;
            root.SetActive(false);
            stage.Hide();
            ShopFade.Cut(0.4f);
            if (game.Hud) game.Hud.SetVisible(true);
            game.SetInputEnabled(true);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        /// <summary>Voiture voisine dans la rangée (flèches de l'écran), en boucle.</summary>
        void Step(int delta)
        {
            if (shown < 0 || order.Length == 0) return;
            int k = ((slotOf[shown] + delta) % order.Length + order.Length) % order.Length;
            EventSystem.current?.SetSelectedGameObject(pips[order[k]].button.gameObject);
        }

        /// <summary>Validation d'une voiture : la prendre si elle est au garage ; sinon l'acheter (deuxième appui pour confirmer).</summary>
        void Choose(int i)
        {
            // L'appui qui a ouvert l'écran ne doit pas valider une voiture
            if (Time.frameCount <= openedFrame + 1 || i < 0) return;
            var cars = game.assets.playerCars;
            var spec = cars[i];
            if (i != shown) { EventSystem.current?.SetSelectedGameObject(pips[i].button.gameObject); return; }
            if (Wallet.Owns(spec))
            {
                if (game.Local.CarIndex != i) { game.SelectCar(i); Say(Loc.T("Au volant !", "You're driving it!"), Money); }
                return;
            }
            int price = Wallet.Price(spec);
            if (Wallet.Cash < price)
            {
                Say(Loc.T($"Il te manque {Wallet.Format(price - Wallet.Cash)}", $"You need {Wallet.Format(price - Wallet.Cash)} more"), Danger);
                return;
            }
            if (confirm != i) { confirm = i; return; }
            confirm = -1;
            if (!Wallet.Buy(spec)) return;
            game.SelectCar(i);
            if (game.Local.Audio) game.Local.Audio.Chime();
            stage.Punch();
            Say(Loc.T($"{spec.displayName} : à toi !", $"{spec.displayName}: it's yours!"), Money);
        }

        void Say(string text, Color color)
        {
            message = text;
            messageColor = color;
            messageTimer = 2.5f;
        }

        // ------------------------------------------------------------------ affichage

        void Refresh()
        {
            var cars = game.assets.playerCars;
            int cash = Wallet.Cash;
            // Fenêtre étroite ou basse : les deux panneaux du bas rétrécissent pour ne pas se chevaucher
            float fit = Mathf.Min(1f, Mathf.Min(screenRt.rect.width / 1540f, screenRt.rect.height / 860f));
            infoRt.localScale = cardRt.localScale = Vector3.one * fit;
            cashText.text = Loc.T("Portefeuille : ", "Wallet: ") + Wallet.Format(cash);
            for (int i = 0; i < pips.Count; i++)
            {
                var spec = cars[i];
                Color c = i == game.Local.CarIndex ? Money : Wallet.Owns(spec) ? White : cash >= Wallet.Price(spec) ? Accent : TooDear;
                bool on = i == shown;
                c.a = on ? 1f : 0.5f;
                pips[i].mark.color = c;
                pips[i].mark.rectTransform.localScale = new Vector3(1f, on ? 2.4f : 1f, 1f);
            }

            if (shown < 0) return;
            var s = cars[shown];
            int owned = game.OwnedCars, place = slotOf[shown] + 1, count = cars.Length;
            countText.text = Loc.F($"{place} / {count}  ·  {owned} au garage", $"{place} / {count}  ·  {owned} owned");
            nameText.text = s.displayName;
            float ease = 1f - (1f - nameAnim) * (1f - nameAnim);
            nameText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.07f, 1f, ease);
            nameText.color = new Color(White.r, White.g, White.b, ease);
            string drive = s.drivetrain == Drivetrain.AWD ? Loc.T("Intégrale", "AWD")
                : s.drivetrain == Drivetrain.FWD ? Loc.T("Traction", "FWD") : Loc.T("Propulsion", "RWD");
            int gears = s.gearRatios != null ? s.gearRatios.Length : 0;
            subText.text = Loc.F($"{drive}  ·  {gears} rapports  ·  {s.mass:N0} kg", $"{drive}  ·  {gears} speeds  ·  {s.mass:N0} kg");
            float power = Power(s), top = s.TopSpeed * 3.6f;
            SetBar(0, power / maxPower, Loc.F($"{power:0} ch", $"{power:0} hp"));
            SetBar(1, top / maxTop, Loc.F($"{top:0} km/h"));
            float ratio = power / Mathf.Max(s.mass, 1f) * 1000f;
            SetBar(2, ratio / maxRatio, Loc.F($"{ratio:0} ch/t", $"{ratio:0} hp/t"));
            SetBar(3, s.tireGrip / maxGrip, Loc.F($"{s.tireGrip:0.00}"));

            // Étiquette et action de la voiture regardée
            bool pad = GameInput.UsingGamepad;
            string ok = pad ? "A" : Loc.T("Entrée", "Enter");
            bool mine = Wallet.Owns(s);
            int price = Wallet.Price(s);
            if (shown == game.Local.CarIndex) { priceText.text = Loc.T("AU VOLANT", "DRIVING"); priceText.color = Money; }
            else if (mine) { priceText.text = Loc.T("AU GARAGE", "OWNED"); priceText.color = White; }
            else { priceText.text = Wallet.Format(price); priceText.color = cash >= price ? Accent : TooDear; }

            if (message != null) { actionText.text = message; actionText.color = messageColor; }
            else if (shown == game.Local.CarIndex) { actionText.text = Loc.T("Ta voiture du moment", "Your current car"); actionText.color = Money; }
            else if (mine) { actionText.text = Loc.T($"{ok} : prendre le volant", $"{ok}: drive it"); actionText.color = White; }
            else if (cash < price) { actionText.text = Loc.T($"Il te manque {Wallet.Format(price - cash)}", $"You need {Wallet.Format(price - cash)} more"); actionText.color = Danger; }
            else if (confirm == shown) { actionText.text = Loc.T($"{ok} encore : acheter pour {Wallet.Format(price)}", $"{ok} again: buy for {Wallet.Format(price)}"); actionText.color = Accent; }
            else { actionText.text = Loc.T($"{ok} : acheter", $"{ok}: buy"); actionText.color = White; }
            hintText.text = pad ? Loc.T("← → choisir  ·  A valider  ·  B sortir  ·  joystick droit : tourner", "← → browse  ·  A select  ·  B leave  ·  right stick: rotate")
                : Loc.T("← → choisir  ·  Entrée valider  ·  Échap sortir  ·  clic droit : tourner", "← → browse  ·  Enter select  ·  Esc leave  ·  right click: rotate");
        }

        void SetBar(int i, float v, string value)
        {
            bars[i].fill.localScale = new Vector3(Mathf.Clamp01(v), 1f, 1f);
            bars[i].value.text = value;
        }

        /// <summary>Puissance estimée (ch) : couple max tenu jusqu'à 80 % du rupteur.</summary>
        static float Power(VehicleSpec s) => s.maxTorque * s.redline * 0.8f / 9549f * 1.36f;

        // ------------------------------------------------------------------ construction

        void Build()
        {
            var cars = game.assets.playerCars;
            foreach (var c in cars)
            {
                maxPower = Mathf.Max(maxPower, Power(c));
                maxTop = Mathf.Max(maxTop, c.TopSpeed * 3.6f);
                maxGrip = Mathf.Max(maxGrip, c.tireGrip);
                maxRatio = Mathf.Max(maxRatio, Power(c) / Mathf.Max(c.mass, 1f) * 1000f);
            }

            // Rangée du hall : de la moins chère à la plus chère
            order = new int[cars.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => Wallet.Price(cars[a]) != Wallet.Price(cars[b]) ? Wallet.Price(cars[a]).CompareTo(Wallet.Price(cars[b])) : a.CompareTo(b));
            slotOf = new int[cars.Length];
            for (int k = 0; k < order.Length; k++) slotOf[order[k]] = k;
            stage = gameObject.AddComponent<ShowroomStage>();
            stage.Build(game, order);

            root = new GameObject("Dealership", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Résolution de référence plus grande : le menu occupe moins d'écran (environ 80 % de l'ancienne taille)
            scaler.referenceResolution = new Vector2(2400, 1350);
            scaler.matchWidthOrHeight = 0.5f;
            var screen = (RectTransform)root.transform;
            screenRt = screen;

            // Enseigne (en haut à gauche) et portefeuille (en haut à droite)
            var head = Panel(screen, "Head", new Vector2(0f, 1f), new Vector2(50f, -36f), new Vector2(560f, HeadH));
            Edge(head, HeadH);
            var title = Place(Label(head, "", 48, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), new Vector2(34f, -12f), new Vector2(500f, 62f), HeadH);
            Loc.Bind(title, "CONCESSION", "CAR DEALER");
            countText = Place(Label(head, "", 22, TextAnchor.MiddleLeft, Dim, FontStyle.Bold), new Vector2(36f, -82f), new Vector2(500f, 30f), HeadH);
            var purse = Panel(screen, "Wallet", new Vector2(1f, 1f), new Vector2(-50f, -36f), new Vector2(470f, WalletH));
            cashText = Label(purse, "", 30, TextAnchor.MiddleCenter, Money, FontStyle.BoldAndItalic);
            Stretch(cashText.rectTransform, 20f, 20f);
            // Sortir (souris, doigt) : hors de la navigation
            var leave = Plate(screen, "Leave", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -122f), new Vector2(220f, 52f), Close, 24);
            Loc.Bind(leave.label, "Sortir", "Leave");

            // Flèches de la rangée (souris, doigt), sur les voitures voisines
            var prev = Plate(screen, "Previous", new Vector2(0f, 0.56f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(64f, 96f), () => Step(-1), 64);
            prev.label.text = "‹";
            var next = Plate(screen, "Next", new Vector2(1f, 0.56f), new Vector2(1f, 0.5f), new Vector2(-34f, 0f), new Vector2(64f, 96f), () => Step(1), 64);
            next.label.text = "›";

            // Voiture regardée (en bas à gauche) : nom, fiche courte, prix, action, repères de la rangée
            infoRt = Panel(screen, "Car", new Vector2(0f, 0f), new Vector2(50f, 36f), new Vector2(780f, InfoH));
            Edge(infoRt, InfoH);
            nameText = Low(Label(infoRt, "", 52, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), 36f, 334f, 720f, 70f, InfoH);
            subText = Low(Label(infoRt, "", 23, TextAnchor.MiddleLeft, Dim, FontStyle.Bold), 38f, 280f, 720f, 32f, InfoH);
            priceText = Low(Label(infoRt, "", 36, TextAnchor.MiddleLeft, Accent, FontStyle.BoldAndItalic), 36f, 228f, 720f, 48f, InfoH);
            var action = Plate(infoRt, "Action", Vector2.zero, Vector2.zero, new Vector2(34f + Lean(126f + 32f, InfoH), 126f), new Vector2(640f, 64f), () => Choose(shown), 28);
            actionText = action.label;
            float step = Mathf.Min(62f, 700f / Mathf.Max(1, order.Length));
            var built = new (Button button, HudShape mark)[cars.Length];
            for (int k = 0; k < order.Length; k++)
            {
                int index = order[k];
                built[index] = Pip(infoRt, 36f + Lean(90f, InfoH) + k * step, 90f, step, () => Choose(index));
            }
            pips.AddRange(built);
            // Navigation gauche / droite le long de la rangée, en boucle
            for (int k = 0; k < order.Length; k++)
                pips[order[k]].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = pips[order[(k - 1 + order.Length) % order.Length]].button,
                    selectOnRight = pips[order[(k + 1) % order.Length]].button,
                };
            hintText = Low(Label(infoRt, "", 19, TextAnchor.MiddleLeft, Dim, FontStyle.Bold), 38f, 38f, 720f, 28f, InfoH);

            // Fiche technique (en bas à droite)
            cardRt = Panel(screen, "Card", new Vector2(1f, 0f), new Vector2(-50f, 36f), new Vector2(640f, CardH));
            Edge(cardRt, CardH);
            var cardTitle = Place(Label(cardRt, "", 24, TextAnchor.MiddleLeft, Info, FontStyle.BoldAndItalic), new Vector2(34f, -18f), new Vector2(560f, 34f), CardH);
            Loc.Bind(cardTitle, "FICHE TECHNIQUE", "SPECS");
            string[,] labels = { { "Puissance", "Power" }, { "Vitesse max", "Top speed" }, { "Rapport poids/puissance", "Power to weight" }, { "Adhérence", "Grip" } };
            for (int i = 0; i < 4; i++)
            {
                float y = -78f - i * 48f;
                var label = Place(Label(cardRt, "", 21, TextAnchor.MiddleLeft, White, FontStyle.Normal), new Vector2(34f, y), new Vector2(260f, 30f), CardH);
                Loc.Bind(label, labels[i, 0], labels[i, 1]);
                float lean = Lean(CardH + y - 15f, CardH);
                var bg = Rect(cardRt, "Bar", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(296f + lean, y - 19f), new Vector2(480f + lean, y - 11f));
                bg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
                var fill = Rect(bg, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.gameObject.AddComponent<Image>().color = Info;
                var value = Place(Label(cardRt, "", 21, TextAnchor.MiddleRight, Accent, FontStyle.Bold), new Vector2(484f, y), new Vector2(124f, 30f), CardH);
                bars.Add((fill, value));
            }
            root.SetActive(false);
        }

        /// <summary>Panneau à plaque sombre, accroché à un coin de l'écran (ancre = pivot).</summary>
        static RectTransform Panel(Transform parent, string name, Vector2 corner, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, corner, corner, Vector2.zero, Vector2.zero);
            rt.pivot = corner;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            Backdrop(rt, size.y);
            return rt;
        }

        /// <summary>Décalage horizontal du bord penché d'un panneau à la hauteur y (depuis le bas) : 0 au milieu.</summary>
        static float Lean(float y, float panelHeight) => Slope * (y - panelHeight * 0.5f);

        /// <summary>Liseré de couleur le long du bord gauche d'un panneau, à la même pente que sa plaque.</summary>
        static void Edge(RectTransform panel, float height)
        {
            Shape(panel, "Stripe", Vector2.zero, Vector2.zero, new Vector2(4f, height), new Color(Info.r, Info.g, Info.b, 0.7f)).Set(height * Slope, 0f, 0f);
        }

        /// <summary>Bouton à plaque inclinée (souris, doigt), hors de la navigation au clavier et à la manette.</summary>
        (Button button, Text label) Plate(Transform parent, string name, Vector2 corner, Vector2 pivot, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick, int textSize)
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
            var label = Label(go.transform, "", textSize, TextAnchor.MiddleCenter, White, FontStyle.BoldAndItalic);
            Stretch(label.rectTransform, 16f, 16f);
            return (b, label);
        }

        /// <summary>Repère d'une voiture dans la rangée : zone de clic haute, trait fin coloré selon son état.</summary>
        (Button button, HudShape mark) Pip(Transform parent, float x, float y, float width, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Pip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, 40f);
            rt.anchoredPosition = new Vector2(x, y);
            var hit = go.GetComponent<Image>();
            hit.color = Color.clear;
            var b = go.GetComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
            var mark = Shape(go.transform, "Mark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width - 10f, 8f), White);
            mark.Set(8f * Slope, 0f, 0f);
            return (b, mark);
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

        /// <summary>Fond d'un panneau : plaque inclinée sur toute la surface, à la pente commune.</summary>
        static void Backdrop(RectTransform panel, float height)
        {
            var s = Shape(panel, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.025f, 0.03f, 0.045f, 0.74f));
            s.Set(height * Slope, 0f, 0f);
            var rt = s.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
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
