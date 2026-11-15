"""Briques de construction du pack alpin : façades (fenêtres, portes, balcons, stores), toits, cheminées, horloges."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import lod

_DV = {'+x': Vector((1, 0, 0)), '-x': Vector((-1, 0, 0)), '+y': Vector((0, 1, 0)), '-y': Vector((0, -1, 0))}


class Fac:
    """Façade verticale plane. axis = 'y' : perpendiculaire à Y, en y = pos, tournée vers « facing » (-1 = -Y).
    u = coordonnée le long de la façade (x pour axis 'y', y pour axis 'x'), out = distance devant le mur."""

    def __init__(self, g, axis, pos, facing):
        self.g, self.axis, self.pos, self.facing = g, axis, pos, facing
        self.face_dir = ('+' if facing > 0 else '-') + axis
        self.open_dir = ('-' if facing > 0 else '+') + axis

    def P(self, u, out, z):
        if self.axis == 'y':
            return (u, self.pos + out * self.facing, z)
        return (self.pos + out * self.facing, u, z)

    def S(self, su, depth, sz):
        return (su, depth, sz) if self.axis == 'y' else (depth, su, sz)

    def slab(self, u, z, su, sz, depth, color, out=0.0):
        return self.g.slab(self.P(u, out + depth / 2, z), self.S(su, depth, sz), color, open=self.open_dir)

    def box(self, u, z, su, sz, depth, color, out=0.0):
        return self.g.box(self.P(u, out + depth / 2, z), self.S(su, depth, sz), color)

    def quad(self, u, z, su, sz, color, out=0.03):
        return self.g.quad(self.P(u, out, z), su, sz, self.face_dir, color)

    def poly(self, pts, color, out=0.03):
        """Polygone (points (u, z)) plaqué sur la façade, tourné vers l'extérieur."""
        f = self.g.face([self.P(u, out, z) for u, z in pts], color)[0]
        f.normal_update()
        if f.normal.dot(_DV[self.face_dir]) < 0:
            f.normal_flip()
        return [f]

    def prism(self, pts, u0, u1, color):
        """Prisme de section (out, z) extrudé le long de la façade de u0 à u1."""
        sec = [(self.pos + o * self.facing, z) for o, z in pts]
        return self.g.prism(sec, u0, u1, color, axis='x' if self.axis == 'y' else 'y')

    # ---- ouvertures
    def glass(self, u, z, lit):
        return "window_lit" if self.g.chance(lit, *self.P(u, 0, z)) else "window"

    def window(self, u, z, w=0.9, h=1.25, shutter="shutter", frame="plaster_white", lit=0.5, flower=None,
               keep=True, cross=False, flat=False):
        """Fenêtre centrée en (u, z) : encadrement, vitre (éclairée ou non), volets ouverts, jardinière.
        flat : simple rectangle à tous les niveaux (façades arrière) ; keep : visible au niveau 2."""
        g = self.g
        col = self.glass(u, z, lit)
        sw = min(0.5, w * 0.5)
        if flat:
            if g.lod < 2:
                self.quad(u, z, w, h, col, 0.03)
            return
        if g.lod == 0:
            if frame:
                self.slab(u, z, w + 0.2, h + 0.2, 0.05, frame)
            self.slab(u, z, w, h, 0.075, col)
            if cross:
                self.slab(u, z, 0.06, h, 0.09, frame or "plaster_white")
            if shutter:
                for s in (-1, 1):
                    self.slab(u + s * (w / 2 + 0.1 + sw / 2), z, sw, h + 0.08, 0.06, shutter)
            if flower:
                self.flowerbox(u, z - h / 2 - 0.16, w + 0.25, flower)
        elif g.lod == 1:
            self.quad(u, z, w, h, col, 0.04)
            if shutter:
                for s in (-1, 1):
                    self.quad(u + s * (w / 2 + 0.1 + sw / 2), z, sw, h + 0.08, shutter, 0.04)
        elif keep:
            self.quad(u, z, w, h, col, 0.05)

    def arch_pts(self, u, z0, w, h, n=6):
        """Contour d'une baie cintrée : pieds à z0, hauteur totale h."""
        r = w / 2
        pts = [(u - r, z0), (u + r, z0)]
        for k in range(n + 1):
            a = math.pi * k / n
            pts.append((u + math.cos(a) * r, z0 + h - r + math.sin(a) * r))
        return pts

    def arch(self, u, z0, w, h, color="window", out=0.04, lit=None):
        if lit is not None:
            color = self.glass(u, z0, lit)
        return self.poly(self.arch_pts(u, z0, w, h, lod(self.g, 6, 4, 2)), color, out)

    def door(self, u, w=1.0, h=2.1, color="door", frame="plaster_white", z0=0.0, glass=False):
        g = self.g
        if g.lod == 0:
            if frame:
                self.slab(u, z0 + (h + 0.1) / 2, w + 0.22, h + 0.1, 0.05, frame)
            self.slab(u, z0 + h / 2, w, h, 0.08, color)
            if glass:
                self.quad(u, z0 + h * 0.68, w * 0.6, h * 0.36, "window", 0.09)
            self.slab(u + w * 0.34, z0 + h * 0.48, 0.06, 0.14, 0.12, "brass")
        else:
            self.quad(u, z0 + h / 2, w, h, color, 0.05)

    def flowerbox(self, u, z, w, flower="geranium", wood="wood"):
        if self.g.lod > 0:
            return
        self.slab(u, z, w, 0.18, 0.24, wood)
        f = self.box(u, z + 0.17, w - 0.06, 0.2, 0.26, flower, out=0.0)
        self.g.jitter(f, 0.035)
        self.box(u, z + 0.1, w - 0.02, 0.06, 0.3, "leaf", out=-0.01)

    def balcony(self, u0, u1, z, depth=1.1, wood="wood_dark", rail_h=1.0, flowers="geranium", style="plank",
                floor=None):
        """Balcon de u0 à u1, plancher à z. style 'plank' : garde-corps plein en bois ; 'iron' : fer forgé sombre."""
        g = self.g
        uc, L = (u0 + u1) / 2, u1 - u0
        panel = wood if style == "plank" else "metal_dark"
        floor = floor or (wood if style == "plank" else "concrete")
        if g.lod == 2:
            self.box(uc, z + rail_h * 0.42, L, rail_h * 0.95, depth, panel)
            return
        th = 0.06 if style == "plank" else 0.03
        ph = rail_h - (0.18 if style == "plank" else 0.05)
        self.box(uc, z - 0.07, L, 0.14, depth, floor)
        self.box(uc, z + ph / 2 + 0.06, L, ph, th, panel, out=depth - th)
        for s in (-1, 1):
            self.box(uc + s * (L / 2 - th / 2), z + ph / 2 + 0.06, th, ph, depth - th, panel)
        if g.lod == 0:
            self.box(uc, z + rail_h, L + 0.08, 0.08, 0.14, panel if style == "iron" else wood, out=depth - 0.1)
            n = max(2, int(L / 2.6) + 1)
            for k in range(n):
                u = u0 + 0.15 + (L - 0.3) * k / (n - 1)
                self.prism([(0, z - 0.14), (depth * 0.85, z - 0.14), (0, z - 0.6)], u - 0.07, u + 0.07, floor)
            if flowers:
                m = max(1, int(L / 2.2))
                seg = L / m
                for k in range(m):
                    uu = u0 + seg * (k + 0.5)
                    f = self.box(uu, z + rail_h - 0.02, seg * 0.6, 0.24, 0.26, flowers, out=depth - 0.02)
                    g.jitter(f, 0.04)
                    self.box(uu, z + rail_h - 0.16, seg * 0.66, 0.12, 0.24, "leaf", out=depth - 0.01)

    def awning(self, u, z, w, depth=1.3, drop=0.55, color="awning_red", stripe=None):
        """Store banne : pan incliné + lambrequin. stripe : seconde couleur en bandes (détail fin seulement)."""
        g = self.g
        if g.lod == 2:
            self.prism([(0, z), (depth, z - drop), (0, z - 0.12)], u - w / 2, u + w / 2, color)
            return
        n = max(2, int(w / 0.7)) if (stripe and g.lod == 0) else 1
        seg = w / n
        for k in range(n):
            c = stripe if (stripe and k % 2 == 1) else color
            ua = u - w / 2 + seg * k
            self.prism([(0, z), (depth, z - drop), (depth, z - drop - 0.2), (depth - 0.03, z - drop - 0.03), (0, z - 0.06)],
                       ua, ua + seg, c)

    def sign(self, u, z, w, h, color, plate=None, depth=0.08):
        """Bandeau d'enseigne, avec une plaque contrastée au centre (pas de texte)."""
        if self.g.lod == 2:
            self.quad(u, z, w, h, color, 0.05)
            return
        self.slab(u, z, w, h, depth, color)
        if plate and self.g.lod == 0:
            self.slab(u, z, w * 0.5, h * 0.42, depth + 0.03, plate)

    def hanging_sign(self, u, z, color, plate=None, size=0.7):
        """Enseigne en potence, perpendiculaire à la façade (détail fin)."""
        if self.g.lod > 0:
            return
        self.box(u, z + size * 0.55, 0.05, 0.05, size + 0.25, "metal_dark")
        self.box(u, z, 0.06, size * 0.8, size, color, out=0.2)
        if plate:
            self.box(u, z, 0.08, size * 0.4, size * 0.5, plate, out=0.2 + size * 0.25)

    def clock(self, u, z, r=0.8, out=0.05):
        g = self.g
        n = lod(g, 12, 8, 6)
        pts = [(u + math.cos(2 * math.pi * k / n) * r, z + math.sin(2 * math.pi * k / n) * r) for k in range(n)]
        if g.lod == 0:
            rim = [(u + (p[0] - u) * 1.14, z + (p[1] - z) * 1.14) for p in pts]
            self.poly(rim, "metal_dark", out)
        self.poly(pts, "sign_white", out + 0.02)
        if g.lod < 2:
            self.quad(u, z + r * 0.32, r * 0.1, r * 0.66, "black", out + 0.04)
            self.quad(u + r * 0.24, z, r * 0.5, r * 0.1, "black", out + 0.04)


# ----------------------------------------------------------------- volumes
def foundation(g, w, d, color="stone", top=0.35, depth=3.0, center=(0, 0), grow=0.06):
    """Soubassement prolongé sous le sol (pose sur une pente)."""
    return g.box((center[0], center[1], (top - depth) / 2), (w + 2 * grow, d + 2 * grow, top + depth), color)


def gable_body(g, span, length, z0, z_eave, pitch, color, ridge='y', center=(0, 0)):
    """Murs + pignons d'un seul bloc (section en pentagone) ; renvoie la hauteur du faîtage."""
    half = span / 2
    zr = z_eave + half * math.tan(math.radians(pitch))
    if z_eave - z0 > 0.02:
        pts = [(-half, z0), (half, z0), (half, z_eave), (0, zr), (-half, z_eave)]
    else:
        pts = [(-half, z_eave), (half, z_eave), (0, zr)]
    f = g.prism(pts, -length / 2, length / 2, color, axis=ridge)
    g.move(f, (center[0], center[1], 0))
    return zr


def roof_gable(g, span, length, z_eave, pitch, color, ridge='y', over_e=0.8, over_g=1.0, thick=0.24,
               center=(0, 0), trim=None):
    """Toit à deux pans : deux dalles épaisses. over_e = débord des égouts, over_g = débord des pignons.
    trim : couleur des planches de rive (détail). Renvoie la hauteur du faîtage."""
    half = span / 2
    slope = math.tan(math.radians(pitch))
    zr = z_eave + half * slope
    m = g.mark()
    e = half + over_e
    ze = z_eave - over_e * slope
    a = length / 2 + over_g
    for s in (-1, 1):
        g.prism([(s * e, ze), (0, zr), (0, zr + thick), (s * e, ze + thick)], -a, a, color, axis=ridge)
        if trim and g.lod < 2:
            for end in (-1, 1):
                g.prism([(s * e, ze - 0.06), (0, zr - 0.06), (0, zr + thick + 0.03), (s * e, ze + thick + 0.03)],
                        end * a - 0.02 * end, end * (a + 0.07), trim, axis=ridge)
    g.move(g.since(m), (center[0], center[1], 0))
    return zr


def roof_hip(g, w, d, z0, pitch, color, over=0.5, center=(0, 0)):
    """Toit à quatre pans (faîtage le long du grand côté) ; renvoie la hauteur du faîtage."""
    slope = math.tan(math.radians(pitch))
    ax, ay = w / 2 + over, d / 2 + over
    zb = z0 - over * slope
    m = g.mark()
    if ax >= ay:
        h, r = ay * slope, ax - ay
        top = [(-r, 0, zb + h), (r, 0, zb + h)]
        base = [(-ax, -ay, zb), (ax, -ay, zb), (ax, ay, zb), (-ax, ay, zb)]
        g.face([base[0], base[1], top[1], top[0]], color)
        g.face([base[2], base[3], top[0], top[1]], color)
        g.face([base[1], base[2], top[1]], color)
        g.face([base[3], base[0], top[0]], color)
    else:
        h, r = ax * slope, ay - ax
        top = [(0, -r, zb + h), (0, r, zb + h)]
        base = [(-ax, -ay, zb), (ax, -ay, zb), (ax, ay, zb), (-ax, ay, zb)]
        g.face([base[1], base[2], top[1], top[0]], color)
        g.face([base[3], base[0], top[0], top[1]], color)
        g.face([base[0], base[1], top[0]], color)
        g.face([base[2], base[3], top[1]], color)
    g.face(list(reversed(base)), color)
    g.move(g.since(m), (center[0], center[1], 0))
    return zb + h


def roof_frustum(g, w, d, z0, h, inset, color, over=0.3, center=(0, 0), cap=None):
    """Brisis de toit mansardé : tronc de pyramide rectangulaire ; cap = couleur du dessus (None : ouvert)."""
    ax, ay = w / 2 + over, d / 2 + over
    bx, by = ax - inset, ay - inset
    m = g.mark()
    lo = [(-ax, -ay, z0), (ax, -ay, z0), (ax, ay, z0), (-ax, ay, z0)]
    hi = [(-bx, -by, z0 + h), (bx, -by, z0 + h), (bx, by, z0 + h), (-bx, by, z0 + h)]
    for k in range(4):
        j = (k + 1) % 4
        g.face([lo[k], lo[j], hi[j], hi[k]], color)
    g.face(list(reversed(lo)), color)
    if cap:
        g.face(hi, cap)
    g.move(g.since(m), (center[0], center[1], 0))
    return bx * 2, by * 2


def roof_pyramid(g, w, d, z0, h, color, over=0.3, center=(0, 0)):
    ax, ay = w / 2 + over, d / 2 + over
    m = g.mark()
    lo = [(-ax, -ay, z0), (ax, -ay, z0), (ax, ay, z0), (-ax, ay, z0)]
    for k in range(4):
        g.face([lo[k], lo[(k + 1) % 4], (0, 0, z0 + h)], color)
    g.face(list(reversed(lo)), color)
    g.move(g.since(m), (center[0], center[1], 0))


def roof_shed(g, w, d, z_hi, z_lo, color, over=0.3, thick=0.16, center=(0, 0), low='-y'):
    """Toit à un pan (appentis) : haut côté opposé à « low »."""
    ax, ay = w / 2 + over, d / 2 + over
    m = g.mark()
    if low in ('-y', '+y'):
        s = -1 if low == '-y' else 1
        g.prism([(s * ay, z_lo), (-s * ay, z_hi), (-s * ay, z_hi + thick), (s * ay, z_lo + thick)], -ax, ax, color,
                axis='x')
    else:
        s = -1 if low == '-x' else 1
        g.prism([(s * ax, z_lo), (-s * ax, z_hi), (-s * ax, z_hi + thick), (s * ax, z_lo + thick)], -ay, ay, color,
                axis='y')
    g.move(g.since(m), (center[0], center[1], 0))


def chimney(g, x, y, z0, z1, color="stone", w=0.62, cap="stone_dark"):
    g.box((x, y, (z0 + z1) / 2), (w, w, z1 - z0), color)
    if g.lod == 0:
        g.box((x, y, z1 + 0.06), (w + 0.16, w + 0.16, 0.12), cap)


def dormer(g, x, y_front, z_roof, slope, facing=-1, w=1.25, h=1.25, wall="plaster_white", roof="roof_tile",
           lit=0.5):
    """Lucarne sur un versant (toit à faîtage le long de X) : façade en y_front, tournée vers « facing »."""
    if g.lod == 2:
        return
    L = h / max(slope, 0.3) + 0.3
    yc = y_front - facing * L / 2
    g.box((x, yc, z_roof + h / 2 - 0.15), (w, L, h + 0.3), wall)
    m = g.mark()
    g.prism([(-w / 2 - 0.15, 0), (w / 2 + 0.15, 0), (0, w * 0.42)], -L / 2 - 0.12, L / 2, roof, axis='y')
    g.move(g.since(m), (x, yc if facing < 0 else yc, z_roof + h))
    f = Fac(g, 'y', y_front, facing)
    col = f.glass(x, z_roof, lit)
    f.quad(x, z_roof + h / 2, w * 0.62, h * 0.7, col, 0.03)


def flag(g, x, y, z0, h=5.0, color="red", size=(1.2, 0.8), pole="metal", rotz=0.0):
    """Mât + drapeau (le drapeau part vers +X avant rotation)."""
    m = g.mark()
    g.frustum((0, 0, z0), 0.05, 0.035, h, lod(g, 5, 4, 3), pole, cap0=False)
    g.box((size[0] / 2 + 0.05, 0, z0 + h - size[1] / 2 - 0.08), (size[0], 0.03, size[1]), color)
    g.move(g.since(m), (x, y, 0), rotz)


def cross(g, x, y, z, h=1.6, color="metal_dark", t=0.12, axis='x'):
    g.box((x, y, z + h / 2), (t, t, h), color)
    arm = (h * 0.55, t, t) if axis == 'x' else (t, h * 0.55, t)
    g.box((x, y, z + h * 0.68), arm, color)


def steps(g, u, y, w, n=3, rise=0.17, run=0.3, color="stone", facing=-1):
    """Perron : marches devant une porte (façade perpendiculaire à Y)."""
    if g.lod == 2:
        return
    for k in range(n):
        dd = run * (n - k)
        g.box((u, y + facing * dd / 2, rise * (k + 0.5)), (w, dd, rise), color)
