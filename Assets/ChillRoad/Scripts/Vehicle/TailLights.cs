using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Optiques d'un modèle de voiture : les matériaux émissifs rouges (feux arrière) et blancs (phares) sont retrouvés
    /// automatiquement et leur éclat est modulé par matériau (éteints le jour, feux stop), sans toucher aux autres
    /// matériaux du même objet.
    /// </summary>
    public static class TailLights
    {
        public struct Slot
        {
            public Renderer renderer;
            public int index;
            public Color emission;
        }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Feux arrière (émission rouge) ou phares (émission blanche à jaune pâle) d'un objet et de ses enfants.</summary>
        public static Slot[] Find(GameObject root, bool rear = true)
        {
            var list = new List<Slot>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m || !m.HasProperty(EmissionId)) continue;
                    Color e = m.GetColor(EmissionId);
                    bool red = e.r > 0.3f && e.g < e.r * 0.35f && e.b < e.r * 0.35f;
                    bool white = e.r > 0.3f && e.g > e.r * 0.55f && e.b > e.r * 0.35f;
                    if (rear ? red : white) list.Add(new Slot { renderer = r, index = i, emission = e });
                }
            }
            return list.ToArray();
        }

        /// <summary>Applique un facteur d'éclat (0 = éteint, 1 = normal, ~2 = freinage de nuit).</summary>
        public static void Apply(Slot[] slots, float factor, MaterialPropertyBlock mpb)
        {
            if (slots == null) return;
            foreach (var s in slots)
            {
                if (!s.renderer) continue;
                s.renderer.GetPropertyBlock(mpb, s.index);
                mpb.SetColor(EmissionId, s.emission * factor);
                s.renderer.SetPropertyBlock(mpb, s.index);
            }
        }

        /// <summary>
        /// Éclat des feux arrière : éteints le jour sauf au freinage (plus discret que la nuit) ;
        /// de nuit, le feu stop est environ deux fois plus fort que la veilleuse, sans éblouir.
        /// </summary>
        public static float RearFactor(float night, bool braking) => braking ? Mathf.Lerp(1.3f, 2.1f, night) : night * 1f;
    }
}
