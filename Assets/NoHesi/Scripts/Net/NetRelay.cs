using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Second lien réseau d'une partie, à côté de la prise UDP (<see cref="NetSocket"/>) : le relais de Unity (service
    /// Relay), qui fait passer les paquets par un serveur d'Internet. Aucun port n'est à ouvrir sur la box : l'hôte
    /// obtient un code d'invitation court, et qui le tape le rejoint.
    /// - Le protocole du jeu ne change pas : les mêmes paquets voyagent, non fiables et sans ordre garanti, chiffrés
    ///   (DTLS). Seul le transport diffère (Unity Transport, mis à jour à chaque image depuis le fil principal).
    /// - Chaque connexion du relais reçoit une adresse synthétique stable (<see cref="Address"/>) : <see cref="NetGame"/>
    ///   s'en sert comme d'une adresse IP ordinaire, sans savoir par où passent les paquets.
    /// - Les appels aux services (identification, réservation du relais, code) sont asynchrones : ils sont suivis image
    ///   après image, sans jamais bloquer le jeu. Une panne donne un message (<see cref="Error"/>) et rien d'autre.
    /// - Identification anonyme seulement : rien de personnel n'est envoyé (ni pseudo, ni adresse e-mail), et
    ///   l'identité, tirée au hasard par le service, est renouvelée à chaque lancement du jeu.
    /// </summary>
    public sealed class NetRelay : IDisposable
    {
        public enum Step
        {
            Off,
            /// <summary>Services, réservation ou connexion en cours.</summary>
            Pending,
            /// <summary>Hôte : code obtenu, le relais écoute. Joueur : relié à l'hôte.</summary>
            Ready,
            /// <summary>Panne (voir <see cref="Error"/>) : le lien est fermé.</summary>
            Failed,
        }

        /// <summary>
        /// Adresse synthétique d'une connexion du relais, prise dans une plage IPv4 réservée (240.0.0.0/4) qui ne
        /// désigne aucune machine : elle ne peut pas se confondre avec l'adresse d'un joueur du réseau local.
        /// </summary>
        public sealed class Address : IPEndPoint
        {
            public readonly int Serial;

            public Address(int serial)
                : base(new IPAddress(new[] { (byte)240, (byte)(serial >> 16), (byte)(serial >> 8), (byte)serial }), 1)
            {
                Serial = serial;
            }

            public override string ToString() => "relais#" + Serial;
        }

        /// <summary>Lettres et chiffres dont le service compose ses codes (6 à 12 caractères).</summary>
        const string CodeLetters = "6789BCDFGHJKLMNPQRTW";
        public const int CodeMin = 6, CodeMax = 12;
        /// <summary>Paquets chiffrés entre chaque machine et le relais.</summary>
        const string Protocol = "dtls";
        /// <summary>
        /// Places réservées au relais en plus de celles de la partie : un joueur de trop atteint ainsi le serveur du
        /// jeu, qui lui répond « partie complète », au lieu d'être refusé sans explication par le relais.
        /// </summary>
        const int SpareSlots = 4;
        /// <summary>Délai (s) laissé aux services pour répondre.</summary>
        const float ServiceTimeout = 20f;
        const float PingPeriod = 2f;
        /// <summary>Messages de mesure du lien (hors protocole du jeu, qui commence par « CR ») : « CP », 0 aller ou 1 retour, date.</summary>
        const int PingSize = 7;

        public Step State { get; private set; }
        /// <summary>Ce lien est celui d'un hôte (il écoute) et non celui d'un joueur qui rejoint.</summary>
        public bool Hosting { get; private set; }
        /// <summary>Hôte : code d'invitation à donner aux autres joueurs (nul tant qu'il n'est pas obtenu).</summary>
        public string Code { get; private set; }
        /// <summary>Motif de la panne, à afficher tel quel.</summary>
        public string Error { get; private set; }
        /// <summary>Joueur : adresse de l'hôte sur ce lien (nulle tant qu'il n'est pas relié).</summary>
        public IPEndPoint Server => !Hosting && State == Step.Ready ? serverAddress : null;
        /// <summary>
        /// Joueur : aller-retour jusqu'à l'hôte par le relais (ms), -1 tant qu'il n'est pas mesuré. La mesure comprend
        /// l'attente de l'image suivante chez l'hôte et chez soi.
        /// </summary>
        public int PingMs { get; private set; } = -1;
        /// <summary>Charge utile maximale d'un paquet sur ce lien (octets), 0 tant qu'elle n'est pas connue.</summary>
        public int MaxPayload { get; private set; }
        /// <summary>Octets envoyés et reçus sur ce lien depuis son ouverture (en-têtes compris).</summary>
        public long SentBytes => driver.IsCreated ? (long)driver.GetStatistics().TxTotalBytes : 0;
        public long ReceivedBytes => driver.IsCreated ? (long)driver.GetStatistics().RxTotalBytes : 0;
        public byte[] Buffer => buffer;

        NetworkDriver driver;
        readonly byte[] buffer = new byte[2048];
        readonly byte[] pingBytes = new byte[PingSize];
        readonly Dictionary<IPEndPoint, NetworkConnection> byAddress = new Dictionary<IPEndPoint, NetworkConnection>();
        readonly Dictionary<NetworkConnection, Address> byConnection = new Dictionary<NetworkConnection, Address>();
        readonly Address serverAddress = new Address(1);
        NetworkConnection hostConnection;
        int nextSerial = 2;
        Task<(Allocation allocation, string code)> hosting;
        Task<JoinAllocation> joining;
        float deadline, pingTimer;
        bool oversizeLogged;

        /// <summary>Identification en cours ou faite, commune à tous les liens de ce lancement du jeu.</summary>
        static Task signIn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => signIn = null;

        // ------------------------------------------------------------------ codes

        /// <summary>Code tel qu'on le tape, remis en forme : majuscules, sans espaces ni tirets.</summary>
        public static string CleanCode(string text)
        {
            var sb = new System.Text.StringBuilder();
            if (text != null)
                foreach (char c in text)
                    if (!char.IsWhiteSpace(c) && c != '-') sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        /// <summary>Ce texte a la forme d'un code d'invitation (et non celle d'une adresse).</summary>
        public static bool IsCode(string text)
        {
            string code = CleanCode(text);
            if (code.Length < CodeMin || code.Length > CodeMax) return false;
            foreach (char c in code)
                if (CodeLetters.IndexOf(c) < 0) return false;
            return true;
        }

        // ------------------------------------------------------------------ services

        /// <summary>
        /// Connexion aux services de Unity, sous une identité anonyme. Celle d'un lancement précédent n'est pas reprise :
        /// rien ne suit le joueur d'une fois sur l'autre, et deux exemplaires du jeu sur la même machine (un serveur
        /// dédié et un joueur, par exemple) n'ont jamais la même.
        /// </summary>
        static Task SignIn()
        {
            if (signIn == null || signIn.IsCompleted) signIn = SignInOnce();
            return signIn;
        }

        static async Task SignInOnce()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn) return;
            if (auth.SessionTokenExists) auth.ClearSessionToken();
            await auth.SignInAnonymouslyAsync();
        }

        static async Task<(Allocation, string)> Allocate(int connections)
        {
            await SignIn();
            var allocation = await RelayService.Instance.CreateAllocationAsync(connections);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return (allocation, code);
        }

        static async Task<JoinAllocation> Find(string code)
        {
            await SignIn();
            return await RelayService.Instance.JoinAllocationAsync(code);
        }

        /// <summary>Hôte : réserve un relais pour ce nombre de joueurs et demande son code d'invitation.</summary>
        public void Host(int players)
        {
            Close();
            Hosting = true;
            State = Step.Pending;
            deadline = Time.unscaledTime + ServiceTimeout;
            hosting = Guard(() => Allocate(Mathf.Clamp(players + SpareSlots, 1, 100)));
        }

        /// <summary>Joueur : cherche la partie de ce code et s'y relie.</summary>
        public void Join(string code)
        {
            Close();
            Hosting = false;
            State = Step.Pending;
            deadline = Time.unscaledTime + ServiceTimeout;
            joining = Guard(() => Find(CleanCode(code)));
        }

        /// <summary>Un appel qui échoue avant même d'attendre (services absents du build, par exemple) donne une tâche en échec.</summary>
        static Task<T> Guard<T>(Func<Task<T>> call)
        {
            try { return call(); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }

        /// <summary>Panne expliquée au joueur, d'après l'erreur rendue par les services.</summary>
        static string Describe(Exception e)
        {
            if (e is AggregateException many) e = many.Flatten().InnerException ?? e;
            Debug.LogWarning("[Net] Relais : " + e.GetType().Name + " : " + e.Message);
            if (e is RelayServiceException relay)
            {
                switch (relay.Reason)
                {
                    case RelayExceptionReason.JoinCodeNotFound:
                    case RelayExceptionReason.AllocationNotFound:
                    case RelayExceptionReason.EntityNotFound:
                    case RelayExceptionReason.InvalidRequest:
                    case RelayExceptionReason.InvalidArgument:
                    case RelayExceptionReason.Gone:
                        return Loc.T("Code d'invitation inconnu ou expiré", "Unknown or expired invite code");
                    case RelayExceptionReason.NetworkError:
                        return NoInternet();
                    case RelayExceptionReason.InactiveProject:
                    case RelayExceptionReason.Unauthorized:
                    case RelayExceptionReason.Forbidden:
                    case RelayExceptionReason.PaymentRequired:
                        return Loc.T("Codes d'invitation non activés pour ce jeu", "Invite codes are not enabled for this game");
                    case RelayExceptionReason.Conflict:
                        return Loc.T("La partie est complète", "The game is full");
                }
                return Unreachable();
            }
            if (e is ServicesInitializationException)
                return Loc.T("Ce jeu n'est pas relié aux services en ligne", "This game is not linked to the online services");
            if (e is RequestFailedException failed && failed.ErrorCode == CommonErrorCodes.TransportError) return NoInternet();
            return Unreachable();
        }

        static string NoInternet() => Loc.T("Pas de connexion Internet", "No Internet connection");
        static string Unreachable() => Loc.T("Services en ligne injoignables", "Online services unreachable");

        // ------------------------------------------------------------------ lien

        /// <summary>
        /// Ouvre le transport sur le relais réservé : il écoute (hôte) ou demande l'entrée chez l'hôte (joueur). Faux si
        /// le pilote ne s'ouvre pas (dans l'éditeur : Burst arrêté après une mise à jour de paquet, jusqu'au redémarrage).
        /// </summary>
        bool Open(RelayServerData data)
        {
            try { return Link(data); }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] Relais : " + e.Message);
                if (driver.IsCreated) driver.Dispose();
                driver = default;
                hostConnection = default;
                return false;
            }
        }

        bool Link(RelayServerData data)
        {
            var settings = new NetworkSettings(Allocator.Temp);
            settings.WithRelayParameters(ref data);
            // Entrée chez l'hôte : dix essais d'une seconde ; lien muet dix secondes : il est considéré comme rompu
            settings.WithNetworkConfigParameters(connectTimeoutMS: 1000, maxConnectAttempts: 10, disconnectTimeoutMS: 10000);
            driver = NetworkDriver.Create(settings);
            if (!Hosting)
            {
                hostConnection = driver.Connect();
                return hostConnection.IsCreated;
            }
            // Par le relais, le port local n'a aucune importance : un port libre quelconque
            return driver.Bind(NetworkEndpoint.AnyIpv4) == 0 && driver.Listen() == 0;
        }

        void Fail(string message)
        {
            Release();
            Error = message;
            State = Step.Failed;
        }

        /// <summary>
        /// À appeler à chaque image, avant <see cref="Receive"/> : suit les appels aux services, fait vivre le lien
        /// (envois en attente, réception, entretien de la réservation) et accueille les joueurs qui arrivent.
        /// </summary>
        public void Update()
        {
            if (State == Step.Off || State == Step.Failed) return;
            if (hosting != null || joining != null)
            {
                Task task = hosting;
                if (task == null) task = joining;
                if (!task.IsCompleted)
                {
                    if (Time.unscaledTime > deadline) Fail(Unreachable());
                    return;
                }
                if (task.IsFaulted || task.IsCanceled)
                {
                    Fail(task.Exception != null ? Describe(task.Exception) : Unreachable());
                    return;
                }
                bool opened;
                if (hosting != null)
                {
                    Code = hosting.Result.code;
                    opened = Open(hosting.Result.allocation.ToRelayServerData(Protocol));
                }
                else opened = Open(joining.Result.ToRelayServerData(Protocol));
                hosting = null;
                joining = null;
                if (!opened) { Fail(Loc.T("Relais indisponible sur cette machine", "Relay unavailable on this computer")); return; }
                deadline = Time.unscaledTime + ServiceTimeout;
            }
            if (!driver.IsCreated) return;
            driver.ScheduleUpdate().Complete();
            var status = driver.GetRelayConnectionStatus();
            if (status == RelayConnectionStatus.AllocationInvalid)
            {
                // Le relais ne connaît plus notre réservation (coupure d'Internet trop longue, serveur du relais arrêté)
                Fail(Hosting ? Loc.T("Le code d'invitation a expiré", "The invite code has expired")
                    : State == Step.Ready ? Loc.T("Connexion au relais perdue", "Connection to the relay lost")
                    : Loc.T("Code d'invitation inconnu ou expiré", "Unknown or expired invite code"));
                return;
            }
            if (State == Step.Pending && Time.unscaledTime > deadline)
            {
                Fail(Hosting ? Unreachable() : Loc.T("L'hôte ne répond pas", "The host does not answer"));
                return;
            }
            if (Hosting)
            {
                // Le code n'est donné qu'une fois le relais prêt à faire suivre les arrivants
                if (State == Step.Pending && status == RelayConnectionStatus.Established) State = Step.Ready;
                NetworkConnection arrived;
                while ((arrived = driver.Accept()) != default)
                {
                    var address = new Address(nextSerial++);
                    byAddress[address] = arrived;
                    byConnection[arrived] = address;
                    Measure(arrived);
                }
            }
            else if (State == Step.Ready)
            {
                pingTimer -= Time.unscaledDeltaTime;
                if (pingTimer <= 0f)
                {
                    pingTimer = PingPeriod;
                    SendPing(hostConnection, 0, (uint)(Time.realtimeSinceStartupAsDouble * 1000.0));
                }
            }
        }

        /// <summary>Charge utile d'un paquet sur ce lien : relevée à la première connexion, pour la comparer aux paquets du jeu.</summary>
        void Measure(NetworkConnection connection)
        {
            if (MaxPayload > 0) return;
            int size = driver.GetMaxSupportedPayloadSize(connection, NetworkPipeline.Null);
            if (size <= 0) return;
            MaxPayload = size;
            Debug.Log($"[Net] Relais : {size} octets utiles par paquet.");
        }

        void SendPing(NetworkConnection to, byte kind, uint ms)
        {
            if (driver.GetConnectionState(to) != NetworkConnection.State.Connected) return;
            pingBytes[0] = (byte)'C';
            pingBytes[1] = (byte)'P';
            pingBytes[2] = kind;
            for (int i = 0; i < 4; i++) pingBytes[3 + i] = (byte)(ms >> (8 * i));
            if (driver.BeginSend(to, out var writer, PingSize) != 0) return;
            writer.WriteBytes(new Span<byte>(pingBytes, 0, PingSize));
            driver.EndSend(writer);
        }

        /// <summary>Paquet suivant en attente (dans <see cref="Buffer"/>) et adresse de son expéditeur ; faux s'il n'y en a plus.</summary>
        public bool Receive(out int length, out IPEndPoint from)
        {
            length = 0;
            from = null;
            if (!driver.IsCreated) return false;
            NetworkEvent.Type type;
            while (driver.IsCreated && (type = driver.PopEvent(out var connection, out var reader)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Connect)
                {
                    // Joueur : l'hôte nous a acceptés
                    if (Hosting || State != Step.Pending) continue;
                    State = Step.Ready;
                    pingTimer = 0f;
                    Measure(connection);
                    continue;
                }
                if (type == NetworkEvent.Type.Disconnect)
                {
                    if (Hosting)
                    {
                        if (byConnection.TryGetValue(connection, out var gone))
                        {
                            byConnection.Remove(connection);
                            byAddress.Remove(gone);
                        }
                        continue;
                    }
                    Fail(State == Step.Ready ? Loc.T("Connexion à l'hôte perdue", "Connection to the host lost")
                        : Loc.T("L'hôte ne répond pas : la partie est peut-être fermée", "The host does not answer: the game may be closed"));
                    return false;
                }
                int n = reader.Length;
                if (n <= 0 || n > buffer.Length) continue;
                reader.ReadBytes(new Span<byte>(buffer, 0, n));
                IPEndPoint sender;
                if (Hosting)
                {
                    if (!byConnection.TryGetValue(connection, out var known)) continue;
                    sender = known;
                }
                else sender = serverAddress;
                if (n == PingSize && buffer[0] == (byte)'C' && buffer[1] == (byte)'P')
                {
                    uint ms = (uint)(buffer[3] | buffer[4] << 8 | buffer[5] << 16 | buffer[6] << 24);
                    if (buffer[2] == 0) SendPing(connection, 1, ms);
                    else if (!Hosting) PingMs = Mathf.Clamp((int)((uint)(Time.realtimeSinceStartupAsDouble * 1000.0) - ms), 0, 9999);
                    continue;
                }
                length = n;
                from = sender;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Envoie un paquet à une adresse de ce lien. Faux si l'adresse n'est pas une adresse de relais (le paquet est
        /// alors pour la prise UDP) ; vrai sinon, même si la connexion n'existe plus : le paquet est simplement perdu.
        /// </summary>
        public bool Send(NetWriter w, IPEndPoint to)
        {
            if (!(to is Address)) return false;
            if (!driver.IsCreated || w.Overflow) return true;
            NetworkConnection connection;
            if (Hosting) { if (!byAddress.TryGetValue(to, out connection)) return true; }
            else connection = hostConnection;
            if (driver.GetConnectionState(connection) != NetworkConnection.State.Connected) return true;
            int result = driver.BeginSend(connection, out var writer, w.Length);
            if (result != 0)
            {
                if (result == (int)Unity.Networking.Transport.Error.StatusCode.NetworkPacketOverflow && !oversizeLogged)
                {
                    oversizeLogged = true;
                    Debug.LogWarning($"[Net] Relais : paquet de {w.Length} octets trop gros pour le lien ({MaxPayload} au plus), non envoyé.");
                }
                return true;
            }
            writer.WriteBytes(new Span<byte>(w.Data, 0, w.Length));
            driver.EndSend(writer);
            return true;
        }

        /// <summary>Ferme le lien en prévenant l'autre bout ; les paquets en attente (adieux) partent d'abord.</summary>
        public void Close()
        {
            Release();
            Error = null;
            State = Step.Off;
        }

        void Release()
        {
            if (driver.IsCreated)
            {
                try
                {
                    // Les paquets en attente d'abord, puis l'annonce de la fermeture
                    driver.ScheduleUpdate().Complete();
                    foreach (var connection in byConnection.Keys) driver.Disconnect(connection);
                    if (hostConnection.IsCreated) driver.Disconnect(hostConnection);
                    driver.ScheduleUpdate().Complete();
                }
                catch (Exception e) { Debug.LogWarning("[Net] Relais : " + e.Message); }
                driver.Dispose();
            }
            driver = default;
            hostConnection = default;
            byAddress.Clear();
            byConnection.Clear();
            // Une réponse des services encore attendue est abandonnée : la réservation inutilisée s'éteint d'elle-même
            hosting = null;
            joining = null;
            Code = null;
            PingMs = -1;
            MaxPayload = 0;
        }

        public void Dispose() => Close();
    }
}
