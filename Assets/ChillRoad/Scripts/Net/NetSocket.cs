using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ChillRoad
{
    /// <summary>
    /// Prise UDP non bloquante (IPv4), lue à chaque image depuis le fil principal : aucun fil d'exécution à part.
    /// </summary>
    public sealed class NetSocket : IDisposable
    {
        Socket socket;
        readonly byte[] buffer = new byte[2048];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);

        public int Port { get; private set; }
        public byte[] Buffer => buffer;

        /// <summary>Ouvre la prise sur un port (0 : port libre quelconque). Null si le port est pris.</summary>
        public static NetSocket Open(int port)
        {
            Socket s = null;
            try
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false, EnableBroadcast = true };
                // Windows : un envoi vers un port fermé ne doit pas faire échouer les lectures suivantes
                try { s.IOControl(unchecked((int)0x9804000C), new byte[] { 0, 0, 0, 0 }, null); } catch { }
                s.Bind(new IPEndPoint(IPAddress.Any, port));
                return new NetSocket { socket = s, Port = ((IPEndPoint)s.LocalEndPoint).Port };
            }
            catch (Exception)
            {
                try { s?.Close(); } catch { }
                return null;
            }
        }

        public void Send(NetWriter w, IPEndPoint to)
        {
            if (socket == null || w.Overflow) return;
            try { socket.SendTo(w.Data, 0, w.Length, SocketFlags.None, to); }
            catch (Exception) { /* destinataire injoignable : le prochain envoi réessaiera */ }
        }

        /// <summary>Paquet suivant en attente (dans <see cref="Buffer"/>), faux s'il n'y en a plus.</summary>
        public bool Receive(out int length, out IPEndPoint sender)
        {
            length = 0;
            sender = null;
            if (socket == null) return false;
            for (int guard = 0; guard < 8; guard++)
            {
                try
                {
                    if (socket.Available <= 0) return false;
                    length = socket.ReceiveFrom(buffer, ref from);
                    sender = (IPEndPoint)from;
                    return true;
                }
                catch (SocketException e)
                {
                    // Paquet trop gros ou destinataire précédent injoignable : on passe au suivant
                    if (e.SocketErrorCode == SocketError.WouldBlock) return false;
                }
                catch (Exception) { return false; }
            }
            return false;
        }

        public void Dispose()
        {
            try { socket?.Close(); } catch { }
            socket = null;
        }

        /// <summary>Adresses de diffusion de chaque carte réseau active (plus la diffusion générale et la machine elle-même).</summary>
        public static List<IPAddress> BroadcastAddresses()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] a = ua.Address.GetAddressBytes(), m = ua.IPv4Mask.GetAddressBytes();
                        if (a.Length != 4 || m.Length != 4) continue;
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                        var address = new IPAddress(b);
                        if (!list.Contains(address)) list.Add(address);
                    }
                }
            }
            catch (Exception) { /* la diffusion générale suffit le plus souvent */ }
            return list;
        }

        /// <summary>Adresses IPv4 de cette machine sur ses réseaux (à donner aux joueurs qui veulent rejoindre).</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string text = ua.Address.ToString();
                        // 169.254.x.x : carte sans réseau
                        if (text.StartsWith("169.254.") || list.Contains(text)) continue;
                        // Les adresses de réseau local d'abord (192.168, 10, 172.16-31)
                        if (text.StartsWith("192.168.") || text.StartsWith("10.")) list.Insert(0, text);
                        else list.Add(text);
                    }
                }
            }
            catch (Exception) { }
            return list;
        }
    }
}
