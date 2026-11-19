using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Notification d'un succès obtenu, en bas à droite (à gauche du compte-tours) : plaque inclinée qui glisse depuis le
    /// bord, médaille dorée au trophée qui claque, « SUCCÈS DÉBLOQUÉ », nom et description, liseré doré qui se vide
    /// pendant l'affichage, et la récompense versée en vert (« +5 000 $ »). Un carillon (arpège de cloches, synthétisé ici) l'accompagne. Les succès obtenus coup sur coup
    /// passent l'un après l'autre. Son propre canevas, au-dessus des menus (achat à la concession, garage…).
    /// </summary>
    public class AchievementToast : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Gold = new Color(0.95f, 0.62f, 0.12f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.62f);
        static readonly Color Plate = new Color(0.03f, 0.04f, 0.06f, 0.94f);
        static readonly Color Money = new Color(0.5f, 1f, 0.6f);

        const float W = 500f, H = 116f, Slope = 0.14f;
        const float ShowTime = 5f, InTime = 0.45f, OutTime = 0.35f;
        static readonly Vector2 Home = new Vector2(-352f, 84f);          // à gauche du compte-tours, au-dessus de la ligne d'aide
        static readonly Vector2 TouchHome = new Vector2(-300f, 380f);    // commandes tactiles : au-dessus des pédales

        Font font;
        RectTransform panel;
        CanvasGroup group;
        HudShape stripe;
        Image glow;
        RectTransform medal;
        Text kicker, title, detail, reward;
        AudioSource source;
        AudioClip chime;
        /// <summary>Notification en attente : textes déjà dans la langue en cours, récompense ($).</summary>
        struct Entry { public string kicker, title, detail; public int reward; }
        readonly Queue<Entry> queue = new Queue<Entry>();
        float t = -1f;

        public void Init()
        {
            font = HudArt.Font;
            Build();
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.priority = 16;
            chime = MakeChime();
            Achievements.Unlocked += Enqueue;
            Achievements.BackPaid += OnBackPaid;
        }

        void OnDestroy()
        {
            Achievements.Unlocked -= Enqueue;
            Achievements.BackPaid -= OnBackPaid;
            if (panel) Destroy(panel.root.gameObject);
            if (chime) Destroy(chime);
        }

        void Enqueue(Achievements.Def d) => queue.Enqueue(new Entry
        {
            kicker = Loc.T("SUCCÈS DÉBLOQUÉ", "ACHIEVEMENT UNLOCKED"),
            title = d.Name,
            detail = d.Description,
            reward = d.Reward,
        });

        /// <summary>Récompenses des succès obtenus avant leur création : un récapitulatif.</summary>
        void OnBackPaid(int count, int cash) => queue.Enqueue(new Entry
        {
            kicker = Loc.T("RÉCOMPENSES DES SUCCÈS", "ACHIEVEMENT REWARDS"),
            title = count > 1 ? Loc.F($"{count} succès déjà obtenus", $"{count} achievements already unlocked")
                : Loc.T("Un succès déjà obtenu", "One achievement already unlocked"),
            detail = Loc.T("Récompenses versées au portefeuille.", "Rewards paid into your wallet."),
            reward = cash,
        });

        /// <summary>Montre une notification (essai, capture) sans débloquer de succès.</summary>
        public void Preview(Achievements.Def d) => Enqueue(d);

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (t < 0f)
            {
                if (queue.Count == 0) return;
                Show(queue.Dequeue());
            }
            t += dt;
            float total = InTime + ShowTime + OutTime;
            // Entrée : glisse depuis la droite avec un léger dépassement ; sortie : glisse vers le bas en s'effaçant
            float x, y = 0f, a;
            if (t < InTime)
            {
                float u = t / InTime, back = 1.7f;
                float e = 1f + (back + 1f) * Mathf.Pow(u - 1f, 3f) + back * Mathf.Pow(u - 1f, 2f);
                x = (1f - e) * (W + 380f);
                a = Mathf.Clamp01(u * 3f);
            }
            else if (t < InTime + ShowTime) { x = 0f; a = 1f; }
            else
            {
                float u = Mathf.Clamp01((t - InTime - ShowTime) / OutTime);
                x = 0f; y = -u * u * 40f; a = 1f - u;
            }
            panel.anchoredPosition = (TouchInput.Showing ? TouchHome : Home) + new Vector2(x, y);
            group.alpha = a;

            // Médaille : claque à l'arrivée ; halo qui s'éteint
            float m = t - InTime * 0.6f;
            float punch = m < 0f ? 0f : m < 0.5f ? Mathf.Sin(m / 0.5f * Mathf.PI) * (1f - m / 0.5f) * 0.6f : 0f;
            medal.localScale = Vector3.one * (m < 0f ? 0.6f : 1f + punch);
            var gc = glow.color; gc.a = m < 0f ? 0f : Mathf.Clamp01(1f - m / 1.2f) * 0.55f; glow.color = gc;
            // Liseré qui se vide pendant l'affichage
            float left = 1f - Mathf.Clamp01((t - InTime) / ShowTime);
            stripe.rectTransform.localScale = new Vector3(left, 1f, 1f);

            if (t >= total) { t = -1f; group.alpha = 0f; }
        }

        void Show(Entry e)
        {
            t = 0f;
            kicker.text = e.kicker;
            title.text = e.title;
            detail.text = e.detail;
            reward.text = e.reward > 0 ? "+" + Wallet.Format(e.reward) : "";
            if (chime && source) source.PlayOneShot(chime, Mathf.Clamp01(GameSettings.UiVolume) * 0.75f);
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            var root = new GameObject("AchievementToast", typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(root.transform, false);
            panel = (RectTransform)go.transform;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
            panel.sizeDelta = new Vector2(W, H);
            panel.anchoredPosition = Home;
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = group.interactable = false;

            var plate = Shape(panel, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H), Plate);
            plate.Set(H * Slope, 0f, 0f);
            // Liseré doré en bas (se vide de droite à gauche), filet doré sur le bord gauche
            stripe = Shape(panel, "Stripe", new Vector2(0f, 0f), new Vector2(H * Slope * -0.5f + 6f, 0f), new Vector2(W - 12f, 5f), Gold);
            stripe.Set(5f * Slope, 0f, 0f);
            stripe.rectTransform.pivot = new Vector2(0f, 0f);
            Shape(panel, "Edge", new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(5f, H), Accent).Set(H * Slope, 0f, 0f);

            // Médaille : halo, disque doré, trophée à l'encre
            var medalGo = new GameObject("Medal", typeof(RectTransform));
            medalGo.transform.SetParent(panel, false);
            medal = (RectTransform)medalGo.transform;
            medal.anchorMin = medal.anchorMax = new Vector2(0f, 0.5f);
            medal.sizeDelta = new Vector2(80f, 80f);
            medal.anchoredPosition = new Vector2(66f, 2f);
            var glowGo = new GameObject("Halo", typeof(RectTransform), typeof(Image));
            glowGo.transform.SetParent(medal, false);
            glow = glowGo.GetComponent<Image>();
            glow.sprite = HudArt.SoftDisc;
            glow.color = new Color(1f, 0.75f, 0.25f, 0f);
            glow.raycastTarget = false;
            var hr = (RectTransform)glowGo.transform;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f);
            hr.sizeDelta = new Vector2(170f, 170f);
            var disc = new GameObject("Disc", typeof(RectTransform), typeof(Image));
            disc.transform.SetParent(medal, false);
            var di = disc.GetComponent<Image>();
            di.sprite = HudArt.Disc;
            di.color = Gold;
            di.raycastTarget = false;
            var dr = (RectTransform)disc.transform;
            dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(76f, 76f);
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
            iconGo.transform.SetParent(medal, false);
            var icon = iconGo.GetComponent<HudIcon>();
            icon.Shape = HudIcon.Kind.Trophy;
            icon.raycastTarget = false;
            icon.SetColors(new Color(0.06f, 0.07f, 0.09f), Gold);
            ((RectTransform)iconGo.transform).sizeDelta = new Vector2(48f, 48f);

            kicker = Label(panel, 16, Accent, FontStyle.BoldAndItalic, new Vector2(124f, -14f), new Vector2(W - 150f, 22f));
            title = Label(panel, 28, White, FontStyle.BoldAndItalic, new Vector2(122f, -38f), new Vector2(W - 146f, 36f));
            detail = Label(panel, 17, Dim, FontStyle.Bold, new Vector2(124f, -76f), new Vector2(W - 150f, 32f));
            // Récompense : en vert, à droite de la ligne « succès débloqué »
            reward = Label(panel, 20, Money, FontStyle.BoldAndItalic, new Vector2(W - 196f, -12f), new Vector2(170f, 26f));
            reward.alignment = TextAnchor.MiddleRight;
            detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            detail.resizeTextForBestFit = true;
            detail.resizeTextMinSize = 12;
            detail.resizeTextMaxSize = 17;
            title.resizeTextForBestFit = true;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.resizeTextMinSize = 18;
            title.resizeTextMaxSize = 28;
        }

        HudShape Shape(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var s = go.GetComponent<HudShape>();
            s.color = color;
            s.raycastTarget = false;
            return s;
        }

        /// <summary>Texte posé depuis le haut à gauche ; sa marge suit le bord penché de la plaque.</summary>
        Text Label(Transform parent, int size, Color color, FontStyle style, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            float lean = Slope * ((H + pos.y - box.y * 0.5f) - H * 0.5f);
            rt.anchoredPosition = pos + new Vector2(lean, 0f);
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color; t.fontStyle = style; Glass.Adopt(t, style);
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.55f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        // ------------------------------------------------------------------ carillon

        /// <summary>
        /// Carillon du succès : arpège montant de cloches (sol, si, ré, sol aigu ; partiels inharmoniques qui s'éteignent
        /// vite), posé sur une note grave douce, et une pluie de petites étincelles aiguës à la fin.
        /// </summary>
        static AudioClip MakeChime()
        {
            const int rate = 44100;
            float length = 1.9f;
            int n = (int)(rate * length);
            var data = new float[n];
            float[] notes = { 783.99f, 987.77f, 1174.66f, 1567.98f };
            float[] starts = { 0f, 0.075f, 0.15f, 0.225f };
            for (int k = 0; k < notes.Length; k++)
            {
                bool last = k == notes.Length - 1;
                Bell(data, rate, starts[k], notes[k], last ? 0.55f : 0.42f, last ? 2.4f : 5.5f);
            }
            // Note grave tenue (sol), attaque douce
            Pad(data, rate, 0.02f, 392f, 0.16f, 1.6f);
            // Étincelles : quelques notes très aiguës, brèves, de plus en plus faibles
            var rng = new System.Random(7);
            float[] sparkle = { 3135.96f, 2637.02f, 3951.07f, 3135.96f, 4698.63f };
            for (int k = 0; k < sparkle.Length; k++)
                Bell(data, rate, 0.34f + k * 0.07f + (float)rng.NextDouble() * 0.02f, sparkle[k], 0.07f * (1f - k * 0.15f), 14f);

            float peak = 0f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = peak > 0f ? 0.85f / peak : 1f;
            for (int i = 0; i < n; i++)
            {
                // Fondu final pour ne pas couper net
                float tail = Mathf.Clamp01((n - i) / (rate * 0.15f));
                data[i] *= gain * tail;
            }
            var clip = AudioClip.Create("AchievementChime", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Bell(float[] data, int rate, float start, float f, float amp, float decay)
        {
            int s = (int)(start * rate);
            float[] ratios = { 1f, 2f, 2.76f, 5.4f };
            float[] gains = { 1f, 0.42f, 0.22f, 0.08f };
            float[] decays = { 1f, 1.6f, 2.4f, 4f };
            for (int i = s; i < data.Length; i++)
            {
                float t = (i - s) / (float)rate;
                float env = Mathf.Clamp01(t / 0.003f);
                float v = 0f;
                for (int p = 0; p < ratios.Length; p++)
                    v += gains[p] * Mathf.Exp(-t * decay * decays[p]) * Mathf.Sin(2f * Mathf.PI * f * ratios[p] * t);
                if (Mathf.Exp(-t * decay) < 0.0005f) break;
                data[i] += amp * env * v;
            }
        }

        static void Pad(float[] data, int rate, float start, float f, float amp, float decay)
        {
            int s = (int)(start * rate);
            for (int i = s; i < data.Length; i++)
            {
                float t = (i - s) / (float)rate;
                float env = Mathf.Clamp01(t / 0.06f) * Mathf.Exp(-t * decay);
                data[i] += amp * env * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2f * t) * Mathf.Exp(-t * 3f));
            }
        }
    }
}
