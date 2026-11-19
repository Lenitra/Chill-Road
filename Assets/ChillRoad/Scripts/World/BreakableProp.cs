using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Petit panneau de bord de route (un seul poteau) : n'arrête jamais la voiture. Percuté (au-delà d'une simple
    /// touchette, <see cref="MinSpeed"/>), il casse et part en débris physique ; la voiture est seulement ralentie
    /// (<see cref="SpeedAfter"/>, appliqué par VehicleController.OnCollisionEnter). Le panneau revient à sa place
    /// quand le joueur est loin et l'a laissé un moment.
    /// Les piles et murs de pneus du terrain d'essai (<see cref="UseTires"/>) cassent aussi, quand on fonce dedans
    /// (<see cref="TireBreakSpeed"/>) : le modèle disparaît, chaque pneu devient un débris physique, ceux près du choc
    /// sont projetés, les autres restent empilés et se poussent.
    /// Une fois retombés (<see cref="Linger"/>), les débris perdent leur collision et s'effacent en fondu tramé
    /// (propriété _Fade du shader Toon), puis disparaissent jusqu'au retour de l'objet.
    /// </summary>
    public class BreakableProp : MonoBehaviour
    {
        /// <summary>Vitesse (m/s) sous laquelle la voiture ne fait que s'appuyer contre le poteau : 5 km/h.</summary>
        public const float MinSpeed = 5f / 3.6f;
        /// <summary>Ralentissement de la voiture qui fauche un panneau : 15 % de sa vitesse plus 1 m/s (3,6 km/h).</summary>
        const float Kept = 0.85f, Loss = 1f;

        /// <summary>Pneus : il faut foncer dedans (15 km/h) ; plus doucement, la pile ou le mur reste un obstacle.</summary>
        public const float TireBreakSpeed = 15f / 3.6f;

        /// <summary>Vitesse de la voiture juste après avoir fauché un panneau, lancée à <paramref name="v"/>.</summary>
        public static Vector3 SpeedAfter(Vector3 v) => Slow(v, Kept, Loss);

        static Vector3 Slow(Vector3 v, float kept, float loss)
        {
            float s = v.magnitude;
            return s > 0.01f ? v * (Mathf.Max(0f, s * kept - loss) / s) : v;
        }

        /// <summary>Vitesse au-delà de laquelle la voiture casse cet objet.</summary>
        public float BreakSpeed => tires != null ? TireBreakSpeed : MinSpeed;

        /// <summary>
        /// Vitesse de la voiture après avoir cassé cet objet : un mur de pneus freine bien plus qu'un panneau
        /// (30 % de la vitesse plus 1,5 m/s), une pile seule un peu (12 % plus 0,5 m/s).
        /// </summary>
        public Vector3 SpeedAfterBreak(Vector3 v) =>
            tires == null ? SpeedAfter(v) : tires.Count > 3 ? Slow(v, 0.7f, 1.5f) : Slow(v, 0.88f, 0.5f);

        /// <summary>Un pneu du modèle : pied (repère local), rayon, hauteur, rouge ou blanc.</summary>
        struct Tire
        {
            public Vector3 pos;
            public float r, h;
            public bool red;
        }

        const float TireMass = 9f, TireR = 0.35f, TireH = 0.24f;
        List<Tire> tires;
        GameObject redTire, whiteTire;
        readonly List<GameObject> hidden = new List<GameObject>();
        GameObject pile;
        static Mesh hull;

        const float Mass = 18f;
        /// <summary>Temps laissé aux débris pour voler et retomber, puis durée du fondu.</summary>
        const float Linger = 3f, FadeTime = 0.8f;
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static MaterialPropertyBlock fadeBlock;
        const float RestoreDelay = 90f, RestoreDistance = 200f;

        public bool Broken { get; private set; }

        Collider standing;
        BoxCollider debris;
        Rigidbody body;
        Vector3 homePos;
        Quaternion homeRot;
        Vector3 size;
        int homeLayer;

        /// <summary>Panneaux concernés : catégorie signalisation ou piste, collision capsule, un seul poteau (étroit).</summary>
        public static bool Fits(AlpPack.Item item) =>
            (item.category == "sign" || item.category == "ski") && item.collider == "capsule" && item.size.x <= 1.4f
            || IsTires(item);

        /// <summary>Pile de pneus et mur de pneus du terrain d'essai (Tools/Blender/alp_school.py).</summary>
        public static bool IsTires(AlpPack.Item item) => item.id == "school_tires" || item.id == "school_tirewall";

        /// <summary>
        /// Fait de cet objet une pile (school_tires) ou un mur (school_tirewall) de pneus : la disposition reprend
        /// celle des modèles d'alp_school.py (repère Blender x, y, z → Unity -x, z, -y).
        /// </summary>
        public void UseTires(string id, GameObject red, GameObject white)
        {
            redTire = red;
            whiteTire = white;
            tires = new List<Tire>();
            if (id == "school_tires")
            {
                for (int k = 0; k < 3; k++)
                    tires.Add(new Tire { pos = new Vector3(0f, 0.26f * k, 0f), r = 0.38f, h = 0.25f, red = k != 1 });
                return;
            }
            foreach (float y in new[] { -0.33f, 0.33f })
                for (int i = 0; i < 6; i++)
                    for (int z = 0; z < 3; z++)
                        tires.Add(new Tire { pos = new Vector3(-(-1.67f + i * 0.67f), z * 0.25f, -y), r = 0.33f, h = 0.24f, red = (i + z) % 2 == 0 });
        }

        public void Init(Collider collider, Vector3 itemSize)
        {
            standing = collider;
            size = itemSize;
            homePos = transform.position;
            homeRot = transform.rotation;
            homeLayer = gameObject.layer;
        }

        /// <summary>Casse l'objet heurté en <paramref name="at"/> par une voiture lancée à <paramref name="carVelocity"/>.</summary>
        public void Break(Vector3 carVelocity, Collider[] carColliders, Vector3 at)
        {
            if (Broken) return;
            Broken = true;
            // Plus d'obstacle tout de suite ; le débris naît à l'image suivante : un corps créé pendant les rappels de
            // collision (ou juste après, WaitForFixedUpdate) ne prend pas la vitesse qu'on lui donne (le panneau
            // basculait sur place)
            standing.enabled = false;
            StartCoroutine(tires != null ? TireLife(carVelocity, carColliders, at) : Life(carVelocity, carColliders));
        }

        /// <summary>Enveloppe de collision d'un pneu (cylindre à 10 côtés, pied à l'origine), partagée.</summary>
        static Mesh Hull()
        {
            if (hull) return hull;
            const int n = 10;
            var v = new Vector3[n * 2];
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                v[k] = new Vector3(Mathf.Cos(a) * TireR, 0f, Mathf.Sin(a) * TireR);
                v[k + n] = v[k] + Vector3.up * TireH;
            }
            var t = new List<int>();
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                t.AddRange(new[] { k, k + n, k1, k1, k + n, k1 + n });
            }
            for (int k = 1; k + 1 < n; k++) t.AddRange(new[] { 0, k + 1, k, n, n + k, n + k + 1 });
            hull = new Mesh { name = "TireHull", vertices = v, triangles = t.ToArray() };
            return hull;
        }

        IEnumerator TireLife(Vector3 carVelocity, Collider[] carColliders, Vector3 at)
        {
            yield return null;
            // Le modèle d'un bloc s'efface, remplacé par ses pneus un par un, à la même place
            hidden.Clear();
            foreach (Transform child in transform)
                if (child.gameObject.activeSelf)
                {
                    hidden.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                }
            pile = new GameObject("Tires");
            pile.transform.SetParent(transform, false);
            float speed = carVelocity.magnitude, reach = tires.Count > 3 ? 1.9f : 3f;
            var flung = new List<Collider>();
            foreach (var t in tires)
            {
                var prefab = t.red ? redTire : whiteTire;
                if (!prefab) continue;
                var go = Instantiate(prefab, transform.TransformPoint(t.pos), transform.rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), pile.transform);
                go.transform.localScale = new Vector3(t.r / TireR, t.h / TireH, t.r / TireR);
                // Couche « Ignore Raycast » : les rayons des roues passent au travers (sinon la voiture décolle dessus)
                go.layer = 2;
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = Hull();
                col.convex = true;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = TireMass;
                rb.linearDamping = 0.1f;
                rb.angularDamping = 0.3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                Vector3 away = go.transform.position - at;
                away.y = 0f;
                float f = Mathf.Clamp01(1f - away.magnitude / reach);
                if (f <= 0f) continue;
                // Près du choc : projeté devant la voiture, en gerbe, en tournoyant
                rb.linearVelocity = (carVelocity * Random.Range(0.5f, 0.95f) + away.normalized * (speed * 0.15f)
                                     + Vector3.up * Mathf.Clamp(0.4f + speed * 0.1f, 0.4f, 4f) + Random.insideUnitSphere * 1.5f) * f;
                rb.angularVelocity = Random.insideUnitSphere * (2f + speed * 0.4f) * f;
                foreach (var c in carColliders) if (c) Physics.IgnoreCollision(col, c, true);
                flung.Add(col);
            }
            // Les pneus projetés ne recognent pas la voiture tout de suite (pare-brise) ; ensuite, elle les pousse
            yield return new WaitForSeconds(0.6f);
            foreach (var col in flung)
                if (col)
                    foreach (var c in carColliders)
                        if (c) Physics.IgnoreCollision(col, c, false);
            yield return new WaitForSeconds(Linger - 0.6f);
            yield return Vanish(pile.GetComponentsInChildren<Renderer>(), pile.GetComponentsInChildren<Collider>());
            Destroy(pile);
            pile = null;
            yield return WaitUntilAway();
            foreach (var go in hidden) if (go) go.SetActive(true);
            hidden.Clear();
            standing.enabled = true;
            Broken = false;
        }

        /// <summary>
        /// Débris qui s'en vont : plus de collision ni de physique (ils restent figés là où ils sont tombés), fondu tramé
        /// en <see cref="FadeTime"/>, puis rendus invisibles.
        /// </summary>
        IEnumerator Vanish(Renderer[] renderers, Collider[] colliders)
        {
            foreach (var c in colliders) if (c) c.enabled = false;
            foreach (var rb in GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            fadeBlock ??= new MaterialPropertyBlock();
            for (float t = 0f; t < FadeTime; t += Time.deltaTime)
            {
                SetFade(renderers, t / FadeTime);
                yield return null;
            }
            foreach (var r in renderers) if (r) r.enabled = false;
            body = null;
        }

        static void SetFade(Renderer[] renderers, float fade)
        {
            foreach (var r in renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(fadeBlock);
                fadeBlock.SetFloat(FadeId, fade);
                r.SetPropertyBlock(fadeBlock);
            }
        }

        /// <summary>Attend que la caméra soit loin de l'objet (et de sa place) depuis <see cref="RestoreDelay"/> secondes.</summary>
        IEnumerator WaitUntilAway()
        {
            float away = 0f;
            while (away < RestoreDelay)
            {
                yield return new WaitForSeconds(1f);
                var cam = Camera.main;
                bool far = !cam || ((cam.transform.position - transform.position).sqrMagnitude > RestoreDistance * RestoreDistance
                                    && (cam.transform.position - homePos).sqrMagnitude > RestoreDistance * RestoreDistance);
                away = far ? away + 1f : 0f;
            }
        }

        IEnumerator Life(Vector3 carVelocity, Collider[] carColliders)
        {
            yield return null;
            // Débris : boîte du panneau entier (le poteau seul est trop fin pour reposer à plat avec son panneau)
            if (!debris)
            {
                debris = gameObject.AddComponent<BoxCollider>();
                debris.size = new Vector3(Mathf.Max(size.x, 0.12f), size.y, Mathf.Max(size.z, 0.12f));
                debris.center = new Vector3(0f, size.y * 0.5f + 0.05f, 0f);
            }
            debris.enabled = true;
            // Couche « Ignore Raycast », comme les voitures : les rayons de suspension ne le prennent pas pour le sol
            // (la voiture qui passait dessus décollait) et la caméra ne bute pas dessus
            gameObject.layer = 2;
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = Mass;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Balayage continu : le mode spéculatif, avec la rotation rapide près du sol, créait des contacts
            // fantômes qui arrêtaient net le débris au pied du poteau
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Sorti du sol avant de bouger : le pied du poteau est posé au ras du terrain
            body.position = transform.position + Vector3.up * 0.1f;
            // Après la création du corps (qui refait l'objet physique et oublie les paires ignorées)
            foreach (var c in carColliders) if (c) Physics.IgnoreCollision(debris, c, true);

            float speed = carVelocity.magnitude;
            Vector3 flat = Vector3.ProjectOnPlane(carVelocity, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = transform.forward;
            // Fauché au pied : le pied part devant à peu près à la vitesse de la voiture, le haut (le panneau) bascule
            // en arrière vers le capot. Vitesse donnée au centre du panneau, rotation autour de l'axe
            // Cross(up, avant) : avec l'autre sens le pied restait cloué sur place
            body.linearVelocity = carVelocity * Random.Range(0.6f, 0.8f) + Vector3.up * Mathf.Clamp(0.3f + speed * 0.15f, 0.3f, 5f)
                                  + Random.insideUnitSphere * 1.2f;
            body.angularVelocity = Vector3.Cross(Vector3.up, flat) * Mathf.Clamp(speed * 0.22f, 1f, 10f) + Random.insideUnitSphere * 2f;

            // Plus de collision avec la voiture le temps qu'il passe par-dessus (sinon second choc contre le pare-brise)
            yield return new WaitForSeconds(0.8f);
            foreach (var c in carColliders) if (c && debris) Physics.IgnoreCollision(debris, c, false);
            yield return new WaitForSeconds(Linger - 0.8f);
            var shown = GetComponentsInChildren<Renderer>();
            yield return Vanish(shown, new Collider[] { debris });
            yield return WaitUntilAway();
            foreach (var r in shown)
                if (r)
                {
                    r.SetPropertyBlock(null);
                    r.enabled = true;
                }
            Restore();
        }

        void Restore()
        {
            if (body) Destroy(body);
            body = null;
            debris.enabled = false;
            gameObject.layer = homeLayer;
            transform.SetPositionAndRotation(homePos, homeRot);
            standing.enabled = true;
            Broken = false;
        }
    }
}
