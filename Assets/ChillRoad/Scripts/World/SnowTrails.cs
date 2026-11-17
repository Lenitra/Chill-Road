using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Neige déformable : les roues tassent la neige et y laissent des sillons. Une texture de tassement de 80 m de
    /// côté suit le joueur (recopiée décalée quand il avance, comblée lentement par la neige qui tombe) ; chaque roue
    /// au sol y imprime son passage. Les shaders du sol et de la route la lisent (Shaders/SnowTrail.hlsl) : fond du
    /// sillon tassé, bords éclairés, bourrelet. Le trafic proche et les autres joueurs laissent aussi leurs traces,
    /// calculées sur cette machine.
    /// </summary>
    public class SnowTrails : MonoBehaviour
    {
        const int Size = 1024;
        const float Area = 80f;
        const float Texel = Area / Size;

        static readonly int TrailId = Shader.PropertyToID("_ChillRoadSnowTrail");
        static readonly int RectId = Shader.PropertyToID("_ChillRoadSnowTrailRect");
        static readonly int OnId = Shader.PropertyToID("_ChillRoadSnowTrailOn");
        static readonly int ScrollId = Shader.PropertyToID("_Scroll");

        struct Track { public Vector3 last; public int frame; }
        struct Segment { public Vector3 a, b; public float radius, depth; }

        MapData map;
        GameManager gm;
        RenderTexture front, back;
        Material mat;
        Vector2 origin;
        bool active;
        float idle;
        readonly Dictionary<long, Track> tracks = new Dictionary<long, Track>();
        readonly List<Segment> segments = new List<Segment>();
        readonly List<long> stale = new List<long>();

        public void Init(MapData m)
        {
            map = m;
            gm = GetComponent<GameManager>();
            var shader = Resources.Load<Shader>("SnowTrail");
            if (!shader || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)) { enabled = false; return; }
            mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            front = Create();
            back = Create();
            Shader.SetGlobalFloat(OnId, 0f);
        }

        static RenderTexture Create()
        {
            var rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
            {
                name = "SnowTrail", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false
            };
            rt.Create();
            Clear(rt);
            return rt;
        }

        static void Clear(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = prev;
        }

        void OnDestroy()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            if (front) front.Release();
            if (back) back.Release();
            if (mat) Destroy(mat);
            if (stampMesh) Destroy(stampMesh);
        }

        void LateUpdate()
        {
            if (!mat || map == null || !gm || !gm.Player) return;
            Vector3 p = gm.Player.transform.position;
            // Loin de la neige : rien à faire (la texture est vidée au retour)
            bool snowy = map.Snow(p.x, p.z) > 0.02f;
            idle = snowy ? 0f : idle + Time.deltaTime;
            if (idle > 4f)
            {
                if (active) { active = false; Shader.SetGlobalFloat(OnId, 0f); tracks.Clear(); }
                return;
            }
            Vector2 o = new Vector2(Mathf.Floor((p.x - Area * 0.5f) / Texel) * Texel, Mathf.Floor((p.z - Area * 0.5f) / Texel) * Texel);
            if (!active)
            {
                active = true;
                origin = o;
                Clear(front);
            }
            Collect(p);

            // Recopie décalée + comblement : la neige qui tombe referme les sillons (plus vite sous une averse)
            float snowfall = gm.Weather ? gm.Weather.Rain : 0f;
            float fade = Mathf.Exp(-Time.deltaTime / Mathf.Lerp(240f, 30f, snowfall));
            mat.SetVector(ScrollId, new Vector4((o.x - origin.x) / Area, (o.y - origin.y) / Area, fade, 0f));
            Graphics.Blit(front, back, mat, 0);
            origin = o;
            if (segments.Count > 0) Stamp(back);
            (front, back) = (back, front);

            Shader.SetGlobalTexture(TrailId, front);
            Shader.SetGlobalVector(RectId, new Vector4(origin.x, origin.y, 1f / Area, Texel));
            Shader.SetGlobalFloat(OnId, 1f);
        }

        /// <summary>Passages de roues depuis l'image précédente : le joueur (contacts réels), le trafic proche et les
        /// autres joueurs proches.</summary>
        void Collect(Vector3 center)
        {
            segments.Clear();
            int frame = Time.frameCount;
            var car = gm.Player;
            for (int i = 0; i < car.WheelCount; i++)
                if (car.WheelContact(i, out var point, out _, out _)) Add(i, point, 0.17f, 1f, frame);
            if (gm.Traffic != null)
            {
                foreach (var c in gm.Traffic.Cars)
                {
                    if (c == null || !c.go) continue;
                    Vector3 q = c.cachedPos;
                    float dx = q.x - center.x, dz = q.z - center.z;
                    if (dx * dx + dz * dz > 42f * 42f || map.Snow(q.x, q.z) < 0.2f) continue;
                    Vector3 f = c.cachedFwd;
                    Vector3 r = new Vector3(f.z, 0f, -f.x);
                    long id = (long)c.go.GetInstanceID() << 3;
                    float w = c.halfWidth * 0.8f, l = c.length * 0.3f;
                    for (int k = 0; k < 4; k++)
                        Add(id + 8 + k, q + r * (k % 2 == 0 ? -w : w) + f * (k < 2 ? -l : l), c.truck ? 0.22f : 0.17f, 0.9f, frame);
                }
            }
            // Autres joueurs : rien ne passe par le réseau, leurs sillons sont calculés ici à partir de la voiture
            // montrée (roues à leur place de repos, au sol selon l'état reçu : la trace s'interrompt pendant un saut)
            if (gm.Online)
            {
                foreach (var remote in gm.Net.Remotes)
                {
                    if (!remote || !remote.Ready || !remote.Car || !remote.gameObject.activeInHierarchy) continue;
                    Vector3 q = remote.transform.position;
                    float dx = q.x - center.x, dz = q.z - center.z;
                    if (dx * dx + dz * dz > 42f * 42f) continue;
                    var rc = remote.Car;
                    // Clés à part du joueur (0-7) et du trafic : bit 40 + identifiant de l'objet
                    long id = (1L << 40) | ((long)(uint)remote.GetInstanceID() << 3);
                    for (int i = 0; i < rc.WheelCount && i < 8; i++)
                        if (rc.WheelContact(i, out var point, out _, out _)) Add(id + i, point, 0.17f, 1f, frame);
                }
            }
            // Roues qui ne touchent plus le sol (saut, voiture sortie de la zone) : la trace s'interrompt
            stale.Clear();
            foreach (var kv in tracks) if (kv.Value.frame != frame) stale.Add(kv.Key);
            foreach (var k in stale) tracks.Remove(k);
        }

        void Add(long key, Vector3 point, float radius, float depth, int frame)
        {
            if (tracks.TryGetValue(key, out var t))
            {
                float d = (point - t.last).magnitude;
                if (d < 0.03f) { t.frame = frame; tracks[key] = t; return; }
                if (d < 5f) segments.Add(new Segment { a = t.last, b = point, radius = radius, depth = depth });
            }
            else segments.Add(new Segment { a = point, b = point, radius = radius, depth = depth });
            tracks[key] = new Track { last = point, frame = frame };
        }

        Mesh stampMesh;
        UnityEngine.Rendering.CommandBuffer stampCmd;
        readonly List<Vector3> sv = new List<Vector3>();
        readonly List<Vector2> su = new List<Vector2>();
        readonly List<Vector3> sd = new List<Vector3>();
        readonly List<int> si = new List<int>();

        /// <summary>Imprime les passages de roues : un quadrilatère par segment, positions données en coordonnées de
        /// la texture (le shader n'utilise aucune matrice de caméra).</summary>
        void Stamp(RenderTexture target)
        {
            if (!stampMesh) stampMesh = new Mesh { name = "SnowTrailStamps", hideFlags = HideFlags.HideAndDontSave };
            if (stampCmd == null) stampCmd = new UnityEngine.Rendering.CommandBuffer { name = "SnowTrail" };
            sv.Clear(); su.Clear(); sd.Clear(); si.Clear();
            foreach (var s in segments)
            {
                Vector2 a = new Vector2(s.a.x, s.a.z), b = new Vector2(s.b.x, s.b.z);
                Vector2 d = b - a;
                float len = d.magnitude;
                Vector2 dir = len > 1e-4f ? d / len : Vector2.right;
                Vector2 side = new Vector2(-dir.y, dir.x);
                float r = s.radius + Texel;
                int v0 = sv.Count;
                for (int k = 0; k < 4; k++)
                {
                    float u = k == 0 || k == 3 ? -r : len + r;
                    float v = k < 2 ? -r : r;
                    Vector2 w = (a + dir * u + side * v - origin) / Area;
                    sv.Add(new Vector3(w.x, w.y, 0f));
                    su.Add(new Vector2(u, v));
                    sd.Add(new Vector3(len, s.radius, s.depth));
                }
                si.Add(v0); si.Add(v0 + 1); si.Add(v0 + 2); si.Add(v0); si.Add(v0 + 2); si.Add(v0 + 3);
            }
            stampMesh.Clear();
            stampMesh.SetVertices(sv);
            stampMesh.SetUVs(0, su);
            stampMesh.SetUVs(1, sd);
            stampMesh.SetTriangles(si, 0, false);
            stampMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            stampCmd.Clear();
            stampCmd.SetRenderTarget(target);
            stampCmd.DrawMesh(stampMesh, Matrix4x4.identity, mat, 0, 1);
            Graphics.ExecuteCommandBuffer(stampCmd);
        }
    }
}
