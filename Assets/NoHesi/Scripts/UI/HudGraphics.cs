using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Plaque de HUD : parallélogramme (bords inclinés) à bords latéraux estompés et dégradé vertical,
    /// pour des panneaux qui se fondent dans l'image au lieu de rectangles pleins.
    /// </summary>
    public class HudShape : MaskableGraphic
    {
        public float skew = 14f;                      // décalage du bord haut vers la droite (px)
        public float fadeLeft, fadeRight;             // largeur des fondus latéraux (px)
        public float topAlpha = 1f, bottomAlpha = 1f; // dégradé vertical

        public HudShape Set(float skewPx, float fadeL, float fadeR, float top = 1f, float bottom = 1f)
        {
            skew = skewPx; fadeLeft = fadeL; fadeRight = fadeR; topAlpha = top; bottomAlpha = bottom;
            SetVerticesDirty();
            return this;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float[] xs = { r.xMin, r.xMin + fadeLeft, r.xMax - fadeRight, r.xMax };
            float[] a = { fadeLeft > 0f ? 0f : 1f, 1f, 1f, fadeRight > 0f ? 0f : 1f };
            for (int i = 0; i < 4; i++)
            {
                vh.AddVert(new Vector3(xs[i] - skew * 0.5f, r.yMin), Alpha(a[i] * bottomAlpha), Vector4.zero);
                vh.AddVert(new Vector3(xs[i] + skew * 0.5f, r.yMax), Alpha(a[i] * topAlpha), Vector4.zero);
            }
            for (int i = 0; i < 3; i++)
            {
                int b = i * 2;
                vh.AddTriangle(b, b + 1, b + 3);
                vh.AddTriangle(b, b + 3, b + 2);
            }
        }

        Color Alpha(float k)
        {
            var c = color;
            c.a *= k;
            return c;
        }
    }

    /// <summary>Ressources du HUD générées au lancement : anneau (compte-tours), disques, police.</summary>
    public static class HudArt
    {
        static Sprite ring, disc, softDisc;
        static Font font;

        /// <summary>Anneau fin (remplissage radial pour les jauges circulaires).</summary>
        public static Sprite Ring => ring ? ring : ring = Make(256, (d, r) => Mathf.Clamp01((r - d) * 1.5f) * Mathf.Clamp01((d - r * 0.86f) * 1.5f));
        /// <summary>Disque net.</summary>
        public static Sprite Disc => disc ? disc : disc = Make(128, (d, r) => Mathf.Clamp01((r - d) * 1.5f));
        /// <summary>Disque aux bords très doux (fond du compteur).</summary>
        public static Sprite SoftDisc => softDisc ? softDisc : softDisc = Make(256, (d, r) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - d) / (r * 0.35f))));

        static Sprite Make(int size, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = (size - 1) * 0.5f, r = size * 0.5f - 1f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(d, r)) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Police du HUD : Bahnschrift (Windows 10/11, esprit tableau de bord) si elle est installée,
        /// sinon la police intégrée. Les symboles absents passent par Segoe UI Symbol puis Arial.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (font) return font;
                string[] installed = Font.GetOSInstalledFontNames();
                if (installed.Contains("Bahnschrift"))
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI Symbol", "Arial" }, 32);
                if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }
    }
}
