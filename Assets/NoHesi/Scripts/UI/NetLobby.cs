using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Salon multijoueur (construit en code), ouvert depuis l'écran d'accueil, dans le langage visuel des options :
    /// voile sombre, plaque inclinée anthracite, titre or, rubriques cyan.
    /// - pseudo du joueur ;
    /// - héberger : la partie démarre, les autres joueurs la rejoignent ;
    /// - rejoindre : les parties du réseau local apparaissent toutes seules (recherche toutes les 1,5 s) ; sinon,
    ///   adresse IP du serveur (réseau local ou Internet, port par défaut 47777).
    /// </summary>
    public class NetLobby : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Danger = new Color(1f, 0.42f, 0.34f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

        const string AddressKey = "nohesi_net_addr";
        const int Rows = 4;

        public static bool IsOpen { get; private set; }

        GameManager game;
        NetGame net;
        Font font;
        GameObject root;
        Action onClose;
        InputField nameField, addressField;
        Button hostButton;
        Text statusText, hostHint;
        readonly List<(Button button, Text text)> rows = new List<(Button, Text)>();
        readonly List<NetGame.ServerInfo> shown = new List<NetGame.ServerInfo>();
        string error;
        float errorTimer, searchTimer;
        int openedFrame;

        public void Init(GameManager gm)
        {
            game = gm;
            net = gm.Net;
            font = HudArt.Font;
            IsOpen = false;
            net.Joined += OnJoined;
            net.Failed += message => { error = message; errorTimer = 6f; };

            var cgo = new GameObject("NetLobby", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = cgo;

            var shade = Rect(cgo.transform, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.62f);
            var panel = Panel(shade, "Lobby", new Vector2(-560, -470), new Vector2(560, 470));

            var title = Label(panel, "", 64, TextAnchor.MiddleLeft, Accent, FontStyle.BoldAndItalic);
            Loc.Bind(title, "MULTIJOUEUR", "MULTIPLAYER");
            Fixed(title.rectTransform, 84);

            nameField = Field(panel, "Pseudo", "Name", "", "", NetProtocol.MaxName);

            Section(panel, "HÉBERGER", "HOST", "tu joues, les autres te rejoignent", "you play, the others join you");
            var host = AddButton(panel, Host);
            Loc.Bind(host.text, "Héberger une partie", "Host a game");
            hostButton = host.button;
            hostHint = Label(panel, "", 21, TextAnchor.MiddleLeft, Dim, FontStyle.Normal);
            Fixed(hostHint.rectTransform, 34);

            Section(panel, "REJOINDRE", "JOIN", "réseau local ou adresse IP", "local network or IP address");
            for (int i = 0; i < Rows; i++)
            {
                int index = i;
                var row = AddButton(panel, () => JoinFound(index), TextAnchor.MiddleLeft);
                row.button.GetComponent<LayoutElement>().preferredHeight = 52;
                row.text.fontSize = 24;
                row.button.gameObject.SetActive(false);
                rows.Add(row);
            }
            statusText = Label(panel, "", 23, TextAnchor.MiddleLeft, Dim, FontStyle.Italic);
            Fixed(statusText.rectTransform, 40);
            addressField = Field(panel, "Adresse du serveur", "Server address", "ex. 192.168.1.20", "e.g. 192.168.1.20", 60);
            var join = AddButton(panel, JoinAddress);
            Loc.Bind(join.text, "Rejoindre cette adresse", "Join this address");

            Spacer(panel);
            var back = AddButton(panel, Close);
            Loc.Bind(back.text, "Retour", "Back");
            root.SetActive(false);
        }

        // ------------------------------------------------------------------ ouverture / fermeture

        /// <summary>Ouvre le salon ; <paramref name="closed"/> est appelé si on revient à l'accueil sans jouer.</summary>
        public void Open(Action closed)
        {
            onClose = closed;
            nameField.text = NetGame.PlayerName;
            addressField.text = PlayerPrefs.GetString(AddressKey, "");
            error = null;
            searchTimer = 0f;
            var addresses = NetSocket.LocalAddresses();
            hostHint.text = addresses.Count > 0
                ? Loc.T($"Ton adresse sur le réseau local : {addresses[0]}   ·   par Internet : ouvre le port UDP {NetProtocol.DefaultPort} de ta box",
                        $"Your address on the local network: {addresses[0]}   ·   over the Internet: forward UDP port {NetProtocol.DefaultPort}")
                : Loc.T("Aucun réseau détecté sur cette machine", "No network found on this computer");
            root.SetActive(true);
            IsOpen = true;
            openedFrame = Time.frameCount;
            EventSystem.current?.SetSelectedGameObject(hostButton.gameObject);
        }

        public void Close()
        {
            net.CancelJoin();
            SaveFields();
            Hide();
            onClose?.Invoke();
        }

        void Hide()
        {
            root.SetActive(false);
            IsOpen = false;
        }

        void SaveFields()
        {
            NetGame.PlayerName = nameField.text;
            PlayerPrefs.SetString(AddressKey, addressField.text.Trim());
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ actions

        void Host()
        {
            if (net.Connecting) return;
            SaveFields();
            if (!net.Host(NetGame.PlayerName, NetProtocol.DefaultPort, NetProtocol.DefaultMaxPlayers, false, out string why))
            {
                error = why;
                errorTimer = 6f;
                return;
            }
            Hide();
            game.StartOnline();
        }

        void JoinFound(int index)
        {
            if (net.Connecting || index >= shown.Count) return;
            if (!shown[index].compatible)
            {
                error = Loc.T("Version du jeu différente de celle du serveur", "Game version differs from the server's");
                errorTimer = 6f;
                return;
            }
            SaveFields();
            error = null;
            net.Join(shown[index].ep);
        }

        void JoinAddress()
        {
            if (net.Connecting) return;
            SaveFields();
            error = null;
            net.Join(addressField.text);
        }

        void OnJoined()
        {
            if (!IsOpen) return;
            Hide();
            game.StartOnline();
        }

        void Update()
        {
            if (!IsOpen) return;
            float dt = Time.unscaledDeltaTime;
            if (Time.frameCount != openedFrame && GameInput.CancelPressed && !Typing)
            {
                if (net.Connecting) net.CancelJoin();
                else Close();
                return;
            }
            // Recherche des parties du réseau local
            searchTimer -= dt;
            if (searchTimer <= 0f && !net.Connecting)
            {
                searchTimer = 1.5f;
                net.Search();
            }
            shown.Clear();
            shown.AddRange(net.Found);
            for (int i = 0; i < rows.Count; i++)
            {
                bool on = i < shown.Count;
                if (rows[i].button.gameObject.activeSelf != on) rows[i].button.gameObject.SetActive(on);
                if (!on) continue;
                var s = shown[i];
                string version = s.compatible ? "" : Loc.T("   ·   autre version", "   ·   other version");
                rows[i].text.text = Loc.T($"{s.name}   ·   {s.players}/{s.max} joueurs   ·   {s.ep.Address}{version}",
                                          $"{s.name}   ·   {s.players}/{s.max} players   ·   {s.ep.Address}{version}");
            }
            errorTimer -= dt;
            if (net.Connecting)
            {
                statusText.color = Info;
                statusText.text = Loc.T("Connexion au serveur", "Connecting to the server") + Dots();
            }
            else if (error != null && errorTimer > 0f)
            {
                statusText.color = Danger;
                statusText.text = error;
            }
            else
            {
                statusText.color = Dim;
                statusText.text = shown.Count > 0 ? Loc.T("Parties trouvées sur le réseau local : choisis-en une", "Games found on the local network: pick one")
                    : Loc.T("Recherche de parties sur le réseau local", "Looking for games on the local network") + Dots();
            }
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == null && GameInput.UsingGamepad)
                EventSystem.current.SetSelectedGameObject(hostButton.gameObject);
        }

        /// <summary>Saisie en cours dans un champ : Échap quitte le champ, pas le salon.</summary>
        bool Typing => nameField.isFocused || addressField.isFocused;

        static string Dots() => new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);

        // ------------------------------------------------------------------ lignes

        static RectTransform Panel(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var panel = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), min, max);
            var plate = Shape(panel, "Plate", new Color(0.03f, 0.04f, 0.06f, 0.92f)).Set(24f, 0f, 0f);
            plate.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fill(plate.rectTransform, Vector2.zero, Vector2.zero);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(58, 52, 26, 30);
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
            Loc.Bind(Label(row, "", 26, TextAnchor.LowerLeft, Info, FontStyle.BoldAndItalic), fr, en);
            Loc.Bind(Label(row, "", 20, TextAnchor.LowerLeft, Dim, FontStyle.Italic), "·  " + noteFr, "·  " + noteEn);
            var line = Shape(row, "Line", new Color(Info.r, Info.g, Info.b, 0.4f)).Set(0f, 0f, 260f);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var lrt = line.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = new Vector2(0f, 2f);
        }

        /// <summary>Ligne de saisie : libellé à gauche, champ à droite.</summary>
        InputField Field(RectTransform parent, string fr, string en, string hintFr, string hintEn, int limit)
        {
            var row = Rect(parent, en, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fixed(row, 54);
            var name = Loc.Bind(Label(row, "", 25, TextAnchor.MiddleLeft, White, FontStyle.Normal), fr, en);
            Stretch(name.rectTransform, 0f, 0.34f, 6f, 0f);

            var go = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(InputField));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.36f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, 3f);
            rt.offsetMax = new Vector2(0f, -3f);
            var shape = go.GetComponent<HudShape>();
            shape.Set(14f, 0f, 0f);
            shape.color = Color.white;
            shape.raycastTarget = true;
            var input = go.GetComponent<InputField>();
            input.targetGraphic = shape;
            var colors = input.colors;
            colors.normalColor = new Color(0.14f, 0.16f, 0.2f, 0.95f);
            colors.highlightedColor = new Color(0.24f, 0.27f, 0.33f, 1f);
            colors.selectedColor = new Color(0.3f, 0.34f, 0.42f, 1f);
            colors.pressedColor = new Color(0.3f, 0.34f, 0.42f, 1f);
            input.colors = colors;
            var text = Label(go.transform, "", 25, TextAnchor.MiddleLeft, White, FontStyle.Bold);
            text.supportRichText = false;
            Stretch(text.rectTransform, 0f, 1f, 24f, 20f);
            var hint = Label(go.transform, "", 23, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.32f), FontStyle.Italic);
            Loc.Bind(hint, hintFr, hintEn);
            Stretch(hint.rectTransform, 0f, 1f, 24f, 20f);
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = limit;
            input.caretColor = Accent;
            input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            return input;
        }

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
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 58;
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
            Stretch(t.rectTransform, 0f, 1f, align == TextAnchor.MiddleLeft ? 26f : 0f, 0f);
            return (b, t);
        }
    }
}
