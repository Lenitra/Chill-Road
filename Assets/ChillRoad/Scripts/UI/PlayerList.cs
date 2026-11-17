using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Tableau des joueurs d'une partie en réseau : qui est là, qui héberge la partie, qui conduit quelle voiture (et de
    /// quelle couleur), et où en est chacun (distance, course à plusieurs).
    /// - en conduisant : Tab maintenu (clavier) ;
    /// - dans le menu d'une partie en réseau (Start, Échap, bouton II) : à droite du menu, à la place des contrôles ; Tab,
    ///   RB ou un clic sur le panneau passe de l'un à l'autre (<see cref="Hud.PlayersInMenu"/>).
    /// Chaque joueur garde la couleur de son étiquette au-dessus de sa voiture et de son point sur la minicarte.
    /// </summary>
    public class PlayerList : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Ink = new Color(0.03f, 0.04f, 0.06f);

        // Même largeur et, dans le menu, même place que le panneau des contrôles du HUD
        const float Width = 740f, MenuHeight = 740f, HeaderHeight = 124f, FooterHeight = 46f, RowMax = 46f, Inset = 30f;
        static readonly Vector2 HeldTop = new Vector2(0f, 330f), MenuTop = new Vector2(410f, 370f);

        class Row
        {
            public RectTransform rt;
            public HudShape back, badge, swatch;
            public Image dot;
            public Text name, badgeText, car, status;
        }

        GameManager game;
        Font font;
        RectTransform panel;
        CanvasGroup group;
        Button toggle;
        Text title, server, summary, footer, headName, headCar;
        readonly List<Row> rows = new List<Row>();
        readonly List<NetGame.PlayerInfo> players = new List<NetGame.PlayerInfo>();
        float refresh;
        bool shown, inMenu;

        public void Init(GameManager gm) => game = gm;

        void OnDestroy()
        {
            if (panel) Destroy(panel.parent.gameObject);
        }

        void LateUpdate()
        {
            if (!game || GameManager.DedicatedServer) return;
            var hud = game.Hud;
            bool online = game.Online;
            bool driving = game.State == GameManager.Phase.Playing || game.State == GameManager.Phase.Countdown;
            bool menu = online && hud && hud.PlayersInMenu;
            bool held = online && driving && !menu && GameInput.PlayersHeld && !(hud && hud.Paused) && !OptionsMenu.IsOpen;
            bool show = menu || held;
            if (!panel)
            {
                if (!show) return;
                Build();
            }
            float dt = Time.unscaledDeltaTime;
            if (show && (!shown || menu != inMenu))
            {
                inMenu = menu;
                refresh = 0f;
            }
            shown = show;
            // Dans le menu, il prend la place des contrôles d'un coup ; en conduisant, il apparaît et s'efface vite
            group.alpha = menu ? 1f : Mathf.MoveTowards(group.alpha, show ? 1f : 0f, dt * (show ? 14f : 8f));
            group.blocksRaycasts = group.interactable = menu;
            if (!show) return;
            refresh -= dt;
            if (refresh > 0f) return;
            refresh = 0.25f;
            Refresh();
        }

        // ------------------------------------------------------------------ contenu

        void Refresh()
        {
            var net = game.Net;
            net.ListPlayers(players);
            bool hostPlays = false;
            foreach (var p in players) hostPlays |= p.host;
            title.text = Loc.T("JOUEURS", "PLAYERS");
            headName.text = Loc.T("PILOTE", "DRIVER");
            headCar.text = Loc.T("VOITURE", "CAR");
            server.text = net.ServerName;
            string collisions = net.Collisions ? Loc.T("collisions activées", "collisions on") : Loc.T("collisions désactivées", "collisions off");
            string count = Loc.T($"{players.Count} / {net.MaxPlayers} joueurs", $"{players.Count} / {net.MaxPlayers} players");
            string dedicated = hostPlays ? "" : Loc.T("  ·  serveur dédié", "  ·  dedicated server");
            string code = net.InviteCode != null ? Loc.T($"  ·  code : {net.InviteCode}", $"  ·  code: {net.InviteCode}") : "";
            summary.text = count + "  ·  " + collisions + dedicated + code;

            while (rows.Count < players.Count) rows.Add(MakeRow());
            float listHeight = (inMenu ? MenuHeight - HeaderHeight - FooterHeight : RowMax * NetProtocol.MaxPlayersLimit);
            float rowHeight = Mathf.Min(RowMax, listHeight / Mathf.Max(1, players.Count));
            var cars = game.assets.playerCars;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool used = i < players.Count;
                if (row.rt.gameObject.activeSelf != used) row.rt.gameObject.SetActive(used);
                if (!used) continue;
                var p = players[i];
                row.rt.anchoredPosition = new Vector2(Inset, -HeaderHeight - i * rowHeight);
                row.rt.sizeDelta = new Vector2(Width - Inset * 2f, rowHeight - 4f);
                row.back.color = new Color(1f, 1f, 1f, p.local ? 0.1f : i % 2 == 0 ? 0.035f : 0f);
                row.dot.color = RemoteCar.Colors[p.id % RemoteCar.Colors.Length];
                row.name.text = p.local ? p.name + Loc.T("  (toi)", "  (you)") : p.name;
                row.name.color = p.local ? Accent : White;
                row.badge.gameObject.SetActive(p.host);
                if (p.host) row.badgeText.text = Loc.T("HÔTE", "HOST");
                bool known = p.car >= 0 && p.car < cars.Length;
                row.swatch.gameObject.SetActive(known);
                if (known)
                {
                    var spec = cars[p.car];
                    row.car.text = spec.displayName;
                    row.swatch.color = CarPaint.Shade(spec, p.paint);
                }
                else row.car.text = "…";
                row.status.text = Status(p, out var color);
                row.status.color = color;
            }

            footer.gameObject.SetActive(inMenu);
            if (inMenu)
                footer.text = TouchInput.Showing ? Loc.T("Touche le panneau : contrôles", "Tap the panel: controls")
                    : GameInput.UsingGamepad ? Loc.T("RB : contrôles", "RB: controls")
                    : Loc.T("Tab ou clic : contrôles  ·  en conduisant, Tab maintenu affiche ce tableau",
                            "Tab or click: controls  ·  while driving, hold Tab to show this list");
            float height = inMenu ? MenuHeight : HeaderHeight + players.Count * rowHeight + 22f;
            panel.anchoredPosition = inMenu ? MenuTop : HeldTop;
            panel.sizeDelta = new Vector2(Width, height);
        }

        /// <summary>Où en est le joueur : sa course à plusieurs s'il en court une, sinon sa distance à notre voiture.</summary>
        string Status(NetGame.PlayerInfo p, out Color color)
        {
            color = Dim;
            var race = game.Net.Race;
            var entry = race.Exists ? race.Find(p.id) : null;
            if (entry != null && (entry.InRace || entry.status == NetRaceStatus.Finished))
            {
                color = Accent;
                return entry.status == NetRaceStatus.Finished ? Loc.T("arrivé", "finished") : Loc.T("en course", "racing");
            }
            if (p.local || !p.remote || !p.remote.Ready || !game.Player) return "";
            float d = Vector3.Distance(p.remote.transform.position, game.Player.transform.position);
            return d < 1000f ? $"{Mathf.RoundToInt(d / 10f) * 10} m" : Loc.F($"{d / 1000f:0.0} km");
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            font = HudArt.Font;
            var go = new GameObject("PlayerList", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;   // au-dessus du HUD et de son menu
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var pgo = new GameObject("Panel", typeof(RectTransform));
            pgo.transform.SetParent(go.transform, false);
            panel = (RectTransform)pgo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = HeldTop;
            panel.sizeDelta = new Vector2(Width, HeaderHeight);
            group = pgo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;

            // Fond : la même plaque que les panneaux du menu ; un clic (menu seulement) rend la place aux contrôles
            var plate = Shape(panel, "Plate", new Color(0.03f, 0.04f, 0.06f, 0.92f));
            Stretch(plate.rectTransform);
            plate.Set(24f, 0f, 0f);
            plate.raycastTarget = true;
            // Sans graphisme cible : la teinte d'un bouton inactif (en conduisant) n'éclaircit pas la plaque
            toggle = pgo.AddComponent<Button>();
            toggle.transition = Selectable.Transition.None;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.onClick.AddListener(() => { if (game.Hud) game.Hud.ShowPlayersInMenu(false); });

            title = Label(panel, Loc.T("JOUEURS", "PLAYERS"), 34, TextAnchor.MiddleLeft, Accent, new Vector2(0f, 1f), new Vector2(50f, -22f), new Vector2(300f, 48f), FontStyle.BoldAndItalic);
            server = Label(panel, "", 22, TextAnchor.MiddleRight, Dim, new Vector2(1f, 1f), new Vector2(-50f, -26f), new Vector2(360f, 40f), FontStyle.Italic);
            summary = Label(panel, "", 20, TextAnchor.MiddleLeft, Info, new Vector2(0f, 1f), new Vector2(50f, -66f), new Vector2(Width - 100f, 28f), FontStyle.Bold);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            summary.resizeTextForBestFit = true;
            summary.resizeTextMinSize = 13;
            summary.resizeTextMaxSize = 20;
            // En-têtes de colonnes
            headName = Label(panel, "", 15, TextAnchor.MiddleLeft, Dim, new Vector2(0f, 1f), new Vector2(Inset + 46f, -98f), new Vector2(200f, 22f), FontStyle.Bold);
            headCar = Label(panel, "", 15, TextAnchor.MiddleLeft, Dim, new Vector2(0f, 1f), new Vector2(Inset + 352f, -98f), new Vector2(200f, 22f), FontStyle.Bold);
            footer = Label(panel, "", 18, TextAnchor.MiddleLeft, Dim, new Vector2(0f, 0f), new Vector2(50f, 12f), new Vector2(Width - 100f, 26f), FontStyle.Normal);
            footer.rectTransform.pivot = new Vector2(0f, 0f);
            footer.horizontalOverflow = HorizontalWrapMode.Wrap;
            footer.resizeTextForBestFit = true;
            footer.resizeTextMinSize = 12;
            footer.resizeTextMaxSize = 18;
        }

        /// <summary>Ligne d'un joueur : pastille de sa couleur, pseudo, badge de l'hôte, peinture et nom de la voiture, état.</summary>
        Row MakeRow()
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            var row = new Row { rt = rt };
            row.back = Shape(rt, "Back", Color.clear);
            Stretch(row.back.rectTransform);
            row.back.Set(12f, 0f, 0f);

            var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(rt, false);
            var drt = (RectTransform)dot.transform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 0.5f);
            drt.anchoredPosition = new Vector2(24f, 0f);
            drt.sizeDelta = new Vector2(16f, 16f);
            row.dot = dot.GetComponent<Image>();
            row.dot.sprite = HudArt.Disc;
            row.dot.raycastTarget = false;

            row.name = Label(rt, "", 23, TextAnchor.MiddleLeft, White, new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(214f, 30f), FontStyle.BoldAndItalic);
            FitWidth(row.name, 13, 23);

            row.badge = Shape(rt, "Host", Accent);
            var brt = row.badge.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = new Vector2(266f, 0f);
            brt.sizeDelta = new Vector2(70f, 26f);
            row.badge.Set(8f, 0f, 0f);
            row.badgeText = Label(brt, "", 15, TextAnchor.MiddleCenter, Ink, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 26f), FontStyle.Bold);
            row.badgeText.GetComponent<Shadow>().enabled = false;

            row.swatch = Shape(rt, "Paint", Color.white);
            var srt = row.swatch.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0.5f);
            srt.anchoredPosition = new Vector2(352f, 0f);
            srt.sizeDelta = new Vector2(20f, 20f);
            row.swatch.Set(6f, 0f, 0f);

            // Une seule ligne de haut : un nom de voiture trop long rétrécit au lieu de passer à la ligne
            row.car = Label(rt, "", 21, TextAnchor.MiddleLeft, White, new Vector2(0f, 0.5f), new Vector2(382f, 0f), new Vector2(200f, 26f), FontStyle.Normal);
            FitWidth(row.car, 12, 21);
            row.status = Label(rt, "", 19, TextAnchor.MiddleRight, Dim, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(96f, 30f), FontStyle.Italic);
            FitWidth(row.status, 12, 19);
            return row;
        }

        // ------------------------------------------------------------------ outils

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Texte sur une ligne qui rétrécit plutôt que de déborder sur la colonne suivante.</summary>
        static void FitWidth(Text t, int min, int max)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = min;
            t.resizeTextMaxSize = max;
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

        Text Label(Transform parent, string text, int size, TextAnchor align, Color color, Vector2 anchor, Vector2 pos, Vector2 box, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = color; t.text = text;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.raycastTarget = false;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.55f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }
    }
}
