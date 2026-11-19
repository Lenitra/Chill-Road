using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Protocole du jeu en réseau (UDP, sans dépendance). Tout est transmis par états répétés plutôt que par messages
    /// fiables : un paquet perdu est remplacé par le suivant, rien n'est à retransmettre.
    /// - le client envoie l'état de sa voiture 30 fois par seconde (et les véhicules du trafic qu'il a percutés) ;
    /// - le serveur renvoie l'état de toutes les voitures, l'heure et la météo (30 Hz), la liste des joueurs (1 Hz) et
    ///   le trafic qu'il simule (10 Hz, en plusieurs paquets) ;
    /// - recherche sur le réseau local : le client diffuse une question sur les ports du jeu, les serveurs répondent ;
    /// - course à plusieurs : le serveur diffuse la séance en cours (<see cref="NetRace"/>, 5 Hz) tant qu'elle existe, et
    ///   chaque client répète dans son état sa réponse à la séance (et la course qu'il propose de lancer).
    /// Chaque paquet commence par « CR », la version du protocole et le type de message.
    /// </summary>
    public static class NetProtocol
    {
        public const byte Version = 7;
        /// <summary>Port du serveur ; s'il est pris, les suivants sont essayés (et interrogés par la recherche locale).</summary>
        public const int DefaultPort = 47777;
        public const int PortTries = 4;
        public const int DefaultMaxPlayers = 8, MaxPlayersLimit = 16;
        public const int MaxName = 16;
        public const float PlayerRate = 30f, TrafficRate = 10f, RosterRate = 1f, RaceRate = 5f;
        /// <summary>Sans nouvelles pendant ce délai (s) : le joueur ou le serveur est considéré comme parti.</summary>
        public const float Timeout = 6f;
        /// <summary>Retard d'affichage (s) des voitures des autres joueurs et du trafic : le temps d'avoir deux états à relier.</summary>
        public const double PlayerDelay = 0.1, TrafficDelay = 0.22;
        /// <summary>Véhicules du trafic par paquet (33 octets chacun : sous la taille d'un paquet UDP sûr).</summary>
        public const int TrafficPerPacket = 34;
        /// <summary>Paquets au plus pour un envoi complet du trafic à un joueur.</summary>
        public const int TrafficPackets = 8;
        public const int MaxOwned = 8;
        public const byte NoOwner = 255;

        public const int MaxServerName = 32;

        /// <summary>Nom affichable (pseudo, serveur) : sans balises ni caractères de contrôle, 16 caractères au plus pour un pseudo.</summary>
        public static string CleanName(string name, int max = MaxName)
        {
            var sb = new StringBuilder();
            if (name != null)
                foreach (char c in name.Trim())
                {
                    if (c == '<' || c == '>' || char.IsControl(c)) continue;
                    sb.Append(c);
                    if (sb.Length >= max) break;
                }
            return sb.Length > 0 ? sb.ToString() : "Pilote";
        }
    }

    public enum NetMsg : byte
    {
        /// <summary>Recherche locale : « y a-t-il un serveur ? » (diffusion).</summary>
        Query = 1,
        /// <summary>Réponse d'un serveur : identifiant, nom, joueurs présents, places, options (<see cref="NetOptions"/>).</summary>
        Info = 2,
        /// <summary>Demande d'entrée : jeton, pseudo.</summary>
        Hello = 3,
        Welcome = 4,
        Refuse = 5,
        /// <summary>Client → serveur : état de sa voiture, véhicules du trafic qu'il simule, bloc course.</summary>
        State = 6,
        /// <summary>Serveur → clients : toutes les voitures, heure, météo, numéro de la séance de course en cours.</summary>
        Snapshot = 7,
        Roster = 8,
        Traffic = 9,
        Bye = 10,
        /// <summary>Serveur → clients : séance de course en cours (<see cref="NetRace"/>).</summary>
        Race = 11,
    }

    /// <summary>Étapes d'une séance de course à plusieurs (<see cref="NetRace"/>).</summary>
    public enum NetRacePhase : byte
    {
        None = 0,
        /// <summary>Invitation envoyée à tous : chacun accepte ou refuse.</summary>
        Invite = 1,
        /// <summary>Les coureurs sont sur la grille, le départ est fixé (heure du serveur).</summary>
        Countdown = 2,
        Running = 3,
        /// <summary>Course finie : le classement reste diffusé quelques secondes.</summary>
        Results = 4,
    }

    /// <summary>Statut d'un joueur dans une séance de course : sa réponse à l'invitation, puis où il en est.</summary>
    public enum NetRaceStatus : byte
    {
        Invited = 0,
        Accepted = 1,
        Declined = 2,
        Racing = 3,
        Finished = 4,
        Abandoned = 5,
    }

    /// <summary>
    /// Options d'une partie, choisies par le serveur à son ouverture et fixes ensuite : un octet de drapeaux, annoncé
    /// dans Info (recherche locale) et dans Welcome (entrée dans la partie).
    /// </summary>
    public static class NetOptions
    {
        /// <summary>Les voitures des joueurs se percutent ; sans ce drapeau, elles se traversent.</summary>
        public const byte Collisions = 1;
    }

    /// <summary>Motifs de refus d'entrée.</summary>
    public static class NetRefuse
    {
        public const byte Full = 1, Version = 2, Content = 3;
    }

    /// <summary>État d'une voiture de joueur, tel qu'il voyage sur le réseau (39 octets).</summary>
    public struct NetCarState
    {
        public Vector3 pos, vel;
        public Quaternion rot;
        public float steer, rpm01, throttle, skid, skidFront, skidRear, dirt;
        public int gear, carIndex;
        /// <summary>Couleur choisie au garage de tuning (indice du nuancier de <see cref="CarPaint"/>).</summary>
        public byte paint;
        /// <summary>Accessoires du garage de tuning : néons, vitres, pop and bang, flammes (<see cref="CarCosmetics.Look.Pack"/>).</summary>
        public ushort cosm;
        /// <summary>Compteur de téléportations (replacement, départ) : quand il change, la voiture saute sans glisser.</summary>
        public byte teleport;
        public bool lights, braking, handbrake, onRoad, grounded;
        /// <summary>Klaxon tenu, appel de phares en cours : répétés dans chaque état tant qu'ils durent.</summary>
        public bool horn, flash;

        public void Write(NetWriter w)
        {
            w.Vector(pos);
            w.Rotation(rot);
            w.Velocity(vel);
            w.SByte(Mathf.RoundToInt(steer));
            w.Unit(rpm01);
            w.Unit(throttle);
            w.SByte(gear);
            w.Byte((byte)((lights ? 1 : 0) | (braking ? 2 : 0) | (handbrake ? 4 : 0) | (onRoad ? 8 : 0) | (grounded ? 16 : 0)
                | (horn ? 32 : 0) | (flash ? 64 : 0)));
            w.Unit(skid);
            w.Unit(skidFront);
            w.Unit(skidRear);
            w.Unit(dirt);
            w.Byte((byte)Mathf.Clamp(carIndex, 0, 255));
            w.Byte(teleport);
            w.Byte(paint);
            w.Byte((byte)(cosm & 255));
            w.Byte((byte)(cosm >> 8));
        }

        public static NetCarState Read(NetReader r)
        {
            var s = new NetCarState();
            s.pos = r.Vector();
            s.rot = r.Rotation();
            s.vel = r.Velocity();
            s.steer = r.SByte();
            s.rpm01 = r.Unit();
            s.throttle = r.Unit();
            s.gear = r.SByte();
            byte flags = r.Byte();
            s.lights = (flags & 1) != 0;
            s.braking = (flags & 2) != 0;
            s.handbrake = (flags & 4) != 0;
            s.onRoad = (flags & 8) != 0;
            s.grounded = (flags & 16) != 0;
            s.horn = (flags & 32) != 0;
            s.flash = (flags & 64) != 0;
            s.skid = r.Unit();
            s.skidFront = r.Unit();
            s.skidRear = r.Unit();
            s.dirt = r.Unit();
            s.carIndex = r.Byte();
            s.teleport = r.Byte();
            s.paint = r.Byte();
            int low = r.Byte();
            s.cosm = (ushort)(low | r.Byte() << 8);
            return s;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits
    {
        [FieldOffset(0)] public float f;
        [FieldOffset(0)] public uint u;
    }

    /// <summary>Écriture d'un paquet (petit-boutiste). Un paquet trop long est tronqué et signalé par <see cref="Overflow"/>.</summary>
    public sealed class NetWriter
    {
        public readonly byte[] Data = new byte[1400];
        public int Length { get; private set; }
        public bool Overflow { get; private set; }

        public NetWriter Begin(NetMsg msg)
        {
            Length = 0;
            Overflow = false;
            Byte((byte)'C');
            Byte((byte)'R');
            Byte(NetProtocol.Version);
            Byte((byte)msg);
            return this;
        }

        public void Byte(byte v)
        {
            if (Length >= Data.Length) { Overflow = true; return; }
            Data[Length++] = v;
        }

        /// <summary>Réécrit un octet déjà posé (compteur rempli après coup).</summary>
        public void Patch(int at, byte v) => Data[at] = v;

        public void SByte(int v) => Byte((byte)(sbyte)Mathf.Clamp(v, -127, 127));
        public void UShort(ushort v) { Byte((byte)v); Byte((byte)(v >> 8)); }
        public void UInt(uint v) { Byte((byte)v); Byte((byte)(v >> 8)); Byte((byte)(v >> 16)); Byte((byte)(v >> 24)); }
        public void Float(float v) => UInt(new FloatBits { f = v }.u);
        /// <summary>Valeur de 0 à 1 sur un octet.</summary>
        public void Unit(float v) => Byte((byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f));
        public void Vector(Vector3 v) { Float(v.x); Float(v.y); Float(v.z); }

        /// <summary>Vitesse au centimètre par seconde près (±327 m/s).</summary>
        public void Velocity(Vector3 v)
        {
            for (int i = 0; i < 3; i++) UShort((ushort)(short)Mathf.Clamp(Mathf.RoundToInt(v[i] * 100f), -32767, 32767));
        }

        /// <summary>Rotation sur 7 octets : les trois plus petites composantes du quaternion, la quatrième se recalcule.</summary>
        public void Rotation(Quaternion q)
        {
            int largest = 0;
            float max = Mathf.Abs(q.x);
            for (int i = 1; i < 4; i++)
                if (Mathf.Abs(q[i]) > max) { max = Mathf.Abs(q[i]); largest = i; }
            float sign = q[largest] < 0f ? -1f : 1f;
            Byte((byte)largest);
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                float u = Mathf.Clamp01(q[i] * sign / 0.70710678f * 0.5f + 0.5f);
                UShort((ushort)Mathf.RoundToInt(u * 65535f));
            }
        }

        public void String(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            int n = Mathf.Min(bytes.Length, 64);
            Byte((byte)n);
            for (int i = 0; i < n; i++) Byte(bytes[i]);
        }
    }

    /// <summary>
    /// Lecture d'un paquet reçu. Rien n'est supposé sur son contenu : une lecture au-delà de la fin ou un nombre
    /// invalide rend <see cref="Ok"/> faux, et le paquet est alors ignoré par l'appelant.
    /// </summary>
    public sealed class NetReader
    {
        byte[] data;
        int pos, end;
        public bool Ok { get; private set; }
        public byte Version { get; private set; }
        public int Left => end - pos;

        /// <summary>Faux si ce n'est pas un paquet du jeu.</summary>
        public bool Begin(byte[] d, int length, out NetMsg msg)
        {
            data = d;
            pos = 0;
            end = length;
            Ok = true;
            msg = 0;
            if (length < 4 || d[0] != (byte)'C' || d[1] != (byte)'R') return false;
            Version = d[2];
            msg = (NetMsg)d[3];
            pos = 4;
            return true;
        }

        public byte Byte()
        {
            if (pos >= end) { Ok = false; return 0; }
            return data[pos++];
        }

        public int SByte() => (sbyte)Byte();
        public ushort UShort() { int a = Byte(), b = Byte(); return (ushort)(a | b << 8); }
        public uint UInt() { uint a = Byte(), b = Byte(), c = Byte(), d = Byte(); return a | b << 8 | c << 16 | d << 24; }

        public float Float()
        {
            float f = new FloatBits { u = UInt() }.f;
            if (float.IsNaN(f) || float.IsInfinity(f) || Mathf.Abs(f) > 1e6f) { Ok = false; return 0f; }
            return f;
        }

        public float Unit() => Byte() / 255f;
        public Vector3 Vector() { float x = Float(), y = Float(), z = Float(); return new Vector3(x, y, z); }

        public Vector3 Velocity()
        {
            var v = new Vector3();
            for (int i = 0; i < 3; i++) v[i] = (short)UShort() / 100f;
            return v;
        }

        public Quaternion Rotation()
        {
            int largest = Byte();
            if (largest > 3) { Ok = false; return Quaternion.identity; }
            var q = new Quaternion();
            float sum = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                float v = (UShort() / 65535f - 0.5f) * 2f * 0.70710678f;
                q[i] = v;
                sum += v * v;
            }
            q[largest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sum));
            return q;
        }

        public string String()
        {
            int n = Byte();
            if (n > Left) { Ok = false; return ""; }
            string s = Encoding.UTF8.GetString(data, pos, n);
            pos += n;
            return s;
        }
    }

    /// <summary>
    /// File d'états datés d'un objet piloté par le réseau (voiture d'un joueur, véhicule du trafic). On le montre avec un
    /// léger retard, à mi-chemin entre deux états reçus ; faute de nouvelles, il continue un instant sur sa lancée.
    /// </summary>
    public sealed class NetBuffer
    {
        const int Size = 12;
        struct Entry { public double t; public Vector3 p, v; public Quaternion r; }
        readonly Entry[] e = new Entry[Size];
        int count;

        public bool Empty => count == 0;
        public void Clear() => count = 0;

        public void Push(double t, Vector3 p, Quaternion r, Vector3 v)
        {
            if (count > 0 && t <= e[count - 1].t) return;
            if (count == Size)
            {
                System.Array.Copy(e, 1, e, 0, Size - 1);
                count--;
            }
            e[count++] = new Entry { t = t, p = p, r = r, v = v };
        }

        public bool Sample(double t, out Vector3 p, out Quaternion r, out Vector3 v)
        {
            p = v = Vector3.zero;
            r = Quaternion.identity;
            if (count == 0) return false;
            var last = e[count - 1];
            if (t >= last.t)
            {
                // Pas d'état plus récent : on prolonge la trajectoire un quart de seconde au plus
                float ahead = Mathf.Min((float)(t - last.t), 0.25f);
                p = last.p + last.v * ahead;
                r = last.r;
                v = t - last.t > 0.25 ? Vector3.zero : last.v;
                return true;
            }
            for (int i = count - 2; i >= 0; i--)
            {
                if (t < e[i].t) continue;
                float u = (float)((t - e[i].t) / (e[i + 1].t - e[i].t));
                p = Vector3.LerpUnclamped(e[i].p, e[i + 1].p, u);
                r = Quaternion.Slerp(e[i].r, e[i + 1].r, u);
                v = Vector3.LerpUnclamped(e[i].v, e[i + 1].v, u);
                return true;
            }
            p = e[0].p;
            r = e[0].r;
            v = e[0].v;
            return true;
        }
    }

    /// <summary>
    /// Écart entre l'horloge d'une autre machine et la nôtre, estimé au fil des paquets reçus : permet de replacer ses
    /// états sur notre ligne de temps malgré les irrégularités du réseau.
    /// </summary>
    public struct NetClock
    {
        double offset;
        bool set;

        /// <summary>Date locale correspondant à une date de l'autre machine (reçue à l'instant <paramref name="now"/>).</summary>
        public double ToLocal(double remote, double now)
        {
            double sample = now - remote;
            if (!set || System.Math.Abs(sample - offset) > 0.5) { offset = sample; set = true; }
            else offset += (sample - offset) * 0.03;
            return remote + offset;
        }

        /// <summary>Date locale d'une date de l'autre machine, d'après l'écart déjà estimé (départ d'une course).</summary>
        public double At(double remote) => remote + offset;
    }
}
