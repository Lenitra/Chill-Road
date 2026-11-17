using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ChillRoad
{
    /// <summary>
    /// Lecture centralisée des commandes du joueur (Input System) : la dernière manette utilisée, le clavier et la
    /// souris, plus les commandes tactiles (<see cref="TouchInput"/>) qui s'y ajoutent comme un appareil de plus.
    /// Les commandes de conduite se règlent dans les options (onglet Touches) : deux touches et deux boutons de manette
    /// par action (<see cref="Bind"/>), enregistrés dans les PlayerPrefs. Le joystick gauche dirige toujours, le droit
    /// regarde autour ; pause, retour, onglets et validation des menus restent fixes.
    /// </summary>
    public static class GameInput
    {
        static Gamepad Pad => Gamepad.current;
        static Keyboard Kb => Keyboard.current;

        // ------------------------------------------------------------------ commandes réglables

        /// <summary>Actions réglables, dans l'ordre de l'onglet Touches.</summary>
        public enum Bind { Throttle, Brake, Left, Right, Handbrake, LookBack, Camera, Reset, Lights, Flash, Horn, Music, Map, Players }
        public const int BindCount = 14, Slots = 2;

        static readonly (string fr, string en)[] BindNames =
        {
            ("Accélérer", "Throttle"), ("Freiner · marche arrière", "Brake · reverse"), ("Tourner à gauche", "Steer left"),
            ("Tourner à droite", "Steer right"), ("Frein à main", "Handbrake"), ("Regarder derrière", "Look back"),
            ("Changer de vue", "Change camera"), ("Replacer · abandonner", "Reset · abandon"), ("Phares", "Headlights"),
            ("Appel de phares", "Flash headlights"), ("Klaxon", "Horn"), ("Musique suivante", "Next song"), ("Carte", "Map"),
            ("Joueurs (en réseau)", "Players (online)"),
        };

        public static string BindName(Bind b) { var n = BindNames[(int)b]; return Loc.T(n.fr, n.en); }

        static readonly Key[][] DefaultKeys =
        {
            new[] { Key.W, Key.UpArrow }, new[] { Key.S, Key.DownArrow }, new[] { Key.A, Key.LeftArrow }, new[] { Key.D, Key.RightArrow },
            new[] { Key.Space, Key.None }, new[] { Key.B, Key.None }, new[] { Key.C, Key.V }, new[] { Key.R, Key.None },
            new[] { Key.L, Key.None }, new[] { Key.F, Key.None }, new[] { Key.H, Key.None },
            new[] { Key.M, Key.None },      // remplacée par la touche marquée « M » (voir DefaultKey)
            new[] { Key.Comma, Key.None },  // remplacée par la touche marquée « , »
            new[] { Key.Tab, Key.None },
        };

        // Contrôles de manette : chemin relatif à la manette (Input System), "" = aucun
        static readonly string[][] DefaultPads =
        {
            new[] { "rightTrigger", "" }, new[] { "leftTrigger", "" }, new[] { "dpad/left", "" }, new[] { "dpad/right", "" },
            new[] { "buttonEast", "rightShoulder" }, new[] { "leftShoulder", "" }, new[] { "buttonWest", "" }, new[] { "buttonNorth", "" },
            new[] { "leftStickPress", "" }, new[] { "dpad/down", "" }, new[] { "rightStickPress", "" }, new[] { "dpad/up", "" },
            new[] { "", "" }, new[] { "select", "" },
        };

        static readonly Key[,] keys = new Key[BindCount, Slots];
        static readonly string[,] pads = new string[BindCount, Slots];
        static bool bindingsLoaded;

        /// <summary>Augmente à chaque changement de commande (pour rafraîchir l'aide affichée).</summary>
        public static int BindingsVersion { get; private set; }

        const string Prefix = "chillroad_bind_";

        /// <summary>Touche par défaut ; la musique et la carte suivent le signe imprimé sur le clavier utilisé (AZERTY…).</summary>
        static Key DefaultKey(Bind b, int slot)
        {
            if (slot == 0 && b == Bind.Music) { var k = KeyLabeled("m", Key.M); return k != null ? k.keyCode : Key.M; }
            if (slot == 0 && b == Bind.Map) { var k = KeyLabeled(MapKeyLabel, Key.Comma); return k != null ? k.keyCode : Key.Comma; }
            return DefaultKeys[(int)b][slot];
        }

        static void LoadBindings()
        {
            if (bindingsLoaded) return;
            bindingsLoaded = true;
            for (int b = 0; b < BindCount; b++)
                for (int s = 0; s < Slots; s++)
                {
                    string kk = $"{Prefix}k{b}_{s}", pk = $"{Prefix}p{b}_{s}";
                    keys[b, s] = PlayerPrefs.HasKey(kk) ? (Key)PlayerPrefs.GetInt(kk) : DefaultKey((Bind)b, s);
                    pads[b, s] = PlayerPrefs.HasKey(pk) ? PlayerPrefs.GetString(pk) : DefaultPads[b][s];
                }
        }

        public static Key GetKey(Bind b, int slot) { LoadBindings(); return keys[(int)b, slot]; }
        public static string GetPad(Bind b, int slot) { LoadBindings(); return pads[(int)b, slot]; }

        /// <summary>Change une touche ; la même touche est retirée des autres actions (une touche = une action).</summary>
        public static void SetKey(Bind b, int slot, Key key)
        {
            LoadBindings();
            if (key != Key.None)
                for (int i = 0; i < BindCount; i++)
                    for (int s = 0; s < Slots; s++)
                        if (keys[i, s] == key && (i != (int)b || s != slot)) SaveKey(i, s, Key.None);
            SaveKey((int)b, slot, key);
            PlayerPrefs.Save();
            BindingsVersion++;
        }

        /// <summary>Change un bouton de manette ; le même bouton est retiré des autres actions.</summary>
        public static void SetPad(Bind b, int slot, string path)
        {
            LoadBindings();
            path ??= "";
            if (path.Length > 0)
                for (int i = 0; i < BindCount; i++)
                    for (int s = 0; s < Slots; s++)
                        if (pads[i, s] == path && (i != (int)b || s != slot)) SavePad(i, s, "");
            SavePad((int)b, slot, path);
            PlayerPrefs.Save();
            BindingsVersion++;
        }

        static void SaveKey(int b, int s, Key key) { keys[b, s] = key; PlayerPrefs.SetInt($"{Prefix}k{b}_{s}", (int)key); }
        static void SavePad(int b, int s, string path) { pads[b, s] = path; PlayerPrefs.SetString($"{Prefix}p{b}_{s}", path); }

        /// <summary>Commandes d'origine (bouton « Par défaut » des options).</summary>
        public static void ResetBindings()
        {
            for (int b = 0; b < BindCount; b++)
                for (int s = 0; s < Slots; s++)
                {
                    PlayerPrefs.DeleteKey($"{Prefix}k{b}_{s}");
                    PlayerPrefs.DeleteKey($"{Prefix}p{b}_{s}");
                }
            PlayerPrefs.Save();
            bindingsLoaded = false;
            LoadBindings();
            BindingsVersion++;
        }

        // Contrôles de la manette en cours retrouvés par leur chemin (cache vidé quand la manette change)
        static readonly Dictionary<string, ButtonControl> padControls = new Dictionary<string, ButtonControl>();
        static Gamepad padControlsOf;

        static ButtonControl PadControl(Gamepad p, string path)
        {
            if (p == null || string.IsNullOrEmpty(path)) return null;
            if (p != padControlsOf) { padControls.Clear(); padControlsOf = p; }
            if (!padControls.TryGetValue(path, out var c))
            {
                c = p.TryGetChildControl<ButtonControl>(path);
                padControls[path] = c;
            }
            return c;
        }

        static bool KeyHeld(Bind b)
        {
            var kb = Kb;
            if (kb == null) return false;
            LoadBindings();
            for (int s = 0; s < Slots; s++)
            {
                var k = keys[(int)b, s];
                if (k != Key.None && kb[k].isPressed) return true;
            }
            return false;
        }

        static bool KeyDown(Bind b)
        {
            var kb = Kb;
            if (kb == null) return false;
            LoadBindings();
            for (int s = 0; s < Slots; s++)
            {
                var k = keys[(int)b, s];
                if (k != Key.None && kb[k].wasPressedThisFrame) return true;
            }
            return false;
        }

        /// <summary>Valeur 0-1 (gâchette ou bouton) sur la manette donnée.</summary>
        static float PadValue(Bind b, Gamepad p)
        {
            if (p == null) return 0f;
            LoadBindings();
            float v = 0f;
            for (int s = 0; s < Slots; s++)
            {
                var c = PadControl(p, pads[(int)b, s]);
                if (c != null) v = Mathf.Max(v, c.ReadValue());
            }
            return v;
        }

        static bool PadHeld(Bind b, Gamepad p)
        {
            if (p == null) return false;
            LoadBindings();
            for (int s = 0; s < Slots; s++)
            {
                var c = PadControl(p, pads[(int)b, s]);
                if (c != null && c.isPressed) return true;
            }
            return false;
        }

        static bool PadDown(Bind b, Gamepad p)
        {
            if (p == null) return false;
            LoadBindings();
            for (int s = 0; s < Slots; s++)
            {
                var c = PadControl(p, pads[(int)b, s]);
                if (c != null && c.wasPressedThisFrame) return true;
            }
            return false;
        }

        /// <summary>Appui sur n'importe quelle manette (pause, carte : la manette qui n'est pas la dernière utilisée compte aussi).</summary>
        static bool AnyPadDown(Bind b)
        {
            foreach (var p in Gamepad.all) if (PadDown(b, p)) return true;
            return false;
        }

        static bool AnyPadHeld(Bind b)
        {
            foreach (var p in Gamepad.all) if (PadHeld(b, p)) return true;
            return false;
        }

        // ------------------------------------------------------------------ noms affichés

        /// <summary>Nom d'une touche sur le clavier utilisé (lettre de la disposition en cours, flèches, Espace…).</summary>
        public static string KeyName(Key k)
        {
            switch (k)
            {
                case Key.None: return "—";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
                case Key.Space: return Loc.T("Espace", "Space");
                case Key.Enter: return Loc.T("Entrée", "Enter");
                case Key.Tab: return "Tab";
                case Key.Backspace: return Loc.T("Retour arr.", "Backspace");
                case Key.LeftShift: return Loc.T("Maj gauche", "Left Shift");
                case Key.RightShift: return Loc.T("Maj droite", "Right Shift");
                case Key.LeftCtrl: return Loc.T("Ctrl gauche", "Left Ctrl");
                case Key.RightCtrl: return Loc.T("Ctrl droite", "Right Ctrl");
                case Key.LeftAlt: return "Alt";
                case Key.RightAlt: return "Alt Gr";
            }
            var kb = Kb;
            string name = null;
            if (kb != null)
            {
                try { name = kb[k].displayName; } catch (System.ArgumentException) { }
            }
            if (string.IsNullOrEmpty(name)) name = k.ToString();
            return name.Length <= 2 ? name.ToUpperInvariant() : name;
        }

        static readonly Dictionary<string, (string fr, string en)> PadNames = new Dictionary<string, (string, string)>
        {
            { "rightTrigger", ("RT", "RT") }, { "leftTrigger", ("LT", "LT") }, { "rightShoulder", ("RB", "RB") }, { "leftShoulder", ("LB", "LB") },
            { "buttonSouth", ("A", "A") }, { "buttonEast", ("B", "B") }, { "buttonWest", ("X", "X") }, { "buttonNorth", ("Y", "Y") },
            { "leftStickPress", ("L3", "L3") }, { "rightStickPress", ("R3", "R3") }, { "select", ("Select", "Select") }, { "start", ("Start", "Start") },
            { "dpad/up", ("Croix ↑", "D-pad ↑") }, { "dpad/down", ("Croix ↓", "D-pad ↓") },
            { "dpad/left", ("Croix ←", "D-pad ←") }, { "dpad/right", ("Croix →", "D-pad →") },
        };

        /// <summary>Nom d'un contrôle de manette (noms Xbox : A, B, RT, L3…).</summary>
        public static string PadName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "—";
            if (PadNames.TryGetValue(path, out var n)) return Loc.T(n.fr, n.en);
            var c = PadControl(Pad, path);
            return c != null ? c.displayName : path;
        }

        /// <summary>Commandes d'une action pour l'aide : « Z / ↑ », « B / RB » (les emplacements vides sont omis).</summary>
        public static string Describe(Bind b, bool pad)
        {
            LoadBindings();
            var parts = new List<string>(Slots);
            for (int s = 0; s < Slots; s++)
            {
                if (pad) { if (pads[(int)b, s].Length > 0) parts.Add(PadName(pads[(int)b, s])); }
                else if (keys[(int)b, s] != Key.None) parts.Add(KeyName(keys[(int)b, s]));
            }
            return parts.Count == 0 ? "—" : string.Join(" / ", parts);
        }

        /// <summary>Première commande d'une action (aide en une ligne pendant la conduite).</summary>
        public static string Short(Bind b, bool pad)
        {
            LoadBindings();
            for (int s = 0; s < Slots; s++)
            {
                if (pad) { if (pads[(int)b, s].Length > 0) return PadName(pads[(int)b, s]); }
                else if (keys[(int)b, s] != Key.None) return KeyName(keys[(int)b, s]);
            }
            return "—";
        }

        // ------------------------------------------------------------------ capture (onglet Touches)

        /// <summary>Touche appuyée à cette image (pour l'attribuer), Key.None sinon.</summary>
        public static Key PressedKey()
        {
            var kb = Kb;
            if (kb == null) return Key.None;
            foreach (var k in kb.allKeys)
                if (k != null && k.wasPressedThisFrame && k.keyCode != Key.None) return k.keyCode;
            return Key.None;
        }

        /// <summary>
        /// Contrôle de manette appuyé à cette image, en chemin relatif (« buttonSouth », « dpad/up », « rightTrigger ») ;
        /// les directions des joysticks et Start (pause) ne comptent pas. null sinon.
        /// </summary>
        public static string PressedPadControl()
        {
            foreach (var p in Gamepad.all)
                foreach (var c in p.allControls)
                {
                    if (!(c is ButtonControl b) || !b.wasPressedThisFrame || c == p.startButton) continue;
                    if (c.parent is StickControl || c.synthetic) continue;
                    string path = c.path;
                    string root = p.path + "/";
                    if (!path.StartsWith(root)) continue;
                    return path.Substring(root.Length);
                }
            return null;
        }

        // ------------------------------------------------------------------ conduite

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
                    v += PadValue(Bind.Right, p) - PadValue(Bind.Left, p);
                }
                if (KeyHeld(Bind.Left)) v -= 1f;
                if (KeyHeld(Bind.Right)) v += 1f;
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
                float v = PadValue(Bind.Throttle, Pad);
                if (KeyHeld(Bind.Throttle)) v = 1f;
                return Mathf.Max(v, TouchInput.Throttle);
            }
        }

        public static float Brake
        {
            get
            {
                float v = PadValue(Bind.Brake, Pad);
                if (KeyHeld(Bind.Brake)) v = 1f;
                return Mathf.Max(v, TouchInput.Brake);
            }
        }

        public static bool Handbrake => PadHeld(Bind.Handbrake, Pad) || KeyHeld(Bind.Handbrake) || TouchInput.Handbrake;

        /// <summary>Bouton de replacement (Y / R) : appui court = replacer ; en course, maintenu = abandonner.</summary>
        public static bool ResetHeld => PadHeld(Bind.Reset, Pad) || KeyHeld(Bind.Reset) || TouchInput.ResetHeld;

        public static bool ResetPressed => PadDown(Bind.Reset, Pad) || KeyDown(Bind.Reset);

        /// <summary>Musique suivante : croix ↑ ou la touche marquée « M » (sa position américaine est « , » sur un AZERTY : la carte).</summary>
        public static bool NextTrackPressed => PadDown(Bind.Music, Pad) || TouchInput.NextTrackPressed || KeyDown(Bind.Music);

        /// <summary>Changer de vue : X sur la manette, C ou V au clavier.</summary>
        public static bool CameraPressed => PadDown(Bind.Camera, Pad) || KeyDown(Bind.Camera) || TouchInput.CameraPressed;

        /// <summary>Klaxon : R3 (clic du joystick droit) maintenu, ou H au clavier.</summary>
        public static bool HornHeld => PadHeld(Bind.Horn, Pad) || KeyHeld(Bind.Horn);

        /// <summary>
        /// Menus : la manette ne navigue qu'au joystick gauche (et à la croix) ; le joystick droit reste libre pour
        /// tourner la caméra ou la voiture (concession, garage de tuning).
        /// </summary>
        public static void LeftStickMenus(UnityEngine.InputSystem.UI.InputSystemUIInputModule module)
        {
            var nav = module && module.move != null ? module.move.action : null;
            if (nav == null) return;
            for (int i = 0; i < nav.bindings.Count; i++)
            {
                string path = nav.bindings[i].path;
                if (path != null && path.IndexOf("rightStick", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    nav.ApplyBindingOverride(i, "");
            }
        }

        /// <summary>Allumer / éteindre les phares : L3 (clic du joystick gauche) ou L au clavier.</summary>
        public static bool LightsPressed => PadDown(Bind.Lights, Pad) || KeyDown(Bind.Lights) || TouchInput.LightsPressed;

        /// <summary>Appel de phares au clavier : F maintenue.</summary>
        public static bool FlashKeyHeld => KeyHeld(Bind.Flash);

        /// <summary>
        /// Appel de phares à la manette : croix ↓ maintenue. Elle sert aussi à refuser une invitation de course
        /// (<see cref="DeclinePressed"/>) : tant qu'une invitation attend, c'est le refus qui passe (voir <see cref="GameManager"/>).
        /// </summary>
        public static bool FlashPadHeld => PadHeld(Bind.Flash, Pad);

        /// <summary>
        /// Tableau des joueurs d'une partie en réseau, en conduisant : Tab (clavier) ou Select (manette) maintenu. Aussi
        /// dans le menu (voir <see cref="PlayerList"/>).
        /// </summary>
        public static bool PlayersHeld => KeyHeld(Bind.Players) || AnyPadHeld(Bind.Players);

        public static bool LookBack => PadHeld(Bind.LookBack, Pad) || KeyHeld(Bind.LookBack) || TouchInput.LookBack;

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

        /// <summary>Carte globale : touche « , » (celle qui porte ce signe sur le clavier utilisé) ; aussi dans le menu pause.</summary>
        public static bool MapPressed => AnyPadDown(Bind.Map) || KeyDown(Bind.Map);

        /// <summary>Signe porté par la touche de la carte (réglage par défaut).</summary>
        public const string MapKeyLabel = ",";

        static readonly Dictionary<string, KeyControl> labeled = new Dictionary<string, KeyControl>();
        static Keyboard labeledKeyboard;
        static string labeledLayout;

        /// <summary>
        /// Touche qui porte un signe donné sur la disposition du clavier en cours (AZERTY, QWERTY…) ; à défaut, la touche
        /// de repli. L'Input System nomme les touches par leur position américaine : sur un AZERTY, « M » physique est « , ».
        /// </summary>
        public static KeyControl KeyLabeled(string label, Key fallback)
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

        // ------------------------------------------------------------------ menus (fixes)

        /// <summary>Entrer (concession) : A sur n'importe quelle manette, E ou Entrée.</summary>
        public static bool InteractPressed
        {
            get
            {
                foreach (var p in Gamepad.all) if (p.buttonSouth.wasPressedThisFrame) return true;
                return Kb != null && (Kb.eKey.wasPressedThisFrame || Kb.enterKey.wasPressedThisFrame || Kb.numpadEnterKey.wasPressedThisFrame);
            }
        }

        /// <summary>Refuser (invitation à une course à plusieurs) : croix ↓ sur n'importe quelle manette, ou N.</summary>
        public static bool DeclinePressed
        {
            get
            {
                foreach (var p in Gamepad.all) if (p.dpad.down.wasPressedThisFrame) return true;
                return Kb != null && Kb.nKey.wasPressedThisFrame;
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
                        if (b is ButtonControl button && !(b.parent is StickControl)
                            && b != pad.leftTrigger && b != pad.rightTrigger && button.wasPressedThisFrame) { active = true; break; }
                if (active) lastGamepad = true;
            }
            else
            {
                var mouse = Mouse.current;
                bool typed = Kb != null && Kb.anyKey.wasPressedThisFrame;
                bool mouseUsed = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 400f);
                var touch = Touchscreen.current;
                bool touched = touch != null && touch.primaryTouch.press.wasPressedThisFrame;
                if (typed || mouseUsed || touched) lastGamepad = false;
            }
        }
    }
}
