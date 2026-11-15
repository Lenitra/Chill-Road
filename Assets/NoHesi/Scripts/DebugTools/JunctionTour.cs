using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Tournée des carrefours (outil de mise au point) : le joueur est posé près de chaque nœud du réseau, le temps
    /// que le terrain fin s'y construise, puis le carrefour est photographié de biais et à la verticale.
    /// Lancer en jeu : NoHesi.JunctionTour.Run(). Les images « j_nom_a/b.png » vont dans le dossier de MapShot ;
    /// « j_done.txt » est écrit à la fin.
    /// </summary>
    public class JunctionTour : MonoBehaviour
    {
        public static void Run(string only = null)
        {
            var gm = FindFirstObjectByType<GameManager>();
            if (!gm) return;
            var go = new GameObject("JunctionTour");
            go.AddComponent<JunctionTour>().StartCoroutine(Tour(gm, only));
        }

        static IEnumerator Tour(GameManager gm, string only)
        {
            Application.runInBackground = true;
            VegetationRenderer.NoFrustumCull = true;
            if (gm.State == GameManager.Phase.Title) gm.StartCountdown();
            while (gm.State != GameManager.Phase.Playing) yield return null;
            gm.Weather.Force(false, true);
            File.Delete(MapShot.Folder + "j_done.txt");
            var nodes = new List<RoadNetwork.Node>();
            foreach (var n in gm.Road.Nodes.Values)
                if (n.edges.Count != 2 && (only == null || n.id.Contains(only))) nodes.Add(n);
            nodes.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            var log = new List<string>();
            foreach (var n in nodes)
            {
                gm.DayNight.timeOfDay = 13.5f;
                gm.TeleportTo(n.pos);
                gm.Player.Parked = true;
                gm.Player.InputEnabled = false;
                float until = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < until)
                {
                    // la voiture ne roule pas dans la pente pendant l'attente
                    gm.Player.Body.linearVelocity = Vector3.zero;
                    yield return null;
                }
                // voiture écartée de la photo
                Vector3 p = n.pos;
                float size = Mathf.Max(n.radius, 8f);
                // cap de la vue de biais : en face de l'arête la plus étroite (souvent la route secondaire)
                float heading = 30f;
                MapShot.Over("j_" + n.id + "_a", p.x, p.z, size * 4.5f + 22f, size * 3f + 16f, heading, 55f);
                MapShot.Take("j_" + n.id + "_b", p + Vector3.up * (size * 5f + 45f) + Vector3.forward * 0.1f, p, 55f);
                log.Add($"{n.id} edges={n.edges.Count} radius={n.radius:0.0} merge={n.merge} pos={p}");
            }
            File.WriteAllLines(MapShot.Folder + "j_done.txt", log);
            gm.Player.InputEnabled = true;
            VegetationRenderer.NoFrustumCull = false;
        }
    }
}
