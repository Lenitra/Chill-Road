using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NoHesi.EditorTools
{
    /// <summary>
    /// Build Windows 64 bits du jeu (menu NoHesi) :
    /// - « Build Windows » : compile dans Builds/Windows (résumé dans Builds/last_build.txt) ;
    /// - « Build Windows + zip » : compile, puis zippe le jeu dans Builds/ChillRoad_AAAA-MM-JJ_HHhMM.zip (dossiers de
    ///   débogage exclus), prêt à envoyer ou à mettre en ligne, et ouvre l'explorateur sur le zip.
    /// Le splash screen Unity est désactivé (optionnel depuis Unity 6, même en licence Personal).
    /// </summary>
    public static class NoHesiBuild
    {
        const string Scene = "Assets/NoHesi/NoHesi.unity";
        const string OutputFolder = "Builds/Windows";

        [MenuItem("NoHesi/Build Windows")]
        public static void BuildWindows() => Build();

        [MenuItem("NoHesi/Build Windows + zip")]
        public static void BuildAndZip()
        {
            if (!Build()) return;
            string zip = ZipBuild();
            if (zip != null) EditorUtility.RevealInFinder(zip);
        }

        /// <summary>Compile le jeu dans un dossier vidé au préalable (aucun fichier d'un ancien build ne traîne).</summary>
        static bool Build()
        {
            // Pas de splash screen Unity au lancement du jeu
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
            Directory.CreateDirectory(OutputFolder);
            string exe = $"{OutputFolder}/{PlayerSettings.productName}.exe";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string text = $"{s.result} | {s.totalSize / (1024 * 1024)} Mo | {s.totalTime:mm\\:ss} | erreurs {s.totalErrors} | avertissements {s.totalWarnings} | {Path.GetFullPath(exe)}";
            File.WriteAllText("Builds/last_build.txt", text);
            Debug.Log("NoHesi build : " + text);
            if (s.result != BuildResult.Succeeded) Debug.LogError("NoHesi build : échec, pas de zip.");
            else WriteServerLauncher();
            return s.result == BuildResult.Succeeded;
        }

        /// <summary>
        /// Lanceur du serveur dédié, posé à côté du jeu : la même application lancée avec « -server » (pas d'accueil ni
        /// de voiture, la caméra suit les joueurs). Texte sans accents : les .bat n'aiment pas l'UTF-8.
        /// </summary>
        static void WriteServerLauncher()
        {
            string exe = PlayerSettings.productName + ".exe";
            string bat = "@echo off\r\n" +
                "rem Serveur dedie Chill Road : heberge une partie sans y jouer (fermer sa fenetre l'arrete).\r\n" +
                "rem Options : -port 47777   -name \"Mon serveur\"   -players 8   -nocollisions   -invite\r\n" +
                "rem Sans fenetre : ajouter -batchmode -nographics -logFile server.log (arret par le Gestionnaire des taches).\r\n" +
                "rem Par Internet : ajouter -invite (le serveur affiche un code d'invitation, rien a regler sur la box),\r\n" +
                "rem ou rediriger le port UDP 47777 de la box vers cette machine et donner son adresse IP.\r\n" +
                $"start \"\" \"%~dp0{exe}\" -server -port 47777 %*\r\n";
            File.WriteAllText($"{OutputFolder}/Serveur dedie.bat", bat, new System.Text.UTF8Encoding(false));
        }

        /// <summary>
        /// Zippe le dernier build (Builds/Windows) dans Builds/ChillRoad_date_heure.zip : un dossier « Chill Road » à la
        /// racine, sans les dossiers « …_DoNotShip » et « …_ButDontShipItWithYourGame ». Renvoie le chemin du zip.
        /// </summary>
        public static string ZipBuild()
        {
            if (!Directory.Exists(OutputFolder)) { Debug.LogError("NoHesi zip : aucun build dans " + OutputFolder); return null; }
            string product = PlayerSettings.productName;
            string zip = $"Builds/{product.Replace(" ", "")}_{DateTime.Now:yyyy-MM-dd_HH'h'mm}.zip";
            if (File.Exists(zip)) File.Delete(zip);
            string root = Path.GetFullPath(OutputFolder);
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            long bytes = 0;
            int count = 0;
            try
            {
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        string relative = files[i].Substring(root.Length + 1).Replace('\\', '/');
                        if (relative.Contains("_DoNotShip") || relative.Contains("DontShipItWithYourGame")) continue;
                        EditorUtility.DisplayProgressBar("Chill Road", "Zip : " + relative, i / (float)files.Length);
                        archive.CreateEntryFromFile(files[i], product + "/" + relative, System.IO.Compression.CompressionLevel.Optimal);
                        bytes += new FileInfo(files[i]).Length;
                        count++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            long zipped = new FileInfo(zip).Length;
            string text = $"{count} fichiers, {bytes / (1024 * 1024)} Mo → {zipped / (1024 * 1024)} Mo : {Path.GetFullPath(zip)}";
            File.AppendAllText("Builds/last_build.txt", Environment.NewLine + "zip | " + text);
            Debug.Log("NoHesi zip : " + text);
            return zip;
        }
    }
}
