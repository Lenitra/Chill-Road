using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Écran des succès, ouvert depuis le menu pause (tuile « Succès ») : en tête, le trophée, le nombre de succès
    /// obtenus et une jauge ; dessous, un onglet par catégorie (pictogramme, nom, obtenus / total) et la grille de ses six
    /// succès. Chaque carte montre une médaille (dorée si obtenu, sombre sinon), le nom, l'objectif, puis la date
    /// d'obtention ou la progression (jauge et valeur).
    /// ← → (croix, joystick), LB / RB, Tab ou clic pour changer d'onglet ; Échap / B pour revenir au menu pause.
    /// </summary>
    public class AchievementsMenu : MonoBehaviour
    {
        static readonly Color White = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Gold = new Color(0.95f, 0.62f, 0.12f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color Muted = new Color(0.5f, 0.52f, 0.56f);
        static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        static readonly Color InkDim = new Color(0.06f, 0.07f, 0.09f, 0.7f);
        static readonly Color PanelPlate = new Color(0.025f, 0.03f, 0.045f, 0.9f);
        static readonly Color TabPlate = new Color(0.12f, 0.14f, 0.18f, 0.96f);
        static readonly Color CardLocked = new Color(0.075f, 0.085f, 0.11f, 0.95f);
        static readonly Color CardDone = new Color(0.14f, 0.12f, 0.08f, 0.96f);
        static readonly Color MedalLocked = new Color(0.16f, 0.18f, 0.22f);
        static readonly Color Money = new Color(0.5f, 1f, 0.6f);

        const float Slope = 0.14f;
        const float AreaW = 1760f, AreaH = 860f;
        const float TabW = 316f, TabH = 66f, TabGap = 14f;
        const float CardW = 560f, CardH = 250f, CardGap = 24f;

        public static bool IsOpen { get; private set; }
        /// <summary>Image où l'écran s'est ouvert ou fermé (la touche de fermeture ne doit pas rouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;
        static int lastTab;

        class Tab { public Button button; public HudShape plate; public HudIcon icon; public Text name, count; public float on; }
        class Card
        {
            public RectTransform rt;
            public HudShape plate, edge, barBack, barFill;
            public Image medal;
            public HudIcon icon;
            public Text title, desc, state, value, reward;
        }

        Font font;
        GameObject root;
        RectTransform area, screenRt;
        Text countText, hintText;
        RectTransform totalFill;
        readonly List<Tab> tabs = new List<Tab>();
        readonly List<Card> cards = new List<Card>();
        int tab = -1, openedFrame;
        float cardAnim;
        System.Action onClose;

        public void Init()
        {
            font = HudArt.Font;
            Build();
        }

        void OnDestroy()
        {
            IsOpen = false;
            if (root) Destroy(root);
        }

        /// <summary>Ouvre l'écran ; onClosed est appelé au retour (menu pause).</summary>
        public void Open(System.Action onClosed)
        {
            if (IsOpen) return;
            onClose = onClosed;
            IsOpen = true;
            root.SetActive(true);
            openedFrame = ChangedFrame = Time.frameCount;
            tab = -1;
            foreach (var t in tabs) t.on = 0f;
            ShowTab(lastTab);
            EventSystem.current?.SetSelectedGameObject(tabs[lastTab].button.gameObject);
            Refresh(0f);
        }

        void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ChangedFrame = Time.frameCount;
            root.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            var cb = onClose;
            onClose = null;
            cb?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (Time.frameCount != openedFrame && (GameInput.CancelPressed || GameInput.PausePressed)) { Close(); return; }
            if (GameInput.TabNextPressed) Select(tab + 1);
            else if (GameInput.TabPrevPressed) Select(tab - 1);
            // L'onglet sélectionné (manette, clavier, clic) est celui affiché
            var es = EventSystem.current;
            if (es)
            {
                int selected = -1;
                for (int i = 0; i < tabs.Count; i++)
                    if (es.currentSelectedGameObject == tabs[i].button.gameObject) selected = i;
                if (selected < 0) es.SetSelectedGameObject(tabs[Mathf.Clamp(tab, 0, tabs.Count - 1)].button.gameObject);
                else if (selected != tab) ShowTab(selected);
            }
            Refresh(dt);
        }

        void Select(int i)
        {
            int n = tabs.Count;
            i = (i % n + n) % n;
            EventSystem.current?.SetSelectedGameObject(tabs[i].button.gameObject);
            ShowTab(i);
        }

        void ShowTab(int i)
        {
            if (i == tab) return;
            tab = lastTab = Mathf.Clamp(i, 0, tabs.Count - 1);
            cardAnim = 0f;
            var cat = (Achievements.Category)tab;
            var list = new List<Achievements.Def>();
            foreach (var d in Achievements.All) if (d.category == cat) list.Add(d);
            for (int k = 0; k < cards.Count; k++)
            {
                var c = cards[k];
                bool used = k < list.Count;
                c.rt.gameObject.SetActive(used);
                if (used) Fill(c, list[k]);
            }
        }

        /// <summary>Carte d'un succès : obtenu (médaille dorée, date) ou à obtenir (médaille sombre, progression).</summary>
        void Fill(Card c, Achievements.Def d)
        {
            bool done = Achievements.IsUnlocked(d);
            c.plate.color = done ? CardDone : CardLocked;
            c.edge.color = done ? Accent : new Color(1f, 1f, 1f, 0.08f);
            c.medal.color = done ? Gold : MedalLocked;
            c.icon.Shape = done ? Achievements.CategoryIcon(d.category) : HudIcon.Kind.Lock;
            c.icon.SetColors(done ? Ink : Muted, done ? Gold : MedalLocked);
            c.title.text = d.Name;
            c.title.color = done ? White : new Color(0.85f, 0.87f, 0.9f);
            c.desc.text = d.Description;
            // Récompense : verte une fois versée, dorée et discrète à obtenir
            c.reward.text = d.Reward > 0 ? "+" + Wallet.Format(d.Reward) : "";
            c.reward.color = done ? Money : new Color(Accent.r, Accent.g, Accent.b, 0.75f);
            if (done)
            {
                var when = Achievements.UnlockDate(d);
                c.state.text = when.HasValue
                    ? Loc.T("OBTENU LE ", "UNLOCKED ") + when.Value.ToString(Loc.French ? "dd/MM/yyyy" : "MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
                    : Loc.T("OBTENU", "UNLOCKED");
                c.state.color = Accent;
                c.value.text = "";
                c.barBack.gameObject.SetActive(false);
            }
            else if (d.Single)
            {
                c.state.text = Loc.T("À OBTENIR", "LOCKED");
                c.state.color = Muted;
                c.value.text = "";
                c.barBack.gameObject.SetActive(false);
            }
            else
            {
                c.state.text = "";
                c.value.text = Achievements.ProgressText(d);
                c.barBack.gameObject.SetActive(true);
                c.barFill.rectTransform.localScale = new Vector3(Mathf.Max(0.004f, Achievements.Progress01(d)), 1f, 1f);
            }
        }

        void Refresh(float dt)
        {
            float fit = screenRt.rect.width < 1f ? 1f : Mathf.Min(1f, Mathf.Min(screenRt.rect.width / AreaW, screenRt.rect.height / AreaH));
            area.localScale = Vector3.one * fit;

            int got = Achievements.UnlockedCount, total = Achievements.All.Length;
            string earned = Wallet.Format(Achievements.RewardsEarned);
            countText.text = Loc.F($"{got} / {total} succès obtenus  ·  {Mathf.RoundToInt(100f * got / total)} %  ·  {earned} gagnés",
                $"{got} / {total} achievements unlocked  ·  {Mathf.RoundToInt(100f * got / total)}%  ·  {earned} earned");
            totalFill.localScale = new Vector3(Mathf.Max(0.004f, got / (float)total), 1f, 1f);

            // Onglets : le choisi passe à l'or (textes à l'encre)
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                var cat = (Achievements.Category)i;
                t.on = dt > 0f ? Mathf.MoveTowards(t.on, i == tab ? 1f : 0f, dt * 9f) : (i == tab ? 1f : 0f);
                float e = t.on * t.on * (3f - 2f * t.on);
                var plate = Color.Lerp(TabPlate, Gold, e);
                t.plate.color = plate;
                plate.a = 1f;
                t.icon.SetColors(Color.Lerp(Accent, Ink, e), plate);
                t.name.text = Achievements.CategoryName(cat);
                t.name.color = Color.Lerp(White, Ink, e);
                int n = Achievements.CountIn(cat, true), all = Achievements.CountIn(cat, false);
                t.count.text = $"{n} / {all}";
                t.count.color = n == all ? Color.Lerp(Accent, Ink, e) : Color.Lerp(Dim, InkDim, e);
                t.button.transform.localScale = Vector3.one * (1f + 0.03f * e);
            }

            // Cartes : arrivée en cascade à chaque onglet
            cardAnim += dt;
            for (int k = 0; k < cards.Count; k++)
            {
                float u = dt <= 0f ? 1f : Mathf.Clamp01((cardAnim - k * 0.04f) / 0.22f);
                float e = 1f - (1f - u) * (1f - u);
                var c = cards[k];
                c.rt.anchoredPosition = CardPos(k) + new Vector2(0f, (1f - e) * -26f);
                c.rt.GetComponent<CanvasGroup>().alpha = e;
            }

            bool pad = GameInput.UsingGamepad;
            hintText.text = pad
                ? Loc.T("LB RB / ← → catégorie  ·  B retour", "LB RB / ← → category  ·  B back")
                : Loc.T("Tab / ← → catégorie  ·  Échap retour", "Tab / ← → category  ·  Esc back");
        }

        Vector2 CardPos(int k)
        {
            int col = k % 3, row = k / 3;
            float x0 = -(CardW * 3f + CardGap * 2f) * 0.5f + CardW * 0.5f;
            return new Vector2(x0 + col * (CardW + CardGap), -296f - row * (CardH + CardGap) - CardH * 0.5f);
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            root = new GameObject("AchievementsMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            screenRt = (RectTransform)root.transform;

            var veil = Rect(screenRt, "Veil", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var vi = veil.gameObject.AddComponent<Image>();
            vi.color = new Color(0.01f, 0.012f, 0.02f, 0.86f);

            // Zone de mise en page (1760 × 860), centrée et réduite sur les petits écrans
            area = Rect(screenRt, "Area", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            area.sizeDelta = new Vector2(AreaW, AreaH);

            // Enseigne : trophée, titre, nombre obtenu et jauge
            const float headH = 140f;
            var head = Panel(area, "Head", new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(780f, headH));
            Shape(head, "Stripe", Vector2.zero, Vector2.zero, new Vector2(4f, headH), new Color(Info.r, Info.g, Info.b, 0.7f)).Set(headH * Slope, 0f, 0f);
            var icon = Icon(head, HudIcon.Kind.Trophy, new Vector2(0f, 1f), new Vector2(40f + Lean(headH - 50f, headH), -50f), 70f);
            icon.SetColors(Accent, PanelPlate);
            var title = Place(Label(head, 50, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), new Vector2(124f, -12f), new Vector2(600f, 64f), headH);
            Loc.Bind(title, "SUCCÈS", "ACHIEVEMENTS");
            countText = Place(Label(head, 22, TextAnchor.MiddleLeft, Dim, FontStyle.Bold), new Vector2(126f, -78f), new Vector2(600f, 30f), headH);
            float lean = Lean(22f, headH);
            var bar = Rect(head, "Total", Vector2.zero, Vector2.zero, new Vector2(128f + lean, 18f), new Vector2(700f + lean, 26f));
            bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            totalFill = Rect(bar, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            totalFill.pivot = new Vector2(0f, 0.5f);
            totalFill.gameObject.AddComponent<Image>().color = Accent;

            // Retour (souris, doigt) et aide des touches, en haut à droite
            var back = BackButton(area);
            hintText = Label(area, 19, TextAnchor.MiddleRight, Dim, FontStyle.Bold);
            var hr = hintText.rectTransform;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1f, 1f);
            hr.sizeDelta = new Vector2(800f, 30f);
            hr.anchoredPosition = new Vector2(-4f, -82f);

            // Onglets des catégories
            float tabsW = TabW * Achievements.CategoryCount + TabGap * (Achievements.CategoryCount - 1);
            for (int i = 0; i < Achievements.CategoryCount; i++)
                tabs.Add(BuildTab(i, new Vector2(-tabsW * 0.5f + TabW * 0.5f + i * (TabW + TabGap), -206f)));
            for (int i = 0; i < tabs.Count; i++)
                tabs[i].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = tabs[(i - 1 + tabs.Count) % tabs.Count].button,
                    selectOnRight = tabs[(i + 1) % tabs.Count].button,
                };

            // Grille des succès (trois colonnes, deux rangées)
            for (int k = 0; k < 6; k++) cards.Add(BuildCard(k));
            root.SetActive(false);
        }

        Tab BuildTab(int i, Vector2 pos)
        {
            var t = new Tab();
            var go = new GameObject("Tab", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button));
            go.transform.SetParent(area, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(TabW, TabH);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            t.plate = go.GetComponent<HudShape>();
            t.plate.Set(TabH * Slope, 0f, 0f);
            t.plate.color = TabPlate;
            t.button = go.GetComponent<Button>();
            t.button.targetGraphic = t.plate;
            t.button.transition = Selectable.Transition.None;
            int index = i;
            t.button.onClick.AddListener(() => Select(index));
            t.icon = Icon(go.transform, Achievements.CategoryIcon((Achievements.Category)i), new Vector2(0f, 0.5f), new Vector2(42f, 0f), 38f);
            t.name = Label(go.transform, 24, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic);
            Fit(t.name.rectTransform, Vector2.zero, Vector2.one, new Vector2(76f, 0f), new Vector2(-80f, 0f));
            t.count = Label(go.transform, 20, TextAnchor.MiddleRight, Dim, FontStyle.Bold);
            Fit(t.count.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(-24f, 0f));
            t.name.GetComponent<Shadow>().enabled = t.count.GetComponent<Shadow>().enabled = false;
            return t;
        }

        Card BuildCard(int k)
        {
            var c = new Card();
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(area, false);
            c.rt = (RectTransform)go.transform;
            c.rt.anchorMin = c.rt.anchorMax = new Vector2(0.5f, 1f);
            c.rt.pivot = new Vector2(0.5f, 0.5f);
            c.rt.sizeDelta = new Vector2(CardW, CardH);
            c.rt.anchoredPosition = CardPos(k);
            go.GetComponent<CanvasGroup>().blocksRaycasts = false;

            c.plate = Shape(c.rt, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH), CardLocked);
            c.plate.Set(CardH * Slope, 0f, 0f);
            c.edge = Shape(c.rt, "Edge", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, CardH), Accent);
            c.edge.Set(CardH * Slope, 0f, 0f);

            // Médaille en haut à gauche
            float my = -78f, mx = 46f + Lean(CardH + my, CardH) + 44f;
            var medalGo = new GameObject("Medal", typeof(RectTransform), typeof(Image));
            medalGo.transform.SetParent(c.rt, false);
            var mr = (RectTransform)medalGo.transform;
            mr.anchorMin = mr.anchorMax = new Vector2(0f, 1f);
            mr.sizeDelta = new Vector2(96f, 96f);
            mr.anchoredPosition = new Vector2(mx, my);
            c.medal = medalGo.GetComponent<Image>();
            c.medal.sprite = HudArt.Disc;
            c.medal.raycastTarget = false;
            c.icon = Icon(mr, HudIcon.Kind.Lock, new Vector2(0.5f, 0.5f), Vector2.zero, 56f);
            // Récompense sous la médaille
            c.reward = Label(c.rt, 19, TextAnchor.MiddleCenter, Money, FontStyle.BoldAndItalic);
            var rr = c.reward.rectTransform;
            rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(130f, 26f);
            rr.anchoredPosition = new Vector2(mx - 3f, my - 68f);
            c.reward.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.reward.resizeTextForBestFit = true;
            c.reward.resizeTextMinSize = 13;
            c.reward.resizeTextMaxSize = 19;

            c.title = Place(Label(c.rt, 29, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic), new Vector2(166f, -34f), new Vector2(CardW - 204f, 44f), CardH);
            c.title.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.title.resizeTextForBestFit = true;
            c.title.resizeTextMinSize = 18;
            c.title.resizeTextMaxSize = 29;
            c.desc = Place(Label(c.rt, 20, TextAnchor.UpperLeft, Dim, FontStyle.Bold), new Vector2(166f, -84f), new Vector2(CardW - 212f, 84f), CardH);
            c.desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.desc.verticalOverflow = VerticalWrapMode.Truncate;

            // Bas de carte : état (obtenu le…, à obtenir) ou jauge et progression
            c.state = Low(Label(c.rt, 21, TextAnchor.MiddleLeft, Accent, FontStyle.BoldAndItalic), 40f, 44f, CardW - 100f, 30f, CardH);
            float bl = Lean(44f, CardH);
            var back = Shape(c.rt, "Bar", Vector2.zero, new Vector2(40f + bl, 38f), new Vector2(CardW - 280f, 12f), new Color(1f, 1f, 1f, 0.12f));
            back.rectTransform.pivot = new Vector2(0f, 0f);
            back.Set(12f * Slope, 0f, 0f);
            c.barBack = back;
            c.barFill = Shape(back.rectTransform, "Fill", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(CardW - 280f, 12f), Info);
            c.barFill.Set(12f * Slope, 0f, 0f);
            c.value = Low(Label(c.rt, 21, TextAnchor.MiddleRight, Accent, FontStyle.Bold), CardW - 236f, 44f, 196f, 30f, CardH);
            c.value.resizeTextForBestFit = true;
            c.value.resizeTextMinSize = 14;
            c.value.resizeTextMaxSize = 21;
            c.value.horizontalOverflow = HorizontalWrapMode.Wrap;
            return c;
        }

        RectTransform BackButton(Transform parent)
        {
            var go = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(230f, 60f);
            rt.anchoredPosition = new Vector2(0f, -6f);
            var shape = go.GetComponent<HudShape>();
            shape.Set(60f * Slope, 0f, 0f);
            shape.color = Color.white;
            var b = go.GetComponent<Button>();
            b.targetGraphic = shape;
            var colors = b.colors;
            colors.normalColor = new Color(0.14f, 0.16f, 0.2f, 0.92f);
            colors.highlightedColor = new Color(0.2f, 0.42f, 0.55f, 1f);
            colors.selectedColor = new Color(0.14f, 0.16f, 0.2f, 0.92f);
            colors.pressedColor = Info;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(Close);
            var icon = Icon(rt, HudIcon.Kind.Back, new Vector2(0f, 0.5f), new Vector2(46f, 0f), 30f);
            icon.SetColors(White, Color.clear);
            var label = Label(rt, 26, TextAnchor.MiddleLeft, White, FontStyle.BoldAndItalic);
            Fit(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(78f, 0f), new Vector2(-16f, 0f));
            Loc.Bind(label, "Retour", "Back");
            return rt;
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Panel(Transform parent, string name, Vector2 corner, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, corner, corner, Vector2.zero, Vector2.zero);
            rt.pivot = corner;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var s = Shape(rt, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PanelPlate);
            s.Set(size.y * Slope, 0f, 0f);
            var pr = s.rectTransform;
            pr.anchorMin = Vector2.zero;
            pr.anchorMax = Vector2.one;
            pr.offsetMin = pr.offsetMax = Vector2.zero;
            pr.SetAsFirstSibling();
            return rt;
        }

        /// <summary>Décalage horizontal du bord penché d'un panneau à la hauteur y (depuis le bas) : 0 au milieu.</summary>
        static float Lean(float y, float panelHeight) => Slope * (y - panelHeight * 0.5f);

        static HudIcon Icon(Transform parent, HudIcon.Kind kind, Vector2 anchor, Vector2 pos, float size)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            var icon = go.GetComponent<HudIcon>();
            icon.Shape = kind;
            icon.raycastTarget = false;
            return icon;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static void Fit(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = oMin; rt.offsetMax = oMax;
        }

        /// <summary>Texte posé depuis le haut à gauche d'un panneau de hauteur panelHeight : sa marge suit le bord penché.</summary>
        static Text Place(Text t, Vector2 pos, Vector2 size, float panelHeight)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = size;
            float lean = Lean(panelHeight + pos.y - size.y * 0.5f, panelHeight);
            rt.anchoredPosition = pos + new Vector2(lean, -size.y * 0.5f);
            return t;
        }

        /// <summary>Texte posé depuis le bas à gauche d'un panneau (y : milieu de la ligne) ; sa marge suit le bord penché.</summary>
        static Text Low(Text t, float x, float y, float width, float height, float panelHeight)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x + Lean(y, panelHeight), y);
            return t;
        }

        static HudShape Shape(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
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

        Text Label(Transform parent, int size, TextAnchor align, Color color, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.color = color;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.55f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }
    }
}
