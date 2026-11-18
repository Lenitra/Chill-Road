using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Photo du radar : au flash, une caméra placée à la lampe du radar photographie la voiture du joueur, et le cliché
    /// s'affiche un court instant en haut à droite du HUD, en polaroid (il se révèle puis repart), avec la vitesse relevée.
    /// Pendant une course, il se place sous le panneau de la course.
    /// </summary>
    public class RadarPolaroid : MonoBehaviour
    {
        const int PhotoW = 512, PhotoH = 420;
        const float Width = 236f, Height = 276f, Margin = 26f, RaceDrop = 150f;
        const float InTime = 0.28f, DevelopTime = 0.9f, HoldTime = 3.6f, OutTime = 0.32f;
        static readonly Vector2 Hidden = new Vector2(Width + 90f, 26f);
        static readonly Color Paper = new Color(0.96f, 0.95f, 0.91f), Ink = new Color(0.1f, 0.1f, 0.13f);

        GameManager game;
        RectTransform root;
        CanvasGroup group;
        RawImage photo;
        Image veil;
        Text speedText, nameText;
        RenderTexture target;
        Camera cam;
        Light lamp;
        float age = -1f, tilt;

        public void Init(GameManager g, RectTransform parent)
        {
            game = g;
            root = Node(parent, "RadarPolaroid", new Vector2(1f, 1f), Vector2.zero, new Vector2(Width, Height));
            root.pivot = new Vector2(1f, 1f);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;

            var shadow = Node(root, "Shadow", new Vector2(0.5f, 0.5f), new Vector2(5f, -7f), new Vector2(Width + 6f, Height + 6f));
            Graphic<Image>(shadow).color = new Color(0f, 0f, 0f, 0.38f);
            var paper = Node(root, "Paper", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, Height));
            Graphic<Image>(paper).color = Paper;

            float w = Width - 24f, h = w * PhotoH / (float)PhotoW;
            var pic = Node(root, "Photo", new Vector2(0.5f, 1f), new Vector2(0f, -12f - h * 0.5f), new Vector2(w, h));
            photo = Graphic<RawImage>(pic);
            veil = Graphic<Image>(Node(pic, "Develop", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h)));

            speedText = Label("Speed", 30, FontStyle.BoldAndItalic, -h - 30f, 36f, Ink);
            nameText = Label("Name", 15, FontStyle.Normal, -h - 58f, 22f, new Color(Ink.r, Ink.g, Ink.b, 0.62f));
            root.gameObject.SetActive(false);
        }

        static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static T Graphic<T>(RectTransform rt) where T : Graphic
        {
            rt.gameObject.AddComponent<CanvasRenderer>();
            var g = rt.gameObject.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        Text Label(string name, int size, FontStyle style, float y, float h, Color color)
        {
            var t = Graphic<Text>(Node(root, name, new Vector2(0.5f, 1f), new Vector2(0f, y - h * 0.5f + 6f), new Vector2(Width - 24f, h)));
            t.font = HudArt.Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.color = color;
            return t;
        }

        /// <summary>Prend la photo depuis la lampe du radar et lance l'affichage.</summary>
        public void Snap(Vector3 lampPos, string radarName, float kmh)
        {
            var car = game ? game.Player : null;
            if (!car || !car.Model || GameManager.DedicatedServer) return;
            Shoot(lampPos, car);
            speedText.text = Loc.F($"{Mathf.Floor(kmh):0} km/h");
            nameText.text = radarName;
            tilt = Random.value < 0.5f ? -3.2f : 2.6f;
            age = 0f;
            root.gameObject.SetActive(true);
            Animate();
        }

        void Shoot(Vector3 lampPos, VehicleController car)
        {
            var main = Camera.main;
            if (!main) return;
            if (!target)
            {
                target = new RenderTexture(PhotoW, PhotoH, 24, RenderTextureFormat.ARGB32) { name = "RadarPhoto", antiAliasing = 2 };
                photo.texture = target;
            }
            if (!cam)
            {
                var go = new GameObject("RadarPhotoCamera");
                go.transform.SetParent(transform, false);
                cam = go.AddComponent<Camera>();
                cam.enabled = false;
                var extra = go.AddComponent<UniversalAdditionalCameraData>();
                extra.renderPostProcessing = true;
                extra.renderShadows = true;
                lamp = new GameObject("RadarPhotoFlash").AddComponent<Light>();
                lamp.transform.SetParent(go.transform, false);
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.98f, 0.94f);
                lamp.range = 45f;
                lamp.intensity = 28f;
                lamp.shadows = LightShadows.None;
                lamp.enabled = false;
            }
            cam.cullingMask = main.cullingMask;
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.farClipPlane = 1200f;
            cam.nearClipPlane = 0.2f;

            // Vue depuis la lampe, décalée vers la voiture pour sortir du boîtier et du mât du radar
            var b = car.LocalBounds;
            Vector3 look = car.transform.TransformPoint(b.center);
            Vector3 eye = lampPos + (look - lampPos).normalized * 1.6f;
            float dist = Mathf.Max(1.5f, Vector3.Distance(eye, look));
            float reach = Mathf.Max(b.extents.z, b.extents.x) * 1.2f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(reach / dist) * Mathf.Rad2Deg, 7f, 60f);
            cam.targetTexture = target;
            cam.aspect = PhotoW / (float)PhotoH;
            lamp.enabled = true;
            cam.Render();
            lamp.enabled = false;
        }

        void Update()
        {
            if (age < 0f) return;
            age += Time.unscaledDeltaTime;
            Animate();
        }

        void Animate()
        {
            float total = InTime + HoldTime + OutTime;
            if (age >= total)
            {
                age = -1f;
                root.gameObject.SetActive(false);
                return;
            }
            float show = Mathf.Clamp01(age / InTime), hide = Mathf.Clamp01((age - InTime - HoldTime) / OutTime);
            float ease = 1f - (1f - show) * (1f - show) * (1f - show);
            float drop = game.Races && game.Races.Active != null ? RaceDrop : 0f;
            Vector2 rest = new Vector2(-Margin, -Margin - drop);
            root.anchoredPosition = rest + Hidden * ((1f - ease) + hide * hide);
            root.localRotation = Quaternion.Euler(0f, 0f, tilt + (1f - ease) * -9f + hide * 7f);
            group.alpha = Mathf.Clamp01(show * 3f) * (1f - hide);
            // Le cliché se révèle : voile laiteux qui se dissipe
            float d = Mathf.Clamp01(age / DevelopTime);
            veil.color = new Color(0.98f, 0.97f, 0.93f, (1f - d) * (1f - d));
        }

        void OnDestroy()
        {
            if (target) { target.Release(); Destroy(target); }
        }
    }
}
