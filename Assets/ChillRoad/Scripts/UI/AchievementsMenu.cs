using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Écran des succès, ouvert depuis le menu pause (tuile « Succès »), style verre sur l'image floutée du jeu : en tête,
    /// « SUCCÈS », une fine jauge dorée et le nombre obtenu ; dessous, un onglet texte par catégorie (souligné d'or quand
    /// il est choisi, obtenus / total) et la grille de ses succès (six au plus). Une carte obtenue est teintée d'or (pastille
    /// cochée, « DÉBLOQUÉ » et sa date) ; une carte à obtenir est en verre simple (anneau vide, jauge et valeur).
    /// ← → (croix, joystick), LB / RB, Tab ou clic pour changer d'onglet ; Échap / B pour revenir au menu pause.
    /// </summary>
    public class AchievementsMenu : MonoBehaviour
    {
        const float AreaW = 1920f, AreaH = 1080f, Margin = 180f;
        const float HeadY = -81f, TabsY = -156f, TabH = 51f, TabGap = 42f;
        const float GridY = -249f, CardW = 504f, CardH = 315f, CardGap = 24f, CardPad = 36f;

        public static bool IsOpen { get; private set; }
        /// <summary>Image où l'écran s'est ouvert ou fermé (la touche de fermeture ne doit pas rouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;
        static int lastTab;

        class Tab { public Button button; public RectTransform rt; public Text name; public Image line; public float on; }
        class Card
        {
            public RectTransform rt;
            public GlassRect plate, disc, ring;
            public HudIcon check;
            public Image barBack;
            public RectTransform barFill;
            public Text title, desc, state, value, reward;
        }

        GameObject root;
        RectTransform area, screenRt, barRt, backRt;
        Text titleText, countText, subText, hintText, backText;
        GlassRect backPlate;
        RectTransform totalFill;
        readonly List<Tab> tabs = new List<Tab>();
        readonly List<Card> cards = new List<Card>();
        int tab = -1, openedFrame;
        float cardAnim;
        System.Action onClose;

        public void Init()
        {
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

        /// <summary>Carte d'un succès : obtenue (verre doré, pastille cochée, date) ou à obtenir (verre, anneau vide, progression).</summary>
        void Fill(Card c, Achievements.Def d)
        {
            bool done = Achievements.IsUnlocked(d);
            c.plate.Set(done ? Glass.A(Glass.Gold, 0.035f) : Glass.Panel, done ? Glass.A(Glass.Gold, 0.7f) : Glass.Edge, 1.5f);
            c.disc.gameObject.SetActive(done);
            c.ring.gameObject.SetActive(!done);
            c.title.text = d.Name;
            c.title.color = done ? Glass.White : Glass.A(Glass.White, 0.88f);
            c.desc.text = d.Description;
            // Récompense : verte une fois versée, dorée et discrète à obtenir
            c.reward.text = d.Reward > 0 ? "+" + Wallet.Format(d.Reward) : "";
            c.reward.color = done ? Glass.Good : Glass.A(Glass.Gold, 0.75f);
            if (done)
            {
                var when = Achievements.UnlockDate(d);
                string state = when.HasValue
                    ? Loc.T("DÉBLOQUÉ LE ", "UNLOCKED ") + when.Value.ToString(Loc.French ? "dd/MM/yyyy" : "MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant()
                    : Loc.T("DÉBLOQUÉ", "UNLOCKED");
                c.state.text = Glass.Spaced(state, 1);
                c.state.color = Glass.Gold;
                c.value.text = "";
                c.barBack.gameObject.SetActive(false);
            }
            else if (d.Single)
            {
                c.state.text = Glass.Spaced(Loc.T("À OBTENIR", "LOCKED"), 1);
                c.state.color = Glass.Faint;
                c.value.text = "";
                c.barBack.gameObject.SetActive(false);
            }
            else
            {
                c.state.text = "";
                c.value.text = Achievements.ProgressText(d);
                c.barBack.gameObject.SetActive(true);
                // La jauge prend la place laissée par la valeur
                float vw = Mathf.Min(220f, c.value.preferredWidth);
                c.barBack.rectTransform.sizeDelta = new Vector2(Mathf.Max(60f, CardW - CardPad * 2f - vw - 21f), 3f);
                c.barFill.localScale = new Vector3(Mathf.Max(0.004f, Achievements.Progress01(d)), 1f, 1f);
            }
        }

        void Refresh(float dt)
        {
            float fit = screenRt.rect.width < 1f ? 1f : Mathf.Min(1f, Mathf.Min(screenRt.rect.width / AreaW, screenRt.rect.height / AreaH));
            area.localScale = Vector3.one * fit;

            // En-tête : titre, jauge entre le titre et le compte, compte (obtenus en or)
            int got = Achievements.UnlockedCount, total = Achievements.All.Length;
            countText.text = $"<color=#FFC233>{got}</color> / {total}";
            float left = Margin + titleText.preferredWidth + 36f, right = AreaW - Margin - countText.preferredWidth - 36f;
            barRt.anchoredPosition = new Vector2(left, HeadY);
            barRt.sizeDelta = new Vector2(Mathf.Max(40f, right - left), 3f);
            totalFill.localScale = new Vector3(Mathf.Max(0.004f, got / (float)total), 1f, 1f);
            string earned = Wallet.Format(Achievements.RewardsEarned);
            subText.text = Loc.F($"{Mathf.RoundToInt(100f * got / total)} %  ·  {earned} gagnés", $"{Mathf.RoundToInt(100f * got / total)}%  ·  {earned} earned");
            subText.rectTransform.anchoredPosition = new Vector2(left, HeadY - 14f);

            // Onglets : texte espacé, le choisi passe à l'or et se souligne ; posés à la suite selon leur largeur
            float x = Margin;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                var cat = (Achievements.Category)i;
                t.on = dt > 0f ? Mathf.MoveTowards(t.on, i == tab ? 1f : 0f, dt * 9f) : (i == tab ? 1f : 0f);
                float e = t.on * t.on * (3f - 2f * t.on);
                bool sel = i == tab;
                int n = Achievements.CountIn(cat, true), all = Achievements.CountIn(cat, false);
                string name = Glass.Spaced(Achievements.CategoryName(cat).ToUpperInvariant(), 1);
                string count = $"{n}/{all}";
                // Catégorie complète : compte en or ; sinon plus discret que le nom
                t.name.text = sel ? $"{name}  {count}" : n == all ? $"{name}  <color=#FFC233>{count}</color>" : $"{name}  <color=#FFFFFF73>{count}</color>";
                t.name.font = Glass.FontOf(sel ? Glass.Weight.Semibold : Glass.Weight.Regular);
                t.name.color = Color.Lerp(Glass.Dim, Glass.Gold, e);
                t.line.color = Glass.A(Glass.Gold, e);
                float w = t.name.preferredWidth + 4f;
                t.rt.anchoredPosition = new Vector2(x, TabsY);
                t.rt.sizeDelta = new Vector2(w, TabH);
                x += w + TabGap;
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

            // Retour : doré au survol de la souris
            var mouse = Mouse.current;
            bool hover = mouse != null && !GameInput.UsingGamepad && RectTransformUtility.RectangleContainsScreenPoint(backRt, mouse.position.ReadValue(), null);
            var fillColor = hover ? Glass.Gold : Glass.Pill;
            if (backPlate.color != fillColor) backPlate.Set(fillColor, hover ? Color.clear : Glass.Edge, 1.5f);
            backText.color = hover ? Glass.Ink : Glass.White;

            const string G = "<color=#FFC233>", E = "</color>";
            hintText.text = GameInput.UsingGamepad
                ? Loc.T($"{G}LB RB{E} / {G}← →{E}  catégorie     ·     {G}B{E}  retour", $"{G}LB RB{E} / {G}← →{E}  category     ·     {G}B{E}  back")
                : Loc.T($"{G}Tab{E} / {G}← →{E}  catégorie     ·     {G}Échap{E}  retour", $"{G}Tab{E} / {G}← →{E}  category     ·     {G}Esc{E}  back");
        }

        Vector2 CardPos(int k)
        {
            int col = k % 3, row = k / 3;
            return new Vector2(Margin + CardW * 0.5f + col * (CardW + CardGap), GridY - CardH * 0.5f - row * (CardH + CardGap));
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

            // Image du jeu floutée sous un voile (elle arrête aussi les clics vers le menu pause)
            MenuBackdrop.Attach(screenRt, 0.5f);

            // Zone de mise en page (1920 × 1080), centrée et réduite sur les écrans plus étroits
            area = Glass.Rect(screenRt, "Area", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            area.sizeDelta = new Vector2(AreaW, AreaH);

            // En-tête : SUCCÈS, jauge fine, compte
            titleText = Glass.Label(area, "", 57, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(Margin, HeadY + 36f), new Vector2(900f, 72f));
            Loc.Bind(titleText, Glass.Spaced("SUCCÈS", 3), Glass.Spaced("ACHIEVEMENTS", 3));
            // Le texte posé a son pivot en haut : on le recentre sur la ligne de l'en-tête
            titleText.rectTransform.pivot = new Vector2(0f, 0.5f);
            titleText.rectTransform.anchoredPosition = new Vector2(Margin, HeadY);
            countText = Glass.Label(area, "", 51, Glass.Weight.Light, Glass.White, TextAnchor.MiddleRight,
                new Vector2(0f, 1f), new Vector2(AreaW - Margin, HeadY), new Vector2(400f, 72f));
            countText.rectTransform.pivot = new Vector2(1f, 0.5f);
            countText.rectTransform.anchoredPosition = new Vector2(AreaW - Margin, HeadY);
            var bar = Glass.Bar(area, "Total", Glass.Track);
            barRt = bar.rectTransform;
            barRt.anchorMin = barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0f, 0.5f);
            totalFill = Glass.Bar(bar.transform, "Fill", Glass.Gold).rectTransform;
            Glass.Stretch(totalFill);
            totalFill.pivot = new Vector2(0f, 0.5f);
            subText = Glass.Label(area, "", 19, Glass.Weight.Semilight, Glass.Faint, TextAnchor.UpperLeft);
            var sr = subText.rectTransform;
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(0f, 1f);
            sr.sizeDelta = new Vector2(800f, 26f);

            // Onglets des catégories, sur une ligne fine
            var baseLine = Glass.Bar(area, "TabsLine", Glass.A(Color.white, 0.22f)).rectTransform;
            baseLine.anchorMin = baseLine.anchorMax = baseLine.pivot = new Vector2(0f, 1f);
            baseLine.anchoredPosition = new Vector2(Margin, TabsY - TabH);
            baseLine.sizeDelta = new Vector2(AreaW - Margin * 2f, 1.5f);
            for (int i = 0; i < Achievements.CategoryCount; i++) tabs.Add(BuildTab(i));
            for (int i = 0; i < tabs.Count; i++)
                tabs[i].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = tabs[(i - 1 + tabs.Count) % tabs.Count].button,
                    selectOnRight = tabs[(i + 1) % tabs.Count].button,
                };

            // Grille des succès (trois colonnes, deux rangées)
            for (int k = 0; k < 6; k++) cards.Add(BuildCard(k));

            // Retour (pilule, souris et doigt) en bas à gauche, aide des touches en bas à droite
            BuildBack();
            hintText = Glass.Label(area, "", 22, Glass.Weight.Light, Glass.White, TextAnchor.MiddleRight,
                new Vector2(1f, 0f), new Vector2(-Margin, 51f), new Vector2(1000f, 75f));
            root.SetActive(false);
        }

        Tab BuildTab(int i)
        {
            var t = new Tab();
            var go = new GameObject("Tab", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(area, false);
            t.rt = (RectTransform)go.transform;
            t.rt.anchorMin = t.rt.anchorMax = t.rt.pivot = new Vector2(0f, 1f);
            t.rt.sizeDelta = new Vector2(200f, TabH);
            var hit = go.GetComponent<Image>();
            hit.color = Color.clear;   // zone de clic seulement
            t.button = go.GetComponent<Button>();
            t.button.targetGraphic = hit;
            t.button.transition = Selectable.Transition.None;
            int index = i;
            t.button.onClick.AddListener(() => Select(index));
            t.name = Glass.Label(go.transform, "", 22, Glass.Weight.Regular, Glass.Dim, TextAnchor.MiddleLeft);
            var nr = t.name.rectTransform;
            nr.anchorMin = new Vector2(0f, 1f); nr.anchorMax = Vector2.one;
            nr.pivot = new Vector2(0f, 1f);
            nr.offsetMin = new Vector2(2f, -36f); nr.offsetMax = Vector2.zero;
            // Soulignement doré, posé sur la ligne des onglets
            t.line = Glass.Bar(go.transform, "Underline", Glass.A(Glass.Gold, 0f));
            var lr = t.line.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.offsetMin = new Vector2(0f, -1.5f); lr.offsetMax = new Vector2(0f, 3f);
            return t;
        }

        Card BuildCard(int k)
        {
            var c = new Card();
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(area, false);
            c.rt = (RectTransform)go.transform;
            c.rt.anchorMin = c.rt.anchorMax = new Vector2(0f, 1f);
            c.rt.pivot = new Vector2(0.5f, 0.5f);
            c.rt.sizeDelta = new Vector2(CardW, CardH);
            c.rt.anchoredPosition = CardPos(k);
            go.GetComponent<CanvasGroup>().blocksRaycasts = false;

            c.plate = Glass.Backdrop(c.rt, 39f);

            // Pastille dorée cochée (obtenu) ou anneau vide (à obtenir), en haut à gauche
            var top = new Vector2(0f, 1f);
            c.disc = Glass.Make(c.rt, "Done", 32f, Glass.Gold, 0f);
            Pose(c.disc.rectTransform, top, new Vector2(CardPad, -33f), new Vector2(63f, 63f));
            var ig = new GameObject("Check", typeof(RectTransform), typeof(CanvasRenderer), typeof(HudIcon));
            ig.transform.SetParent(c.disc.transform, false);
            Pose((RectTransform)ig.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            c.check = ig.GetComponent<HudIcon>();
            c.check.Shape = HudIcon.Kind.Check;
            c.check.raycastTarget = false;
            c.check.SetColors(Glass.Ink, Glass.Gold);
            c.ring = Glass.Make(c.rt, "Todo", 32f, Color.clear, 2.25f);
            c.ring.borderColor = Glass.A(Color.white, 0.6f);
            Pose(c.ring.rectTransform, top, new Vector2(CardPad, -33f), new Vector2(63f, 63f));

            // Récompense en haut à droite
            c.reward = Glass.Label(c.rt, "", 21, Glass.Weight.Regular, Glass.Good, TextAnchor.MiddleRight,
                new Vector2(1f, 1f), new Vector2(-CardPad, -48f), new Vector2(220f, 32f));

            c.title = Glass.Label(c.rt, "", 31, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft,
                top, new Vector2(CardPad, -114f), new Vector2(CardW - CardPad * 2f, 42f));
            c.title.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.title.resizeTextForBestFit = true;
            c.title.resizeTextMinSize = 20;
            c.title.resizeTextMaxSize = 31;
            c.desc = Glass.Label(c.rt, "", 22, Glass.Weight.Light, Glass.Dim, TextAnchor.UpperLeft,
                top, new Vector2(CardPad, -162f), new Vector2(CardW - CardPad * 2f, 70f));
            c.desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.desc.verticalOverflow = VerticalWrapMode.Truncate;

            // Bas de carte : état (débloqué le…, à obtenir) ou jauge fine et progression
            var low = new Vector2(0f, 0f);
            c.state = Glass.Label(c.rt, "", 17, Glass.Weight.Semibold, Glass.Gold, TextAnchor.MiddleLeft,
                low, new Vector2(CardPad, 30f), new Vector2(CardW - CardPad * 2f, 30f));
            c.barBack = Glass.Bar(c.rt, "Bar", Glass.Track);
            var br = c.barBack.rectTransform;
            br.anchorMin = br.anchorMax = Vector2.zero;
            br.pivot = new Vector2(0f, 0.5f);
            br.anchoredPosition = new Vector2(CardPad, 45f);
            br.sizeDelta = new Vector2(240f, 3f);
            c.barFill = Glass.Bar(c.barBack.transform, "Fill", Glass.White).rectTransform;
            Glass.Stretch(c.barFill);
            c.barFill.pivot = new Vector2(0f, 0.5f);
            c.value = Glass.Label(c.rt, "", 21, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleRight,
                new Vector2(1f, 0f), new Vector2(-CardPad, 30f), new Vector2(220f, 30f));
            c.value.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.value.resizeTextForBestFit = true;
            c.value.resizeTextMinSize = 14;
            c.value.resizeTextMaxSize = 21;
            return c;
        }

        void BuildBack()
        {
            var go = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer), typeof(GlassRect), typeof(Button));
            go.transform.SetParent(area, false);
            backRt = (RectTransform)go.transform;
            Pose(backRt, Vector2.zero, new Vector2(Margin, 51f), new Vector2(170f, 75f));
            backPlate = go.GetComponent<GlassRect>();
            backPlate.radius = 37.5f;
            backPlate.color = Glass.Pill;
            var b = go.GetComponent<Button>();
            b.targetGraphic = backPlate;
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(Close);
            backText = Glass.Label(go.transform, "", 24, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleCenter);
            Glass.Stretch(backText.rectTransform);
            Loc.Bind(backText, "Retour", "Back");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Ancre et pivot au même point, position et taille.</summary>
        static void Pose(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
