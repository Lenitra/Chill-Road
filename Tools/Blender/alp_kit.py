"""Briques de construction du pack alpin : façades (fenêtres, portes, balcons, stores), toits, cheminées, horloges.

v2 : chaque brique a un rendu « hd » (g.hd, LOD0 exporté) nettement plus fin — fenêtres à tableau, dormant,
croisillons et appui, volets à lames ou à planches ferrés, portes à panneaux, couvertures en rangs décalés,
chevrons, gouttières et descentes, balustres découpés, pierres apparentes — tandis que le niveau moyen garde les
volumes d'origine (mêmes positions, mêmes gabarits : les silhouettes se superposent)."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import lod

_DV = {'+x': Vector((1, 0, 0)), '-x': Vector((-1, 0, 0)), '+y': Vector((0, 1, 0)), '-y': Vector((0, -1, 0))}

# couverture : couleur -> (second ton, hauteur de rang, largeur d'élément, épaisseur) ; None = joints debout (tôle)
ROOFING = {
    "roof_tile": ("roof_tile_b", 0.36, 0.5, 0.05), "roof_red": ("roof_red_b", 0.36, 0.5, 0.05),
    "roof_slate": ("roof_slate_b", 0.4, 0.55, 0.03), "lauze": ("lauze_b", 0.6, 0.85, 0.07),
    "roof_brown": ("roof_brown_b", 0.42, 0.45, 0.045), "copper": ("copper_b", 0.7, 0.9, 0.022),
    "roof_metal": None, "rust": None, "metal_dark": None, "wood_dark": ("wood", 0.32, 0.5, 0.03),
}
JOINT = {"wood_dark": "bark", "wood_honey": "wood", "wood_grey": "stone_dark", "wood_red": "wood_dark", "wood": "wood_dark"}


class Fac:
    """Façade verticale plane. axis = 'y' : perpendiculaire à Y, en y = pos, tournée vers « facing » (-1 = -Y).
    u = coordonnée le long de la façade (x pour axis 'y', y pour axis 'x'), out = distance devant le mur."""

    def __init__(self, g, axis, pos, facing, shutter_style="louvre", sash="white"):
        self.g, self.axis, self.pos, self.facing = g, axis, pos, facing
        self.face_dir = ('+' if facing > 0 else '-') + axis
        self.open_dir = ('-' if facing > 0 else '+') + axis
        self.shutter_style = shutter_style
        self.sash = sash
        self.holes = []                     # ouvertures posées (u0, u1, z0, z1) : évitées par les pierres en relief

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

    def face3(self, pts, color):
        """Face quelconque donnée en (u, out, z), orientée vers l'extérieur de la façade."""
        f = self.g.face([self.P(u, o, z) for u, o, z in pts], color)[0]
        f.normal_update()
        if f.normal.dot(_DV[self.face_dir]) < 0:
            f.normal_flip()
        return [f]

    def prism(self, pts, u0, u1, color):
        """Prisme de section (out, z) extrudé le long de la façade de u0 à u1."""
        sec = [(self.pos + o * self.facing, z) for o, z in pts]
        return self.g.prism(sec, u0, u1, color, axis='x' if self.axis == 'y' else 'y')

    def plate(self, pts, out0, out1, color):
        """Plaque découpée : profil (u, z) dans le plan de la façade, épaisseur de out0 à out1."""
        a, b = self.pos + out0 * self.facing, self.pos + out1 * self.facing
        return self.g.prism(pts, min(a, b), max(a, b), color, axis=self.axis)

    def tube(self, pts, radii, n, color, **kw):
        return self.g.tube([self.P(u, o, z) for u, o, z in pts], radii, n, color, **kw)

    # ---- ouvertures
    def glass(self, u, z, lit):
        return "window_lit" if self.g.chance(lit, *self.P(u, 0, z)) else "window"

    def _shutter_hd(self, uc, z, sw, h, color, hinge_side):
        """Volet ouvert rabattu contre le mur : à lames (persienne) ou à planches barrées, avec pentures."""
        g = self.g
        if self.shutter_style == "plank":
            for k in range(3):                        # trois planches, celle du milieu en relief (pas de joints plaqués)
                self.slab(uc - sw / 2 + sw * (k + 0.5) / 3, z, sw / 3, h, 0.04 + 0.014 * (k % 2), color)
            for zz in (z - h * 0.32, z + h * 0.32):   # barres, pentures
                self.slab(uc, zz, sw, 0.075, 0.035, color, out=0.04)
                self.slab(uc + hinge_side * sw * 0.12, zz, sw * 0.7, 0.035, 0.02, "metal_dark", out=0.075)
            self.plate([(uc - sw / 2, z - h * 0.28), (uc - sw / 2 + 0.07, z - h * 0.28), (uc + sw / 2, z + h * 0.28),
                        (uc + sw / 2 - 0.07, z + h * 0.28)], 0.04, 0.076, color)          # écharpe
            self.poly([(uc, z + 0.06), (uc + 0.05, z), (uc, z - 0.06), (uc - 0.05, z)], "black", 0.098)
        else:
            st = 0.055
            for s in (-1, 1):
                self.slab(uc + s * (sw / 2 - st / 2), z, st, h, 0.045, color)
            for zz in (z - h / 2 + 0.035, z, z + h / 2 - 0.035):
                self.slab(uc, zz, sw - 2 * st, 0.07, 0.045, color)
            self.quad(uc, z, sw - 2 * st, h - 0.07, color, 0.022)
            for (za, zb) in ((z - h / 2 + 0.07, z - 0.035), (z + 0.035, z + h / 2 - 0.07)):
                n = max(2, min(4, int((zb - za) / 0.13)))
                for k in range(n):
                    z0 = za + (zb - za) * k / n
                    z1 = za + (zb - za) * (k + 1) / n
                    self.face3([(uc - sw / 2 + st, 0.038, z0 + 0.01), (uc + sw / 2 - st, 0.038, z0 + 0.01),
                                (uc + sw / 2 - st, 0.012, z1), (uc - sw / 2 + st, 0.012, z1)], color)
            for zz in (z - h * 0.3, z + h * 0.3):
                self.slab(uc + hinge_side * (sw / 2 - 0.06), zz, 0.14, 0.035, 0.02, "metal_dark", out=0.045)

    def _window_hd(self, u, z, w, h, shutter, frame, col, flower, simple=False):
        """Fenêtre détaillée : tableau (jambages, linteau), appui saillant, vitre en retrait, dormant, croisillons."""
        g = self.g
        t, d = 0.1, 0.13
        sash = self.sash
        if frame:
            for s in (-1, 1):
                self.slab(u + s * (w / 2 + t / 2), z + t / 2, t, h + t, d, frame)
            self.slab(u, z + h / 2 + t / 2, w, t, d, frame)
            zs = z - h / 2
            self.prism([(0, zs - t), (d + 0.07, zs - t), (d + 0.07, zs - t + 0.05), (d, zs), (0, zs)], u - w / 2 - t - 0.04,
                       u + w / 2 + t + 0.04, frame)
        else:
            self.slab(u, z - h / 2 - 0.03, w + 0.1, 0.06, 0.09, "stone")
        self.quad(u, z, w, h, col, 0.015)
        sw_ = 0.055
        for s in (-1, 1):
            self.slab(u + s * (w / 2 - sw_ / 2), z, sw_, h, 0.05, sash)
            self.slab(u, z + s * (h / 2 - sw_ / 2), w - 2 * sw_, sw_, 0.05, sash)
        if w > 0.7:
            self.slab(u, z, 0.05, h - 2 * sw_, 0.068, sash)
        nb = 0 if simple or h < 0.75 else 1 if h < 1.3 else 2 if h < 1.75 else 3
        for k in range(nb):
            self.slab(u, z - h / 2 + h * (k + 1) / (nb + 1), w - 2 * sw_, 0.035, 0.044, sash)
        if shutter:
            sw = min(0.5, w * 0.5)
            for s in (-1, 1):
                self._shutter_hd(u + s * (w / 2 + t + sw / 2), z, sw - 0.03, h + 0.08, shutter, -s)
        if flower:
            self.flowerbox(u, z - h / 2 - 0.16, w + 0.25, flower)

    def window(self, u, z, w=0.9, h=1.25, shutter="shutter", frame="plaster_white", lit=0.5, flower=None,
               keep=True, cross=False, flat=False):
        """Fenêtre centrée en (u, z) : encadrement, vitre (éclairée ou non), volets ouverts, jardinière.
        flat : simple rectangle à tous les niveaux (façades arrière) ; keep : visible au niveau 2."""
        g = self.g
        col = self.glass(u, z, lit)
        sw = min(0.5, w * 0.5)
        if g.lod < 2:
            self.holes.append((u - w / 2 - 0.12, u + w / 2 + 0.12, z - h / 2 - 0.12, z + h / 2 + 0.12))
        if flat:
            if g.hd:                                  # façade arrière : vitre, croisée, appui
                self.quad(u, z, w, h, col, 0.015)
                self.slab(u, z, 0.05, h, 0.032, self.sash)
                self.slab(u, z + h * 0.18, w, 0.04, 0.05, self.sash)
                self.slab(u, z - h / 2 - 0.03, w + 0.1, 0.06, 0.08, "stone")
            elif g.lod < 2:
                self.quad(u, z, w, h, col, 0.03)
            return
        if g.hd:
            if not keep:                              # façades secondaires : fenêtre simplifiée, sans volets
                self._window_hd(u, z, w, h, None, frame, col, None, simple=True)
            else:
                self._window_hd(u, z, w, h, shutter, frame, col, flower)
        elif g.lod == 0:
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
        if self.g.lod < 2:
            self.holes.append((u - w / 2 - 0.1, u + w / 2 + 0.1, z0 - 0.1, z0 + h + 0.1))
        return self.poly(self.arch_pts(u, z0, w, h, 14 if self.g.hd else lod(self.g, 6, 4, 2)), color, out)

    def arch_window(self, u, z0, w, h, glass="window", surround="stone", lit=None, bars=2, depth=0.14, sash="white"):
        """Baie cintrée détaillée (hd) : archivolte en claveaux, vitre en retrait, meneau et traverses.
        Aux autres niveaux : simple baie plaquée (comme arch)."""
        g = self.g
        if lit is not None:
            glass = self.glass(u, z0, lit)
        if not g.hd:
            return self.arch(u, z0, w, h, glass, 0.05)
        self.holes.append((u - w / 2 - 0.3, u + w / 2 + 0.3, z0 - 0.1, z0 + h + 0.3))
        r = w / 2
        zs = z0 + h - r                               # naissance de l'arc
        t = 0.16
        self.poly(self.arch_pts(u, z0, w, h, 14), glass, 0.015)
        for s in (-1, 1):                             # piédroits
            self.slab(u + s * (r + t / 2), (z0 + zs) / 2, t, zs - z0, depth, surround)
        n = 14
        for k in range(n):                            # claveaux
            a0, a1 = math.pi * k / n, math.pi * (k + 1) / n
            pts = [(u + math.cos(a0) * r, zs + math.sin(a0) * r), (u + math.cos(a0) * (r + t), zs + math.sin(a0) * (r + t)),
                   (u + math.cos(a1) * (r + t), zs + math.sin(a1) * (r + t)), (u + math.cos(a1) * r, zs + math.sin(a1) * r)]
            self.plate(pts, 0.0, depth + (0.025 if k in (n // 2 - 1, n // 2) else 0.0), surround)
        self.prism([(0, z0 - 0.12), (depth + 0.08, z0 - 0.12), (depth + 0.08, z0 - 0.06), (depth, z0), (0, z0)],
                   u - r - t - 0.05, u + r + t + 0.05, surround)
        if sash and w > 0.9:
            self.slab(u, (z0 + zs + r * 0.9) / 2, 0.06, zs + r * 0.9 - z0, 0.06, sash)
        if sash:
            for k in range(bars):
                self.slab(u, z0 + (zs - z0) * (k + 1) / bars, w - 0.02, 0.05, 0.05, sash)

    def door(self, u, w=1.0, h=2.1, color="door", frame="plaster_white", z0=0.0, glass=False):
        g = self.g
        if g.lod < 2:
            self.holes.append((u - w / 2 - 0.15, u + w / 2 + 0.15, z0 - 0.05, z0 + h + 0.15))
        if g.hd:
            t, d = 0.11, 0.13
            is_glass = color.startswith("window")
            if frame:
                for s in (-1, 1):
                    self.slab(u + s * (w / 2 + t / 2), z0 + (h + t) / 2, t, h + t, d, frame)
                self.slab(u, z0 + h + t / 2, w, t, d, frame)
            if is_glass:                              # porte-fenêtre / porte vitrée
                self.quad(u, z0 + h / 2, w, h, color, 0.015)
                for s in (-1, 1):
                    self.slab(u + s * (w / 2 - 0.035), z0 + h / 2, 0.07, h, 0.05, self.sash)
                self.slab(u, z0 + h - 0.035, w - 0.14, 0.07, 0.05, self.sash)
                self.slab(u, z0 + 0.1, w - 0.14, 0.2, 0.05, self.sash)
                self.slab(u, z0 + h * 0.5, w - 0.14, 0.05, 0.035, self.sash)
                if w > 1.1:
                    self.slab(u, z0 + h / 2, 0.07, h - 0.1, 0.065, self.sash)
                self.slab(u + (0.1 if w > 1.1 else w * 0.34), z0 + h * 0.46, 0.035, 0.16, 0.05, "brass", out=0.05)
            else:
                self.slab(u, z0 + h / 2, w, h, 0.05, color)
                if w > 1.6:
                    self.slab(u, z0 + h / 2, 0.05, h, 0.065, "black" if color != "black" else "metal_dark")
                cols = 2 if w <= 1.6 else 4
                rows = (0.12, 0.42, 0.5, 0.92) if not glass else (0.12, 0.42)
                pw = w / cols
                for c in range(cols):
                    for k in range(0, len(rows), 2):
                        za, zb = z0 + h * rows[k], z0 + h * rows[k + 1]
                        self.slab(u - w / 2 + pw * (c + 0.5), (za + zb) / 2, pw - 0.16, zb - za, 0.022, color, out=0.05)
                if glass:
                    self.quad(u, z0 + h * 0.72, w * 0.62, h * 0.36, "window", 0.062)
                    self.slab(u, z0 + h * 0.72, 0.035, h * 0.36, 0.04, color, out=0.05)
                    self.slab(u, z0 + h * 0.72, w * 0.62, 0.035, 0.026, color, out=0.05)
                self.slab(u + w * 0.36, z0 + h * 0.48, 0.05, 0.2, 0.045, "brass", out=0.05)
                g.octa(self.P(u + w * 0.36, 0.12, z0 + h * 0.5), 0.045, "brass")
                self.slab(u, z0 + 0.09, w - 0.04, 0.16, 0.02, "metal_dark", out=0.05)
            if z0 < 0.2:                              # seuil de pierre
                self.box(u, z0 + 0.04, w + 2 * t + 0.1, 0.16, 0.36, "stone", out=0.0)
        elif g.lod == 0:
            if frame:
                self.slab(u, z0 + (h + 0.1) / 2, w + 0.22, h + 0.1, 0.05, frame)
            self.slab(u, z0 + h / 2, w, h, 0.08, color)
            if glass:
                self.quad(u, z0 + h * 0.68, w * 0.6, h * 0.36, "window", 0.09)
            self.slab(u + w * 0.34, z0 + h * 0.48, 0.06, 0.14, 0.12, "brass")
        else:
            self.quad(u, z0 + h / 2, w, h, color, 0.05)

    def flowerbox(self, u, z, w, flower="geranium", wood="wood"):
        g = self.g
        if g.lod > 0:
            return
        if not g.hd:
            self.slab(u, z, w, 0.18, 0.24, wood)
            f = self.box(u, z + 0.17, w - 0.06, 0.2, 0.26, flower, out=0.0)
            g.jitter(f, 0.035)
            self.box(u, z + 0.1, w - 0.02, 0.06, 0.3, "leaf", out=-0.01)
            return
        self.slab(u, z, w, 0.18, 0.24, wood)
        self.slab(u, z + 0.02, w + 0.04, 0.035, 0.26, JOINT.get(wood, "wood_dark"))
        for s in (-1, 1):
            self.prism([(0, z - 0.09), (0.2, z - 0.09), (0, z - 0.27)], u + s * (w / 2 - 0.12) - 0.025,
                       u + s * (w / 2 - 0.12) + 0.025, "metal_dark")
        n = max(2, int(w / 0.42))
        for k in range(n):                            # feuillage
            uu = u - w / 2 + w * (k + 0.5) / n
            f = g.ico(self.P(uu, 0.13, z + 0.13), 0.15, 1, "leaf" if k % 2 else "leaf_dark", scale=(1.15, 1.0, 0.7), auto=False)
        m = max(5, int(w / 0.16))
        for k in range(m):                            # fleurs une à une
            uu = u - w / 2 + 0.06 + (w - 0.12) * (k + g.D(0.2, 0.8)) / m
            c = flower if g.D() > 0.18 else ("flower_white" if flower != "flower_white" else "flower_pink")
            g.octa(self.P(uu, g.D(0.06, 0.22), z + g.D(0.2, 0.3)), g.D(0.05, 0.075), c, scale=(1, 1, 0.8))
        for k in range(2):                            # retombées
            uu = u + (k - 0.5) * w * 0.5
            g.octa(self.P(uu, 0.25, z + 0.02), 0.06, flower, scale=(1, 1, 1.2))
            g.octa(self.P(uu + 0.05, 0.26, z - 0.1), 0.05, "leaf", scale=(1, 1, 1.4))

    def _baluster(self, u, z, h, a, out0, out1, color):
        """Balustre en planche chantournée (profil en sablier)."""
        b = a * 0.42
        self.plate([(u - a, z), (u + a, z), (u + b, z + h * 0.45), (u + a, z + h), (u - a, z + h), (u - b, z + h * 0.45)],
                   out0, out1, color)

    def balcony(self, u0, u1, z, depth=1.1, wood="wood_dark", rail_h=1.0, flowers="geranium", style="plank",
                floor=None):
        """Balcon de u0 à u1, plancher à z. style 'plank' : garde-corps en bois ; 'iron' : fer forgé sombre."""
        g = self.g
        uc, L = (u0 + u1) / 2, u1 - u0
        panel = wood if style == "plank" else "metal_dark"
        floor = floor or (wood if style == "plank" else "concrete")
        if g.lod == 2:
            self.box(uc, z + rail_h * 0.42, L, rail_h * 0.95, depth, panel)
            return
        th = 0.06 if style == "plank" else 0.03
        ph = rail_h - (0.18 if style == "plank" else 0.05)
        if not g.hd:
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
            return
        # ---- hd
        trim = JOINT.get(wood, "wood_dark") if style == "plank" else "metal_dark"
        self.box(uc, z - 0.07, L, 0.14, depth, floor)
        self.box(uc, z - 0.07, L + 0.04, 0.17, 0.05, trim, out=depth - 0.03)                 # rive du plancher
        n = max(2, int(L / 2.2) + 1)
        for k in range(n):                            # consoles chantournées
            u = u0 + 0.12 + (L - 0.24) * k / (n - 1)
            self.prism([(0, z - 0.14), (depth * 0.9, z - 0.14), (depth * 0.9, z - 0.24), (depth * 0.45, z - 0.34),
                        (0.14, z - 0.62), (0, z - 0.7)], u - 0.06, u + 0.06, wood if style == "plank" else "stone")
        if style == "plank":
            zb, hb = z + 0.1, rail_h - 0.13
            self.box(uc, z + 0.07, L, 0.07, 0.08, wood, out=depth - 0.1)                    # lisse basse
            self.box(uc, z + rail_h, L + 0.1, 0.07, 0.15, trim, out=depth - 0.12)           # main courante
            nb = max(3, min(42, int(L / 0.21)))
            for k in range(nb):
                u = u0 + 0.1 + (L - 0.2) * (k + 0.5) / nb
                self._baluster(u, zb, hb, 0.07, depth - 0.075, depth - 0.045, wood)
            npost = max(2, int(L / 2.6) + 1)
            for k in range(npost):
                u = u0 + 0.05 + (L - 0.1) * k / (npost - 1)
                self.box(u, z + rail_h / 2 + 0.03, 0.1, rail_h + 0.06, 0.12, trim, out=depth - 0.12)
                g.octa(self.P(u, depth - 0.06, z + rail_h + 0.1), 0.06, trim)
            ns = max(2, int(depth / 0.22))
            for s in (-1, 1):                         # retours latéraux
                us = uc + s * (L / 2 - 0.03)
                self.box(us, z + rail_h, 0.07, 0.07, depth - 0.125, trim, out=0.0)
                for k in range(ns):
                    o = (depth - 0.14) * (k + 0.5) / ns
                    self.box(us, zb + hb / 2, 0.03, hb, 0.12, wood, out=o - 0.06)
        else:
            self.box(uc, z + rail_h, L, 0.045, 0.05, panel, out=depth - 0.05)
            self.box(uc, z + 0.1, L, 0.03, 0.03, panel, out=depth - 0.04)
            nb = max(4, min(40, int(L / 0.13)))
            for k in range(nb + 1):
                u = u0 + 0.02 + (L - 0.04) * k / nb
                self.slab(u, z + rail_h / 2 + 0.03, 0.022, rail_h - 0.1, 0.022, panel, out=depth - 0.04)
            for k in range(max(1, int(L / 1.3))):     # motif : anneaux entre lisses
                u = u0 + L * (k + 0.5) / max(1, int(L / 1.3))
                pts = [(u + math.cos(6.2832 * i / 8) * 0.16, z + rail_h * 0.55 + math.sin(6.2832 * i / 8) * 0.16) for i in range(8)]
                for i in range(8):
                    a, b = pts[i], pts[(i + 1) % 8]
                    self.g.beam(self.P(a[0], depth - 0.03, a[1]), self.P(b[0], depth - 0.03, b[1]), 0.02, panel)
            for s in (-1, 1):
                us = uc + s * (L / 2 - 0.015)
                self.box(us, z + rail_h, 0.045, 0.045, depth - 0.055, panel, out=0.0)
                self.box(us, z + 0.1, 0.03, 0.03, depth - 0.045, panel, out=0.0)
                for k in range(max(2, int(depth / 0.15))):
                    o = (depth - 0.06) * (k + 0.5) / max(2, int(depth / 0.15))
                    self.box(us, z + rail_h / 2 + 0.03, 0.022, rail_h - 0.1, 0.022, panel, out=o)
        if flowers:
            m = max(1, int(L / 3.2))
            seg = L / m
            for k in range(m):
                uu = u0 + seg * (k + 0.5)
                bw = min(1.5, seg * 0.6)
                bx = Fac(g, self.axis, self.pos + (depth - 0.02) * self.facing, self.facing)
                bx.flowerbox(uu, z + rail_h - 0.12, bw, flowers, wood if style == "plank" else "wood_dark")

    def awning(self, u, z, w, depth=1.3, drop=0.55, color="awning_red", stripe=None):
        """Store banne : pan incliné + lambrequin. stripe : seconde couleur en bandes (détail fin seulement)."""
        g = self.g
        if g.lod == 2:
            self.prism([(0, z), (depth, z - drop), (0, z - 0.12)], u - w / 2, u + w / 2, color)
            return
        n = max(2, int(w / 0.7)) if (stripe and g.lod == 0) else 1
        if g.hd:
            n = max(3, int(w / 0.55))
        seg = w / n
        for k in range(n):
            c = stripe if (stripe and k % 2 == 1) else color
            ua = u - w / 2 + seg * k
            if g.hd:
                self.prism([(0.06, z), (depth, z - drop), (depth - 0.01, z - drop - 0.03), (0.06, z - 0.04)], ua, ua + seg, c)
                zl = z - drop - 0.02                 # lambrequin festonné
                self.face3([(ua, depth, zl), (ua + seg, depth, zl), (ua + seg, depth + 0.01, zl - 0.14),
                            (ua + seg / 2, depth + 0.012, zl - 0.22), (ua, depth + 0.01, zl - 0.14)], c)
            else:
                self.prism([(0, z), (depth, z - drop), (depth, z - drop - 0.2), (depth - 0.03, z - drop - 0.03), (0, z - 0.06)],
                           ua, ua + seg, c)
        if g.hd:
            self.box(u, z + 0.02, w + 0.1, 0.12, 0.12, "metal_dark")                          # coffre
            for s in (-1, 1):
                ue = u + s * (w / 2 - 0.03)
                g.beam(self.P(ue, 0.06, z - drop - 0.3), self.P(ue, depth - 0.03, z - drop - 0.02), 0.03, "metal")
                self.face3([(ue + s * 0.05, 0.06, z), (ue + s * 0.05, depth, z - drop), (ue + s * 0.05, depth, z - drop - 0.16),
                            (ue + s * 0.05, 0.06, z - 0.16)], color)

    def sign(self, u, z, w, h, color, plate=None, depth=0.08):
        """Bandeau d'enseigne, avec une plaque contrastée au centre (pas de texte)."""
        g = self.g
        if g.lod == 2:
            self.quad(u, z, w, h, color, 0.05)
            return
        self.slab(u, z, w, h, depth, color)
        if g.hd:
            edge = plate or "metal_dark"
            for s in (-1, 1):                         # cadre mouluré
                self.slab(u, z + s * h / 2, w + 0.08, 0.04, depth + 0.025, edge)
                self.slab(u + s * w / 2, z, 0.04, h - 0.04, depth + 0.025, edge)
            if plate:                                 # faux lettrage en relief
                n = max(4, min(9, int(w / 0.55)))
                tw = w * 0.62
                x = u - tw / 2
                for k in range(n):
                    lw = tw / n * (0.55 + 0.35 * g.h(u, z, k))
                    self.slab(x + tw / n / 2, z, lw, h * (0.42 if k % 3 else 0.5), 0.02, plate, out=depth)
                    x += tw / n
        elif plate and g.lod == 0:
            self.slab(u, z, w * 0.5, h * 0.42, depth + 0.03, plate)

    def hanging_sign(self, u, z, color, plate=None, size=0.7):
        """Enseigne en potence, perpendiculaire à la façade (détail fin)."""
        g = self.g
        if g.lod > 0:
            return
        if not g.hd:
            self.box(u, z + size * 0.55, 0.05, 0.05, size + 0.25, "metal_dark")
            self.box(u, z, 0.06, size * 0.8, size, color, out=0.2)
            if plate:
                self.box(u, z, 0.08, size * 0.4, size * 0.5, plate, out=0.2 + size * 0.25)
            return
        zt = z + size * 0.58
        self.box(u, zt, 0.04, 0.04, size + 0.32, "metal_dark")                               # potence
        g.beam(self.P(u, 0.02, zt - 0.45), self.P(u, size * 0.7, zt - 0.03), 0.03, "metal_dark")
        g.octa(self.P(u, size + 0.34, zt), 0.05, "brass")
        for o in (0.25, 0.2 + size - 0.05):
            self.box(u, zt - 0.08, 0.015, 0.14, 0.015, "metal_dark", out=o)
        self.box(u, z, 0.05, size * 0.8, size, color, out=0.2)
        for o in (0.185, 0.185 + size):
            self.box(u, z, 0.1, size * 0.8, 0.03, "metal_dark", out=o)
        if plate:
            for s in (-1, 1):
                pts = [(0.2 + size * 0.5 + math.cos(6.2832 * i / 8) * size * 0.26, z + math.sin(6.2832 * i / 8) * size * 0.26)
                       for i in range(8)]
                f = g.face([self.P(u + s * 0.045, o, zz) for o, zz in pts], plate)[0]
                f.normal_update()
                side = _DV['+x'] if self.axis == 'y' else _DV['+y']
                if f.normal.dot(side) * s < 0:
                    f.normal_flip()

    def lantern(self, u, z, color="metal_dark"):
        """Lanterne de façade sur console (hd) : cage vitrée émissive."""
        g = self.g
        if not g.hd:
            return
        self.box(u, z + 0.32, 0.03, 0.03, 0.34, color)
        g.beam(self.P(u, 0.02, z + 0.05), self.P(u, 0.3, z + 0.3), 0.022, color)
        c = self.P(u, 0.32, z)
        g.frustum((c[0], c[1], z - 0.02), 0.07, 0.1, 0.3, 4, "lamp", rot=math.pi / 4, cap0=True, cap1=False)
        g.frustum((c[0], c[1], z + 0.28), 0.14, 0.0, 0.12, 4, color, rot=math.pi / 4)
        g.frustum((c[0], c[1], z - 0.06), 0.05, 0.075, 0.04, 4, color, rot=math.pi / 4)
        for k in range(4):
            a = math.pi / 4 + math.pi / 2 * k
            g.beam((c[0] + math.cos(a) * 0.072, c[1] + math.sin(a) * 0.072, z - 0.02),
                   (c[0] + math.cos(a) * 0.102, c[1] + math.sin(a) * 0.102, z + 0.28), 0.014, color)
        g.box((c[0], c[1], z + 0.36), (0.015, 0.015, 0.1), color)

    def clock(self, u, z, r=0.8, out=0.05):
        g = self.g
        n = 24 if g.hd else lod(g, 12, 8, 6)
        pts = [(u + math.cos(2 * math.pi * k / n) * r, z + math.sin(2 * math.pi * k / n) * r) for k in range(n)]
        if g.lod == 0:
            rim = [(u + (p[0] - u) * 1.14, z + (p[1] - z) * 1.14) for p in pts]
            if g.hd:
                self.plate(rim, out - 0.04, out + 0.01, "metal_dark")
            else:
                self.poly(rim, "metal_dark", out)
        self.poly(pts, "sign_white", out + (0.03 if g.hd else 0.02))
        if g.hd:
            for k in range(12):                       # index des heures
                a = 6.2832 * k / 12
                c, s = math.cos(a), math.sin(a)
                r0, r1, hw = r * (0.74 if k % 3 else 0.68), r * 0.92, r * (0.025 if k % 3 else 0.045)
                self.poly([(u + c * r0 - s * hw, z + s * r0 + c * hw), (u + c * r0 + s * hw, z + s * r0 - c * hw),
                           (u + c * r1 + s * hw, z + s * r1 - c * hw), (u + c * r1 - s * hw, z + s * r1 + c * hw)], "black", out + 0.045)
            for k, (a, L, hw) in enumerate(((1.15, r * 0.52, r * 0.055), (-0.35, r * 0.8, r * 0.04))):   # aiguilles effilées
                c, s = math.cos(a), math.sin(a)
                self.poly([(u - c * r * 0.12 - s * hw, z - s * r * 0.12 + c * hw), (u - c * r * 0.12 + s * hw, z - s * r * 0.12 - c * hw),
                           (u + c * L, z + s * L)], "black", out + 0.06 + 0.015 * k)
            g.octa(self.P(u, out + 0.085, z), r * 0.07, "brass")
        elif g.lod < 2:
            self.quad(u, z + r * 0.32, r * 0.1, r * 0.66, "black", out + 0.04)
            self.quad(u + r * 0.24, z, r * 0.5, r * 0.1, "black", out + 0.055)

    # ---- maçonnerie et bois (hd)
    def stonework(self, u0, u1, z0, z1, colors=("stone", "stone_warm", "rock_light"), row=0.42, fill=0.8, depth=0.045,
                  wmin=0.45, wmax=0.9):
        """Pierres apparentes en léger relief (hd), hors des ouvertures déjà posées sur cette façade."""
        g = self.g
        if not g.hd:
            return
        rows = max(1, round((z1 - z0) / row))
        rh = (z1 - z0) / rows
        for r in range(rows):
            zc = z0 + rh * (r + 0.5)
            u = u0 + (0.0 if r % 2 else 0.2)
            while u < u1 - 0.25:
                w = min(g.D(wmin, wmax), u1 - u)
                if g.D() < fill:
                    a, b = u + 0.02, u + w - 0.02
                    za, zb = zc - rh / 2 + 0.02, zc + rh / 2 - 0.02
                    if not any(a < h1 and b > h0 and za < hz1 and zb > hz0 for h0, h1, hz0, hz1 in self.holes):
                        self.slab((a + b) / 2, zc, b - a, rh - 0.04, depth * g.D(0.6, 1.0), colors[int(g.D() * len(colors)) % len(colors)])
                u += w

    def log_courses(self, u0, u1, z0, z1, wood, step=0.3):
        """Madriers : joints d'assise marqués par de fins couvre-joints sombres (hd)."""
        if not self.g.hd:
            return
        col = JOINT.get(wood, "wood_dark")
        z = z0 + step
        while z < z1 - 0.1:
            self.hband(z, u0, u1, 0.03, 0.022, col)
            z += step

    def hband(self, z, u0, u1, h, depth, color, gap=0.0):
        """Bande horizontale en relief, interrompue au droit des ouvertures posées sur cette façade."""
        cuts = sorted((a - gap, b + gap) for a, b, za, zb in self.holes if za < z + h / 2 and zb > z - h / 2)
        u = u0
        for a, b in cuts + [(u1, u1)]:
            if a > u + 0.05:
                self.slab((u + min(a, u1)) / 2, z, min(a, u1) - u, h, depth, color)
            u = max(u, b)
            if u >= u1:
                break

    def band(self, z, u0, u1, h=0.25, depth=0.12, color="plaster_white"):
        """Bandeau / corniche mouluré (profil en doucine, hd)."""
        self.prism([(0, z - h / 2), (depth * 0.45, z - h / 2), (depth * 0.6, z - h * 0.1), (depth, z + h * 0.15),
                    (depth, z + h / 2), (0, z + h / 2)], u0, u1, color)


# ----------------------------------------------------------------- couverture en relief (hd)
def shingles(g, a, b, c, d, color, course=0.4, width=0.6, thick=0.045, var=None, ref=(0, 0, 1), cap=420, key=0.0):
    """Rangs d'éléments de couverture décalés sur un pan a (bas gauche), b (bas droit), c (haut droit), d (haut gauche).
    Chaque élément : une face inclinée (relevée au bord bas) + sa tranche. Le pan est orienté vers « ref »."""
    a, b, c, d = Vector(a), Vector(b), Vector(c), Vector(d)
    nrm = (b - a).cross(d - a)
    if nrm.length < 1e-6:
        nrm = (b - a).cross(c - a)
    nrm.normalize()
    if nrm.dot(Vector(ref)) < 0:
        a, b, c, d = b, a, d, c
        nrm = -nrm
    Lv = ((d - a).length + (c - b).length) / 2
    Lu = ((b - a).length + (c - d).length) / 2
    est = (Lv / course) * (Lu / width)
    if est > cap:
        k = math.sqrt(est / cap)
        course *= k
        width *= k
    nc = max(1, round(Lv / course))
    var = var or color
    faces_a, faces_b = [], []
    bm = g.bm
    for k in range(nc):
        t0, t1 = k / nc, (k + 1) / nc
        l0, r0, l1, r1 = a.lerp(d, t0), b.lerp(c, t0), a.lerp(d, t1), b.lerp(c, t1)
        wk = (r0 - l0).length
        if wk < 0.08:
            continue
        ns = max(1, round(wk / width))
        cuts = [0.0] + [(i + (0.5 if k % 2 else 0.0)) / ns for i in range(1, ns + (1 if k % 2 == 0 else 1))]
        cuts = [x for x in cuts if 0.0 < x < 1.0]
        cuts = [0.0] + cuts + [1.0]
        down = (l0 - l1)
        down = down.normalized() if down.length > 1e-5 else Vector((0, 0, -1))
        for i in range(len(cuts) - 1):
            sa, sb = cuts[i], cuts[i + 1]
            if sb - sa < 0.02:
                continue
            j = (g.h(key, k, i, 1) - 0.5) * course * 0.22 if k > 0 else 0.0
            th = thick * (0.75 + 0.5 * g.h(key, k, i, 2))
            p0, p1 = l0.lerp(r0, sa) + down * j, l0.lerp(r0, sb) + down * j
            p2, p3 = l1.lerp(r1, sb), l1.lerp(r1, sa)
            v0, v1 = bm.verts.new(p0 + nrm * 0.02), bm.verts.new(p1 + nrm * 0.02)
            q0, q1 = bm.verts.new(p0 + nrm * (th + 0.03)), bm.verts.new(p1 + nrm * (th + 0.03))
            q2, q3 = bm.verts.new(p2 + nrm * 0.03), bm.verts.new(p3 + nrm * 0.03)
            fs = [bm.faces.new((q0, q1, q2, q3)), bm.faces.new((v0, v1, q1, q0))]
            (faces_b if g.h(key, k, i, 3) < 0.24 else faces_a).extend(fs)
    g.paint(faces_a, color)
    g.paint(faces_b, var)


def seams(g, a, b, c, d, color, step=0.6, ref=(0, 0, 1)):
    """Tôle à joints debout : nervures le long de la pente."""
    a, b, c, d = Vector(a), Vector(b), Vector(c), Vector(d)
    n = max(1, round((b - a).length / step))
    nrm = (b - a).cross(d - a).normalized()
    if nrm.dot(Vector(ref)) < 0:
        nrm = -nrm
    for i in range(n + 1):
        t = i / n
        g.beam(a.lerp(b, t) + nrm * 0.025, d.lerp(c, t) + nrm * 0.025, 0.045, color, 0.05)


def roofing(g, a, b, c, d, color, ref=(0, 0, 1), key=0.0, cap=420):
    """Habille un pan de toit selon sa couleur : tuiles / ardoises / lauzes / bardeaux en rangs, ou tôle nervurée."""
    if not g.hd:
        return
    spec = ROOFING.get(color, (color, 0.5, 0.7, 0.04))
    if spec is None:
        seams(g, a, b, c, d, "roof_metal_b" if color == "roof_metal" else color, ref=ref)
    else:
        shingles(g, a, b, c, d, color, spec[1], spec[2], spec[3], spec[0], ref, cap, key)


def gutter(g, axis, u, a0, a1, z, s, color="roof_metal"):
    """Gouttière pendante le long de « axis » ('x' ou 'y'), à la position transversale u, ouverte vers le haut."""
    pts = [(0, 0.0), (0.15, 0.0), (0.15, 0.11), (0.12, 0.11), (0.12, 0.03), (0.03, 0.03), (0.03, 0.11), (0, 0.11)]
    g.prism([(u + s * o, z + zz) for o, zz in pts], a0, a1, color, axis=axis)


def quoins(g, w, d, z0, z1, color="plaster_white", center=(0, 0), step=0.38, out=0.035, sides=True, inset=0.05):
    """Chaînes d'angle : blocs alternés aux quatre angles (hd). sides=False : façades avant et arrière seulement
    (maisons en rangée : rien ne dépasse des pignons)."""
    if not g.hd:
        return
    n = max(1, int((z1 - z0) / step))
    hh = (z1 - z0) / n
    for sx in (-1, 1):
        for sy in (-1, 1):
            for k in range(n):
                zc = z0 + hh * (k + 0.5)
                lx = 0.55 if k % 2 == 0 else 0.3
                ly = 0.3 if k % 2 == 0 else 0.55
                cx = center[0] + sx * (w / 2 - lx / 2 + (out if sides else 0.0))
                cy = center[1] + sy * (d / 2 - ly / 2 + out)
                if sides:
                    g.box((cx, cy, zc), (lx, ly, hh - 0.03), color)
                else:                                 # maisons en rangée : en retrait du plan de pignon
                    g.box((center[0] + sx * (w / 2 - inset - lx / 2), center[1] + sy * (d / 2 + out / 2), zc), (lx, out, hh - 0.03), color)


# ----------------------------------------------------------------- volumes
def foundation(g, w, d, color="stone", top=0.35, depth=3.0, center=(0, 0), grow=0.06, grow_y=None, stones=True):
    """Soubassement prolongé sous le sol (pose sur une pente). hd : pierres apparentes sur la plinthe.
    grow / grow_y : débord en x / en y (grow_y = grow par défaut)."""
    gy = grow if grow_y is None else grow_y
    f = g.box((center[0], center[1], (top - depth) / 2), (w + 2 * grow, d + 2 * gy, top + depth), color)
    if stones and g.hd and top >= 0.25 and color in ("stone", "stone_dark", "stone_warm", "wall_stone"):
        cols = (color, color, "rock_light" if color == "stone" else "stone")
        hx, hy = w / 2 + grow, d / 2 + gy
        for fac, a, b in ((Fac(g, 'y', center[1] - hy, -1), center[0] - hx, center[0] + hx),
                          (Fac(g, 'y', center[1] + hy, 1), center[0] - hx, center[0] + hx),
                          (Fac(g, 'x', center[0] - hx, -1), center[1] - hy, center[1] + hy),
                          (Fac(g, 'x', center[0] + hx, 1), center[1] - hy, center[1] + hy)):
            if fac.axis == 'x' and grow <= 0.001:
                continue
            fac.stonework(a, b, 0.0, top, cols, row=min(0.42, top), fill=0.8, depth=0.035)
    return f


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
               center=(0, 0), trim=None, gutters=True, pipes=True, rafters=None):
    """Toit à deux pans : deux dalles épaisses. over_e = débord des égouts, over_g = débord des pignons.
    trim : couleur des planches de rive. hd : couverture en rangs, faîtières, chevrons, gouttières, descentes.
    Renvoie la hauteur du faîtage."""
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
                hi = 0.12 if g.hd else 0.03              # rive : s'arrête 3 cm avant le faîtage (pas de face sur l'axe)
                g.prism([(s * e, ze - 0.06), (s * 0.03, zr - 0.03 * slope - 0.06), (s * 0.03, zr - 0.03 * slope + thick + hi),
                         (s * e, ze + thick + hi)], end * a - 0.02 * end, end * (a + 0.07), trim, axis=ridge)
    if g.hd:
        P = (lambda u, al, z: Vector((u, al, z))) if ridge == 'y' else (lambda u, al, z: Vector((al, u, z)))
        spec = ROOFING.get(color, (color,))
        cap_col = (spec[0] if spec else "roof_metal_b")
        rc = rafters or trim or "wood"
        for s in (-1, 1):
            roofing(g, P(s * e, -a, ze + thick), P(s * e, a, ze + thick), P(0, a, zr + thick), P(0, -a, zr + thick), color,
                    key=s * 1.0 + span)
            if over_e >= 0.3 and length > 1.5:        # abouts de chevrons sous l'égout
                n = max(2, int(2 * a / 0.85))
                for k in range(n):
                    al = -a + 0.2 + (2 * a - 0.4) * k / (n - 1)
                    g.beam(P(s * (half - 0.02), al, z_eave - 0.1), P(s * (e - 0.05), al, ze - 0.1), 0.09, rc, 0.13)
            if gutters and length > 2.5 and z_eave > 1.9:
                gutter(g, ridge, s * (e + 0.01), -a, a, ze - 0.02, s)
                if pipes and z_eave > 2.3:
                    ap = s * (length / 2 - 0.3)
                    wz = min(ze - 0.3, z_eave - 0.45)
                    g.tube([P(s * (e + 0.085), ap, ze + 0.0), P(s * (e + 0.085), ap, ze - 0.14), P(s * (half + 0.1), ap, wz),
                            P(s * (half + 0.1), ap, 0.35), P(s * (half + 0.3), ap, 0.14)], [0.05] * 5, 8, "roof_metal")
                    for zz in (wz - 0.3, 1.2):
                        if zz > 0.6:
                            g.box(P(s * (half + 0.07), ap, zz), (0.16, 0.16, 0.05), "metal_dark")
        n = max(1, int(2 * a / 0.6))                  # faîtières
        seg = 2 * a / n
        for k in range(n):
            dz = 0.012 if k % 2 else 0.0
            g.prism([(-0.19, zr + thick - 0.04), (0.19, zr + thick - 0.04), (0.07, zr + thick + 0.09 + dz), (-0.07, zr + thick + 0.09 + dz)],
                    -a + seg * k + 0.008, -a + seg * (k + 1) - 0.008, cap_col, axis=ridge)
    g.move(g.since(m), (center[0], center[1], 0))
    return zr


def roof_hip(g, w, d, z0, pitch, color, over=0.5, center=(0, 0)):
    """Toit à quatre pans (faîtage le long du grand côté) ; renvoie la hauteur du faîtage."""
    slope = math.tan(math.radians(pitch))
    ax, ay = w / 2 + over, d / 2 + over
    zb = z0 - over * slope
    m = g.mark()
    base = [(-ax, -ay, zb), (ax, -ay, zb), (ax, ay, zb), (-ax, ay, zb)]
    if ax >= ay:
        h, r = ay * slope, ax - ay
        top = [(-r, 0, zb + h), (r, 0, zb + h)]
        pans = [(base[0], base[1], top[1], top[0]), (base[2], base[3], top[0], top[1]), (base[1], base[2], top[1], top[1]),
                (base[3], base[0], top[0], top[0])]
    else:
        h, r = ax * slope, ay - ax
        top = [(0, -r, zb + h), (0, r, zb + h)]
        pans = [(base[1], base[2], top[1], top[0]), (base[3], base[0], top[0], top[1]), (base[0], base[1], top[0], top[0]),
                (base[2], base[3], top[1], top[1])]
    for p in pans:
        g.face(list(p[:3]) if p[2] == p[3] else list(p), color)
    g.face(list(reversed(base)), color)
    if g.hd:
        spec = ROOFING.get(color, (color,))
        cap_col = (spec[0] if spec else "roof_metal_b")
        for k, p in enumerate(pans):
            roofing(g, p[0], p[1], p[2], p[3], color, key=k + w)
        g.beam(top[0], top[1], 0.3, cap_col, 0.12) if r > 0.05 else None
        for bp, tp in ((base[0], top[0]), (base[1], top[1] if ax >= ay else top[0]), (base[2], top[1]),
                       (base[3], top[0] if ax >= ay else top[1])):
            g.beam(Vector(bp) + Vector((0, 0, 0.05)), Vector(tp) + Vector((0, 0, 0.05)), 0.22, cap_col, 0.1)
        for s in (-1, 1):
            gutter(g, 'x', s * (ay + 0.01), -ax, ax, zb - 0.1, s)
            gutter(g, 'y', s * (ax + 0.01), -ay, ay, zb - 0.1, s)
        for sx, sy in ((1, -1), (-1, 1)):
            g.tube([(sx * (ax - 0.3), sy * (ay + 0.085), zb - 0.08), (sx * (ax - 0.3), sy * (ay + 0.085), zb - 0.22),
                    (sx * (w / 2 - 0.3), sy * (d / 2 + 0.1), z0 - 0.55), (sx * (w / 2 - 0.3), sy * (d / 2 + 0.1), 0.35),
                    (sx * (w / 2 - 0.3), sy * (d / 2 + 0.3), 0.14)], [0.05] * 5, 8, "roof_metal")
    g.move(g.since(m), (center[0], center[1], 0))
    return zb + h


def roof_frustum(g, w, d, z0, h, inset, color, over=0.3, center=(0, 0), cap=None):
    """Brisis de toit mansardé : tronc de pyramide rectangulaire ; cap = couleur du dessus (None : ouvert)."""
    ax, ay = w / 2 + over, d / 2 + over
    bx, by = ax - inset, ay - inset
    m = g.mark()
    lo = [(-ax, -ay, z0), (ax, -ay, z0), (ax, ay, z0), (-ax, ay, z0)]
    hi = [(-bx, -by, z0 + h), (bx, -by, z0 + h), (bx, by, z0 + h), (-bx, by, z0 + h)]
    refs = [(0, -1, 0.3), (1, 0, 0.3), (0, 1, 0.3), (-1, 0, 0.3)]
    for k in range(4):
        j = (k + 1) % 4
        g.face([lo[k], lo[j], hi[j], hi[k]], color)
        if g.hd:
            roofing(g, lo[k], lo[j], hi[j], hi[k], color, ref=refs[k], key=k + w + d, cap=380)
            g.beam(Vector(lo[k]), Vector(hi[k]), 0.16, "roof_metal", 0.1)
    g.face(list(reversed(lo)), color)
    if cap:
        g.face(hi, cap)
        if g.hd:
            for k in range(4):
                j = (k + 1) % 4
                g.beam(Vector(hi[k]) + Vector((0, 0, 0.04)), Vector(hi[j]) + Vector((0, 0, 0.04)), 0.2, "roof_metal", 0.12)
    if g.hd:
        for s in (-1, 1):
            gutter(g, 'x', s * (ay + 0.01), -ax, ax, z0 - 0.1, s)
    g.move(g.since(m), (center[0], center[1], 0))
    return bx * 2, by * 2


def roof_pyramid(g, w, d, z0, h, color, over=0.3, center=(0, 0)):
    ax, ay = w / 2 + over, d / 2 + over
    m = g.mark()
    lo = [(-ax, -ay, z0), (ax, -ay, z0), (ax, ay, z0), (-ax, ay, z0)]
    refs = [(0, -1, 0.3), (1, 0, 0.3), (0, 1, 0.3), (-1, 0, 0.3)]
    for k in range(4):
        g.face([lo[k], lo[(k + 1) % 4], (0, 0, z0 + h)], color)
        if g.hd:
            roofing(g, lo[k], lo[(k + 1) % 4], (0, 0, z0 + h), (0, 0, z0 + h), color, ref=refs[k], key=k + w, cap=160)
    g.face(list(reversed(lo)), color)
    g.move(g.since(m), (center[0], center[1], 0))


def cone_roofing(g, c, r0, r1, h, n, color, rot=0.0, cap=140):
    """Habille un toit conique ou une flèche (mêmes paramètres que g.frustum) pan par pan (hd)."""
    if not g.hd:
        return
    c = Vector(c)
    for k in range(n):
        a0, a1 = rot + 6.2832 * k / n, rot + 6.2832 * (k + 1) / n
        am = (a0 + a1) / 2
        lo0 = c + Vector((math.cos(a0) * r0, math.sin(a0) * r0, 0))
        lo1 = c + Vector((math.cos(a1) * r0, math.sin(a1) * r0, 0))
        hi0 = c + Vector((math.cos(a0) * r1, math.sin(a0) * r1, h))
        hi1 = c + Vector((math.cos(a1) * r1, math.sin(a1) * r1, h))
        roofing(g, lo0, lo1, hi1, hi0, color, ref=(math.cos(am), math.sin(am), 0.3), key=k + r0, cap=cap)


def roof_shed(g, w, d, z_hi, z_lo, color, over=0.3, thick=0.16, center=(0, 0), low='-y'):
    """Toit à un pan (appentis) : haut côté opposé à « low »."""
    ax, ay = w / 2 + over, d / 2 + over
    m = g.mark()
    if low in ('-y', '+y'):
        s = -1 if low == '-y' else 1
        g.prism([(s * ay, z_lo), (-s * ay, z_hi), (-s * ay, z_hi + thick), (s * ay, z_lo + thick)], -ax, ax, color,
                axis='x')
        if g.hd:
            roofing(g, (-ax, s * ay, z_lo + thick), (ax, s * ay, z_lo + thick), (ax, -s * ay, z_hi + thick),
                    (-ax, -s * ay, z_hi + thick), color, key=w + d, cap=300)
            if z_lo > 1.9 and w > 2.0:
                gutter(g, 'x', s * (ay + 0.01), -ax, ax, z_lo - 0.02, s)
    else:
        s = -1 if low == '-x' else 1
        g.prism([(s * ax, z_lo), (-s * ax, z_hi), (-s * ax, z_hi + thick), (s * ax, z_lo + thick)], -ay, ay, color,
                axis='y')
        if g.hd:
            roofing(g, (s * ax, -ay, z_lo + thick), (s * ax, ay, z_lo + thick), (-s * ax, ay, z_hi + thick),
                    (-s * ax, -ay, z_hi + thick), color, key=w + d, cap=300)
            if z_lo > 1.9 and d > 2.0:
                gutter(g, 'y', s * (ax + 0.01), -ay, ay, z_lo - 0.02, s)
    g.move(g.since(m), (center[0], center[1], 0))


def chimney(g, x, y, z0, z1, color="stone", w=0.62, cap="stone_dark"):
    g.box((x, y, (z0 + z1) / 2), (w, w, z1 - z0), color)
    if g.hd:
        g.box((x, y, z1 - 0.22), (w + 0.1, w + 0.1, 0.12), color)                           # larmier
        g.box((x, y, z1 + 0.05), (w + 0.18, w + 0.18, 0.1), cap)
        for sx in (-1, 1):                            # mitre : dalle sur quatre plots
            for sy in (-1, 1):
                g.box((x + sx * (w / 2 - 0.08), y + sy * (w / 2 - 0.08), z1 + 0.2), (0.12, 0.12, 0.2), color)
        m = g.mark()
        g.prism([(-w / 2 - 0.12, z1 + 0.3), (w / 2 + 0.12, z1 + 0.3), (0, z1 + 0.5)], y - w / 2 - 0.1, y + w / 2 + 0.1, cap,
                axis='y')
        g.move(g.since(m), (x, 0, 0))
        g.box((x, y, z1 + 0.11), (w * 0.5, w * 0.5, 0.02), "black")
    elif g.lod == 0:
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
    if g.hd:
        for s in (-1, 1):                             # couverture, rives
            roofing(g, (s * (w / 2 + 0.15), -L / 2 - 0.12, 0.0), (s * (w / 2 + 0.15), L / 2, 0.0), (0, L / 2, w * 0.42),
                    (0, -L / 2 - 0.12, w * 0.42), roof, key=x + s, cap=36)
            g.beam((s * (w / 2 + 0.15), -L / 2 - 0.14, 0.0), (0, -L / 2 - 0.14, w * 0.42), 0.05, "plaster_white", 0.1)
        g.frustum((0, -L / 2 - 0.1, w * 0.42), 0.03, 0.0, 0.25, 4, "roof_metal")
    g.move(g.since(m), (x, yc if facing < 0 else yc, z_roof + h))
    f = Fac(g, 'y', y_front, facing)
    col = f.glass(x, z_roof, lit)
    if g.hd:
        f._window_hd(x, z_roof + h / 2, w * 0.62, h * 0.7, None, "plaster_white", col, None)
    else:
        f.quad(x, z_roof + h / 2, w * 0.62, h * 0.7, col, 0.03)


def flag(g, x, y, z0, h=5.0, color="red", size=(1.2, 0.8), pole="metal", rotz=0.0):
    """Mât + drapeau (le drapeau part vers +X avant rotation)."""
    m = g.mark()
    g.frustum((0, 0, z0), 0.05, 0.035, h, lod(g, 5, 4, 3), pole, cap0=False)
    if g.hd:
        g.octa((0, 0, z0 + h + 0.05), 0.07, "brass")
        n = 5
        zt, zb = z0 + h - 0.08, z0 + h - 0.08 - size[1]
        for k in range(n):                            # étoffe ondulée
            x0, x1 = 0.05 + size[0] * k / n, 0.05 + size[0] * (k + 1) / n
            y0, y1 = 0.09 * math.sin(k * 1.7), 0.09 * math.sin((k + 1) * 1.7)
            s0, s1 = 0.05 * k / n, 0.05 * (k + 1) / n
            p = [(x0, y0, zb - s0 * 3), (x1, y1, zb - s1 * 3), (x1, y1, zt - s1), (x0, y0, zt - s0)]
            g.face(p, color)
            g.face([(q[0], q[1] + 0.004, q[2]) for q in reversed(p)], color)
    else:
        g.box((size[0] / 2 + 0.05, 0, z0 + h - size[1] / 2 - 0.08), (size[0], 0.03, size[1]), color)
    g.move(g.since(m), (x, y, 0), rotz)


def cross(g, x, y, z, h=1.6, color="metal_dark", t=0.12, axis='x'):
    g.box((x, y, z + h / 2), (t, t, h), color)
    arm = (h * 0.55, t, t) if axis == 'x' else (t, h * 0.55, t)
    g.box((x, y, z + h * 0.68), arm, color)


def steps(g, u, y, w, n=3, rise=0.17, run=0.3, color="stone", facing=-1):
    """Perron : marches devant une porte (façade perpendiculaire à Y). hd : nez de marche."""
    if g.lod == 2:
        return
    for k in range(n):
        dd = run * (n - k)
        g.box((u, y + facing * dd / 2, rise * (k + 0.5)), (w, dd, rise), color)
        if g.hd:
            g.box((u, y + facing * (dd + 0.015), rise * (k + 1) - 0.025), (w + 0.04, 0.05, 0.05), "rock_light")
