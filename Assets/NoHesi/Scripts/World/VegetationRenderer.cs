using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NoHesi
{
    /// <summary>
    /// Rendu instancié de la végétation (GPU instancing) : chaque modèle n'existe qu'une fois en mémoire,
    /// les tuiles ne stockent que des matrices. À chaque image, chaque tuile visible choisit son niveau de détail
    /// selon sa distance à la caméra ; l'herbe et les fleurs ne sont dessinées que de près.
    /// </summary>
    public class VegetationRenderer : MonoBehaviour
    {
        public const int DetailLevel = 3;
        /// <summary>Végétation au sol (GroundFlora) : paliers proche (modèles détaillés), moyen et lointain.</summary>
        public const int FloraNear = 4, FloraMid = 5, FloraFar = 6;
        /// <summary>Tapis dense de GroundFlora : modèles détaillés tout près, puis moyens.</summary>
        public const int FloraDense = 7, FloraDenseMid = 8;

        [Tooltip("Distances de passage aux niveaux de détail 1 et 2, et distance max d'affichage")]
        public float lod1Distance = 160f, lod2Distance = 420f, cullDistance = 1300f;
        public float detailDistance = 220f;
        [Tooltip("Végétation au sol : fin des paliers proche, moyen et lointain (réglés par GroundFlora)")]
        public float floraNear = 20f, floraMid = 110f, floraFar = 230f;
        [Tooltip("Tapis dense : fin des modèles détaillés, fin du tapis (0 = désactivé)")]
        public float denseNear = 14f, denseFar = 62f;

        public struct BatchKey : System.IEquatable<BatchKey>
        {
            public Mesh mesh; public int submesh; public Material material; public int lod;
            public bool Equals(BatchKey o) => mesh == o.mesh && submesh == o.submesh && material == o.material && lod == o.lod;
            public override int GetHashCode() => (mesh.GetHashCode() * 397 ^ submesh * 31 ^ material.GetHashCode()) * 17 + lod;
        }

        class Tile
        {
            public Bounds bounds;
            public readonly Dictionary<BatchKey, List<Matrix4x4>> batches = new Dictionary<BatchKey, List<Matrix4x4>>();
        }

        readonly Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
        readonly Dictionary<BatchKey, List<Matrix4x4>> frame = new Dictionary<BatchKey, List<Matrix4x4>>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        readonly Plane[] planes = new Plane[6];

        public int DrawnInstances { get; private set; }
        /// <summary>Triangles dessinés à la dernière image : végétation au sol (GroundFlora), le reste (arbres, rochers,
        /// objets instanciés), et la part du reste qui projette aussi des ombres (dessinée une seconde fois par cascade).</summary>
        public long FloraTriangles { get; private set; }
        public long OtherTriangles { get; private set; }
        public long ShadowCasterTriangles { get; private set; }
        /// <summary>Captures d'écran depuis une autre caméra : tout est dessiné, sans tri par le champ de la caméra du jeu.</summary>
        public static bool NoFrustumCull;
        /// <summary>Végétation au sol : palier choisi plante par plante aux bornes des cases (faux : par case entière, pour comparer).</summary>
        public static bool FloraPerPlant = true;

        readonly Dictionary<Material, Material> instancedCopies = new Dictionary<Material, Material>();

        /// <summary>Matériau utilisable en instancing (copie si l'original est verrouillé, ex. intégré à un FBX).</summary>
        Material Instanced(Material m)
        {
            if (m.enableInstancing) return m;
            if (!instancedCopies.TryGetValue(m, out var copy))
            {
                copy = new Material(m) { enableInstancing = true, name = m.name + " (instanced)" };
                instancedCopies[m] = copy;
            }
            return copy;
        }

        public void Add(Vector2Int tile, Bounds tileBounds, BatchKey key, Matrix4x4 matrix)
        {
            key.material = Instanced(key.material);
            if (!tiles.TryGetValue(tile, out var t)) { t = new Tile { bounds = tileBounds }; tiles[tile] = t; }
            if (!t.batches.TryGetValue(key, out var list)) t.batches[key] = list = new List<Matrix4x4>();
            list.Add(matrix);
        }

        /// <summary>Enregistre d'un coup des lots entiers (végétation au sol : un carré de 32 m) ; les listes sont copiées.</summary>
        public void AddBatch(Vector2Int tile, Bounds tileBounds, Dictionary<BatchKey, List<Matrix4x4>> batches)
        {
            var t = new Tile { bounds = tileBounds };
            tiles[tile] = t;
            foreach (var kv in batches)
            {
                if (kv.Value.Count == 0) continue;
                var key = kv.Key;
                key.material = Instanced(key.material);
                if (!t.batches.TryGetValue(key, out var list)) t.batches[key] = list = new List<Matrix4x4>(kv.Value.Count);
                list.AddRange(kv.Value);
            }
        }

        public void Remove(Vector2Int tile) => tiles.Remove(tile);

        /// <summary>
        /// Enregistre un modèle entier (ses niveaux de détail, ou son seul maillage aux trois niveaux) à une position.
        /// detail = vrai : petit objet dessiné seulement de près.
        /// </summary>
        public void AddPrefab(Vector2Int tile, Bounds tileBounds, GameObject prefab, Matrix4x4 place, bool detail = false)
        {
            if (!prefab) return;
            var group = prefab.GetComponentInChildren<LODGroup>();
            if (group)
            {
                var levels = group.GetLODs();
                if (detail) { foreach (var r in levels[0].renderers) AddRenderer(tile, tileBounds, DetailLevel, r, prefab, place); return; }
                for (int l = 0; l < 3; l++)
                    foreach (var r in levels[Mathf.Min(l, levels.Length - 1)].renderers) AddRenderer(tile, tileBounds, l, r, prefab, place);
            }
            else
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
                {
                    if (detail) { AddRenderer(tile, tileBounds, DetailLevel, r, prefab, place); continue; }
                    for (int l = 0; l < 3; l++) AddRenderer(tile, tileBounds, l, r, prefab, place);
                }
        }

        void AddRenderer(Vector2Int tile, Bounds tileBounds, int level, Renderer r, GameObject prefab, Matrix4x4 place)
        {
            if (!r) return;
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) return;
            Matrix4x4 world = place * (prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix);
            var mats = r.sharedMaterials;
            for (int sub = 0; sub < mf.sharedMesh.subMeshCount && sub < mats.Length; sub++)
                if (mats[sub]) Add(tile, tileBounds, new BatchKey { mesh = mf.sharedMesh, submesh = sub, material = mats[sub], lod = level }, world);
        }

        /// <summary>Distances (min, max) auxquelles un palier de la végétation au sol est dessiné.</summary>
        void FloraRange(int level, out float lo, out float hi)
        {
            switch (level)
            {
                case FloraNear: lo = 0f; hi = floraNear; break;
                case FloraMid: lo = floraNear; hi = floraMid; break;
                case FloraFar: lo = floraMid; hi = floraFar; break;
                case FloraDense: lo = 0f; hi = denseNear; break;
                default: lo = denseNear; hi = denseFar; break;
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 camPos = cam.transform.position;

            foreach (var list in frame.Values) list.Clear();
            foreach (var t in tiles.Values)
            {
                if (!NoFrustumCull && !GeometryUtility.TestPlanesAABB(planes, t.bounds)) continue;
                // Niveau de détail selon la distance de la tuile à la caméra
                float d = Mathf.Sqrt(t.bounds.SqrDistance(camPos));
                if (d > cullDistance) continue;
                int lod = d < lod1Distance ? 0 : d < lod2Distance ? 1 : 2;
                bool details = d < detailDistance;
                // Végétation au sol : distance du coin le plus lointain de la case (pour savoir si elle chevauche un palier)
                float far = d + t.bounds.size.x + t.bounds.size.z;
                foreach (var kv in t.batches)
                {
                    int l = kv.Key.lod;
                    if (l >= FloraNear)
                    {
                        // Palier de la végétation au sol : chaque plante est retenue d'après sa propre distance quand la
                        // case chevauche les bornes du palier (sinon une case de 32 m dessinerait ses modèles détaillés
                        // jusqu'à 70 m)
                        FloraRange(l, out float lo, out float hi);
                        if (FloraPerPlant ? d >= hi || far < lo : d < lo || d >= hi) continue;
                        if (!frame.TryGetValue(kv.Key, out var bucket)) frame[kv.Key] = bucket = new List<Matrix4x4>();
                        if (!FloraPerPlant || (d >= lo && far < hi)) { bucket.AddRange(kv.Value); continue; }
                        float lo2 = lo * lo, hi2 = hi * hi;
                        var plants = kv.Value;
                        for (int i = 0; i < plants.Count; i++)
                        {
                            var m = plants[i];
                            float dx = m.m03 - camPos.x, dy = m.m13 - camPos.y, dz = m.m23 - camPos.z;
                            float d2 = dx * dx + dy * dy + dz * dz;
                            if (d2 >= lo2 && d2 < hi2) bucket.Add(m);
                        }
                        continue;
                    }
                    if (l == DetailLevel ? !details : lod == 0 ? l > 1 : l != lod) continue;
                    if (!frame.TryGetValue(kv.Key, out var target)) frame[kv.Key] = target = new List<Matrix4x4>();
                    if (lod == 0 && l <= 1)
                    {
                        // Case proche : le niveau complet (des milliers de triangles par arbre) se choisit objet par
                        // objet, d'après sa propre distance, et non pour toute la case
                        var all = kv.Value;
                        float near2 = lod1Distance * lod1Distance;
                        for (int i = 0; i < all.Count; i++)
                        {
                            var m = all[i];
                            float dx = m.m03 - camPos.x, dy = m.m13 - camPos.y, dz = m.m23 - camPos.z;
                            if ((dx * dx + dy * dy + dz * dz < near2) == (l == 0)) target.Add(m);
                        }
                        continue;
                    }
                    target.AddRange(kv.Value);
                }
            }

            DrawnInstances = 0;
            long sumFlora = 0, sumOther = 0, sumCasters = 0;
            foreach (var kv in frame)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                var key = kv.Key;
                var rp = new RenderParams(key.material)
                {
                    shadowCastingMode = key.lod <= 1 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                    worldBounds = new Bounds(camPos, Vector3.one * cullDistance * 2f),
                };
                for (int start = 0; start < list.Count; start += buffer.Length)
                {
                    int n = Mathf.Min(buffer.Length, list.Count - start);
                    list.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, key.mesh, key.submesh, buffer, n);
                }
                DrawnInstances += list.Count;
                long tris = (long)key.mesh.GetIndexCount(key.submesh) / 3 * list.Count;
                if (key.lod >= FloraNear) sumFlora += tris;
                else sumOther += tris;
                if (key.lod <= 1) sumCasters += tris;
            }
            FloraTriangles = sumFlora;
            OtherTriangles = sumOther;
            ShadowCasterTriangles = sumCasters;
        }
    }
}
