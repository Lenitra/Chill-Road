# Faux joueur / faux serveur pour essayer le protocole réseau de Chill Road sans second exemplaire du jeu.
#   python netbot.py client <port> <x> <y> <z> <yaw°> [secondes] [options]
#   python netbot.py server <port> <x> <y> <z> <yaw°> [secondes] [options]
# Protocole : version 5, celle de NetProtocol.Version (à changer ici, VERSION, chaque fois qu'elle change là-bas).
# Il parle en UDP direct, sur la machine même (127.0.0.1) : le faux joueur rejoint le jeu qui héberge sur <port>, le
# faux serveur attend sur <port> que le jeu le rejoigne par son adresse. Il ne connaît pas les codes d'invitation :
# le relais n'a pas changé le protocole, seulement le transport, et s'essaie avec un second exemplaire du jeu
# (« Chill Road.exe -nettest -join CODE », voir DebugTools/NetTestBot.cs).
# Options du faux joueur :
#   --claim : il revendique un véhicule du trafic (comme s'il l'avait percuté).
#   --wait=N : il n'envoie son état qu'après N secondes (le serveur, qui ignore où il est, lui envoie tout le trafic).
#   --name=X, --car=N : son pseudo, sa voiture (indice du garage).
#   --race=accept|decline|ignore : sa réponse à une invitation de course (par défaut il l'ignore : elle expire).
#   --answer=N : délai (s) avant cette réponse.
#   --path=FICHIER : tracé de la course (JSON : points, gates, laps), qu'il suit une fois le départ donné.
#   --speed=V : sa vitesse le long du tracé (m/s) ; son temps final est exactement longueur / vitesse.
#   --abandon=F : il abandonne à cette part de la course (0-1). --quit=F : il quitte la partie à cette part de la
#     course (--silent : sans prévenir, le serveur attend alors son délai d'absence).
#   --propose=TRACÉ,TRAFIC,VOITURE : il propose une course (voiture 255 : libre) ; --propose-at=N : après N secondes.
#   --horn=A-B[,C-D…] : il klaxonne de A à B secondes (puis de C à D…). --flash=A-B[,C-D…] : appels de phares, de même.
#     Il annonce aussi le klaxon et les appels de phares des autres joueurs, à chaque changement.
#   --still : il reste à la position donnée, à l'arrêt (--lights=0 : phares éteints).
#   --route=FICHIER : hors course, il parcourt ce trajet (JSON : points) à --speed m/s et s'arrête au bout ;
#     --route-at=N : il part après N secondes (il attend au premier point). --leave=N : il quitte la partie après N secondes.
# Options du faux serveur :
#   --nocollisions : il annonce une partie sans collisions entre joueurs.
#   --race=TRACÉ,TRAFIC,VOITURE : son hôte (joueur 0) lance une course ; --race-at=N : après N secondes.
#   --horn=A-B[,…], --flash=A-B[,…] : klaxon et appels de phares de son hôte. Il annonce ceux du joueur.
#   --path=FICHIER, --speed=V : tracé et vitesse de l'hôte dans cette course.
#   --finish-delay=N : délai laissé aux autres après le premier arrivé (par défaut celui du jeu).
#   --results=N : durée de diffusion des résultats (s).
# Le fichier du tracé s'obtient depuis le jeu (Races.All[i] : path, portes) ; les distances des portes se comptent
# depuis la ligne de départ, le long d'un tour.
import bisect, json, math, socket, struct, sys, time

VERSION = 5
QUERY, INFO, HELLO, WELCOME, REFUSE, STATE, SNAPSHOT, ROSTER, TRAFFIC, BYE, RACE = range(1, 12)
# Octet d'options de la partie (dans Info et Welcome) : bit 0, les voitures des joueurs se percutent
OPT_COLLISIONS = 1
# Séance de course : étapes, statut d'un joueur
P_NONE, P_INVITE, P_COUNTDOWN, P_RUNNING, P_RESULTS = range(5)
S_INVITED, S_ACCEPTED, S_DECLINED, S_RACING, S_FINISHED, S_ABANDONED = range(6)
PHASES = ["aucune", "invitation", "décompte", "course", "résultats"]
STATUS = ["invité", "accepté", "refusé", "en course", "arrivé", "abandon"]
FREE_CAR = 255
# Octet de drapeaux de l'état d'une voiture : klaxon tenu, appel de phares en cours
F_HORN, F_FLASH = 32, 64
INVITE_TIME, GRID_TIME, START_BACK, GRID_SPACING = 15.0, 5.0, 4.0, 9.0


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


def car_state(pos, yaw, vel, car=0, teleport=0, lights=True, braking=False, throttle=0.6, skid=0.0, paint=0, horn=False, flash=False):
    # 37 octets, comme NetCarState.Write : position, rotation, vitesse, braquage, régime, gaz, rapport, drapeaux
    # (1 phares, 2 frein, 4 frein à main, 8 sur la route, 16 roues au sol, 32 klaxon, 64 appel de phares),
    # glisse (toutes roues, avant, arrière), poussière, voiture, compteur de téléportations, peinture
    flags = (1 if lights else 0) | (2 if braking else 0) | 8 | 16 | (F_HORN if horn else 0) | (F_FLASH if flash else 0)
    return (struct.pack("<3f", *pos) + rotation(yaw) + velocity(vel) + s8(0) + bytes([120, int(throttle * 255)]) + s8(3)
            + bytes([flags, int(skid * 255), 0, int(skid * 255), 0, car, teleport & 255, paint]))


def race_block(race_id=0, status=S_INVITED, gates=0, progress=0.0, time_s=0.0, seq=0, track=0, traffic=0, car=FREE_CAR):
    # 13 octets, à la suite de l'état de la voiture dans State : séance à laquelle on répond, statut, portes franchies,
    # avancement (0-65535), temps final (ms) ; puis la course proposée : numéro (0 : aucune), tracé, trafic, voiture
    return struct.pack("<BBBHIBBBB", race_id, status, min(gates, 255), int(round(max(0.0, min(1.0, progress)) * 65535)),
                       int(round(time_s * 1000)), seq, track, traffic, car)


def race_message(ms, race):
    # Message Race : 20 octets, puis 10 par joueur de la séance
    out = head(RACE) + struct.pack("<IBBBBBBBIIB", ms, race["id"], race["phase"], race["track"], race["traffic"], race["car"],
                                   race["launcher"], race["seq"], race["start"] & 0xFFFFFFFF, race["end"] & 0xFFFFFFFF, len(race["entries"]))
    for e in race["entries"]:
        out += struct.pack("<BBBHIB", e["id"], e["status"], min(e["gates"], 255), int(round(max(0.0, min(1.0, e["progress"])) * 65535)),
                           int(round(e["time"] * 1000)), e["rank"])
    return out


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
        steer, rpm, thr, gear, flags, skid, sf, sr, dirt, car, tp, paint = self.take("<bBBbBBBBBBBB")
        return dict(pos=pos, vel=vel, car=car, teleport=tp, flags=flags, gear=gear, paint=paint)

    def race_block(self):
        rid, status, gates, progress, tms, seq, track, traffic, car = self.take("<BBBHIBBBB")
        return dict(id=rid, status=status, gates=gates, progress=progress / 65535, time=tms / 1000, seq=seq, track=track, traffic=traffic, car=car)

    def race(self):
        ms, rid, phase, track, traffic, car, launcher, seq, start, end, n = self.take("<IBBBBBBBIIB")
        entries = []
        for _ in range(n):
            pid, status, gates, progress, tms, rank = self.take("<BBBHIB")
            entries.append(dict(id=pid, status=status, gates=gates, progress=progress / 65535, time=tms / 1000, rank=rank))
        return dict(ms=ms, id=rid, phase=phase, track=track, traffic=traffic, car=car, launcher=launcher, seq=seq, start=start, end=end, entries=entries)


class Path:
    """Tracé d'une course : points d'un tour, distances des portes le long d'un tour, nombre de tours."""

    def __init__(self, file):
        data = json.load(open(file, encoding="utf-8"))
        self.pts, self.gates, self.laps = data["points"], data.get("gates", []), data.get("laps", 1)
        self.dist = [0.0]
        for a, b in zip(self.pts, self.pts[1:]):
            self.dist.append(self.dist[-1] + distance(a, b))
        self.length = self.dist[-1]
        self.total = self.length * self.laps
        self.gate_count = len(self.gates) * self.laps

    def at(self, d):
        """Position et cap à la distance d du départ (négative : derrière la ligne, dans l'axe du départ)."""
        if d <= 0:
            a, b = self.pts[0], self.pts[1]
            k = d / max(distance(a, b), 1e-6)
        else:
            w = self.length if d >= self.total else d % self.length
            i = min(max(bisect.bisect_right(self.dist, w), 1), len(self.pts) - 1)
            a, b = self.pts[i - 1], self.pts[i]
            k = (w - self.dist[i - 1]) / max(self.dist[i] - self.dist[i - 1], 1e-6)
        pos = tuple(a[j] + (b[j] - a[j]) * k for j in range(3))
        return pos, math.degrees(math.atan2(b[0] - a[0], b[2] - a[2]))

    def passed(self, d):
        """Portes franchies à la distance d du départ."""
        if d >= self.total:
            return self.gate_count
        lap, within = int(d // self.length), d % self.length
        return lap * len(self.gates) + sum(1 for g in self.gates if g <= within)


def now_ms(t0):
    return int((time.time() - t0) * 1000) & 0xFFFFFFFF


def forward(yaw):
    r = math.radians(yaw)
    return (math.sin(r), 0.0, math.cos(r))


def distance(a, b):
    return math.sqrt(sum((p - q) ** 2 for p, q in zip(a, b)))


def option(name, default=None, cast=str):
    for a in sys.argv:
        if a.startswith("--" + name + "="):
            return cast(a.split("=", 1)[1])
    return default


def windows(name):
    """Fenêtres de temps d'une option « --nom=A-B,C-D » (secondes depuis le lancement)."""
    spec = option(name)
    return [tuple(float(v) for v in w.split("-")) for w in spec.split(",")] if spec else []


def within(spans, t):
    return any(a <= t < b for a, b in spans)


def signals(flags):
    return ("klaxon " if flags & F_HORN else "") + ("appel de phares" if flags & F_FLASH else "") or "plus rien"


def describe(race, names=None):
    names = names or {}
    rows = []
    for e in sorted(race["entries"], key=lambda e: (e["rank"] == 0, e["rank"], e["id"])):
        rows.append(f"{e['rank'] or '-'}. {names.get(e['id'], 'joueur ' + str(e['id']))} [{STATUS[e['status']]}] portes {e['gates']}"
                    f" avancement {e['progress']:.3f} temps {e['time']:.3f}")
    return " | ".join(rows)


def rank(entries):
    """Classement, comme NetRace.Rank : les arrivés par temps, puis ceux qui roulent par portes et avancement."""
    def group(e):
        return 0 if e["status"] == S_FINISHED else 1 if e["status"] in (S_ACCEPTED, S_RACING) else 2
    order = sorted(entries, key=lambda e: (group(e), e["time"] if group(e) == 0 else 0, -e["gates"] if group(e) == 1 else 0,
                                           -e["progress"] if group(e) == 1 else 0, e["id"]))
    n = 0
    for e in order:
        if group(e) < 2:
            n += 1
            e["rank"] = n
        else:
            e["rank"] = 0


def client(port, x, y, z, yaw, seconds, claim, wait):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(0.005)
    server = ("127.0.0.1", port)
    t0 = time.time()
    sock.sendto(head(QUERY), server)
    nonce = 0x1234ABCD
    name, my_car = option("name", "Robot<b>"), option("car", 3, int)
    my_id, counts, players, traffic, roster, info = None, {}, {}, {}, {}, None
    last_hello = last_state = 0.0
    snapshots_extra = None
    f = forward(yaw)
    claimed = None
    # Envois complets du trafic : (instant, état déjà envoyé ?, nombre de véhicules, distance du plus lointain)
    my_pos, frames, sends = None, {}, []
    # Course à plusieurs
    answer, answer_after = option("race", "ignore"), option("answer", 1.5, float)
    path = Path(option("path")) if option("path") else None
    speed = option("speed", 60.0, float)
    abandon_at, quit_at, silent = option("abandon", None, float), option("quit", None, float), "--silent" in sys.argv
    propose, propose_at = option("propose"), option("propose-at", 3.0, float)
    proposal_seq, proposal_until = 0, 0.0
    race, race_seen, status, snap_race = None, 0.0, S_INVITED, 0
    gates, progress, final, slot, teleports = 0, 0.0, 0.0, 0, 0
    # Heure du serveur : celle du dernier paquet daté, prolongée par notre horloge (un dixième de seconde au plus)
    server_ms, server_at = None, 0.0
    last_phase, last_gates, quit_now = None, 0, False
    # Klaxon, appels de phares, immobilité, trajet hors course, départ de la partie
    horns, flashes, still, lit = windows("horn"), windows("flash"), "--still" in sys.argv, option("lights", 1, int) != 0
    route = Path(option("route")) if option("route") else None
    route_at, leave_at = option("route-at", 0.0, float), option("leave", None, float)
    said_signals = None

    def server_now():
        return None if server_ms is None else server_ms + min(time.time() - server_at, 0.1) * 1000

    while time.time() - t0 < seconds and not quit_now:
        t = time.time() - t0
        if leave_at is not None and t >= leave_at:
            print(f"[{t:.1f}] le robot quitte la partie")
            break
        # Tant qu'il n'envoie pas son état, le robot redemande l'entrée : le serveur le garde sans savoir où il est
        if (my_id is None or t < wait) and t - last_hello > 0.4:
            last_hello = t
            sock.sendto(head(HELLO) + struct.pack("<I", nonce) + string(name), server)
        if my_id is not None and t >= wait and t - last_state > 1 / 30:
            last_state = t
            # Course : réponse à l'invitation, place sur la grille, puis le tracé à vitesse constante depuis le départ
            now = server_now()
            if race is not None and now is not None:
                if race["phase"] == P_INVITE and status == S_INVITED and answer != "ignore" and t - race_seen >= answer_after:
                    status = S_ACCEPTED if answer == "accept" else S_DECLINED
                    print(f"[{t:.1f}] invitation {'acceptée' if status == S_ACCEPTED else 'refusée'}")
                elif race["phase"] in (P_COUNTDOWN, P_RUNNING) and status == S_ACCEPTED:
                    mine = [i for i, e in enumerate(race["entries"]) if e["id"] == my_id]
                    if mine and path is not None:
                        status, slot, teleports = S_RACING, mine[0], teleports + 1
                        print(f"[{t:.1f}] sur la grille, place {slot + 1}/{len(race['entries'])}, départ dans {(race['start'] - now) / 1000:.2f} s,"
                              f" voiture imposée {race['car']}")
                    else:
                        status = S_DECLINED
                        print(f"[{t:.1f}] pas dans la course (ou pas de tracé) : sans nous")
                elif status in (S_INVITED, S_ACCEPTED) and race["phase"] != P_INVITE:
                    status = S_DECLINED
                    print(f"[{t:.1f}] invitation expirée")
            racing_pos = None
            if status in (S_RACING, S_FINISHED, S_ABANDONED) and race is not None and path is not None and now is not None:
                elapsed = max(0.0, (now - race["start"]) / 1000) if status == S_RACING else None
                if status == S_RACING and race["phase"] == P_RESULTS:
                    status = S_ABANDONED
                    print(f"[{t:.1f}] course fermée par le serveur avant notre arrivée")
                elif status == S_RACING:
                    d = speed * elapsed
                    gates, progress = path.passed(d), min(1.0, d / path.total)
                    if gates != last_gates:
                        last_gates = gates
                        print(f"[{t:.1f}] porte {gates}/{path.gate_count} à {elapsed:.2f} s")
                    if abandon_at is not None and progress >= abandon_at:
                        status = S_ABANDONED
                        print(f"[{t:.1f}] abandon à {progress:.2f}")
                    elif quit_at is not None and progress >= quit_at:
                        print(f"[{t:.1f}] déconnexion en pleine course à {progress:.2f}" + (" (sans prévenir)" if silent else ""))
                        quit_now = True
                    elif d >= path.total:
                        status, final, progress = S_FINISHED, path.total / speed, 1.0
                        print(f"[{t:.1f}] arrivée, temps {final:.3f} s")
                d = speed * elapsed if status == S_RACING else progress * path.total
                # Avant le départ : derrière la ligne (en file si les voitures se percutent)
                back = -START_BACK - (slot * GRID_SPACING if collisions else 0.0)
                racing_pos, racing_yaw = path.at(d if d > 0 else back)
                racing_vel = speed if status == S_RACING and d > 0 else 0.0
            if racing_pos is not None:
                pos, fy = (racing_pos[0], racing_pos[1] + 0.3, racing_pos[2]), forward(racing_yaw)
                my_pos = pos
                shown = race["car"] if race["car"] != FREE_CAR and status == S_RACING else my_car
                state = car_state(pos, racing_yaw, (fy[0] * racing_vel, 0, fy[2] * racing_vel), car=shown, teleport=teleports,
                                  horn=within(horns, t), flash=within(flashes, t))
            elif route is not None:
                # Trajet hors course : à vitesse constante depuis son premier point, arrêt au bout
                d = max(0.0, t - route_at) * speed
                rp, ry = route.at(min(d, route.total) if d > 0 else 0.001)
                pos, fy, rv = (rp[0], rp[1] + 0.3, rp[2]), forward(ry), speed if 0 < d < route.total else 0.0
                my_pos = pos
                state = car_state(pos, ry, (fy[0] * rv, 0, fy[2] * rv), car=my_car, lights=lit, throttle=0.6 if rv else 0.0,
                                  horn=within(horns, t), flash=within(flashes, t))
            elif still:
                pos = (x, y + 0.3, z)
                my_pos = pos
                state = car_state(pos, yaw, (0, 0, 0), car=my_car, lights=lit, throttle=0.0, horn=within(horns, t), flash=within(flashes, t))
            else:
                # Va-et-vient de 30 m devant la position donnée, à 8 m/s ; téléportation à mi-parcours
                phase = (t * 8.0) % 60.0
                d = 12.0 + (phase if phase < 30 else 60 - phase)
                v = 8.0 if phase < 30 else -8.0
                side = 3.5
                pos = (x + f[0] * d + f[2] * side, y + 0.3, z + f[2] * d - f[0] * side)
                my_pos = pos
                tp = teleports + (1 if t > seconds * 0.5 and path is None else 0)
                state = car_state(pos, yaw, (f[0] * v, 0, f[2] * v), car=my_car, teleport=tp, braking=v < 0, skid=0.8, lights=lit,
                                  horn=within(horns, t), flash=within(flashes, t))
            if (within(horns, t), within(flashes, t)) != said_signals:
                if said_signals is not None or within(horns, t) or within(flashes, t):
                    print(f"[{t:.2f}] le robot envoie : {signals((F_HORN if within(horns, t) else 0) | (F_FLASH if within(flashes, t) else 0))}")
                said_signals = (within(horns, t), within(flashes, t))
            # Course proposée : répétée jusqu'à la voir reprise par le serveur (3 s au plus)
            if propose and proposal_seq == 0 and t >= propose_at and race is None:
                proposal_seq, proposal_until = 1, t + 3.0
                print(f"[{t:.1f}] course proposée : {propose}")
            proposing = proposal_seq != 0 and t < proposal_until
            ptrack, ptraffic, pcar = (int(v) for v in propose.split(",")) if propose else (0, 0, FREE_CAR)
            block = race_block(race["id"] if race else 0, status, gates, progress, final,
                               proposal_seq if proposing else 0, ptrack if proposing else 0, ptraffic if proposing else 0, pcar if proposing else FREE_CAR)
            pkt = head(STATE) + struct.pack("<I", now_ms(t0)) + state + block
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
            info = (r.take("<I"), r.string(), r.take("<B"), r.take("<B"), r.take("<B"))
            assert r.p == len(data), "info: taille inattendue"
        elif msg == WELCOME:
            echoed, pid = r.take("<I"), r.take("<B")
            sname, most, options = r.string(), r.take("<B"), r.take("<B")
            assert r.p == len(data), "welcome: taille inattendue"
            if my_id is None:
                print("WELCOME id", pid, "nonce ok", echoed == nonce, "serveur", repr(sname), "max", most,
                      "options", options, "collisions", "oui" if options & OPT_COLLISIONS else "non")
            my_id, collisions = pid, bool(options & OPT_COLLISIONS)
        elif msg == REFUSE:
            print("REFUSE", r.take("<I"), r.take("<B"))
        elif msg == SNAPSHOT:
            ms, hour, stormy, storm, rain, wet, snap_race, n = r.take("<I"), r.take("<f"), *r.take("<4B"), r.take("<B"), r.take("<B")
            snapshots_extra = (round(hour, 2), stormy, storm, rain, wet)
            if server_ms is None or (ms - server_ms) & 0xFFFFFFFF < 0x80000000:
                server_ms, server_at = ms, time.time()
            for _ in range(n):
                pid, pms = r.take("<B"), r.take("<I")
                before = players.get(pid)
                players[pid] = r.state()
                if pid != my_id and (before is None or before["car"] != players[pid]["car"]):
                    print(f"[{t:.1f}] le joueur {pid} affiche la voiture {players[pid]['car']}")
                now_flags, old_flags = players[pid]["flags"] & (F_HORN | F_FLASH), (before["flags"] if before else 0) & (F_HORN | F_FLASH)
                if pid != my_id and now_flags != old_flags:
                    print(f"[{t:.2f}] le joueur {pid} : {signals(now_flags)} (drapeaux {players[pid]['flags']})")
            assert r.p == len(data), "snapshot: taille inattendue"
            # Le serveur annonce une autre séance que la nôtre, ou aucune : la nôtre est finie
            if race is not None and snap_race != race["id"] and (ms - race["ms"]) & 0xFFFFFFFF < 0x80000000:
                print(f"[{t:.1f}] séance {race['id']} fermée (le serveur annonce la séance {snap_race})")
                race, status, gates, progress, final, last_phase, last_gates = None, S_INVITED, 0, 0.0, 0.0, None, 0
        elif msg == ROSTER:
            roster = {}
            for _ in range(r.take("<B")):
                pid = r.take("<B")
                roster[pid] = r.string()
        elif msg == RACE:
            got = r.race()
            assert r.p == len(data), "race: taille inattendue"
            if race is not None and got["id"] == race["id"] and (got["ms"] - race["ms"]) & 0xFFFFFFFF >= 0x80000000:
                continue
            if race is None or got["id"] != race["id"]:
                race_seen = t
                status = S_ACCEPTED if got["launcher"] == my_id else S_INVITED
                gates, progress, final, last_phase, last_gates = 0, 0.0, 0.0, None, 0
                print(f"[{t:.1f}] séance {got['id']} : tracé {got['track']}, trafic {got['traffic']}, voiture {got['car']},"
                      f" lancée par {roster.get(got['launcher'], got['launcher'])} (proposition {got['seq']})")
                if got["launcher"] == my_id and got["seq"] == proposal_seq:
                    proposal_until = 0.0
            race = got
            if race["phase"] != last_phase:
                last_phase = race["phase"]
                print(f"[{t:.1f}] étape : {PHASES[race['phase']]} ; {describe(race, roster)}")
        elif msg == TRAFFIC:
            ms, frame, chunk, chunks, n = r.take("<I"), r.take("<H"), r.take("<B"), r.take("<B"), r.take("<B")
            seen, cars = frames.setdefault(frame, (set(), {}))
            for _ in range(n):
                cid, model = r.take("<H"), r.take("<B")
                rgb = r.take("<3B")
                flags, owner = r.take("<B"), r.take("<B")
                pos = r.take("<3f")
                r.p += 7 + 6
                traffic[cid] = dict(kind=model >> 6, model=model & 63, crashed=flags & 1, owner=owner, pos=tuple(round(c, 1) for c in pos))
                cars[cid] = pos
            assert r.p == len(data), "trafic: taille inattendue"
            seen.add(chunk)
            if len(seen) == chunks:
                # Envoi complet : combien de véhicules, et à quelle distance du robot se trouve le plus lointain
                here = my_pos or (x, y, z)
                sends.append((t, my_pos is not None, len(cars), max((distance(p, here) for p in cars.values()), default=0.0)))
                del frames[frame]
    if not (quit_now and silent):
        sock.sendto(head(BYE), server)
    names = {INFO: "info", WELCOME: "welcome", SNAPSHOT: "snapshot", ROSTER: "roster", TRAFFIC: "traffic", BYE: "bye", REFUSE: "refuse", RACE: "race"}
    print("paquets:", {names.get(k, k): v for k, v in counts.items()})
    print("info:", info, "collisions", None if info is None else "oui" if info[4] & OPT_COLLISIONS else "non")
    for label, known in (("avant le premier état", False), ("état envoyé", True)):
        part = [s for s in sends if s[1] == known]
        if part:
            print(f"trafic, {label}: {len(part)} envois complets, véhicules par envoi {min(s[2] for s in part)} à {max(s[2] for s in part)},"
                  f" plus lointain à {max(s[3] for s in part):.0f} m")
            step = max(1, len(part) // 12)
            print("   au fil du temps (s: véhicules/plus lointain m):", "  ".join(f"{s[0]:.0f}: {s[2]}/{s[3]:.0f}" for s in part[::step]))
    print("roster:", roster)
    print("heure/météo:", snapshots_extra, "séance annoncée par le dernier snapshot:", snap_race)
    for pid, s in players.items():
        print(f"joueur {pid}: pos {tuple(round(c, 1) for c in s['pos'])} vel {s['vel']} voiture {s['car']} tp {s['teleport']} peinture {s['paint']}")
    print("trafic vu:", len(traffic), "véhicules ; exemples:", list(traffic.items())[:3])
    if claimed is not None:
        print("véhicule revendiqué:", claimed, traffic.get(claimed))
    if race is not None:
        print("séance à la fin:", PHASES[race["phase"]], ";", describe(race, roster))


def server(port, x, y, z, yaw, seconds, collisions):
    options = bytes([OPT_COLLISIONS if collisions else 0])
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("0.0.0.0", port))
    sock.settimeout(0.005)
    t0 = time.time()
    peer, got, last = None, {}, {}
    last_snap = last_roster = last_traffic = last_race = 0.0
    frame = 0
    f = forward(yaw)
    states = 0
    owned_seen = set()
    # Course à plusieurs : l'hôte (joueur 0) en lance une ; « Garé » (joueur 1) refuse ; le joueur est le numéro 2
    launch, launch_at = option("race"), option("race-at", 6.0, float)
    path = Path(option("path")) if option("path") else None
    speed = option("speed", 60.0, float)
    finish_delay, results_time = option("finish-delay", None, float), option("results", 12.0, float)
    race, next_id, launched, player_block, seen_proposal = None, 1, False, None, 0
    names = {0: "Hôte robot", 1: "Garé", 2: "Moi"}
    said, cars_seen = None, []
    horns, flashes, heard = windows("horn"), windows("flash"), 0

    def open_race(launcher, seq, track, traffic, car, ms):
        nonlocal race, next_id
        entries = [dict(id=launcher, status=S_ACCEPTED, gates=0, progress=0.0, time=0.0, rank=0)]
        entries += [dict(id=i, status=S_INVITED, gates=0, progress=0.0, time=0.0, rank=0) for i in (0, 1, 2) if i != launcher]
        race = dict(id=next_id, phase=P_INVITE, track=track, traffic=traffic, car=car, launcher=launcher, seq=seq,
                    start=ms + int(INVITE_TIME * 1000), end=0, entries=entries, opened=ms)
        next_id = next_id % 255 + 1
        print(f"[{ms / 1000:.1f}] séance {race['id']} ouverte par {names[launcher]} : tracé {track}, trafic {traffic}, voiture {car}")

    def entry(pid):
        return next((e for e in race["entries"] if e["id"] == pid), None) if race else None

    def tick_race(ms):
        nonlocal race
        e0, e1 = entry(0), entry(1)
        if race["phase"] == P_INVITE:
            # « Garé » refuse après 2 s ; l'hôte, s'il est invité (course proposée par le joueur), accepte après 1 s
            if e1 and e1["status"] == S_INVITED and ms - race["opened"] > 2000:
                e1["status"] = S_DECLINED
            if e0 and e0["status"] == S_INVITED and ms - race["opened"] > 1000:
                e0["status"] = S_ACCEPTED
            if any(e["status"] == S_INVITED for e in race["entries"]) and ms < race["start"]:
                return
            race["entries"] = [e for e in race["entries"] if e["status"] == S_ACCEPTED]
            race["phase"], race["start"] = P_COUNTDOWN, ms + int(GRID_TIME * 1000)
            rank(race["entries"])
            print(f"[{ms / 1000:.1f}] décompte : {describe(race, names)}")
        elif race["phase"] == P_COUNTDOWN:
            if e0 and e0["status"] == S_ACCEPTED:
                e0["status"] = S_RACING
            if ms >= race["start"]:
                race["phase"] = P_RUNNING
                print(f"[{ms / 1000:.1f}] départ")
        elif race["phase"] == P_RUNNING:
            if e0 and e0["status"] == S_RACING and path is not None:
                d = speed * (ms - race["start"]) / 1000
                e0["gates"], e0["progress"] = path.passed(d), min(1.0, d / path.total)
                if d >= path.total:
                    e0["status"], e0["time"] = S_FINISHED, path.total / speed
                    print(f"[{ms / 1000:.1f}] l'hôte arrive en {e0['time']:.3f} s")
            for e in race["entries"]:
                if e["status"] == S_ACCEPTED and ms > race["start"] + 10000:
                    e["status"] = S_ABANDONED
            done = [e["time"] for e in race["entries"] if e["status"] == S_FINISHED]
            if done and race["end"] == 0:
                delay = finish_delay if finish_delay is not None else max(30.0, min(90.0, min(done) * 0.5))
                race["end"] = ms + int(delay * 1000)
            racing = [e for e in race["entries"] if e["status"] in (S_ACCEPTED, S_RACING)]
            if not racing or race["end"] and ms >= race["end"]:
                for e in racing:
                    e["status"] = S_ABANDONED
                race["phase"], race["end"] = P_RESULTS, ms + int(results_time * 1000)
                rank(race["entries"])
                print(f"[{ms / 1000:.1f}] résultats : {describe(race, names)}")
            rank(race["entries"])
        elif race["phase"] == P_RESULTS and ms >= race["end"]:
            print(f"[{ms / 1000:.1f}] séance {race['id']} fermée")
            race = None

    while time.time() - t0 < seconds:
        t = time.time() - t0
        ms = now_ms(t0)
        try:
            data, addr = sock.recvfrom(4096)
            if data[:2] == b"CR":
                msg = data[3]
                got[msg] = got.get(msg, 0) + 1
                r = Reader(data)
                if msg == QUERY:
                    sock.sendto(head(INFO) + struct.pack("<I", 777) + string("Serveur robot") + bytes([1, 8]) + options, addr)
                elif msg == HELLO:
                    nonce, name = r.take("<I"), r.string()
                    if peer is None:
                        print("HELLO de", name, addr)
                    peer = addr
                    sock.sendto(head(WELCOME) + struct.pack("<I", nonce) + bytes([2]) + string("Serveur robot") + bytes([8]) + options, addr)
                elif msg == STATE:
                    pms = r.take("<I")
                    last = r.state()
                    player_block = r.race_block()
                    states += 1
                    for _ in range(r.take("<B")):
                        owned_seen.add(r.take("<H"))
                        r.p += 12 + 7 + 6
                    assert r.p == len(data), "state: taille inattendue"
                    if not cars_seen or cars_seen[-1][1:] != (last["car"], last["teleport"]):
                        cars_seen.append((round(t, 1), last["car"], last["teleport"]))
                    if last["flags"] & (F_HORN | F_FLASH) != heard:
                        heard = last["flags"] & (F_HORN | F_FLASH)
                        print(f"[{t:.2f}] le joueur : {signals(heard)} (drapeaux {last['flags']})")
                    # Ce que le joueur dit de lui pour la séance en cours, et la course qu'il propose
                    b, e = player_block, entry(2)
                    if (b["id"], b["status"], b["gates"], b["seq"]) != said:
                        said = (b["id"], b["status"], b["gates"], b["seq"])
                        print(f"[{t:.1f}] le joueur dit : séance {b['id']}, {STATUS[b['status']]}, portes {b['gates']}, avancement {b['progress']:.3f},"
                              f" temps {b['time']:.3f}, proposition {b['seq']} ({b['track']},{b['traffic']},{b['car']}) ; voiture affichée {last['car']}")
                    if race and e and b["id"] == race["id"]:
                        if race["phase"] == P_INVITE and e["status"] == S_INVITED and b["status"] in (S_ACCEPTED, S_DECLINED):
                            e["status"] = b["status"]
                        elif race["phase"] in (P_COUNTDOWN, P_RUNNING) and e["status"] in (S_ACCEPTED, S_RACING):
                            if b["status"] == S_RACING:
                                e["status"], e["gates"], e["progress"] = S_RACING, max(e["gates"], b["gates"]), max(e["progress"], b["progress"])
                            elif b["status"] == S_FINISHED and b["time"] > 0 and race["phase"] == P_RUNNING:
                                e["status"], e["time"], e["gates"], e["progress"] = S_FINISHED, b["time"], b["gates"], 1.0
                            elif b["status"] in (S_ABANDONED, S_DECLINED):
                                e["status"] = S_ABANDONED
                    if b["seq"] != 0 and b["seq"] != seen_proposal:
                        seen_proposal = b["seq"]
                        if race is None:
                            open_race(2, b["seq"], b["track"], b["traffic"], b["car"], ms)
                elif msg == BYE:
                    print("BYE reçu")
                    peer = None
        except (socket.timeout, ConnectionResetError):
            pass
        if peer is None:
            continue
        if launch and not launched and t >= launch_at and race is None:
            launched = True
            open_race(0, 0, *(int(v) for v in launch.split(",")), ms)
        if race is not None:
            tick_race(ms)
        if t - last_snap > 1 / 30:
            last_snap = t
            # Deux faux joueurs : l'un tourne en rond devant le joueur (ou court sa course), l'autre est garé à côté
            a = t * 0.8
            cx, cz = x + f[0] * 25, z + f[2] * 25
            p0 = (cx + math.sin(a) * 9, y + 0.3, cz + math.cos(a) * 9)
            v0 = (math.cos(a) * 7.2, 0, -math.sin(a) * 7.2)
            y0, car0, e0 = math.degrees(a) + 90, 1, entry(0)
            if race and path is not None and e0 and race["phase"] != P_INVITE:
                d = speed * (ms - race["start"]) / 1000 if e0["status"] == S_RACING else e0["progress"] * path.total
                rp, y0 = path.at(d if d > 0 else -START_BACK)
                p0 = (rp[0], rp[1] + 0.3, rp[2])
                fr = forward(y0)
                rv = speed if e0["status"] == S_RACING and d > 0 else 0.0
                v0 = (fr[0] * rv, 0, fr[2] * rv)
                if race["car"] != FREE_CAR and e0["status"] == S_RACING:
                    car0 = race["car"]
            p1 = (x + f[2] * 4, y + 0.3, z - f[0] * 4)
            pkt = head(SNAPSHOT) + struct.pack("<If", ms, 22.5) + bytes([1, 255, 200, 255, race["id"] if race else 0, 2])
            pkt += bytes([0]) + struct.pack("<I", ms) + car_state(p0, y0, v0, car=car0, skid=0.0 if race else 0.9,
                                                                  horn=within(horns, t), flash=within(flashes, t))
            pkt += bytes([1]) + struct.pack("<I", ms) + car_state(p1, yaw, (0, 0, 0), car=5, throttle=0.0, braking=True)
            sock.sendto(pkt, peer)
        if race is not None and t - last_race > 0.2:
            last_race = t
            sock.sendto(race_message(ms, race), peer)
        if t - last_roster > 1.0:
            last_roster = t
            listed = [(0, "Hôte robot"), (1, "Garé"), (2, "Moi")] + ([(5, "Tardif")] if 6 < t < 12 else [])
            sock.sendto(head(ROSTER) + bytes([len(listed)]) + b"".join(bytes([i]) + string(n) for i, n in listed), peer)
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
    print("voitures affichées par le joueur (instant, voiture, téléportations):", cars_seen)
    print("véhicules revendiqués par le joueur:", sorted(owned_seen))


if __name__ == "__main__":
    mode, port = sys.argv[1], int(sys.argv[2])
    x, y, z, yaw = (float(a) for a in sys.argv[3:7])
    rest = [a for a in sys.argv[7:] if not a.startswith("--")]
    seconds = float(rest[0]) if rest else 15.0
    if mode == "client":
        client(port, x, y, z, yaw, seconds, "--claim" in sys.argv, option("wait", 0.0, float))
    else:
        server(port, x, y, z, yaw, seconds, "--nocollisions" not in sys.argv)
