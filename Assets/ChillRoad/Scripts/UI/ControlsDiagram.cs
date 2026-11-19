using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Dessin vectoriel en unités du canevas, origine au centre du rectangle : polygones convexes et anneaux aux bords
    /// adoucis, dessinés dans l'ordre où on les ajoute (manette et clavier du menu pause, traits de rappel).
    /// </summary>
    public class DeviceArt : MaskableGraphic
    {
        enum OpKind { Poly, Ring }
        struct Op { public OpKind kind; public Vector2[] pts; public Color color; public Vector2 center; public float radius, width; }
        readonly List<Op> ops = new List<Op>();
        const float Feather = 1.1f;

        public void Poly(Color c, params Vector2[] pts)
        {
            ops.Add(new Op { kind = OpKind.Poly, pts = pts, color = c });
            SetVerticesDirty();
        }

        public void Disc(Color c, Vector2 center, float r)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(r * 0.9f), 14, 48);
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            Poly(c, pts);
        }

        public void Ring(Color c, Vector2 center, float r, float width)
        {
            ops.Add(new Op { kind = OpKind.Ring, center = center, radius = r, width = width, color = c });
            SetVerticesDirty();
        }

        public void RoundRect(Color c, Vector2 center, Vector2 size, float radius)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(hx, hy));
            const int seg = 7;
            var pts = new List<Vector2>(4 * (seg + 1));
            Vector2[] corners =
            {
                new Vector2(hx - radius, hy - radius), new Vector2(-hx + radius, hy - radius),
                new Vector2(-hx + radius, -hy + radius), new Vector2(hx - radius, -hy + radius),
            };
            for (int k = 0; k < 4; k++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (k * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                    pts.Add(center + corners[k] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
            Poly(c, pts.ToArray());
        }

        /// <summary>Gélule entre deux centres (poignées de la manette).</summary>
        public void Capsule(Color c, Vector2 a, Vector2 b, float r)
        {
            Vector2 d = (b - a).normalized;
            float baseAng = Mathf.Atan2(d.y, d.x);
            const int seg = 14;
            var pts = new Vector2[2 * (seg + 1)];
            for (int i = 0; i <= seg; i++)
            {
                float t = baseAng - Mathf.PI * 0.5f + i * Mathf.PI / seg;
                pts[i] = b + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
                float u = baseAng + Mathf.PI * 0.5f + i * Mathf.PI / seg;
                pts[seg + 1 + i] = a + new Vector2(Mathf.Cos(u), Mathf.Sin(u)) * r;
            }
            Poly(c, pts);
        }

        public void Line(Color c, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.01f) return;
            d.Normalize();
            Vector2 t = new Vector2(-d.y, d.x) * width * 0.5f;
            Poly(c, a - t, b - t, b + t, a + t);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 o = rectTransform.rect.center;
            foreach (var op in ops)
                if (op.kind == OpKind.Poly) FillPoly(vh, op.pts, op.color, o);
                else FillRing(vh, op.center + o, op.radius, op.width, op.color);
        }

        static Color A(Color c, float k) { c.a *= k; return c; }

        static void FillPoly(VertexHelper vh, Vector2[] src, Color c, Vector2 o)
        {
            // Points confondus retirés (coins de rayon maximal), sens trigonométrique
            var p = new List<Vector2>(src.Length);
            foreach (var q in src)
                if (p.Count == 0 || (q + o - p[p.Count - 1]).sqrMagnitude > 0.0004f) p.Add(q + o);
            if (p.Count > 2 && (p[0] - p[p.Count - 1]).sqrMagnitude <= 0.0004f) p.RemoveAt(p.Count - 1);
            int n = p.Count;
            if (n < 3) return;
            float area = 0f;
            for (int i = 0; i < n; i++) { var a = p[i]; var b = p[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            if (area < 0f) p.Reverse();
            Vector2 center = Vector2.zero;
            foreach (var q in p) center += q;
            center /= n;
            int start = vh.currentVertCount;
            vh.AddVert(center, c, Vector4.zero);
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = p[(i - 1 + n) % n], cur = p[i], next = p[(i + 1) % n];
                Vector2 e1 = (cur - prev).normalized, e2 = (next - cur).normalized;
                Vector2 n1 = new Vector2(e1.y, -e1.x), n2 = new Vector2(e2.y, -e2.x);
                Vector2 m = (n1 + n2).normalized;
                float k = Mathf.Min(1f / Mathf.Max(Vector2.Dot(m, n1), 0.35f), 2.5f);
                vh.AddVert(cur, c, Vector4.zero);
                vh.AddVert(cur + m * Feather * k, A(c, 0f), Vector4.zero);
            }
            for (int i = 0; i < n; i++)
            {
                int a = start + 1 + i * 2, b = start + 1 + ((i + 1) % n) * 2;
                vh.AddTriangle(start, a, b);
                vh.AddTriangle(a, a + 1, b + 1);
                vh.AddTriangle(a, b + 1, b);
            }
        }

        static void FillRing(VertexHelper vh, Vector2 o, float radius, float width, Color c)
        {
            int segments = Mathf.Clamp(Mathf.RoundToInt(radius * 1.2f), 24, 72);
            float rIn = radius - width * 0.5f, rOut = radius + width * 0.5f;
            int start = vh.currentVertCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(o + d * (rIn - Feather), A(c, 0f), Vector4.zero);
                vh.AddVert(o + d * rIn, c, Vector4.zero);
                vh.AddVert(o + d * rOut, c, Vector4.zero);
                vh.AddVert(o + d * (rOut + Feather), A(c, 0f), Vector4.zero);
            }
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < 3; j++)
                {
                    int a = start + i * 4 + j, b = a + 4;
                    vh.AddTriangle(a, a + 1, b + 1);
                    vh.AddTriangle(a, b + 1, b);
                }
        }
    }

    /// <summary>
    /// Contrôles du menu pause dessinés sur l'appareil : une manette ou un clavier (et sa souris), les commandes en or,
    /// des étiquettes reliées à chaque bouton ou touche. Suit les commandes choisies dans les options (onglet Touches)
    /// et la disposition du clavier en cours (AZERTY, QWERTY…). Construit dans un rectangle dont le centre est l'origine.
    /// </summary>
    public static class ControlsDiagram
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Faint = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color Gold = new Color(0.95f, 0.62f, 0.12f);
        static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        static readonly Color Lead = new Color(1f, 0.76f, 0.2f, 0.85f);
        static readonly Color LabelPlate = new Color(0.12f, 0.135f, 0.17f, 0.97f);
        // Manette
        static readonly Color Shell = new Color(0.25f, 0.27f, 0.32f);
        static readonly Color ShellDark = new Color(0.17f, 0.185f, 0.22f);
        static readonly Color Well = new Color(0.1f, 0.11f, 0.135f);
        static readonly Color Knob = new Color(0.33f, 0.355f, 0.41f);
        // Clavier
        static readonly Color Chassis = new Color(0.085f, 0.095f, 0.12f);
        static readonly Color Cap = new Color(0.19f, 0.205f, 0.245f);

        const int Left = 0, Right = 1, Top = 2, Bottom = 3;

        class Callout
        {
            public string title, sub;
            public readonly List<(Vector2 c, float r)> anchors = new List<(Vector2, float)>();
            public int side, tier;
            public float key;          // position voulue : y (colonnes) ou x (rangées)
            public Vector2 pos, size;
            public RectTransform rt;
        }

        /// <summary>Commandes montrées (les joueurs : en réseau seulement).</summary>
        static IEnumerable<GameInput.Bind> Shown(bool online)
        {
            for (int b = 0; b < GameInput.BindCount; b++)
                if (online || b != (int)GameInput.Bind.Players) yield return (GameInput.Bind)b;
        }

        /// <summary>Nom court de l'action, pour une étiquette.</summary>
        static string Name(GameInput.Bind b)
        {
            switch (b)
            {
                case GameInput.Bind.Throttle: return Loc.T("Accélérer", "Throttle");
                case GameInput.Bind.Brake: return Loc.T("Freiner · reculer", "Brake · reverse");
                case GameInput.Bind.Left: return Loc.T("Tourner à gauche", "Steer left");
                case GameInput.Bind.Right: return Loc.T("Tourner à droite", "Steer right");
                case GameInput.Bind.Handbrake: return Loc.T("Frein à main", "Handbrake");
                case GameInput.Bind.LookBack: return Loc.T("Regarder derrière", "Look back");
                case GameInput.Bind.Camera: return Loc.T("Changer de vue", "Change camera");
                case GameInput.Bind.Reset: return Loc.T("Replacer", "Reset");
                case GameInput.Bind.Lights: return Loc.T("Phares", "Headlights");
                case GameInput.Bind.Flash: return Loc.T("Appel de phares", "Flash lights");
                case GameInput.Bind.Horn: return Loc.T("Klaxon", "Horn");
                case GameInput.Bind.Music: return Loc.T("Musique suivante", "Next song");
                case GameInput.Bind.Map: return Loc.T("Carte", "Map");
                default: return Loc.T("Joueurs", "Players");
            }
        }

        static string Note(GameInput.Bind b) =>
            b == GameInput.Bind.Reset ? Loc.T("maintenir : abandonner", "hold: abandon") : null;

        // ------------------------------------------------------------------ manette

        /// <summary>Manette (disposition Xbox) : gâchettes, tranches, joysticks, croix, A B X Y, Select / Start.</summary>
        public static void BuildPad(RectTransform root, bool online)
        {
            var art = NewArt(root);
            const float S = 0.94f;
            Vector2 O = new Vector2(0f, -36f);
            Vector2 P(float x, float y) => O + new Vector2(x, y) * S;

            var used = new Dictionary<string, GameInput.Bind>();
            foreach (var b in Shown(online))
                for (int s = 0; s < GameInput.Slots; s++)
                {
                    string path = GameInput.GetPad(b, s);
                    if (!string.IsNullOrEmpty(path)) used[path] = b;
                }
            bool Lit(string path) => used.ContainsKey(path);
            string N(string path) => used.TryGetValue(path, out var b) ? Name(b) : null;
            string Sub(string path) => used.TryGetValue(path, out var b) ? Note(b) : null;
            var texts = new List<(string s, Vector2 p, int size, Color c)>();

            // Gâchettes et tranches, derrière la coque
            foreach (int sx in new[] { -1, 1 })
            {
                string trig = sx < 0 ? "leftTrigger" : "rightTrigger", bump = sx < 0 ? "leftShoulder" : "rightShoulder";
                art.RoundRect(Lit(trig) ? Gold : ShellDark, P(112 * sx, 122), new Vector2(64, 34) * S, 12 * S);
                art.RoundRect(Lit(bump) ? Gold : Knob, P(112 * sx, 96), new Vector2(96, 24) * S, 11 * S);
                texts.Add((sx < 0 ? "LT" : "RT", P(112 * sx, 129), 12, Lit(trig) ? Ink : Dim));
                texts.Add((sx < 0 ? "LB" : "RB", P(112 * sx, 101), 11, Lit(bump) ? Ink : Dim));
            }

            // Coque : deux poignées, le bas et le corps
            art.Capsule(Shell, P(-122, 12), P(-140, -92), 48 * S);
            art.Capsule(Shell, P(122, 12), P(140, -92), 48 * S);
            art.Poly(Shell, P(-125, -20), P(125, -20), P(84, -62), P(-84, -62));
            art.RoundRect(Shell, P(0, 32), new Vector2(330, 116) * S, 50 * S);

            // Joysticks (toujours utilisés : diriger, regarder autour)
            foreach (var c in new[] { P(-95, 40), P(52, -22) })
            {
                art.Disc(Well, c, 31 * S);
                art.Ring(Gold, c, 31 * S, 2.6f);
                art.Disc(Knob, c, 21 * S);
                art.Ring(ShellDark, c, 14 * S, 1.6f);
            }

            // Croix
            Vector2 dp = P(-52, -22);
            art.Disc(Well, dp, 32 * S);
            bool dpadLit = Lit("dpad/up") || Lit("dpad/down") || Lit("dpad/left") || Lit("dpad/right");
            if (dpadLit) art.Ring(Gold, dp, 32 * S, 2.6f);
            art.RoundRect(Knob, dp, new Vector2(52, 17) * S, 4 * S);
            art.RoundRect(Knob, dp, new Vector2(17, 52) * S, 4 * S);
            foreach (var (dir, v) in new[] { ("up", Vector2.up), ("down", Vector2.down), ("left", Vector2.left), ("right", Vector2.right) })
            {
                Vector2 t = new Vector2(-v.y, v.x);
                Vector2 tip = dp + v * 22f * S, b = dp + v * 14f * S;
                art.Poly(Lit("dpad/" + dir) ? Gold : ShellDark, tip, b + t * 5f * S, b - t * 5f * S);
            }

            // A B X Y : lettres aux couleurs habituelles
            var face = new[]
            {
                ("buttonNorth", "Y", P(98, 62), new Color(1f, 0.82f, 0.25f)),
                ("buttonEast", "B", P(120, 40), new Color(1f, 0.4f, 0.34f)),
                ("buttonWest", "X", P(76, 40), new Color(0.38f, 0.62f, 1f)),
                ("buttonSouth", "A", P(98, 18), new Color(0.5f, 0.88f, 0.38f)),
            };
            foreach (var (path, letter, c, tint) in face)
            {
                if (Lit(path)) art.Disc(Gold, c, 15.5f * S);
                art.Disc(Well, c, 12.5f * S);
                texts.Add((letter, c, 14, tint));
            }

            // Select, Start (pause, toujours), bouton central
            Vector2 sel = P(-30, 50), start = P(30, 50), guide = P(0, 76);
            if (Lit("select")) art.RoundRect(Gold, sel, new Vector2(25, 16) * S, 7 * S);
            art.RoundRect(Well, sel, new Vector2(20, 12) * S, 6 * S);
            art.RoundRect(Gold, start, new Vector2(25, 16) * S, 7 * S);
            art.RoundRect(Well, start, new Vector2(20, 12) * S, 6 * S);
            art.Disc(ShellDark, guide, 13 * S);
            art.Ring(Knob, guide, 9 * S, 1.6f);

            foreach (var (s, p, size, c) in texts)
                Txt(root, s, size, TextAnchor.MiddleCenter, c, FontStyle.Bold, p, new Vector2(40, 24));

            // Étiquettes
            var list = new List<Callout>();
            void Add(int side, string title, string sub, Vector2 c, float r)
            {
                if (title == null) return;
                var co = new Callout { side = side, title = title, sub = sub };
                co.anchors.Add((c, r));
                list.Add(co);
            }
            Add(Left, N("leftTrigger"), Sub("leftTrigger"), P(-144, 122), 0f);
            Add(Right, N("rightTrigger"), Sub("rightTrigger"), P(144, 122), 0f);
            Add(Left, N("leftShoulder"), Sub("leftShoulder"), P(-160, 96), 0f);
            Add(Right, N("rightShoulder"), Sub("rightShoulder"), P(160, 96), 0f);
            string ls = N("leftStickPress"), rs = N("rightStickPress");
            Add(Left, Loc.T("Diriger", "Steer"), ls != null ? Loc.T("clic : ", "click: ") + ls : null, P(-95, 40), 31 * S);
            Add(Right, Loc.T("Regarder autour", "Look around"), rs != null ? Loc.T("clic : ", "click: ") + rs : null, P(52, -22), 31 * S);
            foreach (var (path, _, c, _) in face) Add(Right, N(path), Sub(path), c, 12.5f * S);
            Add(Top, N("select"), Sub("select"), sel + new Vector2(0f, 6f * S), 0f);
            Add(Top, Loc.T("Pause", "Pause"), null, start + new Vector2(0f, 6f * S), 0f);

            // Croix : une ligne par direction (gauche et droite réunies quand elles dirigent)
            var lines = new List<string>();
            used.TryGetValue("dpad/left", out var dl);
            used.TryGetValue("dpad/right", out var dr);
            bool steer = Lit("dpad/left") && Lit("dpad/right") && dl == GameInput.Bind.Left && dr == GameInput.Bind.Right;
            if (Lit("dpad/up")) lines.Add("↑  " + N("dpad/up"));
            if (Lit("dpad/down")) lines.Add("↓  " + N("dpad/down"));
            if (steer) lines.Add("←  →  " + Loc.T("Diriger", "Steer"));
            else
            {
                if (Lit("dpad/left")) lines.Add("←  " + N("dpad/left"));
                if (Lit("dpad/right")) lines.Add("→  " + N("dpad/right"));
            }
            if (lines.Count > 0) Add(Left, string.Join("\n", lines), null, dp, 32 * S);

            // Commandes sans bouton dessiné (non attribuées, ou bouton propre à une autre manette)
            var known = new HashSet<string> { "leftTrigger", "rightTrigger", "leftShoulder", "rightShoulder", "leftStickPress", "rightStickPress",
                "dpad/up", "dpad/down", "dpad/left", "dpad/right", "buttonNorth", "buttonEast", "buttonWest", "buttonSouth", "select" };
            var foot = new List<string>();
            foreach (var b in Shown(online))
            {
                var names = new List<string>();
                bool drawn = false;
                for (int s = 0; s < GameInput.Slots; s++)
                {
                    string path = GameInput.GetPad(b, s);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (known.Contains(path)) drawn = true;
                    else names.Add(GameInput.PadName(path));
                }
                if (names.Count > 0) foot.Add(Name(b) + " : " + string.Join(" / ", names));
                else if (!drawn) foot.Add(b == GameInput.Bind.Map ? Loc.T("Carte : tuile du menu pause", "Map: pause menu tile") : Name(b) + " : —");
            }

            foreach (var c in list) MakeLabel(root, c);
            Column(list, Left, -188f, -268f, 292f);
            Column(list, Right, 188f, -268f, 292f);
            Row(list, Top, P(0, 152).y, -176f, 176f);
            Leaders(art, list);
            Footer(root, foot);
        }

        // ------------------------------------------------------------------ clavier

        struct KeyCap { public Key key; public float x, y, w; }

        /// <summary>Clavier sans pavé numérique (positions américaines, comme <see cref="Key"/>) : x et y en largeurs de touche.</summary>
        static List<KeyCap> Layout()
        {
            var caps = new List<KeyCap>();
            void R(float y, float x, params (Key k, float w)[] keys)
            {
                foreach (var (k, w) in keys)
                {
                    if (k != Key.None) caps.Add(new KeyCap { key = k, x = x, y = y, w = w });
                    x += w;
                }
            }
            const float g = 0.4f;
            R(0f, 0f, (Key.Escape, 1f), (Key.None, 1f), (Key.F1, 1f), (Key.F2, 1f), (Key.F3, 1f), (Key.F4, 1f), (Key.None, 0.5f),
                (Key.F5, 1f), (Key.F6, 1f), (Key.F7, 1f), (Key.F8, 1f), (Key.None, 0.5f), (Key.F9, 1f), (Key.F10, 1f), (Key.F11, 1f), (Key.F12, 1f),
                (Key.None, g), (Key.PrintScreen, 1f), (Key.ScrollLock, 1f), (Key.Pause, 1f));
            R(1f + g, 0f, (Key.Backquote, 1f), (Key.Digit1, 1f), (Key.Digit2, 1f), (Key.Digit3, 1f), (Key.Digit4, 1f), (Key.Digit5, 1f),
                (Key.Digit6, 1f), (Key.Digit7, 1f), (Key.Digit8, 1f), (Key.Digit9, 1f), (Key.Digit0, 1f), (Key.Minus, 1f), (Key.Equals, 1f),
                (Key.Backspace, 2f), (Key.None, g), (Key.Insert, 1f), (Key.Home, 1f), (Key.PageUp, 1f));
            R(2f + g, 0f, (Key.Tab, 1.5f), (Key.Q, 1f), (Key.W, 1f), (Key.E, 1f), (Key.R, 1f), (Key.T, 1f), (Key.Y, 1f), (Key.U, 1f),
                (Key.I, 1f), (Key.O, 1f), (Key.P, 1f), (Key.LeftBracket, 1f), (Key.RightBracket, 1f), (Key.Backslash, 1.5f),
                (Key.None, g), (Key.Delete, 1f), (Key.End, 1f), (Key.PageDown, 1f));
            R(3f + g, 0f, (Key.CapsLock, 1.75f), (Key.A, 1f), (Key.S, 1f), (Key.D, 1f), (Key.F, 1f), (Key.G, 1f), (Key.H, 1f), (Key.J, 1f),
                (Key.K, 1f), (Key.L, 1f), (Key.Semicolon, 1f), (Key.Quote, 1f), (Key.Enter, 2.25f));
            R(4f + g, 0f, (Key.LeftShift, 2.25f), (Key.Z, 1f), (Key.X, 1f), (Key.C, 1f), (Key.V, 1f), (Key.B, 1f), (Key.N, 1f), (Key.M, 1f),
                (Key.Comma, 1f), (Key.Period, 1f), (Key.Slash, 1f), (Key.RightShift, 2.75f), (Key.None, g + 1f), (Key.UpArrow, 1f));
            R(5f + g, 0f, (Key.LeftCtrl, 1.25f), (Key.LeftMeta, 1.25f), (Key.LeftAlt, 1.25f), (Key.Space, 6.25f), (Key.RightAlt, 1.25f),
                (Key.RightMeta, 1.25f), (Key.ContextMenu, 1.25f), (Key.RightCtrl, 1.25f), (Key.None, g),
                (Key.LeftArrow, 1f), (Key.DownArrow, 1f), (Key.RightArrow, 1f));
            return caps;
        }

        /// <summary>Inscription de la touche : celle de la disposition en cours pour les lettres et signes, abrégée pour les autres.</summary>
        static string Legend(Key k)
        {
            switch (k)
            {
                case Key.Escape: return Loc.T("Échap", "Esc");
                case Key.Tab: return "Tab";
                case Key.CapsLock: return Loc.T("Verr.", "Caps");
                case Key.LeftShift: case Key.RightShift: return Loc.T("Maj", "Shift");
                case Key.LeftCtrl: case Key.RightCtrl: return "Ctrl";
                case Key.LeftMeta: case Key.RightMeta: return "Win";
                case Key.LeftAlt: return "Alt";
                case Key.RightAlt: return "Alt Gr";
                case Key.ContextMenu: return "Menu";
                case Key.Space: return "";
                case Key.Enter: return Loc.T("Entrée", "Enter");
                case Key.Backspace: return Loc.T("Retour", "Back");
                case Key.Insert: return Loc.T("Inser", "Ins");
                case Key.Home: return Loc.T("Début", "Home");
                case Key.PageUp: return Loc.T("Pg ↑", "PgUp");
                case Key.PageDown: return Loc.T("Pg ↓", "PgDn");
                case Key.Delete: return Loc.T("Suppr", "Del");
                case Key.End: return Loc.T("Fin", "End");
                case Key.PrintScreen: return Loc.T("Impr", "PrtSc");
                case Key.ScrollLock: return Loc.T("Défil", "ScrLk");
                case Key.Pause: return "Pause";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
            }
            if (k >= Key.F1 && k <= Key.F12) return "F" + (k - Key.F1 + 1);
            if (Keyboard.current != null) return Short(GameInput.KeyName(k));
            if (k >= Key.Digit1 && k <= Key.Digit9) return ((int)(k - Key.Digit1) + 1).ToString();
            switch (k)
            {
                case Key.Digit0: return "0";
                case Key.Backquote: return "`";
                case Key.Minus: return "-";
                case Key.Equals: return "=";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Backslash: return "\\";
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Slash: return "/";
            }
            return k.ToString();
        }

        /// <summary>Touches mortes nommées en toutes lettres (« Accent Circonflexe ») : leur signe, ou les premières lettres.</summary>
        static string Short(string name)
        {
            if (name.Length <= 3) return name;
            string n = name.ToLowerInvariant();
            if (n.Contains("circonflexe") || n.Contains("circumflex") || n.Contains("caret")) return "^";
            if (n.Contains("grave")) return "`";
            if (n.Contains("aigu") || n.Contains("acute")) return "´";
            if (n.Contains("tilde")) return "~";
            if (n.Contains("tréma") || n.Contains("trema") || n.Contains("diaeresis") || n.Contains("umlaut")) return "¨";
            return name.Substring(0, 3);
        }

        /// <summary>Clavier et souris : touches des commandes en or, étiquettes au-dessus et au-dessous.</summary>
        public static void BuildKeyboard(RectTransform root, bool online)
        {
            var art = NewArt(root);
            const float u = 34f, W = 18.4f, H = 6.4f;
            Vector2 C = new Vector2(0f, -4f);
            Vector2 K(float x, float y) => C + new Vector2((x - W * 0.5f) * u, (H * 0.5f - y) * u);

            var caps = Layout();
            var at = new Dictionary<Key, KeyCap>();
            foreach (var cap in caps) at[cap.key] = cap;
            Vector2 Center(KeyCap c) => K(c.x + c.w * 0.5f, c.y + 0.5f);

            // Groupes d'étiquettes : une action (diriger : gauche et droite), la pause (touches fixes)
            var groups = new List<(string title, string note, List<(Key k, string name)> keys)>();
            List<(Key, string)> KeysOf(params GameInput.Bind[] binds)
            {
                var l = new List<(Key, string)>();
                foreach (var b in binds)
                    for (int s = 0; s < GameInput.Slots; s++)
                    {
                        var k = GameInput.GetKey(b, s);
                        if (k != Key.None) l.Add((k, GameInput.KeyName(k)));
                    }
                return l;
            }
            foreach (var b in Shown(online))
            {
                if (b == GameInput.Bind.Right) continue;
                if (b == GameInput.Bind.Left) groups.Add((Loc.T("Diriger", "Steer"), null, KeysOf(GameInput.Bind.Left, GameInput.Bind.Right)));
                else groups.Add((Name(b), Note(b), KeysOf(b)));
            }
            groups.Add((Loc.T("Pause", "Pause"), null, new List<(Key, string)> { (Key.Escape, Legend(Key.Escape)), (Key.P, GameInput.KeyName(Key.P)) }));

            var lit = new HashSet<Key>();
            foreach (var g in groups) foreach (var (k, _) in g.keys) lit.Add(k);

            // Châssis, touches, inscriptions
            art.RoundRect(Chassis, K(W * 0.5f, H * 0.5f), new Vector2(W + 0.5f, H + 0.5f) * u, 0.35f * u);
            foreach (var cap in caps)
            {
                bool on = lit.Contains(cap.key);
                art.RoundRect(on ? Gold : Cap, Center(cap), new Vector2(cap.w - 0.12f, 0.88f) * u, 0.14f * u);
            }
            foreach (var cap in caps)
            {
                string s = Legend(cap.key);
                if (s.Length == 0) continue;
                bool on = lit.Contains(cap.key);
                int size = s.Length <= 2 ? 15 : s.Length <= 3 ? 12 : 11;
                if (cap.key >= Key.F1 && cap.key <= Key.F12) size = 11;
                Txt(root, s, size, TextAnchor.MiddleCenter, on ? Ink : Faint, FontStyle.Bold, Center(cap), new Vector2(cap.w * u, u));
            }

            // Souris, au-dessus des flèches : clic droit maintenu pour regarder autour
            float kbTop = K(0f, -0.25f).y, kbBottom = K(0f, H + 0.25f).y;
            Vector2 ms = new Vector2(302f, kbTop + 72f);
            const float mw = 37f, mh = 56f, mr = 34f;
            art.RoundRect(ShellDark, ms, new Vector2(mw * 2f, mh * 2f), mr);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var pts = new List<Vector2>();
                Vector2 corner = ms + new Vector2((mw - mr) * sx, mh - mr);
                for (int i = 0; i <= 8; i++)
                {
                    float a = (sx > 0 ? 90f - i * 90f / 8f : 90f + i * 90f / 8f) * Mathf.Deg2Rad;
                    pts.Add(corner + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * mr);
                }
                pts.Add(ms + new Vector2(mw * sx, 6f));
                pts.Add(ms + new Vector2(2.5f * sx, 6f));
                pts.Add(ms + new Vector2(2.5f * sx, mh));
                art.Poly(sx > 0 ? Gold : Cap, pts.ToArray());
            }
            art.RoundRect(Chassis, ms + new Vector2(0f, 30f), new Vector2(9f, 22f), 4.5f);

            // Étiquettes : touches voisines d'une même action reliées ensemble, les autres citées (« aussi ↑ »)
            var list = new List<Callout>();
            var foot = new List<string>();
            foreach (var (title, note, keys) in groups)
            {
                var drawn = new List<(Key k, Vector2 p)>();
                var others = new List<string>();
                foreach (var (k, name) in keys)
                    if (at.TryGetValue(k, out var cap)) drawn.Add((k, Center(cap)));
                    else others.Add(name);
                if (drawn.Count == 0)
                {
                    foot.Add(title + " : " + (others.Count > 0 ? string.Join(" / ", others) : "—"));
                    continue;
                }
                var linked = new List<(Key k, Vector2 p)> { drawn[0] };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var d in drawn)
                    {
                        if (linked.Contains(d)) continue;
                        foreach (var l in linked)
                            if ((d.p - l.p).magnitude <= 2.2f * u) { linked.Add(d); grew = true; break; }
                    }
                }
                foreach (var d in drawn) if (!linked.Contains(d)) others.Add(GameInput.KeyName(d.k));
                string also = others.Count > 0 ? Loc.T("aussi ", "also ") + "<color=#FFC233>" + string.Join("  ", others) + "</color>" : null;
                string sub = note != null && also != null ? note + "  ·  " + also : note ?? also;
                var co = new Callout { title = title, sub = sub };
                float row = 0f;
                foreach (var l in linked)
                {
                    var cap = at[l.k];
                    co.anchors.Add((l.p, -(cap.w * 0.5f - 0.3f) * u));   // r < 0 : touche large, le trait la rejoint au plus court
                    row += cap.y;
                }
                row /= linked.Count;
                co.side = row < 2.5f ? Top : row > 4f ? Bottom : -1;
                list.Add(co);
            }
            // Rangée du milieu : du côté où aucune étiquette ne vise une touche voisine, sinon du moins chargé
            float AnchorX(Callout c) { float x = 0f; foreach (var an in c.anchors) x += an.c.x; return x / c.anchors.Count; }
            var middle = list.FindAll(c => c.side < 0);
            middle.Sort((p, q) => AnchorX(p).CompareTo(AnchorX(q)));
            foreach (var co in middle)
            {
                float x = AnchorX(co);
                int Score(int side)
                {
                    int n = 0;
                    foreach (var o in list)
                        if (o.side == side) n += Mathf.Abs(AnchorX(o) - x) < 1.6f * u ? 10 : 1;
                    return n;
                }
                co.side = Score(Top) < Score(Bottom) ? Top : Bottom;
            }

            // Point d'attache sur la touche : bord haut ou bas, selon le côté de l'étiquette
            foreach (var co in list)
                for (int i = 0; i < co.anchors.Count; i++)
                    co.anchors[i] = (co.anchors[i].c + new Vector2(0f, (co.side == Top ? 0.36f : -0.36f) * u), co.anchors[i].r);

            foreach (var c in list) MakeLabel(root, c);

            // Souris et son étiquette, à sa gauche : obstacles pour les autres étiquettes du haut
            var look = new Callout { title = Loc.T("Regarder autour", "Look around"), sub = Loc.T("clic droit maintenu", "hold right click"), side = Left };
            look.anchors.Add((ms + new Vector2(6f, mh - 12f), 0f));
            MakeLabel(root, look);
            look.pos = new Vector2(ms.x - mw - 10f - look.size.x * 0.5f, ms.y + 22f);
            look.rt.anchoredPosition = look.pos;
            var obstacles = new List<Rect> { Box(ms, new Vector2(mw * 2f, mh * 2f), 6f), Box(look.pos, look.size, 6f) };
            var segments = new List<(Vector2, Vector2)> { (look.pos + new Vector2(look.size.x * 0.5f, 0f), look.anchors[0].c) };
            Place(list, Top, kbTop, 1f, -338f, 338f, obstacles, segments);
            Place(list, Bottom, kbBottom, -1f, -338f, 338f, new List<Rect>(), new List<(Vector2, Vector2)>());
            list.Add(look);
            Leaders(art, list);
            Footer(root, foot);
        }

        static Rect Box(Vector2 center, Vector2 size, float margin) =>
            new Rect(center - size * 0.5f - Vector2.one * margin, size + Vector2.one * margin * 2f);

        /// <summary>
        /// Étiquettes d'un côté du clavier, sur trois rangs au plus : chacune au plus près au-dessus (ou au-dessous) de ses
        /// touches, sans chevaucher une autre étiquette, sans qu'un trait de rappel passe sous une étiquette.
        /// </summary>
        static void Place(List<Callout> list, int side, float edge, float dir, float min, float max, List<Rect> placed, List<(Vector2 a, Vector2 b)> lines)
        {
            var mine = list.FindAll(c => c.side == side);
            foreach (var c in mine)
            {
                float x = 0f;
                foreach (var a in c.anchors) x += a.c.x;
                c.key = x / c.anchors.Count;
            }
            mine.Sort((a, b) => a.key.CompareTo(b.key));
            var offsets = new List<float> { 0f };
            for (float o = 12f; o <= 360f; o += 12f) { offsets.Add(o); offsets.Add(-o); }
            foreach (var c in mine)
            {
                Vector2 half = c.size * 0.5f;
                // Place la moins coûteuse : chevauchement exclu autant que possible, puis trait sous une étiquette,
                // traits croisés, rang éloigné, décalage
                float bestCost = float.MaxValue;
                Vector2 best = new Vector2(Mathf.Clamp(c.key, min + half.x, max - half.x), edge + dir * 40f);
                for (int tier = 0; tier < 3; tier++)
                {
                    float y = edge + dir * (40f + 50f * tier);
                    foreach (float off in offsets)
                    {
                        float x = c.key + off;
                        if (x - half.x < min || x + half.x > max) continue;
                        float cost = tier * 14f + Mathf.Abs(off) * 1f;
                        if (cost >= bestCost) continue;
                        var pos = new Vector2(x, y);
                        var r = Box(pos, c.size, 5f);
                        foreach (var q in placed) if (q.Overlaps(r)) cost += 5000f;
                        foreach (var (a, b) in lines) if (Hits(a, b, r)) cost += 400f;
                        Vector2 from = pos - new Vector2(0f, dir * half.y);
                        foreach (var an in c.anchors)
                        {
                            Vector2 to = End(from, an);
                            cost += (to - from).magnitude * 0.15f;
                            foreach (var q in placed) if (Hits(from, to, q)) cost += 400f;
                            foreach (var (a, b) in lines) if (Crosses(from, to, a, b)) cost += 150f;
                        }
                        if (cost < bestCost) { bestCost = cost; best = pos; }
                    }
                }
                c.pos = best;
                c.rt.anchoredPosition = best;
                placed.Add(Box(best, c.size, 5f));
                Vector2 start = best - new Vector2(0f, dir * half.y);
                foreach (var an in c.anchors) lines.Add((start, End(start, an)));
            }
        }

        /// <summary>Bout du trait de rappel : pourtour d'un bouton rond (r > 0), point le plus proche sur une touche large (r < 0).</summary>
        static Vector2 End(Vector2 from, (Vector2 c, float r) an)
        {
            if (an.r < 0f) return new Vector2(Mathf.Clamp(from.x, an.c.x + an.r, an.c.x - an.r), an.c.y);
            if (an.r > 0f && (from - an.c).sqrMagnitude > an.r * an.r) return an.c + (from - an.c).normalized * an.r;
            return an.c;
        }

        /// <summary>Deux segments se croisent-ils (hors extrémités communes) ?</summary>
        static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return d1 * d2 < -0.01f && d3 * d4 < -0.01f;
        }

        /// <summary>Le segment ab traverse-t-il le rectangle ? (découpage de Liang-Barsky)</summary>
        static bool Hits(Vector2 a, Vector2 b, Rect r)
        {
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            float[] p = { -d.x, d.x, -d.y, d.y };
            float[] q = { a.x - r.xMin, r.xMax - a.x, a.y - r.yMin, r.yMax - a.y };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-5f) { if (q[i] < 0f) return false; continue; }
                float t = q[i] / p[i];
                if (p[i] < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return t0 <= t1;
        }

        // ------------------------------------------------------------------ étiquettes

        static DeviceArt NewArt(RectTransform root)
        {
            var go = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer), typeof(DeviceArt));
            go.transform.SetParent(root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var art = go.GetComponent<DeviceArt>();
            art.raycastTarget = false;
            return art;
        }

        static Text Txt(Transform parent, string s, int size, TextAnchor align, Color color, FontStyle style, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = HudArt.Font; t.fontSize = size; t.alignment = align; t.color = color; t.fontStyle = style; Glass.Adopt(t, style);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = s;
            return t;
        }

        /// <summary>Plaque inclinée avec le nom de l'action (et sa précision dessous), mesurée à son texte.</summary>
        static void MakeLabel(RectTransform root, Callout c)
        {
            var go = new GameObject("Callout", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape));
            go.transform.SetParent(root, false);
            var plate = go.GetComponent<HudShape>();
            plate.Set(10f, 0f, 0f);
            plate.color = LabelPlate;
            plate.raycastTarget = false;
            c.rt = (RectTransform)go.transform;
            c.rt.anchorMin = c.rt.anchorMax = c.rt.pivot = new Vector2(0.5f, 0.5f);
            var align = c.side == Left ? TextAnchor.MiddleRight : c.side == Right ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            string s = c.sub == null ? c.title : c.title + "\n<size=13><color=#FFFFFF8C>" + c.sub + "</color></size>";
            var t = Txt(c.rt, s, 16, align, White, FontStyle.Bold, Vector2.zero, new Vector2(10f, 10f));
            t.lineSpacing = 0.95f;
            float w = Mathf.Ceil(t.preferredWidth), h = Mathf.Ceil(t.preferredHeight);
            c.size = new Vector2(w + 22f, h + 10f);
            c.rt.sizeDelta = c.size;
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(11f, 4f);
            t.rectTransform.offsetMax = new Vector2(-11f, -5f);
        }

        /// <summary>Colonne d'étiquettes (gauche ou droite de la manette), bord intérieur aligné sur <paramref name="edge"/>.</summary>
        static void Column(List<Callout> list, int side, float edge, float min, float max)
        {
            var mine = list.FindAll(c => c.side == side);
            // À hauteur égale, le bouton le plus loin de la colonne passe dessous (X sous B) : son trait longe le bas de B
            foreach (var c in mine) c.key = c.anchors[0].c.y - Mathf.Abs(c.anchors[0].c.x - edge) * 0.05f;
            Spread(mine, true, min, max, 8f);
            foreach (var c in mine)
            {
                float x = side == Left ? edge - c.size.x * 0.5f : edge + c.size.x * 0.5f;
                c.pos = new Vector2(x, c.pos.y);
                c.rt.anchoredPosition = c.pos;
            }
        }

        /// <summary>Rangée d'étiquettes à hauteur fixe (au-dessus de la manette).</summary>
        static void Row(List<Callout> list, int side, float y, float min, float max)
        {
            var mine = list.FindAll(c => c.side == side);
            foreach (var c in mine) c.key = c.anchors[0].c.x;
            Spread(mine, false, min, max, 12f);
            foreach (var c in mine)
            {
                c.pos = new Vector2(c.pos.x, y);
                c.rt.anchoredPosition = c.pos;
            }
        }

        /// <summary>Répartit des étiquettes sur un axe près de leur position voulue, sans chevauchement, entre deux bornes.</summary>
        static void Spread(List<Callout> mine, bool vertical, float min, float max, float gap)
        {
            mine.Sort((a, b) => a.key.CompareTo(b.key));
            int n = mine.Count;
            if (n == 0) return;
            var p = new float[n];
            var h = new float[n];
            for (int i = 0; i < n; i++)
            {
                p[i] = mine[i].key;
                h[i] = (vertical ? mine[i].size.y : mine[i].size.x) * 0.5f;
            }
            for (int it = 0; it < 300; it++)
            {
                bool moved = false;
                for (int i = 0; i + 1 < n; i++)
                {
                    float need = h[i] + h[i + 1] + gap - (p[i + 1] - p[i]);
                    if (need > 0.01f) { p[i] -= need * 0.5f; p[i + 1] += need * 0.5f; moved = true; }
                }
                for (int i = 0; i < n; i++) p[i] = Mathf.Clamp(p[i], min + h[i], Mathf.Max(min + h[i], max - h[i]));
                if (!moved) break;
            }
            for (int i = 0; i < n; i++)
                mine[i].pos = vertical ? new Vector2(mine[i].pos.x, p[i]) : new Vector2(p[i], mine[i].pos.y);
        }

        /// <summary>Traits de rappel : du bord de l'étiquette au bouton (son pourtour) ou à la touche, point doré au bout.</summary>
        static void Leaders(DeviceArt art, List<Callout> list)
        {
            foreach (var c in list)
            {
                Vector2 half = c.size * 0.5f;
                Vector2 from = c.side == Left ? c.pos + new Vector2(half.x, 0f) : c.side == Right ? c.pos - new Vector2(half.x, 0f)
                    : c.side == Top ? c.pos - new Vector2(0f, half.y) : c.pos + new Vector2(0f, half.y);
                foreach (var an in c.anchors)
                {
                    Vector2 end = End(from, an);
                    art.Line(Lead, from, end, 2f);
                    art.Disc(Ink, end, 4.4f);    // bord sombre : le point se voit aussi sur une touche dorée
                    art.Disc(Gold, end, 2.8f);
                }
            }
        }

        static void Footer(RectTransform root, List<string> items)
        {
            if (items.Count == 0) return;
            var t = Txt(root, string.Join("     ", items), 15, TextAnchor.MiddleCenter, Dim, FontStyle.Bold, new Vector2(0f, -306f), new Vector2(640f, 30f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
