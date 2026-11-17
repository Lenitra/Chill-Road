using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Réserve de lumières attribuées aux objets les plus proches (lampadaires, voitures).
    /// Une lumière reste attachée au même objet tant qu'il reste parmi les plus proches, et apparaît / disparaît
    /// en fondu : aucune lumière ne saute d'un objet à l'autre (pas de clignotement).
    /// </summary>
    public class LightPool<T> where T : class
    {
        readonly Light[] lights;
        readonly T[] owners;
        readonly bool[] leaving;
        readonly float[] fade;

        public LightPool(IList<Light> pool)
        {
            lights = new Light[pool.Count];
            pool.CopyTo(lights, 0);
            owners = new T[lights.Length];
            leaving = new bool[lights.Length];
            fade = new float[lights.Length];
        }

        /// <summary>Objets à éclairer (du plus prioritaire au moins prioritaire).</summary>
        public void Assign(List<T> wanted)
        {
            // Les objets qui ne sont plus voulus s'éteignent en fondu ; ceux qui reviennent se rallument
            for (int i = 0; i < owners.Length; i++)
                if (owners[i] != null) leaving[i] = !wanted.Contains(owners[i]);
            foreach (var w in wanted)
            {
                if (Array.IndexOf(owners, w) >= 0) continue;
                int free = Array.IndexOf(owners, null);
                if (free < 0) break;
                owners[free] = w;
                leaving[free] = false;
                fade[free] = 0f;
            }
        }

        /// <summary>Place et règle chaque lumière ; un objet disparu libère sa lumière immédiatement.</summary>
        public void Update(float dt, Func<T, bool> alive, Action<Light, T> place, Func<T, float> intensity)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                var owner = owners[i];
                if (owner != null && !alive(owner)) { owners[i] = null; owner = null; }
                if (owner == null) { lights[i].enabled = false; continue; }
                fade[i] = Mathf.MoveTowards(fade[i], leaving[i] ? 0f : 1f, dt * 3f);
                if (leaving[i] && fade[i] <= 0f) { owners[i] = null; lights[i].enabled = false; continue; }
                place(lights[i], owner);
                lights[i].intensity = intensity(owner) * fade[i];
                lights[i].enabled = lights[i].intensity > 0.01f;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < owners.Length; i++) { owners[i] = null; lights[i].enabled = false; }
        }
    }
}
