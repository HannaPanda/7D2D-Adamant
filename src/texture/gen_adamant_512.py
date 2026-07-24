"""
Procedural tileable 'Adamant' block texture: purple crystalline metal.
Outputs (all seamlessly tileable):
  adamant_diffuse.png   - base colour / diffuse
  adamant_normal.png   - tangent-space normal map (OpenGL +Y)
  adamant_specular.png - grayscale metallic/gloss mask
Size default 1024. numpy + PIL only.
"""
import numpy as np
from PIL import Image

N = 512
rng = np.random.default_rng(7)

# ---- tileable Voronoi (crystalline facets) --------------------------------
# Seed points on a jittered grid so the pattern tiles: we replicate seeds in a
# 3x3 block and take the minimum distance, which makes edges wrap seamlessly.
GRID = 9                      # GRID*GRID cells -> facet size
step = 1.0 / GRID
pts = []
for gy in range(GRID):
    for gx in range(GRID):
        jx = (gx + 0.5 + rng.uniform(-0.42, 0.42)) * step
        jy = (gy + 0.5 + rng.uniform(-0.42, 0.42)) * step
        pts.append((jx, jy))
pts = np.array(pts)                       # (P,2) in [0,1)

# tile the seed points across the 3x3 neighbourhood
offsets = np.array([(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
tiled = (pts[None, :, :] + offsets[:, None, :]).reshape(-1, 2)   # (9P,2)

xs = (np.arange(N) + 0.5) / N
gx, gy = np.meshgrid(xs, xs)
P = np.stack([gx.ravel(), gy.ravel()], axis=1)                   # (N*N,2)

# distance to nearest and 2nd-nearest seed (chunked to bound memory)
nearest_idx = np.empty(P.shape[0], dtype=np.int32)
d1 = np.empty(P.shape[0]); d2 = np.empty(P.shape[0])
CH = 65536
for i in range(0, P.shape[0], CH):
    seg = P[i:i+CH]
    dd = np.sqrt(((seg[:, None, :] - tiled[None, :, :]) ** 2).sum(-1))  # (ch,9P)
    order = np.argpartition(dd, 2, axis=1)[:, :2]
    two = np.take_along_axis(dd, order, axis=1)
    sortmask = two[:, 0] > two[:, 1]
    two[sortmask] = two[sortmask][:, ::-1]
    order[sortmask] = order[sortmask][:, ::-1]
    d1[i:i+CH] = two[:, 0]
    d2[i:i+CH] = two[:, 1]
    nearest_idx[i:i+CH] = order[:, 0]

d1 = d1.reshape(N, N); d2 = d2.reshape(N, N)
cell = (nearest_idx % (GRID * GRID)).reshape(N, N)   # collapse tiled copies

# edge = ridge between the two nearest cells -> crystal facet seams
edge = np.clip((d2 - d1) / step, 0, 1)               # 0 at seam, ~1 inside facet

# ---- tileable value noise (fine metal grain) ------------------------------
def tileable_noise(res, octaves=4):
    acc = np.zeros((N, N)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        r = res * (2 ** o)
        base = rng.random((r, r))
        img = np.array(Image.fromarray((base * 255).astype(np.uint8)).resize((N, N), Image.BICUBIC)) / 255.0
        acc += img * amp; tot += amp; amp *= 0.5
    return acc / tot
grain = tileable_noise(8)

# per-facet brightness so neighbouring crystals read as distinct planes
facet_tone = rng.uniform(0.72, 1.12, size=GRID * GRID)[cell]

# ---- height map (facets are raised, seams are grooves) --------------------
height = (0.55 * edge + 0.30 * (facet_tone - 0.72) / 0.4 + 0.15 * grain)
height = np.clip(height, 0, 1)

# ---- albedo: purple metal -------------------------------------------------
# base adamant purple ~ #7B4FB0, brightened on facet centres, darkened at seams
base = np.array([0.40, 0.24, 0.62])          # linear-ish purple
hi   = np.array([0.72, 0.58, 0.95])          # bright facet sheen
lo   = np.array([0.10, 0.05, 0.18])          # deep seam

shade = (0.60 * facet_tone + 0.30 * edge + 0.25 * (grain - 0.5))[..., None]
col = base[None, None, :] * (0.6 + 0.8 * shade)
col = col + (hi - base)[None, None, :] * np.clip((edge[..., None] - 0.55) * 1.6, 0, 1)
col = col - (base - lo)[None, None, :] * np.clip((0.35 - edge[..., None]) * 2.2, 0, 1)
# subtle cyan micro-sparkle in a few facets for a 'mythic' feel
spark = (grain > 0.93) & (edge > 0.5)
col[spark] += np.array([0.15, 0.35, 0.45])
col = np.clip(col, 0, 1)
Image.fromarray((col * 255).astype(np.uint8)).save("adamant_diffuse.png")

# ---- normal map from height (wrapped gradients -> tileable) ---------------
STR = 2.6
gx_ = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * STR
gy_ = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * STR
nz = np.ones_like(height)
nl = np.sqrt(gx_**2 + gy_**2 + nz**2)
nrm = np.stack([-gx_/nl, gy_/nl, nz/nl], axis=-1)     # OpenGL +Y up
Image.fromarray(((nrm * 0.5 + 0.5) * 255).astype(np.uint8)).save("adamant_normal.png")

# ---- specular / metallic mask (facets glossy, seams matte) ----------------
spec = np.clip(0.35 + 0.6 * edge + 0.2 * (facet_tone - 0.9), 0, 1)
spec = (spec * 255).astype(np.uint8)
Image.fromarray(np.stack([spec]*3, -1)).save("adamant_specular.png")

print("wrote adamant_diffuse.png, adamant_normal.png, adamant_specular.png  (%dx%d, tileable)" % (N, N))
