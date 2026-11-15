# Faux joueur / faux serveur pour essayer le protocole réseau de Chill Road sans second exemplaire du jeu.
#   python netbot.py client <port> <x> <y> <z> <yaw°> [secondes] [--claim]
#   python netbot.py server <port> <x> <y> <z> <yaw°> [secondes]
import math, socket, struct, sys, time

VERSION = 1
QUERY, INFO, HELLO, WELCOME, REFUSE, STATE, SNAPSHOT, ROSTER, TRAFFIC, BYE = range(1, 11)


def head(msg):
    return b"CR" + bytes([VERSION, msg])


def s8(v):
    return struct.pack("<b", max(-127, min(127, int(round(v)))))


def string(s):
    b = s.encode("utf-8")[:64]
    return bytes([len(b)]) + b


def rotation(yaw_deg):
    h = math.radians(yaw_deg) / 2
    q = [0.0, math.sin(h), 0.0, math.cos(h)]
    largest = max(range(4), key=lambda i: abs(q[i]))
    sign = -1.0 if q[largest] < 0 else 1.0
    out = bytes([largest])
    for i in range(4):
        if i == largest:
            continue
        u = max(0.0, min(1.0, q[i] * sign / 0.70710678 * 0.5 + 0.5))
        out += struct.pack("<H", int(round(u * 65535)))
    return out


def velocity(v):
    return b"".join(struct.pack("<h", max(-32767, min(32767, int(round(c * 100))))) for c in v)


def car_state(pos, yaw, vel, car=0, teleport=0, lights=True, braking=False, throttle=0.6, skid=0.0):
    flags = (1 if lights else 0) | (2 if braking else 0) | 8 | 16
    return (struct.pack("<3f", *pos) + rotation(yaw) + velocity(vel) + s8(0) + bytes([120, int(throttle * 255)]) + s8(3)
            + bytes([flags, int(skid * 255), 0, int(skid * 255), 0, car, teleport]))


class Reader:
    def __init__(self, data):
        self.d, self.p = data, 4

    def take(self, fmt):
        n = struct.calcsize(fmt)
        v = struct.unpack_from(fmt, self.d, self.p)
        self.p += n
        return v if len(v) > 1 else v[0]

    def string(self):
        n = self.take("<B")
        s = self.d[self.p:self.p + n].decode("utf-8")
        self.p += n
        return s

    def state(self):
        pos = self.take("<3f")
        self.p += 7
        vel = [c / 100 for c in self.take("<3h")]
        steer, rpm, thr, gear, flags, skid, sf, sr, dirt, car, tp = self.take("<bBBbBBBBBBB")
        return dict(pos=pos, vel=vel, car=car, teleport=tp, flags=flags, gear=gear)


def now_ms(t0):
    return int((time.time() - t0) * 1000) & 0xFFFFFFFF


def forward(yaw):
    r = math.radians(yaw)
    return (math.sin(r), 0.0, math.cos(r))


def client(port, x, y, z, yaw, seconds, claim):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(0.005)
    server = ("127.0.0.1", port)
    t0 = time.time()
    sock.sendto(head(QUERY), server)
    nonce = 0x1234ABCD
    my_id, counts, players, traffic, roster, info = None, {}, {}, {}, {}, None
    last_hello = last_state = 0.0
    snapshots_extra = None
    f = forward(yaw)
    claimed = None
    while time.time() - t0 < seconds:
        t = time.time() - t0
        if my_id is None and t - last_hello > 0.4:
            last_hello = t
            sock.sendto(head(HELLO) + struct.pack("<I", nonce) + string("Robot<b>"), server)
        if my_id is not None and t - last_state > 1 / 30:
            last_state = t
            # Va-et-vient de 30 m devant la voiture de l'hôte, à 8 m/s ; téléportation à mi-parcours
            phase = (t * 8.0) % 60.0
            d = 12.0 + (phase if phase < 30 else 60 - phase)
            v = 8.0 if phase < 30 else -8.0
            side = 3.5
            pos = (x + f[0] * d + f[2] * side, y + 0.3, z + f[2] * d - f[0] * side)
            tp = 1 if t > seconds * 0.5 else 0
            pkt = head(STATE) + struct.pack("<I", now_ms(t0)) + car_state(pos, yaw, (f[0] * v, 0, f[2] * v), car=3, teleport=tp, braking=v < 0, skid=0.8)
            if claim and traffic and claimed is None and t > 4:
                claimed = sorted(traffic)[0]
            if claimed is not None:
                cp = (x + f[0] * 20, y + 2.0 + math.sin(t * 2), z + f[2] * 20)
                pkt += bytes([1]) + struct.pack("<H", claimed) + struct.pack("<3f", *cp) + rotation(t * 90 % 360) + velocity((0, 0, 0))
            else:
                pkt += bytes([0])
            sock.sendto(pkt, server)
        try:
            data, _ = sock.recvfrom(4096)
        except (socket.timeout, ConnectionResetError):
            continue
        if data[:2] != b"CR":
            continue
        msg = data[3]
        counts[msg] = counts.get(msg, 0) + 1
        r = Reader(data)
        if msg == INFO:
            info = (r.take("<I"), r.string(), r.take("<B"), r.take("<B"))
        elif msg == WELCOME:
            echoed, my_id = r.take("<I"), r.take("<B")
            print("WELCOME id", my_id, "nonce ok", echoed == nonce, "serveur", repr(r.string()), "max", r.take("<B"))
        elif msg == REFUSE:
            print("REFUSE", r.take("<I"), r.take("<B"))
        elif msg == SNAPSHOT:
            ms, hour, stormy, storm, rain, wet, n = r.take("<I"), r.take("<f"), *r.take("<4B"), r.take("<B")
            snapshots_extra = (round(hour, 2), stormy, storm, rain, wet)
            for _ in range(n):
                pid, pms = r.take("<B"), r.take("<I")
                players[pid] = r.state()
            assert r.p == len(data), "snapshot: taille inattendue"
        elif msg == ROSTER:
            roster = {}
            for _ in range(r.take("<B")):
                pid = r.take("<B")
                roster[pid] = r.string()
        elif msg == TRAFFIC:
            ms, frame, chunk, chunks, n = r.take("<I"), r.take("<H"), r.take("<B"), r.take("<B"), r.take("<B")
            if chunk == 0:
                traffic_frame = {}
            for _ in range(n):
                cid, model = r.take("<H"), r.take("<B")
                rgb = r.take("<3B")
                flags, owner = r.take("<B"), r.take("<B")
                pos = r.take("<3f")
                r.p += 7 + 6
                traffic[cid] = dict(kind=model >> 6, model=model & 63, crashed=flags & 1, owner=owner, pos=tuple(round(c, 1) for c in pos))
            assert r.p == len(data), "trafic: taille inattendue"
    sock.sendto(head(BYE), server)
    names = {INFO: "info", WELCOME: "welcome", SNAPSHOT: "snapshot", ROSTER: "roster", TRAFFIC: "traffic", BYE: "bye", REFUSE: "refuse"}
    print("paquets:", {names.get(k, k): v for k, v in counts.items()})
    print("info:", info)
    print("roster:", roster)
    print("heure/météo:", snapshots_extra)
    for pid, s in players.items():
        print(f"joueur {pid}: pos {tuple(round(c, 1) for c in s['pos'])} vel {s['vel']} voiture {s['car']} tp {s['teleport']}")
    print("trafic vu:", len(traffic), "véhicules ; exemples:", list(traffic.items())[:3])
    if claimed is not None:
        print("véhicule revendiqué:", claimed, traffic.get(claimed))


def server(port, x, y, z, yaw, seconds):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("0.0.0.0", port))
    sock.settimeout(0.005)
    t0 = time.time()
    peer, got, last = None, {}, {}
    last_snap = last_roster = last_traffic = 0.0
    frame = 0
    f = forward(yaw)
    states = 0
    owned_seen = set()
    while time.time() - t0 < seconds:
        t = time.time() - t0
        try:
            data, addr = sock.recvfrom(4096)
            if data[:2] == b"CR":
                msg = data[3]
                got[msg] = got.get(msg, 0) + 1
                r = Reader(data)
                if msg == QUERY:
                    sock.sendto(head(INFO) + struct.pack("<I", 777) + string("Serveur robot") + bytes([1, 8]), addr)
                elif msg == HELLO:
                    nonce, name = r.take("<I"), r.string()
                    if peer is None:
                        print("HELLO de", name, addr)
                    peer = addr
                    sock.sendto(head(WELCOME) + struct.pack("<I", nonce) + bytes([2]) + string("Serveur robot") + bytes([8]), addr)
                elif msg == STATE:
                    ms = r.take("<I")
                    last = r.state()
                    states += 1
                    for _ in range(r.take("<B")):
                        owned_seen.add(r.take("<H"))
                        r.p += 12 + 7 + 6
                elif msg == BYE:
                    print("BYE reçu")
                    peer = None
        except (socket.timeout, ConnectionResetError):
            pass
        if peer is None:
            continue
        ms = now_ms(t0)
        if t - last_snap > 1 / 30:
            last_snap = t
            # Deux faux joueurs : l'un tourne en rond devant le joueur, l'autre est garé à côté (phares allumés)
            a = t * 0.8
            cx, cz = x + f[0] * 25, z + f[2] * 25
            p0 = (cx + math.sin(a) * 9, y + 0.3, cz + math.cos(a) * 9)
            v0 = (math.cos(a) * 7.2, 0, -math.sin(a) * 7.2)
            p1 = (x + f[2] * 4, y + 0.3, z - f[0] * 4)
            pkt = head(SNAPSHOT) + struct.pack("<If", ms, 22.5) + bytes([1, 255, 200, 255, 2])
            pkt += bytes([0]) + struct.pack("<I", ms) + car_state(p0, math.degrees(a) + 90, v0, car=1, skid=0.9)
            pkt += bytes([1]) + struct.pack("<I", ms) + car_state(p1, yaw, (0, 0, 0), car=5, throttle=0.0, braking=True)
            sock.sendto(pkt, peer)
        if t - last_roster > 1.0:
            last_roster = t
            names = [(0, "Hôte robot"), (1, "Garé"), (2, "Moi")] + ([(5, "Tardif")] if 6 < t < 12 else [])
            sock.sendto(head(ROSTER) + bytes([len(names)]) + b"".join(bytes([i]) + string(n) for i, n in names), peer)
        if t - last_traffic > 0.1:
            last_traffic = t
            frame = (frame + 1) & 0xFFFF
            cars = []
            # Trois véhicules qui remontent la rue en file, un quatrième qui disparaît après 10 s
            for k in range(4 if t < 10 else 3):
                d = 40 + k * 14 - (t * 6) % 30
                pos = (x + f[0] * d - f[2] * 3.5, y + 0.05, z + f[2] * d + f[0] * 3.5)
                crashed = 1 if (k + 21) in owned_seen else 0
                cars.append(struct.pack("<HB", 21 + k, (1 << 6 | 0) if k == 2 else k) + bytes([200, 40 + k * 60, 40]) + bytes([crashed, 2 if crashed else 255])
                            + struct.pack("<3f", *pos) + rotation(yaw + 180) + velocity((-f[0] * 6, 0, -f[2] * 6)))
            sock.sendto(head(TRAFFIC) + struct.pack("<IHBBB", ms, frame, 0, 1, len(cars)) + b"".join(cars), peer)
    if peer:
        sock.sendto(head(BYE), peer)
    print("reçus:", got, "états:", states)
    print("dernier état du joueur:", {k: (tuple(round(c, 1) for c in v) if isinstance(v, (tuple, list)) else v) for k, v in last.items()})
    print("véhicules revendiqués par le joueur:", sorted(owned_seen))


if __name__ == "__main__":
    mode, port = sys.argv[1], int(sys.argv[2])
    x, y, z, yaw = (float(a) for a in sys.argv[3:7])
    rest = [a for a in sys.argv[7:] if not a.startswith("--")]
    seconds = float(rest[0]) if rest else 15.0
    if mode == "client":
        client(port, x, y, z, yaw, seconds, "--claim" in sys.argv)
    else:
        server(port, x, y, z, yaw, seconds)
