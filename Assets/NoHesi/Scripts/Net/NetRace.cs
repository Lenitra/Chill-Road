using System.Collections.Generic;
using UnityEngine;

namespace NoHesi
{
    /// <summary>
    /// Séance de course à plusieurs (partie en réseau) : une seule à la fois par serveur. Le serveur la tient et la
    /// diffuse en entier, plusieurs fois par seconde, tant qu'elle existe (<see cref="NetMsg.Race"/>) ; chaque joueur
    /// répète sa réponse dans l'état de sa voiture. Rien n'est à retransmettre : un paquet perdu est remplacé par le
    /// suivant.
    /// Déroulé : un joueur propose une course (menu d'avant-course, <see cref="RaceSetup"/>) ; le serveur ouvre la
    /// séance et les autres reçoivent l'invitation (<see cref="InviteTime"/> s au plus, moins si tous ont répondu) ;
    /// ceux qui ont accepté sont posés sur la grille pour un décompte commun, réglé sur l'horloge du serveur ; chacun
    /// court avec la logique locale de <see cref="Races"/> et remonte ses portes franchies, son avancement et son temps
    /// final ; le serveur en tire le classement. La course se termine quand tous sont arrivés ou ont abandonné, ou un
    /// moment après le premier arrivé (<see cref="FinishDelay"/>) ; le classement final reste diffusé quelques secondes.
    /// Cette classe est la séance elle-même : celle que tient le serveur (méthodes « Host »), ou sa copie chez un
    /// joueur qui a rejoint la partie (<see cref="Read"/>).
    /// </summary>
    public sealed class NetRace
    {
        /// <summary>Durée de l'invitation (s) ; elle s'arrête dès que tous les invités ont répondu.</summary>
        public const float InviteTime = 15f;
        /// <summary>Du placement sur la grille au départ (s) : le temps de recevoir la séance, puis le décompte.</summary>
        public const float GridTime = 5f;
        /// <summary>Diffusion du classement final (s) ; plus court pour un coureur seul, qui n'a pas de tableau.</summary>
        public const float ResultsTime = 12f, AloneResultsTime = 3f;
        /// <summary>Un coureur dont on ne sait toujours rien ce délai (s) après le départ est compté comme abandon.</summary>
        public const float StartGrace = 10f;
        /// <summary>Voiture imposée : aucune, chacun garde la sienne.</summary>
        public const byte FreeCar = 255;
        /// <summary>Niveaux de trafic d'une course : 0 normal, 1 léger, 2 aucun (<see cref="RaceSetup.TrafficLevel"/>).</summary>
        public const int TrafficLevels = 3;
        public const int MaxEntries = NetProtocol.MaxPlayersLimit + 1;

        /// <summary>Un joueur dans la séance.</summary>
        public class Entry
        {
            public byte id;
            public string name = "";
            public NetRaceStatus status;
            /// <summary>Portes franchies, et part de la course déjà couverte (0-1).</summary>
            public int gates;
            public float progress;
            /// <summary>Temps final (s) une fois arrivé.</summary>
            public float time;
            /// <summary>Place au classement (1 : en tête) ; 0 : non classé (invité, abandon).</summary>
            public int rank;

            /// <summary>Sur la grille ou en course : ni arrivé, ni abandon.</summary>
            public bool InRace => status == NetRaceStatus.Accepted || status == NetRaceStatus.Racing;
        }

        /// <summary>Numéro de la séance (1 à 255 ; jamais 0, qui veut dire « aucune »).</summary>
        public byte Id { get; private set; }
        public NetRacePhase Phase { get; private set; }
        /// <summary>Tracé : indice dans <see cref="Races.All"/>.</summary>
        public int Track { get; private set; }
        /// <summary>Trafic pour les coureurs : 0 normal, 1 léger, 2 aucun.</summary>
        public int Traffic { get; private set; }
        /// <summary>Voiture imposée (indice du garage), ou <see cref="FreeCar"/>.</summary>
        public int Car { get; private set; }
        /// <summary>Joueur qui a lancé la course, et numéro de sa proposition (il la reconnaît ainsi).</summary>
        public byte Launcher { get; private set; }
        public byte Seq { get; private set; }
        /// <summary>
        /// Heure du serveur (ms). Invitation : sa fin au plus tard ; ensuite : l'instant du départ.
        /// </summary>
        public uint StartMs { get; private set; }
        /// <summary>
        /// Heure du serveur (ms), 0 tant qu'elle n'est pas fixée. En course : fin imposée, une fois le premier arrivé ;
        /// résultats : fermeture de la séance.
        /// </summary>
        public uint EndMs { get; private set; }
        /// <summary>Joueurs de la séance, dans l'ordre de la grille (le lanceur en tête). Après l'invitation : les seuls coureurs.</summary>
        public readonly List<Entry> Entries = new List<Entry>();
        public bool Exists => Phase != NetRacePhase.None;
        /// <summary>Serveur : la séance vient de changer d'étape ou de composition (à diffuser sans attendre).</summary>
        public bool Changed;
        /// <summary>Copie : heure du serveur (ms) du dernier état reçu ; rien de plus ancien n'est pris en compte.</summary>
        public uint Stamp { get; private set; }

        byte lastId;
        bool stamped;
        readonly List<Entry> scratch = new List<Entry>();

        public Entry Find(byte id)
        {
            foreach (var e in Entries) if (e.id == id) return e;
            return null;
        }

        /// <summary>Nombre de joueurs classés (sur la grille, en course ou arrivés).</summary>
        public int Ranked
        {
            get
            {
                int n = 0;
                foreach (var e in Entries) if (e.rank > 0) n++;
                return n;
            }
        }

        /// <summary>Le premier arrivé, s'il y en a un.</summary>
        public Entry Winner
        {
            get
            {
                foreach (var e in Entries) if (e.rank == 1 && e.status == NetRaceStatus.Finished) return e;
                return null;
            }
        }

        /// <summary>Délai (s) laissé aux autres après le premier arrivé : la moitié de son temps, entre 30 s et 1 min 30.</summary>
        public static float FinishDelay(float winnerTime) => Mathf.Clamp(winnerTime * 0.5f, 30f, 90f);

        /// <summary>Plus de séance (la numérotation continue).</summary>
        void Close()
        {
            Phase = NetRacePhase.None;
            Entries.Clear();
            Changed = true;
        }

        /// <summary>Partie quittée : tout est oublié.</summary>
        public void Reset()
        {
            Close();
            Changed = false;
            lastId = 0;
            stamped = false;
        }

        static bool Before(uint a, uint b) => (int)(a - b) < 0;
        static uint After(uint now, float seconds) => now + (uint)Mathf.RoundToInt(seconds * 1000f);

        // ------------------------------------------------------------------ serveur

        /// <summary>Ouvre la séance : le lanceur y est d'office, les autres joueurs sont à inviter (<see cref="HostInvite"/>).</summary>
        public void HostOpen(byte launcher, string name, byte seq, int track, int traffic, int car, uint now)
        {
            do lastId++; while (lastId == 0);
            Id = lastId;
            Phase = NetRacePhase.Invite;
            Track = track;
            Traffic = traffic;
            Car = car;
            Launcher = launcher;
            Seq = seq;
            StartMs = After(now, InviteTime);
            EndMs = 0;
            Entries.Clear();
            Entries.Add(new Entry { id = launcher, name = name ?? "", status = NetRaceStatus.Accepted });
            Changed = true;
        }

        /// <summary>Invite un joueur (présent à l'ouverture, ou arrivé dans la partie pendant l'invitation).</summary>
        public void HostInvite(byte id, string name)
        {
            if (Phase != NetRacePhase.Invite || Find(id) != null || Entries.Count >= MaxEntries) return;
            Entries.Add(new Entry { id = id, name = name ?? "", status = NetRaceStatus.Invited });
            Changed = true;
        }

        /// <summary>Ce qu'un joueur dit de lui pour la séance <paramref name="raceId"/> : sa réponse à l'invitation, puis sa course.</summary>
        public void HostReport(byte player, byte raceId, NetRaceStatus status, int gates, float progress, float time)
        {
            if (!Exists || raceId != Id) return;
            var e = Find(player);
            if (e == null) return;
            if (Phase == NetRacePhase.Invite)
            {
                if (e.status != NetRaceStatus.Invited || status != NetRaceStatus.Accepted && status != NetRaceStatus.Declined) return;
                e.status = status;
                Changed = true;
            }
            else if (Phase != NetRacePhase.Results && e.InRace)
            {
                // Arrivée et abandon sont définitifs ; l'avancement ne recule pas
                if (status == NetRaceStatus.Racing)
                {
                    e.status = NetRaceStatus.Racing;
                    e.gates = Mathf.Max(e.gates, gates);
                    e.progress = Mathf.Max(e.progress, Mathf.Clamp01(progress));
                }
                else if (status == NetRaceStatus.Finished && time > 0f && Phase == NetRacePhase.Running)
                {
                    e.status = NetRaceStatus.Finished;
                    e.time = time;
                    e.gates = Mathf.Max(e.gates, gates);
                    e.progress = 1f;
                    Changed = true;
                }
                else if (status == NetRaceStatus.Abandoned || status == NetRaceStatus.Declined)
                {
                    e.status = NetRaceStatus.Abandoned;
                    Changed = true;
                }
            }
        }

        /// <summary>
        /// Un joueur a quitté la partie. Pendant l'invitation : il sort de la séance, qui est annulée si c'était le
        /// lanceur. Ensuite : il est compté comme abandon, et la course continue pour les autres.
        /// </summary>
        public void HostLeave(byte player)
        {
            var e = Exists ? Find(player) : null;
            if (e == null) return;
            if (Phase == NetRacePhase.Invite)
            {
                if (player == Launcher) { Close(); return; }
                Entries.Remove(e);
            }
            else if (e.InRace) e.status = NetRaceStatus.Abandoned;
            Changed = true;
        }

        /// <summary>Fait avancer la séance : fin de l'invitation, départ, classement, fin de la course, fermeture.</summary>
        public void HostTick(uint now)
        {
            switch (Phase)
            {
                case NetRacePhase.Invite:
                {
                    bool waiting = false;
                    foreach (var e in Entries) waiting |= e.status == NetRaceStatus.Invited;
                    if (waiting && Before(now, StartMs)) break;
                    // Seuls restent ceux qui ont accepté : ils vont sur la grille
                    Entries.RemoveAll(e => e.status != NetRaceStatus.Accepted);
                    if (Entries.Count == 0) { Close(); break; }
                    Phase = NetRacePhase.Countdown;
                    StartMs = After(now, GridTime);
                    Rank();
                    Changed = true;
                    break;
                }
                case NetRacePhase.Countdown:
                {
                    int left = 0;
                    foreach (var e in Entries) if (e.InRace) left++;
                    if (left == 0) { Close(); break; }
                    Rank();
                    if (Before(now, StartMs)) break;
                    Phase = NetRacePhase.Running;
                    Changed = true;
                    break;
                }
                case NetRacePhase.Running:
                {
                    int racing = 0;
                    float first = 0f;
                    foreach (var e in Entries)
                    {
                        // Jamais parti (paquets perdus, joueur occupé au moment du départ)
                        if (e.status == NetRaceStatus.Accepted && !Before(now, After(StartMs, StartGrace))) e.status = NetRaceStatus.Abandoned;
                        if (e.InRace) racing++;
                        if (e.status == NetRaceStatus.Finished && (first <= 0f || e.time < first)) first = e.time;
                    }
                    if (first > 0f && EndMs == 0)
                    {
                        EndMs = After(now, FinishDelay(first));
                        if (EndMs == 0) EndMs = 1;
                        Changed = true;
                    }
                    if (racing == 0 || EndMs != 0 && !Before(now, EndMs))
                    {
                        // Délai dépassé : ceux qui roulent encore ne sont pas classés
                        foreach (var e in Entries) if (e.InRace) e.status = NetRaceStatus.Abandoned;
                        Phase = NetRacePhase.Results;
                        EndMs = After(now, Entries.Count > 1 ? ResultsTime : AloneResultsTime);
                        Changed = true;
                    }
                    Rank();
                    break;
                }
                case NetRacePhase.Results:
                    if (!Before(now, EndMs)) Close();
                    break;
            }
        }

        static readonly System.Comparison<Entry> ByRank = Compare;

        /// <summary>Classement : les arrivés par temps, puis ceux qui roulent par portes franchies et avancement ; un abandon n'est pas classé.</summary>
        void Rank()
        {
            scratch.Clear();
            scratch.AddRange(Entries);
            scratch.Sort(ByRank);
            int rank = 0;
            foreach (var e in scratch) e.rank = Group(e) < 2 ? ++rank : 0;
        }

        static int Group(Entry e) => e.status == NetRaceStatus.Finished ? 0 : e.InRace ? 1 : 2;

        static int Compare(Entry a, Entry b)
        {
            int ga = Group(a), gb = Group(b);
            if (ga != gb) return ga.CompareTo(gb);
            if (ga == 0 && a.time != b.time) return a.time.CompareTo(b.time);
            if (ga == 1)
            {
                if (a.gates != b.gates) return b.gates.CompareTo(a.gates);
                if (a.progress != b.progress) return b.progress.CompareTo(a.progress);
            }
            return a.id.CompareTo(b.id);
        }

        /// <summary>Écrit la séance (message <see cref="NetMsg.Race"/>, après l'en-tête) : 20 octets, puis 10 par joueur.</summary>
        public void Write(NetWriter w, uint now)
        {
            w.UInt(now);
            w.Byte(Id);
            w.Byte((byte)Phase);
            w.Byte((byte)Track);
            w.Byte((byte)Traffic);
            w.Byte((byte)Car);
            w.Byte(Launcher);
            w.Byte(Seq);
            w.UInt(StartMs);
            w.UInt(EndMs);
            w.Byte((byte)Entries.Count);
            foreach (var e in Entries)
            {
                w.Byte(e.id);
                w.Byte((byte)e.status);
                w.Byte((byte)Mathf.Clamp(e.gates, 0, 255));
                w.UShort((ushort)Mathf.RoundToInt(Mathf.Clamp01(e.progress) * 65535f));
                w.UInt((uint)Mathf.RoundToInt(Mathf.Max(0f, e.time) * 1000f));
                w.Byte((byte)Mathf.Clamp(e.rank, 0, 255));
            }
        }

        // ------------------------------------------------------------------ copie (partie rejointe)

        /// <summary>
        /// Séance reçue du serveur. Faux si le message est invalide ou plus ancien que le dernier pris en compte.
        /// Un joueur déjà connu garde son entrée (et son pseudo, que le message ne porte pas).
        /// </summary>
        public bool Read(NetReader r, out uint ms)
        {
            ms = r.UInt();
            byte id = r.Byte();
            int phase = r.Byte(), track = r.Byte(), traffic = r.Byte(), car = r.Byte();
            byte launcher = r.Byte(), seq = r.Byte();
            uint start = r.UInt(), end = r.UInt();
            int n = r.Byte();
            if (!r.Ok || id == 0 || phase < (int)NetRacePhase.Invite || phase > (int)NetRacePhase.Results || traffic >= TrafficLevels || n > MaxEntries) return false;
            if (stamped && (int)(ms - Stamp) <= 0) return false;
            bool same = Exists && id == Id;
            scratch.Clear();
            for (int i = 0; i < n; i++)
            {
                byte player = r.Byte();
                int status = r.Byte(), gates = r.Byte();
                float progress = r.UShort() / 65535f;
                float time = r.UInt() / 1000f;
                int rank = r.Byte();
                if (!r.Ok || status > (int)NetRaceStatus.Abandoned) return false;
                var e = (same ? Find(player) : null) ?? new Entry { id = player };
                e.status = (NetRaceStatus)status;
                e.gates = gates;
                e.progress = progress;
                e.time = time;
                e.rank = rank;
                scratch.Add(e);
            }
            Stamp = ms;
            stamped = true;
            Id = id;
            Phase = (NetRacePhase)phase;
            Track = track;
            Traffic = traffic;
            Car = car;
            Launcher = launcher;
            Seq = seq;
            StartMs = start;
            EndMs = end;
            Entries.Clear();
            Entries.AddRange(scratch);
            return true;
        }

        /// <summary>
        /// Le serveur annonce à l'instant <paramref name="ms"/> une autre séance que la nôtre, ou aucune : elle est finie.
        /// Faux si cette annonce est plus ancienne que notre dernier état de la séance.
        /// </summary>
        public bool ReadClosed(uint ms)
        {
            if (!Exists || stamped && (int)(ms - Stamp) <= 0) return false;
            Close();
            Stamp = ms;
            stamped = true;
            return true;
        }
    }
}
