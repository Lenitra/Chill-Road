using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Minicarte du HUD (bas gauche, pendant du compte-tours), en version claire : disque découpé dans
    /// Resources/Map/map_minimap.png (style « épuré » : fond crème, routes blanches bordées de beige, autoroute orange,
    /// dessinée par Tools/Map/map_minimap.py) et tourné avec la voiture (l'avant toujours en haut). La voiture, flèche noire cerclée de
    /// blanc, est un peu sous le centre pour voir plus loin devant ; le champ s'élargit avec la vitesse. Circulation en
    /// points gris foncé, autres joueurs en flèches grises (posées sur le bord quand ils sont hors champ), nord sur le
    /// bord du disque.
    /// </summary>
    public class MiniMap : MonoBehaviour
    {
        /// <summary>Côté du bloc (unités du HUD, référence 1920 × 1080).</summary>
        public const float Size = 300f;
        const float Radius = 128f;              // rayon du disque de carte
        const float CarOffset = 0.3f;           // la voiture est sous le centre (fraction du rayon)
        const float NearRange = 230f, FarRange = 430f;   // mètres couverts par un rayon, à l'arrêt et à pleine vitesse

        static readonly Color Black = new Color(0.04f, 0.04f, 0.04f, 0.95f);
        static readonly Color White = new Color(0.98f, 0.98f, 0.98f, 1f);
        internal static readonly Color TrafficCore = new Color(0.36f, 0.36f, 0.36f, 1f);
        internal static readonly Color TrafficEdge = new Color(0.98f, 0.98f, 0.98f, 0.95f);

        // ------------------------------------------------------------------ autres joueurs

        class Marker { public object owner; public Func<Transform> target; public Color color; }
        static readonly List<Marker> markers = new List<Marker>();

        /// <summary>
        /// Montre une voiture sur la minicarte (joueur en réseau). <paramref name="target"/> est relu à chaque image
        /// (changement de voiture) ; null ou détruit = rien d'affiché. La couleur n'est gardée que pour sa clarté (gris).
        /// </summary>
        public static void Track(object owner, Func<Transform> target, Color color)
        {
            Untrack(owner);
            markers.Add(new Marker { owner = owner, target = target, color = color });
        }

        public static void Untrack(object owner) => markers.RemoveAll(m => m.owner == owner);

        /// <summary>
        /// Couches dessinées sur le disque (itinéraire du GPS) : appelées à chaque image avec la racine de la minicarte,
        /// la conversion monde → disque (unités du disque, centre = 0) et le rayon du disque.
        /// </summary>
        public static event Action<RectTransform, Func<Vector3, Vector2>, float> Overlay;

        // ------------------------------------------------------------------ instance

        GameManager game;
        PlayerSlot slot;
        CanvasGroup group;
        MiniMapDisc disc;
        MiniMapDots dots;
        RectTransform north, carMark;
        readonly List<(RectTransform rt, HudArrow fill)> others = new List<(RectTransform, HudArrow)>();
        float heading, range = NearRange;
        bool hasHeading;

        public void Init(GameManager g, PlayerSlot player)
        {
            game = g;
            slot = player;
            var root = (RectTransform)transform;
            // Canevas à part : la minicarte change à chaque image sans refaire tout le HUD
            gameObject.AddComponent<Canvas>();
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Ombre portée : disque noir aux bords doux, détache la carte de l'image du jeu
            var back = Box(root, "Back", Vector2.zero, new Vector2(Size, Size)).gameObject.AddComponent<Image>();
            back.sprite = HudArt.SoftDisc;
            back.color = new Color(0f, 0f, 0f, 0.55f);
            back.raycastTarget = false;

            disc = Box(root, "Map", Vector2.zero, new Vector2(Radius * 2f, Radius * 2f)).gameObject.AddComponent<MiniMapDisc>();
            disc.color = new Color(1f, 1f, 1f, 0.96f);
            disc.raycastTarget = false;
            disc.radius = Radius;

            dots = Box(root, "Traffic", Vector2.zero, new Vector2(Radius * 2f, Radius * 2f)).gameObject.AddComponent<MiniMapDots>();

            // Liseré : fin cercle sombre, net sur le bord clair de la carte
            var rim = Box(root, "Rim", Vector2.zero, new Vector2(Radius * 2f + 8f, Radius * 2f + 8f)).gameObject.AddComponent<HudRing>();
            rim.inner = Radius - 0.5f;
            rim.outer = Radius + 1.8f;
            rim.color = new Color(0f, 0f, 0f, 0.55f);

            // Nord : pastille noire et « N » blanc, sur le bord du disque
            north = Box(root, "North", Vector2.zero, new Vector2(26f, 26f));
            var pip = Box(north, "Disc", Vector2.zero, new Vector2(26f, 26f)).gameObject.AddComponent<Image>();
            pip.sprite = HudArt.Disc;
            pip.color = Black;
            pip.raycastTarget = false;
            var ring = Box(north, "Ring", Vector2.zero, new Vector2(30f, 30f)).gameObject.AddComponent<HudRing>();
            ring.inner = 12.5f;
            ring.outer = 13.5f;
            ring.color = new Color(1f, 1f, 1f, 0.32f);
            Label(north, "N", 16, White).rectTransform.anchoredPosition = new Vector2(0f, 1f);

            // Voiture du joueur : flèche noire cerclée de blanc, toujours vers le haut
            carMark = Box(root, "Car", new Vector2(0f, -CarOffset * Radius), new Vector2(32f, 32f));
            Arrow(carMark, 30f, White);
            Arrow(carMark, 21f, Black).rectTransform.anchoredPosition = new Vector2(0f, 0.5f);
        }

        void LateUpdate()
        {
            var map = game ? game.Map : null;
            var car = slot != null ? slot.Car : null;
            bool ready = map != null && car && disc;
            group.alpha = ready ? 1f : 0f;
            if (!ready) return;
            if (!disc.texture)
            {
                // Carte de la minicarte (Tools/Map/map_minimap.py), sinon la carte globale
                disc.texture = Resources.Load<Texture2D>("Map/map_minimap");
                if (!disc.texture) disc.texture = Resources.Load<Texture2D>("Map/map_overview");
                disc.half = map.Half;
                disc.SetMaterialDirty();
            }
            float dt = Time.unscaledDeltaTime;

            // Cap de la voiture (sur le plan horizontal, lissé) et champ selon la vitesse
            var t = car.transform;
            Vector3 f = t.forward;
            if (f.x * f.x + f.z * f.z > 0.04f)
            {
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                heading = hasHeading ? Mathf.LerpAngle(heading, yaw, 1f - Mathf.Exp(-dt * 12f)) : yaw;
                hasHeading = true;
            }
            float target = Mathf.Lerp(NearRange, FarRange, Mathf.InverseLerp(30f, 170f, car.SpeedKmh));
            range = Mathf.Lerp(range, target, 1f - Mathf.Exp(-dt * 1.2f));
            float metresPerUnit = range / Radius;
            Vector2 pos = new Vector2(t.position.x, t.position.z);
            Vector2 carUi = carMark.anchoredPosition;
            disc.Show(pos, heading, metresPerUnit, carUi);

            float rad = heading * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 ToUi(Vector3 w)
            {
                float dx = (w.x - pos.x) / metresPerUnit, dz = (w.z - pos.y) / metresPerUnit;
                return carUi + new Vector2(dx * cos - dz * sin, dx * sin + dz * cos);
            }

            // Nord : direction (−sin ψ, cos ψ) depuis le centre
            north.anchoredPosition = new Vector2(-sin, cos) * Radius;

            // Circulation
            dots.points.Clear();
            var traffic = game.Traffic;
            if (traffic != null)
                foreach (var c in traffic.Cars)
                {
                    if (c.go == null || c.veiled || !c.go.activeInHierarchy) continue;
                    Vector2 p = ToUi(c.go.transform.position);
                    if (p.sqrMagnitude < (Radius - 6f) * (Radius - 6f)) dots.points.Add(p);
                }
            dots.SetVerticesDirty();
            Overlay?.Invoke((RectTransform)transform, ToUi, Radius);

            // Autres joueurs : flèche grise cerclée de blanc, posée sur le bord quand ils sont hors du disque
            int used = 0;
            foreach (var m in markers)
            {
                Transform o = null;
                try { o = m.target?.Invoke(); } catch (Exception) { }
                if (!o || o == t) continue;   // pas sa propre voiture
                if (used == others.Count) others.Add(MakeOther());
                var (rt, fill) = others[used++];
                rt.gameObject.SetActive(true);
                float grey = Mathf.Lerp(0.3f, 0.55f, m.color.grayscale);
                fill.color = new Color(grey, grey, grey, 1f);
                Vector2 p = ToUi(o.position);
                float edge = Radius - 10f;
                if (p.sqrMagnitude > edge * edge) p = p.normalized * edge;
                rt.anchoredPosition = p;
                Vector3 of = o.forward;
                float oyaw = Mathf.Atan2(of.x, of.z) * Mathf.Rad2Deg;
                rt.localEulerAngles = new Vector3(0f, 0f, heading - oyaw);
            }
            for (int i = used; i < others.Count; i++) others[i].rt.gameObject.SetActive(false);
        }

        (RectTransform, HudArrow) MakeOther()
        {
            var rt = Box((RectTransform)transform, "Other", Vector2.zero, new Vector2(24f, 24f));
            // Sous le nord et la voiture du joueur, qui restent au-dessus de tout
            rt.SetSiblingIndex(north.GetSiblingIndex());
            Arrow(rt, 22f, White);
            var fill = Arrow(rt, 14f, Black);
            fill.rectTransform.anchoredPosition = new Vector2(0f, 0.5f);
            return (rt, fill);
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Box(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static HudArrow Arrow(RectTransform parent, float size, Color color)
        {
            var a = Box(parent, "Arrow", Vector2.zero, new Vector2(size * 0.8f, size)).gameObject.AddComponent<HudArrow>();
            a.color = color;
            a.raycastTarget = false;
            return a;
        }

        static Text Label(RectTransform parent, string text, int size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudArt.Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 30f);
            return t;
        }
    }

    /// <summary>
    /// Disque de la carte : éventail de triangles dont les UV couvrent la zone autour de la voiture, tournée selon son
    /// cap. Le bord est adouci par une couronne de sommets transparents (pas de masque, pas de crénelage).
    /// </summary>
    public class MiniMapDisc : MaskableGraphic
    {
        const int Segments = 72;
        public Texture2D texture;
        public float radius = 128f, half = 3200f;
        Vector2 center, carUi;
        float heading, metresPerUnit = 2f;

        public override Texture mainTexture => texture ? texture : s_WhiteTexture;

        public void Show(Vector2 worldCenter, float headingDeg, float mPerUnit, Vector2 carPos)
        {
            center = worldCenter;
            heading = headingDeg;
            metresPerUnit = mPerUnit;
            carUi = carPos;
            SetVerticesDirty();
        }

        Vector2 Uv(Vector2 local, float cos, float sin)
        {
            // Écran → monde : rotation inverse du cap autour de la voiture
            Vector2 s = (local - carUi) * metresPerUnit;
            float dx = s.x * cos + s.y * sin, dz = -s.x * sin + s.y * cos;
            float k = 0.5f / half;
            return new Vector2(Mathf.Clamp01((center.x + dx) * k + 0.5f), Mathf.Clamp01((center.y + dz) * k + 0.5f));
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!texture) return;
            float rad = heading * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Color32 solid = color, clear = color;
            clear.a = 0;
            vh.AddVert(Vector3.zero, solid, Uv(Vector2.zero, cos, sin));
            float rIn = radius - 1.2f, rOut = radius + 0.6f;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(dir * rIn, solid, Uv(dir * rIn, cos, sin));
                vh.AddVert(dir * rOut, clear, Uv(dir * rOut, cos, sin));
            }
            for (int i = 0; i < Segments; i++)
            {
                int a0 = 1 + i * 2, a1 = 1 + (i + 1) % Segments * 2;
                vh.AddTriangle(0, a1, a0);
                vh.AddTriangle(a0, a1, a1 + 1);
                vh.AddTriangle(a0, a1 + 1, a0 + 1);
            }
        }
    }

    /// <summary>Points de la circulation : un disque blanc et un disque gris foncé par voiture, en un seul maillage.</summary>
    public class MiniMapDots : MaskableGraphic
    {
        public readonly List<Vector2> points = new List<Vector2>();
        public override Texture mainTexture => HudArt.Disc.texture;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 edge = MiniMap.TrafficEdge, core = MiniMap.TrafficCore;
            foreach (var p in points) Quad(vh, p, 5.2f, edge);
            foreach (var p in points) Quad(vh, p, 3.4f, core);
        }

        static void Quad(VertexHelper vh, Vector2 p, float r, Color32 c)
        {
            int b = vh.currentVertCount;
            vh.AddVert(new Vector3(p.x - r, p.y - r), c, new Vector2(0f, 0f));
            vh.AddVert(new Vector3(p.x - r, p.y + r), c, new Vector2(0f, 1f));
            vh.AddVert(new Vector3(p.x + r, p.y + r), c, new Vector2(1f, 1f));
            vh.AddVert(new Vector3(p.x + r, p.y - r), c, new Vector2(1f, 0f));
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
        }
    }

    /// <summary>Couronne aux bords adoucis (liseré d'un disque, cadran).</summary>
    public class HudRing : MaskableGraphic
    {
        public float inner = 60f, outer = 64f;
        const int Segments = 72;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 solid = color, clear = color;
            clear.a = 0;
            float[] radii = { inner - 0.8f, inner, outer, outer + 0.8f };
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (int k = 0; k < 4; k++) vh.AddVert(dir * radii[k], k == 0 || k == 3 ? clear : solid, Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                int a = i * 4, b = (i + 1) % Segments * 4;
                for (int k = 0; k < 3; k++)
                {
                    vh.AddTriangle(a + k, b + k, b + k + 1);
                    vh.AddTriangle(a + k, b + k + 1, a + k + 1);
                }
            }
        }
    }

    /// <summary>Flèche de position (pointe vers le haut du rectangle, encoche à l'arrière).</summary>
    public class HudArrow : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Color32 c = color;
            vh.AddVert(new Vector3(r.center.x, r.yMax), c, Vector2.zero);                     // pointe
            vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector2.zero);                         // arrière droit
            vh.AddVert(new Vector3(r.center.x, r.yMin + r.height * 0.28f), c, Vector2.zero);  // encoche
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector2.zero);                         // arrière gauche
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}
