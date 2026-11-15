using System.Collections.Generic;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Phares et feux arrière de la voiture du joueur, allumés et éteints par le joueur (L3 / L) : projecteurs avant,
    /// feux de position et optiques qui s'illuminent. Les feux stop fonctionnent toujours, phares allumés ou non.
    /// Les phares escamotables du modèle (enfants « popup-* », pivot sur la charnière) se lèvent avec les phares.
    /// Les faisceaux partent des repères « headlight* » du modèle s'il en a (un seul pour la Peel P50), sinon de deux
    /// points calculés d'après l'encombrement.
    /// </summary>
    public class CarLights : MonoBehaviour
    {
        /// <summary>Bascule des phares escamotables, avant vers le haut (même valeur que POPUP_ANGLE dans play_cars.py).</summary>
        const float PopupAngle = 60f;
        const float PopupTime = 0.6f;

        VehicleController car;
        GameAssets assets;
        Transform root;
        Light[] beams;
        Light[] rear;
        TailLights.Slot[] tails, heads;
        MaterialPropertyBlock mpb;
        float level, glow = -1f, headGlow = -1f, beamScale = 1f;
        Transform[] popups;
        Quaternion[] popupClosed;
        float popupOpen;

        /// <summary>Phares allumés (choix du joueur, gardé aux changements de voiture).</summary>
        public bool On { get; private set; }
        public event System.Action<bool> Toggled;

        public void Init(VehicleController c, GameAssets a)
        {
            car = c;
            assets = a;
            mpb = new MaterialPropertyBlock();
            Rebuild();
        }

        /// <summary>À rappeler après un changement de voiture (dimensions différentes).</summary>
        public void Rebuild()
        {
            if (root) Destroy(root.gameObject);
            root = new GameObject("CarLights").transform;
            root.SetParent(car.transform, false);
            Bounds b = car.LocalBounds;
            float front = b.max.z - 0.05f, back = b.min.z + 0.05f;
            float y = b.min.y + b.size.y * 0.35f, x = b.extents.x * 0.62f;

            // Faisceaux : un par repère « headlight* » du modèle (ex. le phare unique de la Peel P50), sinon deux
            var marks = new List<Transform>();
            foreach (var t in car.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("headlight")) marks.Add(t);
            int count = marks.Count > 0 ? marks.Count : 2;
            beamScale = count == 1 ? 1.6f : 1f;          // un phare seul éclaire un peu plus fort
            beams = new Light[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Headlight");
                go.transform.SetParent(root, false);
                if (marks.Count > 0)
                {
                    Vector3 p = car.transform.InverseTransformPoint(marks[i].position);
                    go.transform.localPosition = p;
                    go.transform.localRotation = Quaternion.Euler(1.8f, Mathf.Abs(p.x) < 0.05f ? 0f : Mathf.Sign(p.x) * 3f, 0f);
                }
                else
                {
                    go.transform.localPosition = new Vector3(i == 0 ? -x : x, y, front);
                    go.transform.localRotation = Quaternion.Euler(1.8f, i == 0 ? -3f : 3f, 0f);
                }
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 150f;
                l.spotAngle = 60f;
                l.innerSpotAngle = 26f;
                l.color = new Color(1f, 0.95f, 0.85f);
                l.shadows = LightShadows.None;
                l.intensity = 0f;
                beams[i] = l;
            }

            // Lumière rouge des feux arrière, projetée vers l'arrière et vers le sol
            rear = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("TailLight");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(i == 0 ? -x : x, y + 0.08f, back - 0.15f);
                go.transform.localRotation = Quaternion.Euler(28f, 180f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 16f;
                l.spotAngle = 125f;
                l.innerSpotAngle = 60f;
                l.color = new Color(1f, 0.07f, 0.04f);
                l.shadows = LightShadows.None;
                l.intensity = 0f;
                rear[i] = l;
            }

            // Optiques émissives si le modèle n'en a pas déjà (modèles Kenney)
            bool hasEmissive = false;
            foreach (var r in car.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m && m.IsKeywordEnabled("_EMISSION")) hasEmissive = true;
            if (!hasEmissive)
            {
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    Cube(new Vector3(side * x, y, front + 0.02f), new Vector3(0.32f, 0.12f, 0.04f), assets.emissiveWhite);
                    Cube(new Vector3(side * x, y + 0.08f, back - 0.02f), new Vector3(0.34f, 0.1f, 0.04f), assets.emissiveRed);
                }
            }
            // Optiques arrière du modèle (ou des cubes ci-dessus) : leur éclat suit les feux stop
            tails = TailLights.Find(car.gameObject);
            heads = TailLights.Find(car.gameObject, false);
            glow = headGlow = -1f;

            // Phares escamotables : déjà dans la bonne position au changement de voiture
            var found = new List<Transform>();
            foreach (var t in car.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("popup")) found.Add(t);
            popups = found.ToArray();
            popupClosed = new Quaternion[popups.Length];
            for (int i = 0; i < popups.Length; i++) popupClosed[i] = popups[i].localRotation;
            popupOpen = level > 0.02f ? 1f : 0f;
            PosePopups();
        }

        void PosePopups()
        {
            float k = Mathf.SmoothStep(0f, 1f, popupOpen);
            for (int i = 0; i < popups.Length; i++)
                if (popups[i]) popups[i].localRotation = popupClosed[i] * Quaternion.AngleAxis(-PopupAngle * k, Vector3.right);
        }

        Renderer Cube(Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public void Toggle()
        {
            On = !On;
            Toggled?.Invoke(On);
        }

        /// <summary>Allume ou éteint (voiture d'un autre joueur : état reçu du réseau).</summary>
        public void Set(bool on)
        {
            if (on != On) Toggle();
        }

        void Update()
        {
            if (!car || beams == null) return;
            if (car.InputEnabled && GameInput.LightsPressed) Toggle();
            // Allumage bref et progressif (pas d'allumage automatique à la tombée de la nuit)
            level = Mathf.MoveTowards(level, On ? 1f : 0f, Time.deltaTime * 6f);
            foreach (var l in beams)
            {
                if (!l) continue;
                l.intensity = level * 450f * beamScale;
                l.enabled = level > 0.02f;
            }
            // Feux stop : optiques et lumière projetée bien plus fortes au freinage
            bool braking = car.BrakeInput > 0.1f && car.ForwardSpeed > 0.5f;
            // Phares éteints : seuls les feux stop s'allument, plus discrètement
            float target = TailLights.RearFactor(level, braking);
            if (Mathf.Abs(target - glow) > 0.01f)
            {
                glow = target;
                TailLights.Apply(tails, glow, mpb);
            }
            if (Mathf.Abs(level - headGlow) > 0.01f)
            {
                headGlow = level;
                TailLights.Apply(heads, headGlow, mpb);
            }
            float popupTarget = level > 0.02f ? 1f : 0f;
            if (popups != null && popups.Length > 0 && popupOpen != popupTarget)
            {
                popupOpen = Mathf.MoveTowards(popupOpen, popupTarget, Time.deltaTime / PopupTime);
                PosePopups();
            }
            float rearIntensity = braking ? Mathf.Lerp(1.2f, 8f, level) : level * 3.5f;
            foreach (var l in rear)
            {
                if (!l) continue;
                l.intensity = rearIntensity;
                l.enabled = rearIntensity > 0.05f;
            }
        }
    }
}
