using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Style « verre » de l'interface (refonte 2026-11-19) : panneaux sombres translucides aux coins arrondis et au
    /// liseré clair, texte fin, or pour la sélection et l'argent, cyan pour les petits titres. Les menus sont posés sur
    /// une image floutée du jeu (<see cref="MenuBackdrop"/>).
    /// </summary>
    public static class Glass
    {
        // Couleurs. Le projet est en espace linéaire : Unity mélange la transparence en linéaire, ce qui éclaircit les
        // voiles sombres et renforce les teintes claires par rapport aux maquettes (sRVB). D'où des fonds plus opaques
        // et des voiles d'or et de blanc plus légers que les valeurs CSS des maquettes, pour le même rendu à l'œil.
        public static readonly Color Panel = new Color(0.063f, 0.078f, 0.102f, 0.7f);
        public static readonly Color PanelStrong = new Color(0.063f, 0.078f, 0.102f, 0.84f);
        public static readonly Color Edge = new Color(1f, 1f, 1f, 0.2f);
        public static readonly Color Pill = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.28f);
        public static readonly Color Gold = new Color(1f, 0.76f, 0.2f);
        public static readonly Color GoldSoft = new Color(1f, 0.76f, 0.2f, 0.05f);
        public static readonly Color GoldHalo = new Color(1f, 0.76f, 0.2f, 0.2f);
        public static readonly Color Cyan = new Color(0.44f, 0.86f, 1f);
        public static readonly Color Ink = new Color(0.08f, 0.094f, 0.118f);
        public static readonly Color White = new Color(0.97f, 0.98f, 0.99f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.72f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.45f);
        public static readonly Color Good = new Color(0.61f, 0.94f, 0.76f);
        public static readonly Color Bad = new Color(1f, 0.54f, 0.48f);
        public static readonly Color Veil = new Color(0.055f, 0.07f, 0.094f, 0.62f);

        // Polices : Inter en graisses fines (Light 300, Regular 400, Medium 500, SemiBold 600)
        public enum Weight { Light, Semilight, Regular, Semibold }
        static readonly Font[] fonts = new Font[4];
        static readonly string[] Files = { "Fonts/Inter-Light", "Fonts/Inter-Regular", "Fonts/Inter-Medium", "Fonts/Inter-SemiBold" };

        public static Font FontOf(Weight w)
        {
            int i = (int)w;
            if (fonts[i]) return fonts[i];
            // Inter embarquée (OFL, Resources/Fonts) : Unity ne sait pas ouvrir les graisses fines de Segoe UI par leur nom
            fonts[i] = Resources.Load<Font>(Files[i]);
            if (!fonts[i]) fonts[i] = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 32);
            if (!fonts[i]) fonts[i] = HudArt.Font;
            return fonts[i];
        }

        /// <summary>Espace fine entre les lettres d'un titre (le texte classique n'a pas d'interlettrage).</summary>
        public static string Spaced(string s, int spaces = 1)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string gap = new string(' ', spaces);
            var sb = new System.Text.StringBuilder(s.Length * (spaces + 1));
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append(s[i] == ' ' ? "  " : gap);
                if (s[i] != ' ') sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ fabrique

        public static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        /// <summary>Rectangle posé : ancre et pivot au même point, position et taille.</summary>
        public static RectTransform Place(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, anchor, anchor, Vector2.zero, Vector2.zero);
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Panneau de verre : fond sombre translucide, liseré clair, coins arrondis.</summary>
        public static GlassRect Make(Transform parent, string name, float radius, Color? fill = null, float edge = 1.5f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<GlassRect>();
            g.color = fill ?? Panel;
            g.radius = radius;
            g.border = edge;
            g.borderColor = Edge;
            g.raycastTarget = false;
            return g;
        }

        /// <summary>Fond de verre étiré sur tout le parent, en premier enfant, hors de toute mise en page.</summary>
        public static GlassRect Backdrop(RectTransform parent, float radius, Color? fill = null, float edge = 1.5f)
        {
            var g = Make(parent, "Glass", radius, fill, edge);
            g.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(g.rectTransform);
            g.rectTransform.SetAsFirstSibling();
            return g;
        }

        public static Text Label(Transform parent, string text, int size, Weight weight, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = FontOf(weight);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        /// <summary>Texte posé (ancre = pivot) à la position et dans la boîte données.</summary>
        public static Text Label(Transform parent, string text, int size, Weight weight, Color color, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var t = Label(parent, text, size, weight, color, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            return t;
        }

        /// <summary>
        /// Texte d'un écran encore construit à l'ancienne (gras italique) passé au style verre : graisse fine pour les
        /// grands chiffres et titres, normale pour le gras, demi-fine pour le reste ; plus d'italique.
        /// </summary>
        public static void Adopt(Text t, FontStyle style)
        {
            t.font = FontOf(t.fontSize >= 40 ? Weight.Light : style != FontStyle.Normal ? Weight.Regular : Weight.Semilight);
            t.fontStyle = FontStyle.Normal;
        }

        /// <summary>Petit titre cyan espacé (« TOUR », « COURSE », « GÉNÉRAL »…).</summary>
        public static Text Kicker(Transform parent, string text, int size = 16)
        {
            var t = Label(parent, Spaced(text, 2), size, Weight.Semibold, Cyan);
            return t;
        }

        /// <summary>Ombre douce derrière un texte posé sur l'image du jeu.</summary>
        public static void Soft(Text t, float alpha = 0.55f)
        {
            var s = t.gameObject.GetComponent<Shadow>() ?? t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, alpha);
            s.effectDistance = new Vector2(0f, -2f);
        }

        public static Image Bar(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Couleur avec une autre opacité.</summary>
        public static Color A(Color c, float a) { c.a = a; return c; }

        // ------------------------------------------------------------------ anneaux (jauges)

        static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>();

        /// <summary>Anneau fin (remplissage radial) : épaisseur en fraction du rayon, bords lissés.</summary>
        public static Sprite Ring(float thickness)
        {
            int key = Mathf.RoundToInt(thickness * 1000f);
            if (rings.TryGetValue(key, out var s) && s) return s;
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = (size - 1) * 0.5f, r = size * 0.5f - 2f, inner = r * (1f - thickness);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(r - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            rings[key] = s;
            return s;
        }
    }

    /// <summary>
    /// Rectangle arrondi du style verre : remplissage (couleur du Graphic), liseré, halo extérieur facultatif (sélection),
    /// bords lissés d'un pixel. radius au-delà de la demi-hauteur = pilule.
    /// </summary>
    public class GlassRect : MaskableGraphic
    {
        public float radius = 24f;
        public float border = 1.5f;
        public Color borderColor = Glass.Edge;
        public float halo;
        public Color haloColor = Glass.GoldHalo;

        public GlassRect Set(Color fill, Color edgeColor, float edge, float haloSize = 0f, Color? haloTint = null)
        {
            color = fill;
            borderColor = edgeColor;
            border = edge;
            halo = haloSize;
            if (haloTint.HasValue) haloColor = haloTint.Value;
            SetVerticesDirty();
            return this;
        }

        public void SetRadius(float r) { radius = r; SetVerticesDirty(); }

        static readonly List<Vector2> outer = new List<Vector2>(), inner = new List<Vector2>();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Draw(vh, GetPixelAdjustedRect(), radius, color, border, borderColor, halo, haloColor);
        }

        /// <summary>Dessine un rectangle arrondi de verre dans le maillage (aussi utilisé par <see cref="HudShape"/>).</summary>
        public static void Draw(VertexHelper vh, Rect r, float radius, Color fill, float border, Color borderColor, float halo, Color haloColor)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            float rad = Mathf.Min(radius, r.width * 0.5f, r.height * 0.5f);
            int seg = Mathf.Clamp(Mathf.CeilToInt(rad / 3f), 2, 16);
            float b = Mathf.Min(Mathf.Max(0f, border), rad);
            // Remplissage jusqu'au liseré (ou jusqu'au bord)
            Contour(r, b, rad, seg, inner);
            int center = vh.currentVertCount;
            vh.AddVert(r.center, fill, Vector4.zero);
            int start = vh.currentVertCount;
            foreach (var p in inner) vh.AddVert(p, fill, Vector4.zero);
            for (int i = 0; i < inner.Count; i++) vh.AddTriangle(center, start + i, start + (i + 1) % inner.Count);
            // Liseré, puis lissage vers l'extérieur
            if (b > 0f && borderColor.a > 0f)
            {
                Contour(r, 0f, rad, seg, outer);
                Band(vh, inner, fill, outer, borderColor, b > 1f);
                Contour(r, -1f, rad, seg, inner);
                Band(vh, outer, borderColor, inner, Glass.A(borderColor, 0f), false);
            }
            else
            {
                Contour(r, -1f, rad, seg, outer);
                Band(vh, inner, fill, outer, Glass.A(fill, 0f), false);
            }
            if (halo > 0f && haloColor.a > 0f)
            {
                Contour(r, -0.5f, rad, seg, inner);
                Contour(r, -halo, rad, seg, outer);
                Band(vh, inner, haloColor, outer, Glass.A(haloColor, 0f), false);
            }
        }

        /// <summary>Bande entre deux contours de même nombre de points ; hard : le bord intérieur garde la couleur extérieure (liseré net).</summary>
        static void Band(VertexHelper vh, List<Vector2> a, Color ca, List<Vector2> b, Color cb, bool hard)
        {
            int n = a.Count;
            int start = vh.currentVertCount;
            for (int i = 0; i < n; i++)
            {
                vh.AddVert(a[i], hard ? cb : ca, Vector4.zero);
                vh.AddVert(b[i], cb, Vector4.zero);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a0 = start + i * 2, b0 = a0 + 1, a1 = start + j * 2, b1 = a1 + 1;
                vh.AddTriangle(a0, b0, b1);
                vh.AddTriangle(a0, b1, a1);
            }
        }

        /// <summary>Contour arrondi rentré de <paramref name="inset"/> (négatif : vers l'extérieur), même nombre de points quel que soit l'écart (rayon ≥ écart).</summary>
        static void Contour(Rect r, float inset, float rad, int seg, List<Vector2> pts)
        {
            pts.Clear();
            float cr = Mathf.Max(0f, rad - inset);
            float xMin = r.xMin + rad, xMax = r.xMax - rad, yMin = r.yMin + rad, yMax = r.yMax - rad;
            var centers = new[] { new Vector2(xMax, yMax), new Vector2(xMin, yMax), new Vector2(xMin, yMin), new Vector2(xMax, yMin) };
            for (int c = 0; c < 4; c++)
                for (int s = 0; s <= seg; s++)
                {
                    float ang = (c * 90f + 90f * s / seg) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    // Coin sans rayon : le décalage suit la diagonale pour que le contour reste parallèle
                    pts.Add(centers[c] + dir * cr);
                }
        }
    }
}
