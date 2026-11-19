using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Carte globale : plein écran, ouverte par la touche « , » (ou Select à la manette) pendant la balade. Le jeu est
    /// en pause tant qu'elle est affichée (sauf en réseau : le monde continue, les autres joueurs s'y déplacent).
    /// La carte (image générée avec la carte, Tools/Map/map_overview.py) remplit l'écran et se fond dans le fond ; des
    /// plaques de verre (style de l'interface, <see cref="Glass"/>) flottent par-dessus : titre, lieu actuel et légende
    /// en haut à gauche, échelle et boussole en haut à droite, touches en bas à droite. Molette (vers le curseur) ou gâchettes pour zoomer,
    /// flèches / joystick / glisser pour se déplacer ; les noms secondaires (hameaux, sites, courses) apparaissent en
    /// zoomant.
    /// GPS : un clic sur la carte (ou A / Entrée sur le viseur) y pose la destination, l'itinéraire le plus rapide
    /// s'affiche (<see cref="RouteGuide"/>) ; un clic sur le repère, Y ou Suppr le retire.
    /// En réseau, le repère d'un autre joueur se choisit de la même façon (clic, ou A / Entrée avec le viseur dessus) :
    /// le GPS mène à lui et le suit ; son repère est alors cerclé de cyan, et un clic dessus cesse de le suivre.
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
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.05f, 0.92f);
        /// <summary>Fond du menu : les bords de l'image de la carte s'y fondent (map_overview.py, BG).</summary>
        static readonly Color Back = new Color32(9, 11, 15, 255);
        /// <summary>Taille de la carte entière (unités du canevas) au zoom 1.</summary>
        const float ViewSize = 1040f, MaxZoom = 7f, Margin = 36f;
        /// <summary>Part de l'image où l'on peut encore amener le bord de la vue (au-delà : le fondu vers le fond).</summary>
        const float Reach = 0.9f;
        /// <summary>Zoom à partir duquel s'écrivent les noms secondaires.</summary>
        const float SiteZoom = 1.5f, HamletZoom = 1.9f, RaceNameZoom = 2.4f;
        // Plaques de verre (unités du canevas 1920×1080) : arrondi, largeur de la plaque de gauche, boussole, échelle, touches
        const float PlateRadius = 39f, SideWidth = 480f, CompassSize = 87f, PillHeight = 66f, HintHeight = 60f;

        GameManager game;
        GameObject root;
        RectTransform shade, content;
        readonly List<RectTransform> fixedSize = new List<RectTransform>();
        // Noms posés sur la carte, par priorité (Declutter) ; rectangles déjà occupés à l'écran
        readonly List<MapName> names = new List<MapName>();
        readonly List<Rect> taken = new List<Rect>(), dots = new List<Rect>();
        readonly List<int> dotOwners = new List<int>();
        // Plaques posées sur la carte : un clic ou la molette dessus n'agit pas sur la carte
        readonly List<RectTransform> panels = new List<RectTransform>();
        // Repères des joueurs : le premier est la voiture locale, les suivants ceux des autres joueurs (réseau)
        readonly List<RectTransform> playerMarks = new List<RectTransform>();
        readonly List<RectTransform> markArrows = new List<RectTransform>();
        readonly List<Graphic> markCores = new List<Graphic>();
        readonly List<Text> markNames = new List<Text>();
        readonly List<HudRing> markRings = new List<HudRing>();
        // Autre joueur désigné par le curseur ou le viseur (son repère grossit et dit comment le choisir)
        RemoteCar hoveredPlayer;
        /// <summary>Distance à un repère de joueur (unités d'écran) sous laquelle le curseur ou le viseur le désigne.</summary>
        const float PlayerHoverRadius = 22f;
        Text placeText, scaleText;
        RectTransform scaleBar, scalePill, hintRow;
        float hintMaxWidth;
        bool hintsForPad;
        float minZoom = 1f;
        // Zone de la carte non couverte par les plaques : centre (par rapport au centre de l'écran) et demi-taille
        Vector2 openOffset, openHalf;
        // GPS : fenêtre de la carte, repère de destination, tracé de l'itinéraire, viseur (manette, clavier)
        RectTransform view, pin, cross;
        Text pinLabel;
        RouteLine mapRoute;
        int routeVersion = -1;
        float routeZoom;
        // Courses : parcours de chaque tracé et étiquette de son départ (nom, genre, longueur, record)
        readonly List<RouteLine> raceLines = new List<RouteLine>();
        readonly List<Text> raceLabels = new List<Text>();
        // Seuls les départs sont affichés ; le tracé (et l'arrivée d'un sprint) n'apparaît qu'au survol du départ
        readonly List<RectTransform> raceStarts = new List<RectTransform>(), raceFinishes = new List<RectTransform>();
        int raceHover = -1;
        float raceZoom = -1f;
        /// <summary>Distance au départ (unités d'écran) sous laquelle le curseur ou le viseur le survole.</summary>
        const float RaceHoverRadius = 18f;
        // Radars et tremplins : coche « réussi » de la pastille et étiquette de chacun (texte selon qu'il est déjà réussi)
        const float GoalBadge = 26f;
        readonly List<GameObject> radarChecks = new List<GameObject>(), stuntChecks = new List<GameObject>();
        /// <summary>Repères des objectifs : les radars, puis les tremplins (index de <see cref="goalHover"/>).</summary>
        readonly List<RectTransform> goalMarks = new List<RectTransform>();
        int goalHover = -1;
        readonly List<Text> radarLabels = new List<Text>(), stuntLabels = new List<Text>();
        // Fiche du repère survolé (course, radar, tremplin) : carte de verre posée à côté du repère, à la place de son nom
        RectTransform card, cardRule, cardState, cardHint;
        GlassRect cardDot, cardHintChip;
        CanvasGroup cardGroup;
        Text cardKicker, cardName, cardStateText, cardHintKey, cardHintText;
        GameObject cardCheck;
        HudRing cardTodo;
        readonly List<Text> cardKeys = new List<Text>(), cardValues = new List<Text>();
        string cardFor;
        const float CardPad = 33f, CardRow = 33f;
        bool pressed;
        float pressMoved;
        float zoom = 1.12f;
        Vector2 center;          // point de la carte (m) au centre de la zone libre
        // Point de la carte (m) sous le viseur (manette, flèches) : la vue le suit tant qu'elle peut défiler ; bornée
        // (carte dézoomée, bord de la carte), c'est le viseur qui se déplace dans la vue
        Vector2 aim;
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
                names.Clear();
                panels.Clear();
                playerMarks.Clear();
                markArrows.Clear();
                markCores.Clear();
                markNames.Clear();
                markRings.Clear();
                routeVersion = -1;
                raceLines.Clear();
                raceLabels.Clear();
                raceStarts.Clear();
                raceFinishes.Clear();
                radarChecks.Clear();
                radarLabels.Clear();
                stuntChecks.Clear();
                stuntLabels.Clear();
                goalMarks.Clear();
                cardKeys.Clear();
                cardValues.Clear();
                goalHover = -1;
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
            // Fiche réécrite à la réouverture (un objectif a pu être réussi entre-temps)
            cardFor = null;
            if (open)
            {
                // Vue d'ensemble : toute la zone où l'on roule
                zoom = minZoom;
                follow = false;
                center = aim = new Vector2(0f, 60f);
                BuildHints(GameInput.UsingGamepad);
                RefreshRaceLabels();
                RefreshGoals();
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
                Vector2 screen = mouse.position.ReadValue();
                bool onMap = OnMap(screen);
                // Molette : zoom vers le point sous le curseur
                float wheel = Mathf.Clamp(mouse.scroll.ReadValue().y, -1f, 1f);
                if (Mathf.Abs(wheel) > 0.01f && onMap && RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, null, out var under))
                {
                    float next = Mathf.Clamp(zoom * Mathf.Exp(wheel * 0.18f), minZoom, MaxZoom);
                    Vector2 p = under * MetresPerUnit;
                    center = aim = p - (p - center) * (zoom / next);
                    zoom = next;
                    follow = false;
                }
                // Clic (sans glisser) dans la carte : destination du GPS
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    pressed = onMap;
                    pressMoved = 0f;
                }
                if (mouse.leftButton.isPressed && pressed)
                {
                    // Glisser : la carte suit le curseur
                    Vector2 d = mouse.delta.ReadValue();
                    pressMoved += d.magnitude;
                    float pxToMetres = MetresPerUnit / zoom * (1080f / Mathf.Max(Screen.height, 1));
                    center -= d * pxToMetres;
                    aim = center;
                    if (d.sqrMagnitude > 0.5f) follow = false;
                }
                if (mouse.leftButton.wasReleasedThisFrame && pressed && pressMoved < 8f) ClickAt(screen);
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
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) PlaceAt(aim);
                if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) ClearDestination();
            }
            foreach (var pad in Gamepad.all)
            {
                move += pad.leftStick.ReadValue();
                zoomIn += (pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue()) * 1.5f;
                if (pad.buttonSouth.wasPressedThisFrame) PlaceAt(aim);
                if (pad.buttonNorth.wasPressedThisFrame) ClearDestination();
                if (pad.buttonWest.wasPressedThisFrame) FocusPlayer();
            }
            if (Mathf.Abs(zoomIn) > 0.01f)
            {
                // Gâchettes / Page : zoom vers le viseur (il reste au même endroit de l'écran)
                float next = Mathf.Clamp(zoom * Mathf.Exp(zoomIn * dt * 1.6f), minZoom, MaxZoom);
                if (!follow) center = aim - (aim - center) * (zoom / next);
                zoom = next;
            }
            if (move.sqrMagnitude > 0.04f)
            {
                // Le viseur avance ; la vue se recentre sur lui autant qu'elle le peut (bornée dans Refresh)
                aim += Vector2.ClampMagnitude(move, 1f) * (2600f / zoom) * dt;
                center = aim;
                follow = false;
            }
        }

        /// <summary>Point de l'écran sur la carte elle-même (pas sur une des plaques).</summary>
        bool OnMap(Vector2 screen)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(view, screen, null)) return false;
            foreach (var p in panels)
                if (p && p.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(p, screen, null)) return false;
            return true;
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
            if (nav && nav.HasDestination && pin && pin.gameObject.activeSelf && (local - pin.anchoredPosition).magnitude * zoom < 20f)
            {
                ClearDestination();
                return;
            }
            // Clic sur le joueur déjà suivi : on cesse de le suivre
            var player = PlayerAt(local);
            if (nav && player && nav.Friend == player)
            {
                ClearDestination();
                return;
            }
            PlaceAt(local * MetresPerUnit);
        }

        /// <summary>Autre joueur (partie en réseau) dont le repère est sous ce point de la carte, ou null.</summary>
        RemoteCar PlayerAt(Vector2 local)
        {
            var remotes = game.Net ? game.Net.Remotes : null;
            if (remotes == null) return null;
            RemoteCar found = null;
            float best = PlayerHoverRadius;
            foreach (var r in remotes)
            {
                if (!r || !r.Ready) continue;
                Vector3 p = r.transform.position;
                float d = (Local(p.x, p.z) - local).magnitude * zoom;
                if (d < best) { best = d; found = r; }
            }
            return found;
        }

        /// <summary>Point de la carte désigné : sous le viseur s'il est affiché (manette, flèches), sinon sous le curseur.</summary>
        bool Pointed(out Vector2 local)
        {
            local = Vector2.zero;
            if (cross.gameObject.activeSelf)
            {
                local = Local(aim.x, aim.y);
                return true;
            }
            if (Mouse.current == null) return false;
            Vector2 screen = Mouse.current.position.ReadValue();
            return OnMap(screen) && RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, null, out local);
        }

        /// <summary>Destination au point de la carte (m) : un lieu-dit tout proche lui donne son nom.</summary>
        void PlaceAt(Vector2 xz)
        {
            var nav = game.Nav;
            // Pendant une course, le GPS affiche son parcours : pas d'autre destination
            if (!nav || nav.OnCourse || GameManager.DedicatedServer) return;
            // Sur le repère d'un autre joueur : le GPS mène à lui et le suit
            var player = PlayerAt(Local(xz.x, xz.y));
            if (player)
            {
                nav.Follow(player);
                return;
            }
            // Clic sur le départ d'une course : le GPS y mène
            if (game.Races)
                foreach (var t in game.Races.All)
                    if ((Local(t.start.pos.x, t.start.pos.z) - Local(xz.x, xz.y)).magnitude * zoom < 22f)
                    {
                        nav.SetDestination(t.start.pos, t.fr, t.en);
                        return;
                    }
            // Clic sur un radar : le GPS y mène
            if (game.Radars)
                foreach (var r in game.Radars.All)
                    if ((Local(r.center.x, r.center.z) - Local(xz.x, xz.y)).magnitude * zoom < 18f)
                    {
                        nav.SetDestination(r.center, r.data.fr, r.data.en);
                        return;
                    }
            // Clic sur un tremplin : le GPS mène au début de sa bande d'élan
            if (game.Stunts)
                foreach (var s in game.Stunts.All)
                    if ((Local(s.lip.x, s.lip.z) - Local(xz.x, xz.y)).magnitude * zoom < 18f)
                    {
                        nav.SetDestination(s.lip - s.forward * (s.data.length + 40f), s.data.fr, s.data.en);
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
            if (game.Radars)
                foreach (var r in game.Radars.All)
                    if (r.Name == name) return true;
            if (game.Stunts)
                foreach (var s in game.Stunts.All)
                    if (s.Name == name) return true;
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
            // Joueur suivi : pas de losange, c'est son repère qui est cerclé (Refresh)
            bool friend = has && nav.Friend;
            pin.gameObject.SetActive(has && !friend);
            if (has && !friend)
            {
                pin.anchoredPosition = Local(nav.Destination.x, nav.Destination.z);
                pin.localScale = Vector3.one * ((1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.06f) / zoom);
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
                mapRoute.width = 6f / zoom;
                mapRoute.edgeWidth = 2f / zoom;
                mapRoute.SetVerticesDirty();
            }
            cross.gameObject.SetActive(GameInput.UsingGamepad || Keyboard.current != null && (Keyboard.current.upArrowKey.isPressed || Keyboard.current.downArrowKey.isPressed || Keyboard.current.leftArrowKey.isPressed || Keyboard.current.rightArrowKey.isPressed));
        }

        Vector2 Local(float x, float z) => new Vector2(x, z) / MetresPerUnit;

        void Refresh()
        {
            if (follow && game.Player)
            {
                Vector3 p = game.Player.transform.position;
                center = aim = new Vector2(p.x, p.z);
            }
            // La vue ne sort pas de la carte : centrée quand la carte tient dans la zone libre, sinon bornée à ses bords
            float m = MetresPerUnit, reach = ViewSize * 0.5f * Reach;
            float ax = openHalf.x / zoom, ay = openHalf.y / zoom;
            center.x = reach <= ax ? 0f : Mathf.Clamp(center.x, -(reach - ax) * m, (reach - ax) * m);
            center.y = reach <= ay ? 0f : Mathf.Clamp(center.y, -(reach - ay) * m, (reach - ay) * m);
            // Le viseur reste sur la carte et dans la zone libre (pas sous les plaques)
            float edge = 24f / zoom;
            aim.x = Mathf.Clamp(aim.x, Mathf.Max(-reach, center.x / m - ax + edge) * m, Mathf.Min(reach, center.x / m + ax - edge) * m);
            aim.y = Mathf.Clamp(aim.y, Mathf.Max(-reach, center.y / m - ay + edge) * m, Mathf.Min(reach, center.y / m + ay - edge) * m);
            content.localScale = Vector3.one * zoom;
            content.anchoredPosition = openOffset - Local(center.x, center.y) * zoom;
            cross.anchoredPosition = openOffset + (Local(aim.x, aim.y) - Local(center.x, center.y)) * zoom;
            // Repères et noms : taille constante à l'écran quel que soit le zoom
            foreach (var mark in fixedSize) mark.localScale = Vector3.one / zoom;
            var remotes = game.Net ? game.Net.Remotes : null;
            int others = remotes != null ? remotes.Count : 0;
            while (playerMarks.Count < 1 + others) playerMarks.Add(PlayerMark(playerMarks.Count));
            hoveredPlayer = others > 0 && Pointed(out var pointed) ? PlayerAt(pointed) : null;
            var followed = game.Nav ? game.Nav.Friend : null;
            for (int i = 0; i < playerMarks.Count; i++)
            {
                var remote = i > 0 && i - 1 < others ? remotes[i - 1] : null;
                Transform t = i == 0 ? (GameManager.DedicatedServer ? null : game.Player.transform) : remote && remote.Ready ? remote.transform : null;
                playerMarks[i].gameObject.SetActive(t);
                if (!t) continue;
                bool over = remote && remote == hoveredPlayer, target = remote && remote == followed;
                if (remote)
                {
                    markCores[i].color = remote.Color;
                    // Sous son pseudo : comment le choisir (survolé), ou le rappel qu'il est la destination
                    bool pad = GameInput.UsingGamepad;
                    string note = over && target ? (pad ? Loc.T("Y : ne plus le suivre", "Y: stop following") : Loc.T("Clic : ne plus le suivre", "Click: stop following"))
                        : over ? (pad ? Loc.T("A : le rejoindre", "A: drive to this player") : Loc.T("Clic : le rejoindre", "Click: drive to this player"))
                        : target ? Loc.T("destination du GPS", "GPS destination") : null;
                    markNames[i].text = note == null ? remote.Name : remote.Name + "\n<size=16><color=#FFFFFFD8>" + note + "</color></size>";
                }
                // Joueur suivi : son repère est cerclé de cyan
                if (markRings[i].gameObject.activeSelf != target) markRings[i].gameObject.SetActive(target);
                if (target) markRings[i].transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.07f);
                playerMarks[i].anchoredPosition = Local(t.position.x, t.position.z);
                markArrows[i].localEulerAngles = new Vector3(0f, 0f, -t.eulerAngles.y);
                float pulse = i == 0 ? 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.08f : over ? 1.15f : target ? 1f : 0.85f;
                playerMarks[i].localScale = Vector3.one * (pulse / zoom);
            }
            // Le joueur reste au-dessus de tout le reste
            if (playerMarks.Count > 0) playerMarks[0].SetAsLastSibling();
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
            UpdateHover();
            RefreshCard();
            Declutter();
            RefreshScale();
            if (GameInput.UsingGamepad != hintsForPad) BuildHints(GameInput.UsingGamepad);
        }

        /// <summary>Échelle : une barre d'une longueur ronde (100 m … 2 km) d'au plus 150 unités à l'écran ; la pilule suit sa longueur.</summary>
        void RefreshScale()
        {
            float unitsPerMetre = zoom / MetresPerUnit;
            float best = 100f;
            foreach (float len in new[] { 100f, 200f, 250f, 500f, 1000f, 2000f })
                if (len * unitsPerMetre <= 150f) best = len;
            float bar = best * unitsPerMetre;
            scaleBar.sizeDelta = new Vector2(bar, scaleBar.sizeDelta.y);
            scaleText.text = best >= 1000f ? Loc.F($"{best / 1000f:0} km") : Loc.F($"{best:0} m");
            const float pad = 24f, gap = 15f;
            float textWidth = scaleText.preferredWidth + 4f;
            scaleText.rectTransform.anchoredPosition = new Vector2(pad + bar + gap, 0f);
            scaleText.rectTransform.sizeDelta = new Vector2(textWidth, 30f);
            scalePill.sizeDelta = new Vector2(pad + bar + gap + textWidth + pad - 4f, PillHeight);
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

            shade = Rect(root.transform, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.gameObject.AddComponent<Image>().color = Back;

            // Mise en page : plaque de verre en haut à gauche, la carte occupe la zone à sa droite, entre la rangée
            // échelle / boussole en haut et la pilule des touches en bas (écrans 16:9 comme 4:3)
            builtAspect = Screen.width / (float)Mathf.Max(Screen.height, 1);
            float canvasWidth = 1080f * builtAspect;
            float sideWidth = Mathf.Clamp(canvasWidth - ViewSize - Margin * 3f, 400f, SideWidth);
            float openLeft = Margin * 2f + sideWidth, openRight = canvasWidth - Margin;
            float openTop = 1080f - Margin - CompassSize - 12f, openBottom = Margin + HintHeight + 12f;
            openOffset = new Vector2((openLeft + openRight) * 0.5f - canvasWidth * 0.5f, (openTop + openBottom) * 0.5f - 540f);
            openHalf = new Vector2((openRight - openLeft) * 0.5f, (openTop - openBottom) * 0.5f);
            // Au plus loin : la zone où l'on roule (le centre de l'image) remplit la hauteur libre
            minZoom = Mathf.Clamp(openHalf.y * 2f / (ViewSize * 0.84f), 0.8f, 2f);

            view = Rect(shade, "View", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            view.gameObject.AddComponent<RectMask2D>();
            content = Box(view, "Content", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize));
            var mapImage = Box(content, "Image", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize)).gameObject.AddComponent<RawImage>();
            mapImage.texture = Resources.Load<Texture2D>("Map/map_overview");
            mapImage.color = mapImage.texture ? Color.white : new Color(0.2f, 0.3f, 0.22f);
            mapImage.raycastTarget = false;

            BuildPlaces(map);

            // Itinéraire du GPS (sous les repères) et destination
            mapRoute = Box(content, "Route", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize)).gameObject.AddComponent<RouteLine>();
            mapRoute.core = Info;
            mapRoute.edge = Ink;
            mapRoute.raycastTarget = false;

            // Courses : le départ de chaque tracé ; son parcours (un tour pour une boucle, au-dessus de l'itinéraire du
            // GPS) et, pour un sprint, son arrivée, masqués tant que le départ n'est pas survolé
            if (game.Races)
                foreach (var t in game.Races.All)
                {
                    var line = Box(content, "Race_" + t.id, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize)).gameObject.AddComponent<RouteLine>();
                    line.core = t.Tint;
                    line.edge = Ink;
                    line.raycastTarget = false;
                    var strip = new List<Vector2>();
                    foreach (var p in t.path)
                    {
                        Vector2 q = Local(p.x, p.z);
                        if (strip.Count == 0 || (q - strip[strip.Count - 1]).sqrMagnitude > 0.25f) strip.Add(q);
                    }
                    strip.Add(Local(t.Finish.x, t.Finish.z));
                    line.strips.Add(strip);
                    line.transform.SetSiblingIndex(1);
                    line.gameObject.SetActive(false);
                    raceLines.Add(line);
                    var label = RaceMark(t, t.start.pos, true, t.side);
                    raceLabels.Add(label);
                    raceStarts.Add((RectTransform)label.transform.parent);
                    AddName(label, raceStarts[raceStarts.Count - 1], 4, RaceNameZoom, 21f, false, raceLabels.Count - 1);
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
            BuildGoals();

            // Ordre : image, itinéraire du GPS, tracés des courses, puis les repères
            mapRoute.transform.SetSiblingIndex(1);

            // Destination : losange cyan cerclé d'encre, nom au-dessus quand ce n'est pas déjà un lieu de la carte
            pin = Box(content, "Destination", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            foreach (var (size, color) in new[] { (28f, Ink), (19f, Info), (6f, Ink) })
            {
                var layer = Box(pin, "Layer", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<Image>();
                layer.color = color;
                layer.raycastTarget = false;
                layer.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            }
            pinLabel = MapLabel(pin, 22, TextAnchor.LowerCenter, Info, new Vector2(0f, 20f), FontStyle.BoldAndItalic);
            pinLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            pin.gameObject.SetActive(false);

            // Joueurs : la voiture locale ; ceux des autres joueurs s'ajoutent à l'affichage (Refresh)
            playerMarks.Add(PlayerMark(0));

            // Viseur au centre de la zone libre (manette, clavier) : la destination se pose dessous
            cross = Box(view, "Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            var ring = Box(cross, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<HudRing>();
            ring.inner = 13f;
            ring.outer = 15.5f;
            ring.color = new Color(1f, 1f, 1f, 0.9f);
            foreach (var (pos, size) in new[] { (new Vector2(0f, 22f), new Vector2(2.5f, 10f)), (new Vector2(0f, -22f), new Vector2(2.5f, 10f)),
                                               (new Vector2(22f, 0f), new Vector2(10f, 2.5f)), (new Vector2(-22f, 0f), new Vector2(10f, 2.5f)) })
            {
                var back = Box(cross, "Back", new Vector2(0.5f, 0.5f), pos, size + new Vector2(3f, 3f)).gameObject.AddComponent<Image>();
                back.color = new Color(0f, 0f, 0f, 0.6f);
                back.raycastTarget = false;
                var tick = Box(cross, "Tick", new Vector2(0.5f, 0.5f), pos, size).gameObject.AddComponent<Image>();
                tick.color = Color.white;
                tick.raycastTarget = false;
            }
            cross.gameObject.SetActive(false);

            BuildCard();

            BuildSide(sideWidth);
            BuildCompass();

            // Touches : pilule de verre en bas à droite (BuildHints), rétrécie si elle ne tient pas dans la largeur
            hintRow = Panel(shade, "Hints", new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(200f, HintHeight), HintHeight * 0.5f);
            hintMaxWidth = canvasWidth - Margin * 2f;
            root.SetActive(false);
        }

        /// <summary>
        /// Lieux de la carte, par importance : villages (nom toujours écrit), hameaux et sites (lac, cols, gorge, pistes :
        /// nom en italique, écrits en zoomant), concession et garage de tuning (pastille de couleur avec un symbole).
        /// </summary>
        void BuildPlaces(MapData map)
        {
            if (map.Json.pois == null) return;
            foreach (var poi in map.Json.pois)
            {
                if (poi.id == "depart" || string.IsNullOrEmpty(poi.fr)) continue;
                var mark = Box(content, "Poi_" + poi.id, new Vector2(0.5f, 0.5f), Local(poi.x, poi.z), new Vector2(10f, 10f));
                bool left = poi.side < 0;
                fixedSize.Add(mark);
                bool dealer = poi.id == "dealer", tuning = poi.id == "tuning", school = poi.id == "school", airport = poi.id == "airport";
                if (dealer || tuning || school || airport)
                {
                    Color tint = school ? new Color(1f, 0.76f, 0.2f) : airport ? new Color(0.5f, 0.92f, 0.62f) : dealer ? Info : Tuning;
                    Disc(mark, 32f, Ink);
                    Disc(mark, 26f, tint);
                    var glyph = Label(mark, school ? "⚑" : airport ? "✈" : dealer ? "€" : "⚙", dealer || school || airport ? 19 : 21, TextAnchor.MiddleCenter, Ink, new Vector2(0f, 0.5f), new Vector2(30f, 30f), FontStyle.Bold);
                    glyph.raycastTarget = false;
                    var tag = MapLabel(mark, 21, left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, tint, new Vector2(left ? -24f : 24f, 0f), FontStyle.BoldAndItalic);
                    tag.rectTransform.pivot = new Vector2(left ? 1f : 0f, 0.5f);
                    Loc.Bind(tag, poi.fr, poi.en);
                    AddName(tag, mark, 0, 0f, 32f, true);
                    continue;
                }
                bool hamlet = poi.id.StartsWith("h_");
                bool site = poi.side == 0 && !hamlet || poi.id == "pistes";
                if (site)
                {
                    // Site naturel : nom seul, centré sur le lieu, en italique (bleu pour l'eau) ; le lac est toujours écrit
                    bool water = poi.id == "lac" || poi.id == "plage";
                    var name = MapLabel(mark, poi.id == "lac" ? 21 : 18, TextAnchor.MiddleCenter, water ? new Color(0.8f, 0.93f, 1f) : new Color(0.96f, 0.95f, 0.88f), Vector2.zero, FontStyle.Italic);
                    Loc.Bind(name, poi.fr, poi.en);
                    AddName(name, mark, 2, poi.id == "lac" ? 0f : SiteZoom, 0f, false);
                    continue;
                }
                float size = hamlet ? 9f : 12f;
                Disc(mark, size + 5f, Ink);
                Disc(mark, size, Color.white);
                var label = MapLabel(mark, hamlet ? 18 : 23, left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, Color.white,
                    new Vector2(left ? -size * 0.5f - 9f : size * 0.5f + 9f, 0f), hamlet ? FontStyle.Normal : FontStyle.Bold);
                label.rectTransform.pivot = new Vector2(left ? 1f : 0f, 0.5f);
                Loc.Bind(label, poi.fr, poi.en);
                AddName(label, mark, hamlet ? 3 : 1, hamlet ? HamletZoom : 0f, size + 5f, true);
            }
        }

        /// <summary>
        /// Plaque de gauche : titre « CARTE » fin et espacé, filet or et cyan, nom du massif, lieu actuel, puis la légende
        /// sur deux colonnes (mêmes couleurs que l'image : Tools/Map/map_overview.py). Hauteur ajustée au contenu.
        /// </summary>
        void BuildSide(float width)
        {
            const float pad = 33f;
            float inner = width - pad * 2f;
            var plate = Panel(shade, "Side", new Vector2(0f, 1f), new Vector2(Margin, -Margin), new Vector2(width, 400f), PlateRadius);

            float y = -24f;
            var title = Glass.Label(plate, "", 48, Glass.Weight.Light, Glass.White, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(pad - 2f, y), new Vector2(inner, 62f));
            Loc.Bind(title, Glass.Spaced("CARTE", 2), Glass.Spaced("MAP", 2));
            y -= 66f;
            // Filet or puis cyan sous le titre
            Shape(plate, "RuleGold", new Vector2(0f, 1f), new Vector2(pad + 60f, y), new Vector2(120f, 4.5f), 2.25f, Glass.Gold);
            Shape(plate, "RuleCyan", new Vector2(0f, 1f), new Vector2(pad + 127.5f + 19.5f, y), new Vector2(39f, 4.5f), 2.25f, Glass.Cyan);
            y -= 14f;
            var sub = Glass.Label(plate, "", 15, Glass.Weight.Semilight, Glass.Faint, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(pad, y), new Vector2(inner, 22f));
            Loc.Bind(sub, Glass.Spaced("LES TROIS VALLONS", 1), Glass.Spaced("THREE VALLEYS", 1));
            y -= 40f;
            var here = Glass.Kicker(plate, "", 17);
            TopLeft(here.rectTransform, new Vector2(pad, y), new Vector2(inner, 24f));
            Loc.Bind(here, Glass.Spaced("VOUS ÊTES ICI", 2), Glass.Spaced("YOU ARE HERE", 2));
            y -= 28f;
            placeText = Glass.Label(plate, "", 28, Glass.Weight.Regular, Glass.White, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(pad, y), new Vector2(inner, 40f));
            placeText.horizontalOverflow = HorizontalWrapMode.Wrap;
            placeText.verticalOverflow = VerticalWrapMode.Truncate;
            placeText.resizeTextForBestFit = true;
            placeText.resizeTextMinSize = 16;
            placeText.resizeTextMaxSize = 28;
            y -= 40f + 16f;
            // Filet fin entre le lieu et la légende
            var line = Glass.Bar(plate, "Line", Glass.A(Color.white, 0.22f));
            TopLeft(line.rectTransform, new Vector2(pad, y), new Vector2(inner, 1.5f));
            y -= 16f;
            var legendTitle = Glass.Kicker(plate, "", 15);
            TopLeft(legendTitle.rectTransform, new Vector2(pad, y), new Vector2(inner, 22f));
            Loc.Bind(legendTitle, Glass.Spaced("LÉGENDE", 2), Glass.Spaced("LEGEND", 2));
            y -= 30f;

            // Légende sur deux colonnes : routes, tunnel et voiture à gauche ; eau, forêt, village, courses, objectifs à droite
            const float rowStep = 34f;
            var roads = new (Color32 fill, Color32 edge, float w, string fr, string en)[]
            {
                (new Color32(255, 168, 60, 255), new Color32(96, 44, 14, 255), 9f, "Autoroute", "Motorway"),
                (new Color32(255, 255, 252, 255), new Color32(66, 70, 80, 255), 7f, "Route principale", "Main road"),
                (new Color32(255, 222, 128, 255), new Color32(104, 78, 34, 255), 6f, "Route de montagne", "Mountain road"),
                (new Color32(238, 234, 224, 255), new Color32(92, 92, 98, 255), 4.5f, "Chemin", "Lane"),
                (new Color32(212, 176, 128, 255), new Color32(104, 76, 48, 255), 3.5f, "Chemin de terre", "Dirt track"),
            };
            float colW = (inner - 16f) * 0.5f, col0 = pad, col1 = pad + colW + 16f, top = y - rowStep * 0.5f;
            for (int i = 0; i < roads.Length; i++)
            {
                var r = roads[i];
                var row = LegendRow(plate, col0, top - i * rowStep, colW, r.fr, r.en);
                // Trait arrondi de la couleur de la route, cerné de son bord (comme sur l'image)
                Shape(row, "Swatch", new Vector2(0f, 0.5f), new Vector2(17f, 0f), new Vector2(32f, r.w + 3f), 8f, r.fill, 1.5f, r.edge);
            }
            {
                // Tunnel : pointillé
                var row = LegendRow(plate, col0, top - roads.Length * rowStep, colW, "Tunnel", "Tunnel");
                for (int k = 0; k < 3; k++)
                    Shape(row, "Dash", new Vector2(0f, 0.5f), new Vector2(5f + k * 12f, 0f), new Vector2(8f, 7f), 2f, new Color32(255, 168, 60, 255), 1.5f, new Color32(96, 44, 14, 255));
            }
            {
                var row = LegendRow(plate, col0, top - (roads.Length + 1) * rowStep, colW, "Votre voiture", "Your car");
                // Repère du joueur en petit : disque blanc cerclé d'or, flèche d'encre
                var disc = Shape(row, "Player", new Vector2(0f, 0.5f), new Vector2(17f, 0f), new Vector2(23f, 23f), 11.5f, Color.white, 3f, Glass.Gold);
                disc.halo = 4f;
                disc.haloColor = Glass.A(Glass.Gold, 0.4f);
                var arrow = Box(row, "Arrow", new Vector2(0f, 0.5f), new Vector2(17f, 0.5f), new Vector2(10f, 11f)).gameObject.AddComponent<HudArrow>();
                arrow.color = Glass.Ink;
                arrow.raycastTarget = false;
            }
            {
                var row = LegendRow(plate, col1, top, colW, "Lac, rivière", "Lake, river");
                Shape(row, "Swatch", new Vector2(0f, 0.5f), new Vector2(17f, 0f), new Vector2(32f, 16f), 5f, new Color32(78, 150, 212, 255), 1.5f, new Color32(206, 232, 246, 255));
            }
            {
                var row = LegendRow(plate, col1, top - rowStep, colW, "Forêt", "Forest");
                Shape(row, "Swatch", new Vector2(0f, 0.5f), new Vector2(17f, 0f), new Vector2(30f, 15f), 5f, new Color32(62, 112, 74, 255));
            }
            {
                var row = LegendRow(plate, col1, top - 2f * rowStep, colW, "Village", "Village");
                Swatch(row, new Color32(104, 88, 84, 255), new Vector2(9f, 9f), -10f);
                Swatch(row, new Color32(104, 88, 84, 255), new Vector2(7f, 12f), 1f);
                Swatch(row, new Color32(104, 88, 84, 255), new Vector2(8f, 7f), 11f);
            }
            // Courses : une ligne par genre (le nom est écrit au départ en zoomant, le détail et le record au survol)
            RaceSwatch(LegendRow(plate, col1, top - 3f * rowStep, colW, "Course : sprint", "Race: sprint"), Races.SprintTint);
            RaceSwatch(LegendRow(plate, col1, top - 4f * rowStep, colW, "Course : tours", "Race: laps"), Races.LapsTint);
            GoalSwatch(LegendRow(plate, col1, top - 5f * rowStep, colW, "Radar", "Speed camera"), HudIcon.Kind.RadarPin, SpeedCameras.Tint, false);
            GoalSwatch(LegendRow(plate, col1, top - 6f * rowStep, colW, "Tremplin", "Stunt jump"), HudIcon.Kind.RampPin, StuntJumps.Tint, true);
            DoneSwatch(LegendRow(plate, col1, top - 7f * rowStep, colW, "Réussi", "Completed"));
            y -= 8f * rowStep;
            plate.sizeDelta = new Vector2(width, -y + 26f);
        }

        /// <summary>En haut à droite : pilule de l'échelle (longueur suivie par RefreshScale) et boussole ronde, nord en triangle doré.</summary>
        void BuildCompass()
        {
            var compass = Panel(shade, "Compass", new Vector2(1f, 1f), new Vector2(-Margin, -Margin), new Vector2(CompassSize, CompassSize), CompassSize * 0.5f);
            var north = Box(compass, "North", new Vector2(0.5f, 0.5f), new Vector2(0f, 11f), new Vector2(18f, 18f)).gameObject.AddComponent<HudArrow>();
            north.color = Glass.Gold;
            north.raycastTarget = false;
            Glass.Label(compass, "N", 19, Glass.Weight.Semibold, Glass.White, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(30f, 24f));

            scalePill = Panel(shade, "Scale", new Vector2(1f, 1f), new Vector2(-Margin - CompassSize - 15f, -Margin - (CompassSize - PillHeight) * 0.5f), new Vector2(200f, PillHeight), PillHeight * 0.5f);
            // Barre d'échelle en crochet : base fine, extrémités relevées
            scaleBar = Box(scalePill, "Bar", new Vector2(0f, 0.5f), new Vector2(24f, -1f), new Vector2(100f, 10f));
            scaleBar.pivot = new Vector2(0f, 0.5f);
            var baseLine = Glass.Bar(scaleBar, "Base", Color.white).rectTransform;
            baseLine.anchorMin = Vector2.zero; baseLine.anchorMax = new Vector2(1f, 0f);
            baseLine.offsetMin = Vector2.zero; baseLine.offsetMax = new Vector2(0f, 2f);
            foreach (float end in new[] { 0f, 1f })
            {
                var tick = Glass.Bar(scaleBar, "Tick", Color.white).rectTransform;
                tick.anchorMin = new Vector2(end, 0f); tick.anchorMax = new Vector2(end, 1f);
                tick.offsetMin = new Vector2(end > 0f ? -2f : 0f, 0f); tick.offsetMax = new Vector2(end > 0f ? 0f : 2f, 0f);
            }
            scaleText = Glass.Label(scalePill, "", 19, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(140f, 0f), new Vector2(80f, 30f));
        }

        /// <summary>Touches de la carte dans la pilule du bas (touche dorée, action en blanc fin), selon la manette ou le clavier.</summary>
        void BuildHints(bool pad)
        {
            hintsForPad = pad;
            for (int i = hintRow.childCount - 1; i >= 0; i--) Destroy(hintRow.GetChild(i).gameObject);
            var keys = pad
                ? new[] { ("L", Loc.T("Déplacer", "Move")), ("LT / RT", "Zoom"), ("A", "Destination"), ("Y", Loc.T("Effacer", "Clear")),
                          ("X", Loc.T("Ma position", "My position")), ("B", Loc.T("Fermer", "Close")) }
                : new[] { (Loc.T("Glisser", "Drag"), Loc.T("Déplacer", "Move")), (Loc.T("Molette", "Wheel"), "Zoom"), (Loc.T("Clic", "Click"), "Destination"),
                          (Loc.T("Suppr", "Del"), Loc.T("Effacer", "Clear")), (Loc.T("Espace", "Space"), Loc.T("Ma position", "My position")), (Loc.T("Échap", "Esc"), Loc.T("Fermer", "Close")) };
            const float side = 36f, gap = 27f;
            float xPos = side;
            foreach (var (key, action) in keys)
            {
                var keyText = Glass.Label(hintRow, key, 21, Glass.Weight.Semibold, Glass.Gold, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(xPos, 0f), new Vector2(10f, 30f));
                float keyWidth = keyText.preferredWidth + 2f;
                keyText.rectTransform.sizeDelta = new Vector2(keyWidth, 30f);
                xPos += keyWidth + 9f;
                var act = Glass.Label(hintRow, action, 21, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(xPos, 0f), new Vector2(10f, 30f));
                float actWidth = act.preferredWidth + 2f;
                act.rectTransform.sizeDelta = new Vector2(actWidth, 30f);
                xPos += actWidth + gap;
            }
            float width = xPos - gap + side;
            hintRow.sizeDelta = new Vector2(width, HintHeight);
            // Trop large pour l'écran (écran étroit) : la pilule rétrécit depuis son coin
            hintRow.localScale = Vector3.one * Mathf.Min(1f, hintMaxWidth / Mathf.Max(width, 1f));
        }

        /// <summary>
        /// Repère d'une course, de la couleur de son tracé : pastille au départ (étiquette à gauche, au-dessus ou à
        /// droite selon <paramref name="side"/>, là où la carte est dégagée ; écrite en zoomant ou au survol), losange à
        /// l'arrivée d'un sprint (étiquette dessous). Renvoie l'étiquette.
        /// </summary>
        Text RaceMark(Races.Track t, Vector3 at, bool start, int side)
        {
            var mark = Box(content, (start ? "RaceStart_" : "RaceFinish_") + t.id, new Vector2(0.5f, 0.5f), Local(at.x, at.z), new Vector2(10f, 10f));
            var layers = new (float size, Color color)[] { (start ? 21f : 17f, Ink), (start ? 16f : 12f, t.Tint), (start ? 6f : 5f, Color.white) };
            foreach (var (size, color) in layers)
            {
                var img = Box(mark, "Layer", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<Image>();
                if (start) img.sprite = HudArt.Disc;
                else img.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
                img.color = color;
                img.raycastTarget = false;
            }
            Text label;
            if (start && side != 0)
            {
                label = MapLabel(mark, 19, side < 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, t.Tint, new Vector2(side * 17f, 0f), FontStyle.BoldAndItalic);
                label.rectTransform.pivot = new Vector2(side < 0 ? 1f : 0f, 0.5f);
            }
            else
            {
                label = MapLabel(mark, start ? 19 : 17, start ? TextAnchor.LowerCenter : TextAnchor.UpperCenter, t.Tint,
                    new Vector2(0f, start ? 14f : -13f), FontStyle.BoldAndItalic);
                label.rectTransform.pivot = new Vector2(0.5f, start ? 0f : 1f);
            }
            fixedSize.Add(mark);
            return label;
        }

        /// <summary>
        /// Repère survolé par le curseur (ou par le viseur, à la manette et aux flèches), le plus proche l'emporte.
        /// Départ de course : son tracé et l'arrivée d'un sprint s'affichent, le repère grossit et son étiquette se
        /// détaille (les autres tracés restent masqués, leurs noms ne s'écrivent qu'en zoomant). Radar ou tremplin : le
        /// repère grossit et son étiquette se détaille de même.
        /// </summary>
        void UpdateHover()
        {
            if (raceStarts.Count == 0 && goalMarks.Count == 0) return;
            // Un joueur désigné passe avant le repère sur lequel il se trouve (c'est lui que la validation choisit)
            bool has = Pointed(out var local) && !hoveredPlayer;
            int hover = -1, goal = -1;
            if (has)
            {
                float best = RaceHoverRadius;
                for (int i = 0; i < raceStarts.Count; i++)
                {
                    float d = (local - raceStarts[i].anchoredPosition).magnitude * zoom;
                    if (d < best) { best = d; hover = i; }
                }
                for (int i = 0; i < goalMarks.Count; i++)
                {
                    float d = (local - goalMarks[i].anchoredPosition).magnitude * zoom;
                    if (d < best) { best = d; goal = i; hover = -1; }
                }
            }
            if (goal != goalHover)
            {
                goalHover = goal;
                if (goal >= 0) goalMarks[goal].SetAsLastSibling();
                RefreshGoals();
            }
            if (goal >= 0) goalMarks[goal].localScale = Vector3.one * (1.3f / zoom);
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

        /// <summary>Nom posé sur la carte : priorité (0 en premier), zoom minimal, taille de son repère, place d'origine.</summary>
        class MapName
        {
            public Text text;
            public RectTransform mark;
            public int priority, race = -1, goal = -1;
            public float minZoom, dot;
            public bool movable;
            public Vector2 basePos;
            public float basePivot;
            public TextAnchor baseAlign;
        }

        void AddName(Text text, RectTransform mark, int priority, float minZoom, float dot, bool movable, int race = -1, int goal = -1)
        {
            var rt = text.rectTransform;
            var name = new MapName { text = text, mark = mark, priority = priority, minZoom = minZoom, dot = dot, movable = movable, race = race, goal = goal,
                                     basePos = rt.anchoredPosition, basePivot = rt.pivot.x, baseAlign = text.alignment };
            // ordre de placement : priorité, puis ordre de construction
            int at = names.Count;
            while (at > 0 && names[at - 1].priority > priority) at--;
            names.Insert(at, name);
        }

        /// <summary>
        /// Noms sans chevauchement : dans l'ordre des priorités (départ survolé, services, villages, sites, hameaux,
        /// courses), chaque nom essaie sa place d'origine, puis l'autre côté de son repère, au-dessus, au-dessous ; s'il
        /// ne tient nulle part il est masqué (sauf les services et les villages, toujours écrits). Les flèches des joueurs,
        /// la destination et les pastilles des repères comptent comme des obstacles.
        /// </summary>
        void Declutter()
        {
            taken.Clear();
            dots.Clear();
            dotOwners.Clear();
            foreach (var p in playerMarks)
                if (p.gameObject.activeSelf) taken.Add(Centered(p.anchoredPosition * zoom, 56f));
            if (pin.gameObject.activeSelf) taken.Add(Centered(pin.anchoredPosition * zoom, 28f));
            for (int i = 0; i < names.Count; i++)
                if (names[i].dot > 0f && names[i].mark.gameObject.activeInHierarchy)
                {
                    var dot = Centered(names[i].mark.anchoredPosition * zoom, names[i].dot);
                    // Concession et garage : leur pastille n'est jamais recouverte par un nom
                    if (names[i].priority == 0) taken.Add(dot);
                    else
                    {
                        dots.Add(dot);
                        dotOwners.Add(i);
                    }
                }
            int first = -1;
            for (int i = 0; i < names.Count; i++)
                if (names[i].race >= 0 && names[i].race == raceHover || names[i].goal >= 0 && names[i].goal == goalHover) first = i;
            // Repère survolé : sa fiche (RefreshCard) remplace son nom
            if (first >= 0) names[first].text.enabled = false;
            for (int i = 0; i < names.Count; i++)
                if (i != first) Place(i);
        }

        void Place(int i)
        {
            var n = names[i];
            if (!n.mark.gameObject.activeInHierarchy) return;
            if (zoom < n.minZoom)
            {
                n.text.enabled = false;
                return;
            }
            // Premier essai : aucune gêne ; second essai : les pastilles des repères voisins peuvent passer dessous
            int tries = n.movable ? 4 : 1, chosen = -1;
            for (int pass = 0; pass < 2 && chosen < 0; pass++)
                for (int k = 0; k < tries && chosen < 0; k++)
                {
                    Arrange(n, k);
                    if (Free(NameRect(n), i, pass == 0)) chosen = k;
                }
            if (chosen < 0)
            {
                Arrange(n, 0);
                if (n.priority <= 1) chosen = 0;
            }
            n.text.enabled = chosen >= 0;
            if (chosen >= 0) taken.Add(NameRect(n));
        }

        /// <summary>Place k d'un nom : 0 d'origine, 1 de l'autre côté du repère, 2 au-dessus, 3 au-dessous.</summary>
        static void Arrange(MapName n, int k)
        {
            var rt = n.text.rectTransform;
            float gap = n.dot * 0.5f + 3f;
            switch (k)
            {
                case 1:
                    rt.anchoredPosition = new Vector2(-n.basePos.x, n.basePos.y);
                    rt.pivot = new Vector2(1f - n.basePivot, 0.5f);
                    n.text.alignment = n.baseAlign == TextAnchor.MiddleLeft ? TextAnchor.MiddleRight : n.baseAlign == TextAnchor.MiddleRight ? TextAnchor.MiddleLeft : n.baseAlign;
                    break;
                case 2:
                    rt.anchoredPosition = new Vector2(0f, gap);
                    rt.pivot = new Vector2(0.5f, 0f);
                    n.text.alignment = TextAnchor.LowerCenter;
                    break;
                case 3:
                    rt.anchoredPosition = new Vector2(0f, -gap);
                    rt.pivot = new Vector2(0.5f, 1f);
                    n.text.alignment = TextAnchor.UpperCenter;
                    break;
                default:
                    rt.anchoredPosition = n.basePos;
                    rt.pivot = new Vector2(n.basePivot, n.movable ? 0.5f : rt.pivot.y);
                    n.text.alignment = n.baseAlign;
                    break;
            }
        }

        /// <summary>Rectangle d'un nom à l'écran (unités du canevas, origine au centre de la carte), un peu rogné.</summary>
        Rect NameRect(MapName n)
        {
            var rt = n.text.rectTransform;
            // Repère grossi (survolé) : son nom l'est aussi
            float s = n.mark.localScale.x * zoom;
            float w = n.text.preferredWidth * s, h = n.text.preferredHeight * 0.85f * s;
            Vector2 at = n.mark.anchoredPosition * zoom + rt.anchoredPosition * s;
            return new Rect(at.x - rt.pivot.x * w + 2f, at.y - rt.pivot.y * h + 2f, w - 4f, h - 4f);
        }

        bool Free(Rect r, int owner, bool withDots)
        {
            foreach (var t in taken)
                if (t.Overlaps(r)) return false;
            if (!withDots) return true;
            for (int k = 0; k < dots.Count; k++)
                if (dotOwners[k] != owner && dots[k].Overlaps(r)) return false;
            return true;
        }

        static Rect Centered(Vector2 at, float size) => new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size);

        /// <summary>Étiquette de chaque départ : le nom du tracé (le détail est sur la fiche du survol).</summary>
        void RefreshRaceLabels()
        {
            if (!game.Races) return;
            var races = game.Races.All;
            for (int i = 0; i < raceLabels.Count && i < races.Count; i++) raceLabels[i].text = races[i].Name;
        }

        /// <summary>
        /// Repères des objectifs : radars (pastille à centre sombre) et tremplins (losange), avec leur nom et leur objectif
        /// écrits en zoomant.
        /// </summary>
        void BuildGoals()
        {
            if (game.Radars)
                foreach (var r in game.Radars.All)
                {
                    var mark = Box(content, "Radar_" + r.data.id, new Vector2(0.5f, 0.5f), Local(r.center.x, r.center.z), new Vector2(10f, 10f));
                    radarChecks.Add(HudIcon.Badge(mark, Vector2.zero, GoalBadge, HudIcon.Kind.RadarPin, SpeedCameras.Tint, false));
                    radarLabels.Add(GoalLabel(mark, SpeedCameras.Tint));
                }
            if (game.Stunts)
                foreach (var s in game.Stunts.All)
                {
                    var mark = Box(content, "Stunt_" + s.data.id, new Vector2(0.5f, 0.5f), Local(s.lip.x, s.lip.z), new Vector2(10f, 10f));
                    stuntChecks.Add(HudIcon.Badge(mark, Vector2.zero, GoalBadge, HudIcon.Kind.RampPin, StuntJumps.Tint, true));
                    stuntLabels.Add(GoalLabel(mark, StuntJumps.Tint));
                }
        }

        Text GoalLabel(RectTransform mark, Color tint)
        {
            var label = MapLabel(mark, 17, TextAnchor.MiddleLeft, tint, new Vector2(GoalBadge * 0.5f + 9f, 0f), FontStyle.BoldAndItalic);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            fixedSize.Add(mark);
            AddName(label, mark, 5, RaceNameZoom, GoalBadge + 4f, true, -1, goalMarks.Count);
            goalMarks.Add(mark);
            return label;
        }

        /// <summary>
        /// État des objectifs : la pastille garde sa couleur ; une coche verte s'y ajoute une fois réussi, et l'étiquette
        /// passe de ce qu'il faut atteindre au record (le détail est sur la fiche du survol).
        /// </summary>
        void RefreshGoals()
        {
            if (game.Radars)
            {
                var all = game.Radars.All;
                for (int i = 0; i < radarChecks.Count && i < all.Count; i++)
                {
                    var r = all[i];
                    if (radarChecks[i].activeSelf != r.done) radarChecks[i].SetActive(r.done);
                    radarLabels[i].text = r.done
                        ? Loc.F($"{r.Name}  ·  record {r.best:0} km/h", $"{r.Name}  ·  best {r.best:0} km/h")
                        : Loc.F($"{r.Name}  ·  {r.Target:0} km/h");
                }
            }
            if (game.Stunts)
            {
                var all = game.Stunts.All;
                for (int i = 0; i < stuntChecks.Count && i < all.Count; i++)
                {
                    var s = all[i];
                    if (stuntChecks[i].activeSelf != s.done) stuntChecks[i].SetActive(s.done);
                    stuntLabels[i].text = !s.done ? Loc.F($"{s.Name}  ·  {s.Target:0} m")
                        : s.best > 0f ? Loc.F($"{s.Name}  ·  record {s.best:0} m", $"{s.Name}  ·  best {s.best:0} m")
                        : s.Name;
                }
            }
        }

        // ------------------------------------------------------------------ fiche du survol

        /// <summary>
        /// Fiche du repère survolé, construite une fois : carte de verre, genre en petit titre de la couleur du repère
        /// (avec sa pastille), nom en texte fin, filet, lignes libellé / valeur, état (coche verte quand c'est réussi),
        /// puis la touche dorée pour s'y rendre au GPS. Remplie par <see cref="FillCard"/>.
        /// </summary>
        void BuildCard()
        {
            card = Box(view, "HoverCard", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(435f, 200f));
            card.pivot = new Vector2(0f, 1f);
            cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            cardGroup.blocksRaycasts = false;
            cardGroup.interactable = false;
            Glass.Backdrop(card, 33f, Glass.A(Glass.PanelStrong, 0.93f));   // posée sur la carte chargée : presque opaque pour rester lisible

            cardDot = Shape(card, "Dot", new Vector2(0f, 1f), new Vector2(CardPad + 7.5f, -38f), new Vector2(15f, 15f), 7.5f, Accent);
            cardKicker = Glass.Label(card, "", 16, Glass.Weight.Semibold, Accent, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(CardPad + 27f, -38f), new Vector2(10f, 24f));
            cardKicker.rectTransform.pivot = new Vector2(0f, 0.5f);
            cardName = Glass.Label(card, "", 36, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(CardPad, -80f), new Vector2(10f, 46f));
            cardName.rectTransform.pivot = new Vector2(0f, 0.5f);
            cardRule = Glass.Bar(card, "Rule", Glass.A(Color.white, 0.22f)).rectTransform;
            TopLeft(cardRule, new Vector2(CardPad, -112f), new Vector2(10f, 1.5f));

            cardState = Box(card, "State", new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, 30f));
            cardState.pivot = new Vector2(0f, 0.5f);
            cardCheck = HudIcon.Check(cardState, new Vector2(11f - 5f, 0f), 22f);
            cardTodo = Box(cardState, "Todo", new Vector2(0.5f, 0.5f), new Vector2(11f - 5f, 0f), Vector2.zero).gameObject.AddComponent<HudRing>();
            cardTodo.inner = 8.2f;
            cardTodo.outer = 10.5f;
            cardTodo.color = Dim;
            cardTodo.raycastTarget = false;
            cardStateText = Glass.Label(cardState, "", 22, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(10f, 30f));

            cardHint = Box(card, "Hint", new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, 30f));
            cardHint.pivot = new Vector2(0f, 0.5f);
            // Touche : pastille dorée, texte d'encre
            cardHintChip = Shape(cardHint, "Key", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(30f, 30f), 15f, Glass.Gold);
            cardHintKey = Glass.Label(cardHintChip.rectTransform, "", 19, Glass.Weight.Semibold, Glass.Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            cardHintText = Glass.Label(cardHint, "", 21, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(10f, 30f));
            card.gameObject.SetActive(false);
        }

        /// <summary>Fiche du repère survolé : réécrite quand il change (ou la manette), posée chaque image à côté de lui.</summary>
        void RefreshCard()
        {
            if (!card) return;
            RectTransform mark = raceHover >= 0 && raceHover < raceStarts.Count ? raceStarts[raceHover]
                : goalHover >= 0 && goalHover < goalMarks.Count ? goalMarks[goalHover] : null;
            if (!mark)
            {
                if (card.gameObject.activeSelf) card.gameObject.SetActive(false);
                cardFor = null;
                return;
            }
            string key = (raceHover >= 0 ? "r" + raceHover : "g" + goalHover) + (GameInput.UsingGamepad ? "p" : "m");
            if (key != cardFor)
            {
                bool fresh = cardFor == null || cardFor.Substring(0, cardFor.Length - 1) != key.Substring(0, key.Length - 1);
                cardFor = key;
                FillCard();
                card.gameObject.SetActive(true);
                if (fresh) cardGroup.alpha = 0f;
            }
            cardGroup.alpha = Mathf.MoveTowards(cardGroup.alpha, 1f, Time.unscaledDeltaTime * 9f);
            // À droite du repère (grossi au survol), à gauche si la place manque ; toujours dans la zone libre
            Vector2 at = content.anchoredPosition + mark.anchoredPosition * zoom;
            Vector2 size = card.sizeDelta;
            float gap = 26f + (1f - cardGroup.alpha) * 10f;
            float x = at.x + gap;
            if (x + size.x > openOffset.x + openHalf.x) x = at.x - gap - size.x;
            float top = Mathf.Clamp(at.y + size.y * 0.5f, openOffset.y - openHalf.y + size.y, openOffset.y + openHalf.y);
            card.anchoredPosition = new Vector2(x, top);
        }

        /// <summary>Contenu de la fiche selon le repère survolé (départ de course, radar ou tremplin) ; largeur et hauteur ajustées.</summary>
        void FillCard()
        {
            var rows = new List<(string key, string value, Color color)>();
            Color tint;
            string kicker, name, state = null;
            bool done = false;
            int radarCount = game.Radars ? game.Radars.All.Count : 0;
            if (raceHover >= 0)
            {
                var t = game.Races.All[raceHover];
                tint = t.Tint;
                kicker = Loc.T("COURSE", "RACE") + " · " + t.Kind;
                name = t.Name;
                rows.Add((Loc.T("Distance", "Distance"), Loc.F($"{t.Distance / 1000f:0.0} km"), Glass.White));
                if (t.Loop) rows.Add((Loc.T("Tour", "Lap"), Loc.F($"{t.length / 1000f:0.0} km"), Glass.White));
                rows.Add((Loc.T("Record", "Best time"), t.best > 0f ? Races.Clock(t.best) : "—", t.best > 0f ? Glass.Gold : Glass.Dim));
                if (t.best > 0f && !string.IsNullOrEmpty(t.bestCar)) rows.Add((Loc.T("Voiture", "Car"), t.bestCar, Glass.White));
            }
            else if (goalHover < radarCount)
            {
                var r = game.Radars.All[goalHover];
                tint = SpeedCameras.Tint;
                kicker = Loc.T("RADAR", "SPEED CAMERA");
                name = r.Name;
                rows.Add((Loc.T("Objectif", "Target"), Loc.F($"{r.Target:0} km/h minimum"), Glass.White));
                rows.Add((Loc.T("Prime", "Reward"), Wallet.Format(r.data.reward), Glass.Gold));
                if (r.done) rows.Add((Loc.T("Record", "Best"), Loc.F($"{r.best:0} km/h"), Glass.White));
                done = r.done;
                state = done ? Loc.T("Réussi", "Completed") : Loc.T("Pas encore flashé", "Not flashed yet");
            }
            else
            {
                var s = game.Stunts.All[goalHover - radarCount];
                tint = StuntJumps.Tint;
                kicker = Loc.T("TREMPLIN", "STUNT JUMP");
                name = s.Name;
                rows.Add((Loc.T("Objectif", "Target"), Loc.F($"{s.Target:0} m en l'air", $"{s.Target:0} m airborne"), Glass.White));
                rows.Add((Loc.T("Prime", "Reward"), Wallet.Format(s.data.reward), Glass.Gold));
                if (s.done && s.best > 0f) rows.Add((Loc.T("Record", "Best"), Loc.F($"{s.best:0} m"), Glass.White));
                done = s.done;
                state = done ? Loc.T("Réussi", "Completed") : Loc.T("Pas encore réussi", "Not completed yet");
            }

            // Petit titre de la couleur du repère, précédé de sa pastille
            cardDot.color = tint;
            cardKicker.text = Glass.Spaced(kicker, 2);
            cardKicker.color = tint;
            cardName.text = name;
            float width = Mathf.Max(cardName.preferredWidth, 27f + cardKicker.preferredWidth);

            // Lignes : libellé fin à gauche, valeur plus marquée à droite (primes et records en or)
            float y = -137f, bottom = -114f;
            while (cardKeys.Count < rows.Count)
            {
                var k = Glass.Label(card, "", 22, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, CardRow));
                k.rectTransform.pivot = new Vector2(0f, 0.5f);
                var v = Glass.Label(card, "", 22, Glass.Weight.Regular, Glass.White, TextAnchor.MiddleRight, new Vector2(1f, 1f), Vector2.zero, new Vector2(10f, CardRow));
                v.rectTransform.pivot = new Vector2(1f, 0.5f);
                cardKeys.Add(k);
                cardValues.Add(v);
            }
            for (int i = 0; i < cardKeys.Count; i++)
            {
                bool on = i < rows.Count;
                cardKeys[i].gameObject.SetActive(on);
                cardValues[i].gameObject.SetActive(on);
                if (!on) continue;
                cardKeys[i].text = rows[i].key;
                cardValues[i].text = rows[i].value;
                cardValues[i].color = rows[i].color;
                cardKeys[i].rectTransform.anchoredPosition = new Vector2(CardPad, y);
                cardValues[i].rectTransform.anchoredPosition = new Vector2(-CardPad, y);
                cardKeys[i].rectTransform.sizeDelta = new Vector2(cardKeys[i].preferredWidth + 4f, CardRow);
                cardValues[i].rectTransform.sizeDelta = new Vector2(cardValues[i].preferredWidth + 4f, CardRow);
                width = Mathf.Max(width, cardKeys[i].preferredWidth + 36f + cardValues[i].preferredWidth);
                bottom = y - CardRow * 0.5f;
                y -= CardRow;
            }

            // État : coche verte et « Réussi », ou cercle atténué et ce qu'il reste à faire
            cardState.gameObject.SetActive(state != null);
            if (state != null)
            {
                cardState.anchoredPosition = new Vector2(CardPad, y);
                cardCheck.SetActive(done);
                cardTodo.gameObject.SetActive(!done);
                cardStateText.text = state;
                cardStateText.color = done ? Glass.Good : Glass.Dim;
                cardStateText.rectTransform.sizeDelta = new Vector2(cardStateText.preferredWidth + 4f, 30f);
                width = Mathf.Max(width, 30f + cardStateText.preferredWidth);
                bottom = y - 15f;
                y -= CardRow;
            }

            // Touche pour s'y rendre (pas pendant une course : le GPS suit alors son parcours)
            var nav = game.Nav;
            bool hint = nav && !nav.OnCourse && !GameManager.DedicatedServer;
            cardHint.gameObject.SetActive(hint);
            if (hint)
            {
                y -= 10f;
                cardHint.anchoredPosition = new Vector2(CardPad, y);
                cardHintKey.text = GameInput.UsingGamepad ? "A" : Loc.T("Clic", "Click");
                float chip = Mathf.Max(30f, cardHintKey.preferredWidth + 36f);
                var chipRect = cardHintChip.rectTransform;
                chipRect.sizeDelta = new Vector2(chip, 30f);
                chipRect.anchoredPosition = new Vector2(chip * 0.5f, 0f);
                cardHintKey.rectTransform.sizeDelta = chipRect.sizeDelta;
                cardHintText.text = Loc.T("Y aller avec le GPS", "Set as GPS destination");
                cardHintText.rectTransform.anchoredPosition = new Vector2(chip + 15f, 0f);
                cardHintText.rectTransform.sizeDelta = new Vector2(cardHintText.preferredWidth + 4f, 30f);
                width = Mathf.Max(width, chip + 15f + cardHintText.preferredWidth);
                bottom = y - 15f;
            }

            width = Mathf.Clamp(width + CardPad * 2f, 380f, 540f);
            card.sizeDelta = new Vector2(width, -bottom + 27f);
            cardName.rectTransform.sizeDelta = new Vector2(width - CardPad * 2f, 46f);
            cardKicker.rectTransform.sizeDelta = new Vector2(cardKicker.preferredWidth + 4f, 24f);
            cardRule.sizeDelta = new Vector2(width - CardPad * 2f, 1.5f);
        }

        /// <summary>
        /// Repère d'un joueur : disque blanc et flèche orientée. Soi : cerclé d'or dans un halo doré, flèche d'encre ;
        /// les autres : plus petit, cerné d'encre, flèche de leur couleur et pseudo.
        /// </summary>
        RectTransform PlayerMark(int index)
        {
            var mark = Box(content, "Player" + index, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            bool self = index == 0;
            float size = self ? 60f : 40f;
            var disc = Shape(mark, "Disc", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), size * 0.5f, Color.white, self ? 6f : 2.5f, self ? Glass.Gold : Glass.Ink);
            if (self)
            {
                disc.halo = 10f;
                disc.haloColor = Glass.A(Glass.Gold, 0.4f);
            }
            var turn = Box(mark, "Heading", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            var core = Box(turn, "Core", new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), self ? new Vector2(23f, 26f) : new Vector2(18f, 21f)).gameObject.AddComponent<HudArrow>();
            core.color = Glass.Ink;
            core.raycastTarget = false;
            var name = MapLabel(mark, 20, TextAnchor.MiddleLeft, Color.white, new Vector2(size * 0.5f + 10f, 0f), FontStyle.Bold);
            name.rectTransform.pivot = new Vector2(0f, 0.5f);
            // Cercle du joueur suivi par le GPS (masqué sinon)
            var ring = Box(mark, "Followed", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<HudRing>();
            ring.inner = size * 0.5f + 3f;
            ring.outer = size * 0.5f + 6.5f;
            ring.color = Info;
            ring.gameObject.SetActive(false);
            markRings.Add(ring);
            markArrows.Add(turn);
            markCores.Add(core);
            markNames.Add(name);
            return mark;
        }

        // ------------------------------------------------------------------ éléments

        /// <summary>Plaque de verre posée (ancre = pivot) ; la carte ne réagit pas au curseur dessus.</summary>
        RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, float radius)
        {
            var glass = Glass.Make(parent, name, radius, Glass.PanelStrong);
            var rt = glass.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            panels.Add(rt);
            return rt;
        }

        /// <summary>Rectangle arrondi de verre centré sur <paramref name="pos"/> (pastilles, traits de légende, touches).</summary>
        static GlassRect Shape(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, float radius, Color fill, float edge = 0f, Color? edgeColor = null)
        {
            var g = Glass.Make(parent, name, radius, fill, edge);
            if (edgeColor.HasValue) g.borderColor = edgeColor.Value;
            var rt = g.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return g;
        }

        /// <summary>Ancre et pivot en haut à gauche, à la position et à la taille données.</summary>
        static void TopLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Ligne de légende : le motif se pose à gauche de la ligne (Swatch), le libellé à sa droite.</summary>
        static RectTransform LegendRow(RectTransform parent, float x, float y, float width, string fr, string en)
        {
            var row = Box(parent, "Row", new Vector2(0f, 1f), new Vector2(x + width * 0.5f, y), new Vector2(width, 30f));
            var text = Glass.Label(row, "", 20, Glass.Weight.Light, Glass.White, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(42f, 0f), new Vector2(width - 42f, 30f));
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 13;
            text.resizeTextMaxSize = 20;
            Loc.Bind(text, fr, en);
            return row;
        }

        static void Swatch(RectTransform row, Color color, Vector2 size, float dx)
        {
            var img = Box(row, "Swatch", new Vector2(0f, 0.5f), new Vector2(17f + dx, 0f), size).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        static void RaceSwatch(RectTransform row, Color tint)
        {
            var at = new Vector2(17f - row.sizeDelta.x * 0.5f, 0f);
            Disc(row, 19f, Ink, at);
            Disc(row, 14f, tint, at);
            Disc(row, 5f, Color.white, at);
        }

        static void GoalSwatch(RectTransform row, HudIcon.Kind kind, Color tint, bool diamond)
            => HudIcon.Badge(row, new Vector2(17f - row.sizeDelta.x * 0.5f, 0f), GoalBadge, kind, tint, diamond);

        static void DoneSwatch(RectTransform row)
            => HudIcon.Check(row, new Vector2(17f - row.sizeDelta.x * 0.5f, 0f), GoalBadge * 0.7f);

        static Image Disc(RectTransform parent, float size, Color color, Vector2? pos = null)
        {
            var img = Box(parent, "Disc", new Vector2(0.5f, 0.5f), pos ?? Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<Image>();
            img.sprite = HudArt.Disc;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Nom posé sur la carte : cerné d'encre et ombré pour rester lisible sur tous les fonds.</summary>
        static Text MapLabel(RectTransform parent, int size, TextAnchor align, Color color, Vector2 pos, FontStyle style)
        {
            var label = Label(parent, "", size, align, color, pos, new Vector2(520f, 30f), style);
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.03f, 0.05f, 0.9f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(2.5f, -3f);
            return label;
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
            // Polices fines du style verre : le gras devient Semibold (pas de gras synthétique), l'italique reste
            // pour les sites naturels
            bool strong = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
            t.font = Glass.FontOf(strong ? Glass.Weight.Semibold : Glass.Weight.Regular);
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = style == FontStyle.Italic ? FontStyle.Italic : FontStyle.Normal;
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
