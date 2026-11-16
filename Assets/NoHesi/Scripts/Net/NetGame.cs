using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace NoHesi
{
    /// <summary>
    /// Partie en réseau : balade libre à plusieurs sur la même carte, en réseau local ou par Internet.
    /// - Hôte : un joueur héberge la partie et joue ; les autres le trouvent tout seuls sur le réseau local, ou entrent
    ///   son adresse IP. Serveur dédié : le même rôle sans voiture (lancement avec « -server »).
    /// - Chaque joueur simule sa propre voiture et en envoie l'état ; le serveur le redistribue à tous.
    /// - Le serveur fait foi pour l'heure, la météo et le trafic, qu'il simule autour de tous les joueurs. Un véhicule
    ///   percuté est ensuite simulé par le joueur qui l'a heurté (lui seul a le relief sous ses roues).
    /// - Connexion perdue : la balade continue en solo.
    /// </summary>
    public class NetGame : MonoBehaviour
    {
        public enum Mode { Off, Host, Client }

        /// <summary>Partie trouvée sur le réseau local.</summary>
        public class ServerInfo
        {
            public IPEndPoint ep;
            public uint instance;
            public string name;
            public int players, max;
            /// <summary>Même version du protocole que nous.</summary>
            public bool compatible;
            public float seen;
        }

        class Peer
        {
            public byte id;
            public IPEndPoint ep;
            public string name;
            public uint nonce, lastMs;
            public float heard;
            public bool hasState;
            public NetCarState state;
            public NetClock clock;
            public RemoteCar car;
        }

        const string NameKey = "nohesi_net_name";

        public Mode State { get; private set; }
        /// <summary>Serveur sans joueur local.</summary>
        public bool Dedicated { get; private set; }
        /// <summary>Demande d'entrée envoyée, réponse du serveur attendue.</summary>
        public bool Connecting { get; private set; }
        /// <summary>Partie en réseau en cours (hébergée ou rejointe).</summary>
        public bool Active => State == Mode.Host || State == Mode.Client && !Connecting;
        public bool IsHost => State == Mode.Host;
        public bool IsClient => State == Mode.Client && !Connecting;
        /// <summary>Notre numéro de joueur (0 : l'hôte).</summary>
        public byte LocalId { get; private set; }
        public string ServerName { get; private set; } = "";
        public int Port { get; private set; }
        public int MaxPlayers { get; private set; } = NetProtocol.DefaultMaxPlayers;
        /// <summary>Voitures des autres joueurs.</summary>
        public IReadOnlyList<RemoteCar> Remotes => remotes;
        /// <summary>Parties trouvées sur le réseau local (voir <see cref="Search"/>).</summary>
        public readonly List<ServerInfo> Found = new List<ServerInfo>();
        /// <summary>Partie rejointe (le serveur a répondu).</summary>
        public event Action Joined;
        /// <summary>Entrée refusée ou sans réponse : message à afficher.</summary>
        public event Action<string> Failed;

        /// <summary>Pseudo du joueur (enregistré).</summary>
        public static string PlayerName
        {
            get => NetProtocol.CleanName(PlayerPrefs.GetString(NameKey, Loc.T("Pilote", "Driver")));
            set { PlayerPrefs.SetString(NameKey, NetProtocol.CleanName(value)); PlayerPrefs.Save(); }
        }

        /// <summary>Joueurs présents, soi compris.</summary>
        public int PlayerCount => State == Mode.Host ? peerList.Count + (Dedicated ? 0 : 1) : IsClient ? Mathf.Max(names.Count, remotes.Count + 1) : 1;

        /// <summary>Adresse à donner aux joueurs qui veulent rejoindre (hôte).</summary>
        public string LocalAddress
        {
            get
            {
                var list = NetSocket.LocalAddresses();
                string ip = list.Count > 0 ? list[0] : "127.0.0.1";
                return Port == NetProtocol.DefaultPort ? ip : ip + ":" + Port;
            }
        }

        GameManager game;
        NetSocket socket;
        readonly NetWriter writer = new NetWriter();
        readonly NetReader reader = new NetReader();
        readonly List<RemoteCar> remotes = new List<RemoteCar>();
        float stateTimer, rosterTimer, trafficTimer;

        // Hôte
        readonly Dictionary<IPEndPoint, Peer> peers = new Dictionary<IPEndPoint, Peer>();
        readonly List<Peer> peerList = new List<Peer>();
        uint instance;
        ushort trafficFrame;
        readonly List<TrafficSystem.Car> sendCars = new List<TrafficSystem.Car>();

        // Client
        IPEndPoint server;
        Task<IPAddress[]> resolving;
        int resolvingPort;
        uint nonce;
        float helloTimer, deadline, heard;
        uint snapshotMs;
        bool hasSnapshot, hasTrafficFrame, rosterKnown;
        int trafficMask, stamp;
        NetClock serverClock;
        Dictionary<byte, string> names = new Dictionary<byte, string>();
        readonly Dictionary<byte, RemoteCar> byId = new Dictionary<byte, RemoteCar>();
        readonly List<(byte id, uint ms, NetCarState state)> incoming = new List<(byte, uint, NetCarState)>();

        // Étiquettes des joueurs, écran du serveur dédié
        RectTransform overlay;
        readonly List<Text> tags = new List<Text>();
        Text serverText;
        float serverTextTimer, spectateTimer;
        int spectateIndex = -1;
        RemoteCar spectated;

        public void Init(GameManager gm) => game = gm;

        static uint NowMs => (uint)(Time.timeAsDouble * 1000.0);

        // ------------------------------------------------------------------ héberger

        /// <summary>
        /// Héberge une partie sur ce port (ou l'un des suivants s'il est pris). Faux, avec le motif, si aucun n'est libre.
        /// </summary>
        public bool Host(string serverName, int port, int maxPlayers, bool dedicated, out string error)
        {
            Shutdown(true);
            error = null;
            for (int i = 0; i < NetProtocol.PortTries && socket == null; i++) socket = NetSocket.Open(port + i);
            if (socket == null)
            {
                error = Loc.T($"Impossible d'ouvrir le port {port} (déjà utilisé ?)", $"Could not open port {port} (already in use?)");
                return false;
            }
            State = Mode.Host;
            Dedicated = dedicated;
            LocalId = 0;
            Port = socket.Port;
            ServerName = NetProtocol.CleanName(serverName, NetProtocol.MaxServerName);
            MaxPlayers = Mathf.Clamp(maxPlayers, 1, NetProtocol.MaxPlayersLimit);
            instance = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            game.Traffic.LocalId = 0;
            // La partie continue quand la fenêtre n'est plus au premier plan : les autres joueurs en dépendent
            Application.runInBackground = true;
            Debug.Log($"[Net] Serveur « {ServerName} » ouvert sur le port {Port} ({string.Join(", ", NetSocket.LocalAddresses())}), {MaxPlayers} joueurs au plus.");
            return true;
        }

        void HostReceive(NetMsg msg, IPEndPoint from)
        {
            switch (msg)
            {
                case NetMsg.Query:
                    writer.Begin(NetMsg.Info);
                    writer.UInt(instance);
                    writer.String(ServerName);
                    writer.Byte((byte)PlayerCount);
                    writer.Byte((byte)MaxPlayers);
                    socket.Send(writer, from);
                    break;
                case NetMsg.Hello:
                    OnHello(from);
                    break;
                case NetMsg.State:
                    if (reader.Version == NetProtocol.Version && peers.TryGetValue(from, out var p)) OnState(p);
                    break;
                case NetMsg.Bye:
                    if (peers.TryGetValue(from, out var gone)) RemovePeer(gone);
                    break;
            }
        }

        void OnHello(IPEndPoint from)
        {
            uint theirNonce = reader.UInt();
            string name = NetProtocol.CleanName(reader.String());
            if (!reader.Ok) return;
            if (reader.Version != NetProtocol.Version) { Refuse(from, theirNonce, NetRefuse.Version); return; }
            if (!peers.TryGetValue(from, out var p))
            {
                if (PlayerCount >= MaxPlayers) { Refuse(from, theirNonce, NetRefuse.Full); return; }
                byte id = 1;
                while (peerList.Exists(q => q.id == id)) id++;
                p = new Peer { id = id, ep = from, name = name, nonce = theirNonce };
                peers[from] = p;
                peerList.Add(p);
                Log($"{name} joined the game");
            }
            p.heard = Time.unscaledTime;
            writer.Begin(NetMsg.Welcome);
            writer.UInt(theirNonce);
            writer.Byte(p.id);
            writer.String(ServerName);
            writer.Byte((byte)MaxPlayers);
            socket.Send(writer, from);
        }

        void Refuse(IPEndPoint to, uint theirNonce, byte reason)
        {
            writer.Begin(NetMsg.Refuse);
            writer.UInt(theirNonce);
            writer.Byte(reason);
            socket.Send(writer, to);
        }

        /// <summary>État de la voiture d'un joueur, et véhicules du trafic qu'il a percutés (il les simule).</summary>
        void OnState(Peer p)
        {
            uint ms = reader.UInt();
            var s = NetCarState.Read(reader);
            int owned = reader.Byte();
            if (!reader.Ok || owned > NetProtocol.MaxOwned) return;
            p.heard = Time.unscaledTime;
            if (p.hasState && (int)(ms - p.lastMs) <= 0) return;
            double t = p.clock.ToLocal(ms / 1000.0, Time.timeAsDouble);
            for (int i = 0; i < owned; i++)
            {
                ushort id = reader.UShort();
                Vector3 pos = reader.Vector();
                Quaternion rot = reader.Rotation();
                Vector3 vel = reader.Velocity();
                if (!reader.Ok) return;
                game.Traffic.Claim(p.id, id, pos, rot, vel, t);
            }
            // Replacé sur la route (ou arrivé) : la voie est dégagée autour de lui
            if (!p.hasState || s.teleport != p.state.teleport) game.Traffic.ClearAround(s.pos, 14f);
            p.lastMs = ms;
            p.state = s;
            p.hasState = true;
            if (!p.car) p.car = AddRemote(p.id, p.name);
            p.car.Apply(s, ms);
        }

        void RemovePeer(Peer p)
        {
            peers.Remove(p.ep);
            peerList.Remove(p);
            RemoveRemote(p.car);
            game.Traffic.DropOwner(p.id);
            Log($"{p.name} left the game");
        }

        void HostUpdate(float dt)
        {
            float now = Time.unscaledTime;
            for (int i = peerList.Count - 1; i >= 0; i--)
                if (now - peerList[i].heard > NetProtocol.Timeout) RemovePeer(peerList[i]);
            if (peerList.Count == 0) return;
            if (Due(ref stateTimer, 1f / NetProtocol.PlayerRate, dt)) SendSnapshot();
            if (Due(ref rosterTimer, 1f / NetProtocol.RosterRate, dt)) SendRoster();
            if (Due(ref trafficTimer, 1f / NetProtocol.TrafficRate, dt)) SendTraffic();
        }

        static bool Due(ref float timer, float period, float dt)
        {
            timer -= dt;
            if (timer > 0f) return false;
            timer = Mathf.Max(timer + period, 0f);
            return true;
        }

        void Broadcast()
        {
            foreach (var p in peerList) socket.Send(writer, p.ep);
        }

        /// <summary>Toutes les voitures des joueurs, l'heure et la météo.</summary>
        void SendSnapshot()
        {
            uint ms = NowMs;
            writer.Begin(NetMsg.Snapshot);
            writer.UInt(ms);
            writer.Float(game.DayNight.timeOfDay);
            var weather = game.Weather;
            writer.Byte((byte)(weather.Stormy ? 1 : 0));
            writer.Unit(weather.Storm);
            writer.Unit(weather.Rain);
            writer.Unit(weather.Wetness);
            int countAt = writer.Length;
            writer.Byte(0);
            int n = 0;
            if (!Dedicated)
            {
                writer.Byte(0);
                writer.UInt(ms);
                Capture().Write(writer);
                n++;
            }
            foreach (var p in peerList)
            {
                if (!p.hasState) continue;
                writer.Byte(p.id);
                writer.UInt(p.lastMs);
                p.state.Write(writer);
                n++;
            }
            writer.Patch(countAt, (byte)n);
            Broadcast();
        }

        void SendRoster()
        {
            writer.Begin(NetMsg.Roster);
            writer.Byte((byte)PlayerCount);
            if (!Dedicated) { writer.Byte(0); writer.String(PlayerName); }
            foreach (var p in peerList) { writer.Byte(p.id); writer.String(p.name); }
            Broadcast();
        }

        /// <summary>Le trafic, en autant de paquets qu'il faut ; chaque envoi complet porte un numéro.</summary>
        void SendTraffic()
        {
            sendCars.Clear();
            foreach (var c in game.Traffic.Cars) if (c.go) sendCars.Add(c);
            trafficFrame++;
            uint ms = NowMs;
            int per = NetProtocol.TrafficPerPacket;
            int chunks = Mathf.Clamp((sendCars.Count + per - 1) / per, 1, 8);
            for (int k = 0; k < chunks; k++)
            {
                int start = k * per, count = Mathf.Clamp(sendCars.Count - start, 0, per);
                writer.Begin(NetMsg.Traffic);
                writer.UInt(ms);
                writer.UShort(trafficFrame);
                writer.Byte((byte)k);
                writer.Byte((byte)chunks);
                writer.Byte((byte)count);
                for (int i = start; i < start + count; i++)
                {
                    var c = sendCars[i];
                    var t = c.go.transform;
                    writer.UShort(c.id);
                    writer.Byte((byte)(c.kind << 6 | c.model & 63));
                    writer.Byte(c.paint.r);
                    writer.Byte(c.paint.g);
                    writer.Byte(c.paint.b);
                    writer.Byte((byte)(c.crashed ? 1 : 0));
                    writer.Byte(c.owner);
                    writer.Vector(t.position);
                    writer.Rotation(t.rotation);
                    writer.Velocity(c.velocity);
                }
                Broadcast();
            }
        }

        // ------------------------------------------------------------------ rejoindre

        bool EnsureSocket()
        {
            if (socket == null) socket = NetSocket.Open(0);
            return socket != null;
        }

        /// <summary>Recherche sur le réseau local : diffuse une question, les réponses remplissent <see cref="Found"/>.</summary>
        public void Search()
        {
            if (State != Mode.Off || !EnsureSocket()) return;
            writer.Begin(NetMsg.Query);
            foreach (var address in NetSocket.BroadcastAddresses())
                for (int i = 0; i < NetProtocol.PortTries; i++)
                    socket.Send(writer, new IPEndPoint(address, NetProtocol.DefaultPort + i));
        }

        /// <summary>Rejoint le serveur à cette adresse : « 192.168.1.20 », « 192.168.1.20:47778 » ou un nom de machine.</summary>
        public void Join(string address)
        {
            if (State != Mode.Off) return;
            address = (address ?? "").Trim();
            int port = NetProtocol.DefaultPort;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out int parsed) && parsed > 0 && parsed < 65536)
            {
                port = parsed;
                address = address.Substring(0, colon);
            }
            if (address.Length == 0) { Fail(Loc.T("Entre l'adresse du serveur", "Enter the server address")); return; }
            if (!EnsureSocket()) { Fail(Loc.T("Réseau indisponible", "Network unavailable")); return; }
            if (IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                Join(new IPEndPoint(ip, port));
                return;
            }
            // Nom de machine : résolu en tâche de fond
            State = Mode.Client;
            Connecting = true;
            server = null;
            resolvingPort = port;
            deadline = Time.unscaledTime + 8f;
            try { resolving = Dns.GetHostAddressesAsync(address); }
            catch (Exception) { Fail(Loc.T("Adresse introuvable", "Address not found")); }
        }

        public void Join(IPEndPoint endPoint)
        {
            if (State == Mode.Host || IsClient || !EnsureSocket()) return;
            State = Mode.Client;
            Connecting = true;
            resolving = null;
            server = endPoint;
            nonce = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            helloTimer = 0f;
            deadline = Time.unscaledTime + 6f;
        }

        /// <summary>Abandonne une demande d'entrée en cours.</summary>
        public void CancelJoin()
        {
            if (!Connecting) return;
            Connecting = false;
            State = Mode.Off;
            server = null;
            resolving = null;
        }

        void Fail(string message)
        {
            Connecting = false;
            State = Mode.Off;
            server = null;
            resolving = null;
            Failed?.Invoke(message);
        }

        void ConnectUpdate(float dt)
        {
            if (Time.unscaledTime > deadline) { Fail(Loc.T("Pas de réponse du serveur", "No answer from the server")); return; }
            if (resolving != null)
            {
                if (!resolving.IsCompleted) return;
                IPAddress found = null;
                if (resolving.Status == TaskStatus.RanToCompletion)
                    foreach (var a in resolving.Result)
                        if (a.AddressFamily == AddressFamily.InterNetwork) { found = a; break; }
                resolving = null;
                if (found == null) { Fail(Loc.T("Adresse introuvable", "Address not found")); return; }
                Connecting = false;
                State = Mode.Off;
                Join(new IPEndPoint(found, resolvingPort));
                return;
            }
            helloTimer -= dt;
            if (helloTimer > 0f) return;
            helloTimer = 0.4f;
            writer.Begin(NetMsg.Hello);
            writer.UInt(nonce);
            writer.String(PlayerName);
            socket.Send(writer, server);
        }

        void ClientReceive(NetMsg msg, IPEndPoint from)
        {
            if (msg == NetMsg.Info) { OnInfo(from); return; }
            if (server == null || !from.Equals(server)) return;
            if (Connecting)
            {
                if (msg == NetMsg.Welcome) OnWelcome();
                else if (msg == NetMsg.Refuse) OnRefuse();
                return;
            }
            if (State != Mode.Client || reader.Version != NetProtocol.Version) return;
            switch (msg)
            {
                case NetMsg.Snapshot: OnSnapshot(); break;
                case NetMsg.Roster: OnRoster(); break;
                case NetMsg.Traffic: OnTraffic(); break;
                case NetMsg.Bye: Disconnected(Loc.T("Le serveur s'est arrêté : balade en solo", "The server has stopped: driving solo")); break;
            }
        }

        void OnInfo(IPEndPoint from)
        {
            uint id = reader.UInt();
            string name = reader.String();
            int players = reader.Byte(), max = reader.Byte();
            if (!reader.Ok) return;
            var info = Found.Find(f => f.instance == id);
            if (info == null) Found.Add(info = new ServerInfo { ep = from, instance = id });
            info.name = NetProtocol.CleanName(name, NetProtocol.MaxServerName);
            info.players = players;
            info.max = max;
            info.compatible = reader.Version == NetProtocol.Version;
            info.seen = Time.unscaledTime;
        }

        void OnWelcome()
        {
            uint echoed = reader.UInt();
            byte id = reader.Byte();
            string name = reader.String();
            int max = reader.Byte();
            if (!reader.Ok || echoed != nonce) return;
            Connecting = false;
            LocalId = id;
            ServerName = NetProtocol.CleanName(name, NetProtocol.MaxServerName);
            MaxPlayers = max;
            Port = server.Port;
            heard = Time.unscaledTime;
            hasSnapshot = hasTrafficFrame = rosterKnown = false;
            serverClock = new NetClock();
            names.Clear();
            // Le trafic, l'heure et la météo sont désormais ceux du serveur
            game.Traffic.LocalId = id;
            game.Traffic.SetReplica(true);
            Application.runInBackground = true;
            Debug.Log($"[Net] Partie « {ServerName} » rejointe ({server}), joueur {id}.");
            Joined?.Invoke();
        }

        void OnRefuse()
        {
            uint echoed = reader.UInt();
            byte reason = reader.Byte();
            if (!reader.Ok || echoed != nonce) return;
            Fail(reason == NetRefuse.Full ? Loc.T("La partie est complète", "The game is full")
                : reason == NetRefuse.Version ? Loc.T("Version du jeu différente de celle du serveur", "Game version differs from the server's")
                : Loc.T("Entrée refusée par le serveur", "The server refused the connection"));
        }

        void OnSnapshot()
        {
            uint ms = reader.UInt();
            float hour = reader.Float();
            bool stormy = reader.Byte() != 0;
            float storm = reader.Unit(), rain = reader.Unit(), wetness = reader.Unit();
            int n = reader.Byte();
            if (!reader.Ok || n > NetProtocol.MaxPlayersLimit + 1) return;
            incoming.Clear();
            for (int i = 0; i < n; i++)
            {
                byte id = reader.Byte();
                uint stateMs = reader.UInt();
                var s = NetCarState.Read(reader);
                if (!reader.Ok) return;
                incoming.Add((id, stateMs, s));
            }
            heard = Time.unscaledTime;
            if (hasSnapshot && (int)(ms - snapshotMs) <= 0) return;
            snapshotMs = ms;
            hasSnapshot = true;
            game.DayNight.SyncTime(hour);
            game.Weather.SetRemote(stormy, storm, rain, wetness);
            stamp++;
            foreach (var (id, stateMs, s) in incoming)
            {
                if (id == LocalId) continue;
                if (!byId.TryGetValue(id, out var car) || !car)
                    car = AddRemote(id, names.TryGetValue(id, out var known) ? known : "");
                car.Stamp = stamp;
                car.Apply(s, stateMs);
            }
            // Les joueurs absents de la liste sont partis
            for (int i = remotes.Count - 1; i >= 0; i--)
                if (remotes[i].Stamp != stamp) RemoveRemote(remotes[i]);
        }

        void OnRoster()
        {
            int n = reader.Byte();
            if (!reader.Ok || n > NetProtocol.MaxPlayersLimit + 1) return;
            var fresh = new Dictionary<byte, string>();
            for (int i = 0; i < n; i++)
            {
                byte id = reader.Byte();
                string name = NetProtocol.CleanName(reader.String());
                if (!reader.Ok) return;
                fresh[id] = name;
            }
            if (rosterKnown)
            {
                foreach (var kv in fresh)
                    if (kv.Key != LocalId && !names.ContainsKey(kv.Key)) Log($"{kv.Value} joined the game");
                foreach (var kv in names)
                    if (kv.Key != LocalId && !fresh.ContainsKey(kv.Key)) Log($"{kv.Value} left the game");
            }
            names = fresh;
            rosterKnown = true;
            foreach (var r in remotes)
                if (names.TryGetValue(r.Id, out var name)) r.Name = name;
        }

        void OnTraffic()
        {
            uint ms = reader.UInt();
            ushort frame = reader.UShort();
            int chunk = reader.Byte(), chunks = reader.Byte(), n = reader.Byte();
            if (!reader.Ok || chunks == 0 || chunks > 8 || chunk >= chunks || n > NetProtocol.TrafficPerPacket) return;
            int age = (short)(frame - trafficFrame);
            if (hasTrafficFrame && age < 0) return;
            if (!hasTrafficFrame || age > 0)
            {
                trafficFrame = frame;
                trafficMask = 0;
                hasTrafficFrame = true;
            }
            double t = serverClock.ToLocal(ms / 1000.0, Time.timeAsDouble);
            for (int i = 0; i < n; i++)
            {
                ushort id = reader.UShort();
                int model = reader.Byte();
                byte r = reader.Byte(), g = reader.Byte(), b = reader.Byte();
                bool crashed = (reader.Byte() & 1) != 0;
                byte owner = reader.Byte();
                Vector3 pos = reader.Vector();
                Quaternion rot = reader.Rotation();
                Vector3 vel = reader.Velocity();
                if (!reader.Ok) return;
                game.Traffic.ApplyNet(id, model >> 6, model & 63, new Color32(r, g, b, 255), crashed, owner, pos, rot, vel, t, frame);
            }
            trafficMask |= 1 << chunk;
            // Envoi complet reçu : les véhicules qui n'y figurent plus ont disparu
            if (trafficMask == (1 << chunks) - 1) game.Traffic.EndNetFrame(frame);
        }

        void ClientUpdate(float dt)
        {
            if (Time.unscaledTime - heard > NetProtocol.Timeout)
            {
                Disconnected(Loc.T("Connexion au serveur perdue : balade en solo", "Connection to the server lost: driving solo"));
                return;
            }
            if (!Due(ref stateTimer, 1f / NetProtocol.PlayerRate, dt)) return;
            writer.Begin(NetMsg.State);
            writer.UInt(NowMs);
            Capture().Write(writer);
            // Véhicules du trafic percutés : simulés ici, leur état part avec le nôtre
            int countAt = writer.Length;
            writer.Byte(0);
            int owned = 0;
            foreach (var c in game.Traffic.Cars)
            {
                if (!c.mine || !c.go || owned >= NetProtocol.MaxOwned) continue;
                writer.UShort(c.id);
                writer.Vector(c.rb.position);
                writer.Rotation(c.rb.rotation);
                writer.Velocity(c.rb.linearVelocity);
                owned++;
            }
            writer.Patch(countAt, (byte)owned);
            socket.Send(writer, server);
        }

        /// <summary>Le serveur ne répond plus (ou s'est arrêté) : on continue seul, avec son propre trafic.</summary>
        void Disconnected(string message)
        {
            Shutdown(false);
            Log(message);
        }

        // ------------------------------------------------------------------ commun

        /// <summary>État de la voiture locale à envoyer.</summary>
        NetCarState Capture()
        {
            var car = game.Player;
            var t = car.transform;
            return new NetCarState
            {
                pos = t.position, rot = t.rotation, vel = car.Velocity,
                steer = car.SteerAngle, rpm01 = car.Rpm01, throttle = car.Throttle, gear = car.Gear,
                carIndex = game.Local.CarIndex, teleport = (byte)car.TeleportCount,
                paint = (byte)CarPaint.Selected(game.assets.playerCars[Mathf.Clamp(game.Local.CarIndex, 0, game.assets.playerCars.Length - 1)]),
                lights = game.Lights && game.Lights.On, braking = car.BrakeInput > 0.1f, handbrake = car.HandbrakeOn,
                onRoad = car.OnRoad, grounded = car.GroundedWheels > 0, dirt = car.SurfaceDirt,
                skid = car.SkidAmount, skidFront = car.SkidFront, skidRear = car.SkidRear,
            };
        }

        RemoteCar AddRemote(byte id, string name)
        {
            var car = RemoteCar.Create(game, id, name);
            remotes.Add(car);
            byId[id] = car;
            game.Traffic.Drivers.Add(car.Car);
            return car;
        }

        void RemoveRemote(RemoteCar car)
        {
            if (!car) return;
            remotes.Remove(car);
            if (byId.TryGetValue(car.Id, out var known) && known == car) byId.Remove(car.Id);
            game.Traffic.Drivers.Remove(car.Car);
            Destroy(car.gameObject);
        }

        /// <summary>Ajoute la position des autres joueurs (le trafic vit autour de tous).</summary>
        public void AddPlayers(List<Vector3> positions)
        {
            foreach (var r in remotes)
                if (r && r.Ready) positions.Add(r.transform.position);
        }

        static void Log(string message) => Debug.Log("[Net] " + message);

        /// <summary>Quitte la partie en prévenant les autres (retour au menu, fermeture du jeu).</summary>
        public void Leave() => Shutdown(true);

        void Shutdown(bool sayBye)
        {
            if (socket != null && sayBye)
            {
                // Deux fois : un paquet perdu ne doit pas laisser un fantôme jusqu'au délai d'absence
                for (int k = 0; k < 2; k++)
                {
                    writer.Begin(NetMsg.Bye);
                    if (State == Mode.Host) Broadcast();
                    else if (IsClient && server != null) socket.Send(writer, server);
                }
            }
            bool wasClient = IsClient;
            for (int i = remotes.Count - 1; i >= 0; i--) RemoveRemote(remotes[i]);
            byId.Clear();
            peers.Clear();
            peerList.Clear();
            names.Clear();
            Found.Clear();
            socket?.Dispose();
            socket = null;
            server = null;
            resolving = null;
            State = Mode.Off;
            Connecting = false;
            Dedicated = false;
            LocalId = 0;
            if (!game) return;
            if (game.Traffic) game.Traffic.LocalId = 0;
            if (wasClient)
            {
                if (game.Traffic) game.Traffic.SetReplica(false);
                if (game.Weather) game.Weather.ClearRemote();
            }
        }

        void OnDestroy() => Shutdown(true);
        void OnApplicationQuit() => Shutdown(true);

        void Update()
        {
            if (socket == null) return;
            // Lecture de tout ce qui est arrivé depuis la dernière image
            for (int guard = 0; guard < 500 && socket != null && socket.Receive(out int length, out var from); guard++)
            {
                if (!reader.Begin(socket.Buffer, length, out var msg)) continue;
                if (State == Mode.Host) HostReceive(msg, from);
                else ClientReceive(msg, from);
            }
            if (socket == null) return;
            float dt = Time.unscaledDeltaTime;
            if (State == Mode.Host) HostUpdate(dt);
            else if (Connecting) ConnectUpdate(dt);
            else if (State == Mode.Client) ClientUpdate(dt);
            for (int i = Found.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - Found[i].seen > 4.5f) Found.RemoveAt(i);
        }

        // ------------------------------------------------------------------ étiquettes, serveur dédié

        void LateUpdate()
        {
            if (!game) return;
            UpdateSpectator();
            UpdateTags();
        }

        /// <summary>Serveur dédié : la caméra suit un joueur, puis le suivant ; personne : vue tournante du départ.</summary>
        void UpdateSpectator()
        {
            if (!Dedicated || !game.Camera) return;
            var rig = game.Camera;
            int ready = 0;
            foreach (var r in remotes) if (r.Ready) ready++;
            if (ready == 0)
            {
                spectated = null;
                if (!rig.Showcase) { rig.target = game.Player; rig.Showcase = true; }
                return;
            }
            spectateTimer -= Time.unscaledDeltaTime;
            if (spectated && spectated.Ready && spectateTimer > 0f) return;
            for (int k = 0; k < remotes.Count; k++)
            {
                spectateIndex = (spectateIndex + 1) % remotes.Count;
                if (remotes[spectateIndex].Ready) break;
            }
            spectated = remotes[spectateIndex];
            rig.target = spectated.Car;
            rig.Showcase = false;
            spectateTimer = 20f;
        }

        void BuildOverlay()
        {
            var go = new GameObject("NetOverlay", typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1;   // sous le HUD
            overlay = (RectTransform)go.transform;
        }

        Text MakeText(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(overlay, false);
            var t = go.AddComponent<Text>();
            t.font = HudArt.Font;
            t.fontStyle = FontStyle.BoldAndItalic;
            t.alignment = TextAnchor.LowerCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(600f, 40f);
            return t;
        }

        /// <summary>Pseudo de chaque joueur au-dessus de sa voiture (et sa distance quand il est loin).</summary>
        void UpdateTags()
        {
            var cam = UnityEngine.Camera.main;
            bool playing = game.State == GameManager.Phase.Playing || game.State == GameManager.Phase.Countdown;
            bool show = cam && Active && playing && remotes.Count > 0;
            if (!overlay)
            {
                if (!show && !Dedicated) return;
                BuildOverlay();
            }
            while (tags.Count < remotes.Count) tags.Add(MakeText("Tag"));
            float scale = Screen.height / 1080f;
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                var r = i < remotes.Count ? remotes[i] : null;
                bool visible = show && r && r.Ready;
                Vector3 screen = Vector3.zero;
                float d = 0f;
                if (visible)
                {
                    Vector3 world = r.transform.position + Vector3.up * (r.Car.LocalBounds.max.y + 0.55f);
                    screen = cam.WorldToScreenPoint(world);
                    d = screen.z;
                    visible = d > 1f && d < 400f;
                }
                if (tag.gameObject.activeSelf != visible) tag.gameObject.SetActive(visible);
                if (!visible) continue;
                string name = string.IsNullOrEmpty(r.Name) ? "…" : r.Name;
                tag.text = d > 60f ? Loc.F($"{name}  ·  {Mathf.RoundToInt(d / 10f) * 10} m") : name;
                tag.fontSize = Mathf.Max(10, Mathf.RoundToInt(Mathf.Lerp(26f, 16f, Mathf.InverseLerp(10f, 200f, d)) * scale));
                var color = r.Color;
                color.a = Mathf.Clamp01((400f - d) / 80f);
                tag.color = color;
                tag.rectTransform.anchoredPosition = new Vector2(screen.x, screen.y);
            }
            UpdateServerText(scale);
        }

        /// <summary>Serveur dédié : rappel de l'adresse et des joueurs présents, en haut à gauche de la fenêtre.</summary>
        void UpdateServerText(float scale)
        {
            if (!Dedicated) { if (serverText) serverText.gameObject.SetActive(false); return; }
            if (!serverText)
            {
                serverText = MakeText("Server");
                serverText.alignment = TextAnchor.UpperLeft;
                serverText.fontStyle = FontStyle.Bold;
                serverText.color = new Color(1f, 0.86f, 0.5f);
                var rt = serverText.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(1200f, 300f);
            }
            serverText.gameObject.SetActive(true);
            serverTextTimer -= Time.unscaledDeltaTime;
            if (serverTextTimer > 0f) return;
            serverTextTimer = 0.5f;
            serverText.fontSize = Mathf.Max(12, Mathf.RoundToInt(24f * scale));
            serverText.rectTransform.anchoredPosition = new Vector2(28f, -24f) * scale;
            var list = new List<string>();
            foreach (var p in peerList) list.Add(p.name);
            string who = list.Count > 0 ? string.Join(", ", list) : Loc.T("personne pour l'instant", "nobody yet");
            serverText.text = Loc.T(
                $"SERVEUR DÉDIÉ  ·  {ServerName}\nAdresse : {LocalAddress}   (port UDP {Port})\nJoueurs {peerList.Count}/{MaxPlayers} : {who}\nFermer la fenêtre arrête le serveur",
                $"DEDICATED SERVER  ·  {ServerName}\nAddress: {LocalAddress}   (UDP port {Port})\nPlayers {peerList.Count}/{MaxPlayers}: {who}\nClosing the window stops the server");
        }
    }
}
