using UnityEngine;

namespace ChillRoad
{
    /// <summary>Feu orange clignotant (travaux).</summary>
    public class Blinker : MonoBehaviour
    {
        static Material material;
        Renderer lamp;
        float phase;

        public static void Attach(Transform parent, Vector3 localPos, float phase)
        {
            if (!material)
            {
                var src = RoadNetwork.Instance ? RoadNetwork.Instance.Assets.emissiveRed : null;
                material = src ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = "Blinker (runtime)";
                material.SetColor("_BaseColor", new Color(1f, 0.6f, 0.1f));
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.05f) * 6f);
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.035f;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var b = go.AddComponent<Blinker>();
            b.lamp = r;
            b.phase = phase;
        }

        void Update() => lamp.enabled = Mathf.Repeat(Time.time * 1.6f + phase, 1f) < 0.45f;
    }
}
