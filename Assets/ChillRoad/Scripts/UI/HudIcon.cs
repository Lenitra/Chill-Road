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
        public enum Kind { Play, Car, Clock, Gear, Home, Power, Invite, Back }

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

                case Kind.Back:
                    Poly(fg, new Vector2(0.1f, 0.5f), new Vector2(0.46f, 0.84f), new Vector2(0.46f, 0.16f));
                    Line(fg, new Vector2(0.4f, 0.5f), new Vector2(0.9f, 0.5f), 0.14f);
                    break;
            }
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
