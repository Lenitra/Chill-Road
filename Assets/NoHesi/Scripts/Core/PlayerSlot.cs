namespace NoHesi
{
    /// <summary>
    /// Le joueur de cette machine : sa voiture, sa caméra, son HUD, son son, son score, et l'état de son bouton de
    /// replacement. (Les voitures des autres joueurs d'une partie en réseau sont des <see cref="RemoteCar"/>.)
    /// </summary>
    public class PlayerSlot
    {
        public VehicleController Car;
        public CarLights Lights;
        public ScoreSystem Score;
        public CameraRig Rig;
        public Hud Hud;
        public GameAudio Audio;
        /// <summary>Voiture choisie dans le garage.</summary>
        public int CarIndex;

        /// <summary>Voiture retournée depuis… (s) : au-delà de quelques secondes, elle est replacée sur la route.</summary>
        public float UpsideDown;
        // Bouton de replacement : appui court = replacer, maintenu = voiture suivante
        public float ResetHeld;
        public bool ResetConsumed;
        /// <summary>Progression de l'appui long sur le bouton de replacement (0-1), pour le HUD.</summary>
        public float ResetHold;
    }
}
