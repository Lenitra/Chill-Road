using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Garage de tuning (lieu « tuning » de la carte) : à l'arrêt dans la zone, A / E / Entrée (ou l'invite du HUD)
    /// fait entrer la voiture que l'on conduit dans l'atelier (<see cref="WorkshopStage"/>). Menu à deux étages, en
    /// haut à gauche : les pièces (<see cref="CarTuning"/>), puis les options de la pièce ouverte avec leur prix : niveaux
    /// (série, 1, 2, 3) ou types pour les pneus et les suspensions (origine, rallye, slick…). La caméra de l'atelier
    /// vient cadrer la pièce ; la fiche montre ce que l'option regardée change (performances, adhérence par surface
    /// pour les pneus, réglages pour les suspensions).
    /// Chaque option s'achète seule (le niveau 3 ne donne pas les niveaux 1 et 2) ; une option achetée, comme la pièce
    /// de série, se remonte ensuite gratuitement, autant de fois que l'on veut.
    /// Deux onglets (LB / RB, Tab, ou clic) : PERFORMANCES (les pièces ci-dessus) et COSMÉTIQUE, sans effet sur la
    /// conduite : la peinture (nuancier de <see cref="CarPaint"/>), puis les accessoires de <see cref="CarCosmetics"/>
    /// (néons, vitres teintées, pop and bang, couleur des flammes), essayés en direct sur la voiture de l'atelier — le pop
    /// and bang et les flammes y pétaradent en démonstration. Même règle d'achat : payé une fois, remonté gratuitement.
    /// En réseau, le monde continue pendant ce temps.
    /// </summary>
    public class TuningGarage : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Brand = new Color(1f, 0.42f, 0.82f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Danger = new Color(1f, 0.32f, 0.26f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color Money = new Color(0.5f, 1f, 0.6f);
        static readonly Color PipOff = new Color(1f, 1f, 1f, 0.16f);
        // Sur la ligne choisie (plaque blanche) : mêmes teintes, foncées
        static readonly Color InkDim = new Color(0f, 0f, 0f, 0.5f);
        static readonly Color InkMoney = new Color(0.04f, 0.46f, 0.18f);
        static readonly Color InkAccent = new Color(0.66f, 0.4f, 0f);
        static readonly Color InkDanger = new Color(0.74f, 0.13f, 0.09f);
        static readonly Color InkBrand = new Color(0.8f, 0.16f, 0.58f);

        /// <summary>Vitesse au-dessous de laquelle on peut entrer (m/s).</summary>
        const float EnterSpeed = 5.5f;
        const int Stats = 6;
        /// <summary>Onglets : performances (pièces), cosmétique (peinture et accessoires).</summary>
        const int TabParts = 0, TabLooks = 1;
        /// <summary>Lignes de l'onglet cosmétique : la peinture, puis un accessoire par ligne.</summary>
        const int LookRows = 1 + CarCosmetics.ItemCount;
        /// <summary>Onglet ouvert la dernière fois (gardé pour la session).</summary>
        static int lastTab;

        public static bool IsOpen { get; private set; }
        /// <summary>Image où l'écran s'est ouvert ou fermé (la touche de fermeture ne doit pas ouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;
        /// <summary>Atelier de la partie en cours (invite du HUD).</summary>
        public static TuningGarage Current { get; private set; }

        /// <summary>Le joueur est arrêté dans la zone du garage : l'invite s'affiche.</summary>
        public bool CanEnter { get; private set; }

        class Row
        {
            public Button button;
            public Text name, status;
            public Image[] pips;
            /// <summary>Pastille de couleur (peinture).</summary>
            public Image swatch;
        }

        GameManager game;
        Font font;
        MapData.PoiJson poi;
        bool poiSearched;
        GameObject root;
        WorkshopStage stage;
        RectTransform screenRt, menuRt, cardRt;
        Text carText, cashText, crumbText, countText, nameText, subText, levelText, actionText, hintText, leaveText, tabPrevText, tabNextText;
        readonly List<Row> rows = new List<Row>();
        readonly List<(RectTransform fill, RectTransform gain, RectTransform loss, Text label, Text value, RectTransform bg)> bars = new List<(RectTransform, RectTransform, RectTransform, Text, Text, RectTransform)>();
        /// <summary>Boutons des onglets : plaque, libellé, trait de l'onglet ouvert.</summary>
        readonly List<(HudShape plate, Text label, Image mark)> tabs = new List<(HudShape, Text, Image)>();
        /// <summary>Échelle des barres, par fiche (<see cref="CardMode"/>).</summary>
        readonly float[,] scale = new float[3, Stats];
        /// <summary>Onglet ouvert (<see cref="TabParts"/>, <see cref="TabLooks"/>).</summary>
        int tab;
        /// <summary>Ligne ouverte de l'onglet (pièce, peinture ou accessoire, ses options sont listées) ; -1 : la liste.</summary>
        int part = -1;
        int openedFrame, shown = -1;
        /// <summary>Couleur montrée sur la voiture de l'atelier (-1 : à repeindre).</summary>
        int painted = -1;
        /// <summary>Démonstration du pop and bang : option montrée, temps avant la prochaine pétarade.</summary>
        int demoShown = -1;
        float demoTimer;
        float messageTimer;
        string message;
        Color messageColor;

        public void Init(GameManager g)
        {
            game = g;
            font = HudArt.Font;
            Current = this;
        }

        void OnDestroy()
        {
            IsOpen = false;
            if (Current == this) Current = null;
        }

        /// <summary>Le nuancier est ouvert.</summary>
        bool InPaint => tab == TabLooks && part == 0;
        /// <summary>Les options d'un accessoire cosmétique sont listées (<see cref="LookItem"/>).</summary>
        bool InLook => tab == TabLooks && part > 0;
        CarCosmetics.Item LookItem => (CarCosmetics.Item)(part - 1);

        /// <summary>
        /// Lignes de la liste affichée : pièces, ou peinture et accessoires ; options de la pièce ouverte (série comprise),
        /// nuancier, ou options de l'accessoire.
        /// </summary>
        int RowCount
        {
            get
            {
                if (tab == TabParts) return part < 0 ? CarTuning.PartCount : CarTuning.Options((CarTuning.Part)part);
                if (part < 0) return LookRows;
                return InPaint ? CarPaint.Count : CarCosmetics.Options(LookItem);
            }
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
            if (Time.frameCount != openedFrame && (GameInput.CancelPressed || GameInput.PausePressed)) { Back(); return; }
            if (Time.frameCount != openedFrame && (GameInput.TabPrevPressed || GameInput.TabNextPressed)) SwitchTab(1 - tab);

            // Toujours une ligne choisie (manette, clavier)
            var es = EventSystem.current;
            int selected = -1, count = RowCount;
            if (es)
            {
                for (int i = 0; i < count; i++)
                    if (es.currentSelectedGameObject == rows[i].button.gameObject) selected = i;
                if (selected < 0) { selected = Mathf.Clamp(shown, 0, count - 1); es.SetSelectedGameObject(rows[selected].button.gameObject); }
            }
            if (selected != shown) { shown = selected; message = null; }

            // Caméra de l'atelier tournée à la main : joystick droit, ou clic droit maintenu
            float orbit = GameInput.LookStick.x * 130f * dt;
            if (GameInput.MouseLookHeld) orbit += GameInput.MouseDelta.x * 0.3f;
            stage.Orbit(orbit);

            if (messageTimer > 0f) { messageTimer -= dt; if (messageTimer <= 0f) message = null; }
            PreviewLook(dt);
            Refresh();
        }

        /// <summary>
        /// Accessoires essayés sur la voiture de l'atelier : ceux montés, avec l'option regardée à la place de celle de
        /// l'accessoire ouvert. Pop and bang et couleur des flammes : une pétarade de démonstration à intervalles.
        /// </summary>
        void PreviewLook(float dt)
        {
            var look = ShownLook();
            stage.Dress(look);
            bool exhaust = InLook && (LookItem == CarCosmetics.Item.Pop || LookItem == CarCosmetics.Item.Flame);
            if (!exhaust) { demoShown = -1; return; }
            if (shown != demoShown) { demoShown = shown; demoTimer = 0.3f; }
            demoTimer -= dt;
            if (demoTimer > 0f) return;
            stage.Pop(look);
            demoTimer = look.pop == CarCosmetics.PopDiesel ? 2.4f : 1.7f;
        }

        /// <summary>Accessoires montés, et l'option regardée de l'accessoire ouvert.</summary>
        CarCosmetics.Look ShownLook()
        {
            var look = CarCosmetics.Fitted(BaseSpec);
            if (InLook && shown >= 0) look[LookItem] = shown;
            return look;
        }

        MapData.PoiJson Poi
        {
            get
            {
                if (!poiSearched && game.Map != null && game.Map.Json != null)
                {
                    poiSearched = true;
                    if (game.Map.Json.pois != null)
                        foreach (var p in game.Map.Json.pois)
                            if (p.id == "tuning") poi = p;
                }
                return poi;
            }
        }

        bool InZone()
        {
            var p = Poi;
            if (p == null || GameManager.DedicatedServer || game.State != GameManager.Phase.Playing) return false;
            if ((game.Hud && game.Hud.Paused) || OptionsMenu.IsOpen || WorldMap.IsOpen || Dealership.IsOpen) return false;
            // Invitation à une course affichée : la touche d'entrée y répond
            if (game.Races && game.Races.InvitePending) return false;
            var car = game.Player;
            if (!car || car.Speed > EnterSpeed) return false;
            Vector3 pos = car.transform.position;
            float dx = pos.x - p.x, dz = pos.z - p.z, r = Mathf.Max(p.r, 30f);
            return dx * dx + dz * dz < r * r;
        }

        /// <summary>Voiture que l'on conduit (fiche d'origine, sans améliorations).</summary>
        VehicleSpec BaseSpec => game.assets.playerCars[Mathf.Clamp(game.Local.CarIndex, 0, game.assets.playerCars.Length - 1)];

        public void Open()
        {
            if (IsOpen || !InZone()) return;
            if (!root) Build();
            IsOpen = true;
            root.SetActive(true);
            openedFrame = ChangedFrame = Time.frameCount;
            game.SetInputEnabled(false);
            if (game.Hud) game.Hud.SetVisible(false);
            message = null;
            tab = lastTab;
            part = -1;
            Arrange();
            stage.SetCar(BaseSpec);
            painted = -1;
            demoShown = -1;
            stage.Dress(CarCosmetics.Fitted(BaseSpec));
            stage.Focus(-1, true);
            stage.Show();
            ShopFade.Cut(0.5f);
            Select(FirstRow());
            Refresh();
        }

        /// <summary>Ligne choisie en arrivant sur un onglet : la première pièce à niveaux encore améliorable, ou la peinture.</summary>
        int FirstRow()
        {
            if (tab == TabLooks) return 0;
            for (int i = 0; i < CarTuning.PartCount; i++)
                if (!CarTuning.IsTyped((CarTuning.Part)i) && CarTuning.Level(BaseSpec, (CarTuning.Part)i) < CarTuning.MaxLevel) return i;
            return 0;
        }

        /// <summary>Passe à l'autre onglet, sur sa liste.</summary>
        void SwitchTab(int t)
        {
            t = Mathf.Clamp(t, TabParts, TabLooks);
            if (t == tab) return;
            tab = lastTab = t;
            part = -1;
            message = null;
            Arrange();
            stage.Focus(-1);
            Select(FirstRow());
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

        void Select(int row)
        {
            shown = Mathf.Clamp(row, 0, RowCount - 1);
            EventSystem.current?.SetSelectedGameObject(rows[shown].button.gameObject);
        }

        /// <summary>
        /// Ouvre une pièce : ses niveaux remplacent la liste, la caméra vient la cadrer. Peinture : le nuancier, vue
        /// d'ensemble. Accessoire : ses options, la caméra cadre le dessous de caisse, les vitres ou l'échappement.
        /// </summary>
        void EnterPart(int p)
        {
            if (tab == TabLooks && p == 0)
            {
                if (!CarPaint.Paintable(BaseSpec)) { Say(Loc.T("Cette voiture garde sa livrée d'origine", "This car keeps its factory livery"), Dim); return; }
                part = p;
                message = null;
                Arrange();
                stage.Focus(-1);
                Select(CarPaint.Selected(BaseSpec));
                return;
            }
            if (tab == TabLooks)
            {
                var item = (CarCosmetics.Item)(p - 1);
                if (!CarCosmetics.Available(BaseSpec, item)) { Say(CarCosmetics.Unavailable(item), Dim); return; }
                part = p;
                message = null;
                Arrange();
                stage.Focus(item == CarCosmetics.Item.Neon ? WorkshopStage.FocusUnder
                          : item == CarCosmetics.Item.Tint ? WorkshopStage.FocusGlass : WorkshopStage.FocusExhaust);
                Select(CarCosmetics.Fitted(BaseSpec, item));
                return;
            }
            part = p;
            message = null;
            Arrange();
            stage.Focus(p);
            // Sur le prochain niveau à acheter, ou sur le type monté
            var cp = (CarTuning.Part)p;
            int lv = CarTuning.Level(BaseSpec, cp);
            Select(CarTuning.IsTyped(cp) ? lv : Mathf.Min(lv + 1, CarTuning.MaxLevel));
        }

        /// <summary>Retour : des niveaux à la liste des pièces, puis de la liste à la route.</summary>
        void Back()
        {
            if (part < 0) { Close(); return; }
            int p = part;
            part = -1;
            message = null;
            Arrange();
            stage.Focus(-1);
            Select(p);
        }

        /// <summary>Lignes utiles affichées, navigation haut / bas en boucle entre elles.</summary>
        void Arrange()
        {
            int count = RowCount;
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].button.gameObject.SetActive(i < count);
                foreach (var pip in rows[i].pips) pip.gameObject.SetActive(tab == TabParts && part < 0 && i < CarTuning.PartCount && !CarTuning.IsTyped((CarTuning.Part)i));
                // Pastilles : peinture et accessoires de couleur (néons, vitres, flammes ; transparente si sans objet)
                if (rows[i].swatch) rows[i].swatch.gameObject.SetActive(tab == TabLooks && (part < 0 || InPaint || LookItem != CarCosmetics.Item.Pop));
                if (i >= count) continue;
                rows[i].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = rows[(i - 1 + count) % count].button,
                    selectOnDown = rows[(i + 1) % count].button,
                };
            }
        }

        /// <summary>Validation d'une ligne : ouvrir la pièce, ou monter l'option (gratuit si elle est achetée, sinon s'il y a l'argent).</summary>
        void Choose(int i)
        {
            // L'appui qui a ouvert l'écran ne doit pas valider une ligne
            if (Time.frameCount <= openedFrame + 1 || i >= RowCount) return;
            if (i != shown) { Select(i); return; }
            if (part < 0) { EnterPart(i); return; }
            if (InPaint) { Paint(i); return; }
            if (InLook) { FitLook(i); return; }
            var spec = BaseSpec;
            var p = (CarTuning.Part)part;
            int level = CarTuning.Level(spec, p);
            if (i == level) { Say(Loc.T("Déjà installé", "Already fitted"), Dim); return; }
            bool owned = CarTuning.Owns(spec, p, i);
            int price = CarTuning.Price(spec, p, i);
            if (!owned && Wallet.Cash < price)
            {
                Say(Loc.T($"Il te manque {Wallet.Format(price - Wallet.Cash)}", $"You need {Wallet.Format(price - Wallet.Cash)} more"), Danger);
                return;
            }
            if (!CarTuning.Fit(spec, p, i)) return;
            // La voiture garée dehors prend aussitôt sa nouvelle fiche
            game.ShowCar(game.Local.CarIndex);
            if (game.Local.Audio) game.Local.Audio.Chime();
            stage.Punch();
            string what = $"{CarTuning.Name(p)} : {CarTuning.OptionName(p, i)}";
            Say(owned ? Loc.T($"{what}, remonté", $"{what}, refitted") : Loc.T($"{what}, installé !", $"{what}, fitted!"), Money);
        }

        /// <summary>Validation d'une couleur : la remettre si elle est achetée, sinon l'acheter si l'argent suffit.</summary>
        void Paint(int i)
        {
            var spec = BaseSpec;
            if (i == CarPaint.Selected(spec)) { Say(Loc.T("Déjà sur la voiture", "Already on the car"), Dim); return; }
            bool owned = CarPaint.Owns(spec, i);
            int price = CarPaint.Price(spec, i);
            if (!owned && Wallet.Cash < price)
            {
                Say(Loc.T($"Il te manque {Wallet.Format(price - Wallet.Cash)}", $"You need {Wallet.Format(price - Wallet.Cash)} more"), Danger);
                return;
            }
            if (!CarPaint.Buy(spec, i)) return;
            // La voiture garée dehors change aussitôt de couleur
            game.ShowCar(game.Local.CarIndex);
            if (game.Local.Audio) game.Local.Audio.Chime();
            stage.Punch();
            Say(owned ? Loc.T($"Repeinte en {CarPaint.Name(i)}", $"Repainted {CarPaint.Name(i)}")
                      : Loc.T($"{CarPaint.Name(i)} achetée !", $"{CarPaint.Name(i)} bought!"), Money);
        }

        /// <summary>Validation d'une option d'accessoire : la remonter si elle est à disposition, sinon l'acheter si l'argent suffit.</summary>
        void FitLook(int i)
        {
            var spec = BaseSpec;
            var item = LookItem;
            if (i == CarCosmetics.Fitted(spec, item)) { Say(Loc.T("Déjà installé", "Already fitted"), Dim); return; }
            bool owned = CarCosmetics.Owns(spec, item, i);
            int price = CarCosmetics.Price(spec, item, i);
            if (!owned && Wallet.Cash < price)
            {
                Say(Loc.T($"Il te manque {Wallet.Format(price - Wallet.Cash)}", $"You need {Wallet.Format(price - Wallet.Cash)} more"), Danger);
                return;
            }
            if (!CarCosmetics.Fit(spec, item, i)) return;
            // La voiture garée dehors prend aussitôt ses néons et ses vitres (pop and bang : ses effets les lisent)
            game.ShowCar(game.Local.CarIndex);
            if (game.Local.Audio) game.Local.Audio.Chime();
            stage.Punch();
            string what = $"{CarCosmetics.Name(item)} : {CarCosmetics.OptionName(item, i)}";
            Say(owned ? Loc.T($"{what}, monté", $"{what}, fitted") : Loc.T($"{what}, installé !", $"{what}, fitted!"), Money);
        }

        void Say(string text, Color color)
        {
            message = text;
            messageColor = color;
            messageTimer = 2.5f;
        }

        static string PriceText(int price) => price < 0 ? "MAX" : price == 0 ? Loc.T("GRATUIT", "FREE") : Wallet.Format(price);

        // ------------------------------------------------------------------ affichage

        void Refresh()
        {
            var spec = BaseSpec;
            int cash = Wallet.Cash, count = RowCount;
            // Fenêtre basse ou étroite : le menu et la fiche rétrécissent
            menuRt.localScale = Vector3.one * Mathf.Min(1f, (screenRt.rect.height - 88f) / Mathf.Max(menuRt.rect.height, 1f));
            cardRt.localScale = Vector3.one * Mathf.Min(1f, Mathf.Min(screenRt.rect.width / 1500f, screenRt.rect.height / 900f));
            carText.text = spec.displayName;
            cashText.text = Loc.T("Portefeuille : ", "Wallet: ") + Wallet.Format(cash);
            crumbText.text = tab == TabParts
                ? (part < 0 ? Loc.T("PIÈCES", "PARTS") : CarTuning.Name((CarTuning.Part)part).ToUpperInvariant())
                : part < 0 ? Loc.T("PEINTURE ET ACCESSOIRES", "PAINT AND ACCESSORIES") : InPaint ? Loc.T("PEINTURE", "PAINT") : CarCosmetics.Name(LookItem).ToUpperInvariant();
            countText.text = shown >= 0 ? $"{shown + 1} / {count}" : "";
            leaveText.text = part < 0 ? Loc.T("Sortir", "Leave") : Loc.T("Retour", "Back");
            bool pad = GameInput.UsingGamepad;
            for (int k = 0; k < tabs.Count; k++)
            {
                bool open = k == tab;
                tabs[k].plate.color = open ? new Color(0.12f, 0.13f, 0.17f, 0.96f) : new Color(0.02f, 0.025f, 0.04f, 0.85f);
                tabs[k].label.color = open ? White : Dim;
                tabs[k].mark.enabled = open;
            }
            tabPrevText.text = pad ? "LB" : "";
            tabNextText.text = pad ? "RB" : "Tab";

            int current = tab == TabParts && part >= 0 ? CarTuning.Level(spec, (CarTuning.Part)part) : 0;
            int colour = CarPaint.Selected(spec);
            var fitted = CarCosmetics.Fitted(spec);
            // Voiture de l'atelier : la couleur regardée dans le nuancier, sinon la sienne
            int look = InPaint && shown >= 0 ? shown : colour;
            if (look != painted) { stage.Paint(look); painted = look; }
            for (int i = 0; i < count; i++)
            {
                var row = rows[i];
                bool on = i == shown;
                row.name.color = on ? Ink : White;
                if (tab == TabLooks && part < 0 && i > 0)
                {
                    // Accessoire : l'option montée, sa couleur dans la pastille
                    var item = (CarCosmetics.Item)(i - 1);
                    row.name.text = CarCosmetics.Name(item);
                    bool can = CarCosmetics.Available(spec, item);
                    row.swatch.color = can ? CarCosmetics.Swatch(item, fitted[item]) : Color.clear;
                    row.status.text = can ? CarCosmetics.OptionName(item, fitted[item]).ToUpperInvariant() : Loc.T("INDISPONIBLE", "N/A");
                    row.status.color = on ? InkDim : Dim;
                }
                else if (InLook)
                {
                    var item = LookItem;
                    row.name.text = CarCosmetics.OptionName(item, i);
                    row.swatch.color = CarCosmetics.Swatch(item, i);
                    if (i == fitted[item]) { row.status.text = Loc.T("INSTALLÉ", "FITTED"); row.status.color = on ? InkMoney : Money; }
                    else if (CarCosmetics.Owns(spec, item, i))
                    {
                        row.status.text = i == CarCosmetics.Stock(item) ? Loc.T("D'ORIGINE", "STOCK")
                                        : CarCosmetics.OptionPrice(spec, item, i) == 0 ? Loc.T("GRATUIT", "FREE") : Loc.T("ACHETÉ", "OWNED");
                        row.status.color = on ? InkDim : Dim;
                    }
                    else
                    {
                        int price = CarCosmetics.Price(spec, item, i);
                        row.status.text = PriceText(price);
                        row.status.color = cash >= price ? (on ? InkAccent : Accent) : on ? InkDanger : new Color(1f, 0.45f, 0.4f, 0.85f);
                    }
                }
                else if (tab == TabLooks && part < 0)
                {
                    row.name.text = Loc.T("Peinture", "Paint");
                    row.swatch.color = CarPaint.Shade(spec, colour);
                    row.status.text = CarPaint.Paintable(spec) ? CarPaint.Name(colour).ToUpperInvariant() : Loc.T("D'ORIGINE", "FACTORY");
                    row.status.color = on ? InkDim : Dim;
                }
                else if (InPaint)
                {
                    row.name.text = CarPaint.Name(i);
                    row.swatch.color = CarPaint.Shade(spec, i);
                    // Textes courts : la pastille est juste à gauche
                    if (i == colour) { row.status.text = Loc.T("ACTUELLE", "CURRENT"); row.status.color = on ? InkMoney : Money; }
                    else if (CarPaint.Owns(spec, i)) { row.status.text = i == 0 ? Loc.T("OFFERTE", "FREE") : Loc.T("ACHETÉE", "OWNED"); row.status.color = on ? InkDim : Dim; }
                    else
                    {
                        int price = CarPaint.Price(spec, i);
                        row.status.text = PriceText(price);
                        row.status.color = cash >= price ? (on ? InkAccent : Accent) : on ? InkDanger : new Color(1f, 0.45f, 0.4f, 0.85f);
                    }
                }
                else if (part < 0)
                {
                    var p = (CarTuning.Part)i;
                    row.name.text = CarTuning.Name(p);
                    int level = CarTuning.Level(spec, p);
                    if (CarTuning.IsTyped(p))
                    {
                        // Pneus, suspensions : le type monté, comme la couleur de la peinture
                        row.status.text = CarTuning.OptionName(p, level).ToUpperInvariant();
                        row.status.color = on ? InkDim : Dim;
                        continue;
                    }
                    int price = CarTuning.NextPrice(spec, p);
                    for (int k = 0; k < row.pips.Length; k++)
                        row.pips[k].color = k < level ? (on ? InkBrand : Brand) : on ? new Color(0f, 0f, 0f, 0.18f) : PipOff;
                    bool bought = price >= 0 && CarTuning.Owns(spec, p, level + 1);
                    row.status.text = bought ? Loc.T("ACHETÉ", "OWNED") : PriceText(price);
                    row.status.color = price < 0 ? (on ? InkMoney : Money) : bought ?(on ? InkDim : Dim) : cash >= price ? (on ? InkAccent : Accent) : on ? InkDanger : new Color(1f, 0.45f, 0.4f, 0.85f);
                }
                else
                {
                    var cp = (CarTuning.Part)part;
                    row.name.text = CarTuning.OptionName(cp, i);
                    if (i == current) { row.status.text = Loc.T("INSTALLÉ", "FITTED"); row.status.color = on ? InkMoney : Money; }
                    else if (CarTuning.Owns(spec, cp, i)) { row.status.text = i == 0 ? Loc.T("D'ORIGINE", "STOCK") : Loc.T("ACHETÉ", "OWNED"); row.status.color = on ? InkDim : Dim; }
                    else
                    {
                        int price = CarTuning.Price(spec, cp, i);
                        row.status.text = PriceText(price);
                        row.status.color = cash >= price ? (on ? InkAccent : Accent) : on ? InkDanger : new Color(1f, 0.45f, 0.4f, 0.85f);
                    }
                }
            }

            if (shown < 0) return;
            string ok = pad ? "A" : Loc.T("Entrée", "Enter");
            string turn = pad ? Loc.T("joystick droit : tourner", "right stick: rotate") : Loc.T("clic droit : tourner", "right click: rotate");
            string back = pad ? "B" : Loc.T("Échap", "Esc");
            if (InPaint || (tab == TabLooks && part < 0 && shown == 0)) { RefreshPaint(spec, cash, ok, back, turn); return; }
            if (tab == TabLooks) { RefreshLook(spec, cash, ok, back, turn); return; }

            // Fiche : la pièce regardée (ou ouverte), et ce qu'apporte l'option visée
            var sel = (CarTuning.Part)(part < 0 ? shown : part);
            bool typed = CarTuning.IsTyped(sel);
            int lv = CarTuning.Level(spec, sel);
            int target = part >= 0 ? shown : typed ? lv : Mathf.Min(lv + 1, CarTuning.MaxLevel);
            string option = CarTuning.OptionName(sel, target);
            nameText.text = CarTuning.Name(sel);
            subText.text = part >= 0 && typed ? CarTuning.OptionEffect(sel, target) : CarTuning.Effect(sel);
            levelText.text = typed ? CarTuning.OptionName(sel, lv) : Loc.F($"Niveau {lv} / {CarTuning.MaxLevel}", $"Level {lv} / {CarTuning.MaxLevel}");

            var now = CarTuning.Apply(spec);
            ShowBars(CardMode(sel), now, target != lv ? CarTuning.Preview(spec, sel, target) : now);

            int cost = CarTuning.Price(spec, sel, target);
            bool has = CarTuning.Owns(spec, sel, target);
            if (message != null) { actionText.text = message; actionText.color = messageColor; }
            else if (part < 0 && typed) { actionText.text = Loc.T($"{ok} : voir les {CarTuning.Options(sel)} types", $"{ok}: see the {CarTuning.Options(sel)} types"); actionText.color = White; }
            else if (part < 0 && lv >= CarTuning.MaxLevel) { actionText.text = Loc.T($"{ok} : voir les niveaux  ·  pièce au maximum", $"{ok}: see the levels  ·  part fully upgraded"); actionText.color = Money; }
            else if (part < 0) { actionText.text = Loc.T($"{ok} : voir les niveaux  ·  suivant {(has ? "acheté" : PriceText(cost))}", $"{ok}: see the levels  ·  next {(has ? "owned" : PriceText(cost))}"); actionText.color = White; }
            else if (target == lv) { actionText.text = typed ? Loc.T("Monté sur la voiture", "On the car") : Loc.T("Niveau installé", "Level fitted"); actionText.color = Money; }
            else if (target == 0) { actionText.text = Loc.T($"{ok} : remettre d'origine  ·  gratuit", $"{ok}: back to stock  ·  free"); actionText.color = White; }
            else if (has) { actionText.text = Loc.T($"{ok} : remonter {option}  ·  déjà acheté", $"{ok}: refit {option}  ·  already owned"); actionText.color = White; }
            else if (cash < cost) { actionText.text = Loc.T($"Il te manque {Wallet.Format(cost - cash)}", $"You need {Wallet.Format(cost - cash)} more"); actionText.color = Danger; }
            else { actionText.text = Loc.T($"{ok} : installer {option}  ·  {PriceText(cost)}", $"{ok}: fit {option}  ·  {PriceText(cost)}"); actionText.color = White; }

            hintText.text = part < 0
                ? Loc.T($"↑ ↓ choisir  ·  {ok} ouvrir  ·  {back} sortir  ·  {turn}", $"↑ ↓ browse  ·  {ok} open  ·  {back} leave  ·  {turn}")
                : Loc.T($"↑ ↓ choisir  ·  {ok} installer  ·  {back} retour  ·  {turn}", $"↑ ↓ browse  ·  {ok} fit  ·  {back} back  ·  {turn}");
        }

        /// <summary>Fiche de la peinture : sans effet sur la conduite (barres inchangées), la couleur regardée et son prix.</summary>
        void RefreshPaint(VehicleSpec spec, int cash, string ok, string back, string turn)
        {
            bool can = CarPaint.Paintable(spec);
            int colour = CarPaint.Selected(spec);
            nameText.text = InPaint ? CarPaint.Name(shown) : Loc.T("Peinture", "Paint");
            subText.text = Loc.T("Couleur de carrosserie : sans effet sur la conduite", "Body colour: no effect on handling");
            levelText.text = can ? Loc.F($"{CarPaint.OwnedCount(spec)} / {CarPaint.Count} couleurs", $"{CarPaint.OwnedCount(spec)} / {CarPaint.Count} colours") : "";
            ShowLook(spec, InPaint ? shown : colour, CarCosmetics.Fitted(spec));

            int price = InPaint ? CarPaint.Price(spec, shown) : 0;
            if (message != null) { actionText.text = message; actionText.color = messageColor; }
            else if (!can) { actionText.text = Loc.T("Cette voiture garde sa livrée d'origine", "This car keeps its factory livery"); actionText.color = Dim; }
            else if (!InPaint) { actionText.text = Loc.T($"{ok} : voir les couleurs  ·  {CarPaint.Count - 1} au choix", $"{ok}: see the colours  ·  {CarPaint.Count - 1} to choose from"); actionText.color = White; }
            else if (shown == colour) { actionText.text = Loc.T("Couleur actuelle de la voiture", "The car's current colour"); actionText.color = Money; }
            else if (CarPaint.Owns(spec, shown)) { actionText.text = Loc.T($"{ok} : repeindre  ·  déjà achetée", $"{ok}: repaint  ·  already owned"); actionText.color = White; }
            else if (cash < price) { actionText.text = Loc.T($"Il te manque {Wallet.Format(price - cash)}", $"You need {Wallet.Format(price - cash)} more"); actionText.color = Danger; }
            else { actionText.text = Loc.T($"{ok} : acheter et peindre  ·  {PriceText(price)}", $"{ok}: buy and paint  ·  {PriceText(price)}"); actionText.color = White; }

            hintText.text = InPaint
                ? Loc.T($"↑ ↓ essayer  ·  {ok} peindre  ·  {back} retour  ·  {turn}", $"↑ ↓ try on  ·  {ok} paint  ·  {back} back  ·  {turn}")
                : Loc.T($"↑ ↓ choisir  ·  {ok} ouvrir  ·  {back} sortir  ·  {turn}", $"↑ ↓ browse  ·  {ok} open  ·  {back} leave  ·  {turn}");
        }

        /// <summary>Fiche d'un accessoire cosmétique : ce qu'il fait, l'option visée et son prix, l'habillage complet de la voiture.</summary>
        void RefreshLook(VehicleSpec spec, int cash, string ok, string back, string turn)
        {
            var item = part < 0 ? (CarCosmetics.Item)(shown - 1) : LookItem;
            bool can = CarCosmetics.Available(spec, item);
            int fitted = CarCosmetics.Fitted(spec, item);
            int target = part < 0 ? fitted : shown;
            string option = CarCosmetics.OptionName(item, target);
            nameText.text = CarCosmetics.Name(item);
            subText.text = part < 0 ? CarCosmetics.Effect(item) : CarCosmetics.OptionEffect(item, target);
            levelText.text = can ? Loc.F($"{CarCosmetics.OwnedCount(spec, item)} / {CarCosmetics.Options(item)}") : "";
            ShowLook(spec, CarPaint.Selected(spec), ShownLook());

            int cost = CarCosmetics.Price(spec, item, target);
            if (message != null) { actionText.text = message; actionText.color = messageColor; }
            else if (!can) { actionText.text = CarCosmetics.Unavailable(item); actionText.color = Dim; }
            else if (part < 0) { actionText.text = Loc.T($"{ok} : voir les {CarCosmetics.Options(item)} options", $"{ok}: see the {CarCosmetics.Options(item)} options"); actionText.color = White; }
            else if (target == fitted) { actionText.text = Loc.T("Monté sur la voiture", "On the car"); actionText.color = Money; }
            else if (target == CarCosmetics.Stock(item)) { actionText.text = Loc.T($"{ok} : remettre d'origine  ·  gratuit", $"{ok}: back to stock  ·  free"); actionText.color = White; }
            else if (CarCosmetics.Owns(spec, item, target))
            {
                bool free = CarCosmetics.OptionPrice(spec, item, target) == 0;
                actionText.text = Loc.T($"{ok} : monter {option}  ·  {(free ? "gratuit" : "déjà acheté")}", $"{ok}: fit {option}  ·  {(free ? "free" : "already owned")}");
                actionText.color = White;
            }
            else if (cash < cost) { actionText.text = Loc.T($"Il te manque {Wallet.Format(cost - cash)}", $"You need {Wallet.Format(cost - cash)} more"); actionText.color = Danger; }
            else { actionText.text = Loc.T($"{ok} : installer {option}  ·  {PriceText(cost)}", $"{ok}: fit {option}  ·  {PriceText(cost)}"); actionText.color = White; }

            hintText.text = part < 0
                ? Loc.T($"↑ ↓ choisir  ·  {ok} ouvrir  ·  {back} sortir  ·  {turn}", $"↑ ↓ browse  ·  {ok} open  ·  {back} leave  ·  {turn}")
                : Loc.T($"↑ ↓ essayer  ·  {ok} installer  ·  {back} retour  ·  {turn}", $"↑ ↓ try on  ·  {ok} fit  ·  {back} back  ·  {turn}");
        }

        /// <summary>
        /// Fiche de l'onglet cosmétique : à la place des barres, l'habillage de la voiture ligne par ligne (peinture et
        /// accessoires), le changement essayé en surbrillance (« actuel → essayé »).
        /// </summary>
        void ShowLook(VehicleSpec spec, int paint, CarCosmetics.Look look)
        {
            var fitted = CarCosmetics.Fitted(spec);
            int colour = CarPaint.Selected(spec);
            for (int i = 0; i < Stats; i++)
            {
                var bar = bars[i];
                bar.bg.gameObject.SetActive(false);
                string now, then;
                if (i == 0)
                {
                    bar.label.text = Loc.T("Peinture", "Paint");
                    bool can = CarPaint.Paintable(spec);
                    now = can ? CarPaint.Name(colour) : Loc.T("D'origine", "Factory");
                    then = can ? CarPaint.Name(paint) : now;
                }
                else if (i <= CarCosmetics.ItemCount)
                {
                    var item = (CarCosmetics.Item)(i - 1);
                    bar.label.text = CarCosmetics.Name(item);
                    bool can = CarCosmetics.Available(spec, item);
                    now = can ? CarCosmetics.OptionName(item, fitted[item]) : "—";
                    then = can ? CarCosmetics.OptionName(item, look[item]) : now;
                }
                else { bar.label.text = ""; bar.value.text = ""; continue; }
                bool changed = now != then;
                bar.label.color = changed ? Brand : White;
                bar.value.text = changed ? now + "  →  " + then : now;
                bar.value.color = changed ? Brand : Accent;
            }
        }

        /// <summary>Fiche montrée : 0 performances, 1 adhérence par surface (pneus), 2 réglages (suspensions).</summary>
        static int CardMode(CarTuning.Part p) => p == CarTuning.Part.Tires ? 1 : p == CarTuning.Part.Suspension ? 2 : 0;

        static readonly string[,,] StatLabels =
        {
            {
                { "Puissance", "Power" }, { "Vitesse max", "Top speed" }, { "Rapport poids/puissance", "Power to weight" },
                { "Adhérence", "Grip" }, { "Freinage", "Braking" }, { "Appui à 200 km/h", "Downforce at 200 km/h" },
            },
            {
                { "Goudron sec", "Dry tarmac" }, { "Pluie", "Wet" }, { "Terre", "Dirt" },
                { "Neige", "Snow" }, { "Hors route", "Off-road" }, { "Tenue en glisse", "Grip while sliding" },
            },
            {
                { "Raideur des ressorts", "Spring rate" }, { "Antiroulis", "Anti-roll" }, { "Débattement", "Travel" },
                { "Tenue sur goudron", "Grip on tarmac" }, { "Tenue sur terre", "Grip on dirt" }, { "Braquage", "Steering lock" },
            },
        };

        /// <summary>Barres de la fiche : valeurs actuelles, et le changement de l'option visée en surbrillance.</summary>
        void ShowBars(int mode, VehicleSpec now, VehicleSpec after)
        {
            for (int i = 0; i < Stats; i++)
            {
                float a = Stat(mode, now, i), b = Stat(mode, after, i);
                bool changed = Mathf.Abs(b - a) > 1e-3f, lost = changed && b < a;
                float barA = Mathf.Clamp01(Bar(mode, i, a) / scale[mode, i]), barB = Mathf.Clamp01(Bar(mode, i, b) / scale[mode, i]);
                bars[i].bg.gameObject.SetActive(true);
                bars[i].label.text = Loc.T(StatLabels[mode, i, 0], StatLabels[mode, i, 1]);
                // Le plein s'arrête à la valeur qui reste ; au-delà, le gain (rose) ou ce qui serait perdu (rouge)
                bars[i].fill.localScale = new Vector3(Mathf.Min(barA, barB), 1f, 1f);
                bars[i].gain.localScale = new Vector3(lost ? 0f : barB, 1f, 1f);
                bars[i].loss.localScale = new Vector3(lost ? barA : 0f, 1f, 1f);
                Color tint = lost ? Danger : Brand;
                bars[i].label.color = changed ? tint : White;
                bars[i].value.text = changed ? Format(mode, i, a) + "  →  " + Format(mode, i, b) : Format(mode, i, a);
                bars[i].value.color = changed ? tint : Accent;
            }
        }

        static float Stat(int mode, VehicleSpec s, int i)
        {
            if (mode == 1)
                return i < 5 ? CarTuning.GripOn(s, (CarTuning.Ground)i) : CarTuning.SlideHold(s);
            if (mode == 2)
            {
                var u = CarTuning.SuspensionOf(s);
                switch (i)
                {
                    case 0: return 100f / u.sag;
                    case 1: return 100f * u.arb;
                    case 2: return 30f * u.travel;                 // débattement de base : 30 cm
                    case 3: return 100f * u.smooth;                // adhérence relative, goudron
                    case 4: return 100f * u.rough;                 // terre, neige, hors route
                    default: return s.steerLock;
                }
            }
            switch (i)
            {
                case 0: return CarTuning.Power(s);
                case 1: return CarTuning.TopKmh(s);
                case 2: return CarTuning.PowerToWeight(s);
                case 3: return s.tireGrip;
                case 4: return CarTuning.BrakingG(s);
                default: return CarTuning.DownforceKg(s);
            }
        }

        /// <summary>
        /// Longueur de barre d'une valeur. Tenue sur goudron / terre (85 à 110 %) : la barre part de 80 % pour que
        /// l'écart entre les réglages se voie.
        /// </summary>
        static float Bar(int mode, int i, float v) => mode == 2 && (i == 3 || i == 4) ? v - 80f : v;

        static string Format(int mode, int i, float v)
        {
            if (mode == 1) return Loc.F($"{v:0.00}");
            if (mode == 2)
                switch (i)
                {
                    case 2: return Loc.F($"{v:0} cm");
                    case 5: return Loc.F($"{v:0}°");
                    default: return Loc.F($"{v:0} %");
                }
            switch (i)
            {
                case 0: return Loc.F($"{v:0} ch", $"{v:0} hp");
                case 1: return Loc.F($"{v:0} km/h");
                case 2: return Loc.F($"{v:0} ch/t", $"{v:0} hp/t");
                case 3: return Loc.F($"{v:0.00}");
                case 4: return Loc.F($"{v:0.00} g");
                default: return Loc.F($"{v:0} kg");
            }
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            // Échelle des barres : la meilleure voiture du jeu, entièrement améliorée ; pneus et suspensions : le meilleur
            // type sur chaque ligne
            foreach (var c in game.assets.playerCars)
            {
                var m = CarTuning.Maxed(c);
                for (int i = 0; i < Stats; i++) scale[0, i] = Mathf.Max(scale[0, i], Stat(0, m, i), 1e-3f);
                for (int k = 0; k < CarTuning.TypeCount; k++)
                {
                    var t = CarTuning.Preview(c, CarTuning.Part.Tires, k);
                    var u = CarTuning.Preview(c, CarTuning.Part.Suspension, k);
                    for (int i = 0; i < Stats; i++)
                    {
                        scale[1, i] = Mathf.Max(scale[1, i], Stat(1, t, i), 1e-3f);
                        scale[2, i] = Mathf.Max(scale[2, i], Bar(2, i, Stat(2, u, i)), 1e-3f);
                    }
                }
            }
            stage = gameObject.AddComponent<WorkshopStage>();
            stage.Build(game);
            // Démonstration du pop and bang : la détonation, avec la voix du moteur de la voiture
            stage.Popped += loud => { if (game.Local.Audio) game.Local.Audio.PlayBackfire(loud); };

            root = new GameObject("TuningGarage", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

            // Menu (en haut à gauche) : bandeau, fil d'Ariane, lignes, portefeuille ; sa hauteur suit le nombre de lignes
            var menu = Rect(screen, "Menu", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            menu.pivot = new Vector2(0f, 1f);
            menu.sizeDelta = new Vector2(580f, 0f);
            menu.anchoredPosition = new Vector2(56f, -44f);
            menuRt = menu;
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var banner = Bar(menu, 100f, new Color(0.02f, 0.025f, 0.04f, 0.92f), 0f);
            var accent = Rect(banner, "Accent", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(5f, 0f));
            accent.gameObject.AddComponent<Image>().color = Brand;
            var underline = Rect(banner, "Underline", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f));
            underline.gameObject.AddComponent<Image>().color = new Color(Brand.r, Brand.g, Brand.b, 0.5f);
            var title = Place(Label(banner, "", 40, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), new Vector2(26f, -10f), new Vector2(530f, 50f));
            Loc.Bind(title, "GARAGE DE TUNING", "TUNING GARAGE");
            carText = Place(Label(banner, "", 24, TextAnchor.MiddleLeft, Dim, FontStyle.BoldAndItalic), new Vector2(28f, -62f), new Vector2(530f, 30f));

            // Onglets : performances, cosmétique (LB / RB, Tab, ou clic)
            var tabBar = Bar(menu, 54f, new Color(0.01f, 0.012f, 0.02f, 0.94f), 0f);
            tabBar.GetComponent<LayoutElement>().flexibleHeight = 0f;
            var th = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.padding = new RectOffset(6, 6, 6, 6);
            th.spacing = 6;
            th.childControlWidth = true; th.childControlHeight = true;
            th.childForceExpandWidth = false; th.childForceExpandHeight = true;
            tabPrevText = TabGlyph(tabBar);
            var tabNames = new[] { ("PERFORMANCES", "PERFORMANCE"), ("COSMÉTIQUE", "COSMETICS") };
            for (int i = 0; i < tabNames.Length; i++) tabs.Add(MakeTab(tabBar, tabNames[i].Item1, tabNames[i].Item2, i));
            tabNextText = TabGlyph(tabBar);

            var crumb = Bar(menu, 44f, new Color(0.01f, 0.012f, 0.02f, 0.94f), 0f);
            crumbText = Label(crumb, "", 22, TextAnchor.MiddleLeft, White, FontStyle.Bold);
            Stretch(crumbText.rectTransform, 26f, 120f);
            countText = Label(crumb, "", 22, TextAnchor.MiddleRight, Dim, FontStyle.Bold);
            Stretch(countText.rectTransform, 300f, 24f);

            int count = Mathf.Max(Mathf.Max(CarTuning.PartCount, CarTuning.MaxOptions), Mathf.Max(LookRows, Mathf.Max(CarPaint.Count, CarCosmetics.MaxOptions)));
            for (int i = 0; i < count; i++)
            {
                int index = i;
                rows.Add(MakeRow(menu, () => Choose(index), true));
            }

            var purse = Bar(menu, 46f, new Color(0.01f, 0.012f, 0.02f, 0.94f), 0f);
            cashText = Label(purse, "", 24, TextAnchor.MiddleLeft, Money, FontStyle.BoldAndItalic);
            Stretch(cashText.rectTransform, 26f, 20f);
            // Sortir ou revenir (souris, doigt) : hors de la navigation de la liste
            var leave = MakeRow(menu, Back, false);
            Destroy(leave.button.GetComponent<HoverSelect>());
            leave.button.navigation = new Navigation { mode = Navigation.Mode.None };
            var leaveColors = leave.button.colors;
            leaveColors.selectedColor = leaveColors.normalColor;
            leave.button.colors = leaveColors;
            leave.button.GetComponent<LayoutElement>().preferredHeight = 46;
            leaveText = leave.name;
            hintText = Label(menu, "", 19, TextAnchor.MiddleLeft, Dim, FontStyle.Bold);
            hintText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;

            // Fiche de la pièce (en bas à droite)
            const float cardH = 520f;
            var card = Rect(screen, "Card", new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            card.pivot = new Vector2(1f, 0f);
            card.sizeDelta = new Vector2(710f, cardH);
            card.anchoredPosition = new Vector2(-50f, 44f);
            cardRt = card;
            Backdrop(card, cardH);
            // Liseré le long du bord gauche, à la même pente que la plaque
            Shape(card, "Stripe", Vector2.zero, Vector2.zero, new Vector2(4f, cardH), new Color(Brand.r, Brand.g, Brand.b, 0.7f)).Set(cardH * Slope, 0f, 0f);
            nameText = Place(Label(card, "", 40, TextAnchor.UpperLeft, White, FontStyle.BoldAndItalic), new Vector2(34f, -20f), new Vector2(460f, 50f));
            levelText = Place(Label(card, "", 24, TextAnchor.UpperRight, Brand, FontStyle.BoldAndItalic), new Vector2(480f, -28f), new Vector2(200f, 40f));
            subText = Place(Label(card, "", 22, TextAnchor.UpperLeft, Dim, FontStyle.Bold), new Vector2(34f, -70f), new Vector2(640f, 30f));
            for (int i = 0; i < Stats; i++)
            {
                float y = -118f - i * 52f;
                var label = Place(Label(card, "", 21, TextAnchor.MiddleLeft, White, FontStyle.Normal), new Vector2(34f, y), new Vector2(250f, 30f));
                label.text = Loc.T(StatLabels[0, i, 0], StatLabels[0, i, 1]);
                var bg = Rect(card, "Bar", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(290f, y - 19f), new Vector2(470f, y - 11f));
                bg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
                var gain = Rect(bg, "Gain", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                gain.pivot = new Vector2(0f, 0.5f);
                gain.gameObject.AddComponent<Image>().color = Brand;
                var loss = Rect(bg, "Loss", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                loss.pivot = new Vector2(0f, 0.5f);
                loss.gameObject.AddComponent<Image>().color = Danger;
                var fill = Rect(bg, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.gameObject.AddComponent<Image>().color = Info;
                var value = Place(Label(card, "", 21, TextAnchor.MiddleRight, Accent, FontStyle.Bold), new Vector2(480f, y), new Vector2(200f, 30f));
                bars.Add((fill, gain, loss, label, value, bg));
            }
            actionText = Place(Label(card, "", 27, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), new Vector2(34f, -440f), new Vector2(650f, 40f));
            // Textes et barres de la fiche : leur marge suit le bord penché de la plaque
            foreach (RectTransform child in card)
            {
                if (child.name == "Plate" || child.name == "Stripe") continue;
                float yc = cardH + child.anchoredPosition.y;          // milieu de la ligne, depuis le bas de la fiche
                child.anchoredPosition += new Vector2(Slope * (yc - cardH * 0.5f), 0f);
            }
            root.SetActive(false);
        }

        /// <summary>Bandeau du menu : plaque pleine largeur, d'une hauteur donnée dans la mise en page.</summary>
        static RectTransform Bar(Transform parent, float height, Color color, float skew)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var shape = go.GetComponent<HudShape>();
            shape.Set(skew, 0f, 0f);
            shape.color = color;
            shape.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        /// <summary>Ligne du menu : sombre, blanche à texte foncé quand elle est choisie.</summary>
        Row MakeRow(Transform parent, UnityEngine.Events.UnityAction onClick, bool withPips)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button), typeof(LayoutElement), typeof(HoverSelect));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 48;
            var shape = go.GetComponent<HudShape>();
            shape.Set(0f, 0f, 0f);
            shape.color = Color.white;
            var b = go.GetComponent<Button>();
            b.targetGraphic = shape;
            var colors = b.colors;
            colors.normalColor = new Color(0.04f, 0.05f, 0.07f, 0.8f);
            colors.highlightedColor = new Color(0.16f, 0.18f, 0.23f, 0.92f);
            colors.selectedColor = new Color(0.95f, 0.96f, 0.97f, 1f);
            colors.pressedColor = Brand;
            colors.fadeDuration = 0.06f;
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var name = Label(go.transform, "", 24, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic);
            Stretch(name.rectTransform, 26f, 260f);
            name.GetComponent<Shadow>().enabled = false;
            var status = Label(go.transform, "", 22, TextAnchor.MiddleRight, Accent, FontStyle.Bold);
            Stretch(status.rectTransform, 340f, 24f);
            status.GetComponent<Shadow>().enabled = false;
            var pips = new Image[withPips ? CarTuning.MaxLevel : 0];
            for (int k = 0; k < pips.Length; k++)
            {
                // Niveaux : trois pastilles inclinées entre le nom et le prix
                var pip = Rect(go.transform, "Pip", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
                pip.sizeDelta = new Vector2(22f, 14f);
                pip.anchoredPosition = new Vector2(-232f + k * 28f, 0f);
                pip.localRotation = Quaternion.Euler(0f, 0f, -12f);
                var img = pip.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = PipOff;
                pips[k] = img;
            }
            Image swatch = null;
            if (withPips)
            {
                // Peinture : pastille de couleur à la place des niveaux, cernée pour rester lisible sur la ligne blanche
                var sw = Rect(go.transform, "Swatch", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
                sw.sizeDelta = new Vector2(78f, 20f);
                sw.anchoredPosition = new Vector2(-204f, 0f);
                swatch = sw.gameObject.AddComponent<Image>();
                swatch.raycastTarget = false;
                var ring = sw.gameObject.AddComponent<Outline>();
                ring.effectColor = new Color(0f, 0f, 0f, 0.6f);
                ring.effectDistance = new Vector2(2f, -2f);
                sw.gameObject.SetActive(false);
            }
            return new Row { button = b, name = name, status = status, pips = pips, swatch = swatch };
        }

        /// <summary>Onglet : plaque cliquable hors de la navigation de la liste, trait rose sous l'onglet ouvert.</summary>
        (HudShape plate, Text label, Image mark) MakeTab(Transform parent, string fr, string en, int index)
        {
            var go = new GameObject("Tab", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var shape = go.GetComponent<HudShape>();
            shape.Set(10f, 0f, 0f);
            shape.color = new Color(0.02f, 0.025f, 0.04f, 0.85f);
            var b = go.GetComponent<Button>();
            b.targetGraphic = shape;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => SwitchTab(index));
            var label = Label(go.transform, "", 22, TextAnchor.MiddleCenter, White, FontStyle.BoldAndItalic);
            Stretch(label.rectTransform, 8f, 8f);
            Loc.Bind(label, fr, en);
            var mark = Rect(go.transform, "Active", new Vector2(0.1f, 0f), new Vector2(0.9f, 0f), new Vector2(0f, 0f), new Vector2(0f, 4f));
            var img = mark.gameObject.AddComponent<Image>();
            img.color = Brand;
            img.raycastTarget = false;
            return (shape, label, img);
        }

        /// <summary>Repère de touche (LB / RB, Tab) au bord de la rangée d'onglets.</summary>
        Text TabGlyph(Transform parent)
        {
            var t = Label(parent, "", 19, TextAnchor.MiddleCenter, Dim, FontStyle.Bold);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 46;
            return t;
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

        /// <summary>Texte posé en haut à gauche d'un panneau, à une position donnée.</summary>
        static Text Place(Text t, Vector2 pos, Vector2 size)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos + new Vector2(0f, -size.y * 0.5f);
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

        /// <summary>
        /// Pente des plaques inclinées (décalage horizontal par pixel de hauteur, environ 8°, comme à la concession) :
        /// le même angle quelle que soit la hauteur de la plaque.
        /// </summary>
        const float Slope = 0.14f;

        /// <summary>Fond d'un panneau : plaque inclinée sur toute la surface, à la pente commune.</summary>
        static void Backdrop(RectTransform panel, float height)
        {
            var s = Shape(panel, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.025f, 0.03f, 0.045f, 0.78f));
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
