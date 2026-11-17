using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    public enum Language { English, French }

    /// <summary>
    /// Traduction anglais / français : chaque texte est écrit dans les deux langues à l'endroit où il sert
    /// (<c>Loc.T("Jouer", "Play")</c>). Les nombres suivent la langue (12 345,5 ou 12,345.5).
    /// Les textes fixes de l'interface sont enregistrés avec <see cref="Bind"/> et changent de langue tout de suite.
    /// </summary>
    public static class Loc
    {
        public static readonly NumberFormatInfo EnglishNumbers = CultureInfo.InvariantCulture.NumberFormat;
        public static readonly NumberFormatInfo FrenchNumbers = MakeFrench();

        static NumberFormatInfo MakeFrench()
        {
            var n = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            n.NumberDecimalSeparator = ",";
            n.NumberGroupSeparator = " ";   // espace simple : la police intégrée n'a pas l'espace fine insécable
            return n;
        }

        struct Bound { public Text text; public string fr, en; }
        static readonly List<Bound> bound = new List<Bound>();

        public static bool French => GameSettings.Language == Language.French;
        public static IFormatProvider Numbers => French ? FrenchNumbers : EnglishNumbers;

        /// <summary>Déclenché après un changement de langue.</summary>
        public static event Action Changed;

        public static string T(string fr, string en) => French ? fr : en;

        /// <summary>Texte avec des valeurs : les nombres sont formatés selon la langue.</summary>
        public static string F(FormattableString fr, FormattableString en) => (French ? fr : en).ToString(Numbers);

        /// <summary>Même texte dans les deux langues, seuls les nombres changent de format.</summary>
        public static string F(FormattableString s) => s.ToString(Numbers);

        /// <summary>Nombre entier avec séparateur de milliers.</summary>
        public static string Num(float v) => Mathf.RoundToInt(v).ToString("N0", Numbers);

        /// <summary>Texte fixe de l'interface : affiché dans la langue en cours et mis à jour quand elle change.</summary>
        public static Text Bind(Text text, string fr, string en)
        {
            bound.RemoveAll(b => !b.text);
            bound.Add(new Bound { text = text, fr = fr, en = en });
            text.text = T(fr, en);
            return text;
        }

        public static void Set(Language language)
        {
            if (GameSettings.Language == language) return;
            GameSettings.Language = language;
            GameSettings.Save();
            bound.RemoveAll(b => !b.text);
            foreach (var b in bound) b.text.text = T(b.fr, b.en);
            Changed?.Invoke();
        }

        public static void Toggle() => Set(French ? Language.English : Language.French);
    }
}
