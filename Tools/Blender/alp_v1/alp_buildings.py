"""Habitat du pack alpin : chalets (pignon sur la vallée, grands débords, balcons fleuris) et maisons de ville
(accolables en rangée le long de X : pignons latéraux aveugles en x = ±w/2, façade sur rue en y = -d/2)."""
import math
from mathutils import Vector, Matrix
import alp_lib as A
from alp_lib import reg, lod
import alp_kit as K
from alp_kit import Fac


def spread(n, a, b):
    """n positions régulièrement réparties dans [a, b] (centres de travées)."""
    return [a + (b - a) * (k + 0.5) / n for k in range(n)]


# ----------------------------------------------------------------- chalets
def chalet(g, w=10.0, d=9.0, floors=2, ground="stone", wood="wood_dark", trim="wood_honey", roof="lauze",
           pitch=22, over_f=1.6, over_s=1.1, balconies=(1,), shutter="shutter", flowers="geranium",
           gh=2.5, fh=2.6, door_u=None, garage=False, annex=None, chim=True, knee=0.0, stones=False,
           frame="wood_honey"):
    """Chalet : rez en pierre ou enduit, étages en bois, pignon en façade (-Y), toit à faible pente.
    floors = niveaux droits (rez compris) ; knee = surcroît de mur sous le toit (m)."""
    z_eave = gh + (floors - 1) * fh + knee
    K.foundation(g, w, d, "stone")
    g.box((0, 0, gh / 2), (w, d, gh), ground)
    ww, dd = w + 0.16, d + 0.16
    zr = K.gable_body(g, ww, dd, gh, z_eave, pitch, wood)
    rise = zr - z_eave
    K.roof_gable(g, ww, dd, z_eave, pitch, roof, 'y', over_e=over_s, over_g=over_f, thick=0.28, trim=trim)
    yf = -d / 2
    front0 = Fac(g, 'y', yf, -1)
    front = Fac(g, 'y', yf - 0.08, -1)
    if g.lod == 0:
        # poteaux d'angle, sablière, pannes sous le débord, pierres de toit
        for sx in (-1, 1):
            for sy in (-1, 1):
                g.box((sx * (ww / 2 - 0.02), sy * (dd / 2 - 0.02), (gh + z_eave) / 2), (0.2, 0.2, z_eave - gh), trim)
        g.box((0, yf - 0.1, gh + 0.09), (ww + 0.1, 0.1, 0.18), trim)
        slope = math.tan(math.radians(pitch))
        for x in (-ww / 2 - over_s * 0.5, 0.0, ww / 2 + over_s * 0.5):
            z = zr - abs(x) * slope - 0.1
            g.box((x, yf - over_f / 2, z), (0.2, over_f + 0.3, 0.2), trim)
        if stones:
            for k in range(7):
                x = g.R(-ww / 2 - over_s * 0.6, ww / 2 + over_s * 0.6)
                y = g.R(-dd / 2 - over_f * 0.5, dd / 2 + over_f * 0.5)
                z = zr - abs(x) * slope + 0.3
                f = g.ico((x, y, z), g.R(0.16, 0.26), 1, "rock_light", scale=(1.2, 1, 0.6))

    # ---- façade avant : rez
    du = door_u if door_u is not None else (-w * 0.22 if not garage else -w * 0.3)
    gfr = "plaster_white" if ground != "plaster_white" else "stone"
    wide = w >= 15
    nb = 2 if w < 10.5 else 3 if not wide else max(4, round(w / 3.4))
    if wide:
        front0.door(du, 1.6, 2.3, "window_lit", frame=gfr)
        for u in spread(nb, -w / 2 + 0.6, w / 2 - 0.6):
            if abs(u - du) > 1.6:
                front0.window(u, gh * 0.55, 1.5, gh * 0.5, None, frame=gfr, lit=0.7)
    else:
        front0.door(du, 1.0, 2.05, "door", frame=gfr)
        if garage:
            gw = min(2.8, w * 0.3)
            if g.lod == 0:
                front0.slab(w * 0.2, 1.1, gw, 2.2, 0.06, "wood")
            else:
                front0.quad(w * 0.2, 1.1, gw, 2.2, "wood")
        else:
            for u in (du + 2.2, du + 4.2):
                if u < w / 2 - 0.9:
                    front0.window(u, 1.45, 0.9, 1.1, shutter, frame=gfr, flower=flowers)
        if du - 2.0 > -w / 2 + 0.9:
            front0.window(du - 2.0, 1.45, 0.9, 1.1, shutter, frame=gfr, flower=flowers)
    # ---- façade avant : étages (balcon filant + porte-fenêtre) et comble
    for L in range(1, floors):
        z0 = gh + (L - 1) * fh
        us = spread(nb, -w / 2 + 0.6, w / 2 - 0.6)
        if L in balconies:
            front.balcony(-w / 2 - 0.1, w / 2 + 0.1, z0 + 0.12, 1.15, wood, flowers=flowers)
            for k, u in enumerate(us):
                if (k == nb // 2) if not wide else (k % 2 == 1):
                    front.door(u, 0.95, 2.0, "window_lit" if g.chance(0.5, u, z0) else "window", frame=frame, z0=z0 + 0.12)
                else:
                    front.window(u, z0 + 1.45, 0.95, 1.15, shutter, frame=frame)
        else:
            for u in us:
                front.window(u, z0 + 1.45, 0.95, 1.15, shutter, frame=frame, flower=flowers)
    if rise + knee > 1.5:
        za = z_eave - knee
        n_att = 2 if (w > 11 and rise + knee > 2.2) else 1
        for u in spread(n_att, -w * 0.17 * n_att, w * 0.17 * n_att):
            front.window(u, za + (rise + knee) * 0.42, 0.8, 0.9, shutter, frame=frame, flower=flowers if knee > 0.5 else None)
        if floors in balconies:
            front.balcony(-w * 0.28, w * 0.28, za + (rise + knee) * 0.42 - 0.95, 0.95, wood, flowers=flowers)
    # ---- côtés
    ns = max(1, round(d / 3.6))
    for sx in (-1, 1):
        side0 = Fac(g, 'x', sx * w / 2, sx)
        side = Fac(g, 'x', sx * (w / 2 + 0.08), sx)
        us = spread(ns, -d / 2 + 0.5, d / 2 - 0.5)
        for u in us:
            if annex and sx == annex.get("side", 1) and u > -annex.get("d", 4) / 2 + annex.get("y", 0) - 1:
                continue
            side0.window(u, 1.45, 0.85, 1.0, shutter, frame=gfr)
        for L in range(1, floors):
            for u in us:
                side.window(u, gh + (L - 1) * fh + 1.45, 0.9, 1.1, shutter, frame=frame)
    # ---- arrière
    back0 = Fac(g, 'y', d / 2, 1)
    back = Fac(g, 'y', d / 2 + 0.08, 1)
    for u in spread(2, -w / 2 + 0.6, w / 2 - 0.6):
        back0.window(u, 1.45, 0.85, 1.0, None, frame=gfr, keep=False)
        for L in range(1, floors):
            back.window(u, gh + (L - 1) * fh + 1.45, 0.9, 1.1, shutter, frame=frame, keep=False)
    if g.lod < 2:
        back0.door(0, 0.9, 2.0, "door", frame=None)
    # ---- cheminée, annexe, escalier, banc et tas de bois
    if chim:
        slope = math.tan(math.radians(pitch))
        cx = w * 0.24
        K.chimney(g, cx, d * 0.12, z_eave, zr - cx * slope + 1.3, "stone", 0.7)
    if annex:
        aw, ad = annex.get("w", 3.2), annex.get("d", 4.5)
        sx = annex.get("side", 1)
        ax, ay = sx * (w / 2 + aw / 2), annex.get("y", d / 2 - ad / 2)
        ah = annex.get("h", 2.4)
        K.foundation(g, aw, ad, "stone", center=(ax, ay))
        g.box((ax, ay, ah / 2), (aw, ad, ah), annex.get("color", wood))
        K.roof_shed(g, aw, ad, ah + 0.9, ah, roof, over=0.35, center=(ax + sx * 0.1, ay), low='+x' if sx > 0 else '-x')
        af = Fac(g, 'y', ay - ad / 2, -1)
        if annex.get("garage"):
            if g.lod == 0:
                af.slab(ax, 1.05, aw - 0.8, 2.1, 0.06, "wood")
            else:
                af.quad(ax, 1.05, aw - 0.8, 2.1, "wood")
        else:
            af.door(ax, 0.9, 2.0, "door", frame=None)
    if g.lod == 0:
        # tas de bois contre le mur
        px = w / 2 + 0.28 if not (annex and annex.get("side", 1) > 0) else -w / 2 - 0.28
        for k in range(3):
            g.box((px, -d * 0.18 + k * 0.0, 0.25 + k * 0.42), (0.5, 2.4 - k * 0.3, 0.42), "wood_cut" if k % 2 == 0 else "wood")


def reg_chalet(iid, fname, seed, **kw):
    w, d = kw.get("w", 10.0), kw.get("d", 9.0)
    reg(dict(id=iid, file=fname, cat="chalet", tags=kw.pop("tags", ["habitat"]), seed=seed, fp=(w, d),
             build=lambda g, kw=kw: chalet(g, **kw)))


reg_chalet("chalet_a", "Chalet_A", 101, w=10.0, d=9.0, floors=2, ground="stone", wood="wood_dark", roof="lauze",
           balconies=(1,), shutter="shutter", knee=0.9, stones=True)
reg_chalet("chalet_b", "Chalet_B", 102, w=8.0, d=9.0, floors=2, ground="plaster_white", wood="wood_honey",
           trim="wood_dark", roof="roof_brown", pitch=25, balconies=(1,), shutter="barn", knee=0.4, over_f=1.4,
           frame="plaster_white")
reg_chalet("chalet_c", "Chalet_C", 103, w=13.0, d=11.0, floors=3, ground="plaster_white", wood="wood_dark",
           roof="roof_slate", pitch=21, balconies=(1, 2), shutter="shutter", over_f=1.9, over_s=1.3, garage=True,
           knee=0.3)
reg_chalet("chalet_d", "Chalet_D", 104, w=11.0, d=10.0, floors=2, ground="stone_warm", wood="wood_red",
           trim="wood_dark", roof="lauze", pitch=23, balconies=(1, 2), shutter="shutter_blue", knee=1.2,
           annex=dict(side=1, w=3.4, d=5.0, garage=True), stones=True)
# lot 2
reg_chalet("chalet_e", "Chalet_E", 105, w=14.0, d=12.0, floors=3, ground="stone", wood="wood_honey",
           trim="wood_dark", roof="roof_brown", pitch=20, balconies=(1, 2, 3), shutter="shutter", over_f=2.1,
           over_s=1.4, knee=1.4, garage=True, tags=["habitat", "large"])
reg_chalet("chalet_f", "Chalet_F", 106, w=6.5, d=6.0, floors=1, ground="stone", wood="wood_grey", trim="wood_dark",
           roof="lauze", pitch=24, balconies=(), shutter=None, flowers=None, knee=1.0, over_f=1.2, over_s=0.8,
           gh=2.3, stones=True, chim=True, tags=["habitat", "alpage", "small"])
reg_chalet("chalet_g", "Chalet_G", 107, w=9.0, d=10.0, floors=2, ground="plaster_cream", wood="wood_dark",
           trim="wood_honey", roof="roof_red", pitch=26, balconies=(1,), shutter="barn", knee=0.6,
           annex=dict(side=-1, w=3.0, d=4.0, color="wood_dark"))
reg_chalet("chalet_h", "Chalet_H", 108, w=12.0, d=9.5, floors=2, ground="plaster_white", wood="wood_grey",
           trim="wood_dark", roof="roof_slate", pitch=22, balconies=(1, 2), shutter="shutter_blue", knee=1.3,
           over_f=1.7, flowers="flower_pink", frame="plaster_white")


# ----------------------------------------------------------------- maisons de ville
SHOPS = {
    "bakery": dict(sign="sign_brown", plate="wood_cut", awning=("awning_cream", "awning_red"), hang="brass"),
    "cafe": dict(sign="awning_green", plate="awning_cream", awning=("awning_green", "awning_cream"), hang=None),
    "pharmacy": dict(sign="white", plate="neon_green", awning=None, hang="neon_green"),
    "ski": dict(sign="blue", plate="white", awning=("awning_blue", None), hang="red"),
    "grocery": dict(sign="green", plate="yellow", awning=("awning_green", "white"), hang=None),
    "restaurant": dict(sign="wood_dark", plate="brass", awning=("awning_red", None), hang="wood_honey"),
    "souvenir": dict(sign="red", plate="white", awning=("awning_red", "white"), hang="red"),
}


def townhouse(g, w=8.0, d=10.0, floors=3, plaster="plaster_cream", shop=None, roof="roof_tile", shutter="shutter",
              pitch=40, dormers=1, gable_front=False, gh=3.3, fh=2.9, band="plaster_white", balcony=None,
              chims=1, flowers=None, door_side=1, win_h=1.4, half_timber=None):
    """Maison de ville en rangée. floors = niveaux droits (rez compris). shop : clé de SHOPS ou None."""
    H = gh + (floors - 1) * fh
    yf = -d / 2
    K.foundation(g, w, d, "stone", top=0.5, grow=0.0)
    slope = math.tan(math.radians(pitch))
    if gable_front:
        zr = K.gable_body(g, w, d, 0.0, H, pitch, plaster, 'y')
        K.roof_gable(g, w, d, H, pitch, roof, 'y', over_e=0.12, over_g=0.55, thick=0.22, trim=band)
    else:
        zr = K.gable_body(g, d, w, 0.0, H, pitch, plaster, 'x')
        K.roof_gable(g, d, w, H, pitch, roof, 'x', over_e=0.5, over_g=0.0, thick=0.22)
    front = Fac(g, 'y', yf, -1)
    back = Fac(g, 'y', d / 2, 1)
    bays = max(2, round(w / 2.7))
    us = spread(bays, -w / 2 + 0.25, w / 2 - 0.25)
    if g.lod == 0:
        front.slab(0, gh + 0.02, w, 0.22, 0.1, band)                              # bandeau au-dessus du rez
        if not gable_front:
            front.slab(0, H - 0.14, w, 0.28, 0.16, band)                          # corniche
    # ---- rez-de-chaussée
    du = door_side * (w / 2 - 1.0)
    if shop:
        s = SHOPS[shop]
        vw = w - 2.6
        vu = -door_side * 0.75
        if g.lod == 0:
            front.slab(vu, 1.55, vw + 0.24, 2.3, 0.07, "wood_dark" if shop in ("bakery", "restaurant") else band)
            front.slab(vu, 1.6, vw, 2.0, 0.1, "window_lit")
            for k in range(1, max(2, round(vw / 1.6))):
                front.slab(vu - vw / 2 + vw * k / max(2, round(vw / 1.6)), 1.6, 0.07, 2.0, 0.12, "wood_dark" if shop in ("bakery", "restaurant") else band)
            front.slab(vu, 0.3, vw + 0.24, 0.6, 0.12, "stone" if shop != "ski" else band)
        else:
            front.quad(vu, 1.6, vw, 2.0, "window_lit", 0.04)
        front.door(du, 1.0, 2.2, "door" if shop != "pharmacy" else "window", frame=band, glass=True)
        front.sign(vu, gh - 0.42, vw + 0.3, 0.5, s["sign"], s["plate"])
        if s["awning"]:
            front.awning(vu, gh - 0.72, vw + 0.3, 1.3, 0.5, s["awning"][0], s["awning"][1])
        if s["hang"]:
            front.hanging_sign(du - door_side * 0.9, gh + 0.9, s["sign"], s["hang"])
        if shop == "pharmacy" and g.lod < 2:
            cu, cz = du - door_side * 0.2, gh + 1.0
            front.box(cu, cz, 0.22, 0.7, 0.12, "neon_green", out=0.55)
            front.box(cu, cz, 0.7, 0.22, 0.12, "neon_green", out=0.55)
            front.box(cu, cz, 0.06, 0.06, 0.6, "metal_dark", out=0.0)
    else:
        front.door(du, 1.05, 2.25, "door", frame=band)
        if g.lod == 0:
            front.slab(du, 2.55, 1.05, 0.4, 0.06, "window")                         # imposte
        for u in us:
            if abs(u - du) > 1.3:
                front.window(u, 1.75, 1.0, win_h, shutter, frame=band, flower=flowers)
    # ---- étages
    for L in range(1, floors):
        z = gh + (L - 1) * fh + 1.5
        for k, u in enumerate(us):
            fl = flowers if (L == 1 or g.chance(0.4, u, z, 7)) else None
            if balcony == L and 0 < k < bays - 1 or (balcony == L and bays <= 2):
                front.window(u, z - 0.25, 1.0, win_h + 0.5, shutter, frame=band)
            else:
                front.window(u, z, 1.0, win_h, shutter, frame=band, flower=fl)
        if balcony == L:
            a, b = (us[1] - 0.9, us[-2] + 0.9) if bays > 2 else (us[0] - 0.8, us[-1] + 0.8)
            front.balcony(a, b, z - 1.0, 0.85, style="iron", rail_h=0.95, flowers=flowers)
        for u in us:
            back.window(u, z, 0.95, win_h, None, frame=None, keep=False)
    if g.lod < 2:
        back.door(-du, 0.95, 2.1, "door", frame=None)
        for u in us:
            if abs(u + du) > 1.2:
                back.window(u, 1.75, 0.95, 1.2, None, frame=None, keep=False)
    # ---- colombages décoratifs sur le dernier niveau (détail)
    if half_timber and g.lod == 0:
        zt = gh + (floors - 2) * fh
        for u in [-w / 2 + 0.12] + [(us[k] + us[k + 1]) / 2 for k in range(bays - 1)] + [w / 2 - 0.12]:
            front.slab(u, zt + fh / 2 + 0.1, 0.16, fh - 0.25, 0.05, half_timber)
        front.slab(0, zt + 0.12, w, 0.16, 0.05, half_timber)
    # ---- toit : pignon sur rue ou lucarnes ; cheminées
    if gable_front:
        rise = zr - H
        if rise > 1.6:
            front.window(0, H + rise * 0.36, 0.9, 1.1, shutter, frame=band)
        if rise > 3.2 and g.lod < 2:
            front.window(0, H + rise * 0.72, 0.6, 0.6, None, frame=band)
        for k in range(chims):
            cx = (w * 0.25) * (1 if k == 0 else -1)
            K.chimney(g, cx, d * (0.1 - 0.3 * k), H + 0.5, zr - abs(cx) * slope + 1.0, plaster, 0.6)
    else:
        y_d = yf + 0.9
        z_d = H + (d / 2 - abs(y_d)) * slope
        for x in spread(dormers, -w / 2 + 0.8, w / 2 - 0.8) if dormers else []:
            K.dormer(g, x, y_d, z_d, slope, -1, wall=plaster, roof=roof)
        for k in range(chims):
            cx = (w / 2 - 0.55) * (1 if k == 0 else -1)
            K.chimney(g, cx, 0.9, zr - 1.2, zr + 0.9, plaster, 0.6)


def reg_town(iid, fname, seed, **kw):
    w, d = kw.get("w", 8.0), kw.get("d", 10.0)
    tags = ["row"] + (["shop", kw["shop"]] if kw.get("shop") else [])
    reg(dict(id=iid, file=fname, cat="townhouse", tags=tags, seed=seed, fp=(w, d),
             build=lambda g, kw=kw: townhouse(g, **kw)))


reg_town("townhouse_a", "TownHouse_A", 201, w=8.0, d=10.0, floors=3, plaster="plaster_cream", shop="bakery",
         roof="roof_tile", shutter="shutter", dormers=2)
reg_town("townhouse_b", "TownHouse_B", 202, w=6.0, d=9.0, floors=4, plaster="plaster_pink", roof="roof_slate",
         shutter="plaster_white", dormers=1, flowers="geranium")
reg_town("townhouse_c", "TownHouse_C", 203, w=10.0, d=11.0, floors=3, plaster="plaster_ochre", shop="cafe",
         roof="roof_brown", shutter="shutter", dormers=3, balcony=1, flowers="geranium")
reg_town("townhouse_d", "TownHouse_D", 204, w=7.0, d=10.0, floors=3, plaster="plaster_blue", roof="roof_slate",
         shutter="plaster_white", dormers=1, door_side=-1, flowers="flower_pink")
reg_town("townhouse_e", "TownHouse_E", 205, w=9.0, d=10.0, floors=4, plaster="plaster_white", shop="pharmacy",
         roof="roof_tile", shutter="shutter_blue", dormers=2, door_side=-1)
reg_town("townhouse_f", "TownHouse_F", 206, w=11.0, d=11.0, floors=3, plaster="plaster_yellow", roof="roof_red",
         shutter="shutter", dormers=3, balcony=2, flowers="geranium", chims=2)
# lot 2
reg_town("townhouse_g", "TownHouse_G", 207, w=9.0, d=10.0, floors=3, plaster="plaster_green", shop="ski",
         roof="roof_slate", shutter="plaster_white", dormers=2)
reg_town("townhouse_h", "TownHouse_H", 208, w=7.0, d=9.0, floors=3, plaster="plaster_salmon", shop="grocery",
         roof="roof_tile", shutter="shutter", dormers=1, door_side=-1, flowers="geranium")
reg_town("townhouse_i", "TownHouse_I", 209, w=10.0, d=11.0, floors=3, plaster="plaster_white", shop="restaurant",
         roof="roof_brown", shutter="barn", dormers=0, gable_front=True, pitch=38, half_timber="wood_dark",
         flowers="geranium")
reg_town("townhouse_j", "TownHouse_J", 210, w=7.5, d=10.0, floors=4, plaster="plaster_cream", roof="roof_slate",
         shutter="shutter_blue", gable_front=True, pitch=45, dormers=0, flowers="geranium", door_side=-1)
reg_town("townhouse_k", "TownHouse_K", 211, w=8.5, d=10.0, floors=3, plaster="plaster_pink", shop="souvenir",
         roof="roof_tile", shutter="plaster_white", dormers=2, flowers="flower_yellow")
