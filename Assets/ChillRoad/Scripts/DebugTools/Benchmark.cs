using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Banc d'essai des performances (« Chill Road.exe -benchmark ») : après le chargement, la voiture est posée en
    /// plusieurs endroits de la carte (village, alpage, autoroute, lac), en plein jour et par beau temps, et les temps
    /// d'image sont mesurés à chaque niveau de qualité graphique. Le rapport est écrit dans benchmark.txt (dossier des
    /// données persistantes, et à côté de l'exécutable si possible), puis le jeu se ferme. Les réglages du joueur ne
    /// sont pas modifiés.
    /// </summary>
    public class Benchmark : MonoBehaviour
    {
        static readonly (string name, Vector2 xz)[] Spots =
        {
            ("village (Le Plan)", new Vector2(300f, -1810f)),
            ("alpage", new Vector2(-1090f, 215f)),
            ("autoroute (lac)", new Vector2(1500f, -1600f)),
            ("col", new Vector2(50f, 420f)),
        };
        const float Settle = 4f, Measure = 6f;

        GameManager game;

        public void Init(GameManager gm)
        {
            game = gm;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            while (game.State != GameManager.Phase.Title && game.State != GameManager.Phase.Playing) yield return null;
            int quality = GameSettings.Quality, cap = GameSettings.FpsCap, detail = GameSettings.DetailDistance;
            bool vsync = GameSettings.VSync;
            Application.runInBackground = true;
            if (game.State == GameManager.Phase.Title) game.StartCountdown();
            yield return new WaitForSeconds(4f);
            var report = new StringBuilder();
            report.AppendLine("Chill Road - banc d'essai " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            report.AppendLine(SystemInfo.processorType + " | " + SystemInfo.graphicsDeviceName + " | " + Screen.width + "x" + Screen.height);
            GameSettings.FpsCap = GraphicsQuality.FpsCaps.Length - 1;     // illimité pendant la mesure
            GameSettings.VSync = false;
            for (int q = 0; q < 4; q++)
            {
                GameSettings.Quality = q;
                GameSettings.DetailDistance = GraphicsQuality.DefaultDetail(q);
                GraphicsQuality.Apply(game);
                report.AppendLine();
                report.AppendLine("Qualité " + GraphicsQuality.Name(q));
                foreach (var (name, xz) in Spots)
                {
                    game.Weather.Force(false, true);
                    game.DayNight.timeOfDay = 15f;
                    game.DayNight.frozen = true;
                    game.TeleportTo(new Vector3(xz.x, 0f, xz.y));
                    if (game.Player) { game.Player.InputEnabled = false; game.Player.Parked = true; }
                    yield return new WaitForSeconds(Settle);
                    FrameStats.Begin();
                    yield return new WaitForSeconds(Measure);
                    string line = "  " + name.PadRight(20) + FrameStats.Report() + " | " + FrameStats.CpuGpu();
                    report.AppendLine(line);
                    Debug.Log("[Benchmark] Q" + q + " " + line);
                }
            }
            GameSettings.Quality = quality;
            GameSettings.FpsCap = cap;
            GameSettings.VSync = vsync;
            GameSettings.DetailDistance = detail;
            GraphicsQuality.Apply(game);
            string text = report.ToString();
            Debug.Log(text);
            TryWrite(Path.Combine(Application.persistentDataPath, "benchmark.txt"), text);
            TryWrite(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "benchmark.txt"), text);
#if UNITY_EDITOR
            Destroy(this);
#else
            Application.Quit();
#endif
        }

        static void TryWrite(string path, string text)
        {
            try { File.WriteAllText(path, text); } catch (System.Exception) { }
        }
    }
}
