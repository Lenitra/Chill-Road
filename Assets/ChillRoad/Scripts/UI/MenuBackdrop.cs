using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Fond des menus du style verre : l'image du jeu floutée, sous un voile sombre. À l'ouverture du menu, la caméra
    /// principale est rendue une fois dans une petite texture puis floutée (shader Hidden/ChillRoad/UIBlur) ; en
    /// partie en réseau (le monde continue), l'image est refaite deux fois par seconde.
    /// Une seule texture floutée partagée par tous les menus.
    /// </summary>
    public class MenuBackdrop : MonoBehaviour
    {
        static RenderTexture blurA, blurB;
        static Material blur;
        static int capturedFrame = -1;

        /// <summary>Image du jeu floutée (dernière prise), null avant la première.</summary>
        public static Texture Texture => blurA;

        /// <summary>Refaire l'image pendant que le menu est ouvert (le monde continue : partie en réseau).</summary>
        public bool live;
        RawImage image;
        float timer;

        /// <summary>Fond plein écran (image floutée + voile) en premier enfant de <paramref name="parent"/>.</summary>
        public static MenuBackdrop Attach(RectTransform parent, float veil = 0.5f)
        {
            var rt = Glass.Rect(parent, "Backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rt.SetAsFirstSibling();
            var go = rt.gameObject;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = true;   // le menu arrête les clics vers ce qui est dessous
            var veilImg = Glass.Bar(rt, "Veil", Glass.A(Glass.Veil, veil));
            Glass.Stretch(veilImg.rectTransform);
            var b = go.AddComponent<MenuBackdrop>();
            b.image = raw;
            return b;
        }

        void OnEnable()
        {
            if (!image) image = GetComponent<RawImage>();
            Capture();
            timer = 0.5f;
            if (image) { image.texture = blurA; image.color = blurA ? Color.white : new Color(0.04f, 0.05f, 0.07f, 1f); }
        }

        void Update()
        {
            if (!live) return;
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            Capture(true);
            if (image) image.texture = blurA;
        }

        /// <summary>Rend la caméra principale et floute l'image (une fois par image au plus).</summary>
        public static void Capture(bool force = false)
        {
            if (!force && capturedFrame == Time.frameCount) return;
            var cam = Camera.main;
            if (!cam || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (!blur)
            {
                var shader = Resources.Load<Shader>("UIBlur");
                if (!shader || !shader.isSupported) return;
                blur = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            capturedFrame = Time.frameCount;
            int w = Mathf.Max(64, Screen.width / 4), h = Mathf.Max(36, Screen.height / 4);
            Ensure(ref blurA, w, h);
            Ensure(ref blurB, w, h);
            var full = RenderTexture.GetTemporary(Mathf.Max(64, Screen.width / 2), Mathf.Max(36, Screen.height / 2), 24, RenderTextureFormat.Default);
            var prev = cam.targetTexture;
            cam.targetTexture = full;
            cam.Render();
            cam.targetTexture = prev;
            Graphics.Blit(full, blurA);
            RenderTexture.ReleaseTemporary(full);
            for (int i = 0; i < 3; i++)
            {
                blur.SetVector("_Dir", new Vector4(1.6f / w, 0f, 0f, 0f));
                Graphics.Blit(blurA, blurB, blur);
                blur.SetVector("_Dir", new Vector4(0f, 1.6f / h, 0f, 0f));
                Graphics.Blit(blurB, blurA, blur);
            }
        }

        static void Ensure(ref RenderTexture rt, int w, int h)
        {
            if (rt && rt.width == w && rt.height == h) return;
            if (rt) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.Default) { name = "MenuBlur", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
        }
    }
}
