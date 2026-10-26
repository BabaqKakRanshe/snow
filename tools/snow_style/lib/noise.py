"""vectorised noise and shaping helpers (from the Utility Plane livery)"""
import numpy as np


def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def mixc(c, col, t):
    """c (n,3) towards colour col (3,) or (n,3) by t (n,)"""
    t = np.clip(t, 0, 1)[:, None]
    return c * (1 - t) + np.asarray(col, np.float32) * t


def isin(part, s):
    return np.isin(part, list(s))


class GNoise:
    """3D gradient (Perlin) noise, vectorised"""
    def __init__(self, seed):
        rng = np.random.default_rng(seed)
        self.perm = np.concatenate([rng.permutation(256)] * 2).astype(np.int32)
        g = rng.normal(size=(256, 3))
        self.grad = (g / np.linalg.norm(g, axis=1, keepdims=True)).astype(np.float32)

    def __call__(self, p):
        pi = np.floor(p)
        f = (p - pi).astype(np.float32)
        pi = pi.astype(np.int64)
        X, Y, Z = pi[:, 0] & 255, pi[:, 1] & 255, pi[:, 2] & 255
        pm, gr = self.perm, self.grad
        A = pm[X] + Y
        B = pm[X + 1] + Y
        AA, AB, BA, BB = pm[A] + Z, pm[A + 1] + Z, pm[B] + Z, pm[B + 1] + Z
        x, y, z = f[:, 0], f[:, 1], f[:, 2]

        def g(h, dx, dy, dz):
            v = gr[pm[h]]
            return v[:, 0] * dx + v[:, 1] * dy + v[:, 2] * dz
        u = f * f * f * (f * (f * 6 - 15) + 10)
        ux, uy, uz = u[:, 0], u[:, 1], u[:, 2]
        x1, y1, z1 = x - 1, y - 1, z - 1
        a = lerp(g(AA, x, y, z), g(BA, x1, y, z), ux)
        b = lerp(g(AB, x, y1, z), g(BB, x1, y1, z), ux)
        c = lerp(g(AA + 1, x, y, z1), g(BA + 1, x1, y, z1), ux)
        d = lerp(g(AB + 1, x, y1, z1), g(BB + 1, x1, y1, z1), ux)
        return lerp(lerp(a, b, uy), lerp(c, d, uy), uz)


NOISES = [GNoise(s) for s in range(12)]


def fbm(p, freq, octaves=3, gain=0.5, seed=0, stretch=(1.0, 1.0, 1.0)):
    """fractal noise mapped to ~[0, 1] (mean 0.5)"""
    q = p * np.asarray(stretch, np.float32) * freq
    tot = np.zeros(len(p), np.float32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        tot += amp * NOISES[(seed + o) % len(NOISES)](q + np.float32(17.31 * (seed + 1) + 3.7 * o))
        norm += amp
        amp *= gain
        q = q * 2.03
    return np.clip(0.5 + 1.1 * tot / norm, 0, 1)


def dist_sorted(v, arr):
    """distance from v to the nearest value of the sorted array arr"""
    arr = np.asarray(arr, np.float64)
    i = np.clip(np.searchsorted(arr, v), 1, len(arr) - 1)
    return np.minimum(np.abs(v - arr[i - 1]), np.abs(v - arr[i]))


def dist_periodic(v, period, offset=0.0):
    w = (v - offset) / period
    return np.abs(w - np.round(w)) * period


def groove(d, width, aa):
    """1 on a panel line of full width `width` (antialiased over aa)"""
    return 1.0 - ss(width * 0.5 - aa, width * 0.5 + aa, d)


def rivets(d_perp, s_along, spacing, radius, aa, offset=0.0):
    da = dist_periodic(s_along, spacing, offset)
    r = np.sqrt(d_perp * d_perp + da * da)
    t = 1.0 - ss(radius - aa, radius + aa, r)
    dome = np.sqrt(np.clip(1 - (r / radius) ** 2, 0, 1))
    return t, dome


def seg_dist(p, a, b):
    """distance from points p (n,3) to the segment a-b"""
    a, b = np.asarray(a, np.float32), np.asarray(b, np.float32)
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(float(ab @ ab), 1e-9), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1)


