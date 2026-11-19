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
    /// Style verre : un seul panneau à gauche (la liste, puis les options de la ligne ouverte à la place), la fiche en
    /// bas à droite. Onglets affichés PIÈCES / PEINTURE / COSMÉTIQUE (LB / RB, Tab, ou clic ; la peinture est le
    /// nuancier de l'onglet cosmétique). Deux onglets internes : PERFORMANCES (les pièces ci-dessus) et COSMÉTIQUE, sans effet sur la
    /// conduite : la peinture (nuancier de <see cref="CarPaint"/>), puis les accessoires de <see cref="CarCosmetics"/>
    /// (néons, vitres teintées, pop and bang, couleur des flammes), essayés en direct sur la voiture de l'atelier — le pop
    /// and bang et les flammes y pétaradent en démonstration. Même règle d'achat : payé une fois, remonté gratuitement.
    /// En réseau, le monde continue pendant ce temps.
    /// </summary>
    public class TuningGarage : MonoBehaviour
    {
        // Style verre (Glass) : or = sélection, argent, touches ; vert = gain ou monté ; corail = perte ou manque
        static readonly Color White = Glass.White;
        static readonly Color Accent = Glass.Gold;
        /// <summary>Rose de la marque : seulement la lumière de la baie dans le monde (repère du lieu).</summary>
        static readonly Color Brand = new Color(1f, 0.42f, 0.82f);
        static readonly Color Danger = Glass.Bad;
        static readonly Color Dim = Glass.Dim;
        static readonly Color Money = Glass.Good;
        static readonly Color PipOff = new Color(1f, 1f, 1f, 0.18f);
        const string GoldTag = "#FFC233";

        /// <summary>Baie ouverte de l'atelier dans le repère du bâtiment (sur la façade).</summary>
        static readonly Vector3 DoorLocal = new Vector3(-3.6f, 0f, 8f);
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

        /// <summary>Le joueur est aux abords du garage : le HUD indique d'entrer par la baie ouverte.</summary>
        public bool Near => door != null && door.Near;

        class Row
        {
            public Button button;
            public Text name, status;
            public Image[] pips;
            /// <summary>Pastille de couleur (peinture).</summary>
            public GlassRect swatch;
            /// <summary>Fond de la ligne : transparent, or doux cerné d'or quand elle est choisie.</summary>
            public GlassRect plate;
            public LayoutElement layout;
        }

        GameManager game;
        GarageDoorway door;
        GameObject root;
        WorkshopStage stage;
        RectTransform screenRt, menuRt, cardRt;
        Text carText, cashText, walletText, crumbText, countText, chevronText, kickerText, nameText, subText, levelText, actionText, actionPrice, actionKey, hintText, leaveText, tabKeysText;
        GlassRect actionPlate, actionChip;
        LayoutElement leaveLayout;
        /// <summary>Hauteur prise par l'en-tête du menu (titre, onglets, marges), pour répartir le reste entre les lignes.</summary>
        float menuChrome;
        readonly List<Row> rows = new List<Row>();
        readonly List<(RectTransform fill, RectTransform gain, RectTransform loss, Text label, Text value, RectTransform bg)> bars = new List<(RectTransform, RectTransform, RectTransform, Text, Text, RectTransform)>();
        /// <summary>Onglets texte : libellé, trait or sous l'onglet ouvert (pièces, peinture, cosmétique).</summary>
        readonly List<(Text label, Image mark)> tabs = new List<(Text, Image)>();
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
            Current = this;
            door = new GarageDoorway(g, "tuning_garage", DoorLocal, Brand);
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
                door.Tick(CanDriveIn(), Open);
                return;
            }
            float dt = Time.unscaledDeltaTime;
            if (Time.frameCount != openedFrame && (GameInput.CancelPressed || GameInput.PausePressed)) { Back(); return; }
            if (Time.frameCount != openedFrame && (GameInput.TabPrevPressed || GameInput.TabNextPressed)) GoTab(VisualTab + (GameInput.TabNextPressed ? 1 : -1));

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

        /// <summary>Le joueur peut entrer en voiture : en jeu, sans menu ouvert ni course en cours.</summary>
        bool CanDriveIn()
        {
            if (GameManager.DedicatedServer || game.State != GameManager.Phase.Playing) return false;
            if ((game.Hud && game.Hud.Paused) || OptionsMenu.IsOpen || WorldMap.IsOpen || Dealership.IsOpen) return false;
            return !game.Races || (!game.Races.InvitePending && game.Races.Active == null);
        }

        /// <summary>Voiture que l'on conduit (fiche d'origine, sans améliorations).</summary>
        VehicleSpec BaseSpec => game.assets.playerCars[Mathf.Clamp(game.Local.CarIndex, 0, game.assets.playerCars.Length - 1)];

        public void Open()
        {
            if (IsOpen) return;
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

        /// <summary>
        /// Onglet affiché : PIÈCES, PEINTURE (le nuancier de l'onglet cosmétique), COSMÉTIQUE (la liste et les
        /// accessoires). La peinture reste aussi la première ligne de la liste cosmétique.
        /// </summary>
        int VisualTab => tab == TabParts ? 0 : InPaint ? 1 : 2;

        /// <summary>Va à un onglet affiché (en boucle) ; voiture sans peinture : on reste sur la liste cosmétique.</summary>
        void GoTab(int v)
        {
            v = (v % 3 + 3) % 3;
            if (v == VisualTab) return;
            if (v == 0) { SwitchTab(TabParts); return; }
            if (tab != TabLooks) SwitchTab(TabLooks);
            if (v == 1) { if (!InPaint) EnterPart(0); }
            else if (part >= 0) Back();
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
            door.Leave();                          // la voiture ressort toute seule par la baie
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
            FitRows();
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

        /// <summary>Lignes de 75 px (maquette) ; le nuancier, plus long, se resserre pour tenir dans la hauteur du panneau.</summary>
        void FitRows()
        {
            float room = (menuRt.rect.height > 1f ? menuRt.rect.height : 972f) - menuChrome;
            float rowH = Mathf.Round(Mathf.Clamp(room / Mathf.Max(RowCount, 1) - 4f, 36f, 75f));
            foreach (var r in rows)
                if (!Mathf.Approximately(r.layout.preferredHeight, rowH)) r.layout.preferredHeight = rowH;
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
            FitRows();
            // Fenêtre étroite ou basse : la fiche rétrécit (le menu suit la hauteur de l'écran)
            float fit = Mathf.Min((screenRt.rect.width - 780f) / CardW, (screenRt.rect.height - 108f) / CardH);
            cardRt.localScale = Vector3.one * Mathf.Clamp(fit, 0.55f, 1f);
            carText.text = spec.displayName;
            walletText.text = Glass.Spaced(Loc.T("PORTEFEUILLE", "WALLET"), 2);
            cashText.text = Wallet.Format(cash);

            // Fil d'Ariane : la liste de l'onglet, ou « ‹ Pièces › Pneus » et les options possédées sur le total
            bool deep = part >= 0;
            string parent = tab == TabParts ? Loc.T("Pièces", "Parts") : Loc.T("Cosmétique", "Cosmetics");
            string leaf = tab == TabParts ? (deep ? CarTuning.Name((CarTuning.Part)part) : "")
                        : InPaint ? Loc.T("Peinture", "Paint") : deep ? CarCosmetics.Name(LookItem) : "";
            crumbText.text = deep ? $"{parent}  ›  <color=#FFFFFF>{leaf}</color>"
                           : tab == TabParts ? parent : Loc.T("Peinture et accessoires", "Paint and accessories");
            chevronText.enabled = deep;
            crumbText.rectTransform.offsetMin = new Vector2(deep ? 54f : 27f, 0f);
            countText.text = deep ? $"{OwnedOptions(spec)} / {count}" : shown >= 0 ? $"{shown + 1} / {count}" : "";
            leaveText.text = "‹   " + (part < 0 ? Loc.T("Sortir", "Leave") : Loc.T("Retour", "Back"));
            bool pad = GameInput.UsingGamepad;
            int vt = VisualTab;
            for (int k = 0; k < tabs.Count; k++)
            {
                bool open = k == vt;
                tabs[k].label.color = open ? Accent : Dim;
                tabs[k].label.font = Glass.FontOf(open ? Glass.Weight.Semibold : Glass.Weight.Regular);
                tabs[k].mark.enabled = open;
            }
            tabKeysText.text = pad ? "LB   RB" : "Tab";

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
                // Ligne choisie : or doux cerné d'or, texte un peu plus appuyé
                StyleRow(row, on);
                if (tab == TabLooks && part < 0 && i > 0)
                {
                    // Accessoire : l'option montée, sa couleur dans la pastille
                    var item = (CarCosmetics.Item)(i - 1);
                    row.name.text = CarCosmetics.Name(item);
                    bool can = CarCosmetics.Available(spec, item);
                    row.swatch.color = can ? CarCosmetics.Swatch(item, fitted[item]) : Color.clear;
                    row.status.text = can ? CarCosmetics.OptionName(item, fitted[item]).ToUpperInvariant() : Loc.T("INDISPONIBLE", "N/A");
                    row.status.color = Dim;
                }
                else if (InLook)
                {
                    var item = LookItem;
                    row.name.text = CarCosmetics.OptionName(item, i);
                    row.swatch.color = CarCosmetics.Swatch(item, i);
                    if (i == fitted[item]) { row.status.text = Loc.T("INSTALLÉ", "FITTED"); row.status.color = Money; }
                    else if (CarCosmetics.Owns(spec, item, i))
                    {
                        row.status.text = i == CarCosmetics.Stock(item) ? Loc.T("D'ORIGINE", "STOCK")
                                        : CarCosmetics.OptionPrice(spec, item, i) == 0 ? Loc.T("GRATUIT", "FREE") : Loc.T("ACHETÉ", "OWNED");
                        row.status.color = Dim;
                    }
                    else
                    {
                        int price = CarCosmetics.Price(spec, item, i);
                        row.status.text = PriceText(price);
                        row.status.color = cash >= price ? Accent : Danger;
                    }
                }
                else if (tab == TabLooks && part < 0)
                {
                    row.name.text = Loc.T("Peinture", "Paint");
                    row.swatch.color = CarPaint.Shade(spec, colour);
                    row.status.text = CarPaint.Paintable(spec) ? CarPaint.Name(colour).ToUpperInvariant() : Loc.T("D'ORIGINE", "FACTORY");
                    row.status.color = Dim;
                }
                else if (InPaint)
                {
                    row.name.text = CarPaint.Name(i);
                    row.swatch.color = CarPaint.Shade(spec, i);
                    // Textes courts : la pastille est juste à gauche
                    if (i == colour) { row.status.text = Loc.T("ACTUELLE", "CURRENT"); row.status.color = Money; }
                    else if (CarPaint.Owns(spec, i)) { row.status.text = i == 0 ? Loc.T("OFFERTE", "FREE") : Loc.T("ACHETÉE", "OWNED"); row.status.color = Dim; }
                    else
                    {
                        int price = CarPaint.Price(spec, i);
                        row.status.text = PriceText(price);
                        row.status.color = cash >= price ? Accent : Danger;
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
                        row.status.color = Dim;
                        continue;
                    }
                    int price = CarTuning.NextPrice(spec, p);
                    for (int k = 0; k < row.pips.Length; k++)
                        row.pips[k].color = k < level ? Accent : PipOff;
                    bool bought = price >= 0 && CarTuning.Owns(spec, p, level + 1);
                    row.status.text = bought ? Loc.T("ACHETÉ", "OWNED") : PriceText(price);
                    row.status.color = price < 0 ? Money : bought ? Dim : cash >= price ? Accent : Danger;
                }
                else
                {
                    var cp = (CarTuning.Part)part;
                    row.name.text = CarTuning.OptionName(cp, i);
                    if (i == current) { row.status.text = Loc.T("INSTALLÉ", "FITTED"); row.status.color = Money; }
                    else if (CarTuning.Owns(spec, cp, i)) { row.status.text = i == 0 ? Loc.T("D'ORIGINE", "STOCK") : Loc.T("ACHETÉ", "OWNED"); row.status.color = Dim; }
                    else
                    {
                        int price = CarTuning.Price(spec, cp, i);
                        row.status.text = PriceText(price);
                        row.status.color = cash >= price ? Accent : Danger;
                    }
                }
            }

            if (shown < 0) return;
            string ok = pad ? "A" : Loc.T("Entrée", "Enter");
            string back = pad ? "B" : Loc.T("Échap", "Esc");
            actionKey.text = ok;
            // Aide en bas du menu : les touches en or
            string turn = pad ? Loc.T($"{K("joystick droit")} : tourner", $"{K("right stick")}: rotate")
                              : Loc.T($"{K("clic droit")} : tourner", $"{K("right click")}: rotate");
            string keys = K("↑ ↓");
            ok = K(ok);
            back = K(back);
            if (InPaint || (tab == TabLooks && part < 0 && shown == 0)) { RefreshPaint(spec, cash, ok, back, turn, keys); return; }
            if (tab == TabLooks) { RefreshLook(spec, cash, ok, back, turn, keys); return; }

            // Fiche : la pièce regardée (ou ouverte), et ce qu'apporte l'option visée
            var sel = (CarTuning.Part)(part < 0 ? shown : part);
            bool typed = CarTuning.IsTyped(sel);
            int lv = CarTuning.Level(spec, sel);
            int target = part >= 0 ? shown : typed ? lv : Mathf.Min(lv + 1, CarTuning.MaxLevel);
            string option = CarTuning.OptionName(sel, target);
            kickerText.text = Glass.Spaced(CarTuning.Name(sel).ToUpperInvariant(), 2);
            nameText.text = option;
            subText.text = part >= 0 && typed ? CarTuning.OptionEffect(sel, target) : CarTuning.Effect(sel);
            levelText.text = typed ? CarTuning.OptionName(sel, lv) : Loc.F($"Niveau {lv} / {CarTuning.MaxLevel}", $"Level {lv} / {CarTuning.MaxLevel}");

            var now = CarTuning.Apply(spec);
            ShowBars(CardMode(sel), now, target != lv ? CarTuning.Preview(spec, sel, target) : now);

            int cost = CarTuning.Price(spec, sel, target);
            bool has = CarTuning.Owns(spec, sel, target);
            if (message != null) Action(message, messageColor);
            else if (part < 0 && typed) Action(Loc.T($"Voir les {CarTuning.Options(sel)} types", $"See the {CarTuning.Options(sel)} types"), White, key: true);
            else if (part < 0 && lv >= CarTuning.MaxLevel) Action(Loc.T("Voir les niveaux  ·  pièce au maximum", "See the levels  ·  part fully upgraded"), Money, key: true);
            else if (part < 0) Action(Loc.T("Voir les niveaux  ·  suivant", "See the levels  ·  next"), White, has ? Loc.T("acheté", "owned") : PriceText(cost), has ? Dim : cash >= cost ? Accent : Danger, true);
            else if (target == lv) Action(typed ? Loc.T("Monté sur la voiture", "On the car") : Loc.T("Niveau installé", "Level fitted"), Money);
            else if (target == 0) Action(Loc.T("Remettre d'origine", "Back to stock"), White, Loc.T("gratuit", "free"), Money, true);
            else if (has) Action(Loc.T($"Remonter {option}", $"Refit {option}"), White, Loc.T("déjà acheté", "already owned"), Dim, true);
            else if (cash < cost) Action(Loc.T($"Il te manque {Wallet.Format(cost - cash)}", $"You need {Wallet.Format(cost - cash)} more"), Danger, PriceText(cost), Danger);
            else Action(Loc.T($"Installer {option}", $"Fit {option}"), White, PriceText(cost), Accent, true);

            hintText.text = part < 0
                ? Loc.T($"{keys} choisir  ·  {ok} ouvrir  ·  {back} sortir  ·  {turn}", $"{keys} browse  ·  {ok} open  ·  {back} leave  ·  {turn}")
                : Loc.T($"{keys} choisir  ·  {ok} installer  ·  {back} retour  ·  {turn}", $"{keys} browse  ·  {ok} fit  ·  {back} back  ·  {turn}");
        }

        /// <summary>Touche de l'aide, en or.</summary>
        static string K(string key) => $"<color={GoldTag}>{key}</color>";

        /// <summary>Options possédées de la ligne ouverte (fil d'Ariane).</summary>
        int OwnedOptions(VehicleSpec spec)
        {
            if (InPaint) return CarPaint.OwnedCount(spec);
            if (InLook) return CarCosmetics.OwnedCount(spec, LookItem);
            var p = (CarTuning.Part)part;
            int n = 0;
            for (int i = 0; i < CarTuning.Options(p); i++) if (CarTuning.Owns(spec, p, i)) n++;
            return n;
        }

        /// <summary>Fond de ligne : rien au repos, or doux cerné d'or et halo quand elle est choisie (redessiné seulement au changement).</summary>
        static void StyleRow(Row row, bool on)
        {
            row.name.color = White;
            row.name.font = Glass.FontOf(on ? Glass.Weight.Regular : Glass.Weight.Light);
            row.status.font = Glass.FontOf(on ? Glass.Weight.Semibold : Glass.Weight.Regular);
            float edge = on ? 2.25f : 0f;
            if (!Mathf.Approximately(row.plate.border, edge))
                row.plate.Set(on ? Glass.GoldSoft : Color.clear, Accent, edge, on ? 8f : 0f);
        }

        /// <summary>
        /// Ligne d'action de la fiche : pilule cernée d'or quand l'appui fait quelque chose (touche à droite, prix juste
        /// avant), cernée de corail pour un manque d'argent, neutre pour un simple état.
        /// </summary>
        void Action(string text, Color color, string price = null, Color? priceColor = null, bool key = false)
        {
            actionText.text = text;
            actionText.color = color;
            actionPrice.text = price ?? "";
            actionPrice.color = priceColor ?? Accent;
            actionChip.gameObject.SetActive(key);
            bool bad = color == Danger;
            Color fill = key ? Glass.GoldSoft : bad ? Glass.A(Danger, 0.12f) : Glass.Pill;
            Color edge = key ? Glass.A(Accent, 0.6f) : bad ? Glass.A(Danger, 0.6f) : Glass.Edge;
            if (actionPlate.color != fill || actionPlate.borderColor != edge)
            {
                actionPlate.borderColor = edge;
                actionPlate.color = fill;
                actionPlate.SetVerticesDirty();
            }
            float chipW = key ? Mathf.Max(72f, actionKey.preferredWidth + 42f) : 0f;
            actionChip.rectTransform.sizeDelta = new Vector2(chipW, 54f);
            actionPrice.rectTransform.anchoredPosition = new Vector2(key ? -(chipW + 30f) : -30f, 0f);
        }

        /// <summary>Fiche de la peinture : sans effet sur la conduite (barres inchangées), la couleur regardée et son prix.</summary>
        void RefreshPaint(VehicleSpec spec, int cash, string ok, string back, string turn, string keys)
        {
            bool can = CarPaint.Paintable(spec);
            int colour = CarPaint.Selected(spec);
            kickerText.text = Glass.Spaced(Loc.T("PEINTURE", "PAINT"), 2);
            nameText.text = !can ? Loc.T("D'origine", "Factory") : CarPaint.Name(InPaint ? shown : colour);
            subText.text = Loc.T("Couleur de carrosserie : sans effet sur la conduite", "Body colour: no effect on handling");
            levelText.text = can ? Loc.F($"{CarPaint.OwnedCount(spec)} / {CarPaint.Count} couleurs", $"{CarPaint.OwnedCount(spec)} / {CarPaint.Count} colours") : "";
            ShowLook(spec, InPaint ? shown : colour, CarCosmetics.Fitted(spec));

            int price = InPaint ? CarPaint.Price(spec, shown) : 0;
            if (message != null) Action(message, messageColor);
            else if (!can) Action(Loc.T("Cette voiture garde sa livrée d'origine", "This car keeps its factory livery"), Dim);
            else if (!InPaint) Action(Loc.T($"Voir les couleurs  ·  {CarPaint.Count - 1} au choix", $"See the colours  ·  {CarPaint.Count - 1} to choose from"), White, key: true);
            else if (shown == colour) Action(Loc.T("Couleur actuelle de la voiture", "The car's current colour"), Money);
            else if (CarPaint.Owns(spec, shown)) Action(Loc.T("Repeindre", "Repaint"), White, Loc.T("déjà achetée", "already owned"), Dim, true);
            else if (cash < price) Action(Loc.T($"Il te manque {Wallet.Format(price - cash)}", $"You need {Wallet.Format(price - cash)} more"), Danger, PriceText(price), Danger);
            else Action(Loc.T("Acheter et peindre", "Buy and paint"), White, PriceText(price), Accent, true);

            hintText.text = InPaint
                ? Loc.T($"{keys} essayer  ·  {ok} peindre  ·  {back} retour  ·  {turn}", $"{keys} try on  ·  {ok} paint  ·  {back} back  ·  {turn}")
                : Loc.T($"{keys} choisir  ·  {ok} ouvrir  ·  {back} sortir  ·  {turn}", $"{keys} browse  ·  {ok} open  ·  {back} leave  ·  {turn}");
        }

        /// <summary>Fiche d'un accessoire cosmétique : ce qu'il fait, l'option visée et son prix, l'habillage complet de la voiture.</summary>
        void RefreshLook(VehicleSpec spec, int cash, string ok, string back, string turn, string keys)
        {
            var item = part < 0 ? (CarCosmetics.Item)(shown - 1) : LookItem;
            bool can = CarCosmetics.Available(spec, item);
            int fitted = CarCosmetics.Fitted(spec, item);
            int target = part < 0 ? fitted : shown;
            string option = CarCosmetics.OptionName(item, target);
            kickerText.text = Glass.Spaced(CarCosmetics.Name(item).ToUpperInvariant(), 2);
            nameText.text = can ? option : CarCosmetics.Name(item);
            subText.text = part < 0 ? CarCosmetics.Effect(item) : CarCosmetics.OptionEffect(item, target);
            levelText.text = can ? Loc.F($"{CarCosmetics.OwnedCount(spec, item)} / {CarCosmetics.Options(item)}") : "";
            ShowLook(spec, CarPaint.Selected(spec), ShownLook());

            int cost = CarCosmetics.Price(spec, item, target);
            if (message != null) Action(message, messageColor);
            else if (!can) Action(CarCosmetics.Unavailable(item), Dim);
            else if (part < 0) Action(Loc.T($"Voir les {CarCosmetics.Options(item)} options", $"See the {CarCosmetics.Options(item)} options"), White, key: true);
            else if (target == fitted) Action(Loc.T("Monté sur la voiture", "On the car"), Money);
            else if (target == CarCosmetics.Stock(item)) Action(Loc.T("Remettre d'origine", "Back to stock"), White, Loc.T("gratuit", "free"), Money, true);
            else if (CarCosmetics.Owns(spec, item, target))
            {
                bool free = CarCosmetics.OptionPrice(spec, item, target) == 0;
                Action(Loc.T($"Monter {option}", $"Fit {option}"), White, free ? Loc.T("gratuit", "free") : Loc.T("déjà acheté", "already owned"), free ? Money : Dim, true);
            }
            else if (cash < cost) Action(Loc.T($"Il te manque {Wallet.Format(cost - cash)}", $"You need {Wallet.Format(cost - cash)} more"), Danger, PriceText(cost), Danger);
            else Action(Loc.T($"Installer {option}", $"Fit {option}"), White, PriceText(cost), Accent, true);

            hintText.text = part < 0
                ? Loc.T($"{keys} choisir  ·  {ok} ouvrir  ·  {back} sortir  ·  {turn}", $"{keys} browse  ·  {ok} open  ·  {back} leave  ·  {turn}")
                : Loc.T($"{keys} essayer  ·  {ok} installer  ·  {back} retour  ·  {turn}", $"{keys} try on  ·  {ok} fit  ·  {back} back  ·  {turn}");
        }

        /// <summary>
        /// Fiche de l'onglet cosmétique : à la place des barres, l'habillage de la voiture ligne par ligne (peinture et
        /// accessoires), le changement essayé en or (« actuel → essayé »).
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
                bar.label.color = White;
                bar.value.text = changed ? now + "  →  " + then : now;
                bar.value.color = changed ? Accent : Dim;
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
                // Le plein (blanc) s'arrête à la valeur qui reste ; au-delà, le gain (vert) ou ce qui serait perdu (corail)
                bars[i].fill.localScale = new Vector3(Mathf.Min(barA, barB), 1f, 1f);
                bars[i].gain.localScale = new Vector3(lost ? 0f : barB, 1f, 1f);
                bars[i].loss.localScale = new Vector3(lost ? barA : 0f, 1f, 1f);
                Color tint = lost ? Danger : Money;
                bars[i].label.color = White;
                bars[i].value.text = changed ? Format(mode, i, a) + "  →  " + Format(mode, i, b) : Format(mode, i, a);
                bars[i].value.color = changed ? tint : White;
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

        /// <summary>Fiche (en bas à droite), aux mesures de la maquette (×1,5 pour 1920×1080).</summary>
        const float CardW = 780f, CardH = 564f;

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
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var screen = (RectTransform)root.transform;
            screenRt = screen;

            // Menu (à gauche, sur toute la hauteur, l'atelier reste visible derrière) : titre et portefeuille, onglets,
            // fil d'Ariane, lignes ; l'aide des touches en bas
            var menu = Glass.Rect(screen, "Menu", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(60f, 54f), new Vector2(660f, -54f));
            menuRt = menu;
            Glass.Backdrop(menu, 48f);
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 36, 27);
            layout.spacing = 4f;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;

            var head = Block(menu, 78f);
            Glass.Label(head, Glass.Spaced("TUNING", 1), 48, Glass.Weight.Light, White, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(27f, 6f), new Vector2(330f, 60f));
            carText = Glass.Label(head, "", 20, Glass.Weight.Light, Dim, TextAnchor.LowerLeft, new Vector2(0f, 1f), new Vector2(29f, -50f), new Vector2(330f, 26f));
            walletText = Glass.Kicker(head, "", 16);
            Pin(walletText, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-27f, -14f), new Vector2(260f, 22f));
            cashText = Glass.Label(head, "", 27, Glass.Weight.Regular, Accent, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-27f, -38f), new Vector2(260f, 36f));

            // Onglets texte, trait or sous l'onglet ouvert (LB / RB, Tab, ou clic)
            Gap(menu, 6f);
            var tabRow = Block(menu, 54f);
            var rule = Glass.Bar(tabRow, "Rule", Glass.A(White, 0.22f)).rectTransform;
            rule.anchorMin = Vector2.zero; rule.anchorMax = new Vector2(1f, 0f);
            rule.offsetMin = new Vector2(27f, 0f); rule.offsetMax = new Vector2(-27f, 1.5f);
            var strip = Glass.Rect(tabRow, "Tabs", Vector2.zero, Vector2.one, new Vector2(27f, 0f), new Vector2(-27f, 0f));
            var th = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 33;
            th.childControlWidth = true; th.childControlHeight = true;
            th.childForceExpandWidth = false; th.childForceExpandHeight = true;
            var tabNames = new[] { ("PIÈCES", "PARTS"), ("PEINTURE", "PAINT"), ("COSMÉTIQUE", "COSMETICS") };
            for (int i = 0; i < tabNames.Length; i++) tabs.Add(MakeTab(strip, tabNames[i].Item1, tabNames[i].Item2, i));
            Gap(strip, 0f).flexibleWidth = 1f;
            tabKeysText = Glass.Label(strip, "", 18, Glass.Weight.Semibold, Accent, TextAnchor.MiddleRight);
            Gap(menu, 12f);

            // Fil d'Ariane : « ‹ Pièces › Pneus » et le compte ; clic : retour à la liste
            var crumbGo = new GameObject("Crumb", typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button), typeof(LayoutElement));
            crumbGo.transform.SetParent(menu, false);
            var crumbPlate = crumbGo.GetComponent<GlassRect>();
            crumbPlate.Set(Color.clear, Glass.Edge, 0f);
            crumbPlate.radius = 27f;
            var crumbButton = crumbGo.GetComponent<Button>();
            crumbButton.targetGraphic = crumbPlate;
            crumbButton.transition = Selectable.Transition.None;
            crumbButton.navigation = new Navigation { mode = Navigation.Mode.None };
            crumbButton.onClick.AddListener(() => { if (part >= 0) Back(); });
            crumbGo.GetComponent<LayoutElement>().preferredHeight = 56f;
            chevronText = Glass.Label(crumbGo.transform, "‹", 38, Glass.Weight.Light, Glass.Cyan, TextAnchor.MiddleLeft);
            Stretch(chevronText.rectTransform, 25f, 0f);
            chevronText.rectTransform.offsetMin += new Vector2(0f, 4f);
            crumbText = Glass.Label(crumbGo.transform, "", 22, Glass.Weight.Regular, Glass.Cyan, TextAnchor.MiddleLeft);
            Stretch(crumbText.rectTransform, 54f, 120f);
            countText = Glass.Label(crumbGo.transform, "", 20, Glass.Weight.Regular, Dim, TextAnchor.MiddleRight);
            Stretch(countText.rectTransform, 300f, 27f);

            int count = Mathf.Max(Mathf.Max(CarTuning.PartCount, CarTuning.MaxOptions), Mathf.Max(LookRows, Mathf.Max(CarPaint.Count, CarCosmetics.MaxOptions)));
            for (int i = 0; i < count; i++)
            {
                int index = i;
                rows.Add(MakeRow(menu, () => Choose(index), true));
            }

            // L'aide reste en bas du panneau
            Gap(menu, 0f).flexibleHeight = 1f;
            // Sortir ou revenir (souris, doigt) : hors de la navigation de la liste, discret
            var leave = MakeRow(menu, Back, false);
            Destroy(leave.button.GetComponent<HoverSelect>());
            leave.button.navigation = new Navigation { mode = Navigation.Mode.None };
            leave.plate.Set(Color.white, Glass.Edge, 0f);
            leave.button.transition = Selectable.Transition.ColorTint;
            var leaveColors = leave.button.colors;
            leaveColors.normalColor = Color.clear;
            leaveColors.highlightedColor = Glass.Pill;
            leaveColors.selectedColor = Color.clear;
            leaveColors.pressedColor = Glass.GoldSoft;
            leaveColors.colorMultiplier = 1f;
            leaveColors.fadeDuration = 0.06f;
            leave.button.colors = leaveColors;
            leaveLayout = leave.layout;
            leaveLayout.preferredHeight = 44f;
            leaveText = leave.name;
            leaveText.fontSize = 20;
            leaveText.color = Dim;
            var hintBox = Block(menu, 50f);
            hintText = Glass.Label(hintBox, "", 18, Glass.Weight.Light, White, TextAnchor.MiddleLeft);
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText.lineSpacing = 0.95f;
            Stretch(hintText.rectTransform, 27f, 18f);
            // En-tête, onglets, fil d'Ariane, sortie, aide et marges : le reste de la hauteur va aux lignes
            menuChrome = 36f + 27f + 78f + 6f + 54f + 12f + 56f + 44f + 50f + 9f * 4f;

            // Fiche de la pièce (en bas à droite) : catégorie en cyan, option en texte fin, effet, barres, ligne d'action
            var card = Glass.Place(screen, "Card", new Vector2(1f, 0f), new Vector2(-60f, 54f), new Vector2(CardW, CardH));
            cardRt = card;
            Glass.Backdrop(card, 45f);
            kickerText = Glass.Kicker(card, "", 16);
            Pin(kickerText, TextAnchor.LowerLeft, new Vector2(0f, 1f), new Vector2(39f, -24f), new Vector2(480f, 24f));
            nameText = Glass.Label(card, "", 45, Glass.Weight.Light, White, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(37f, -48f), new Vector2(440f, 56f));
            levelText = Glass.Label(card, "", 21, Glass.Weight.Light, White, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(-39f, -62f), new Vector2(300f, 36f));
            subText = Glass.Label(card, "", 22, Glass.Weight.Light, White, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(39f, -108f), new Vector2(702f, 32f));
            var divider = Glass.Bar(card, "Rule", Glass.A(White, 0.22f)).rectTransform;
            divider.anchorMin = divider.anchorMax = divider.pivot = new Vector2(0f, 1f);
            divider.anchoredPosition = new Vector2(39f, -152f);
            divider.sizeDelta = new Vector2(702f, 1.5f);
            for (int i = 0; i < Stats; i++)
            {
                float y = -186f - i * 46f;                    // milieu de la ligne
                var label = Glass.Label(card, "", 21, Glass.Weight.Light, White, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(39f, y + 15f), new Vector2(246f, 30f));
                label.text = Loc.T(StatLabels[0, i, 0], StatLabels[0, i, 1]);
                // Barre fine : piste 6 px, plein blanc, gain vert, perte corail
                var bg = Rect(card, "Bar", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(305f, y - 3f), new Vector2(470f, y + 3f));   // court : place pour « 452 ch/t → 488 ch/t »
                bg.gameObject.AddComponent<Image>().color = Glass.A(White, 0.18f);
                var gain = Rect(bg, "Gain", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                gain.pivot = new Vector2(0f, 0.5f);
                gain.gameObject.AddComponent<Image>().color = Money;
                var loss = Rect(bg, "Loss", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                loss.pivot = new Vector2(0f, 0.5f);
                loss.gameObject.AddComponent<Image>().color = Danger;
                var fill = Rect(bg, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.gameObject.AddComponent<Image>().color = White;
                var value = Glass.Label(card, "", 21, Glass.Weight.Regular, White, TextAnchor.MiddleRight, new Vector2(0f, 1f), new Vector2(480f, y + 15f), new Vector2(261f, 30f));
                bars.Add((fill, gain, loss, label, value, bg));
            }
            // Ligne d'action : pilule cernée d'or, texte, prix en or, touche dans une pastille or
            actionPlate = Glass.Make(card, "Action", 37.5f, Glass.GoldSoft, 1.5f);
            actionPlate.borderColor = Glass.A(Accent, 0.6f);
            var ap = actionPlate.rectTransform;
            ap.anchorMin = ap.anchorMax = ap.pivot = new Vector2(0f, 1f);
            ap.anchoredPosition = new Vector2(39f, -458f);
            ap.sizeDelta = new Vector2(702f, 75f);
            actionText = Glass.Label(ap, "", 24, Glass.Weight.Regular, White, TextAnchor.MiddleLeft);
            Stretch(actionText.rectTransform, 27f, 200f);
            actionPrice = Glass.Label(ap, "", 27, Glass.Weight.Semibold, Accent, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(300f, 40f));
            actionChip = Glass.Make(ap, "Key", 27f, Accent, 0f);
            var kc = actionChip.rectTransform;
            kc.anchorMin = kc.anchorMax = kc.pivot = new Vector2(1f, 0.5f);
            kc.anchoredPosition = new Vector2(-10.5f, 0f);
            kc.sizeDelta = new Vector2(72f, 54f);
            actionKey = Glass.Label(kc, "", 21, Glass.Weight.Semibold, Glass.Ink, TextAnchor.MiddleCenter);
            Stretch(actionKey.rectTransform, 0f, 0f);
            root.SetActive(false);
        }

        /// <summary>Bloc du menu d'une hauteur donnée dans la mise en page.</summary>
        static RectTransform Block(Transform parent, float height)
        {
            var go = new GameObject("Block", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return (RectTransform)go.transform;
        }

        /// <summary>Espace dans une mise en page (fixe, ou extensible par le LayoutElement rendu).</summary>
        static LayoutElement Gap(Transform parent, float size)
        {
            var go = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = size;
            le.preferredWidth = 0f;
            return le;
        }

        /// <summary>Ligne du menu : texte fin sur fond transparent ; choisie, or doux cerné d'or (<see cref="StyleRow"/>).</summary>
        Row MakeRow(Transform parent, UnityEngine.Events.UnityAction onClick, bool withPips)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button), typeof(LayoutElement), typeof(HoverSelect));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 75f;
            var plate = go.GetComponent<GlassRect>();
            plate.radius = 27f;
            plate.border = 0f;
            plate.borderColor = Accent;
            plate.color = Color.clear;
            var b = go.GetComponent<Button>();
            b.targetGraphic = plate;
            // Couleurs pilotées par StyleRow (la ligne choisie suit le survol, HoverSelect)
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
            var name = Glass.Label(go.transform, "", 25, Glass.Weight.Light, White, TextAnchor.MiddleLeft);
            Stretch(name.rectTransform, 27f, 260f);
            var status = Glass.Label(go.transform, "", 20, Glass.Weight.Regular, Accent, TextAnchor.MiddleRight);
            Stretch(status.rectTransform, 300f, 27f);
            var pips = new Image[withPips ? CarTuning.MaxLevel : 0];
            for (int k = 0; k < pips.Length; k++)
            {
                // Niveaux : trois traits fins entre le nom et le prix
                var pip = Rect(go.transform, "Pip", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
                pip.sizeDelta = new Vector2(21f, 6f);
                pip.anchoredPosition = new Vector2(-228f + k * 27f, 0f);
                var img = pip.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = PipOff;
                pips[k] = img;
            }
            GlassRect swatch = null;
            if (withPips)
            {
                // Peinture : pastille de couleur à la place des niveaux, cernée d'un liseré clair
                swatch = Glass.Make(go.transform, "Swatch", 10.5f, Color.white, 1.5f);
                var sw = swatch.rectTransform;
                sw.anchorMin = sw.anchorMax = new Vector2(1f, 0.5f);
                sw.sizeDelta = new Vector2(60f, 21f);
                sw.anchoredPosition = new Vector2(-216f, 0f);
                swatch.gameObject.SetActive(false);
            }
            return new Row { button = b, name = name, status = status, pips = pips, swatch = swatch, plate = plate, layout = le };
        }

        /// <summary>Onglet texte cliquable hors de la navigation de la liste, trait or sous l'onglet ouvert.</summary>
        (Text label, Image mark) MakeTab(Transform parent, string fr, string en, int index)
        {
            var label = Glass.Label(parent, "", 21, Glass.Weight.Regular, Dim, TextAnchor.MiddleCenter);
            label.raycastTarget = true;
            Loc.Bind(label, fr, en);
            var b = label.gameObject.AddComponent<Button>();
            b.targetGraphic = label;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => GoTab(index));
            var mark = Rect(label.transform, "Active", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 4.5f));
            var img = mark.gameObject.AddComponent<Image>();
            img.color = Accent;
            img.raycastTarget = false;
            return (label, img);
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

        /// <summary>Texte posé (ancre = pivot) à la position et dans la boîte données.</summary>
        static void Pin(Text t, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            t.alignment = align;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
        }
    }
}
