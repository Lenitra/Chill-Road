using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Salon multijoueur (construit en code), ouvert depuis l'écran d'accueil, dans le langage visuel des options :
    /// voile sombre, grande plaque inclinée anthracite, titre or, rubriques cyan, police du HUD.
    /// - En-tête : titre, sous-titre, et la carte du pilote (pseudo, modifiable à tout moment).
    /// - Onglet « Rejoindre » : à gauche, la liste des parties du réseau local (recherche toutes les 1,5 s ; nom,
    ///   joueurs et jauge de places, collisions, adresse ; version différente grisée) ; à droite, le code d'invitation
    ///   de l'hôte (la voie simple par Internet) et la connexion directe par adresse IP.
    /// - Onglet « Héberger » : à gauche, les réglages de la partie (nom, joueurs au plus, collisions entre joueurs, code
    ///   d'invitation), tous mémorisés, avec l'aide du réglage choisi ; à droite, comment les amis entrent (code,
    ///   réseau local, adresse) et « Ouvrir la partie ».
    /// - Fenêtre de connexion par-dessus : roue, partie visée, temps écoulé, « Annuler » ; en cas d'échec, le motif
    ///   et « OK ».
    /// Manette : LB/RB (clavier : Tab, Maj+Tab) pour l'onglet, flèches pour se déplacer, gauche/droite sur un réglage
    /// pour le changer, A pour valider, B / Échap pour revenir.
    /// </summary>
    public class NetLobby : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Danger = new Color(1f, 0.42f, 0.34f);
        static readonly Color Good = new Color(0.45f, 0.95f, 0.5f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Faint = new Color(1f, 1f, 1f, 0.3f);
        static readonly Color Card = new Color(1f, 1f, 1f, 0.045f);
        static readonly Color RowNormal = new Color(0.1f, 0.12f, 0.15f, 0.92f);
        static readonly Color RowHover = new Color(0.2f, 0.23f, 0.28f, 1f);
        static readonly Color RowSelected = new Color(0.27f, 0.31f, 0.38f, 1f);

        const string AddressKey = "chillroad_net_addr", CollisionsKey = "chillroad_net_collisions";
        const string ServerNameKey = "chillroad_net_server", MaxPlayersKey = "chillroad_net_maxplayers", InviteKey = "chillroad_net_invite";
        const int Rows = 7, MinPlayers = 2;
        const float SearchPeriod = 1.5f;

        public static bool IsOpen { get; private set; }

        /// <summary>Partie qu'on héberge : les voitures des joueurs se percutent (choix mémorisé d'une fois sur l'autre).</summary>
        public static bool HostCollisions
        {
            get => PlayerPrefs.GetInt(CollisionsKey, 1) == 1;
            set { PlayerPrefs.SetInt(CollisionsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Partie qu'on héberge : nombre de joueurs au plus, hôte compris (mémorisé).</summary>
        public static int HostMaxPlayers
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(MaxPlayersKey, NetProtocol.DefaultMaxPlayers), MinPlayers, NetProtocol.MaxPlayersLimit);
            set { PlayerPrefs.SetInt(MaxPlayersKey, Mathf.Clamp(value, MinPlayers, NetProtocol.MaxPlayersLimit)); PlayerPrefs.Save(); }
        }

        /// <summary>Partie qu'on héberge : un code d'invitation est demandé dès l'ouverture (mémorisé).</summary>
        public static bool HostInvite
        {
            get => PlayerPrefs.GetInt(InviteKey, 1) == 1;
            set { PlayerPrefs.SetInt(InviteKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Nom de la partie qu'on héberge : celui choisi, sinon « Partie de (pseudo) ».</summary>
        public static string HostName
        {
            get
            {
                string chosen = PlayerPrefs.GetString(ServerNameKey, "").Trim();
                return chosen.Length > 0 ? NetProtocol.CleanName(chosen, NetProtocol.MaxServerName) : DefaultHostName;
            }
        }

        static string DefaultHostName => NetProtocol.CleanName(Loc.T($"Partie de {NetGame.PlayerName}", $"{NetGame.PlayerName}'s game"), NetProtocol.MaxServerName);

        /// <summary>Ligne de la liste des parties du réseau local.</summary>
        class ServerRow
        {
            public Button button;
            public CanvasGroup group;
            public Text name, players, address, tag;
            public HudShape tagPlate, fill;
            public RectTransform fillRt;
        }

        enum Modal { None, Connecting, Error }

        GameManager game;
        NetGame net;
        Font font;
        GameObject root;
        CanvasGroup rootGroup, panelGroup;
        RectTransform panelRt;
        Action onClose;
        int openedFrame;
        float openT;

        // En-tête, onglets, pied
        InputField nameField;
        (Button button, Text text, Graphic mark)[] tabs;
        RectTransform[] pages;
        static int lastTab;
        int tab;
        Button backButton;

        // Rejoindre
        readonly List<ServerRow> rows = new List<ServerRow>();
        readonly List<NetGame.ServerInfo> shown = new List<NetGame.ServerInfo>();
        readonly List<Selectable> activeRows = new List<Selectable>();
        GameObject emptyState;
        Text emptyTitle, liveText;
        Image liveDot, radar;
        InputField codeField, addressField;
        Button codeButton, addressButton;
        float searchTimer;
        int wiredRows = -1;
        bool hasNetwork;

        // Héberger
        InputField serverField;
        MenuStepper maxStepper, collisionsStepper, inviteStepper;
        Button openButton;
        Text wayCode, wayLocal, wayAddress;
        readonly List<Action> refreshers = new List<Action>();

        // Lignes qui s'éclairent une fois choisies (champ, réglage, partie) : trait or à gauche
        readonly List<(GameObject target, Graphic mark)> marks = new List<(GameObject, Graphic)>();

        // Fenêtre de connexion
        Modal modal;
        GameObject modalRoot;
        Text modalTitle, modalText, modalDetail;
        Graphic modalTop;
        Image spinner;
        Button modalButton;
        Text modalButtonText;
        string attempt;
        float attemptT;
        GameObject beforeModal;

        public void Init(GameManager gm)
        {
            game = gm;
            net = gm ? gm.Net : null;
            font = HudArt.Font;
            IsOpen = false;
            if (net)
            {
                net.Joined += OnJoined;
                net.Failed += OnFailed;
            }

            var cgo = new GameObject("NetLobby", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = cgo;
            rootGroup = cgo.GetComponent<CanvasGroup>();

            var shade = Rect(cgo.transform, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.7f);
            var panel = Panel(shade, "Lobby", new Vector2(-790, -440), new Vector2(790, 462));
            panelRt = panel;
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();

            BuildHeader(panel);
            BuildTabs(panel);
            pages = new[] { Page(panel, "Join"), Page(panel, "Host") };
            BuildJoin(pages[0]);
            BuildHost(pages[1]);
            BuildFooter(panel);

            // Aide manette / clavier sous la plaque
            var hint = Label(shade, "", 21, TextAnchor.MiddleCenter, Dim, FontStyle.Normal);
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.anchoredPosition = new Vector2(0f, -474f);
            hintRt.sizeDelta = new Vector2(1600f, 40f);
            Loc.Bind(hint, "LB RB / Tab  onglet   ·   ← →  régler   ·   A / Entrée  valider   ·   B / Échap  retour",
                           "LB RB / Tab  tab   ·   ← →  adjust   ·   A / Enter  select   ·   B / Esc  back");

            BuildModal(shade);
            ShowTab(lastTab);
            root.SetActive(false);
        }

        // ------------------------------------------------------------------ construction

        /// <summary>Titre à gauche ; carte du pilote (pseudo) à droite.</summary>
        void BuildHeader(RectTransform panel)
        {
            var header = Rect(panel, "Header", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(header, 88);
            var title = Label(header, "", 62, TextAnchor.MiddleLeft, Accent, FontStyle.BoldAndItalic);
            Loc.Bind(title, "MULTIJOUEUR", "MULTIPLAYER");
            Place(title.rectTransform, Vector2.zero, new Vector2(0.6f, 1f));

            // Carte du pilote
            var card = Rect(header, "Driver", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-520f, -40f), new Vector2(0f, 40f));
            var plate = Shape(card, "Plate", Card).Set(18f, 0f, 0f);
            Fill(plate.rectTransform, Vector2.zero, Vector2.zero);
            var label = Label(card, "", 19, TextAnchor.MiddleLeft, Info, FontStyle.BoldAndItalic);
            Loc.Bind(label, "PILOTE", "DRIVER");
            Place(label.rectTransform, new Vector2(0f, 0f), new Vector2(0.26f, 1f), 26f, 0f);
            nameField = MakeInput(card, 27, TextAnchor.MiddleLeft, White, FontStyle.Bold, "ton pseudo", "your name", NetProtocol.MaxName);
            Place((RectTransform)nameField.transform, new Vector2(0.26f, 0f), new Vector2(1f, 1f), 0f, 12f, 10f);
            nameField.onEndEdit.AddListener(_ => { SaveFields(); RefreshAll(); });
            AddMark(nameField.gameObject, (RectTransform)nameField.transform);
        }

        void BuildTabs(RectTransform panel)
        {
            var tabRow = Rect(panel, "Tabs", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(tabRow, 60);
            var th = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 14;
            th.childControlWidth = true; th.childControlHeight = true;
            th.childForceExpandWidth = false; th.childForceExpandHeight = true;
            TabGlyph(tabRow, "LB", TextAnchor.MiddleRight);
            tabs = new (Button, Text, Graphic)[2];
            tabs[0] = AddTab(tabRow, 0);
            tabs[1] = AddTab(tabRow, 1);
            TabGlyph(tabRow, "RB", TextAnchor.MiddleLeft);
        }

        /// <summary>Onglet « Rejoindre » : liste du réseau local à gauche, code et adresse à droite.</summary>
        void BuildJoin(RectTransform page)
        {
            // La liste s'arrête au bord droit de l'onglet « Rejoindre » : largeur utile 1466, onglet terminé à 726
            // (LB 44 + 14 + onglet 668), colonnes séparées de 44 -> 726 / 696.
            var left = Column(page, "Servers", 726f);
            var right = Column(page, "Direct", 696f);

            // Parties du réseau local
            var section = Section(left, "PARTIES SUR TON RÉSEAU LOCAL", "GAMES ON YOUR LOCAL NETWORK", "", "");
            var live = Rect(section, "Live", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-330f, 0f), new Vector2(0f, 0f));
            live.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            liveDot = Dot(live, new Vector2(0f, 0.5f), new Vector2(12f, -3f), 12f, Good);
            liveText = Label(live, "", 19, TextAnchor.MiddleLeft, Dim, FontStyle.Italic);
            Place(liveText.rectTransform, Vector2.zero, Vector2.one, 26f, 0f, 0f, 6f);

            // En-têtes de colonnes
            var heads = Rect(left, "Columns", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(heads, 30);
            ColumnHead(heads, "NOM DE LA PARTIE", "GAME NAME", 0f, 0.44f, 26f);
            ColumnHead(heads, "JOUEURS", "PLAYERS", 0.44f, 0.62f, 0f);
            ColumnHead(heads, "COLLISIONS", "COLLISIONS", 0.62f, 0.8f, 0f);
            ColumnHead(heads, "ADRESSE", "ADDRESS", 0.8f, 1f, 0f);

            // Liste (lignes fixes, cachées tant qu'elles sont vides) et état vide par-dessus
            var list = Rect(left, "List", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(list, Rows * 66f);
            var lv = list.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 6;
            lv.childControlHeight = true; lv.childControlWidth = true;
            lv.childForceExpandHeight = false; lv.childForceExpandWidth = true;
            for (int i = 0; i < Rows; i++) rows.Add(ServerLine(list, i));

            var empty = Rect(list, "Empty", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            empty.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var emptyPlate = Shape(empty, "Plate", new Color(1f, 1f, 1f, 0.03f)).Set(20f, 0f, 0f);
            Fill(emptyPlate.rectTransform, Vector2.zero, Vector2.zero);
            radar = new GameObject("Radar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            radar.transform.SetParent(empty, false);
            radar.sprite = HudArt.Ring;
            radar.raycastTarget = false;
            var radarRt = radar.rectTransform;
            radarRt.anchorMin = radarRt.anchorMax = new Vector2(0.5f, 0.58f);
            radarRt.sizeDelta = new Vector2(120f, 120f);
            Dot(empty, new Vector2(0.5f, 0.58f), Vector2.zero, 14f, Info);
            emptyTitle = Label(empty, "", 27, TextAnchor.MiddleCenter, White, FontStyle.BoldAndItalic);
            Place(emptyTitle.rectTransform, new Vector2(0f, 0.3f), new Vector2(1f, 0.42f));
            emptyState = empty.gameObject;

            // Code d'invitation et adresse IP : même format (champ + bouton sur une ligne), l'un sous l'autre
            Section(right, "CODE D'INVITATION", "INVITE CODE", "", "");
            (codeField, codeButton) = JoinRow(right, "CodeRow", "ex. K7Q2XM", "e.g. K7Q2XM", NetRelay.CodeMax, "Rejoindre", "Join", JoinByCode);
            codeField.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
            codeField.textComponent.color = Accent;

            Gap(right, 14);
            Section(right, "ADRESSE IP", "IP ADDRESS", "", "");
            (addressField, addressButton) = JoinRow(right, "AddressRow", "ex. 192.168.1.20", "e.g. 192.168.1.20", 60, "Se connecter", "Connect", JoinAddress);
        }

        /// <summary>Ligne « champ + bouton » de l'onglet Rejoindre ; Entrée dans le champ vaut le bouton.</summary>
        (InputField field, Button button) JoinRow(RectTransform parent, string name, string hintFr, string hintEn, int max,
                                                  string fr, string en, Action action)
        {
            var row = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, 58);
            var field = MakeInput(row, 25, TextAnchor.MiddleLeft, White, FontStyle.Bold, hintFr, hintEn, max);
            Place((RectTransform)field.transform, Vector2.zero, new Vector2(0.6f, 1f), 0f, 0f, 3f);
            field.onSubmit.AddListener(_ => action());
            AddMark(field.gameObject, (RectTransform)field.transform);
            var b = AddButton(row, () => action());
            Loc.Bind(b.text, fr, en);
            b.text.fontSize = 25;
            Place((RectTransform)b.button.transform, new Vector2(0.63f, 0f), Vector2.one, 0f, 0f, 3f);
            return (field, b.button);
        }

        /// <summary>Onglet « Héberger » : réglages de la partie à gauche, accès des amis et ouverture à droite.</summary>
        void BuildHost(RectTransform page)
        {
            // Mêmes colonnes que l'onglet « Rejoindre » : la gauche s'arrête au bord de l'onglet
            var left = Column(page, "Settings", 726f);
            var right = Column(page, "Open", 696f);

            Section(left, "RÉGLAGES DE LA PARTIE", "GAME SETTINGS", "", "");
            var nameRow = Rect(left, "ServerName", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(nameRow, 60);
            var nameLabel = Loc.Bind(Label(nameRow, "", 25, TextAnchor.MiddleLeft, White, FontStyle.Normal), "Nom de la partie", "Game name");
            Place(nameLabel.rectTransform, Vector2.zero, new Vector2(0.42f, 1f), 6f, 0f);
            serverField = MakeInput(nameRow, 25, TextAnchor.MiddleLeft, Accent, FontStyle.Bold, "", "", NetProtocol.MaxServerName);
            Place((RectTransform)serverField.transform, new Vector2(0.42f, 0f), Vector2.one, 0f, 0f, 3f);
            serverField.onEndEdit.AddListener(_ => SaveFields());
            AddMark(serverField.gameObject, nameRow);
            maxStepper = AddStepper(left, "Joueurs au plus", "Max players",
                d => HostMaxPlayers = Mathf.Clamp(HostMaxPlayers + d, MinPlayers, NetProtocol.MaxPlayersLimit),
                () => HostMaxPlayers.ToString());
            collisionsStepper = AddStepper(left, "Collisions entre joueurs", "Player collisions",
                d => HostCollisions = !HostCollisions,
                () => HostCollisions ? Loc.T("Activées", "On") : Loc.T("Désactivées", "Off"));
            inviteStepper = AddStepper(left, "Code d'invitation", "Invite code",
                d => HostInvite = !HostInvite,
                () => HostInvite ? Loc.T("Activé", "On") : Loc.T("Désactivé", "Off"));

            // Comment les amis entrent
            Section(right, "TES AMIS TE REJOIGNENT", "YOUR FRIENDS JOIN YOU", "", "");
            wayCode = Way(right, "1", "PAR CODE D'INVITATION", "BY INVITE CODE");
            wayLocal = Way(right, "2", "SUR TON RÉSEAU LOCAL", "ON YOUR LOCAL NETWORK");
            wayAddress = Way(right, "3", "PAR TON ADRESSE IP", "BY YOUR IP ADDRESS");
            Spacer(right);
            var open = AddButton(right, Host);
            open.button.GetComponent<LayoutElement>().preferredHeight = 78;
            open.text.fontSize = 32;
            Loc.Bind(open.text, "OUVRIR LA PARTIE", "OPEN THE GAME");
            Primary(open.button);
            openButton = open.button;
        }

        void BuildFooter(RectTransform panel)
        {
            var footer = Rect(panel, "Footer", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(footer, 62);
            var line = Shape(footer, "Line", new Color(1f, 1f, 1f, 0.08f)).Set(0f, 0f, 400f);
            Place(line.rectTransform, new Vector2(0f, 1f), Vector2.one, 0f, 0f, 0f, 0f);
            line.rectTransform.offsetMin = new Vector2(0f, 6f);
            line.rectTransform.offsetMax = new Vector2(0f, 8f);
            var back = AddButton(footer, Close);
            Loc.Bind(back.text, "Retour", "Back");
            Place((RectTransform)back.button.transform, new Vector2(0.8f, 0f), Vector2.one, 0f, 0f, 2f);
            backButton = back.button;
        }

        /// <summary>Fenêtre de connexion (ou d'échec) par-dessus le salon ; elle bloque le reste tant qu'elle est ouverte.</summary>
        void BuildModal(RectTransform shade)
        {
            var veil = Rect(shade, "Modal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            veil.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0.01f, 0.55f);
            modalRoot = veil.gameObject;
            var box = Rect(veil, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-440f, -190f), new Vector2(440f, 190f));
            var plate = Shape(box, "Plate", new Color(0.04f, 0.05f, 0.075f, 0.98f)).Set(24f, 0f, 0f);
            Fill(plate.rectTransform, Vector2.zero, Vector2.zero);
            var top = Shape(box, "Top", Info).Set(24f, 0f, 0f);
            modalTop = top;
            Place(top.rectTransform, new Vector2(0f, 1f), Vector2.one, 0f, 0f, 0f, 0f);
            top.rectTransform.offsetMin = new Vector2(0f, -5f);
            modalTitle = Label(box, "", 36, TextAnchor.MiddleCenter, White, FontStyle.BoldAndItalic);
            Place(modalTitle.rectTransform, new Vector2(0f, 0.78f), new Vector2(1f, 0.96f));
            spinner = new GameObject("Spinner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            spinner.transform.SetParent(box, false);
            spinner.sprite = HudArt.Ring;
            spinner.type = Image.Type.Filled;
            spinner.fillMethod = Image.FillMethod.Radial360;
            spinner.fillAmount = 0.3f;
            spinner.color = Info;
            spinner.raycastTarget = false;
            var srt = spinner.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.6f);
            srt.sizeDelta = new Vector2(64f, 64f);
            modalText = Label(box, "", 25, TextAnchor.MiddleCenter, White, FontStyle.Normal);
            modalText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(modalText.rectTransform, new Vector2(0.06f, 0.36f), new Vector2(0.94f, 0.5f));
            modalDetail = Label(box, "", 20, TextAnchor.MiddleCenter, Dim, FontStyle.Italic);
            Place(modalDetail.rectTransform, new Vector2(0.06f, 0.27f), new Vector2(0.94f, 0.36f));
            var button = AddButton(box, ModalAction);
            modalButton = button.button;
            modalButtonText = button.text;
            modalButton.navigation = new Navigation { mode = Navigation.Mode.None };
            var brt = (RectTransform)modalButton.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(320f, 60f);
            brt.anchoredPosition = new Vector2(0f, 58f);
            modalRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ ouverture / fermeture

        /// <summary>Ouvre le salon ; <paramref name="closed"/> est appelé si on revient à l'accueil sans jouer.</summary>
        public void Open(Action closed)
        {
            onClose = closed;
            nameField.text = NetGame.PlayerName;
            serverField.text = PlayerPrefs.GetString(ServerNameKey, "");
            addressField.text = PlayerPrefs.GetString(AddressKey, "");
            codeField.text = "";
            searchTimer = 0f;
            wiredRows = -1;
            HideModal();
            root.SetActive(true);
            IsOpen = true;
            openedFrame = Time.frameCount;
            openT = 0f;
            rootGroup.alpha = 0f;
            ShowTab(lastTab);
            RefreshAll();
        }

        public void Close()
        {
            if (net) net.CancelJoin();
            SaveFields();
            Hide();
            onClose?.Invoke();
        }

        void Hide()
        {
            HideModal();
            root.SetActive(false);
            IsOpen = false;
        }

        void SaveFields()
        {
            NetGame.PlayerName = nameField.text;
            nameField.text = NetGame.PlayerName;
            PlayerPrefs.SetString(ServerNameKey, serverField.text.Trim());
            PlayerPrefs.SetString(AddressKey, addressField.text.Trim());
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ actions

        void Host()
        {
            if (!net || net.Connecting || modal != Modal.None) return;
            SaveFields();
            if (!net.Host(HostName, NetProtocol.DefaultPort, HostMaxPlayers, false, HostCollisions, out string why))
            {
                ShowError(Loc.T("IMPOSSIBLE D'OUVRIR LA PARTIE", "COULD NOT OPEN THE GAME"), why);
                return;
            }
            // Le code d'invitation est demandé tout de suite ; il arrive pendant le départ (annonce du HUD, menu pause)
            if (HostInvite) net.OpenInvite();
            Hide();
            game.StartOnline();
        }

        void JoinByCode()
        {
            if (!net || net.Connecting || modal != Modal.None) return;
            SaveFields();
            string code = codeField.text.Trim().ToUpperInvariant();
            BeginAttempt(Loc.T($"la partie du code {code}", $"the game with code {code}"));
            net.JoinCode(codeField.text);
            AfterAttempt();
        }

        void JoinFound(int index)
        {
            if (!net || net.Connecting || modal != Modal.None || index >= shown.Count) return;
            var s = shown[index];
            if (!s.compatible)
            {
                ShowError(Loc.T("VERSION DIFFÉRENTE", "DIFFERENT VERSION"),
                    Loc.T($"« {s.name} » tourne sur une autre version du jeu : mettez-vous à jour tous les deux.",
                          $"“{s.name}” runs another version of the game: both of you should update."));
                return;
            }
            SaveFields();
            BeginAttempt($"« {s.name} »");
            net.Join(s.ep);
            AfterAttempt();
        }

        void JoinAddress()
        {
            if (!net || net.Connecting || modal != Modal.None) return;
            SaveFields();
            BeginAttempt(addressField.text.Trim());
            net.Join(addressField.text);
            AfterAttempt();
        }

        void BeginAttempt(string what)
        {
            attempt = what;
            attemptT = 0f;
            beforeModal = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        }

        /// <summary>Après une demande : fenêtre de connexion si elle est en cours (un refus immédiat a déjà ouvert l'échec).</summary>
        void AfterAttempt()
        {
            if (net.Connecting) ShowModal(Modal.Connecting);
        }

        void OnJoined()
        {
            if (!IsOpen) return;
            Hide();
            game.StartOnline();
        }

        void OnFailed(string message)
        {
            if (!IsOpen) return;
            ShowError(Loc.T("CONNEXION IMPOSSIBLE", "COULD NOT CONNECT"), message);
        }

        void ShowError(string title, string message)
        {
            if (modal == Modal.None) beforeModal = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            modalTitle.text = title;
            modalText.text = message;
            ShowModal(Modal.Error);
        }

        void ShowModal(Modal kind)
        {
            modal = kind;
            modalRoot.SetActive(true);
            panelGroup.interactable = false;
            bool error = kind == Modal.Error;
            spinner.gameObject.SetActive(!error);
            modalTitle.color = error ? Danger : White;
            modalTop.color = error ? Danger : Info;
            modalButtonText.text = error ? Loc.T("OK", "OK") : Loc.T("Annuler", "Cancel");
            modalDetail.text = error ? Loc.T("A / Entrée pour revenir", "A / Enter to go back") : "";
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(modalButton.gameObject);
        }

        void HideModal()
        {
            modal = Modal.None;
            if (modalRoot) modalRoot.SetActive(false);
            if (panelGroup) panelGroup.interactable = true;
        }

        /// <summary>Bouton de la fenêtre : annule la connexion en cours, ou referme l'échec.</summary>
        void ModalAction()
        {
            if (modal == Modal.Connecting && net) net.CancelJoin();
            HideModal();
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(beforeModal && beforeModal.activeInHierarchy ? beforeModal : FirstOfTab());
        }

        // ------------------------------------------------------------------ onglets et navigation

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
            wiredRows = -1;
            Wire();
            TabTexts();
            if (IsOpen && EventSystem.current) EventSystem.current.SetSelectedGameObject(FirstOfTab().gameObject);
        }

        /// <summary>Libellés des onglets : le nombre de parties trouvées sur celui de « Rejoindre ».</summary>
        void TabTexts()
        {
            tabs[0].text.text = shown.Count > 0 ? Loc.T($"REJOINDRE   ·   {shown.Count}", $"JOIN   ·   {shown.Count}") : Loc.T("REJOINDRE", "JOIN");
            tabs[1].text.text = Loc.T("HÉBERGER", "HOST");
        }

        GameObject FirstOfTab() => tab == 0 ? codeField.gameObject : openButton.gameObject;

        /// <summary>Navigation explicite de l'onglet ouvert (la liste du réseau local la refait quand elle change).</summary>
        void Wire()
        {
            if (tab == 0)
            {
                activeRows.Clear();
                foreach (var r in rows)
                    if (r.button.gameObject.activeSelf) activeRows.Add(r.button);
                wiredRows = activeRows.Count;
                Selectable firstRow = activeRows.Count > 0 ? activeRows[0] : null;
                for (int i = 0; i < activeRows.Count; i++)
                    Nav(activeRows[i], i == 0 ? (Selectable)nameField : activeRows[i - 1], i == activeRows.Count - 1 ? (Selectable)backButton : activeRows[i + 1], null, codeField);
                Nav(nameField, null, codeField, null, null);
                Nav(codeField, nameField, addressField, firstRow, codeButton);
                Nav(codeButton, nameField, addressButton, codeField, null);
                Nav(addressField, codeField, backButton, firstRow, addressButton);
                Nav(addressButton, codeButton, backButton, addressField, null);
                Nav(backButton, addressButton, null, firstRow, null);
            }
            else
            {
                Nav(nameField, null, serverField, null, null);
                Nav(serverField, nameField, maxStepper, null, openButton);
                Nav(maxStepper, serverField, collisionsStepper, null, null);
                Nav(collisionsStepper, maxStepper, inviteStepper, null, null);
                Nav(inviteStepper, collisionsStepper, openButton, null, null);
                Nav(openButton, inviteStepper, backButton, inviteStepper, null);
                Nav(backButton, openButton, null, null, null);
            }
        }

        // ------------------------------------------------------------------ mise à jour

        void RefreshAll()
        {
            foreach (var r in refreshers) r();
            serverField.placeholder.GetComponent<Text>().text = DefaultHostName;
            var addresses = NetSocket.LocalAddresses();
            hasNetwork = addresses.Count > 0;
            string ip = hasNetwork ? addresses[0] : null;
            wayCode.text = HostInvite
                ? Loc.T("Le code s'affiche en jeu dès l'ouverture ; le menu pause le copie. Rien à régler sur la box.",
                        "The code shows up in game as soon as it opens; the pause menu copies it. No router setup.")
                : Loc.T("Désactivé : tu pourras en demander un plus tard depuis le menu pause.",
                        "Off: you can still request one later from the pause menu.");
            wayCode.color = HostInvite ? Dim : Faint;
            wayLocal.text = Loc.T("Même box ou même Wi-Fi : ta partie apparaît d'elle-même dans leur liste.",
                                  "Same router or Wi-Fi: your game shows up in their list by itself.");
            wayAddress.text = ip != null
                ? Loc.T($"Ton adresse : {ip}. Par Internet, ouvre le port UDP {NetProtocol.DefaultPort} sur ta box.",
                        $"Your address: {ip}. Over the Internet, forward UDP port {NetProtocol.DefaultPort} on your router.")
                : Loc.T("Aucun réseau détecté sur cette machine.", "No network found on this computer.");
        }

        void Update()
        {
            if (!IsOpen) return;
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            // Arrivée : fondu et léger zoom de la plaque
            openT += dt;
            float k = Mathf.Clamp01(openT / 0.22f);
            float ease = 1f - (1f - k) * (1f - k);
            rootGroup.alpha = ease;
            panelRt.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, ease);

            if (Time.frameCount != openedFrame && GameInput.CancelPressed && !Typing)
            {
                if (modal != Modal.None) ModalAction();
                else Close();
                return;
            }
            if (modal == Modal.None && !Typing)
            {
                if (GameInput.TabPrevPressed) ShowTab(tab - 1);
                else if (GameInput.TabNextPressed) ShowTab(tab + 1);
            }

            UpdateServers(dt, now);
            UpdateModal(dt, now);

            TabTexts();

            // Ligne choisie : trait or
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var m in marks) m.mark.enabled = m.target == selected && modal == Modal.None;
            foreach (var r in rows) r.name.color = r.button.gameObject == selected ? Accent : White;
            // Manette : garder un élément choisi
            if (EventSystem.current && GameInput.UsingGamepad)
            {
                if (selected == null || !selected.activeInHierarchy)
                    EventSystem.current.SetSelectedGameObject(modal != Modal.None ? modalButton.gameObject : FirstOfTab());
                else
                    foreach (var t in tabs)
                        if (selected == t.button.gameObject) EventSystem.current.SetSelectedGameObject(FirstOfTab());
            }
        }

        /// <summary>Recherche des parties du réseau local et remplissage des lignes.</summary>
        void UpdateServers(float dt, float now)
        {
            searchTimer -= dt;
            if (net && searchTimer <= 0f && !net.Connecting)
            {
                searchTimer = SearchPeriod;
                net.Search();
                // Câble branché ou Wi-Fi rejoint pendant que le salon est ouvert : adresses et état vide à jour
                if (hasNetwork != NetSocket.LocalAddresses().Count > 0) RefreshAll();
            }
            shown.Clear();
            if (net) shown.AddRange(net.Found);
            if (shown.Count > Rows) shown.RemoveRange(Rows, shown.Count - Rows);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool on = i < shown.Count;
                if (row.button.gameObject.activeSelf != on) row.button.gameObject.SetActive(on);
                if (!on) continue;
                var s = shown[i];
                bool full = s.players >= s.max;
                row.name.text = s.name;
                row.players.text = full ? Loc.T($"{s.players} / {s.max}  ·  COMPLÈTE", $"{s.players} / {s.max}  ·  FULL") : $"{s.players} / {s.max}";
                row.players.color = full ? Danger : White;
                row.fillRt.localScale = new Vector3(s.max > 0 ? Mathf.Clamp01((float)s.players / s.max) : 0f, 1f, 1f);
                row.fill.color = full ? Danger : Info;
                row.address.text = s.ep.Port == NetProtocol.DefaultPort ? s.ep.Address.ToString() : s.ep.ToString();
                if (!s.compatible)
                {
                    row.tag.text = Loc.T("AUTRE VERSION", "OTHER VERSION");
                    row.tag.color = Danger;
                    row.tagPlate.color = new Color(Danger.r, Danger.g, Danger.b, 0.16f);
                }
                else
                {
                    row.tag.text = s.collisions ? Loc.T("ACTIVÉES", "ON") : Loc.T("DÉSACTIVÉES", "OFF");
                    row.tag.color = s.collisions ? Accent : Dim;
                    row.tagPlate.color = s.collisions ? new Color(Accent.r, Accent.g, Accent.b, 0.16f) : new Color(1f, 1f, 1f, 0.07f);
                }
                row.group.alpha = s.compatible ? 1f : 0.55f;
            }
            if (tab == 0 && shown.Count != wiredRows)
            {
                var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
                Wire();
                if (selected && !selected.activeInHierarchy && EventSystem.current && modal == Modal.None)
                    EventSystem.current.SetSelectedGameObject(shown.Count > 0 ? activeRows[0].gameObject : codeField.gameObject);
            }

            // État vide : onde qui s'élargit autour d'un point, comme un radar
            bool noNetwork = !hasNetwork;
            emptyState.SetActive(shown.Count == 0);
            if (shown.Count == 0)
            {
                float wave = Mathf.Repeat(now * 0.6f, 1f);
                radar.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.6f, wave);
                radar.color = new Color(Info.r, Info.g, Info.b, (1f - wave) * 0.8f);
                emptyTitle.text = noNetwork ? Loc.T("Aucun réseau détecté", "No network found") : Loc.T("Aucune partie trouvée pour l'instant", "No game found yet");
            }
            liveDot.color = new Color(Good.r, Good.g, Good.b, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(now * 2.4f)));
            liveText.text = shown.Count == 0 ? Loc.T("Recherche en cours", "Searching") + Dots()
                : shown.Count == 1 ? Loc.T("1 partie trouvée", "1 game found")
                : Loc.T($"{shown.Count} parties trouvées", $"{shown.Count} games found");
        }

        void UpdateModal(float dt, float now)
        {
            if (modal != Modal.Connecting) return;
            attemptT += dt;
            spinner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -now * 300f);
            spinner.fillAmount = 0.2f + 0.15f * Mathf.Sin(now * 3f);
            bool finding = net && net.FindingCode;
            modalTitle.text = Loc.T("CONNEXION", "CONNECTING");
            modalText.text = finding ? Loc.T($"Recherche de {attempt}", $"Looking for {attempt}") + Dots()
                : Loc.T($"Connexion à {attempt}", $"Connecting to {attempt}") + Dots();
            modalDetail.text = Loc.T($"{Mathf.FloorToInt(attemptT)} s   ·   B / Échap pour annuler", $"{Mathf.FloorToInt(attemptT)} s   ·   B / Esc to cancel");
            // Connexion abandonnée par le jeu sans message (ne devrait pas arriver) : refermer
            if (net && !net.Connecting && modal == Modal.Connecting && !net.Active) HideModal();
        }

        /// <summary>Saisie en cours dans un champ : Échap quitte le champ, pas le salon.</summary>
        bool Typing => nameField.isFocused || addressField.isFocused || codeField.isFocused || serverField.isFocused;

        static string Dots() => new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);

        // ------------------------------------------------------------------ éléments

        static RectTransform Panel(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var panel = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), min, max);
            var plate = Shape(panel, "Plate", new Color(0.03f, 0.04f, 0.06f, 0.94f)).Set(24f, 0f, 0f);
            plate.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fill(plate.rectTransform, Vector2.zero, Vector2.zero);
            // Liseré or en haut de la plaque
            var edge = Shape(panel, "Edge", Accent).Set(24f, 0f, 0f);
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var ert = edge.rectTransform;
            ert.anchorMin = new Vector2(0f, 1f);
            ert.anchorMax = Vector2.one;
            ert.offsetMin = new Vector2(0f, -4f);
            ert.offsetMax = Vector2.zero;
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(60, 54, 22, 26);
            layout.spacing = 12;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            return panel;
        }

        /// <summary>Page d'un onglet : deux colonnes côte à côte qui prennent la place libre de la plaque.</summary>
        static RectTransform Page(RectTransform parent, string name)
        {
            var page = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            page.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var layout = page.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 44;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = true; layout.childForceExpandWidth = false;
            return page;
        }

        static RectTransform Column(RectTransform page, string name, float weight)
        {
            var column = Rect(page, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var le = column.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = weight;
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            return column;
        }

        (Button button, Text text, Graphic mark) AddTab(Transform parent, int index)
        {
            var (button, text) = AddButton(parent, () => ShowTab(index));
            button.GetComponent<LayoutElement>().flexibleWidth = 1f;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            text.fontSize = 26;
            var mark = Shape(button.transform, "Active", Accent).Set(4f, 0f, 0f);
            var rt = mark.rectTransform;
            rt.anchorMin = new Vector2(0.08f, 0f);
            rt.anchorMax = new Vector2(0.92f, 0f);
            rt.offsetMin = new Vector2(0f, 3f);
            rt.offsetMax = new Vector2(0f, 7f);
            return (button, text, mark);
        }

        void TabGlyph(Transform parent, string glyph, TextAnchor align)
        {
            var t = Label(parent, glyph, 21, align, Dim, FontStyle.Bold);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 44;
        }

        /// <summary>Rubrique : nom en cyan, précision estompée, filet cyan qui s'efface vers la droite.</summary>
        RectTransform Section(RectTransform parent, string fr, string en, string noteFr, string noteEn)
        {
            var row = Rect(parent, "Section", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, 46);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14;
            h.childAlignment = TextAnchor.LowerLeft;
            h.padding = new RectOffset(0, 0, 0, 6);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            Loc.Bind(Label(row, "", 25, TextAnchor.LowerLeft, Info, FontStyle.BoldAndItalic), fr, en);
            if (noteFr.Length > 0) Loc.Bind(Label(row, "", 19, TextAnchor.LowerLeft, Dim, FontStyle.Italic), "·  " + noteFr, "·  " + noteEn);
            var line = Shape(row, "Line", new Color(Info.r, Info.g, Info.b, 0.4f)).Set(0f, 0f, 260f);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var lrt = line.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = new Vector2(0f, 2f);
            return row;
        }

        void ColumnHead(RectTransform parent, string fr, string en, float xMin, float xMax, float pad)
        {
            var t = Loc.Bind(Label(parent, "", 17, TextAnchor.MiddleLeft, Faint, FontStyle.Bold), fr, en);
            Place(t.rectTransform, new Vector2(xMin, 0f), new Vector2(xMax, 1f), pad, 0f);
        }

        /// <summary>Ligne d'une partie du réseau local : nom, joueurs et jauge, étiquette de collisions, adresse.</summary>
        ServerRow ServerLine(RectTransform parent, int index)
        {
            var go = new GameObject("Server" + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button), typeof(LayoutElement), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            go.GetComponent<LayoutElement>().preferredHeight = 60;
            var shape = go.GetComponent<HudShape>().Set(16f, 0f, 0f);
            shape.color = Color.white;
            var b = go.GetComponent<Button>();
            b.targetGraphic = shape;
            b.colors = RowColors();
            b.onClick.AddListener(() => JoinFound(index));
            AddMark(go, rt);

            var row = new ServerRow { button = b, group = go.GetComponent<CanvasGroup>() };
            row.name = Label(go.transform, "", 25, TextAnchor.MiddleLeft, White, FontStyle.Bold);
            Place(row.name.rectTransform, Vector2.zero, new Vector2(0.44f, 1f), 26f, 10f);
            row.players = Label(go.transform, "", 22, TextAnchor.MiddleLeft, White, FontStyle.Bold);
            Place(row.players.rectTransform, new Vector2(0.44f, 0.3f), new Vector2(0.62f, 1f), 0f, 0f);
            var track = Shape(go.transform, "Track", new Color(1f, 1f, 1f, 0.12f)).Set(3f, 0f, 0f);
            Place(track.rectTransform, new Vector2(0.44f, 0.2f), new Vector2(0.59f, 0.2f), 0f, 0f, 0f, 0f);
            track.rectTransform.offsetMin = new Vector2(0f, -2f);
            track.rectTransform.offsetMax = new Vector2(0f, 3f);
            row.fill = Shape(track.transform, "Fill", Info).Set(3f, 0f, 0f);
            row.fillRt = row.fill.rectTransform;
            Fill(row.fillRt, Vector2.zero, Vector2.zero);
            row.fillRt.pivot = new Vector2(0f, 0.5f);

            var tag = Rect(go.transform, "Tag", new Vector2(0.62f, 0.5f), new Vector2(0.62f, 0.5f), new Vector2(0f, -15f), new Vector2(150f, 15f));
            tag.gameObject.AddComponent<CanvasRenderer>();
            row.tagPlate = tag.gameObject.AddComponent<HudShape>().Set(10f, 0f, 0f);
            row.tagPlate.raycastTarget = false;
            row.tag = Label(tag, "", 16, TextAnchor.MiddleCenter, Accent, FontStyle.Bold);
            Fill(row.tag.rectTransform, Vector2.zero, Vector2.zero);
            row.tag.GetComponent<Shadow>().enabled = false;

            row.address = Label(go.transform, "", 21, TextAnchor.MiddleLeft, Dim, FontStyle.Normal);
            Place(row.address.rectTransform, new Vector2(0.8f, 0f), Vector2.one, 0f, 8f);
            go.SetActive(false);
            return row;
        }

        /// <summary>Carte « comment les amis entrent » : numéro or, titre cyan, texte estompé (rempli par RefreshAll).</summary>
        Text Way(RectTransform parent, string number, string fr, string en)
        {
            var card = Rect(parent, "Way" + number, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(card, 104);
            var plate = Shape(card, "Plate", Card).Set(18f, 0f, 0f);
            Fill(plate.rectTransform, Vector2.zero, Vector2.zero);
            var n = Label(card, number, 48, TextAnchor.MiddleCenter, Accent, FontStyle.BoldAndItalic);
            Place(n.rectTransform, Vector2.zero, new Vector2(0f, 1f), 0f, 0f);
            n.rectTransform.offsetMax = new Vector2(78f, 0f);
            var title = Loc.Bind(Label(card, "", 20, TextAnchor.LowerLeft, Info, FontStyle.BoldAndItalic), fr, en);
            Place(title.rectTransform, new Vector2(0f, 0.62f), new Vector2(1f, 0.92f), 84f, 20f);
            var body = Label(card, "", 20, TextAnchor.UpperLeft, Dim, FontStyle.Normal);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(body.rectTransform, new Vector2(0f, 0.06f), new Vector2(1f, 0.6f), 84f, 20f);
            return body;
        }

        /// <summary>Réglage à valeurs « ‹ valeur › » (voir <see cref="MenuStepper"/>).</summary>
        MenuStepper AddStepper(RectTransform parent, string fr, string en, Action<int> step, Func<string> value)
        {
            var go = new GameObject(en, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(MenuStepper), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 58;
            le.flexibleHeight = 0f;
            var shape = go.GetComponent<HudShape>().Set(16f, 0f, 0f);
            shape.color = Color.white;
            var stepper = go.GetComponent<MenuStepper>();
            stepper.targetGraphic = shape;
            stepper.colors = RowColors();
            var name = Loc.Bind(Label(go.transform, "", 25, TextAnchor.MiddleLeft, White, FontStyle.Normal), fr, en);
            Place(name.rectTransform, Vector2.zero, new Vector2(0.55f, 1f), 26f, 0f);
            var area = Rect(go.transform, "Value", new Vector2(0.55f, 0f), Vector2.one, Vector2.zero, new Vector2(-16f, 0f));
            stepper.valueArea = area;
            var leftArrow = Label(area, "‹", 34, TextAnchor.MiddleLeft, Dim, FontStyle.Bold);
            Fill(leftArrow.rectTransform, new Vector2(12f, 2f), Vector2.zero);
            var rightArrow = Label(area, "›", 34, TextAnchor.MiddleRight, Dim, FontStyle.Bold);
            Fill(rightArrow.rectTransform, Vector2.zero, new Vector2(-4f, 2f));
            var text = Label(area, "", 25, TextAnchor.MiddleCenter, Accent, FontStyle.BoldAndItalic);
            Fill(text.rectTransform, Vector2.zero, Vector2.zero);
            stepper.Step = d =>
            {
                step(d);
                RefreshAll();
            };
            refreshers.Add(() => text.text = value());
            AddMark(go, rt);
            return stepper;
        }

        /// <summary>Champ de saisie sur plaque inclinée (le parent le place).</summary>
        InputField MakeInput(Transform parent, int size, TextAnchor align, Color color, FontStyle style, string hintFr, string hintEn, int limit)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(InputField));
            go.transform.SetParent(parent, false);
            var shape = go.GetComponent<HudShape>().Set(14f, 0f, 0f);
            shape.color = Color.white;
            shape.raycastTarget = true;
            var input = go.GetComponent<InputField>();
            input.targetGraphic = shape;
            input.colors = RowColors();
            float pad = align == TextAnchor.MiddleCenter ? 12f : 22f;
            var text = Label(go.transform, "", size, align, color, style);
            text.supportRichText = false;
            text.GetComponent<Shadow>().enabled = false;
            Place(text.rectTransform, Vector2.zero, Vector2.one, pad, pad);
            var hint = Label(go.transform, "", size - 2, align, new Color(1f, 1f, 1f, 0.28f), FontStyle.Italic);
            hint.GetComponent<Shadow>().enabled = false;
            if (hintFr.Length > 0) Loc.Bind(hint, hintFr, hintEn);
            Place(hint.rectTransform, Vector2.zero, Vector2.one, pad, pad);
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = InputField.LineType.SingleLine;
            // Choisi à la manette ou au clavier, le champ s'éclaire seulement ; la saisie commence au clic ou à A / Entrée
            input.shouldActivateOnSelect = false;
            input.characterLimit = limit;
            input.caretColor = Accent;
            input.caretWidth = 2;
            input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            return input;
        }

        /// <summary>Trait or à gauche de <paramref name="host"/>, allumé quand <paramref name="target"/> est choisi.</summary>
        void AddMark(GameObject target, RectTransform host)
        {
            var bar = Shape(host, "Mark", Accent).Set(8f, 0f, 0f);
            var rt = bar.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(-20f, 8f);
            rt.offsetMax = new Vector2(-13f, -8f);
            bar.enabled = false;
            marks.Add((target, bar));
        }

        /// <summary>Bouton principal : filet or sous le libellé.</summary>
        static void Primary(Button button)
        {
            var edge = Shape(button.transform, "Edge", Accent).Set(6f, 0f, 0f);
            var rt = edge.rectTransform;
            rt.anchorMin = new Vector2(0.03f, 0f);
            rt.anchorMax = new Vector2(0.97f, 0f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, 4f);
            var colors = button.colors;
            colors.normalColor = new Color(0.18f, 0.2f, 0.25f, 0.98f);
            button.colors = colors;
        }

        static ColorBlock RowColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = RowNormal;
            colors.highlightedColor = RowHover;
            colors.selectedColor = RowSelected;
            colors.pressedColor = RowSelected;
            colors.disabledColor = RowNormal;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        Image Dot(RectTransform parent, Vector2 anchor, Vector2 pos, float size, Color color)
        {
            var img = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(parent, false);
            img.sprite = HudArt.Disc;
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            return img;
        }

        void Note(RectTransform parent, string fr, string en, float height)
        {
            var line = Label(parent, "", 19, TextAnchor.UpperLeft, Dim, FontStyle.Italic);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            Loc.Bind(line, fr, en);
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = height + 20f;
        }

        static void Gap(RectTransform parent, float height) => Fixed(Rect(parent, "Gap", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), height);

        /// <summary>Hauteur fixe d'une ligne de la plaque.</summary>
        static void Fixed(RectTransform row, float height)
        {
            var le = row.gameObject.GetComponent<LayoutElement>();
            if (!le) le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        static void Spacer(RectTransform parent)
        {
            var s = Rect(parent, "Spacer", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            s.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
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

        /// <summary>Ancre un élément sur une portion de son parent, avec des marges (gauche, droite, bas, haut ; haut = bas par défaut).</summary>
        static void Place(RectTransform rt, Vector2 min, Vector2 max, float left = 0f, float right = 0f, float bottom = 0f, float top = -1f)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -(top < 0f ? bottom : top));
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
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 60;
            le.flexibleHeight = 0f;
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
            Place(t.rectTransform, Vector2.zero, Vector2.one, align == TextAnchor.MiddleLeft ? 26f : 0f, 0f);
            return (b, t);
        }
    }
}
