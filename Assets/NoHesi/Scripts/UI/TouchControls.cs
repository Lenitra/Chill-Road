using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace NoHesi
{
    /// <summary>
    /// Commandes tactiles du joueur seul, dans la DA du HUD (plaques inclinées, or au toucher).
    /// À gauche : direction (glisser le pouce, boutons ‹ › ou inclinaison, réglable dans les options).
    /// À droite : pédales d'accélérateur et de frein, frein à main. En haut : pause, replacer (en course, maintenir :
    /// abandonner), regarder derrière, vue, phares, musique. Un doigt glissé ailleurs sur l'écran fait regarder autour.
    /// Les doigts sont lus directement (Enhanced Touch) : plusieurs à la fois, et on peut glisser d'une pédale à
    /// l'autre sans lever le pouce. Affichées sur mobile, ou sur PC dès que l'écran est touché ; le clavier ou la
    /// manette les masquent.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class TouchControls : MonoBehaviour
    {
        /// <summary>Tests au PC : affiche les commandes et fait agir la souris comme un doigt.</summary>
        public static bool Simulate;

        static readonly Color Plate = new Color(0.03f, 0.04f, 0.06f, 0.68f);
        static readonly Color Pressed = new Color(0.95f, 0.62f, 0.12f, 0.92f);
        static readonly Color Accent = new Color(1f, 0.76f, 0.2f);
        static readonly Color White = new Color(1f, 1f, 1f, 0.92f);
        static readonly Color Dark = new Color(0.06f, 0.06f, 0.08f);

        class Control
        {
            public RectTransform rt;
            public HudShape plate;
            public Text label;
            public bool hold;          // maintenu (pédales) ; sinon appui ponctuel
            public Action tap;
            public bool held;
            public float flash;
            public GameObject group;   // contrôles de direction : affichés selon le mode
        }

        GameManager game;
        Font font;
        GameObject root;
        RectTransform safe;
        Canvas canvas;
        Rect appliedSafe;
        readonly List<Control> controls = new List<Control>();
        Control throttle, brake, handbrake, reset, lookBack, steerLeft, steerRight;

        // Direction au glisser
        RectTransform steerZone, steerBase, steerKnob, tiltBar, tiltMarker;
        GameObject dragGroup, buttonsGroup, tiltGroup;
        int steerTouch = -1, lookTouch = -1;
        Vector2 steerOrigin;
        static readonly Vector2 SteerHome = new Vector2(330f, 230f);
        const float SteerRadius = 150f;
        float steer;

        bool shown, wasGamepad;
        int mode = -1;

        public void Init(GameManager gm)
        {
            game = gm;
            font = HudArt.Font;
            EnhancedTouchSupport.Enable();
            if (Simulate) TouchSimulation.Enable();
            TouchInput.InUse = Application.isMobilePlatform || Simulate;
            wasGamepad = GameInput.UsingGamepad;
            if (Application.isMobilePlatform)
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                Application.targetFrameRate = 60;   // Android limite à 30 images/s par défaut
            }
            Build();
            root.SetActive(false);
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            root = new GameObject("TouchControls", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;   // sous la pause et les options (elles masquent de toute façon les commandes)
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            // Zone sûre (encoches, bords arrondis) : tout est posé dedans
            safe = Rect(root.transform, "Safe", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Pédales, en bas à droite (le compteur remonte au-dessus quand les commandes sont affichées)
            throttle = Hold(Corner(1f, 0f), new Vector2(-250f, 30f), new Vector2(220f, 320f), "GAZ", "GAS", 34);
            brake = Hold(Corner(1f, 0f), new Vector2(-480f, 30f), new Vector2(200f, 220f), "FREIN", "BRAKE", 30);
            handbrake = Hold(Corner(1f, 0f), new Vector2(-480f, 268f), new Vector2(200f, 82f), "FREIN À MAIN", "HANDBRAKE", 21);

            // En haut à gauche, sous l'horloge : pause, replacer, regarder derrière
            Tap(Corner(0f, 1f), new Vector2(240f, -72f), new Vector2(84f, 44f), "II", "II", 26, TouchInput.QueuePause);
            reset = Hold(Corner(0f, 1f), new Vector2(26f, -150f), new Vector2(210f, 58f), "REPLACER", "RESET", 22);
            lookBack = Hold(Corner(0f, 1f), new Vector2(26f, -236f), new Vector2(210f, 58f), "DERRIÈRE", "LOOK BACK", 22);
            AddCaption(reset, "course, maintenir : abandon", "race, hold: abandon");

            // En haut à droite, sous l'épreuve en cours : vue, phares, musique
            Tap(Corner(1f, 1f), new Vector2(-196f, -190f), new Vector2(170f, 54f), "VUE", "CAMERA", 22, TouchInput.QueueCamera);
            Tap(Corner(1f, 1f), new Vector2(-196f, -254f), new Vector2(170f, 54f), "PHARES", "LIGHTS", 22, TouchInput.QueueLights);
            Tap(Corner(1f, 1f), new Vector2(-196f, -318f), new Vector2(170f, 54f), "MUSIQUE", "MUSIC", 22, TouchInput.QueueNextTrack);

            // Direction : zone de glisser à gauche (le socle suit le pouce)
            steerZone = Rect(safe, "SteerZone", Vector2.zero, new Vector2(0.42f, 0.66f), Vector2.zero, Vector2.zero);
            dragGroup = Rect(safe, "SteerDrag", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            steerBase = Rect(dragGroup.transform, "Base", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            steerBase.sizeDelta = new Vector2(SteerRadius * 2f + 120f, 70f);
            Shape(steerBase, "Track", Vector2.zero, new Vector2(SteerRadius * 2f + 120f, 70f), Plate).Set(18f, 50f, 50f);
            Shape(steerBase, "Line", Vector2.zero, new Vector2(SteerRadius * 2f, 4f), new Color(1f, 1f, 1f, 0.25f)).Set(0f, 30f, 30f);
            Label(steerBase, "‹", 40, new Vector2(-SteerRadius - 20f, 0f), new Vector2(50f, 60f), new Color(1f, 1f, 1f, 0.5f));
            Label(steerBase, "›", 40, new Vector2(SteerRadius + 20f, 0f), new Vector2(50f, 60f), new Color(1f, 1f, 1f, 0.5f));
            steerKnob = Rect(steerBase, "Knob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            steerKnob.sizeDelta = new Vector2(96f, 96f);
            var knob = steerKnob.gameObject.AddComponent<Image>();
            knob.sprite = HudArt.Disc;
            knob.color = new Color(1f, 1f, 1f, 0.8f);
            knob.raycastTarget = false;
            var hint = Label(steerBase, "", 18, new Vector2(0f, -62f), new Vector2(400f, 30f), new Color(1f, 1f, 1f, 0.55f));
            Loc.Bind(hint, "GLISSER POUR DIRIGER", "SLIDE TO STEER");
            steerBase.anchoredPosition = SteerHome;

            // Direction : deux boutons
            buttonsGroup = Rect(safe, "SteerButtons", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            steerLeft = Hold(Corner(0f, 0f, buttonsGroup.transform), new Vector2(40f, 40f), new Vector2(220f, 220f), "‹", "‹", 90);
            steerRight = Hold(Corner(0f, 0f, buttonsGroup.transform), new Vector2(290f, 40f), new Vector2(220f, 220f), "›", "›", 90);
            steerLeft.group = steerRight.group = buttonsGroup;

            // Direction : inclinaison (témoin seulement)
            tiltGroup = Rect(safe, "SteerTilt", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            tiltBar = Rect(tiltGroup.transform, "Bar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            tiltBar.sizeDelta = new Vector2(380f, 60f);
            tiltBar.anchoredPosition = new Vector2(260f, 90f);
            Shape(tiltBar, "Track", Vector2.zero, new Vector2(380f, 60f), Plate).Set(16f, 40f, 40f);
            tiltMarker = Shape(tiltBar, "Marker", Vector2.zero, new Vector2(14f, 44f), Accent).Set(6f, 0f, 0f).rectTransform;
            var tiltLabel = Label(tiltBar, "", 18, new Vector2(0f, 46f), new Vector2(400f, 30f), new Color(1f, 1f, 1f, 0.55f));
            Loc.Bind(tiltLabel, "INCLINER POUR DIRIGER", "TILT TO STEER");
        }

        RectTransform Corner(float x, float y, Transform parent = null)
        {
            var rt = Rect(parent ? parent : safe, "Corner", new Vector2(x, y), new Vector2(x, y), Vector2.zero, Vector2.zero);
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        /// <summary>Plaque touchable : <paramref name="pos"/> est son coin bas gauche par rapport au coin d'ancrage.</summary>
        Control Make(RectTransform corner, Vector2 pos, Vector2 size, string fr, string en, int fontSize)
        {
            var rt = Rect(corner, fr, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.pivot = Vector2.zero;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var plate = Shape(rt, "Plate", Vector2.zero, size, Plate).Set(18f, 0f, 0f);
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var label = Label(rt, "", fontSize, Vector2.zero, size, White);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            Loc.Bind(label, fr, en);
            var c = new Control { rt = rt, plate = plate, label = label };
            controls.Add(c);
            return c;
        }

        Control Hold(RectTransform corner, Vector2 pos, Vector2 size, string fr, string en, int fontSize)
        {
            var c = Make(corner, pos, size, fr, en, fontSize);
            c.hold = true;
            return c;
        }

        Control Tap(RectTransform corner, Vector2 pos, Vector2 size, string fr, string en, int fontSize, Action onTap)
        {
            var c = Make(corner, pos, size, fr, en, fontSize);
            c.tap = onTap;
            return c;
        }

        void AddCaption(Control c, string fr, string en)
        {
            var t = Label(c.rt, "", 15, new Vector2(0f, -c.rt.sizeDelta.y * 0.5f - 12f), new Vector2(260f, 22f), new Color(1f, 1f, 1f, 0.5f));
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            Loc.Bind(t, fr, en);
        }

        // ------------------------------------------------------------------ mise à jour

        void Update()
        {
            if (game == null) return;
            UpdateDevice();
            bool show = GameSettings.TouchSteer != 0 && TouchInput.InUse && !game.Demo
                && (game.State == GameManager.Phase.Playing || game.State == GameManager.Phase.Countdown)
                && !(game.Hud && game.Hud.Paused) && !OptionsMenu.IsOpen;
            if (show != shown)
            {
                shown = show;
                root.SetActive(show);
                TouchInput.Showing = show;
                ReleaseAll();
            }
            if (!show) return;
            ApplySafeArea();
            ApplyMode();
            ReadTouches();
            Animate(Time.unscaledDeltaTime);
        }

        /// <summary>Écran touché : commandes affichées. Clavier ou manette : masquées.</summary>
        void UpdateDevice()
        {
            bool touched = false;
            foreach (var t in ETouch.activeTouches)
                if (t.began) { touched = true; break; }
            bool pad = GameInput.UsingGamepad;
            if (touched) TouchInput.InUse = true;
            else if (!Simulate && ((pad && !wasGamepad) || (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)))
                TouchInput.InUse = false;
            wasGamepad = pad;
        }

        void ApplySafeArea()
        {
            Rect area = Screen.safeArea;
            if (area == appliedSafe || Screen.width <= 0 || Screen.height <= 0) return;
            appliedSafe = area;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        }

        void ApplyMode()
        {
            if (mode == GameSettings.TouchSteer) return;
            mode = GameSettings.TouchSteer;
            dragGroup.SetActive(mode == 1);
            buttonsGroup.SetActive(mode == 2);
            tiltGroup.SetActive(mode == 3);
            var accel = Accelerometer.current;
            if (accel != null)
            {
                if (mode == 3) InputSystem.EnableDevice(accel);
                else if (accel.enabled) InputSystem.DisableDevice(accel);
            }
            ReleaseAll();
        }

        void ReadTouches()
        {
            foreach (var c in controls) c.held = false;
            bool steerAlive = false, lookAlive = false;
            Vector2 look = Vector2.zero;

            foreach (var t in ETouch.activeTouches)
            {
                bool down = t.phase != UnityEngine.InputSystem.TouchPhase.Ended && t.phase != UnityEngine.InputSystem.TouchPhase.Canceled;
                Vector2 pos = t.screenPosition;
                int id = t.touchId;

                if (id == steerTouch)
                {
                    if (down) { steerAlive = true; DragSteer(pos); }
                    continue;
                }
                if (id == lookTouch)
                {
                    if (down) { lookAlive = true; look += t.delta; }
                    continue;
                }
                if (!down) continue;

                // Plaque touchée : maintenue tant que le doigt est dessus (on peut glisser d'une pédale à l'autre)
                var hit = HitControl(pos);
                if (hit != null)
                {
                    if (hit.hold) hit.held = true;
                    else if (t.began) { hit.tap?.Invoke(); hit.flash = 0.18f; }
                    continue;
                }
                if (!t.began) continue;
                if (mode == 1 && steerTouch < 0 && Contains(steerZone, pos, 0f))
                {
                    steerTouch = id;
                    steerAlive = true;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, pos, null, out var local);
                    steerOrigin = local + safe.rect.size * safe.pivot;   // repère : coin bas gauche de la zone sûre
                    steerBase.anchoredPosition = steerOrigin;
                    steer = 0f;
                }
                else if (lookTouch < 0)
                {
                    lookTouch = id;
                    lookAlive = true;
                }
            }

            if (!steerAlive) { steerTouch = -1; if (mode == 1) steer = 0f; }
            if (!lookAlive) lookTouch = -1;

            // Direction
            if (mode == 2) steer = (steerRight.held ? 1f : 0f) - (steerLeft.held ? 1f : 0f);
            else if (mode == 3) steer = TiltSteer();
            TouchInput.Steer = Mathf.Clamp(steer, -1f, 1f);

            TouchInput.Throttle = throttle.held ? 1f : 0f;
            TouchInput.Brake = brake.held ? 1f : 0f;
            TouchInput.Handbrake = handbrake.held;
            TouchInput.ResetHeld = reset.held;
            TouchInput.LookBack = lookBack.held;
            TouchInput.LookHeld = lookTouch >= 0;
            if (look != Vector2.zero) TouchInput.AddLook(look);
            TouchInput.Publish();
        }

        /// <summary>Socle flottant : le pouce dirige par son écart horizontal ; au-delà du rayon, le socle le suit.</summary>
        void DragSteer(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, screenPos, null, out var local);
            local += safe.rect.size * safe.pivot;
            float dx = local.x - steerOrigin.x;
            if (Mathf.Abs(dx) > SteerRadius)
            {
                steerOrigin.x = local.x - Mathf.Sign(dx) * SteerRadius;
                dx = Mathf.Sign(dx) * SteerRadius;
                steerBase.anchoredPosition = steerOrigin;
            }
            float s = dx / SteerRadius;
            // Petite zone morte et courbe douce, comme le joystick : précis à haute vitesse
            float a = Mathf.Clamp01((Mathf.Abs(s) - 0.06f) / 0.94f);
            steer = Mathf.Sign(s) * Mathf.Pow(a, 1.25f);
        }

        /// <summary>Tenir le téléphone comme un volant : ±25° environ pour braquer à fond.</summary>
        static float TiltSteer()
        {
            var accel = Accelerometer.current;
            if (accel == null || !accel.enabled) return 0f;
            float x = accel.acceleration.ReadValue().x;
            float a = Mathf.Clamp01((Mathf.Abs(x) - 0.04f) / 0.38f);
            return Mathf.Sign(x) * a;
        }

        Control HitControl(Vector2 pos)
        {
            // Les plus récemment ajoutées par-dessus ; marge pour les pouces
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var c = controls[i];
                if (c.group && !c.group.activeSelf) continue;
                if (Contains(c.rt, pos, 18f)) return c;
            }
            return null;
        }

        static bool Contains(RectTransform rt, Vector2 screenPos, float margin)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out var p)) return false;
            var r = rt.rect;
            return p.x >= r.xMin - margin && p.x <= r.xMax + margin && p.y >= r.yMin - margin && p.y <= r.yMax + margin;
        }

        void Animate(float dt)
        {
            foreach (var c in controls)
            {
                if (c.flash > 0f) c.flash -= dt;
                bool lit = c.held || c.flash > 0f;
                c.plate.color = lit ? Pressed : Plate;
                c.label.color = lit ? Dark : White;
            }
            if (mode == 1)
            {
                if (steerTouch < 0) steerBase.anchoredPosition = Vector2.Lerp(steerBase.anchoredPosition, SteerHome, 1f - Mathf.Exp(-dt * 10f));
                steerKnob.anchoredPosition = new Vector2(TouchInput.Steer >= 0f ? Mathf.Pow(TouchInput.Steer, 0.8f) : -Mathf.Pow(-TouchInput.Steer, 0.8f), 0f) * SteerRadius;
                steerKnob.GetComponent<Image>().color = steerTouch >= 0 ? new Color(Accent.r, Accent.g, Accent.b, 0.95f) : new Color(1f, 1f, 1f, 0.7f);
            }
            else if (mode == 3)
                tiltMarker.anchoredPosition = new Vector2(TouchInput.Steer * 170f, 0f);
        }

        void ReleaseAll()
        {
            steerTouch = lookTouch = -1;
            steer = 0f;
            foreach (var c in controls) { c.held = false; c.flash = 0f; }
            TouchInput.Release();
            if (steerBase) steerBase.anchoredPosition = SteerHome;
        }

        void OnDisable()
        {
            TouchInput.Showing = false;
            TouchInput.Release();
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static HudShape Shape(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            // CanvasRenderer d'abord : Unity 6 ne l'ajoute pas avec AddComponent<Graphic>
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HudShape));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var s = go.GetComponent<HudShape>();
            s.color = color;
            s.raycastTarget = false;
            return s;
        }

        Text Label(Transform parent, string text, int size, Vector2 pos, Vector2 box, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.BoldAndItalic;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.45f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }
    }
}
