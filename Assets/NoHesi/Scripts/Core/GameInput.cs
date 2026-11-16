using UnityEngine;
using UnityEngine.InputSystem;

namespace NoHesi
{
    /// <summary>
    /// Lecture centralisée des commandes du joueur (Input System) : la dernière manette utilisée, le clavier et la
    /// souris, plus les commandes tactiles (<see cref="TouchInput"/>) qui s'y ajoutent comme un appareil de plus.
    /// Pause et retour : n'importe quelle manette.
    /// </summary>
    public static class GameInput
    {
        static Gamepad Pad => Gamepad.current;
        static Keyboard Kb => Keyboard.current;

        public static float Steer
        {
            get
            {
                float v = 0f;
                var p = Pad;
                if (p != null)
                {
                    float x = p.leftStick.x.ReadValue();
                    // Zone morte + courbe douce pour la précision à haute vitesse
                    if (Mathf.Abs(x) > 0.08f) v += Mathf.Sign(x) * Mathf.Pow((Mathf.Abs(x) - 0.08f) / 0.92f, 1.35f);
                    v += p.dpad.x.ReadValue();
                }
                var kb = Kb;
                if (kb != null)
                {
                    if (kb.aKey.isPressed || kb.qKey.isPressed || kb.leftArrowKey.isPressed) v -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v += 1f;
                }
                v += TouchInput.Steer;
                return Mathf.Clamp(v, -1f, 1f);
            }
        }

        /// <summary>Regard autour de la voiture : direction du joystick droit (haut = devant, bas = derrière).</summary>
        public static Vector2 LookStick
        {
            get
            {
                var p = Pad;
                if (p == null) return Vector2.zero;
                Vector2 v = p.rightStick.ReadValue();
                return v.magnitude > 0.18f ? v : Vector2.zero;
            }
        }

        /// <summary>Regard à la souris (clic droit maintenu, décalage en degrés accumulé) ou au doigt glissé sur l'écran.</summary>
        public static bool MouseLookHeld => Mouse.current != null && Mouse.current.rightButton.isPressed || TouchInput.LookHeld;
        public static Vector2 MouseDelta =>
            (Mouse.current != null && Mouse.current.rightButton.isPressed ? Mouse.current.delta.ReadValue() : Vector2.zero) + TouchInput.LookDelta;

        public static float Throttle
        {
            get
            {
                var p = Pad;
                float v = p != null ? p.rightTrigger.ReadValue() : 0f;
                var kb = Kb;
                if (kb != null && (kb.wKey.isPressed || kb.zKey.isPressed || kb.upArrowKey.isPressed)) v = 1f;
                return Mathf.Max(v, TouchInput.Throttle);
            }
        }

        public static float Brake
        {
            get
            {
                var p = Pad;
                float v = p != null ? p.leftTrigger.ReadValue() : 0f;
                var kb = Kb;
                if (kb != null && (kb.sKey.isPressed || kb.downArrowKey.isPressed)) v = 1f;
                return Mathf.Max(v, TouchInput.Brake);
            }
        }

        public static bool Handbrake
        {
            get
            {
                var p = Pad;
                var kb = Kb;
                return (p != null && (p.buttonEast.isPressed || p.rightShoulder.isPressed)) || (kb != null && kb.spaceKey.isPressed) || TouchInput.Handbrake;
            }
        }

        /// <summary>Bouton de replacement maintenu (appui long : changer de voiture).</summary>
        public static bool ResetHeld
        {
            get
            {
                var p = Pad;
                var kb = Kb;
                return (p != null && p.buttonNorth.isPressed) || (kb != null && kb.rKey.isPressed) || TouchInput.ResetHeld;
            }
        }

        public static bool ResetPressed
        {
            get
            {
                var p = Pad;
                var kb = Kb;
                return (p != null && p.buttonNorth.wasPressedThisFrame) || (kb != null && kb.rKey.wasPressedThisFrame);
            }
        }

        /// <summary>Musique suivante : croix ↑ ou la touche marquée « M » (sa position américaine est « , » sur un AZERTY : la carte).</summary>
        public static bool NextTrackPressed
        {
            get
            {
                var p = Pad;
                if (p != null && p.dpad.up.wasPressedThisFrame || TouchInput.NextTrackPressed) return true;
                var key = KeyLabeled("m", Key.M);
                return key != null && key.wasPressedThisFrame;
            }
        }

        public static bool CameraPressed
        {
            get
            {
                var p = Pad;
                var kb = Kb;
                return (p != null && p.rightStickButton.wasPressedThisFrame) || (kb != null && (kb.cKey.wasPressedThisFrame || kb.vKey.wasPressedThisFrame)) || TouchInput.CameraPressed;
            }
        }

        /// <summary>Allumer / éteindre les phares : L3 (clic du joystick gauche) ou L au clavier.</summary>
        public static bool LightsPressed
        {
            get
            {
                var p = Pad;
                var kb = Kb;
                return (p != null && p.leftStickButton.wasPressedThisFrame) || (kb != null && kb.lKey.wasPressedThisFrame) || TouchInput.LightsPressed;
            }
        }

        public static bool LookBack
        {
            get
            {
                var p = Pad;
                var kb = Kb;
                return (p != null && p.leftShoulder.isPressed) || (kb != null && kb.bKey.isPressed) || TouchInput.LookBack;
            }
        }

        /// <summary>Pause : Start sur n'importe quelle manette, Échap ou P, bouton II des commandes tactiles.</summary>
        public static bool PausePressed
        {
            get
            {
                if (TouchInput.PausePressed) return true;
                foreach (var p in Gamepad.all) if (p.startButton.wasPressedThisFrame) return true;
                return Kb != null && (Kb.escapeKey.wasPressedThisFrame || Kb.pKey.wasPressedThisFrame);
            }
        }

        /// <summary>Carte globale : touche « , » (celle qui porte ce signe sur le clavier utilisé), ou Select sur une manette.</summary>
        public static bool MapPressed
        {
            get
            {
                foreach (var p in Gamepad.all) if (p.selectButton.wasPressedThisFrame) return true;
                var key = KeyLabeled(MapKeyLabel, Key.Comma);
                return key != null && key.wasPressedThisFrame;
            }
        }

        /// <summary>Signe porté par la touche de la carte (réglage par défaut).</summary>
        public const string MapKeyLabel = ",";

        static readonly System.Collections.Generic.Dictionary<string, UnityEngine.InputSystem.Controls.KeyControl> labeled =
            new System.Collections.Generic.Dictionary<string, UnityEngine.InputSystem.Controls.KeyControl>();
        static Keyboard labeledKeyboard;
        static string labeledLayout;

        /// <summary>
        /// Touche qui porte un signe donné sur la disposition du clavier en cours (AZERTY, QWERTY…) ; à défaut, la touche
        /// de repli. L'Input System nomme les touches par leur position américaine : sur un AZERTY, « M » physique est « , ».
        /// </summary>
        public static UnityEngine.InputSystem.Controls.KeyControl KeyLabeled(string label, Key fallback)
        {
            var kb = Kb;
            if (kb == null) return null;
            if (kb != labeledKeyboard || kb.keyboardLayout != labeledLayout)
            {
                labeled.Clear();
                labeledKeyboard = kb;
                labeledLayout = kb.keyboardLayout;
            }
            if (!labeled.TryGetValue(label, out var key))
            {
                key = kb.FindKeyOnCurrentKeyboardLayout(label) ?? kb[fallback];
                labeled[label] = key;
            }
            return key;
        }

        /// <summary>Entrer (concession) : A sur n'importe quelle manette, E ou Entrée.</summary>
        public static bool InteractPressed
        {
            get
            {
                foreach (var p in Gamepad.all) if (p.buttonSouth.wasPressedThisFrame) return true;
                return Kb != null && (Kb.eKey.wasPressedThisFrame || Kb.enterKey.wasPressedThisFrame || Kb.numpadEnterKey.wasPressedThisFrame);
            }
        }

        /// <summary>Onglet précédent des menus : LB sur n'importe quelle manette, ou Maj+Tab.</summary>
        public static bool TabPrevPressed
        {
            get
            {
                foreach (var p in Gamepad.all) if (p.leftShoulder.wasPressedThisFrame) return true;
                return Kb != null && Kb.tabKey.wasPressedThisFrame && Kb.shiftKey.isPressed;
            }
        }

        /// <summary>Onglet suivant des menus : RB sur n'importe quelle manette, ou Tab.</summary>
        public static bool TabNextPressed
        {
            get
            {
                foreach (var p in Gamepad.all) if (p.rightShoulder.wasPressedThisFrame) return true;
                return Kb != null && Kb.tabKey.wasPressedThisFrame && !Kb.shiftKey.isPressed;
            }
        }

        /// <summary>Retour : B sur n'importe quelle manette, ou Échap.</summary>
        public static bool CancelPressed
        {
            get
            {
                foreach (var p in Gamepad.all) if (p.buttonEast.wasPressedThisFrame) return true;
                return Kb != null && Kb.escapeKey.wasPressedThisFrame;
            }
        }

        /// <summary>
        /// Dernier appareil utilisé : manette ou clavier/souris (pour afficher les bons contrôles).
        /// Au lancement : la manette si elle est branchée.
        /// </summary>
        public static bool UsingGamepad
        {
            get { Poll(); return lastGamepad; }
        }

        static bool lastGamepad = Gamepad.current != null;
        static int polledFrame = -1;

        /// <summary>Détecte l'appareil actif (une fois par image) : bouton, gâchette ou joystick franchement actionné, ou touche / souris.</summary>
        public static void Poll()
        {
            if (polledFrame == Time.frameCount) return;
            polledFrame = Time.frameCount;
            var pad = Pad;
            if (pad == null) { lastGamepad = false; return; }
            if (!lastGamepad)
            {
                bool active = pad.leftStick.ReadValue().magnitude > 0.5f || pad.rightStick.ReadValue().magnitude > 0.5f
                    || pad.leftTrigger.ReadValue() > 0.4f || pad.rightTrigger.ReadValue() > 0.4f || pad.dpad.ReadValue() != Vector2.zero;
                if (!active)
                    foreach (var b in pad.allControls)
                        if (b is UnityEngine.InputSystem.Controls.ButtonControl button && !(b.parent is UnityEngine.InputSystem.Controls.StickControl)
                            && b != pad.leftTrigger && b != pad.rightTrigger && button.wasPressedThisFrame) { active = true; break; }
                if (active) lastGamepad = true;
            }
            else
            {
                var mouse = Mouse.current;
                bool keys = Kb != null && Kb.anyKey.wasPressedThisFrame;
                bool mouseUsed = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 400f);
                var touch = Touchscreen.current;
                bool touched = touch != null && touch.primaryTouch.press.wasPressedThisFrame;
                if (keys || mouseUsed || touched) lastGamepad = false;
            }
        }
    }
}
