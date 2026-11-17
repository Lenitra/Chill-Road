using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Catalogue du pack alpin (bâtiments, décors, nature) : un modèle par identifiant, avec les données du manifeste
    /// exporté par Blender (emprise, collision, têtes de lampes). Rempli par le menu « ChillRoad > Importer le pack alpin ».
    /// </summary>
    public class AlpPack : ScriptableObject
    {
        [Serializable]
        public class Item
        {
            public string id, category;
            public string[] tags;
            public GameObject prefab;
            [Tooltip("Boîte englobante (largeur, hauteur au-dessus du sol, profondeur) en mètres")] public Vector3 size;
            [Tooltip("Emprise des murs au sol (largeur, profondeur)")] public Vector2 footprint;
            [Tooltip("box, capsule, mesh ou none")] public string collider;
            public float radius, module, ymin;
            [Tooltip("Têtes de lampes (repère du modèle)")] public Vector3[] lights;

            public bool Has(string tag) => tags != null && Array.IndexOf(tags, tag) >= 0;
        }

        public Item[] items = new Item[0];
        public Material material;

        Dictionary<string, Item> lookup;

        public Item Find(string id)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Item>();
                foreach (var it in items) if (it != null && !string.IsNullOrEmpty(it.id)) lookup[it.id] = it;
            }
            return id != null && lookup.TryGetValue(id, out var item) ? item : null;
        }

        public GameObject Prefab(string id) => Find(id)?.prefab;

        /// <summary>Modèles existants parmi une liste d'identifiants.</summary>
        public GameObject[] Prefabs(params string[] ids)
        {
            var list = new List<GameObject>();
            foreach (var id in ids)
            {
                var p = Prefab(id);
                if (p) list.Add(p);
            }
            return list.ToArray();
        }
    }
}
