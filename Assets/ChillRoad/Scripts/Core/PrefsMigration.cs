using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Reprise des sauvegardes de l'ancien nom du projet : les clés PlayerPrefs « nohesi_* » (options, argent,
    /// voitures achetées, records, peintures, réglages...) sont recopiées en « chillroad_* » au premier lancement,
    /// avant le chargement de la scène, puis supprimées. Une seule fois (drapeau chillroad_prefs_migrated).
    /// Windows : la liste des clés est lue dans le registre (clés dynamiques par voiture / course / saut comprises).
    /// Ailleurs : seules les clés fixes connues sont reprises.
    /// </summary>
    public static class PrefsMigration
    {
        const string OldPrefix = "nohesi_", NewPrefix = "chillroad_", DoneKey = "chillroad_prefs_migrated";

        static readonly string[] FixedKeys =
        {
            "best", "car", "cash", "owned", "stunts", "opt_lang", "opt_music", "opt_sfx", "opt_highway", "opt_dirt",
            "opt_curves", "opt_hills", "opt_junctions", "opt_seed", "opt_traffic", "opt_events", "opt_rain",
            "opt_touch", "opt_quality", "opt_fpscap", "opt_detail", "race_ghost", "race_traffic", "race_weather",
            "race_time", "race_impose",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Run()
        {
            if (PlayerPrefs.GetInt(DoneKey, 0) == 1) return;
            var keys = new HashSet<string>();
            foreach (var k in FixedKeys) keys.Add(OldPrefix + k);
            ReadRegistryKeys(keys);

            int moved = 0;
            foreach (var oldKey in keys)
            {
                if (!PlayerPrefs.HasKey(oldKey)) continue;
                string newKey = NewPrefix + oldKey.Substring(OldPrefix.Length);
                // Les lectures PlayerPrefs sont typées : une clé d'un autre type rend la valeur par défaut.
                if (!PlayerPrefs.HasKey(newKey))
                {
                    int i = PlayerPrefs.GetInt(oldKey, int.MinValue);
                    float f = PlayerPrefs.GetFloat(oldKey, float.NaN);
                    if (i != int.MinValue) PlayerPrefs.SetInt(newKey, i);
                    else if (!float.IsNaN(f)) PlayerPrefs.SetFloat(newKey, f);
                    else PlayerPrefs.SetString(newKey, PlayerPrefs.GetString(oldKey, ""));
                }
                PlayerPrefs.DeleteKey(oldKey);
                moved++;
            }
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            if (moved > 0) Debug.Log("Chill Road : " + moved + " sauvegardes reprises de l'ancien nom.");
        }

        /// <summary>Windows range les PlayerPrefs dans le registre sous la forme « clé_h&lt;hash&gt; ».</summary>
        static void ReadRegistryKeys(HashSet<string> keys)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                string path = (Application.isEditor ? "Software\\Unity\\UnityEditor\\" : "Software\\")
                              + Application.companyName + "\\" + Application.productName;
                if (RegOpenKeyExW(HKeyCurrentUser, path, 0, KeyRead, out var reg) != 0) return;
                try
                {
                    var name = new System.Text.StringBuilder(16384);
                    for (int i = 0; ; i++)
                    {
                        int len = name.Capacity;
                        name.Length = 0;
                        int err = RegEnumValueW(reg, i, name, ref len, System.IntPtr.Zero, System.IntPtr.Zero,
                                                System.IntPtr.Zero, System.IntPtr.Zero);
                        if (err != 0) break; // 259 = plus de valeurs
                        string n = name.ToString(0, len);
                        if (!n.StartsWith(OldPrefix)) continue;
                        int h = n.LastIndexOf("_h");
                        keys.Add(h > 0 ? n.Substring(0, h) : n);
                    }
                }
                finally { RegCloseKey(reg); }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Chill Road : lecture des anciennes sauvegardes impossible (" + e.Message + ")");
            }
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // Microsoft.Win32.Registry n'existe pas dans le profil .NET Standard du jeu : appel direct de advapi32.
        static readonly System.UIntPtr HKeyCurrentUser = (System.UIntPtr)0x80000001u;
        const int KeyRead = 0x20019;

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RegOpenKeyExW(System.UIntPtr hKey, string subKey, int options, int samDesired, out System.UIntPtr result);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RegEnumValueW(System.UIntPtr hKey, int index, System.Text.StringBuilder name, ref int nameLength,
                                        System.IntPtr reserved, System.IntPtr type, System.IntPtr data, System.IntPtr dataLength);

        [System.Runtime.InteropServices.DllImport("advapi32.dll")]
        static extern int RegCloseKey(System.UIntPtr hKey);
#endif
    }
}
