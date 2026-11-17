using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ChillRoad.EditorTools
{
    // Build de capture (copie du projet uniquement) : Capture/Capture.exe
    public static class CaptureBuild
    {
        public static void Build()
        {
            PlayerSettings.productName = "Chill Road Capture";
            Directory.CreateDirectory("Capture");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/ChillRoad/ChillRoad.unity" },
                locationPathName = "Capture/Capture.exe",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string text = $"{s.result} | {s.totalSize / (1024 * 1024)} Mo | {s.totalTime:mm\\:ss} | erreurs {s.totalErrors}";
            File.WriteAllText("Capture/build.txt", text);
            Debug.Log("Capture build : " + text);
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
