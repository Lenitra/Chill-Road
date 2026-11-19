using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Salon multijoueur (construit en code), ouvert depuis l'écran d'accueil, dans le style verre : image floutée du
    /// jeu, titre fin espacé et filet or + cyan, deux panneaux de verre côte à côte.
    /// - En-tête : titre, et la carte du pilote (pseudo, modifiable à tout moment) en haut à droite.
    /// - Panneau « Héberger » (à gauche) : réglages de la partie (nom, joueurs au plus, collisions entre joueurs, code
    ///   d'invitation), tous mémorisés, comment les amis entrent (code, réseau local, adresse) et « Ouvrir la partie ».
    /// - Panneau « Rejoindre » (à droite) : code d'invitation (la voie simple par Internet), connexion directe par
    ///   adresse IP, puis les parties du réseau local (recherche toutes les 1,5 s ; nom, adresse, collisions, joueurs et
    ///   jauge de places ; version différente grisée) et la ligne d'état de la recherche.
    /// - Le panneau où se trouve l'élément choisi s'éclaire en or.
    /// - Fenêtre de connexion par-dessus (carte de verre centrée) : roue, partie visée, temps écoulé, « Annuler » ; en cas
    ///   d'échec, le motif et « OK ».
    /// Manette : LB/RB (clavier : Tab, Maj+Tab) pour passer d'un panneau à l'autre, flèches pour se déplacer,
    /// gauche/droite sur un réglage pour le changer, A pour valider, B / Échap pour revenir.
    /// </summary>
    public class NetLobby : MonoBehaviour
    {
        // Fonds du style verre (maquette U-Multi) : lignes et champs un peu plus sombres que le panneau
        static readonly Color PanelFill = new Color(0.063f, 0.078f, 0.102f, 0.42f);
        static readonly Color RowFill = new Color(0.063f, 0.078f, 0.102f, 0.4f);
        static readonly Color InputFill = new Color(0.063f, 0.078f, 0.102f, 0.35f);
        static readonly Color InputEdge = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color HoverFill = new Color(1f, 1f, 1f, 0.14f);
        static readonly Color ServerFill = new Color(1f, 1f, 1f, 0.08f);

        const string AddressKey = "chillroad_net_addr", CollisionsKey = "chillroad_net_collisions";
        const string ServerNameKey = "chillroad_net_server", MaxPlayersKey = "chillroad_net_maxplayers", InviteKey = "chillroad_net_invite";
        // Lignes de la liste du réseau local : ce qui tient dans le panneau sous le code et l'adresse
        const int Rows = 4, MinPlayers = 2;
        const float SearchPeriod = 1.5f;
        // Géométrie (maquette 1280 × 720 multipliée par 1,5)
        const float Margin = 195f, PanelTop = 183f, PanelW = 750f, PanelH = 750f, PanelGap = 30f;

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
            public Image dot;
            public GlassRect fill;
            public RectTransform fillRt;
        }

        /// <summary>Aspect d'un élément choisissable : ligne (liseré or et halo), pilule pleine ou soulignée (or plein), bouton principal (or, halo).</summary>
        enum Look { Row, Pill, Outline, Primary }

        /// <summary>Élément qui s'éclaire quand il est choisi (ou survolé, ou en saisie).</summary>
        class Focusable
        {
            public GameObject target;
            public GlassRect plate;
            public Look look;
            public Text text;
            public Color fill, edge, textColor;
            public float border;
            public OptionHit hit;
            public InputField input;
            public int state = -1;
        }

        enum Modal { None, Connecting, Error }

        GameManager game;
        NetGame net;
        GameObject root;
        CanvasGroup rootGroup, panelGroup;
        RectTransform panelRt;
        Action onClose;
        int openedFrame;
        float openT;

        // En-tête, panneaux, pied
        InputField nameField;
        GlassRect hostPanel, joinPanel;
        Text hostTitle, joinTitle, hintText;
        static int lastTab;
        int tab, shownTab = -1;
        Button backButton;
        readonly HashSet<GameObject> hostSide = new HashSet<GameObject>(), joinSide = new HashSet<GameObject>();
        readonly List<Focusable> focusables = new List<Focusable>();

        // Rejoindre
        readonly List<ServerRow> rows = new List<ServerRow>();
        readonly List<NetGame.ServerInfo> shown = new List<NetGame.ServerInfo>();
        readonly List<Selectable> activeRows = new List<Selectable>();
        GameObject emptyState;
        Text emptyTitle, countText, statusText;
        Image radar, statusRing;
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
            var screen = (RectTransform)cgo.transform;

            // Image floutée de l'écran d'accueil (ou du jeu) sous un voile
            MenuBackdrop.Attach(screen, 0.5f);
            var content = Glass.Rect(screen, "Lobby", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panelRt = content;
            panelGroup = content.gameObject.AddComponent<CanvasGroup>();

            BuildHeader(content);
            hostPanel = PanelAt(content, "Host", Margin, "TU INVITES", "YOU INVITE", "HÉBERGER", "HOST", 1, out hostTitle);
            joinPanel = PanelAt(content, "Join", Margin + PanelW + PanelGap, "ON T'INVITE", "YOU'RE INVITED", "REJOINDRE", "JOIN", 0, out joinTitle);
            BuildHost(hostPanel.rectTransform.parent as RectTransform);
            BuildJoin(joinPanel.rectTransform.parent as RectTransform);
            BuildFooter(content);

            BuildModal(screen);
            ShowTab(lastTab);
            root.SetActive(false);
        }

        // ------------------------------------------------------------------ construction

        /// <summary>Titre fin espacé et filet or + cyan à gauche ; carte du pilote (pseudo) à droite.</summary>
        void BuildHeader(RectTransform content)
        {
            var title = Glass.Label(content, "", 57, Glass.Weight.Light, Glass.White, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(Margin, -48f), new Vector2(1000f, 74f));
            Loc.Bind(title, Glass.Spaced("MULTIJOUEUR", 2), Glass.Spaced("MULTIPLAYER", 2));
            Glass.Soft(title);
            var gold = Glass.Make(content, "RuleGold", 2f, Glass.Gold, 0f);
            Put(gold.rectTransform, new Vector2(0f, 1f), new Vector2(Margin, -132f), new Vector2(225f, 4f));
            var cyan = Glass.Make(content, "RuleCyan", 2f, Glass.Cyan, 0f);
            Put(cyan.rectTransform, new Vector2(0f, 1f), new Vector2(Margin + 234f, -132f), new Vector2(66f, 4f));

            // Carte du pilote : le champ lui-même est la pilule, « PILOTE » posé à gauche dedans
            GlassRect plate;
            (nameField, plate) = MakeInput(content, "Driver", 25, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft,
                "ton pseudo", "your name", NetProtocol.MaxName, 150f, 30f);
            plate.radius = 36f;
            plate.color = Glass.Panel;
            plate.borderColor = Glass.Edge;
            Put((RectTransform)nameField.transform, new Vector2(1f, 1f), new Vector2(-Margin, -56f), new Vector2(560f, 72f));
            var label = Kick(nameField.transform, "PILOTE", "DRIVER", 16);
            Put(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(32f, 0f), new Vector2(120f, 30f));
            nameField.onEndEdit.AddListener(_ => { SaveFields(); RefreshAll(); });
            AddFocus(nameField.gameObject, plate, Look.Row, null, nameField.GetComponent<OptionHit>());
        }

        /// <summary>Panneau de verre posé en haut de la page : petit titre cyan, grand titre fin ; le contenu s'empile dessous.</summary>
        GlassRect PanelAt(RectTransform content, string name, float x, string kickFr, string kickEn, string fr, string en, int index, out Text title)
        {
            var panel = Glass.Place(content, name, new Vector2(0f, 1f), new Vector2(x, -PanelTop), new Vector2(PanelW, PanelH));
            var plate = Glass.Backdrop(panel, 48f, PanelFill);
            // Un clic dans le vide du panneau le choisit
            plate.raycastTarget = true;
            plate.gameObject.AddComponent<OptionHit>().Click = () => ShowTab(index);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 42, 40);
            layout.spacing = 0f;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            Tall(Kick(panel, kickFr, kickEn, 16), 22f);
            Gap(panel, 4f);
            title = Glass.Label(panel, "", 45, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft);
            Loc.Bind(title, Glass.Spaced(fr, 1), Glass.Spaced(en, 1));
            Tall(title, 52f);
            Gap(panel, 16f);
            return plate;
        }

        /// <summary>Panneau « Héberger » : réglages, comment les amis entrent, puis « Ouvrir la partie » en bas.</summary>
        void BuildHost(RectTransform panel)
        {
            // Nom de la partie : la ligne entière est le champ, le nom saisi à droite en or
            GlassRect plate;
            (serverField, plate) = MakeInput(panel, "ServerName", 23, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleRight,
                "", "", NetProtocol.MaxServerName, 300f, 26f);
            AsRow(plate);
            Tall(serverField, 62f);
            var nameLabel = Glass.Label(serverField.transform, "", 24, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft);
            Loc.Bind(nameLabel, "Nom de la partie", "Game name");
            Put(nameLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(300f, 40f));
            serverField.onEndEdit.AddListener(_ => SaveFields());
            AddFocus(serverField.gameObject, plate, Look.Row, null, serverField.GetComponent<OptionHit>());
            hostSide.Add(serverField.gameObject);

            Gap(panel, 10f);
            maxStepper = AddStepper(panel, "Joueurs au plus", "Max players",
                d => HostMaxPlayers = Mathf.Clamp(HostMaxPlayers + d, MinPlayers, NetProtocol.MaxPlayersLimit),
                () => HostMaxPlayers.ToString(), null);
            Gap(panel, 10f);
            collisionsStepper = AddStepper(panel, "Collisions entre joueurs", "Player collisions",
                d => HostCollisions = !HostCollisions,
                () => HostCollisions ? Loc.T("Activées", "On") : Loc.T("Désactivées", "Off"), () => HostCollisions);
            Gap(panel, 10f);
            inviteStepper = AddStepper(panel, "Code d'invitation", "Invite code",
                d => HostInvite = !HostInvite,
                () => HostInvite ? Loc.T("Activé", "On") : Loc.T("Désactivé", "Off"), () => HostInvite);

            // Comment les amis entrent (textes remplis par RefreshAll)
            Gap(panel, 16f);
            wayCode = Way(panel);
            Gap(panel, 6f);
            wayLocal = Way(panel);
            Gap(panel, 6f);
            wayAddress = Way(panel);
            Spacer(panel);
            var open = Pill(panel, "Open", Host, Look.Primary, 26, 84f);
            Loc.Bind(open.text, Glass.Spaced("OUVRIR LA PARTIE", 1), Glass.Spaced("OPEN THE GAME", 1));
            openButton = open.button;
            hostSide.Add(openButton.gameObject);
        }

        /// <summary>Texte d'aide estompé du panneau « Héberger ».</summary>
        static Text Way(RectTransform parent)
        {
            var t = Glass.Label(parent, "", 21, Glass.Weight.Semilight, Glass.Dim, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            Tall(t, 56f);
            return t;
        }

        /// <summary>Panneau « Rejoindre » : code d'invitation, adresse IP, parties du réseau local et état de la recherche.</summary>
        void BuildJoin(RectTransform panel)
        {
            Tall(Kick(panel, "CODE D'INVITATION", "INVITE CODE", 16), 22f);
            Gap(panel, 8f);
            (codeField, codeButton) = JoinRow(panel, "CodeRow", 74f, 32, "ex. K7Q2XM", "e.g. K7Q2XM", NetRelay.CodeMax, "Rejoindre", "Join", JoinByCode);
            codeField.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';

            Gap(panel, 14f);
            Tall(Kick(panel, "ADRESSE IP", "IP ADDRESS", 16), 22f);
            Gap(panel, 8f);
            (addressField, addressButton) = JoinRow(panel, "AddressRow", 62f, 24, "ex. 192.168.1.20", "e.g. 192.168.1.20", 60, "Se connecter", "Connect", JoinAddress);

            // Parties du réseau local : rubrique et nombre trouvé à droite
            Gap(panel, 18f);
            var head = Tall(Glass.Rect(panel, "Network", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), 24f);
            var kick = Kick(head, "SUR TON RÉSEAU", "ON YOUR NETWORK", 16);
            Glass.Stretch(kick.rectTransform);
            countText = Glass.Label(head, "", 20, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleRight);
            Glass.Stretch(countText.rectTransform);
            Gap(panel, 10f);

            // Liste (lignes fixes, cachées tant qu'elles sont vides) et état vide par-dessus
            var list = Tall(Glass.Rect(panel, "List", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), Rows * 64f + (Rows - 1) * 8f);
            var lv = list.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 8;
            lv.childControlHeight = true; lv.childControlWidth = true;
            lv.childForceExpandHeight = false; lv.childForceExpandWidth = true;
            for (int i = 0; i < Rows; i++) rows.Add(ServerLine(list, i));

            var empty = Glass.Rect(list, "Empty", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            empty.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var emptyPlate = Glass.Backdrop(empty, 24f, new Color(1f, 1f, 1f, 0.03f));
            emptyPlate.borderColor = Glass.Line;
            radar = new GameObject("Radar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            radar.transform.SetParent(empty, false);
            radar.sprite = Glass.Ring(0.06f);
            radar.raycastTarget = false;
            Put(radar.rectTransform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(110f, 110f));
            radar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Dot(empty, new Vector2(0.5f, 0.6f), Vector2.zero, 12f, Glass.Cyan);
            emptyTitle = Glass.Label(empty, "", 22, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleCenter);
            Put(emptyTitle.rectTransform, new Vector2(0.5f, 0.28f), Vector2.zero, new Vector2(600f, 34f));
            emptyTitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            emptyState = empty.gameObject;

            // Ligne d'état : anneau cyan qui tourne, « Recherche en cours »
            Spacer(panel);
            var status = Tall(Glass.Rect(panel, "Status", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), 28f);
            statusRing = new GameObject("Spinner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            statusRing.transform.SetParent(status, false);
            statusRing.sprite = Glass.Ring(0.14f);
            statusRing.type = Image.Type.Filled;
            statusRing.fillMethod = Image.FillMethod.Radial360;
            statusRing.fillAmount = 0.75f;
            statusRing.color = Glass.Cyan;
            statusRing.raycastTarget = false;
            var srt = statusRing.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(11f, 0f);
            srt.sizeDelta = new Vector2(21f, 21f);
            statusText = Glass.Label(status, "", 20, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleLeft);
            Glass.Stretch(statusText.rectTransform);
            statusText.rectTransform.offsetMin = new Vector2(34f, 0f);

            joinSide.Add(codeField.gameObject);
            joinSide.Add(codeButton.gameObject);
            joinSide.Add(addressField.gameObject);
            joinSide.Add(addressButton.gameObject);
            foreach (var r in rows) joinSide.Add(r.button.gameObject);
        }

        /// <summary>Ligne « champ de verre + pilule soulignée » du panneau Rejoindre ; Entrée dans le champ vaut le bouton.</summary>
        (InputField field, Button button) JoinRow(RectTransform parent, string name, float height, int size, string hintFr, string hintEn, int max,
                                                  string fr, string en, Action action)
        {
            var row = Tall(Glass.Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), height);
            var (field, plate) = MakeInput(row, "Input", size, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft, hintFr, hintEn, max, 30f, 24f);
            var frt = (RectTransform)field.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = new Vector2(-184f, 0f);
            field.onSubmit.AddListener(_ => action());
            AddFocus(field.gameObject, plate, Look.Row, null, field.GetComponent<OptionHit>());
            var b = Pill(row, "Button", () => action(), Look.Outline, 21, height);
            Loc.Bind(b.text, fr, en);
            var brt = (RectTransform)b.button.transform;
            brt.anchorMin = new Vector2(1f, 0f); brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(-170f, 4f); brt.offsetMax = new Vector2(0f, -4f);
            return (field, b.button);
        }

        void BuildFooter(RectTransform content)
        {
            var back = Pill(content, "Back", Close, Look.Pill, 23, 66f);
            Loc.Bind(back.text, "Retour", "Back");
            Put((RectTransform)back.button.transform, Vector2.zero, new Vector2(Margin, 51f), new Vector2(190f, 66f));
            backButton = back.button;
            // Aide manette / clavier en bas à droite, touches en or (mise à jour selon l'appareil)
            hintText = Glass.Label(content, "", 22, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleRight,
                new Vector2(1f, 0f), new Vector2(-Margin, 51f), new Vector2(1200f, 66f));
            Glass.Soft(hintText);
        }

        /// <summary>Fenêtre de connexion (ou d'échec) : carte de verre centrée ; elle bloque le reste tant qu'elle est ouverte.</summary>
        void BuildModal(RectTransform screen)
        {
            var veil = Glass.Rect(screen, "Modal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            veil.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.5f);
            modalRoot = veil.gameObject;
            var box = Glass.Place(veil, "Box", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 410f));
            Glass.Backdrop(box, 42f, Glass.PanelStrong);
            modalTitle = Glass.Label(box, "", 36, Glass.Weight.Light, Glass.White, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(840f, 50f));
            // Filet court sous le titre : cyan pendant la connexion, corail en cas d'échec
            var top = Glass.Make(box, "Rule", 2f, Glass.Cyan, 0f);
            modalTop = top;
            Put(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -98f), new Vector2(120f, 4f));
            spinner = new GameObject("Spinner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            spinner.transform.SetParent(box, false);
            spinner.sprite = Glass.Ring(0.12f);
            spinner.type = Image.Type.Filled;
            spinner.fillMethod = Image.FillMethod.Radial360;
            spinner.fillAmount = 0.3f;
            spinner.color = Glass.Cyan;
            spinner.raycastTarget = false;
            var srt = spinner.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(0f, -150f);
            srt.sizeDelta = new Vector2(60f, 60f);
            modalText = Glass.Label(box, "", 24, Glass.Weight.Semilight, Glass.White, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -196f), new Vector2(800f, 64f));
            modalText.horizontalOverflow = HorizontalWrapMode.Wrap;
            modalDetail = Glass.Label(box, "", 19, Glass.Weight.Semilight, Glass.Dim, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -262f), new Vector2(800f, 30f));
            var button = Pill(box, "Button", ModalAction, Look.Pill, 23, 66f);
            modalButton = button.button;
            modalButtonText = button.text;
            modalButton.navigation = new Navigation { mode = Navigation.Mode.None };
            Put((RectTransform)modalButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(300f, 66f));
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
            modalTitle.text = Glass.Spaced(title, 1);
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
            modalTitle.color = error ? Glass.Bad : Glass.White;
            modalTop.color = error ? Glass.Bad : Glass.Cyan;
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

        // ------------------------------------------------------------------ panneaux et navigation

        /// <summary>Choisit un panneau (0 Rejoindre, 1 Héberger) et son premier élément.</summary>
        void ShowTab(int index)
        {
            tab = lastTab = (index % 2 + 2) % 2;
            wiredRows = -1;
            Wire();
            PanelLooks();
            if (IsOpen && EventSystem.current) EventSystem.current.SetSelectedGameObject(FirstOfTab());
        }

        GameObject FirstOfTab() => tab == 0 ? codeField.gameObject : openButton.gameObject;

        /// <summary>Panneau choisi : teinte or douce, liseré or, halo et titre en or ; l'autre reste en verre sombre.</summary>
        void PanelLooks()
        {
            if (shownTab == tab) return;
            shownTab = tab;
            for (int i = 0; i < 2; i++)
            {
                bool on = i == tab;
                var plate = i == 0 ? joinPanel : hostPanel;
                plate.color = on ? Glass.A(Glass.Gold, 0.035f) : PanelFill;
                plate.borderColor = on ? Glass.Gold : Glass.Edge;
                plate.border = on ? 2.25f : 1.5f;
                plate.halo = on ? 14f : 0f;
                plate.haloColor = Glass.A(Glass.Gold, 0.16f);
                plate.SetVerticesDirty();
                (i == 0 ? joinTitle : hostTitle).color = on ? Glass.Gold : Glass.White;
            }
        }

        /// <summary>Navigation explicite entre les deux panneaux (la liste du réseau local la refait quand elle change).</summary>
        void Wire()
        {
            activeRows.Clear();
            foreach (var r in rows)
                if (r.button.gameObject.activeSelf) activeRows.Add(r.button);
            wiredRows = activeRows.Count;
            Selectable firstRow = activeRows.Count > 0 ? activeRows[0] : null;
            Selectable joinEntry = firstRow != null ? firstRow : (Selectable)addressField;
            Selectable belowAddress = firstRow != null ? firstRow : (Selectable)backButton;
            for (int i = 0; i < activeRows.Count; i++)
                Nav(activeRows[i], i == 0 ? (Selectable)addressField : activeRows[i - 1], i == activeRows.Count - 1 ? (Selectable)backButton : activeRows[i + 1], openButton, null);
            // Héberger (à gauche)
            Nav(serverField, nameField, maxStepper, null, codeField);
            Nav(maxStepper, serverField, collisionsStepper, null, null);
            Nav(collisionsStepper, maxStepper, inviteStepper, null, null);
            Nav(inviteStepper, collisionsStepper, openButton, null, null);
            Nav(openButton, inviteStepper, backButton, null, joinEntry);
            // Rejoindre (à droite), pilote au-dessus
            Nav(nameField, null, codeField, serverField, null);
            Nav(codeField, nameField, addressField, serverField, codeButton);
            Nav(codeButton, nameField, addressButton, codeField, null);
            Nav(addressField, codeField, belowAddress, maxStepper, addressButton);
            Nav(addressButton, codeButton, belowAddress, addressField, null);
            Nav(backButton, openButton, null, null, joinEntry);
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
            wayCode.color = HostInvite ? Glass.Dim : Glass.Faint;
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

            // Arrivée : fondu et léger zoom
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
                // LB / Maj+Tab : panneau de gauche (Héberger) ; RB / Tab : panneau de droite (Rejoindre)
                if (GameInput.TabPrevPressed) ShowTab(1);
                else if (GameInput.TabNextPressed) ShowTab(0);
            }

            UpdateServers(dt, now);
            UpdateModal(dt, now);

            // Le panneau suit l'élément choisi (souris, flèches)
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (selected)
            {
                if (hostSide.Contains(selected)) tab = lastTab = 1;
                else if (joinSide.Contains(selected)) tab = lastTab = 0;
            }
            PanelLooks();
            foreach (var f in focusables) Style(f, selected);

            bool pad = GameInput.UsingGamepad;
            hintText.text = pad
                ? Loc.T("<color=#FFC233>LB RB</color>  panneau     <color=#FFC233>← →</color>  régler     <color=#FFC233>A</color>  valider     <color=#FFC233>B</color>  retour",
                        "<color=#FFC233>LB RB</color>  panel     <color=#FFC233>← →</color>  adjust     <color=#FFC233>A</color>  select     <color=#FFC233>B</color>  back")
                : Loc.T("<color=#FFC233>Tab</color>  panneau     <color=#FFC233>← →</color>  régler     <color=#FFC233>Entrée</color>  valider     <color=#FFC233>Échap</color>  retour",
                        "<color=#FFC233>Tab</color>  panel     <color=#FFC233>← →</color>  adjust     <color=#FFC233>Enter</color>  select     <color=#FFC233>Esc</color>  back");

            // Manette : garder un élément choisi
            if (EventSystem.current && pad && (selected == null || !selected.activeInHierarchy))
                EventSystem.current.SetSelectedGameObject(modal != Modal.None ? modalButton.gameObject : FirstOfTab());
        }

        /// <summary>Aspect d'un élément selon qu'il est choisi (ou en saisie), survolé, ou au repos ; refait seulement s'il change.</summary>
        void Style(Focusable f, GameObject selected)
        {
            bool on = f.target == selected || (f.input && f.input.isFocused);
            bool hover = f.hit && f.hit.Hover;
            int state = (on ? 1 : 0) | (hover ? 2 : 0);
            if (state == f.state) return;
            f.state = state;
            var p = f.plate;
            switch (f.look)
            {
                case Look.Row:
                    p.color = on ? Glass.GoldSoft : hover ? HoverFill : f.fill;
                    p.borderColor = on ? Glass.Gold : f.edge;
                    p.border = on ? 2.25f : f.border;
                    p.halo = on ? 10f : 0f;
                    p.haloColor = Glass.GoldHalo;
                    if (f.text) f.text.color = on ? Glass.Gold : f.textColor;
                    break;
                case Look.Pill:
                case Look.Outline:
                    p.color = on ? Glass.Gold : hover ? HoverFill : f.fill;
                    p.borderColor = on ? Color.clear : f.edge;
                    if (f.text)
                    {
                        f.text.color = on ? Glass.Ink : f.textColor;
                        f.text.font = Glass.FontOf(on ? Glass.Weight.Semibold : Glass.Weight.Regular);
                    }
                    break;
                case Look.Primary:
                    p.color = hover && !on ? new Color(1f, 0.82f, 0.38f) : Glass.Gold;
                    p.borderColor = on ? Glass.A(Color.white, 0.9f) : Color.clear;
                    p.border = 2f;
                    p.halo = on ? 14f : 0f;
                    p.haloColor = Glass.GoldHalo;
                    break;
            }
            p.SetVerticesDirty();
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
                row.players.text = full ? Loc.T($"{s.players} / {s.max}  ·  COMPLÈTE", $"{s.players} / {s.max}  ·  FULL")
                                        : Loc.T($"{s.players} / {s.max} joueurs", $"{s.players} / {s.max} players");
                row.players.color = full ? Glass.Bad : Glass.Dim;
                row.fillRt.localScale = new Vector3(s.max > 0 ? Mathf.Clamp01((float)s.players / s.max) : 0f, 1f, 1f);
                row.fill.color = full ? Glass.Bad : Glass.Cyan;
                row.dot.color = !s.compatible || full ? Glass.Bad : Glass.Good;
                row.address.text = s.ep.Port == NetProtocol.DefaultPort ? s.ep.Address.ToString() : s.ep.ToString();
                if (!s.compatible)
                {
                    row.tag.text = Loc.T("AUTRE VERSION", "OTHER VERSION");
                    row.tag.color = Glass.Bad;
                }
                else
                {
                    row.tag.text = s.collisions ? Loc.T("collisions activées", "collisions on") : Loc.T("collisions désactivées", "collisions off");
                    row.tag.color = s.collisions ? Glass.Gold : Glass.Faint;
                }
                row.group.alpha = s.compatible ? 1f : 0.55f;
            }
            if (shown.Count != wiredRows)
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
                radar.color = Glass.A(Glass.Cyan, (1f - wave) * 0.8f);
                emptyTitle.text = noNetwork ? Loc.T("Aucun réseau détecté", "No network found") : Loc.T("Aucune partie trouvée pour l'instant", "No game found yet");
            }
            countText.text = shown.Count == 0 ? ""
                : shown.Count == 1 ? Loc.T("1 partie trouvée", "1 game found")
                : Loc.T($"{shown.Count} parties trouvées", $"{shown.Count} games found");
            // Ligne d'état : l'anneau tourne tant que la recherche tourne
            statusRing.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -now * 240f);
            statusRing.color = noNetwork ? Glass.Faint : Glass.Cyan;
            statusText.text = noNetwork ? Loc.T("Aucun réseau détecté", "No network found") : Loc.T("Recherche en cours", "Searching") + Dots();
        }

        void UpdateModal(float dt, float now)
        {
            if (modal != Modal.Connecting) return;
            attemptT += dt;
            spinner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -now * 300f);
            spinner.fillAmount = 0.2f + 0.15f * Mathf.Sin(now * 3f);
            bool finding = net && net.FindingCode;
            modalTitle.text = Glass.Spaced(Loc.T("CONNEXION", "CONNECTING"), 1);
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

        /// <summary>Ligne d'une partie du réseau local : point vert, nom et adresse, collisions, joueurs et jauge, pilule « Rejoindre ».</summary>
        ServerRow ServerLine(RectTransform parent, int index)
        {
            var go = new GameObject("Server" + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button),
                typeof(LayoutElement), typeof(CanvasGroup), typeof(OptionHit));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 64;
            var plate = go.GetComponent<GlassRect>();
            plate.radius = 26f;
            plate.border = 1.5f;
            plate.color = ServerFill;
            plate.borderColor = Glass.Edge;
            var b = go.GetComponent<Button>();
            b.targetGraphic = plate;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => JoinFound(index));
            var hit = go.GetComponent<OptionHit>();

            var row = new ServerRow { button = b, group = go.GetComponent<CanvasGroup>() };
            row.dot = Dot((RectTransform)go.transform, new Vector2(0f, 0.5f), new Vector2(30f, 0f), 15f, Glass.Good);
            row.name = Glass.Label(go.transform, "", 23, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(52f, 11f), new Vector2(270f, 30f));
            row.name.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.name.verticalOverflow = VerticalWrapMode.Truncate;
            row.address = Glass.Label(go.transform, "", 16, Glass.Weight.Semilight, Glass.Faint, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(52f, -15f), new Vector2(175f, 22f));
            row.tag = Glass.Label(go.transform, "", 16, Glass.Weight.Semilight, Glass.Gold, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(232f, -15f), new Vector2(200f, 22f));

            row.players = Glass.Label(go.transform, "", 19, Glass.Weight.Regular, Glass.Dim, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(-152f, 8f), new Vector2(170f, 26f));
            var track = Glass.Make(go.transform, "Track", 2f, Glass.Line, 0f);
            Put(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(-152f, -15f), new Vector2(96f, 4f));
            row.fill = Glass.Make(track.transform, "Fill", 2f, Glass.Cyan, 0f);
            row.fillRt = row.fill.rectTransform;
            Glass.Stretch(row.fillRt);
            row.fillRt.pivot = new Vector2(0f, 0.5f);

            // Pilule « Rejoindre » : soulignée, pleine en or quand la ligne est choisie
            var pill = Glass.Make(go.transform, "Join", 21f, Color.clear, 1.5f);
            pill.borderColor = InputEdge;
            Put(pill.rectTransform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(124f, 42f));
            var pillText = Glass.Label(pill.transform, "", 19, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(pillText.rectTransform);
            Loc.Bind(pillText, "Rejoindre", "Join");

            AddFocus(go, plate, Look.Row, row.name, hit);
            AddFocus(go, pill, Look.Outline, pillText, hit);
            go.SetActive(false);
            return row;
        }

        /// <summary>Réglage à valeurs (voir <see cref="MenuStepper"/>) : « ‹ valeur › », ou interrupteur or si <paramref name="isOn"/> est donné.</summary>
        MenuStepper AddStepper(RectTransform parent, string fr, string en, Action<int> step, Func<string> value, Func<bool> isOn)
        {
            var go = new GameObject(en, typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(MenuStepper),
                typeof(LayoutElement), typeof(OptionHit));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 62;
            le.flexibleHeight = 0f;
            var plate = go.GetComponent<GlassRect>();
            AsRow(plate);
            var stepper = go.GetComponent<MenuStepper>();
            stepper.targetGraphic = plate;
            stepper.transition = Selectable.Transition.None;
            var name = Glass.Label(go.transform, "", 24, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft);
            Loc.Bind(name, fr, en);
            Put(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(380f, 40f));
            var area = Glass.Rect(go.transform, "Value", new Vector2(0.55f, 0f), Vector2.one, Vector2.zero, new Vector2(-22f, 0f));
            stepper.valueArea = area;
            if (isOn == null)
            {
                var leftArrow = Glass.Label(area, "‹", 34, Glass.Weight.Light, Glass.Dim, TextAnchor.MiddleLeft);
                Glass.Stretch(leftArrow.rectTransform);
                leftArrow.rectTransform.offsetMin = new Vector2(70f, 2f);
                var rightArrow = Glass.Label(area, "›", 34, Glass.Weight.Light, Glass.Dim, TextAnchor.MiddleRight);
                Glass.Stretch(rightArrow.rectTransform);
                rightArrow.rectTransform.offsetMin = new Vector2(0f, 2f);
                var text = Glass.Label(area, "", 24, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleCenter);
                Glass.Stretch(text.rectTransform);
                text.rectTransform.offsetMin = new Vector2(70f, 0f);
                refreshers.Add(() => text.text = value());
            }
            else
            {
                // Interrupteur : état en or à gauche, pilule or et pastille sombre à droite quand c'est activé
                var state = Glass.Label(area, "", 21, Glass.Weight.Regular, Glass.Gold, TextAnchor.MiddleRight);
                Glass.Stretch(state.rectTransform);
                state.rectTransform.offsetMax = new Vector2(-96f, 0f);
                var sw = Glass.Place(area, "Switch", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(75f, 39f));
                var track = Glass.Backdrop(sw, 20f, Glass.Gold, 0f);
                var knob = Glass.Place(sw, "Knob", new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(30f, 30f));
                var knobImg = knob.gameObject.AddComponent<Image>();
                knobImg.sprite = HudArt.Disc;
                knobImg.raycastTarget = false;
                refreshers.Add(() =>
                {
                    bool v = isOn();
                    state.text = value();
                    state.color = v ? Glass.Gold : Glass.Dim;
                    track.color = v ? Glass.Gold : Glass.A(Color.white, 0.18f);
                    knob.anchoredPosition = new Vector2(v ? 40f : 5f, 0f);
                    knobImg.color = v ? Glass.Ink : Glass.White;
                });
            }
            stepper.Step = d =>
            {
                step(d);
                RefreshAll();
            };
            AddFocus(go, plate, Look.Row, null, go.GetComponent<OptionHit>());
            hostSide.Add(go);
            return stepper;
        }

        /// <summary>Champ de saisie de verre arrondi (le parent le place) ; renvoie aussi son fond pour l'éclairer.</summary>
        (InputField input, GlassRect plate) MakeInput(Transform parent, string name, int size, Glass.Weight weight, Color color, TextAnchor align,
                                                       string hintFr, string hintEn, int limit, float padLeft, float padRight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(InputField), typeof(OptionHit));
            go.transform.SetParent(parent, false);
            var plate = go.GetComponent<GlassRect>();
            plate.radius = 27f;
            plate.border = 1.5f;
            plate.color = InputFill;
            plate.borderColor = InputEdge;
            plate.raycastTarget = true;
            var input = go.GetComponent<InputField>();
            input.targetGraphic = plate;
            input.transition = Selectable.Transition.None;
            var text = Glass.Label(go.transform, "", size, weight, color, align);
            text.supportRichText = false;
            Glass.Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(padLeft, 0f);
            text.rectTransform.offsetMax = new Vector2(-padRight, 0f);
            var hint = Glass.Label(go.transform, "", size - 2, Glass.Weight.Light, Glass.A(Color.white, 0.32f), align);
            hint.supportRichText = false;
            if (hintFr.Length > 0) Loc.Bind(hint, hintFr, hintEn);
            Glass.Stretch(hint.rectTransform);
            hint.rectTransform.offsetMin = new Vector2(padLeft, 0f);
            hint.rectTransform.offsetMax = new Vector2(-padRight, 0f);
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = InputField.LineType.SingleLine;
            // Choisi à la manette ou au clavier, le champ s'éclaire seulement ; la saisie commence au clic ou à A / Entrée
            input.shouldActivateOnSelect = false;
            input.characterLimit = limit;
            input.customCaretColor = true;
            input.caretColor = Glass.Gold;
            input.caretWidth = 2;
            input.selectionColor = Glass.A(Glass.Gold, 0.35f);
            return (input, plate);
        }

        /// <summary>Fond d'une ligne de réglage : verre plus sombre, sans liseré au repos.</summary>
        static void AsRow(GlassRect plate)
        {
            plate.radius = 24f;
            plate.border = 1.5f;
            plate.color = RowFill;
            plate.borderColor = Color.clear;
            plate.raycastTarget = true;
        }

        /// <summary>Bouton pilule : verre (Pill), souligné (Outline) ou or plein (Primary) ; libellé posé par l'appelant.</summary>
        (Button button, Text text) Pill(Transform parent, string name, UnityEngine.Events.UnityAction onClick, Look look, int size, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button), typeof(LayoutElement), typeof(OptionHit));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            var plate = go.GetComponent<GlassRect>();
            plate.radius = height;
            plate.border = 1.5f;
            plate.color = look == Look.Primary ? Glass.Gold : look == Look.Pill ? Glass.Pill : Color.clear;
            plate.borderColor = look == Look.Primary ? Color.clear : look == Look.Pill ? Glass.Edge : InputEdge;
            plate.raycastTarget = true;
            var b = go.GetComponent<Button>();
            b.targetGraphic = plate;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
            var t = Glass.Label(go.transform, "", size, look == Look.Primary ? Glass.Weight.Semibold : Glass.Weight.Regular,
                look == Look.Primary ? Glass.Ink : Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(t.rectTransform);
            AddFocus(go, plate, look, t, go.GetComponent<OptionHit>());
            return (b, t);
        }

        /// <summary>Retient l'aspect de repos d'un élément pour l'éclairer quand il est choisi.</summary>
        void AddFocus(GameObject target, GlassRect plate, Look look, Text text, OptionHit hit)
        {
            focusables.Add(new Focusable
            {
                target = target, plate = plate, look = look, text = text, hit = hit,
                fill = plate.color, edge = plate.borderColor, border = plate.border,
                textColor = text ? text.color : Color.white,
                input = target.GetComponent<InputField>(),
            });
        }

        /// <summary>Petit titre cyan espacé, traduit.</summary>
        static Text Kick(Transform parent, string fr, string en, int size)
        {
            var t = Glass.Label(parent, "", size, Glass.Weight.Semibold, Glass.Cyan);
            Loc.Bind(t, Glass.Spaced(fr, 2), Glass.Spaced(en, 2));
            return t;
        }

        static Image Dot(RectTransform parent, Vector2 anchor, Vector2 pos, float size, Color color)
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

        static void Gap(RectTransform parent, float height) => Tall(Glass.Rect(parent, "Gap", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), height);

        /// <summary>Hauteur fixe d'un élément dans la pile du panneau.</summary>
        static T Tall<T>(T c, float height) where T : Component
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (!le) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            return c;
        }

        static void Spacer(RectTransform parent)
        {
            var s = Glass.Rect(parent, "Spacer", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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

        /// <summary>Ancre et pivot au même point, position et taille.</summary>
        static void Put(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
