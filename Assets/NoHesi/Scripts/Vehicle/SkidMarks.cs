using UnityEngine;

namespace NoHesi
{
    /// <summary>Traces de pneus sur le goudron : rubans posés sous les roues qui glissent (tampon circulaire).</summary>
    public class SkidMarks
    {
        const int MaxQuads = 1600;
        const float HalfWidth = 0.14f;
        const float MinSegment = 0.3f;

        readonly Mesh mesh;
        readonly Vector3[] vertices = new Vector3[MaxQuads * 4];
        readonly Color32[] colors = new Color32[MaxQuads * 4];
        readonly Vector2[] uvs = new Vector2[MaxQuads * 4];
        readonly Vector3[] last = new Vector3[4];
        readonly float[] lastAlpha = new float[4];
        readonly float[] distance = new float[4];
        readonly bool[] active = new bool[4];
        int next;
        bool dirty;

        public SkidMarks(Material material)
        {
            var go = new GameObject("SkidMarks");
            mesh = new Mesh { name = "SkidMarks", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var tris = new int[MaxQuads * 6];
            for (int q = 0; q < MaxQuads; q++)
            {
                int v = q * 4, t = q * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Prolonge la trace de la roue (point au sol, normale, intensité 0-1).</summary>
        public void Add(int wheel, Vector3 point, Vector3 normal, float intensity)
        {
            Vector3 p = point + normal * 0.03f;
            if (!active[wheel])
            {
                active[wheel] = true;
                last[wheel] = p;
                lastAlpha[wheel] = 0f;
                return;
            }
            Vector3 d = p - last[wheel];
            if (d.sqrMagnitude < MinSegment * MinSegment) return;
            if (d.sqrMagnitude > 4f) { last[wheel] = p; return; } // téléportation
            Vector3 side = Vector3.Cross(normal, d.normalized) * HalfWidth;
            float a = Mathf.Clamp01(intensity);
            int v = next * 4;
            vertices[v] = last[wheel] - side; vertices[v + 1] = last[wheel] + side;
            vertices[v + 2] = p - side; vertices[v + 3] = p + side;
            byte a0 = (byte)(lastAlpha[wheel] * 255f), a1 = (byte)(a * 255f);
            colors[v] = colors[v + 1] = new Color32(255, 255, 255, a0);
            colors[v + 2] = colors[v + 3] = new Color32(255, 255, 255, a1);
            float u0 = distance[wheel] / 2f, u1 = (distance[wheel] + d.magnitude) / 2f;
            uvs[v] = new Vector2(0f, u0); uvs[v + 1] = new Vector2(1f, u0);
            uvs[v + 2] = new Vector2(0f, u1); uvs[v + 3] = new Vector2(1f, u1);
            distance[wheel] += d.magnitude;
            last[wheel] = p;
            lastAlpha[wheel] = a;
            next = (next + 1) % MaxQuads;
            dirty = true;
        }

        public void End(int wheel) => active[wheel] = false;

        public void Flush()
        {
            if (!dirty) return;
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.uv = uvs;
            dirty = false;
        }
    }
}
