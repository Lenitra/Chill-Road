using System.Collections.Generic;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Peintures achetées au garage de tuning (<see cref="TuningGarage"/>), voiture par voiture : un nuancier commun,
    /// la couleur d'origine toujours offerte. Une couleur achetée le reste : on peut repasser de l'une à l'autre sans
    /// payer. Sauvegardées sur la machine (PlayerPrefs) ; en réseau, la couleur voyage avec l'état de la voiture.
    /// La carrosserie est le matériau de peinture des voitures jouables (« PC_xxx_Paint », Editor/PlayerCarsImport),
    /// teinté par bloc de propriétés (_BaseColor) comme le trafic : les modèles sans ce matériau gardent leur livrée.
    /// Le prix est une part de la valeur de la voiture (avec un plancher) : repeindre une Jesko coûte plus cher.
    /// </summary>
    public static class CarPaint
    {
        struct Swatch
        {
            public string fr, en;
            public Color color;
            public float share;
            public Swatch(string fr, string en, Color color, float share) { this.fr = fr; this.en = en; this.color = color; this.share = share; }
        }

        const float Classic = 0.02f, Bright = 0.035f, Special = 0.06f;
        /// <summary>Valeur prise pour les voitures données ou très bon marché (Ami, Twingo).</summary>
        const int MinValue = 4000;
        const string SelectedKey = "nohesi_paint_", OwnedKey = "nohesi_paints_";

        /// <summary>Nuancier (sRGB, comme les peintures d'usine) ; 0 : couleur d'origine de la voiture.</summary>
        static readonly Swatch[] Swatches =
        {
            new Swatch("D'origine", "Factory", Color.white, 0f),
            new Swatch("Blanc glacier", "Glacier white", new Color(0.94f, 0.94f, 0.93f), Classic),
            new Swatch("Noir intense", "Deep black", new Color(0.05f, 0.05f, 0.06f), Classic),
            new Swatch("Gris argent", "Silver", new Color(0.70f, 0.71f, 0.73f), Classic),
            new Swatch("Rouge course", "Racing red", new Color(0.82f, 0.06f, 0.07f), Classic),
            new Swatch("Bleu nuit", "Midnight blue", new Color(0.07f, 0.15f, 0.45f), Classic),
            new Swatch("Vert anglais", "British green", new Color(0.06f, 0.30f, 0.16f), Bright),
            new Swatch("Orange vif", "Bright orange", new Color(0.97f, 0.45f, 0.06f), Bright),
            new Swatch("Jaune soleil", "Sunshine yellow", new Color(0.98f, 0.80f, 0.10f), Bright),
            new Swatch("Bleu ciel", "Sky blue", new Color(0.45f, 0.75f, 0.95f), Bright),
            new Swatch("Rose bonbon", "Candy pink", new Color(1f, 0.42f, 0.75f), Special),
            new Swatch("Violet", "Purple", new Color(0.42f, 0.18f, 0.70f), Special),
        };

        public static int Count => Swatches.Length;

        static readonly Dictionary<string, (int selected, int owned)> saved = new Dictionary<string, (int, int)>();
        static readonly Dictionary<GameObject, Color?> factory = new Dictionary<GameObject, Color?>();
        static readonly List<Renderer> renderers = new List<Renderer>();
        static MaterialPropertyBlock mpb;

        public static string Name(int i) => Loc.T(Swatches[Clamp(i)].fr, Swatches[Clamp(i)].en);

        static int Clamp(int i) => Mathf.Clamp(i, 0, Swatches.Length - 1);

        static string Id(VehicleSpec spec) => spec != null ? spec.id ?? "" : "";

        static (int selected, int owned) State(VehicleSpec spec)
        {
            string id = Id(spec);
            if (saved.TryGetValue(id, out var s)) return s;
            // La couleur d'origine (bit 0) est toujours possédée
            s = (Clamp(PlayerPrefs.GetInt(SelectedKey + id, 0)), PlayerPrefs.GetInt(OwnedKey + id, 0) | 1);
            if ((s.owned & (1 << s.selected)) == 0) s.selected = 0;
            saved[id] = s;
            return s;
        }

        static void Save(VehicleSpec spec, (int selected, int owned) s)
        {
            string id = Id(spec);
            saved[id] = s;
            PlayerPrefs.SetInt(SelectedKey + id, s.selected);
            PlayerPrefs.SetInt(OwnedKey + id, s.owned);
            PlayerPrefs.Save();
        }

        /// <summary>Couleur sur la voiture (indice du nuancier, 0 : d'origine).</summary>
        public static int Selected(VehicleSpec spec) => Paintable(spec) ? State(spec).selected : 0;

        public static bool Owns(VehicleSpec spec, int i) => i <= 0 || (State(spec).owned & (1 << i)) != 0;

        /// <summary>Couleurs possédées pour cette voiture (d'origine comprise).</summary>
        public static int OwnedCount(VehicleSpec spec)
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (Owns(spec, i)) n++;
            return n;
        }

        /// <summary>Prix d'une couleur pour cette voiture (0 si elle est déjà achetée ou d'origine).</summary>
        public static int Price(VehicleSpec spec, int i)
        {
            if (Owns(spec, i)) return 0;
            float raw = Mathf.Max(Wallet.Price(spec), MinValue) * Swatches[Clamp(i)].share;
            // Arrondi à la dizaine (100 au-delà de 10 000), comme les pièces
            float step = raw >= 10000f ? 100f : 10f;
            return Mathf.Max(10, Mathf.RoundToInt(raw / step) * (int)step);
        }

        /// <summary>Achète une couleur (si l'argent suffit) et l'applique ; vrai si elle est sur la voiture.</summary>
        public static bool Buy(VehicleSpec spec, int i)
        {
            if (spec == null || !Paintable(spec) || i < 0 || i >= Count) return false;
            if (!Owns(spec, i) && !Wallet.Spend(Price(spec, i))) return false;
            var s = State(spec);
            s.owned |= 1 << i;
            s.selected = i;
            Save(spec, s);
            return true;
        }

        /// <summary>Couleur affichée pour une entrée du nuancier (d'origine : celle du modèle).</summary>
        public static Color Shade(VehicleSpec spec, int i)
        {
            if (i > 0) return Swatches[Clamp(i)].color;
            return spec != null && spec.prefab ? Factory(spec.prefab) ?? Color.gray : Color.gray;
        }

        /// <summary>Le modèle a un matériau de peinture (sinon il garde sa livrée).</summary>
        public static bool Paintable(VehicleSpec spec) => spec != null && spec.prefab && Factory(spec.prefab).HasValue;

        static bool IsPaint(Material m) => m && m.name.StartsWith("PC_") && m.name.Contains("_Paint");

        static Color? Factory(GameObject prefab)
        {
            if (factory.TryGetValue(prefab, out var c)) return c;
            c = null;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                    if (IsPaint(m)) { c = m.GetColor("_BaseColor"); break; }
                if (c.HasValue) break;
            }
            factory[prefab] = c;
            return c;
        }

        /// <summary>Teinte la carrosserie d'un modèle posé (voiture, vitrine) : couleur du nuancier, 0 : d'origine.</summary>
        public static void Apply(Transform model, int i)
        {
            if (!model) return;
            mpb ??= new MaterialPropertyBlock();
            i = Clamp(i);
            model.GetComponentsInChildren(true, renderers);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    if (!IsPaint(mats[k])) continue;
                    Color c = i == 0 ? mats[k].GetColor("_BaseColor") : Swatches[i].color;
                    c.a = 1f;
                    r.GetPropertyBlock(mpb, k);
                    mpb.SetColor("_BaseColor", c);
                    r.SetPropertyBlock(mpb, k);
                }
            }
            renderers.Clear();
        }
    }
}
