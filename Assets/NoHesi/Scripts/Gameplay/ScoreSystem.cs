using System;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Suivi de la balade : distance parcourue. Le score façon No Hesi (frôlements, multiplicateur, drifts) est en
    /// sommeil sur la carte ; ses événements et ses compteurs restent là pour le HUD et le son, prêts à être
    /// rebranchés quand le jeu retrouvera des épreuves.
    /// </summary>
    public class ScoreSystem : MonoBehaviour
    {
        public const string BestKey = "nohesi_best";

        public float comboWindow = 6f;

        public float RunScore { get; private set; }
        public float Multiplier { get; private set; } = 1f;
        public float ComboTimer { get; private set; }
        public bool Oncoming { get; private set; }
        public float Best { get; private set; }
        /// <summary>Distance parcourue depuis le départ (m).</summary>
        public float RunDistance { get; private set; }
        public bool Recording { get; set; } = true;

        public bool Drifting { get; private set; }
        public float DriftPoints { get; private set; }
        public float DriftChain { get; private set; } = 1f;
        public float DriftAngle { get; private set; }

#pragma warning disable 67   // événements en sommeil (voir le résumé de la classe)
        public event Action<float, float, bool> NearMiss;   // points, écart, contresens
        public event Action<float, string> Lost;            // score perdu, raison
        public event Action<float, string> Bonus;           // points, libellé
        public event Action<Vector3> NearMissAt;            // point entre la voiture et le véhicule frôlé
        public event Action<float> DriftScored;             // points encaissés
        public event Action<float> DriftLost;               // points perdus
#pragma warning restore 67

        VehicleController player;

        public void Init(VehicleController car)
        {
            player = car;
        }

        public void Tick(float dt)
        {
            if (player) RunDistance += player.Speed * dt;
        }

        public void Protect(float seconds) { }
        public void SaveBest() { }
        public void ClearBest() => Best = 0f;
    }
}
