using System.Collections;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Petit panneau de bord de route (un seul poteau) : n'arrête jamais la voiture. Percuté (au-delà d'une simple
    /// touchette, <see cref="MinSpeed"/>), il casse et part en débris physique ; la voiture est seulement ralentie
    /// (<see cref="SpeedAfter"/>, appliqué par VehicleController.OnCollisionEnter). Le panneau revient à sa place
    /// quand le joueur est loin et l'a laissé un moment.
    /// </summary>
    public class BreakableProp : MonoBehaviour
    {
        /// <summary>Vitesse (m/s) sous laquelle la voiture ne fait que s'appuyer contre le poteau : 5 km/h.</summary>
        public const float MinSpeed = 5f / 3.6f;
        /// <summary>Ralentissement de la voiture qui fauche un panneau : 15 % de sa vitesse plus 1 m/s (3,6 km/h).</summary>
        const float Kept = 0.85f, Loss = 1f;

        /// <summary>Vitesse de la voiture juste après avoir fauché un panneau, lancée à <paramref name="v"/>.</summary>
        public static Vector3 SpeedAfter(Vector3 v)
        {
            float s = v.magnitude;
            return s > 0.01f ? v * (Mathf.Max(0f, s * Kept - Loss) / s) : v;
        }

        const float Mass = 18f;
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
            (item.category == "sign" || item.category == "ski") && item.collider == "capsule" && item.size.x <= 1.4f;

        public void Init(Collider collider, Vector3 itemSize)
        {
            standing = collider;
            size = itemSize;
            homePos = transform.position;
            homeRot = transform.rotation;
            homeLayer = gameObject.layer;
        }

        /// <summary>Casse le panneau heurté par une voiture lancée à <paramref name="carVelocity"/>.</summary>
        public void Break(Vector3 carVelocity, Collider[] carColliders)
        {
            if (Broken) return;
            Broken = true;
            // Plus d'obstacle tout de suite ; le débris naît à l'image suivante : un corps créé pendant les rappels de
            // collision (ou juste après, WaitForFixedUpdate) ne prend pas la vitesse qu'on lui donne (le panneau
            // basculait sur place)
            standing.enabled = false;
            StartCoroutine(Life(carVelocity, carColliders));
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
            float away = 0f;
            while (true)
            {
                yield return new WaitForSeconds(1f);
                var cam = Camera.main;
                bool far = !cam || ((cam.transform.position - transform.position).sqrMagnitude > RestoreDistance * RestoreDistance
                                    && (cam.transform.position - homePos).sqrMagnitude > RestoreDistance * RestoreDistance);
                away = far ? away + 1f : 0f;
                if (away >= RestoreDelay) break;
            }
            Restore();
        }

        void Restore()
        {
            Destroy(body);
            body = null;
            debris.enabled = false;
            gameObject.layer = homeLayer;
            transform.SetPositionAndRotation(homePos, homeRot);
            standing.enabled = true;
            Broken = false;
        }
    }
}
