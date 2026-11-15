using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// État des commandes tactiles (écran de téléphone / tablette), rempli par <see cref="TouchControls"/> et lu par
    /// <see cref="GameInput"/> comme un appareil de plus. Les appuis ponctuels (vue, phares…) sont publiés
    /// une image après le toucher, au début de l'image suivante : chaque lecteur les voit exactement une fois.
    /// </summary>
    public static class TouchInput
    {
        /// <summary>Commandes tactiles affichées (partie solo en cours, écran touché en dernier).</summary>
        public static bool Showing { get; internal set; }

        /// <summary>L'écran tactile est l'appareil utilisé en dernier (aide et contrôles de la pause).</summary>
        public static bool InUse { get; internal set; }

        public static float Steer { get; internal set; }
        public static float Throttle { get; internal set; }
        public static float Brake { get; internal set; }
        public static bool Handbrake { get; internal set; }
        public static bool ResetHeld { get; internal set; }
        public static bool LookBack { get; internal set; }

        /// <summary>Regard autour : un doigt glissé sur l'écran libre, comme la souris avec le clic droit.</summary>
        public static bool LookHeld { get; internal set; }
        public static Vector2 LookDelta { get; internal set; }

        public static bool CameraPressed { get; private set; }
        public static bool LightsPressed { get; private set; }
        public static bool NextTrackPressed { get; private set; }
        public static bool PausePressed { get; private set; }

        static bool pendingCamera, pendingLights, pendingTrack, pendingPause;
        static Vector2 pendingLook;

        internal static void QueueCamera() => pendingCamera = true;
        internal static void QueueLights() => pendingLights = true;
        internal static void QueueNextTrack() => pendingTrack = true;
        internal static void QueuePause() => pendingPause = true;
        internal static void AddLook(Vector2 delta) => pendingLook += delta;

        /// <summary>Début d'image : publie ce qui a été touché à l'image précédente.</summary>
        internal static void Publish()
        {
            CameraPressed = pendingCamera;
            LightsPressed = pendingLights;
            NextTrackPressed = pendingTrack;
            PausePressed = pendingPause;
            LookDelta = pendingLook;
            pendingCamera = pendingLights = pendingTrack = pendingPause = false;
            pendingLook = Vector2.zero;
        }

        /// <summary>Tout relâcher (commandes masquées, pause).</summary>
        internal static void Release()
        {
            Steer = Throttle = Brake = 0f;
            Handbrake = ResetHeld = LookBack = LookHeld = false;
            pendingCamera = pendingLights = pendingTrack = pendingPause = false;
            pendingLook = Vector2.zero;
            Publish();
        }
    }
}
