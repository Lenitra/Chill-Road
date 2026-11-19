using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Pictogramme vectoriel des menus (tuiles du menu pause, garage) : formes pleines aux bords adoucis, dessinées dans
    /// un carré centré sur le rectangle. Aucune police ni image : nets à toute taille et sur toutes les plateformes.
    /// Les évidements (vitres, passages de roue, trou d'engrenage, porte) prennent la couleur <see cref="cut"/>, celle
    /// du fond de la tuile.
    /// </summary>
    public class HudIcon : MaskableGraphic
    {
        public enum Kind { Play, Car, Clock, Gear, Home, Power, Invite, Back, Map, Trophy, Road, Gauge, Ramp, Flag, Coin, Lock, Radar, Check, RadarPin, RampPin }

        Kind kind;
        Color cut = Color.black;

        public Kind Shape
        {
            get => kind;
            set { if (kind != value) { kind = value; SetVerticesDirty(); } }
        }

        /// <summary>Couleurs du trait et des évidements (fond de la tuile).</summary>
        public void SetColors(Color fg, Color bg)
        {
            if (color != fg) color = fg;
            if (cut != bg) { cut = bg; SetVerticesDirty(); }
        }

        VertexHelper vh;
        Rect box;
        const float Feather = 1.3f;   // largeur du bord adouci (unités du canevas)

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            vh = v;
            Rect r = GetPixelAdjustedRect();
            float s = Mathf.Min(r.width, r.height);
            box = new Rect(r.center.x - s * 0.5f, r.center.y - s * 0.5f, s, s);
            Color fg = color, bg = cut;
            var c = new Vector2(0.5f, 0.5f);
            switch (kind)
            {
                case Kind.Play:
                    Poly(fg, new Vector2(0.3f, 0.17f), new Vector2(0.86f, 0.5f), new Vector2(0.3f, 0.83f));
                    break;

                case Kind.Car:
                    // Caisse (capot plongeant à droite), pavillon, vitres, passages de roue puis roues
                    Poly(fg, new Vector2(0.04f, 0.27f), new Vector2(0.96f, 0.27f), new Vector2(0.96f, 0.43f), new Vector2(0.87f, 0.52f), new Vector2(0.04f, 0.53f));
                    Poly(fg, new Vector2(0.2f, 0.5f), new Vector2(0.78f, 0.5f), new Vector2(0.63f, 0.75f), new Vector2(0.33f, 0.75f));
                    Poly(bg, new Vector2(0.29f, 0.545f), new Vector2(0.475f, 0.545f), new Vector2(0.475f, 0.7f), new Vector2(0.355f, 0.7f));
                    Poly(bg, new Vector2(0.525f, 0.545f), new Vector2(0.705f, 0.545f), new Vector2(0.61f, 0.7f), new Vector2(0.525f, 0.7f));
                    foreach (float x in new[] { 0.26f, 0.74f })
                    {
                        Disc(bg, new Vector2(x, 0.27f), 0.155f);
                        Disc(fg, new Vector2(x, 0.27f), 0.11f);
                        Disc(bg, new Vector2(x, 0.27f), 0.04f);
                    }
                    break;

                case Kind.Clock:
                    Arc(fg, c, 0.37f, 0.085f, 0f, 360f);
                    for (int k = 0; k < 4; k++) Disc(fg, c + Dir(k * 90f) * 0.25f, 0.028f);
                    Line(fg, c, new Vector2(0.5f, 0.74f), 0.075f);
                    Line(fg, c, c + Dir(115f) * 0.19f, 0.075f);
                    Disc(fg, c, 0.065f);
                    break;

                case Kind.Gear:
                    Disc(fg, c, 0.3f);
                    for (int k = 0; k < 8; k++)
                    {
                        Vector2 d = Dir(k * 45f), t = new Vector2(d.y, -d.x);
                        Poly(fg, c + d * 0.24f - t * 0.085f, c + d * 0.43f - t * 0.06f, c + d * 0.43f + t * 0.06f, c + d * 0.24f + t * 0.085f);
                    }
                    Disc(bg, c, 0.13f);
                    break;

                case Kind.Home:
                    Poly(fg, new Vector2(0.06f, 0.5f), new Vector2(0.5f, 0.89f), new Vector2(0.94f, 0.5f));
                    Poly(fg, new Vector2(0.68f, 0.68f), new Vector2(0.68f, 0.86f), new Vector2(0.79f, 0.86f), new Vector2(0.79f, 0.6f));
                    Poly(fg, new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.12f), new Vector2(0.82f, 0.56f), new Vector2(0.18f, 0.56f));
                    Poly(bg, new Vector2(0.415f, 0.12f), new Vector2(0.585f, 0.12f), new Vector2(0.585f, 0.37f), new Vector2(0.415f, 0.37f));
                    break;

                case Kind.Power:
                    Arc(fg, new Vector2(0.5f, 0.46f), 0.33f, 0.095f, 38f, 322f);
                    Line(fg, new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.9f), 0.095f);
                    break;

                case Kind.Invite:
                    Disc(fg, new Vector2(0.38f, 0.66f), 0.155f);
                    {
                        var pts = new List<Vector2>();
                        for (int k = 0; k <= 16; k++) pts.Add(new Vector2(0.38f, 0.12f) + Dir(-90f + k * 180f / 16f) * 0.3f);
                        Poly(fg, pts.ToArray());
                    }
                    Line(fg, new Vector2(0.8f, 0.36f), new Vector2(0.8f, 0.72f), 0.085f);
                    Line(fg, new Vector2(0.62f, 0.54f), new Vector2(0.98f, 0.54f), 0.085f);
                    break;

                case Kind.Map:
                    // Carte dépliée en trois volets, plis en zigzag, repère sur le volet du milieu
                    Poly(fg, new Vector2(0.06f, 0.24f), new Vector2(0.34f, 0.13f), new Vector2(0.34f, 0.77f), new Vector2(0.06f, 0.88f));
                    Poly(fg, new Vector2(0.37f, 0.13f), new Vector2(0.63f, 0.24f), new Vector2(0.63f, 0.88f), new Vector2(0.37f, 0.77f));
                    Poly(fg, new Vector2(0.66f, 0.24f), new Vector2(0.94f, 0.13f), new Vector2(0.94f, 0.77f), new Vector2(0.66f, 0.88f));
                    Disc(bg, new Vector2(0.5f, 0.58f), 0.09f);
                    Poly(bg, new Vector2(0.43f, 0.55f), new Vector2(0.57f, 0.55f), new Vector2(0.5f, 0.36f));
                    Disc(fg, new Vector2(0.5f, 0.58f), 0.035f);
                    break;

                case Kind.Back:
                    Poly(fg, new Vector2(0.1f, 0.5f), new Vector2(0.46f, 0.84f), new Vector2(0.46f, 0.16f));
                    Line(fg, new Vector2(0.4f, 0.5f), new Vector2(0.9f, 0.5f), 0.14f);
                    break;

                case Kind.Trophy:
                    // Coupe à anses, pied et socle, étoile évidée
                    Arc(fg, new Vector2(0.27f, 0.7f), 0.11f, 0.06f, 180f, 360f);
                    Arc(fg, new Vector2(0.73f, 0.7f), 0.11f, 0.06f, 0f, 180f);
                    Poly(fg, new Vector2(0.25f, 0.88f), new Vector2(0.75f, 0.88f), new Vector2(0.71f, 0.62f), new Vector2(0.6f, 0.47f), new Vector2(0.4f, 0.47f), new Vector2(0.29f, 0.62f));
                    Poly(fg, new Vector2(0.45f, 0.48f), new Vector2(0.55f, 0.48f), new Vector2(0.55f, 0.27f), new Vector2(0.45f, 0.27f));
                    Poly(fg, new Vector2(0.36f, 0.2f), new Vector2(0.64f, 0.2f), new Vector2(0.58f, 0.29f), new Vector2(0.42f, 0.29f));
                    Poly(fg, new Vector2(0.27f, 0.08f), new Vector2(0.73f, 0.08f), new Vector2(0.73f, 0.19f), new Vector2(0.27f, 0.19f));
                    Star(bg, new Vector2(0.5f, 0.69f), 0.12f);
                    break;

                case Kind.Road:
                    // Route qui file vers l'horizon : deux bas-côtés et la ligne médiane en tirets
                    Line(fg, new Vector2(0.06f, 0.08f), new Vector2(0.4f, 0.92f), 0.085f);
                    Line(fg, new Vector2(0.94f, 0.08f), new Vector2(0.6f, 0.92f), 0.085f);
                    Poly(fg, new Vector2(0.455f, 0.08f), new Vector2(0.545f, 0.08f), new Vector2(0.537f, 0.3f), new Vector2(0.463f, 0.3f));
                    Poly(fg, new Vector2(0.47f, 0.42f), new Vector2(0.53f, 0.42f), new Vector2(0.525f, 0.58f), new Vector2(0.475f, 0.58f));
                    Poly(fg, new Vector2(0.482f, 0.7f), new Vector2(0.518f, 0.7f), new Vector2(0.515f, 0.82f), new Vector2(0.485f, 0.82f));
                    break;

                case Kind.Gauge:
                    // Compteur : arc, graduations, aiguille dans le rouge
                    {
                        var o = new Vector2(0.5f, 0.42f);
                        Arc(fg, o, 0.38f, 0.085f, -125f, 125f);
                        for (int k = 0; k < 5; k++) Disc(fg, o + Dir(-100f + k * 50f) * 0.24f, 0.03f);
                        Line(fg, o, o + Dir(58f) * 0.3f, 0.075f);
                        Disc(fg, o, 0.085f);
                    }
                    break;

                case Kind.Ramp:
                    // Tremplin et trajectoire du saut en pointillés
                    Poly(fg, new Vector2(0.04f, 0.1f), new Vector2(0.46f, 0.1f), new Vector2(0.46f, 0.4f));
                    for (int k = 0; k < 6; k++)
                    {
                        float t = (k + 1) / 7f, x = 0.5f + t * 0.42f, y = 0.44f + 4f * 0.36f * t * (1f - t) - t * 0.48f;
                        Disc(fg, new Vector2(x, y), 0.058f - k * 0.004f);
                    }
                    Poly(fg, new Vector2(0.62f, 0.1f), new Vector2(0.96f, 0.1f), new Vector2(0.96f, 0.16f), new Vector2(0.62f, 0.16f));
                    break;

                case Kind.Flag:
                    // Drapeau à damier sur sa hampe
                    Line(fg, new Vector2(0.18f, 0.06f), new Vector2(0.18f, 0.94f), 0.075f);
                    Poly(fg, new Vector2(0.22f, 0.9f), new Vector2(0.88f, 0.9f), new Vector2(0.88f, 0.46f), new Vector2(0.22f, 0.46f));
                    for (int i = 0; i < 4; i++)
                        for (int j = 0; j < 2; j++)
                        {
                            if ((i + j) % 2 == 0) continue;
                            float x0 = 0.25f + i * 0.155f, y0 = 0.49f + j * 0.19f;
                            Poly(bg, new Vector2(x0, y0), new Vector2(x0 + 0.135f, y0), new Vector2(x0 + 0.135f, y0 + 0.17f), new Vector2(x0, y0 + 0.17f));
                        }
                    break;

                case Kind.Coin:
                    // Médaille : disque, liseré et étoile évidés
                    Disc(fg, c, 0.42f, 48);
                    Arc(bg, c, 0.33f, 0.045f, 0f, 360f);
                    Star(bg, c, 0.2f);
                    break;

                case Kind.Lock:
                    // Cadenas : anse, corps, trou de serrure
                    Arc(fg, new Vector2(0.5f, 0.52f), 0.19f, 0.085f, -90f, 90f);
                    Line(fg, new Vector2(0.31f, 0.5f), new Vector2(0.31f, 0.53f), 0.085f);
                    Line(fg, new Vector2(0.69f, 0.5f), new Vector2(0.69f, 0.53f), 0.085f);
                    Poly(fg, new Vector2(0.2f, 0.08f), new Vector2(0.8f, 0.08f), new Vector2(0.8f, 0.52f), new Vector2(0.2f, 0.52f));
                    Disc(bg, new Vector2(0.5f, 0.34f), 0.065f);
                    Poly(bg, new Vector2(0.47f, 0.17f), new Vector2(0.53f, 0.17f), new Vector2(0.52f, 0.33f), new Vector2(0.48f, 0.33f));
                    break;

                case Kind.Radar:
                    // Tête de radar sur son mât, objectif évidé, éclairs du flash
                    Line(fg, new Vector2(0.34f, 0.06f), new Vector2(0.34f, 0.5f), 0.09f);
                    Poly(fg, new Vector2(0.1f, 0.46f), new Vector2(0.58f, 0.46f), new Vector2(0.58f, 0.82f), new Vector2(0.1f, 0.82f));
                    Disc(bg, new Vector2(0.34f, 0.64f), 0.115f);
                    Disc(fg, new Vector2(0.34f, 0.64f), 0.05f);
                    Line(fg, new Vector2(0.68f, 0.64f), new Vector2(0.95f, 0.64f), 0.065f);
                    Line(fg, new Vector2(0.66f, 0.78f), new Vector2(0.88f, 0.92f), 0.065f);
                    Line(fg, new Vector2(0.66f, 0.5f), new Vector2(0.88f, 0.36f), 0.065f);
                    break;

                case Kind.Check:
                    // Coche : deux traits épais qui se recouvrent au coude
                    {
                        Vector2 a = new Vector2(0.14f, 0.5f), k = new Vector2(0.4f, 0.22f), b = new Vector2(0.88f, 0.76f);
                        const float w = 0.2f;
                        Line(fg, a, k + (k - a).normalized * w * 0.5f, w);
                        Line(fg, k - (b - k).normalized * w * 0.5f, b, w);
                    }
                    break;

                case Kind.RadarPin:
                    // Radar des cartes (petite taille) : boîtier sur un mât court, objectif évidé, trois éclairs du flash
                    Line(fg, new Vector2(0.38f, 0.08f), new Vector2(0.38f, 0.34f), 0.15f);
                    Poly(fg, new Vector2(0.08f, 0.3f), new Vector2(0.68f, 0.3f), new Vector2(0.68f, 0.76f), new Vector2(0.08f, 0.76f));
                    Disc(bg, new Vector2(0.38f, 0.53f), 0.14f, 24);
                    Disc(fg, new Vector2(0.38f, 0.53f), 0.055f, 16);
                    Line(fg, new Vector2(0.77f, 0.53f), new Vector2(0.98f, 0.53f), 0.11f);
                    Line(fg, new Vector2(0.76f, 0.73f), new Vector2(0.94f, 0.88f), 0.11f);
                    Line(fg, new Vector2(0.76f, 0.33f), new Vector2(0.94f, 0.18f), 0.11f);
                    break;

                case Kind.RampPin:
                    // Tremplin des cartes (petite taille) : rampe pleine et trajectoire du saut en trait épais fléché
                    Poly(fg, new Vector2(0.03f, 0.18f), new Vector2(0.55f, 0.18f), new Vector2(0.55f, 0.56f));
                    Arc(fg, new Vector2(0.76f, 0.5f), 0.2f, 0.15f, -80f, 95f);
                    Poly(fg, new Vector2(0.85f, 0.5f), new Vector2(1.07f, 0.5f), new Vector2(0.96f, 0.22f));
                    break;
            }
        }

        // ------------------------------------------------------------------ pastilles d'objectifs (carte, minicarte)

        static readonly Color BadgeInk = new Color(0.02f, 0.03f, 0.05f, 0.94f);
        static readonly Color PictoInk = new Color(0.07f, 0.075f, 0.09f, 1f);
        /// <summary>Vert de la coche « réussi » : assez soutenu pour porter une coche blanche.</summary>
        public static readonly Color CheckGreen = new Color(0.16f, 0.7f, 0.3f, 1f);

        /// <summary>
        /// Pastille d'objectif centrée sur <paramref name="pos"/> : cerne d'encre, disque (ou losange) de sa couleur et
        /// pictogramme sombre. Sa couleur ne change jamais : la coche verte rendue (masquée) se pose en haut à droite
        /// une fois l'objectif réussi.
        /// </summary>
        public static GameObject Badge(RectTransform parent, Vector2 pos, float size, Kind kind, Color tint, bool diamond)
        {
            var root = Node(parent, "Badge", pos, new Vector2(size, size));
            float border = Mathf.Max(1.6f, size * 0.09f);
            if (diamond)
            {
                float side = size * 0.84f;
                Layer(root, Vector2.zero, side, BadgeInk, null).rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
                Layer(root, Vector2.zero, side - border * 2f, tint, null).rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
                Picto(root, Vector2.zero, size * 0.6f, kind, tint);
            }
            else
            {
                Layer(root, Vector2.zero, size, BadgeInk, HudArt.Disc);
                Layer(root, Vector2.zero, size - border * 2f, tint, HudArt.Disc);
                Picto(root, Vector2.zero, size * 0.6f, kind, tint);
            }
            var check = Check(root, new Vector2(size * 0.4f, size * 0.4f), size * 0.56f);
            check.SetActive(false);
            return check;
        }

        /// <summary>Coche « réussi » : disque vert cerné d'encre, coche blanche.</summary>
        public static GameObject Check(RectTransform parent, Vector2 pos, float size)
        {
            var root = Node(parent, "Done", pos, new Vector2(size, size));
            Layer(root, Vector2.zero, size, BadgeInk, HudArt.Disc);
            Layer(root, Vector2.zero, size - Mathf.Max(1.4f, size * 0.14f) * 2f, CheckGreen, HudArt.Disc);
            Picto(root, new Vector2(0f, -size * 0.02f), size * 0.56f, Kind.Check, CheckGreen).color = Color.white;
            return root.gameObject;
        }

        static RectTransform Node(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image Layer(RectTransform parent, Vector2 pos, float size, Color color, Sprite sprite)
        {
            var img = Node(parent, "Layer", pos, new Vector2(size, size)).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static HudIcon Picto(RectTransform parent, Vector2 pos, float size, Kind kind, Color bg)
        {
            var icon = Node(parent, "Icon", pos, new Vector2(size, size)).gameObject.AddComponent<HudIcon>();
            icon.raycastTarget = false;
            icon.Shape = kind;
            icon.SetColors(PictoInk, bg);
            return icon;
        }

        /// <summary>Étoile à cinq branches : pentagone central et cinq pointes (chaque morceau convexe).</summary>
        void Star(Color c, Vector2 center, float radius)
        {
            float inner = radius * 0.42f;
            var pent = new Vector2[5];
            for (int k = 0; k < 5; k++) pent[k] = center + Dir(36f + k * 72f) * inner;
            Poly(c, pent);
            for (int k = 0; k < 5; k++)
                Poly(c, center + Dir(k * 72f) * radius, center + Dir(k * 72f + 36f) * inner * 0.98f, center + Dir(k * 72f - 36f) * inner * 0.98f);
        }

        // ------------------------------------------------------------------ primitives (coordonnées du carré, 0..1)

        /// <summary>Direction d'un angle en degrés : 0 = haut, sens horaire.</summary>
        static Vector2 Dir(float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        Vector2 P(Vector2 u) => new Vector2(box.xMin + u.x * box.width, box.yMin + u.y * box.height);

        static Color A(Color c, float k) { c.a *= k; return c; }

        void Vert(Vector2 p, Color c) => vh.AddVert(new Vector3(p.x, p.y), c, Vector4.zero);

        /// <summary>Polygone convexe plein, bord extérieur adouci.</summary>
        void Poly(Color c, params Vector2[] u)
        {
            int n = u.Length;
            var p = new Vector2[n];
            float area = 0f;
            for (int i = 0; i < n; i++) p[i] = P(u[i]);
            for (int i = 0; i < n; i++) { var a = p[i]; var b = p[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            if (area < 0f) System.Array.Reverse(p);
            Vector2 center = Vector2.zero;
            foreach (var q in p) center += q;
            center /= n;
            int start = vh.currentVertCount;
            Vert(center, c);
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = p[(i - 1 + n) % n], cur = p[i], next = p[(i + 1) % n];
                Vector2 e1 = (cur - prev).normalized, e2 = (next - cur).normalized;
                Vector2 n1 = new Vector2(e1.y, -e1.x), n2 = new Vector2(e2.y, -e2.x);   // normales sortantes (sens trigonométrique)
                Vector2 m = (n1 + n2).normalized;
                float k = Mathf.Min(1f / Mathf.Max(Vector2.Dot(m, n1), 0.35f), 2.5f);
                Vert(cur, c);
                Vert(cur + m * Feather * k, A(c, 0f));
            }
            for (int i = 0; i < n; i++)
            {
                int a = start + 1 + i * 2, b = start + 1 + ((i + 1) % n) * 2;
                vh.AddTriangle(start, a, b);
                vh.AddTriangle(a, a + 1, b + 1);
                vh.AddTriangle(a, b + 1, b);
            }
        }

        void Disc(Color c, Vector2 center, float radius, int segments = 36)
        {
            var u = new Vector2[segments];
            for (int i = 0; i < segments; i++) u[i] = center + Dir(i * 360f / segments) * radius;
            Poly(c, u);
        }

        /// <summary>Trait épais (bouts carrés).</summary>
        void Line(Color c, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = (b - a).normalized, t = new Vector2(-d.y, d.x) * width * 0.5f;
            Poly(c, a - t, b - t, b + t, a + t);
        }

        /// <summary>Arc d'anneau (angles en degrés, 0 = haut, sens horaire), bords intérieur et extérieur adoucis.</summary>
        void Arc(Color c, Vector2 center, float radius, float width, float from, float to)
        {
            int segments = Mathf.Max(8, Mathf.CeilToInt((to - from) / 6f));
            float s = box.width;
            float rIn = radius * s - width * s * 0.5f, rOut = radius * s + width * s * 0.5f;
            Vector2 o = P(center);
            int start = vh.currentVertCount;
            for (int i = 0; i <= segments; i++)
            {
                Vector2 d = Dir(Mathf.Lerp(from, to, i / (float)segments));
                Vert(o + d * (rIn - Feather), A(c, 0f));
                Vert(o + d * rIn, c);
                Vert(o + d * rOut, c);
                Vert(o + d * (rOut + Feather), A(c, 0f));
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
}
