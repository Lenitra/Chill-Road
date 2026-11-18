using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Accessoires cosmétiques du garage de tuning (onglet COSMÉTIQUE de <see cref="TuningGarage"/>), voiture par voiture :
    /// néons sous la caisse et leur couleur, teinte des vitres, pop and bang (désactivé, petit, conséquent, gazole) et
    /// couleur des flammes. Aucun effet sur la conduite. Comme les pièces : chaque option s'achète une fois, puis se
    /// remonte gratuitement ; l'option d'origine (et « désactivé ») est offerte. Sauvegardés sur la machine (PlayerPrefs) ;
    /// en réseau, l'ensemble voyage dans l'état de la voiture (<see cref="Look.Pack"/>).
    /// Néons : un halo additif au sol, deux tubes sous les bas de caisse et une lumière ponctuelle, enfants du modèle
    /// (<see cref="Apply"/>). Vitres : colonne « glass » de la palette du pack de voitures recolorée par le shader Toon
    /// (_GlassTint / _GlassBand, bloc de propriétés) : les modèles sans matériau PK_Palette gardent leurs vitres.
    /// Pop and bang et flammes : lus par <see cref="VehicleEffects"/> / <see cref="ExhaustFlames"/>.
    /// </summary>
    public static class CarCosmetics
    {
        public enum Item { Neon, Tint, Pop, Flame }
        public const int ItemCount = 4;
        public const int PopOff = 0, PopSmall = 1, PopBig = 2, PopDiesel = 3;

        struct Option
        {
            public string fr, en;
            /// <summary>Couleur montrée dans le menu (sRGB).</summary>
            public Color swatch;
            /// <summary>Couleur de l'effet : néon, verre (sRGB), flamme (multipliée par l'intensité du matériau).</summary>
            public Color fx;
            public float share;
            public Option(string fr, string en, Color swatch, Color fx, float share) { this.fr = fr; this.en = en; this.swatch = swatch; this.fx = fx; this.share = share; }
            public Option(string fr, string en, Color color, float share) : this(fr, en, color, color, share) { }
        }

        static readonly Color None = new Color(0f, 0f, 0f, 0f);

        static readonly Option[] Neons =
        {
            new Option("Aucun", "None", None, 0f),
            new Option("Rose", "Pink", new Color(1f, 0.3f, 0.78f), 0.03f),
            new Option("Cyan", "Cyan", new Color(0.2f, 0.9f, 1f), 0.03f),
            new Option("Bleu", "Blue", new Color(0.22f, 0.38f, 1f), 0.03f),
            new Option("Vert", "Green", new Color(0.25f, 1f, 0.35f), 0.03f),
            new Option("Violet", "Purple", new Color(0.62f, 0.25f, 1f), 0.035f),
            new Option("Rouge", "Red", new Color(1f, 0.12f, 0.1f), 0.035f),
            new Option("Orange", "Orange", new Color(1f, 0.5f, 0.08f), 0.035f),
            new Option("Blanc", "White", new Color(0.9f, 0.95f, 1f), 0.04f),
        };

        static readonly Option[] Tints =
        {
            new Option("D'origine", "Factory", new Color(0.30f, 0.52f, 0.58f), None, 0f),
            new Option("Légères", "Light", new Color(0.42f, 0.46f, 0.5f), 0.015f),
            new Option("Foncées", "Dark", new Color(0.2f, 0.21f, 0.24f), 0.02f),
            new Option("Limousine", "Limo", new Color(0.06f, 0.06f, 0.075f), 0.025f),
            new Option("Bleutées", "Blue", new Color(0.13f, 0.21f, 0.42f), 0.025f),
            new Option("Ambrées", "Amber", new Color(0.44f, 0.29f, 0.11f), 0.025f),
        };

        static readonly Option[] Pops =
        {
            new Option("Désactivé", "Off", None, 0f),
            new Option("Petit", "Small", None, 0f),
            new Option("Conséquent", "Big", None, 0.04f),
            new Option("Gazole", "Diesel", None, 0.06f),
        };

        static readonly Option[] Flames =
        {
            new Option("Orange", "Orange", new Color(1f, 0.5f, 0.1f), new Color(1f, 0.26f, 0.02f), 0f),
            new Option("Bleu", "Blue", new Color(0.25f, 0.5f, 1f), new Color(0.08f, 0.3f, 1f), 0.02f),
            new Option("Vert", "Green", new Color(0.3f, 1f, 0.3f), new Color(0.15f, 1f, 0.1f), 0.025f),
            new Option("Violet", "Purple", new Color(0.66f, 0.3f, 1f), new Color(0.55f, 0.1f, 1f), 0.025f),
            new Option("Rose", "Pink", new Color(1f, 0.35f, 0.72f), new Color(1f, 0.12f, 0.5f), 0.025f),
            new Option("Blanc", "White", new Color(0.92f, 0.95f, 1f), new Color(0.75f, 0.8f, 1f), 0.03f),
        };

        static Option[] Table(Item item) => item == Item.Neon ? Neons : item == Item.Tint ? Tints : item == Item.Pop ? Pops : Flames;

        /// <summary>Option d'origine de chaque accessoire (pop and bang : petit).</summary>
        public static int Stock(Item item) => item == Item.Pop ? PopSmall : 0;

        public static int Options(Item item) => Table(item).Length;

        /// <summary>Le plus grand nombre d'options d'un accessoire.</summary>
        public static int MaxOptions => Mathf.Max(Mathf.Max(Neons.Length, Tints.Length), Mathf.Max(Pops.Length, Flames.Length));

        // ------------------------------------------------------------------ ensemble monté

        /// <summary>Accessoires montés sur une voiture (indice de l'option de chacun).</summary>
        public struct Look
        {
            public int neon, tint, pop, flame;

            public static Look Stock => new Look { pop = PopSmall };

            public int this[Item item]
            {
                get => item == Item.Neon ? neon : item == Item.Tint ? tint : item == Item.Pop ? pop : flame;
                set
                {
                    if (item == Item.Neon) neon = value;
                    else if (item == Item.Tint) tint = value;
                    else if (item == Item.Pop) pop = value;
                    else flame = value;
                }
            }

            /// <summary>Quatre chiffres de 4 bits (réseau).</summary>
            public ushort Pack() => (ushort)((neon & 15) | (tint & 15) << 4 | (pop & 15) << 8 | (flame & 15) << 12);

            public static Look Unpack(ushort v) => new Look
            {
                neon = Mathf.Min(v & 15, Neons.Length - 1),
                tint = Mathf.Min(v >> 4 & 15, Tints.Length - 1),
                pop = Mathf.Min(v >> 8 & 15, Pops.Length - 1),
                flame = Mathf.Min(v >> 12 & 15, Flames.Length - 1),
            };

            public bool Same(Look o) => neon == o.neon && tint == o.tint && pop == o.pop && flame == o.flame;
        }

        // ------------------------------------------------------------------ textes et couleurs

        public static string Name(Item item)
        {
            switch (item)
            {
                case Item.Neon: return Loc.T("Néons", "Underglow");
                case Item.Tint: return Loc.T("Vitres teintées", "Window tint");
                case Item.Pop: return Loc.T("Pop and bang", "Pops and bangs");
                default: return Loc.T("Couleur des flammes", "Flame colour");
            }
        }

        public static string Effect(Item item)
        {
            switch (item)
            {
                case Item.Neon: return Loc.T("Tubes lumineux sous la caisse, halo au sol", "Light tubes under the body, glow on the ground");
                case Item.Tint: return Loc.T("Film teinté sur toutes les vitres", "Tinted film on every window");
                case Item.Pop: return Loc.T("Pétarades et flammes au lever de pied", "Crackles and flames when lifting off");
                default: return Loc.T("Teinte des retours de flamme", "Colour of the exhaust flames");
            }
        }

        public static string OptionName(Item item, int i) { var o = Table(item)[Clamp(item, i)]; return Loc.T(o.fr, o.en); }

        /// <summary>Précision d'une option (pop and bang surtout).</summary>
        public static string OptionEffect(Item item, int i)
        {
            if (item == Item.Pop)
                switch (i)
                {
                    case PopOff: return Loc.T("Plus de pétarades ni de flammes", "No more crackles nor flames");
                    case PopBig: return Loc.T("Grosses flammes et rafales de pétarades", "Big flames and bursts of crackles");
                    case PopDiesel: return Loc.T("Gazole dans l'échappement : énormes flammes qui durent", "Diesel in the exhaust: huge, lingering flames");
                    default: return Loc.T("Petits retours de flamme, d'origine", "Small backfires, factory");
                }
            if (item == Item.Neon && i == 0) return Loc.T("Pas de néons sous la voiture", "No lights under the car");
            if (item == Item.Tint && i == 0) return Loc.T("Vitres claires d'origine", "Clear factory windows");
            return Effect(item);
        }

        /// <summary>Couleur d'une option dans le menu (alpha 0 : pas de pastille).</summary>
        public static Color Swatch(Item item, int i) => Table(item)[Clamp(item, i)].swatch;

        /// <summary>Couleur des néons (alpha 0 : pas de néons).</summary>
        public static Color NeonColor(int i) => Neons[Clamp(Item.Neon, i)].fx;

        /// <summary>Couleur des flammes (multipliée par l'intensité du matériau des flammes).</summary>
        public static Color FlameColor(int i) => Flames[Clamp(Item.Flame, i)].fx;

        /// <summary>Couleur des flammes pour la lumière du flash.</summary>
        public static Color FlameLight(int i) => Flames[Clamp(Item.Flame, i)].swatch;

        static int Clamp(Item item, int i) => Mathf.Clamp(i, 0, Table(item).Length - 1);

        // ------------------------------------------------------------------ sauvegarde et achats

        const string FittedKey = "chillroad_cosm_", OwnedKey = "chillroad_cosmown_";
        /// <summary>Valeur prise pour les voitures données ou très bon marché (Ami, Twingo), comme la peinture.</summary>
        const int MinValue = 4000;

        class State { public Look fitted = Look.Stock; public readonly int[] owned = new int[ItemCount]; }
        static readonly Dictionary<string, State> states = new Dictionary<string, State>();

        static string Id(VehicleSpec spec) => spec != null ? spec.id ?? "" : "";

        static State Load(VehicleSpec spec)
        {
            string id = Id(spec);
            if (states.TryGetValue(id, out var st)) return st;
            st = new State();
            string fitted = PlayerPrefs.GetString(FittedKey + id, "");
            string[] owned = PlayerPrefs.GetString(OwnedKey + id, "").Split(',');
            for (int i = 0; i < ItemCount; i++)
            {
                var item = (Item)i;
                if (i < owned.Length && int.TryParse(owned[i], out int bits)) st.owned[i] = bits;
                int f = i < fitted.Length ? fitted[i] - '0' : Stock(item);
                if (f < 0 || f >= Options(item) || !Owns(st, item, f)) f = Stock(item);
                st.fitted[item] = f;
            }
            states[id] = st;
            return st;
        }

        static void Save(VehicleSpec spec, State st)
        {
            string id = Id(spec);
            var chars = new char[ItemCount];
            for (int i = 0; i < ItemCount; i++) chars[i] = (char)('0' + st.fitted[(Item)i]);
            PlayerPrefs.SetString(FittedKey + id, new string(chars));
            PlayerPrefs.SetString(OwnedKey + id, string.Join(",", st.owned));
            PlayerPrefs.Save();
        }

        static bool Owns(State st, Item item, int option)
            => option == Stock(item) || Table(item)[Clamp(item, option)].share <= 0f || (st.owned[(int)item] & 1 << option) != 0;

        /// <summary>Accessoires montés sur cette voiture.</summary>
        public static Look Fitted(VehicleSpec spec) => spec != null ? Load(spec).fitted : Look.Stock;

        public static int Fitted(VehicleSpec spec, Item item) => Fitted(spec)[item];

        /// <summary>Option à disposition (d'origine, gratuite ou déjà achetée) : la monter est gratuit.</summary>
        public static bool Owns(VehicleSpec spec, Item item, int option) => Owns(Load(spec), item, option);

        public static int OwnedCount(VehicleSpec spec, Item item)
        {
            int n = 0;
            for (int i = 0; i < Options(item); i++) if (Owns(spec, item, i)) n++;
            return n;
        }

        /// <summary>Prix catalogue d'une option (0 : offerte), achetée ou non.</summary>
        public static int OptionPrice(VehicleSpec spec, Item item, int option)
        {
            float share = Table(item)[Clamp(item, option)].share;
            if (share <= 0f || option == Stock(item)) return 0;
            float raw = Mathf.Max(Wallet.Price(spec), MinValue) * share;
            // Arrondi à la dizaine (100 au-delà de 10 000), comme les pièces et la peinture
            float step = raw >= 10000f ? 100f : 10f;
            return Mathf.Max(10, Mathf.RoundToInt(raw / step) * (int)step);
        }

        /// <summary>Prix pour monter une option : 0 si elle est à disposition.</summary>
        public static int Price(VehicleSpec spec, Item item, int option) => Owns(spec, item, option) ? 0 : OptionPrice(spec, item, option);

        /// <summary>Monte une option (achetée au passage si l'argent suffit). Vrai si l'accessoire a changé.</summary>
        public static bool Fit(VehicleSpec spec, Item item, int option)
        {
            if (spec == null || option < 0 || option >= Options(item) || !Available(spec, item)) return false;
            var st = Load(spec);
            if (st.fitted[item] == option) return false;
            if (!Owns(st, item, option))
            {
                if (!Wallet.Spend(OptionPrice(spec, item, option))) return false;
                st.owned[(int)item] |= 1 << option;
            }
            var look = st.fitted;
            look[item] = option;
            st.fitted = look;
            Save(spec, st);
            return true;
        }

        // ------------------------------------------------------------------ voitures compatibles

        static readonly Dictionary<GameObject, (bool tint, bool exhaust)> traits = new Dictionary<GameObject, (bool, bool)>();

        static (bool tint, bool exhaust) Traits(VehicleSpec spec)
        {
            if (spec == null || !spec.prefab) return (false, false);
            if (traits.TryGetValue(spec.prefab, out var t)) return t;
            foreach (var r in spec.prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (IsPalette(m)) t.tint = true;
            foreach (var tr in spec.prefab.GetComponentsInChildren<Transform>(true))
                if (tr.name.StartsWith("exhaust")) t.exhaust = true;
            traits[spec.prefab] = t;
            return t;
        }

        /// <summary>L'accessoire peut se monter sur cette voiture (vitres de la palette du pack, pot d'échappement).</summary>
        public static bool Available(VehicleSpec spec, Item item)
        {
            if (item == Item.Tint) return Traits(spec).tint;
            if (item == Item.Pop || item == Item.Flame) return Traits(spec).exhaust;
            return spec != null;
        }

        /// <summary>Raison pour laquelle l'accessoire n'est pas proposé.</summary>
        public static string Unavailable(Item item)
            => item == Item.Tint ? Loc.T("Cette voiture garde ses vitres d'origine", "This car keeps its factory windows")
             : Loc.T("Pas de pot d'échappement sur cette voiture", "This car has no exhaust pipe");

        // ------------------------------------------------------------------ rendu : vitres et néons

        static bool IsPalette(Material m) => m && m.name == "PK_Palette";

        /// <summary>Colonne « glass » (3e des 18 colonnes) de la palette du pack de voitures (Tools/Blender/pack_lib.py).</summary>
        static readonly Vector4 GlassBand = new Vector4(2f / 18f, 3f / 18f, 0f, 0f);
        static readonly int GlassTintId = Shader.PropertyToID("_GlassTint"), GlassBandId = Shader.PropertyToID("_GlassBand");
        const string GlowName = "Underglow";

        /// <summary>Hauteur des lumières des néons au-dessus du sol (m) et leur intensité.</summary>
        const float NeonHeight = 0.45f, NeonIntensity = 6f;

        static readonly List<Renderer> renderers = new List<Renderer>();
        static MaterialPropertyBlock mpb;

        /// <summary>
        /// Habille un modèle posé (voiture du joueur, voiture d'un autre joueur, atelier, vitrine) : teinte des vitres et
        /// néons. Le pop and bang et les flammes sont l'affaire des effets de la voiture.
        /// </summary>
        public static void Apply(Transform model, Look look, GameAssets assets)
        {
            if (!model) return;
            mpb ??= new MaterialPropertyBlock();
            var glow = model.Find(GlowName);
            if (glow)
            {
                // Sorti du modèle tout de suite : ni retrouvé, ni compté dans l'encombrement des nouveaux néons
                glow.gameObject.SetActive(false);
                glow.SetParent(null, false);
                Object.Destroy(glow.gameObject);
            }

            // Vitres : bloc de propriétés sur les emplacements du matériau de palette
            Color tint = look.tint > 0 ? Tints[Clamp(Item.Tint, look.tint)].fx.linear : Color.white;
            model.GetComponentsInChildren(true, renderers);
            foreach (var r in renderers)
            {
                if (!look.Same(Look.Stock) || r.HasPropertyBlock())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        if (!IsPalette(mats[k])) continue;
                        r.GetPropertyBlock(mpb, k);
                        mpb.SetVector(GlassTintId, new Vector4(tint.r, tint.g, tint.b, look.tint > 0 ? 1f : 0f));
                        mpb.SetVector(GlassBandId, GlassBand);
                        r.SetPropertyBlock(mpb, k);
                    }
                }
            }

            if (look.neon > 0) BuildNeons(model, look.neon);
            renderers.Clear();
        }

        /// <summary>
        /// Néons : de vraies lumières colorées sous la caisse, rien d'autre (ni halo dessiné ni tube, qui traversaient le
        /// sol). Elles sont placées dans le volume de la voiture, le long de l'axe : sans ombre, elles passent sous la caisse
        /// et éclairent la route tout autour, ainsi que les jantes et les bas de caisse par en dessous.
        /// </summary>
        static void BuildNeons(Transform model, int neon)
        {
            // Encombrement du modèle dans son propre repère (les renderers sont déjà dans « renderers »)
            bool any = false;
            Bounds b = default;
            foreach (var r in renderers)
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var lb = r.localBounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = model.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) return;
            float ground = b.min.y, halfW = b.extents.x, len = b.size.z;
            var color = NeonColor(neon);
            int layer = model.gameObject.layer;
            var root = new GameObject(GlowName) { layer = layer }.transform;
            root.SetParent(model, false);
            foreach (float s in new[] { -1f, 0f, 1f })
            {
                var lightGo = new GameObject("NeonLight") { layer = layer };
                lightGo.transform.SetParent(root, false);
                lightGo.transform.localPosition = new Vector3(b.center.x, ground + NeonHeight, b.center.z + s * len * 0.32f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.range = halfW * 2f + 3.2f;
                light.intensity = NeonIntensity;
                light.shadows = LightShadows.None;
            }
        }
    }
}
