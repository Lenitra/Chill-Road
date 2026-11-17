using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Décor posé de la carte (bâtiments, mobilier, signalisation, infrastructures) : la liste vient de la carte générée,
    /// les modèles du pack alpin. Les bâtiments et les objets à collision sont de vrais objets ; les petits décors
    /// passent par le rendu instancié, comme la végétation.
    /// </summary>
    public class MapProps : MonoBehaviour
    {
        /// <summary>Drapeau d'un objet de la carte : dessiné en instances, sans collision.</summary>
        public const int Instanced = 1;
        /// <summary>Petit objet : visible seulement de près.</summary>
        public const int Detail = 2;

        const float CellSize = 256f;
        Transform root;

        public int Count { get; private set; }

        public IEnumerator Build(GameAssets assets, MapData map, RoadNetwork road, VegetationRenderer instancer, System.Action<float> progress)
        {
            root = new GameObject("Props").transform;
            root.SetParent(transform, false);
            var places = map.Json.places;
            var pack = assets.alp;
            if (places == null || !pack) yield break;
            var cells = new Dictionary<Vector2Int, Transform>();
            float last = Time.realtimeSinceStartup;
            for (int i = 0; i < places.Length; i++)
            {
                var p = places[i];
                var item = pack.Find(p.item);
                if (item == null || !item.prefab) continue;
                Vector3 pos = new Vector3(p.x, p.y, p.z);
                Quaternion rot = Quaternion.Euler(0f, p.yaw, 0f);
                float scale = p.scale > 0f ? p.scale : 1f;
                var cell = new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
                Count++;
                if ((p.flags & Instanced) != 0)
                {
                    var bounds = new Bounds(new Vector3((cell.x + 0.5f) * CellSize, p.y, (cell.y + 0.5f) * CellSize), new Vector3(CellSize + 40f, 400f, CellSize + 40f));
                    instancer.AddPrefab(new Vector2Int(cell.x + 20000, cell.y), bounds, item.prefab, Matrix4x4.TRS(pos, rot, Vector3.one * scale), (p.flags & Detail) != 0);
                    continue;
                }
                if (!cells.TryGetValue(cell, out var parent))
                {
                    parent = new GameObject($"Cell_{cell.x}_{cell.y}").transform;
                    parent.SetParent(root, false);
                    cells[cell] = parent;
                }
                var go = Instantiate(item.prefab, pos, rot, parent);
                go.name = item.id;
                go.transform.localScale = Vector3.one * scale;
                TuneLods(go);
                AddCollider(go, item);
                if (item.lights != null && item.lights.Length > 0) road.RegisterLamp(go.transform, item.lights);
                if (Time.realtimeSinceStartup - last > 0.03f)
                {
                    progress?.Invoke(i / (float)places.Length);
                    yield return null;
                    last = Time.realtimeSinceStartup;
                }
            }
            progress?.Invoke(1f);
        }

        /// <summary>
        /// Niveaux de détail des modèles posés : le maillage complet (10 000 triangles et plus pour une maison) de près, le
        /// niveau intermédiaire ensuite, les volumes simples au loin ; jamais masqué. Seuils divisés par
        /// QualitySettings.lodBias (option « Distance des détails ») : à « Loin », une maison de 8 m garde son modèle
        /// complet jusqu'à ~60 m (avec l'ancien seuil 0,85 elle le perdait vers 15 m).
        /// </summary>
        static void TuneLods(GameObject go)
        {
            var group = go.GetComponentInChildren<LODGroup>();
            if (!group) return;
            var lods = group.GetLODs();
            if (lods.Length < 2) return;
            lods[0].screenRelativeTransitionHeight = 0.4f;
            for (int i = 1; i < lods.Length - 1; i++) lods[i].screenRelativeTransitionHeight = 0.12f;
            lods[lods.Length - 1].screenRelativeTransitionHeight = 0.002f;
            group.SetLODs(lods);
        }

        static void AddCollider(GameObject go, AlpPack.Item item)
        {
            switch (item.collider)
            {
                case "box":
                {
                    // Emprise des murs, prolongée sous le sol (bâtiments posés sur une pente)
                    var box = go.AddComponent<BoxCollider>();
                    float below = Mathf.Max(0f, -item.ymin);
                    box.size = new Vector3(Mathf.Max(item.footprint.x, 0.2f), item.size.y + below, Mathf.Max(item.footprint.y, 0.2f));
                    box.center = new Vector3(0f, (item.size.y - below) * 0.5f, 0f);
                    break;
                }
                case "capsule":
                {
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.radius = item.radius > 0f ? item.radius : Mathf.Max(item.footprint.x, item.footprint.y) * 0.5f;
                    cap.height = Mathf.Max(item.size.y + 0.5f, cap.radius * 2f);
                    cap.center = new Vector3(0f, item.size.y * 0.5f - 0.25f, 0f);
                    break;
                }
                case "mesh":
                {
                    // Objets traversables (portail de tunnel, galerie, abribus) : collision sur le maillage détaillé
                    var group = go.GetComponentInChildren<LODGroup>();
                    var renderers = group ? group.GetLODs()[0].renderers : go.GetComponentsInChildren<Renderer>();
                    foreach (var r in renderers)
                    {
                        var mf = r ? r.GetComponent<MeshFilter>() : null;
                        if (mf && mf.sharedMesh) r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    }
                    break;
                }
                case "mesh_low":
                {
                    // Tremplins : collision sur le niveau lointain (volume plein, lisse) : les roues n'accrochent pas
                    // les bordures et les décors du maillage détaillé
                    var group = go.GetComponentInChildren<LODGroup>();
                    if (!group) goto case "mesh";
                    var lods = group.GetLODs();
                    foreach (var r in lods[lods.Length - 1].renderers)
                    {
                        var mf = r ? r.GetComponent<MeshFilter>() : null;
                        if (mf && mf.sharedMesh) r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    }
                    break;
                }
            }
        }
    }
}
