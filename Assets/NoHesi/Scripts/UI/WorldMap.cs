using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Carte globale : plein écran, ouverte par la touche « , » (ou Select à la manette) pendant la balade. Le jeu est
    /// en pause tant qu'elle est affichée (sauf en réseau : le monde continue, les autres joueurs s'y déplacent). On y voit toute la vallée (image générée avec la carte), les lieux-dits et
    /// la position de chaque joueur ; molette ou gâchettes pour zoomer, flèches / joystick / glisser pour se déplacer.
    /// GPS : un clic sur la carte (ou A / Entrée sur le viseur) y pose la destination, l'itinéraire le plus rapide
    /// s'affiche (<see cref="RouteGuide"/>) ; un clic sur le repère, Y ou Suppr le retire.
    /// </summary>
    public class WorldMap : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        /// <summary>Image où la carte s'est ouverte ou fermée (la même touche ne doit pas ouvrir la pause).</summary>
        public static int ChangedFrame { get; private set; } = -1;
        static WorldMap instance;

        /// <summary>Ouvre ou ferme la carte (bouton tactile, raccourci).</summary>
        public static void Toggle()
        {
            if (instance) instance.SetOpen(!IsOpen);
        }

        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color Info = new Color(0.35f, 0.85f, 1f);
        static readonly Color Tuning = new Color(1f, 0.42f, 0.82f);
        static readonly Color Plate = new Color(0.03f, 0.04f, 0.06f, 0.92f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.72f);
        const float ViewSize = 960f, MinZoom = 1f, MaxZoom = 7f;

        GameManager game;
        GameObject root;
        RectTransform content;
        readonly List<RectTransform> fixedSize = new List<RectTransform>();
        // Repères des joueurs : le premier est la voiture locale, les suivants ceux des autres joueurs (réseau)
        readonly List<RectTransform> playerMarks = new List<RectTransform>();
        readonly List<Text> markArrows = new List<Text>(), markNames = new List<Text>();
        Text placeText, hintText, destName, destDetail;
        // GPS : fenêtre de la carte, repère de destination, tracé de l'itinéraire, viseur (manette, clavier)
        RectTransform view, pin, cross;
        Text pinLabel;
        RouteLine mapRoute;
        int routeVersion = -1;
        float routeZoom;
        // Courses : parcours de chaque tracé et étiquette de son départ (nom, genre, longueur, record)
        readonly List<RouteLine> raceLines = new List<RouteLine>();
        readonly List<Text> raceLabels = new List<Text>(), raceRows = new List<Text>();
        // Seuls les départs sont affichés ; le tracé (et l'arrivée d'un sprint) n'apparaît qu'au survol du départ
        readonly List<RectTransform> raceStarts = new List<RectTransform>(), raceFinishes = new List<RectTransform>();
        int raceHover = -1;
        float raceZoom = -1f;
        /// <summary>Distance au départ (unités d'écran) sous laquelle le curseur ou le viseur le survole.</summary>
        const float RaceHoverRadius = 18f;
        bool pressed;
        float pressMoved;
        float zoom = 1.12f;
        Vector2 center;          // point de la carte (m) au centre de la vue
        bool follow = true;
        int openedFrame;
        float builtAspect;

        public void Init(GameManager g)
        {
            game = g;
            instance = this;
        }

        void OnDestroy()
        {
            if (instance == this) { instance = null; IsOpen = false; }
        }

        void Update()
        {
            if (!game || game.Map == null) return;
            if (!IsOpen)
            {
                // Seulement en roulant : ni au menu, ni en pause, ni dans les options
                if (game.State == GameManager.Phase.Playing && Time.timeScale > 0.5f && !OptionsMenu.IsOpen && !(game.Hud && game.Hud.Paused) && GameInput.MapPressed) SetOpen(true);
                return;
            }
            if (Time.frameCount != openedFrame && (GameInput.MapPressed || GameInput.CancelPressed || GameInput.PausePressed))
            {
                SetOpen(false);
                return;
            }
            ReadInput(Time.unscaledDeltaTime);
            Refresh();
        }

        public void SetOpen(bool open)
        {
            if (open == IsOpen || !game || game.Map == null) return;
            // Mise en page refaite si la fenêtre a changé de proportions
            float aspect = Screen.width / (float)Mathf.Max(Screen.height, 1);
            if (open && root && Mathf.Abs(aspect - builtAspect) > 0.01f)
            {
                Destroy(root);
                root = null;
                fixedSize.Clear();
                playerMarks.Clear();
                routeVersion = -1;
                markArrows.Clear();
                markNames.Clear();
                raceLines.Clear();
                raceLabels.Clear();
                raceRows.Clear();
                raceStarts.Clear();
                raceFinishes.Clear();
                raceHover = -1;
                raceZoom = -1f;
            }
            if (open && !root) Build();
            IsOpen = open;
            root.SetActive(open);
            Time.timeScale = open && !game.Online ? 0f : 1f;
            game.SetInputEnabled(!open);
            openedFrame = Time.frameCount;
            ChangedFrame = Time.frameCount;
            if (open)
            {
                // Vue d'ensemble, centrée sur la vallée
                zoom = 1.12f;
                follow = false;
                center = new Vector2(30f, 230f);
                RefreshRaceLabels();
                Refresh();
            }
        }

        void ReadInput(float dt)
        {
            float zoomIn = 0f;
            Vector2 move = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                zoomIn += Mathf.Clamp(mouse.scroll.ReadValue().y, -1f, 1f) * 6f;
                // Clic (sans glisser) dans la carte : destination du GPS
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    pressed = RectTransformUtility.RectangleContainsScreenPoint(view, mouse.position.ReadValue(), null);
                    pressMoved = 0f;
                }
                if (mouse.leftButton.isPressed)
                {
                    // Glisser : la carte suit le curseur
                    Vector2 d = mouse.delta.ReadValue();
                    pressMoved += d.magnitude;
                    float pxToMetres = MetresPerUnit / zoom * (1080f / Mathf.Max(Screen.height, 1));
                    center -= d * pxToMetres;
                    if (d.sqrMagnitude > 0.5f) follow = false;
                }
                if (mouse.leftButton.wasReleasedThisFrame && pressed && pressMoved < 8f) ClickAt(mouse.position.ReadValue());
                if (mouse.leftButton.wasReleasedThisFrame) pressed = false;
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.pageUpKey.isPressed || kb.numpadPlusKey.isPressed) zoomIn += 1.5f;
                if (kb.pageDownKey.isPressed || kb.numpadMinusKey.isPressed) zoomIn -= 1.5f;
                if (kb.spaceKey.wasPressedThisFrame) FocusPlayer();
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) PlaceAt(center);
                if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) ClearDestination();
            }
            foreach (var pad in Gamepad.all)
            {
                move += pad.leftStick.ReadValue();
                zoomIn += (pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue()) * 1.5f;
                if (pad.buttonSouth.wasPressedThisFrame) PlaceAt(center);
                if (pad.buttonNorth.wasPressedThisFrame) ClearDestination();
                if (pad.buttonWest.wasPressedThisFrame) FocusPlayer();
            }
            if (Mathf.Abs(zoomIn) > 0.01f) zoom = Mathf.Clamp(zoom * Mathf.Exp(zoomIn * dt * 1.6f), MinZoom, MaxZoom);
            if (move.sqrMagnitude > 0.04f)
            {
                center += Vector2.ClampMagnitude(move, 1f) * (2600f / zoom) * dt;
                follow = false;
            }
        }

        void FocusPlayer()
        {
            follow = true;
            zoom = Mathf.Max(zoom, 3.2f);
        }

        float MetresPerUnit => game.Map.Half * 2f / ViewSize;

        /// <summary>Clic dans la carte : sur le repère, il le retire ; ailleurs, il y pose la destination.</summary>
        void ClickAt(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, null, out var local)) return;
            var nav = game.Nav;
            if (nav && nav.HasDestination && pin && (local - pin.anchoredPosition).magnitude * zoom < 20f)
            {
                ClearDestination();
                return;
            }
            PlaceAt(local * MetresPerUnit);
        }

        /// <summary>Destination au point de la carte (m) : un lieu-dit tout proche lui donne son nom.</summary>
        void PlaceAt(Vector2 xz)
        {
            var nav = game.Nav;
            // Pendant une course, le GPS affiche son parcours : pas d'autre destination
            if (!nav || nav.OnCourse || GameManager.DedicatedServer) return;
            // Clic sur le départ d'une course : le GPS y mène
            if (game.Races)
                foreach (var t in game.Races.All)
                    if ((Local(t.start.pos.x, t.start.pos.z) - Local(xz.x, xz.y)).magnitude * zoom < 22f)
                    {
                        nav.SetDestination(t.start.pos, t.fr, t.en);
                        return;
                    }
            string fr = null, en = null;
            float best = float.MaxValue;
            if (game.Map.Json.pois != null)
                foreach (var poi in game.Map.Json.pois)
                {
                    if (poi.id == "depart" || string.IsNullOrEmpty(poi.fr)) continue;
                    float px = (Local(poi.x, poi.z) - Local(xz.x, xz.y)).magnitude * zoom;
                    float d = (new Vector2(poi.x, poi.z) - xz).magnitude;
                    // Clic sur le point du lieu-dit, ou dans son périmètre
                    if ((px < 24f || poi.r > 0f && d < poi.r) && d < best) { best = d; fr = poi.fr; en = poi.en; }
                }
            nav.SetDestination(new Vector3(xz.x, 0f, xz.y), fr, en);
        }

        void ClearDestination()
        {
            if (game.Nav) game.Nav.ClearDestination();
        }

        /// <summary>Nom d'un lieu-dit ou d'une course de la carte (déjà écrit à côté de son repère).</summary>
        bool IsPlaceName(string name)
        {
            if (game.Races)
                foreach (var t in game.Races.All)
                    if (t.Name == name) return true;
            if (game.Map.Json.pois != null)
                foreach (var poi in game.Map.Json.pois)
                    if (!string.IsNullOrEmpty(poi.fr) && Loc.T(poi.fr, poi.en) == name) return true;
            return false;
        }

        /// <summary>Repère de destination et tracé de l'itinéraire (épaisseur constante à l'écran quel que soit le zoom).</summary>
        void RefreshRoute()
        {
            var nav = game.Nav;
            bool has = nav && nav.HasDestination;
            pin.gameObject.SetActive(has);
            if (has)
            {
                pin.anchoredPosition = Local(nav.Destination.x, nav.Destination.z);
                pin.localScale = Vector3.one / zoom;
                pinLabel.text = IsPlaceName(nav.DestinationName) ? "" : nav.DestinationName;
            }
            int version = nav ? nav.Version : -1;
            if (version != routeVersion || !Mathf.Approximately(zoom, routeZoom))
            {
                routeVersion = version;
                routeZoom = zoom;
                mapRoute.strips.Clear();
                if (nav && nav.HasRoute)
                {
                    var strip = new List<Vector2>();
                    float step = 1.2f / zoom;
                    foreach (var p in nav.Route)
                    {
                        Vector2 q = Local(p.x, p.z);
                        if (strip.Count == 0 || (q - strip[strip.Count - 1]).sqrMagnitude > step * step) strip.Add(q);
                    }
                    var end = nav.Route[nav.Route.Count - 1];
                    strip.Add(Local(end.x, end.z));
                    mapRoute.strips.Add(strip);
                }
                mapRoute.width = 5f / zoom;
                mapRoute.edgeWidth = 1.6f / zoom;
                mapRoute.SetVerticesDirty();
            }
            cross.gameObject.SetActive(GameInput.UsingGamepad || Keyboard.current != null && (Keyboard.current.upArrowKey.isPressed || Keyboard.current.downArrowKey.isPressed || Keyboard.current.leftArrowKey.isPressed || Keyboard.current.rightArrowKey.isPressed));
            if (!has)
            {
                destName.text = Loc.T("Aucune", "None");
                destDetail.text = GameInput.UsingGamepad ? Loc.T("A : poser la destination sur le viseur", "A: set the destination at the crosshair")
                    : Loc.T("Clic sur la carte : choisir où aller", "Click the map to choose where to go");
            }
            else
            {
                destName.text = nav.DestinationName;
                destDetail.text = nav.OnCourse ? Loc.T("Course en cours : suivre le parcours", "Race in progress: follow the course")
                    : !nav.HasRoute ? Loc.T("Pas d'itinéraire", "No route")
                    : Loc.F($"{nav.Remaining / 1000f:0.0} km  ·  ≈ {Mathf.Max(1, Mathf.RoundToInt(nav.RemainingTime / 60f))} min");
            }
        }

        Vector2 Local(float x, float z) => new Vector2(x, z) / MetresPerUnit;

        void Refresh()
        {
            var map = game.Map;
            if (follow)
            {
                Vector3 p = game.Player.transform.position;
                center = new Vector2(p.x, p.z);
            }
            // La vue ne sort pas de la carte
            float reach = map.Half * (1f - 1f / zoom);
            center.x = Mathf.Clamp(center.x, -reach, reach);
            center.y = Mathf.Clamp(center.y, -reach, reach);
            content.localScale = Vector3.one * zoom;
            content.anchoredPosition = -Local(center.x, center.y) * zoom;
            // Repères et noms : taille constante à l'écran quel que soit le zoom
            foreach (var m in fixedSize) m.localScale = Vector3.one / zoom;
            var remotes = game.Net ? game.Net.Remotes : null;
            int others = remotes != null ? remotes.Count : 0;
            while (playerMarks.Count < 1 + others) playerMarks.Add(PlayerMark(playerMarks.Count));
            for (int i = 0; i < playerMarks.Count; i++)
            {
                var remote = i > 0 && i - 1 < others ? remotes[i - 1] : null;
                Transform t = i == 0 ? (GameManager.DedicatedServer ? null : game.Player.transform) : remote && remote.Ready ? remote.transform : null;
                playerMarks[i].gameObject.SetActive(t);
                if (!t) continue;
                if (remote)
                {
                    markArrows[i].color = remote.Color;
                    markNames[i].text = remote.Name;
                }
                playerMarks[i].anchoredPosition = Local(t.position.x, t.position.z);
                markArrows[i].rectTransform.localEulerAngles = new Vector3(0f, 0f, -t.eulerAngles.y);
                float pulse = i == 0 ? 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.08f : 0.85f;
                playerMarks[i].localScale = Vector3.one * (pulse / zoom);
            }
            placeText.text = string.IsNullOrEmpty(game.Place) ? Loc.T("Sur la route", "On the road") : game.Place;
            // Tracés des courses : épaisseur constante à l'écran
            if (!Mathf.Approximately(zoom, raceZoom))
            {
                raceZoom = zoom;
                foreach (var line in raceLines)
                {
                    line.width = 4.5f / zoom;
                    line.edgeWidth = 1.5f / zoom;
                    line.SetVerticesDirty();
                }
            }
            RefreshRoute();
            UpdateRaceHover();
            bool pad = GameInput.UsingGamepad;
            hintText.text = pad
                ? Loc.T("Joystick : déplacer   ·   LT / RT : zoom   ·   A : destination   ·   Y : l'effacer   ·   X : ma position   ·   Select / B : fermer",
                        "Stick: move   ·   LT / RT: zoom   ·   A: destination   ·   Y: clear it   ·   X: my position   ·   Select / B: close")
                : Loc.T("Glisser ou flèches : déplacer   ·   Molette : zoom   ·   Clic : destination (Entrée : au viseur)   ·   Suppr : l'effacer   ·   Espace : ma position   ·   « , » ou Échap : fermer",
                        "Drag or arrows: move   ·   Wheel: zoom   ·   Click: destination (Enter: at the crosshair)   ·   Del: clear it   ·   Space: my position   ·   \",\" or Esc: close");
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            var map = game.Map;
            root = new GameObject("WorldMap", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 28;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            var shade = Rect(root.transform, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.78f);

            // Cadre et fenêtre de la carte (carrée, calée à droite), masquée à ses bords ; la colonne de gauche prend la
            // place qui reste (écrans 16:9 comme 4:3)
            builtAspect = Screen.width / (float)Mathf.Max(Screen.height, 1);
            float canvasWidth = 1080f * builtAspect;
            float sideWidth = Mathf.Clamp(canvasWidth - (ViewSize + 16f) - 72f, 330f, 560f);
            float mapX = -24f - (ViewSize + 16f) * 0.5f;
            var frame = Box(shade, "Frame", new Vector2(1f, 0.5f), new Vector2(mapX, 0f), new Vector2(ViewSize + 16f, ViewSize + 16f));
            frame.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 0.95f);
            view = Box(shade, "View", new Vector2(1f, 0.5f), new Vector2(mapX, 0f), new Vector2(ViewSize, ViewSize));
            view.gameObject.AddComponent<RectMask2D>();
            content = Box(view, "Content", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize));
            var image = content.gameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>("Map/map_overview");
            image.color = image.texture ? Color.white : new Color(0.2f, 0.3f, 0.22f);

            // Lieux-dits
            if (map.Json.pois != null)
                foreach (var poi in map.Json.pois)
                {
                    if (poi.id == "depart" || string.IsNullOrEmpty(poi.fr)) continue;
                    var mark = Box(content, "Poi_" + poi.id, new Vector2(0.5f, 0.5f), Local(poi.x, poi.z), new Vector2(10f, 10f));
                    // La concession (achat de voitures, cyan) et le garage de tuning (magenta) : repères plus grands,
                    // bien visibles
                    bool dealer = poi.id == "dealer", tuning = poi.id == "tuning";
                    Color tint = dealer ? Info : tuning ? Tuning : Color.white;
                    float size = dealer || tuning ? 18f : 12f;
                    var dot = Box(mark, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<Image>();
                    dot.sprite = HudArt.Disc;
                    dot.color = Plate;
                    var core = Box(mark, "Core", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size - 5f, size - 5f)).gameObject.AddComponent<Image>();
                    core.sprite = HudArt.Disc;
                    core.color = dealer || tuning ? tint : Accent;
                    // étiquette à droite du repère, ou à gauche (side = -1) quand deux lieux sont voisins
                    bool left = poi.side < 0;
                    var label = Label(mark, "", 21, left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, tint,
                        new Vector2(left ? -size * 0.5f - 8f : size * 0.5f + 8f, 0f), new Vector2(420f, 30f), FontStyle.BoldAndItalic);
                    label.rectTransform.pivot = new Vector2(left ? 1f : 0f, 0.5f);
                    Loc.Bind(label, poi.fr, poi.en);
                    var outline = label.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                    outline.effectDistance = new Vector2(1.6f, -1.6f);
                    fixedSize.Add(mark);
                }

            // Itinéraire du GPS (sous les repères) et destination
            mapRoute = Box(content, "Route", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize)).gameObject.AddComponent<RouteLine>();
            mapRoute.core = Info;
            mapRoute.edge = new Color(0.02f, 0.03f, 0.05f, 0.9f);
            mapRoute.transform.SetSiblingIndex(0);

            // Courses : le départ de chaque tracé ; son parcours (un tour pour une boucle, au-dessus de l'itinéraire du
            // GPS) et, pour un sprint, son arrivée, masqués tant que le départ n'est pas survolé
            if (game.Races)
                foreach (var t in game.Races.All)
                {
                    var line = Box(content, "Race_" + t.id, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize)).gameObject.AddComponent<RouteLine>();
                    line.core = t.Tint;
                    line.edge = new Color(0.02f, 0.03f, 0.05f, 0.9f);
                    var strip = new List<Vector2>();
                    foreach (var p in t.path)
                    {
                        Vector2 q = Local(p.x, p.z);
                        if (strip.Count == 0 || (q - strip[strip.Count - 1]).sqrMagnitude > 0.25f) strip.Add(q);
                    }
                    strip.Add(Local(t.Finish.x, t.Finish.z));
                    line.strips.Add(strip);
                    line.transform.SetSiblingIndex(0);
                    line.gameObject.SetActive(false);
                    raceLines.Add(line);
                    var label = RaceMark(t, t.start.pos, true, t.side);
                    raceLabels.Add(label);
                    raceStarts.Add((RectTransform)label.transform.parent);
                    RectTransform finish = null;
                    if (!t.Loop)
                    {
                        var end = RaceMark(t, t.Finish, false, 0);
                        Loc.Bind(end, "Arrivée", "Finish");
                        finish = (RectTransform)end.transform.parent;
                        finish.gameObject.SetActive(false);
                    }
                    raceFinishes.Add(finish);
                }
            mapRoute.transform.SetSiblingIndex(0);

            pin = Box(content, "Destination", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            var pinEdge = Box(pin, "Edge", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f)).gameObject.AddComponent<Image>();
            pinEdge.color = new Color(0.02f, 0.03f, 0.05f, 0.95f);
            pinEdge.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            var pinCore = Box(pin, "Core", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(17f, 17f)).gameObject.AddComponent<Image>();
            pinCore.color = Info;
            pinCore.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            pinLabel = Label(pin, "", 22, TextAnchor.LowerCenter, Info, new Vector2(0f, 18f), new Vector2(420f, 30f), FontStyle.BoldAndItalic);
            pinLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            var pinOutline = pinLabel.gameObject.AddComponent<Outline>();
            pinOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            pinOutline.effectDistance = new Vector2(1.6f, -1.6f);
            pin.gameObject.SetActive(false);

            // Joueurs : la voiture locale ; ceux des autres joueurs s'ajoutent à l'affichage (Refresh)
            playerMarks.Add(PlayerMark(0));

            // Viseur au centre de la vue (manette, clavier) : la destination se pose dessous
            cross = Box(view, "Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            foreach (var size in new[] { new Vector2(30f, 4f), new Vector2(4f, 30f) })
            {
                var back = Box(cross, "Back", new Vector2(0.5f, 0.5f), Vector2.zero, size + new Vector2(3f, 3f)).gameObject.AddComponent<Image>();
                back.color = new Color(0f, 0f, 0f, 0.7f);
                back.raycastTarget = false;
            }
            foreach (var size in new[] { new Vector2(30f, 2f), new Vector2(2f, 30f) })
            {
                var line = Box(cross, "Line", new Vector2(0.5f, 0.5f), Vector2.zero, size).gameObject.AddComponent<Image>();
                line.color = Color.white;
                line.raycastTarget = false;
            }
            cross.gameObject.SetActive(false);

            // Colonne de gauche : titre, lieu, légende
            var side = Box(shade, "Side", new Vector2(0f, 0.5f), new Vector2(24f + sideWidth * 0.5f, 0f), new Vector2(sideWidth, ViewSize + 16f));
            float textWidth = sideWidth - 70f;
            var plate = side.gameObject.AddComponent<Image>();
            plate.color = Plate;
            var title = Label(side, "", 64, TextAnchor.UpperLeft, Accent, new Vector2(40f, -36f), new Vector2(textWidth, 84f), FontStyle.BoldAndItalic, new Vector2(0f, 1f));
            Loc.Bind(title, "CARTE", "MAP");
            var sub = Label(side, "", 26, TextAnchor.UpperLeft, Info, new Vector2(42f, -122f), new Vector2(textWidth, 36f), FontStyle.BoldAndItalic, new Vector2(0f, 1f));
            Loc.Bind(sub, "LES TROIS VALLONS", "THREE VALLEYS");
            var here = Label(side, "", 22, TextAnchor.UpperLeft, Dim, new Vector2(42f, -190f), new Vector2(textWidth, 30f), FontStyle.Normal, new Vector2(0f, 1f));
            Loc.Bind(here, "Vous êtes ici", "You are here");
            placeText = Label(side, "", 30, TextAnchor.UpperLeft, Color.white, new Vector2(42f, -222f), new Vector2(textWidth, 80f), FontStyle.Bold, new Vector2(0f, 1f));
            placeText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Légende : lignes resserrées, la dernière (votre voiture) reste au-dessus de la destination
            const float rowTop = -366f, rowStep = 29f;
            var legend = Label(side, "", 26, TextAnchor.UpperLeft, Info, new Vector2(42f, -318f), new Vector2(textWidth, 36f), FontStyle.BoldAndItalic, new Vector2(0f, 1f));
            Loc.Bind(legend, "LÉGENDE", "LEGEND");
            var rows = new (Color color, float width, string fr, string en)[]
            {
                (new Color(1f, 0.59f, 0.31f), 12f, "Autoroute", "Motorway"),
                (new Color(0.99f, 0.98f, 0.94f), 9f, "Route principale", "Main road"),
                (new Color(1f, 0.84f, 0.43f), 7f, "Route de montagne", "Mountain road"),
                (new Color(0.92f, 0.91f, 0.86f), 4f, "Chemin", "Lane"),
                (new Color(0.74f, 0.59f, 0.42f), 4f, "Chemin de terre", "Dirt track"),
                (new Color(0.28f, 0.54f, 0.77f), 12f, "Lac, rivière", "Lake, river"),
                (new Color(0.48f, 0.26f, 0.2f), 12f, "Village", "Village"),
                (new Color(0.2f, 0.38f, 0.24f), 12f, "Forêt", "Forest"),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float y = rowTop - i * rowStep;
                var bar = Box(side, "Key" + i, new Vector2(0f, 1f), new Vector2(72f, y - 15f), new Vector2(56f, rows[i].width)).gameObject.AddComponent<Image>();
                bar.color = rows[i].color;
                var text = Label(side, "", 23, TextAnchor.UpperLeft, Color.white, new Vector2(122f, y), new Vector2(400f, 32f), FontStyle.Normal, new Vector2(0f, 1f));
                Loc.Bind(text, rows[i].fr, rows[i].en);
            }
            // Courses : une ligne par tracé, de sa couleur (nom, puis record personnel ou longueur : RefreshRaceLabels)
            int row = rows.Length;
            if (game.Races)
                foreach (var t in game.Races.All)
                {
                    float y = rowTop - row++ * rowStep;
                    var bar = Box(side, "Race_" + t.id, new Vector2(0f, 1f), new Vector2(72f, y - 15f), new Vector2(56f, 6f)).gameObject.AddComponent<Image>();
                    bar.color = t.Tint;
                    var text = Label(side, "", 23, TextAnchor.UpperLeft, Color.white, new Vector2(122f, y), new Vector2(sideWidth - 140f, 30f), FontStyle.Normal, new Vector2(0f, 1f));
                    // La ligne rétrécit plutôt que de déborder (colonne étroite sur un écran 4:3)
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    text.verticalOverflow = VerticalWrapMode.Truncate;
                    text.resizeTextForBestFit = true;
                    text.resizeTextMinSize = 13;
                    text.resizeTextMaxSize = 23;
                    raceRows.Add(text);
                }
            var you = Label(side, "▲", 26, TextAnchor.UpperLeft, Accent, new Vector2(58f, rowTop - row * rowStep + 4f), new Vector2(40f, 36f), FontStyle.Normal, new Vector2(0f, 1f));
            you.raycastTarget = false;
            var youText = Label(side, "", 23, TextAnchor.UpperLeft, Color.white, new Vector2(122f, rowTop - row * rowStep), new Vector2(400f, 32f), FontStyle.Normal, new Vector2(0f, 1f));
            Loc.Bind(youText, "Votre voiture", "Your car");

            // GPS : destination choisie, distance et temps estimé
            var destTitle = Label(side, "", 26, TextAnchor.LowerLeft, Info, new Vector2(42f, 248f), new Vector2(textWidth, 36f), FontStyle.BoldAndItalic, new Vector2(0f, 0f));
            Loc.Bind(destTitle, "DESTINATION", "DESTINATION");
            destName = Label(side, "", 28, TextAnchor.LowerLeft, Color.white, new Vector2(42f, 212f), new Vector2(textWidth, 36f), FontStyle.Bold, new Vector2(0f, 0f));
            destDetail = Label(side, "", 21, TextAnchor.LowerLeft, Dim, new Vector2(42f, 184f), new Vector2(textWidth, 30f), FontStyle.Normal, new Vector2(0f, 0f));
            destDetail.horizontalOverflow = HorizontalWrapMode.Wrap;

            hintText = Label(side, "", 19, TextAnchor.LowerLeft, Dim, new Vector2(42f, 34f), new Vector2(textWidth, 150f), FontStyle.Normal, new Vector2(0f, 0f));
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            root.SetActive(false);
        }

        /// <summary>
        /// Repère d'une course, de la couleur de son tracé : pastille au départ (étiquette à gauche, au-dessus ou à
        /// droite selon <paramref name="side"/>, là où la carte est dégagée), carré à l'arrivée d'un sprint (étiquette
        /// dessous). Renvoie l'étiquette.
        /// </summary>
        Text RaceMark(Races.Track t, Vector3 at, bool start, int side)
        {
            var mark = Box(content, (start ? "RaceStart_" : "RaceFinish_") + t.id, new Vector2(0.5f, 0.5f), Local(at.x, at.z), new Vector2(10f, 10f));
            var layers = new (float size, Color color)[] { (start ? 22f : 18f, Plate), (start ? 17f : 13f, t.Tint), (start ? 7f : 5f, Color.white) };
            foreach (var (size, color) in layers)
            {
                var img = Box(mark, "Layer", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<Image>();
                if (start) img.sprite = HudArt.Disc;
                img.color = color;
                img.raycastTarget = false;
            }
            Text label;
            if (start && side != 0)
            {
                label = Label(mark, "", 21, side < 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, t.Tint, new Vector2(side * 19f, 0f), new Vector2(520f, 30f), FontStyle.BoldAndItalic);
                label.rectTransform.pivot = new Vector2(side < 0 ? 1f : 0f, 0.5f);
            }
            else
            {
                label = Label(mark, "", start ? 21 : 18, start ? TextAnchor.LowerCenter : TextAnchor.UpperCenter, t.Tint,
                    new Vector2(0f, start ? 15f : -13f), new Vector2(520f, 30f), FontStyle.BoldAndItalic);
                label.rectTransform.pivot = new Vector2(0.5f, start ? 0f : 1f);
            }
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);
            fixedSize.Add(mark);
            return label;
        }

        /// <summary>
        /// Départ survolé par le curseur (ou par le viseur, à la manette et aux flèches) : son tracé et l'arrivée d'un
        /// sprint s'affichent, le repère grossit et son étiquette se détaille. Les autres tracés restent masqués.
        /// </summary>
        void UpdateRaceHover()
        {
            if (raceStarts.Count == 0) return;
            bool has = false;
            Vector2 local = Vector2.zero;
            if (cross.gameObject.activeSelf)
            {
                local = Local(center.x, center.y);
                has = true;
            }
            else if (Mouse.current != null)
            {
                Vector2 screen = Mouse.current.position.ReadValue();
                has = RectTransformUtility.RectangleContainsScreenPoint(view, screen, null)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, null, out local);
            }
            int hover = -1;
            if (has)
            {
                float best = RaceHoverRadius;
                for (int i = 0; i < raceStarts.Count; i++)
                {
                    float d = (local - raceStarts[i].anchoredPosition).magnitude * zoom;
                    if (d < best) { best = d; hover = i; }
                }
            }
            if (hover != raceHover)
            {
                raceHover = hover;
                for (int i = 0; i < raceLines.Count; i++)
                {
                    bool on = i == hover;
                    raceLines[i].gameObject.SetActive(on);
                    if (raceFinishes[i]) raceFinishes[i].gameObject.SetActive(on);
                }
                // Au premier plan : l'étiquette détaillée passe au-dessus des lieux-dits voisins
                if (hover >= 0) raceStarts[hover].SetAsLastSibling();
                RefreshRaceLabels();
            }
            if (hover >= 0) raceStarts[hover].localScale = Vector3.one * (1.3f / zoom);
        }

        /// <summary>
        /// Courses : étiquette de chaque départ (le nom du tracé ; au survol : aussi son genre, sa longueur et le
        /// record personnel) et sa ligne dans la légende (nom, puis record ou longueur).
        /// </summary>
        void RefreshRaceLabels()
        {
            if (!game.Races) return;
            var races = game.Races.All;
            for (int i = 0; i < raceLabels.Count && i < races.Count; i++)
            {
                var t = races[i];
                string best = t.best > 0f ? Loc.T("record ", "best ") + Races.Clock(t.best) : Loc.T("pas encore de record", "no best time yet");
                raceLabels[i].text = i == raceHover ? t.Name + "\n<size=17>" + t.Summary + "  ·  " + best + "</size>" : t.Name;
                if (i < raceRows.Count)
                    raceRows[i].text = t.Name + "  ·  " + (t.best > 0f ? Races.Clock(t.best) : t.Summary);
            }
        }

        /// <summary>Repère d'un joueur : flèche orientée dans un disque (or : soi ; couleur et pseudo du joueur : les autres).</summary>
        RectTransform PlayerMark(int index)
        {
            var mark = Box(content, "Player" + index, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            var halo = Box(mark, "Halo", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f)).gameObject.AddComponent<Image>();
            halo.sprite = HudArt.Disc;
            halo.color = new Color(0.02f, 0.03f, 0.05f, 0.9f);
            var arrow = Label(mark, "▲", 26, TextAnchor.MiddleCenter, Accent, new Vector2(0f, 1.5f), new Vector2(40f, 40f), FontStyle.Normal);
            var name = Label(mark, "", 21, TextAnchor.MiddleLeft, Color.white, new Vector2(26f, 0f), new Vector2(320f, 30f), FontStyle.BoldAndItalic);
            name.rectTransform.pivot = new Vector2(0f, 0.5f);
            var outline = name.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);
            markArrows.Add(arrow);
            markNames.Add(name);
            return mark;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Text Label(Transform parent, string text, int size, TextAnchor align, Color color, Vector2 pos, Vector2 box, FontStyle style, Vector2? anchor = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudArt.Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = anchor.HasValue ? a : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            return t;
        }
    }
}
