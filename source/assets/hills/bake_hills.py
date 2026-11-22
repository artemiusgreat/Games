#!/usr/bin/env python3
"""Bake the Maps-Hills terrain OBJ into the game's height grid.

Grid contract (Ascent.HillsSample): 512x512 float32 LE, row-major
[iz*512+ix]; cell (ix,iz) centre = world ((ix+0.5)/512*1900-950,
(iz+0.5)/512*1900-950). OBJ space maps linearly: obj = world*100/950
(the OBJ spans 200x200, heights in OBJ Y). Only up-facing triangles
form the top surface (the OBJ is a solid with walls and a bottom).
Vertical scale is the bake factor (3 = shipped 2026-11-22 morning,
10 = owner's pick the same evening).

Usage: bake_hills.py <obj> <factor> <out.bin>
"""
import sys, struct
import numpy as np

obj_path, factor, out_path = sys.argv[1], float(sys.argv[2]), sys.argv[3]

verts = []
faces = []
with open(obj_path) as f:
    for line in f:
        if line.startswith('v '):
            p = line.split()
            verts.append((float(p[1]), float(p[2]), float(p[3])))
        elif line.startswith('f '):
            idx = [int(t.split('/')[0]) - 1 for t in line.split()[1:]]
            for k in range(1, len(idx) - 1):
                faces.append((idx[0], idx[k], idx[k + 1]))
V = np.array(verts, dtype=np.float64)
F = np.array(faces, dtype=np.int64)
print(f"verts={len(V)} tris={len(F)} objX[{V[:,0].min():.1f},{V[:,0].max():.1f}] "
      f"objY[{V[:,1].min():.2f},{V[:,1].max():.2f}] objZ[{V[:,2].min():.1f},{V[:,2].max():.1f}]")

# up-facing triangles only (normal +Y)
a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
n = np.cross(b - a, c - a)
up = n[:, 1] > 0
A, B, C = a[up], b[up], c[up]
print(f"up-facing tris={len(A)}")

# bucket triangles by XZ AABB over the OBJ extent
N = 512
grid = np.zeros((N, N), dtype=np.float64)
cell = 4.0  # OBJ units per bucket
bx0, bz0 = V[:, 0].min(), V[:, 2].min()
nb_x = int(np.ceil((V[:, 0].max() - bx0) / cell))
nb_z = int(np.ceil((V[:, 2].max() - bz0) / cell))
buckets = {}
for ti in range(len(A)):
    xs = (A[ti, 0], B[ti, 0], C[ti, 0]); zs = (A[ti, 2], B[ti, 2], C[ti, 2])
    for bx in range(max(0, int((min(xs) - bx0) / cell)), min(nb_x - 1, int((max(xs) - bx0) / cell)) + 1):
        for bz in range(max(0, int((min(zs) - bz0) / cell)), min(nb_z - 1, int((max(zs) - bz0) / cell)) + 1):
            buckets.setdefault((bx, bz), []).append(ti)
print(f"buckets={len(buckets)}")

miss = 0
for iz in range(N):
    wz = (iz + 0.5) / N * 1900.0 - 950.0
    oz = wz * 100.0 / 950.0
    for ix in range(N):
        wx = (ix + 0.5) / N * 1900.0 - 950.0
        ox = wx * 100.0 / 950.0
        key = (min(nb_x - 1, max(0, int((ox - bx0) / cell))), min(nb_z - 1, max(0, int((oz - bz0) / cell))))
        best = None
        for ti in buckets.get(key, ()):  # barycentric containment, top hit wins
            ax, ay, az = A[ti]; bx_, by, bz_ = B[ti]; cx, cy, cz = C[ti]
            d = (bz_ - cz) * (ax - cx) + (cx - bx_) * (az - cz)
            if abs(d) < 1e-12: continue
            u = ((bz_ - cz) * (ox - cx) + (cx - bx_) * (oz - cz)) / d
            v = ((cz - az) * (ox - cx) + (ax - cx) * (oz - cz)) / d
            w = 1.0 - u - v
            if u >= -1e-9 and v >= -1e-9 and w >= -1e-9:
                h = u * ay + v * by + w * cy
                if best is None or h > best: best = h
        if best is None:
            miss += 1
            # fallback: nearest vertex height (footprint edges)
            d2 = (V[:, 0] - ox) ** 2 + (V[:, 2] - oz) ** 2
            best = V[int(np.argmin(d2)), 1]
        grid[iz, ix] = best * factor
print(f"misses (fallback)={miss}")
out = grid.astype('<f4')
with open(out_path, 'wb') as f:
    f.write(out.tobytes())
print(f"wrote {out_path}: min={out.min():.1f} max={out.max():.1f} mean={out.mean():.1f}")
