"""Outils du générateur de carte (numpy + PIL) : bruit, splines, champs de distance, échantillonnage du relief.

Repère : x = est, z = nord (celui d'Unity), hauteurs en mètres. Les grilles sont indexées [iz, ix].
"""
import math
import numpy as np
from PIL import Image

# ----------------------------------------------------------------- bruit


def _hash(ix, iz, seed):
    h = (ix.astype(np.uint64) * np.uint64(374761393) + iz.astype(np.uint64) * np.uint64(668265263)
         + np.uint64(seed * 1442695041 + 12345)) & np.uint64(0xffffffff)
    h = ((h ^ (h >> np.uint64(13))) * np.uint64(1274126177)) & np.uint64(0xffffffff)
    return h ^ (h >> np.uint64(16))


def perlin(x, z, seed=0):
    """Bruit de gradient 2D, environ dans [-1, 1]."""
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    xi = np.floor(x).astype(np.int64)
    zi = np.floor(z).astype(np.int64)
    xf = x - xi
    zf = z - zi
    u = xf * xf * xf * (xf * (xf * 6 - 15) + 10)
    v = zf * zf * zf * (zf * (zf * 6 - 15) + 10)

    def g(ax, az, dx, dz):
        a = (_hash(ax + 100000, az + 100000, seed) & np.uint64(0xffff)).astype(np.float64) * (2 * math.pi / 65536.0)
        return np.cos(a) * dx + np.sin(a) * dz
    n00 = g(xi, zi, xf, zf)
    n10 = g(xi + 1, zi, xf - 1, zf)
    n01 = g(xi, zi + 1, xf, zf - 1)
    n11 = g(xi + 1, zi + 1, xf - 1, zf - 1)
    return ((n00 * (1 - u) + n10 * u) * (1 - v) + (n01 * (1 - u) + n11 * u) * v) * 1.5


def fbm(x, z, octaves=4, seed=0, lac=2.0, gain=0.5):
    out = 0.0
    amp, freq, total = 1.0, 1.0, 0.0
    for o in range(octaves):
        out = out + perlin(np.asarray(x) * freq, np.asarray(z) * freq, seed + o * 17) * amp
        total += amp
        amp *= gain
        freq *= lac
    return out / total


def ridged(x, z, octaves=4, seed=0):
    out = 0.0
    amp, freq, total = 1.0, 1.0, 0.0
    for o in range(octaves):
        n = 1.0 - np.abs(perlin(np.asarray(x) * freq, np.asarray(z) * freq, seed + o * 31))
        out = out + n * n * amp
        total += amp
        amp *= 0.5
        freq *= 2.0
    return out / total


def hash01(i, seed=0):
    """Hasard déterministe 0-1 à partir d'entiers."""
    i = np.asarray(i, dtype=np.int64)
    return (_hash(i, i * 7 + 3, seed) & np.uint64(0xffffff)).astype(np.float64) / float(0xffffff)


# ----------------------------------------------------------------- outils scalaires
def smoothstep(a, b, x):
    t = np.clip((np.asarray(x, dtype=np.float64) - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def smin(a, b, k):
    """Minimum lissé (k = largeur de raccord en mètres)."""
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def gauss1d(a, sigma):
    """Lissage gaussien d'un profil 1D (bords prolongés)."""
    if sigma <= 0.01 or len(a) < 3:
        return np.array(a, dtype=np.float64)
    r = max(1, int(sigma * 3))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    p = np.pad(np.asarray(a, dtype=np.float64), r, mode='edge')
    return np.convolve(p, k, mode='valid')


def blur2d(a, sigma):
    """Flou gaussien séparable d'une grille."""
    if sigma <= 0.01:
        return a
    r = max(1, int(sigma * 3))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    p = np.pad(a, ((0, 0), (r, r)), mode='edge')
    out = np.zeros_like(a, dtype=np.float64)
    for i, w in enumerate(k):
        out += w * p[:, i:i + a.shape[1]]
    p = np.pad(out, ((r, r), (0, 0)), mode='edge')
    out2 = np.zeros_like(out)
    for i, w in enumerate(k):
        out2 += w * p[i:i + a.shape[0], :]
    return out2


def upsample(a, shape):
    """Agrandit une grille (mêmes bornes, nœuds alignés) : interpolation bilinéaire puis léger flou."""
    n0, n1 = a.shape[0], shape[0]
    f = np.linspace(0, n0 - 1, n1)
    i = np.minimum(np.floor(f).astype(np.int64), n0 - 2)
    u = f - i
    rows = a[i, :] * (1 - u)[:, None] + a[i + 1, :] * u[:, None]
    out = rows[:, i] * (1 - u)[None, :] + rows[:, i + 1] * u[None, :]
    return blur2d(out, 0.6 * (n1 - 1) / (n0 - 1))


# ----------------------------------------------------------------- splines
def catmull(points, step, closed=False):
    """Spline de Catmull-Rom centripète passant par les points (n, k) ; rééchantillonnée tous les « step » mètres
    (distance mesurée sur les 2 premières colonnes). Renvoie (m, k)."""
    P = np.asarray(points, dtype=np.float64)
    n = len(P)
    if n < 2:
        return P.copy()
    if n == 2:
        dense = np.linspace(P[0], P[1], max(2, int(np.linalg.norm(P[1, :2] - P[0, :2]) / (step * 0.25)) + 1))
    else:
        if closed:
            ext = np.vstack([P[-1], P, P[0], P[1]])
        else:
            ext = np.vstack([P[0] * 2 - P[1], P, P[-1] * 2 - P[-2]])
        dense = []
        segs = n if closed else n - 1
        for i in range(segs):
            p0, p1, p2, p3 = ext[i], ext[i + 1], ext[i + 2], ext[i + 3]
            t0 = 0.0
            t1 = t0 + max(1e-4, np.linalg.norm(p1[:2] - p0[:2])) ** 0.5
            t2 = t1 + max(1e-4, np.linalg.norm(p2[:2] - p1[:2])) ** 0.5
            t3 = t2 + max(1e-4, np.linalg.norm(p3[:2] - p2[:2])) ** 0.5
            m = max(4, int(np.linalg.norm(p2[:2] - p1[:2]) / (step * 0.25)))
            for k in range(m):
                u = t1 + (t2 - t1) * k / m
                a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1
                a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2
                a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3
                b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2
                b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3
                dense.append((t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2)
        dense.append(ext[segs + 1])
        dense = np.array(dense)
    return resample(dense, step)


def arclen(P):
    d = np.linalg.norm(np.diff(P[:, :2], axis=0), axis=1)
    return np.concatenate([[0.0], np.cumsum(d)])


def resample(P, step):
    """Rééchantillonne une polyligne à pas constant (≈ step, ajusté pour tomber juste aux extrémités)."""
    P = np.asarray(P, dtype=np.float64)
    s = arclen(P)
    L = s[-1]
    n = max(1, int(round(L / step)))
    t = np.linspace(0.0, L, n + 1)
    return np.stack([np.interp(t, s, P[:, k]) for k in range(P.shape[1])], axis=1)


def tangents(P):
    """Tangentes unitaires (plan xz) d'une polyligne."""
    d = np.gradient(P[:, :2], axis=0)
    n = np.linalg.norm(d, axis=1, keepdims=True)
    return d / np.maximum(n, 1e-9)


def curvature(P, ds):
    """Courbure signée (1/m) d'une polyligne à pas constant ; positive = virage à gauche (sens trigonométrique)."""
    t = tangents(P)
    ang = np.unwrap(np.arctan2(t[:, 1], t[:, 0]))
    return np.gradient(ang) / ds


# ----------------------------------------------------------------- champs de distance
def polyline_field(X, Z, pts):
    """Distance de chaque point de grille à une polyligne (n, k>=2), et attributs interpolés au point le plus proche.
    Renvoie (distance, attributs[k-2 grilles], abscisse curviligne)."""
    pts = np.asarray(pts, dtype=np.float64)
    best = np.full(X.shape, 1e18)
    k = pts.shape[1] - 2
    attrs = [np.zeros(X.shape) for _ in range(k)]
    sbest = np.zeros(X.shape)
    s = arclen(pts)
    for i in range(len(pts) - 1):
        ax, az = pts[i, 0], pts[i, 1]
        dx, dz = pts[i + 1, 0] - ax, pts[i + 1, 1] - az
        l2 = dx * dx + dz * dz
        if l2 < 1e-9:
            continue
        t = np.clip(((X - ax) * dx + (Z - az) * dz) / l2, 0.0, 1.0)
        d2 = (X - ax - t * dx) ** 2 + (Z - az - t * dz) ** 2
        m = d2 < best
        best = np.where(m, d2, best)
        for j in range(k):
            attrs[j] = np.where(m, pts[i, 2 + j] + t * (pts[i + 1, 2 + j] - pts[i, 2 + j]), attrs[j])
        sbest = np.where(m, s[i] + t * (s[i + 1] - s[i]), sbest)
    return np.sqrt(best), attrs, sbest


def point_polyline(p, pts):
    """Distance d'un point à une polyligne, abscisse et indice du segment."""
    pts = np.asarray(pts, dtype=np.float64)
    a = pts[:-1, :2]
    d = pts[1:, :2] - a
    l2 = np.maximum((d * d).sum(axis=1), 1e-12)
    t = np.clip(((p[0] - a[:, 0]) * d[:, 0] + (p[1] - a[:, 1]) * d[:, 1]) / l2, 0, 1)
    q = a + d * t[:, None]
    dist = np.hypot(q[:, 0] - p[0], q[:, 1] - p[1])
    i = int(np.argmin(dist))
    return float(dist[i]), i, float(t[i])


# ----------------------------------------------------------------- grille de relief
class Grid:
    """Grille carrée centrée sur l'origine : n × n nœuds espacés de « step » mètres."""

    def __init__(self, half, step):
        self.half = float(half)
        self.step = float(step)
        self.n = int(round(2 * half / step)) + 1
        ax = np.linspace(-half, half, self.n)
        self.X, self.Z = np.meshgrid(ax, ax)

    def index(self, x, z):
        return (np.asarray(x) + self.half) / self.step, (np.asarray(z) + self.half) / self.step

    def window(self, x0, z0, x1, z1):
        """Tranches (z, x) de la grille couvrant un rectangle en mètres."""
        ix0 = int(max(0, math.floor((x0 + self.half) / self.step)))
        ix1 = int(min(self.n - 1, math.ceil((x1 + self.half) / self.step)))
        iz0 = int(max(0, math.floor((z0 + self.half) / self.step)))
        iz1 = int(min(self.n - 1, math.ceil((z1 + self.half) / self.step)))
        return slice(iz0, iz1 + 1), slice(ix0, ix1 + 1)

    def sample(self, A, x, z):
        """Interpolation bilinéaire d'une grille en des points quelconques."""
        fx, fz = self.index(x, z)
        fx = np.clip(fx, 0, self.n - 1.001)
        fz = np.clip(fz, 0, self.n - 1.001)
        ix = np.floor(fx).astype(np.int64)
        iz = np.floor(fz).astype(np.int64)
        u = fx - ix
        v = fz - iz
        return (A[iz, ix] * (1 - u) * (1 - v) + A[iz, ix + 1] * u * (1 - v)
                + A[iz + 1, ix] * (1 - u) * v + A[iz + 1, ix + 1] * u * v)

    def gradient(self, A, x, z, eps=6.0):
        gx = (self.sample(A, x + eps, z) - self.sample(A, x - eps, z)) / (2 * eps)
        gz = (self.sample(A, x, z + eps) - self.sample(A, x, z - eps)) / (2 * eps)
        return gx, gz


def slope_map(H, step):
    gz, gx = np.gradient(H, step)
    return np.hypot(gx, gz)


# ----------------------------------------------------------------- aperçu
def hillshade(H, step, az=315.0, alt=42.0):
    gz, gx = np.gradient(H, step)
    a, e = math.radians(az), math.radians(alt)
    lx, lz, ly = math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e)
    nx, nz, ny = -gx, -gz, np.ones_like(H)
    nn = np.sqrt(nx * nx + nz * nz + ny * ny)
    return np.clip((nx * lx + nz * lz + ny * ly) / nn, 0, 1)
