using System.IO;
using System.Text;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Second exemplaire automatique pour les essais du jeu en réseau (« Chill Road.exe -nettest », avec « -host »,
    /// « -join CODE » ou « -join adresse ») : un vrai joueur sans personne au clavier, là où le faux joueur de
    /// Tools/Net/netbot.py ne suffit pas (relais, affichage des autres voitures). Toutes les deux secondes, il écrit
    /// dans le journal ce qu'il voit de la partie (lignes « [NetTest] ») : lien, latence et débit par le relais,
    /// voitures des autres joueurs, séance de course. Il accepte les invitations de course, roule tout droit
    /// quelques secondes après le départ, puis abandonne. Quand il voit pour la première fois la voiture d'un autre
    /// joueur près de lui, il en garde une capture d'écran à côté du journal.
    /// - « -nettest 90 » : il quitte le jeu au bout de 90 secondes ;
    /// - « -nettest-race 20 » : il lance la première course de la carte 20 secondes après l'arrivée d'un autre joueur.
    /// </summary>
    public class NetTestBot : MonoBehaviour
    {
        const float Period = 2f, DriveTime = 8f;

        GameManager game;
        float quitAfter, raceAfter, timer, driven, together, shotTimer = -1f;
        long lastSent, lastReceived;
        bool wasRunning, driving, wasActive, shot;
        readonly StringBuilder line = new StringBuilder();

        /// <summary>
        /// <paramref name="seconds"/> : durée de l'essai avant de quitter le jeu (0 : sans limite).
        /// <paramref name="race"/> : délai avant de lancer une course, une fois un autre joueur présent (0 : jamais).
        /// </summary>
        public void Init(GameManager gm, float seconds, float race)
        {
            game = gm;
            quitAfter = seconds;
            raceAfter = race;
            Application.runInBackground = true;
            Debug.Log("[NetTest] exemplaire d'essai" + (seconds > 0f ? $", {seconds:0} s" : "") + (race > 0f ? $", course après {race:0} s" : ""));
        }

        static string Clock => System.DateTime.Now.ToString("HH:mm:ss.fff");

        void Update()
        {
            if (!game || game.State == GameManager.Phase.Loading) return;
            var net = game.Net;
            var races = game.Races;
            if (net.Active != wasActive)
            {
                wasActive = net.Active;
                Debug.Log($"[NetTest] {Clock} " + (wasActive ? $"en partie : joueur {net.LocalId}, {(net.IsHost ? "hôte" : net.ViaRelay ? "par le relais" : "par adresse")}" : "hors partie"));
            }
            bool playing = net.Active && game.State == GameManager.Phase.Playing;
            Watch(net, playing);
            // Course à plusieurs : lancée à la demande ; invitation acceptée ; quelques secondes de ligne droite, puis abandon
            if (races)
            {
                together = playing && net.Remotes.Count > 0 ? together + Time.unscaledDeltaTime : 0f;
                if (raceAfter > 0f && together > raceAfter && !net.Race.Exists && !races.Running && races.All.Count > 0)
                {
                    raceAfter = 0f;
                    Debug.Log($"[NetTest] {Clock} course lancée : {races.All[0].id}");
                    races.Launch(races.All[0]);
                }
                if (races.InvitePending)
                {
                    Debug.Log($"[NetTest] {Clock} invitation de course acceptée");
                    races.SendMessage("Answer", true);
                }
                if (races.Running != wasRunning)
                {
                    wasRunning = races.Running;
                    Debug.Log($"[NetTest] {Clock} " + (wasRunning ? "départ de la course" : "course finie pour nous") + $", voiture en {game.Player.transform.position.ToString("F1")}");
                    if (!wasRunning) Release();
                    driving = wasRunning && net.Active;
                    driven = 0f;
                }
                if (driving)
                {
                    driven += Time.deltaTime;
                    var car = game.Player;
                    car.InputEnabled = false;
                    car.Autopilot = true;
                    car.AutoThrottle = 0.5f;
                    car.AutoBrake = car.AutoSteer = 0f;
                    if (driven > DriveTime)
                    {
                        Release();
                        Debug.Log($"[NetTest] {Clock} abandon en {car.transform.position.ToString("F1")}");
                        races.SendMessage("Abandon");
                    }
                }
            }
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f)
            {
                timer = Period;
                Report(net);
            }
            if (quitAfter > 0f && Time.realtimeSinceStartup > quitAfter)
            {
                Debug.Log($"[NetTest] {Clock} fin de l'essai");
                quitAfter = 0f;
                game.Quit();
            }
        }

        /// <summary>Première voiture d'un autre joueur vue près de la nôtre : capture d'écran, trois secondes plus tard.</summary>
        void Watch(NetGame net, bool playing)
        {
            if (shot || !playing) return;
            if (shotTimer < 0f)
            {
                foreach (var r in net.Remotes)
                    if (r.Ready && Vector3.Distance(r.transform.position, game.Player.transform.position) < 80f) shotTimer = 3f;
                return;
            }
            shotTimer -= Time.unscaledDeltaTime;
            if (shotTimer > 0f) return;
            shot = true;
            string log = Application.consoleLogPath;
            string file = string.IsNullOrEmpty(log) ? Path.Combine(Application.persistentDataPath, "nettest.png") : Path.ChangeExtension(log, ".png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log($"[NetTest] {Clock} capture d'écran : {file}");
        }

        /// <summary>Rend la voiture au joueur.</summary>
        void Release()
        {
            if (!driving) return;
            driving = false;
            var car = game.Player;
            car.Autopilot = false;
            car.AutoThrottle = 0f;
            car.InputEnabled = true;
        }

        void Report(NetGame net)
        {
            line.Clear();
            Vector3 here = game.Player.transform.position;
            line.Append("[NetTest] ").Append(Clock).Append(' ').Append(game.State).Append(' ').Append(net.State);
            if (net.Connecting) line.Append(net.FindingCode ? " (recherche du code)" : " (demande d'entrée)");
            if (net.IsHost) line.Append(" code=").Append(net.InviteCode ?? net.InviteError ?? net.InviteState.ToString());
            line.Append(" joueurs=").Append(net.PlayerCount).Append(" moi=").Append(here.ToString("F1"));
            var relay = net.Relay;
            if (relay != null && relay.State == NetRelay.Step.Ready)
            {
                long sent = relay.SentBytes, received = relay.ReceivedBytes;
                line.Append($" relais : ping={net.RelayPing} ms, {relay.MaxPayload} o/paquet, envoi {(sent - lastSent) / Period / 1024f:0.0} ko/s, réception {(received - lastReceived) / Period / 1024f:0.0} ko/s");
                lastSent = sent;
                lastReceived = received;
            }
            foreach (var r in net.Remotes)
                line.Append($" | joueur {r.Id} « {r.Name} » {(r.Ready ? r.transform.position.ToString("F1") + $" à {Vector3.Distance(here, r.transform.position):0.0} m" : "sans état")}");
            line.Append(" | trafic=").Append(game.Traffic.Cars.Count);
            var race = net.Race;
            if (race != null && race.Exists)
            {
                line.Append(" | course ").Append(race.Phase).Append(" :");
                foreach (var e in race.Entries) line.Append(' ').Append(e.id).Append('=').Append(e.status);
            }
            Debug.Log(line.ToString());
        }
    }
}
