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
    /// Appel de phares (croix ↓ / F) : plein phares le temps de l'appui, phares allumés ou éteints, de jour comme de
    /// nuit ; les optiques brillent plus fort et un halo se pose devant chaque phare, visible de face et de loin.
    /// </summary>
    public class CarLights : MonoBehaviour
    {
        /// <summary>Bascule des phares escamotables, avant vers le haut (même valeur que POPUP_ANGLE dans play_cars.py).</summary>
        const float PopupAngle = 60f;
        const float PopupTime = 0.6f;
        /// <summary>Durée minimale d'un appel de phares (s) : un appui bref se voit quand même, ici et chez les autres joueurs.</summary>
        const float FlashMin = 0.2f;
        /// <summary>Appel de phares : intensité ajoutée aux faisceaux, éclat ajouté aux optiques (1 = phares allumés).</summary>
        const float FlashBeam = 800f, FlashGlow = 2.5f;
        /// <summary>Halo de l'appel de phares : taille de près (m), agrandissement par mètre de distance, distance où il disparaît.</summary>
        const float FlareSize = 0.8f, FlareGrowth = 0.018f, FlareRange = 600f;

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
        // Appel de phares : niveau (0-1), reste de la durée minimale, halos devant les phares
        float flash, flashHold;
        bool flashWas;
        Renderer[] flares;
        static Material flareMaterial;

        /// <summary>
        /// Appel de phares demandé : touche tenue (voiture du joueur, réglé par <see cref="GameManager"/>) ou état reçu
        /// du réseau (voiture d'un autre joueur).
        /// </summary>
        public bool Flash;
        /// <summary>Appel de phares en cours (demandé, ou encore dans sa durée minimale) : c'est lui qui part sur le réseau.</summary>
        public bool Flashing => Flash || flashHold > 0f;

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

            // Halo de l'appel de phares : un disque lumineux devant chaque phare, tourné vers la caméra (UpdateFlares).
            // Créé après la recherche des optiques : ce n'en est pas une
            if (!flareMaterial && assets.spark)
            {
                flareMaterial = new Material(assets.spark) { name = "Headlight flare (runtime)" };
                flareMaterial.SetColor("_BaseColor", new Color(1.5f, 1.42f, 1.22f, 1f));
            }
            flares = new Renderer[flareMaterial ? count : 0];
            for (int i = 0; i < flares.Length; i++)
            {
                var flare = Cube(beams[i].transform.localPosition, Vector3.one, flareMaterial, PrimitiveType.Quad);
                flare.name = "HeadlightFlare";
                flare.receiveShadows = false;
                flare.enabled = false;
                flares[i] = flare;
            }

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

        Renderer Cube(Vector3 pos, Vector3 size, Material mat, PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
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
            // Appel de phares : plein phares aussitôt, retombée rapide ; un appui bref dure quand même un instant
            if (Flash && !flashWas) flashHold = FlashMin;
            flashWas = Flash;
            flashHold = Mathf.Max(0f, flashHold - Time.deltaTime);
            flash = Mathf.MoveTowards(flash, Flashing ? 1f : 0f, Time.deltaTime * (Flashing ? 20f : 9f));
            float beam = level * 450f + flash * FlashBeam;
            foreach (var l in beams)
            {
                if (!l) continue;
                l.intensity = beam * beamScale;
                l.enabled = beam > 9f;
            }
            UpdateFlares();
            // Feux stop : optiques et lumière projetée bien plus fortes au freinage
            bool braking = car.BrakeInput > 0.1f && car.ForwardSpeed > 0.5f;
            // Phares éteints : seuls les feux stop s'allument, plus discrètement
            float target = TailLights.RearFactor(level, braking);
            if (Mathf.Abs(target - glow) > 0.01f)
            {
                glow = target;
                TailLights.Apply(tails, glow, mpb);
            }
            float head = level + flash * FlashGlow;
            if (Mathf.Abs(head - headGlow) > 0.01f)
            {
                headGlow = head;
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

        /// <summary>
        /// Halos de l'appel de phares : face à la caméra, un peu en avant du phare (la carrosserie ne les coupe pas),
        /// visibles seulement de face, et agrandis avec la distance pour se voir de loin en plein jour.
        /// </summary>
        void UpdateFlares()
        {
            var cam = flash > 0.02f ? Camera.main : null;
            for (int i = 0; i < flares.Length; i++)
            {
                var flare = flares[i];
                if (!flare || !beams[i]) continue;
                float size = 0f;
                if (cam)
                {
                    var lamp = beams[i].transform;
                    Vector3 to = cam.transform.position - lamp.position;
                    float d = to.magnitude;
                    if (d > 0.5f && d < FlareRange)
                    {
                        float facing = Mathf.Clamp01(Vector3.Dot(lamp.forward, to / d) * 3f);
                        size = (FlareSize + d * FlareGrowth) * beamScale * flash * facing;
                        flare.transform.SetPositionAndRotation(lamp.position + to / d * 0.35f, cam.transform.rotation);
                        flare.transform.localScale = Vector3.one * size;
                    }
                }
                bool show = size > 0.02f;
                if (flare.enabled != show) flare.enabled = show;
            }
        }
    }
}
