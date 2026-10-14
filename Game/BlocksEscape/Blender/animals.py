# Generates chubby animal blocks for every BlocksEscape block shape, plus each animal's gate cell.
# Run inside Blender:  exec(open(r"...\Blender\animals.py").read()); build("Pig")   (or build_all())
#
# Blender units: 1 m = 1 cell. Level footprint (x right, y down) maps to Blender cells (x, -y).
# Pivot = bottom centre of footprint cell (0,0). The face lies on top; its up-direction is +Y.
# Gate cells: pivot at the cell's floor centre, exit direction +Y (Unity +Z after export).
# Exports go to Assets/Prefabs/Block/<Animal>/ and Assets/Prefabs/Gate/<Animal>/; then run
# "Blocks Escape/Animals/Build All Animal Prefabs" in Unity.
import bpy, bmesh, math, random, os, numpy as np
from mathutils import Vector, Matrix, Euler
from mathutils.geometry import delaunay_2d_cdt

GAME = r"D:\Dev\RootLevel.It\root-level-blocks-escape\Game\BlocksEscape"
ARC = 3                          # segments per 90° corner
TEX = 4096; D_CAP = 320; D_SIDE = 224; PAD = 6
GATE_TEX = 1024; TILE = 0.94; TILE_H = 0.06; TOP_PX = 800

# name: (level footprint, face centre in level coords) - taken from the Block prefabs (2x2: face on its top row)
SHAPES = {
    "1x1":          ([(0, 0)], (0, 0)),
    "2x1":          ([(0, -1), (0, 0)], (0, -1)),
    "2x2":          ([(0, -1), (1, -1), (0, 0), (1, 0)], (0.5, -1)),
    "3x1":          ([(0, -2), (0, -1), (0, 0)], (0, -2)),
    "4x1":          ([(0, -3), (0, -2), (0, -1), (0, 0)], (0, -3)),
    "Corner":       ([(0, -1), (1, -1), (0, 0)], (0, -1)),
    "CornerMirror": ([(-1, -1), (0, -1), (0, 0)], (0, -1)),
    "L":            ([(-1, -2), (0, -2), (0, -1), (0, 0)], (0, -2)),
    "LMirror":      ([(0, -2), (1, -2), (0, -1), (0, 0)], (0, -2)),
    "S":            ([(1, -1), (2, -1), (0, 0), (1, 0)], (1, -0.5)),
    "SMirror":      ([(-2, -1), (-1, -1), (-1, 0), (0, 0)], (-1, -0.5)),
    "T":            ([(0, -2), (0, -1), (1, -1), (0, 0)], (0, -1)),
}

def hexc(h):
    h = h.lstrip('#'); return np.array([int(h[i:i+2], 16) / 255 for i in (0, 2, 4)], np.float32)
WHITE = hexc("#FFFFFF"); EYE_DARK = hexc("#1E1614"); WOOD = hexc("#7A4A26"); WOOD_D = hexc("#4E2C14")
BELL = hexc("#EDB43A"); STRAP = hexc("#B8281C")

# ======================================================================= body geometry
class Body:
    def __init__(s, R=0.16, HGT=0.92, GAP=0.08, BULGE=0.025, BULGE_TOP=0.035, SEGS=2, SCALLOP=0.0):
        s.R, s.HGT, s.GAP, s.BULGE, s.BULGE_TOP, s.SEGS, s.SCALLOP = R, HGT, GAP, BULGE, BULGE_TOP, SEGS, SCALLOP
        s.RHO = R + max(BULGE, SCALLOP) + 0.06   # inner-corner fillet radius on the top cap
        s.S = 1 - GAP                            # body width of one cell = face span
        rad = math.radians
        # (horizontal offset from the core outline, height, side-bulge weight, scallop weight)
        s.PROFILE = ([(R*math.cos(rad(p)), R + R*math.sin(rad(p)), 0, w) for p, w in ((-45, .4), (-22.5, .7), (0, 1))]
                     + [(R, HGT/2, 1, 1)]
                     + [(R*math.cos(rad(p)), HGT - R + R*math.sin(rad(p)), 0, w) for p, w in ((0, 1), (30, .8), (60, .45), (90, 0))])

def outline(cells):
    S = set(cells); nxt = {}
    for (x, y) in cells:  # CCW edges of each cell side without a neighbour
        if (x, y-1) not in S: nxt[(x-.5, y-.5)] = (x+.5, y-.5)
        if (x+1, y) not in S: nxt[(x+.5, y-.5)] = (x+.5, y+.5)
        if (x, y+1) not in S: nxt[(x+.5, y+.5)] = (x-.5, y+.5)
        if (x-1, y) not in S: nxt[(x-.5, y+.5)] = (x-.5, y-.5)
    start = next(iter(nxt)); loop = [start]
    while True:
        p = nxt[loop[-1]]
        if p == start: break
        loop.append(p)
    pts = [np.array(p, float) for p in loop]; keep = []
    for i in range(len(pts)):  # drop collinear points
        a, b, c = pts[i-1], pts[i], pts[(i+1) % len(pts)]
        if abs((b-a)[0]*(c-b)[1] - (b-a)[1]*(c-b)[0]) > 1e-9: keep.append(b)
    return keep

def corners(cells, B):
    out = outline(cells); n = len(out); d = B.GAP/2 + B.R; res = []
    for i in range(n):
        a, v, b = out[i-1], out[i], out[(i+1) % n]
        e1 = (v-a)/np.linalg.norm(v-a); e2 = (b-v)/np.linalg.norm(b-v)
        n1 = np.array([e1[1], -e1[0]]); n2 = np.array([e2[1], -e2[0]])
        res.append(dict(v=v - d*(n1+n2), n1=n1, n2=n2, convex=e1[0]*e2[1] - e1[1]*e2[0] > 0,
                        seg=int(round(np.linalg.norm(b-v))) * B.SEGS))
    return res

def ring(cs, B, t, wb, ws):
    pts = []
    def corner_pts(c):
        out = []
        for k in range(ARC+1):
            th = math.pi/2*k/ARC; dd = math.cos(th)*c["n1"] + math.sin(th)*c["n2"]
            out.append(c["v"] + t*dd if c["convex"] else c["v"] + B.RHO*(c["n1"]+c["n2"]) - (B.RHO-t)*dd)
        return out
    for i, c in enumerate(cs):
        cp = corner_pts(c); pts += cp
        nx0 = corner_pts(cs[(i+1) % len(cs)])[0]
        nb = max(1, c["seg"] // 2)  # scallop bumps along this edge
        for m in range(1, c["seg"]):
            s = m/c["seg"]
            off = B.BULGE*wb*4*s*(1-s) + B.SCALLOP*ws*(0.5 - 0.5*math.cos(2*math.pi*s*nb))
            pts.append(cp[-1]*(1-s) + nx0*s + c["n2"]*off)
    return pts

def inside(px, py, poly):
    x, y = poly[:, 0], poly[:, 1]; x2, y2 = np.roll(x, -1), np.roll(y, -1); res = np.zeros(px.shape, bool)
    for i in range(len(poly)):
        res ^= ((y[i] > py) != (y2[i] > py)) & (px < (x2[i]-x[i])*(py-y[i])/(y2[i]-y[i]+1e-12) + x[i])
    return res

def edge_dist(px, py, poly):
    a = poly; b = np.roll(poly, -1, axis=0); best = np.full(px.shape, 1e9)
    for i in range(len(poly)):
        ab = b[i]-a[i]; t = np.clip(((px-a[i][0])*ab[0] + (py-a[i][1])*ab[1]) / (ab @ ab + 1e-12), 0, 1)
        best = np.minimum(best, np.hypot(px - (a[i][0]+t*ab[0]), py - (a[i][1]+t*ab[1])))
    return best

def build_shape(cells, B):
    cs = corners(cells, B)
    rings = [ring(cs, B, t, wb, ws) for t, z, wb, ws in B.PROFILE]
    mid = np.array(rings[3]); seg = np.linalg.norm(np.roll(mid, -1, axis=0) - mid, axis=1)
    ucol = np.concatenate([[0], np.cumsum(seg)])
    vrow = [0.0]
    for k in range(1, len(B.PROFILE)):
        vrow.append(vrow[-1] + math.hypot(B.PROFILE[k][0]-B.PROFILE[k-1][0], B.PROFILE[k][1]-B.PROFILE[k-1][1]))
    Bd = []
    for p in rings[-1]:
        if not Bd or np.linalg.norm(p - Bd[-1]) > 1e-6: Bd.append(p)
    if np.linalg.norm(Bd[0] - Bd[-1]) < 1e-6: Bd.pop()
    Bd = np.array(Bd)
    mn, mx = Bd.min(0), Bd.max(0); step = 0.26
    gx, gy = np.meshgrid(np.arange(mn[0]+step/2, mx[0], step), np.arange(mn[1]+step/2, mx[1], step))
    gx, gy = gx.ravel(), gy.ravel()
    ok = inside(gx, gy, Bd) & (edge_dist(gx, gy, Bd) > 0.09)
    pts2 = [Vector(p) for p in Bd] + [Vector((x, y)) for x, y in zip(gx[ok], gy[ok])]
    cdt = delaunay_2d_cdt(pts2, [], [list(range(len(Bd)))], 2, 1e-7)
    cv = np.array([[v.x, v.y] for v in cdt[0]])
    k = np.clip(edge_dist(cv[:, 0], cv[:, 1], Bd) / 0.35, 0, 1)
    cz = B.HGT + B.BULGE_TOP * k*k*(3 - 2*k)
    return dict(rings=rings, ucol=ucol, vrow=vrow, cap_v=cv, cap_z=cz, cap_f=cdt[2], cap_min=mn, cap_max=mx)

# ======================================================================= painting helpers
def blend(col, sd, c, aa, alpha=1.0):
    a = (np.clip(0.5 - sd/aa, 0, 1) * alpha)[:, None]; col[:] = col*(1-a) + c*a

def ell(x, y, cx, cy, rx, ry):
    return (np.sqrt(((x-cx)/rx)**2 + ((y-cy)/ry)**2) - 1) * min(rx, ry)

def poly_sd(x, y, pts, w):
    best = np.full(x.shape, 1e9)
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        dx, dy = bx-ax, by-ay; L = dx*dx + dy*dy + 1e-12
        t = np.clip(((x-ax)*dx + (y-ay)*dy)/L, 0, 1)
        best = np.minimum(best, np.hypot(x-ax-t*dx, y-ay-t*dy))
    return best - w

class Face:
    """Paints in face units: u in [0,1] across, v in [0,1] bottom (chin) to top (horns), on a face of span S."""
    def __init__(s, col, X, Z, S, aa): s.c, s.X, s.Z, s.S, s.aa = col, X, Z, S, aa
    def e(s, u, v, ru, rv): return ell(s.X, s.Z, (u-.5)*s.S, v*s.S, ru*s.S, rv*s.S)
    def line(s, pts, w): return poly_sd(s.X, s.Z, [((u-.5)*s.S, v*s.S) for u, v in pts], w*s.S)
    def arc(s, cu, cv, r, a0, a1, w, n=14):
        return s.line([(cu + r*math.cos(a), cv + r*math.sin(a)) for a in np.linspace(a0, a1, n)], w)
    def put(s, sd, c, alpha=1.0, aa=None): blend(s.c, sd, c, aa or s.aa, alpha)
    # common features
    def smile(s, cu, cv, r, w, c): s.put(s.arc(cu, cv, r, math.pi*1.15, math.pi*1.85, w), c)
    def blush(s, cv=.45, du=.33, c=hexc("#F28C8C"), alpha=.45):
        for sx in (-1, 1): s.put(s.e(.5+sx*du, cv, .07, .045), c, alpha, aa=.02)
    def shiny_eye(s, cu, cv, ru, rv, c=EYE_DARK, hl=1.0):
        s.put(s.e(cu, cv, ru, rv), c)
        s.put(s.e(cu - ru*.3, cv + rv*.3, ru*.33*hl, ru*.33*hl), WHITE)
        s.put(s.e(cu + ru*.3, cv - rv*.35, ru*.14*hl, ru*.14*hl), WHITE)

def sample_surface(tris, rng, zmin=0.12, zmax=9.0):
    P = [t[1] for t in tris if zmin < t[1][:, 2].mean() < zmax]
    areas = np.array([np.linalg.norm(np.cross(p[1]-p[0], p[2]-p[0]))/2 for p in P])
    def one():
        p = P[rng.choices(range(len(P)), weights=areas)[0]]
        a, b = rng.random(), rng.random()
        if a + b > 1: a, b = 1-a, 1-b
        return p[0] + a*(p[1]-p[0]) + b*(p[2]-p[0])
    return one, areas.sum()

def blobs(tris, rng, density, rmin, rmax, keep=lambda c, r: True, zmin=0.12, zmax=9.0, gap=0.03, sat=(2, 4)):
    """Irregular spots: clusters of spheres centred on the surface, evaluated in 3D so they wrap edges."""
    one, area = sample_surface(tris, rng, zmin, zmax)
    n_target = int(round(area*density)); spheres = []; centers = []; tries = 0
    while len(centers) < n_target and tries < 20000:
        tries += 1
        c = one(); r0 = rng.uniform(rmin, rmax)
        if not keep(c, r0): continue
        if any(np.linalg.norm(c - q) < r0 + rq + gap for q, rq in centers): continue
        centers.append((c, r0)); spheres.append((c, r0))
        for _ in range(rng.randint(*sat)):
            off = np.array([rng.uniform(-1, 1) for _ in range(3)]); off *= rng.uniform(.07, .14)*r0/0.17/max(np.linalg.norm(off), 1e-6)
            spheres.append((c + off, r0*rng.uniform(.5, .8)))
    return spheres

def blob_sd(P, spheres):
    sd = np.full(len(P), 1e9, np.float32)
    for c, r in spheres: sd = np.minimum(sd, np.linalg.norm(P - c, axis=1) - r)
    return sd

def wool(P, col, tris, rng, base, shade, keep=lambda c: True, spacing=0.15, rmin=.1, rmax=.13):
    """Cloud-like wool puffs: each pixel is shaded by how close it is to the rim of its nearest puff."""
    one, area = sample_surface(tris, rng, zmin=0.02)
    pts = np.zeros((0, 3)); rs = []; tries = 0; n_target = int(area / (spacing*spacing) * 1.1)
    while len(rs) < n_target and tries < 40000:
        tries += 1; c = one()
        if not keep(c): continue
        if len(pts) and np.min(np.linalg.norm(pts - c, axis=1)) < spacing*0.85: continue
        pts = np.vstack([pts, c]); rs.append(rng.uniform(rmin, rmax))
    q = np.full(len(P), 9.0, np.float32)
    for c, r in zip(pts, rs): q = np.minimum(q, np.linalg.norm(P - c, axis=1) / r)
    t = np.clip((q - 0.45) / 0.55, 0, 1) ** 1.6
    col[:] = base*(1-t[:, None]) + shade*t[:, None]

# ======================================================================= accessories (3D)
class Acc:
    def __init__(s, bm, uvl, sw, rect_uv): s.bm, s.uvl, s.sw, s.rect_uv = bm, uvl, sw, rect_uv
    def _uv(s, verts, fn):
        for f in {f for v in verts for f in v.link_faces}:
            up = f.calc_center_median().z > 0
            for l in f.loops: l[s.uvl].uv = fn(up, l.vert.co)
    def cone(s, M, key, segs=6, r1=.07, r2=.012, depth=.22, scale=(1, 1, 1)):
        ret = bmesh.ops.create_cone(s.bm, cap_ends=True, segments=segs, radius1=r1, radius2=r2, depth=depth,
                                    matrix=Matrix.Translation((0, 0, depth/2)))
        s._uv(ret["verts"], lambda up, co: s.sw(key))
        bmesh.ops.transform(s.bm, matrix=M @ Matrix.Diagonal((*scale, 1)), verts=ret["verts"])
    def blob(s, M, scale, key_top, key_bottom=None, seg=8, rings=5):
        """UV sphere scaled to `scale` (local x, y, z); key_top may be 'ear' (pink-inner rect) or a swatch."""
        ret = bmesh.ops.create_uvsphere(s.bm, u_segments=seg, v_segments=rings, radius=1.0)
        def fn(up, co):
            key = key_top if up or key_bottom is None else key_bottom
            return s.rect_uv("ear", co.x*.5+.5, co.y*.5+.5) if key == "ear" else s.sw(key)
        s._uv(ret["verts"], fn)
        bmesh.ops.transform(s.bm, matrix=M @ Matrix.Diagonal((*scale, 1)), verts=ret["verts"])

def T(x, y, z): return Matrix.Translation((x, y, z))
def Rx(d): return Matrix.Rotation(math.radians(d), 4, 'X')
def Ry(d): return Matrix.Rotation(math.radians(d), 4, 'Y')
def Rz(d): return Matrix.Rotation(math.radians(d), 4, 'Z')

def side_free(ctx, sx, dy=0.16):
    """True when no cell continues the head sideways, i.e. an ear can hang on the side wall."""
    fx, fy = ctx["face"]
    return (math.floor(fx + sx*.75 + .5), math.floor(fy + dy + .5)) not in ctx["cells"]

# ======================================================================= animals
class Animal:
    name = "Animal"; type_id = "animal"; has_gate = True
    body = Body()
    base = hexc("#FFFFFF")
    ear_base = None; ear_inner = hexc("#F2A0A0")
    swatches = {}
    seed = 0
    def pattern(s, P, col, ctx): pass             # body pattern on every body pixel (world positions)
    def face(s, f): pass                           # face painting
    def tail(s, x, y, col, ctx, aa): pass          # top-of-tail-end decoration (world x, y on top)
    def accessories(s, a, ctx): pass
    # gate
    gate_base = hexc("#FFFFFF")
    def gate_pattern(s, X, Y, col, aa): pass
    def gate_print(s, lx, ly, sc): return np.full(lx.shape, 1e9)
    gate_print_col = hexc("#000000"); gate_print_rim = hexc("#000000")

# -------------------------------------------------------------- cow (same look as the approved cow)
class Cow(Animal):
    name = "Cow"; type_id = "cow"; seed = 101
    base = hexc("#F7F4EE"); ear_base = hexc("#F7F4EE"); ear_inner = hexc("#EDB3A6")
    swatches = {"horn": hexc("#CDB99B"), "white": hexc("#F7F4EE")}
    def pattern(s, P, col, ctx):
        head = np.array([*ctx["face"], s.body.HGT])
        sph = blobs(ctx["tris"], ctx["rng"], .75, .17, .28, keep=lambda c, r: np.linalg.norm(c - head) > r + .44)
        blend(col, blob_sd(P, sph), hexc("#221E1C"), ctx["aa"])
    def face(s, f):
        f.blush(.47, .32)
        f.put(f.e(.5, .29, .30, .19), hexc("#C78A7E")); f.put(f.e(.5, .295, .285, .175), hexc("#E0A497"))
        for sx in (-1, 1): f.put(f.e(.5+sx*.11, .33, .05, .04), hexc("#7E4A42"))
        f.smile(.5, .25, .07, .008, hexc("#7E4A42"))
        for sx in (-1, 1): f.shiny_eye(.5+sx*.21, .63, .095, .11)
    def accessories(s, a, ctx):
        fx, fy = ctx["face"]; H = s.body.HGT; k = s.body.S/.92
        for sx in (-1, 1):
            a.cone(T(fx+sx*.3*k, fy+.33*k, H-.01) @ Euler((math.radians(-40), math.radians(sx*30), 0)).to_matrix().to_4x4(), "horn",
                   r1=.075, depth=.22)
            on_top = not side_free(ctx, sx)
            c = (fx+sx*.5, fy+.16, H+s.body.BULGE_TOP+.015) if on_top else (fx+sx*.47*k, fy+.16, H-.12)
            a.blob(T(*c) @ Rz(sx*20) @ Ry(-sx*12), (.136, .072, .04), "ear", "white")
    gate_base = hexc("#F7F4EE"); gate_print_col = hexc("#C27A6B"); gate_print_rim = hexc("#7E4A42")
    def gate_pattern(s, X, Y, col, aa):
        sd = np.full(X.shape, 1e9); rng = random.Random(5)
        for cx, cy, r in [(-.36, .30, .15), (.37, -.05, .16), (-.33, -.28, .13), (.33, .36, .10), (.40, -.40, .09)]:
            sd = np.minimum(sd, np.hypot(X-cx, Y-cy) - r)
            for _ in range(3):
                an = rng.uniform(0, 2*math.pi); d = rng.uniform(.04, .08)
                sd = np.minimum(sd, np.hypot(X-cx-d*math.cos(an), Y-cy-d*math.sin(an)) - r*rng.uniform(.5, .8))
        blend(col, sd, hexc("#221E1C"), aa)
    def gate_print(s, lx, ly, sc): return cloven(lx, ly, sc)

# -------------------------------------------------------------- pig
class Pig(Animal):
    name = "Pig"; type_id = "pig"; seed = 202
    body = Body(R=0.2, BULGE=0.03, BULGE_TOP=0.04)
    base = hexc("#F7A1B5"); ear_base = hexc("#F7A1B5"); ear_inner = hexc("#E2738F")
    swatches = {"ear": hexc("#F093A8")}
    DARK = hexc("#C2496A"); SNOUT = hexc("#FBC3CF"); MUD = hexc("#8A5A36")
    def pattern(s, P, col, ctx):
        # a soft lighter belly band low on the sides, plus a few mud splashes
        t = np.clip((0.42 - P[:, 2]) / 0.3, 0, 1)
        col[:] = col*(1 - .25*t[:, None]) + hexc("#FCD6DF")*(.25*t[:, None])
        sph = blobs(ctx["tris"], ctx["rng"], .35, .05, .09, zmax=.38, sat=(1, 3))
        blend(col, blob_sd(P, sph), s.MUD, ctx["aa"], .85)
    def face(s, f):
        f.blush(.46, .34, hexc("#EE7C8E"), .5)
        f.put(f.e(.5, .31, .245, .165), s.DARK); f.put(f.e(.5, .315, .228, .148), s.SNOUT)
        for sx in (-1, 1): f.put(f.e(.5+sx*.08, .315, .042, .066), hexc("#A84C5E"))
        f.smile(.5, .175, .05, .009, s.DARK)
        for sx in (-1, 1):  # sleepy eyes: heavy upper lid with a lash line
            cu, cv = .5+sx*.2, .64
            eye = f.e(cu, cv, .095, .085)
            f.put(eye, WHITE)
            f.put(f.e(cu + sx*.01, cv - .025, .05, .05), EYE_DARK)
            f.put(f.e(cu + sx*.01 - .015, cv - .01, .014, .014), WHITE)
            lid = np.maximum(eye, (cv - .005)*f.S - f.Z)
            f.put(lid, hexc("#F093A8"))
            f.put(f.arc(cu, cv - .005 + .11, .115, math.pi*1.27, math.pi*1.73, .011), hexc("#5A2A33"))
    def tail(s, x, y, col, ctx, aa):
        tc = ctx["tail"]; c = tc["c"] + tc["dir"]*.12
        pts = [(c[0] + (.015 + .02*th)*math.cos(th), c[1] + (.015 + .02*th)*math.sin(th)) for th in np.linspace(0, 3.2*math.pi, 60)]
        blend(col, poly_sd(x, y, pts, .016), s.DARK, aa)
    def accessories(s, a, ctx):
        fx, fy = ctx["face"]; H = s.body.HGT
        for sx in (-1, 1):  # triangular ears flopping forward
            a.cone(T(fx+sx*.3, fy+.28, H-.01) @ Rz(sx*-12) @ Rx(-50) @ Ry(sx*18), "ear", segs=4, r1=.12, r2=.01, depth=.2,
                   scale=(1, .45, 1))
    gate_base = hexc("#F7A1B5"); gate_print_col = hexc("#C2496A"); gate_print_rim = hexc("#8E3050")
    def gate_pattern(s, X, Y, col, aa):
        for cx, cy, r in [(-.34, .28, .07), (.36, -.12, .09), (-.3, -.33, .05), (.32, .34, .05)]:
            blend(col, np.hypot(X-cx, Y-cy) - r, s.MUD, aa, .85)
    def gate_print(s, lx, ly, sc): return cloven(lx, ly, sc*.85)

# -------------------------------------------------------------- chicken
class Chicken(Animal):
    name = "Chicken"; type_id = "chicken"; seed = 303
    body = Body(R=0.27, HGT=0.82, GAP=0.16, BULGE=0.03, BULGE_TOP=0.045)
    base = hexc("#FFD93B")
    swatches = {"beak": hexc("#F07A1A"), "comb": hexc("#E8443A")}
    WING = hexc("#F7BE1E"); FOOT = hexc("#F07A1A")
    def pattern(s, P, col, ctx):
        fx, fy = ctx["face"]; S = s.body.S; H = s.body.HGT
        for sx in (-1, 1):  # wings on the head cell's free sides
            if not side_free(ctx, sx, 0): continue
            wx = fx + sx*S/2
            m = np.abs(P[:, 0] - wx) < .14
            sd = np.where(m, ell(P[:, 1], P[:, 2], fy - .05, H*.42, .22, .15), 1e9)
            blend(col, sd, s.WING, ctx["aa"])
            for k in (-1, 0, 1):  # feather tips
                sd2 = np.where(m, ell(P[:, 1], P[:, 2], fy - .05 + k*.1 - .02, H*.3, .06, .06), 1e9)
                blend(col, sd2, hexc("#E9A40F"), ctx["aa"], .6)
    def face(s, f):
        f.blush(.46, .3, hexc("#F59A7A"), .55)
        for sx in (-1, 1): f.shiny_eye(.5+sx*.17, .6, .055, .066, hl=1.2)
    def tail(s, x, y, col, ctx, aa):
        tc = ctx["tail"]; d, p = tc["dir"], tc["perp"]; c = tc["c"] + d*.05
        for sx in (-1, 1):  # little bird feet pointing away from the head
            h = c + p*sx*.1
            for ang in (-35, 0, 35):
                a = math.radians(ang); v = d*math.cos(a) + p*math.sin(a)
                blend(col, poly_sd(x, y, [tuple(h), tuple(h + v*.1)], .013), s.FOOT, aa)
            blend(col, poly_sd(x, y, [tuple(h), tuple(h - d*.05)], .013), s.FOOT, aa)
    def accessories(s, a, ctx):
        fx, fy = ctx["face"]; H = s.body.HGT + s.body.BULGE_TOP
        a.cone(T(fx, fy - .02, H - .02) @ Rx(35), "beak", segs=4, r1=.07, r2=.008, depth=.12, scale=(1, .8, 1))
        for dx, r, h in ((-.065, .05, -.02), (0, .065, .0), (.065, .05, -.02)):  # comb
            a.blob(T(fx + dx, fy + .2, H + h) @ Rx(-20), (r*.8, r, r*1.15), "comb", seg=6, rings=4)
    gate_base = hexc("#FFE170"); gate_print_col = hexc("#E06A10"); gate_print_rim = hexc("#E06A10")
    def gate_pattern(s, X, Y, col, aa):
        rng = random.Random(7)
        for _ in range(26):  # straw
            cx, cy = rng.uniform(-.42, .42), rng.uniform(-.42, .42)
            if abs(cx) < .2: cx += math.copysign(.2, cx)
            an = rng.uniform(0, math.pi); L = rng.uniform(.06, .14)
            p0 = (cx - L*math.cos(an), cy - L*math.sin(an)); p1 = (cx + L*math.cos(an), cy + L*math.sin(an))
            blend(col, poly_sd(X, Y, [p0, p1], .006), hexc("#D9B65A"), aa)
    def gate_print(s, lx, ly, sc):
        best = np.full(lx.shape, 1e9)
        for ang in (-35, 0, 35):
            a = math.radians(ang)
            best = np.minimum(best, poly_sd(lx, ly, [(0, -.02*sc), (math.sin(a)*.1*sc, math.cos(a)*.1*sc - .02*sc)], .014*sc))
        return np.minimum(best, poly_sd(lx, ly, [(0, -.02*sc), (0, -.07*sc)], .014*sc))

# -------------------------------------------------------------- sheep
class Sheep(Animal):
    name = "Sheep"; type_id = "sheep"; seed = 404
    body = Body(R=0.2, BULGE=0.03, BULGE_TOP=0.04, SEGS=4, SCALLOP=0.03)
    base = hexc("#FBF8F1"); ear_base = hexc("#3F3A46"); ear_inner = hexc("#B98E93")
    swatches = {"grey": hexc("#3F3A46")}
    SHADE = hexc("#DCD3C3"); HEAD = hexc("#3F3A46")
    def pattern(s, P, col, ctx):
        wool(P, col, ctx["tris"], ctx["rng"], s.base, s.SHADE)
    def face(s, f):
        f.put(f.e(.5, .42, .31, .27), s.HEAD)
        for du in (-.2, -.1, 0, .1, .2):  # wool fringe over the forehead
            f.put(f.e(.5+du, .67 - abs(du)*.25, .085, .075), s.SHADE)
            f.put(f.e(.5+du, .675 - abs(du)*.25, .072, .062), s.base)
        for sx in (-1, 1):  # big eyes looking to the side
            cu = .5 + sx*.115
            f.put(f.e(cu, .47, .085, .09), WHITE)
            f.put(f.e(cu + .035, .455, .045, .052), EYE_DARK)
            f.put(f.e(cu + .025, .475, .014, .014), WHITE)
        f.put(f.e(.5, .3, .05, .03), hexc("#26222B"))
        f.smile(.5, .27, .045, .008, hexc("#26222B"))
        f.blush(.33, .23, hexc("#C98A93"), .5)
    def accessories(s, a, ctx):
        fx, fy = ctx["face"]; H = s.body.HGT + s.body.BULGE_TOP
        for sx in (-1, 1):  # grey ears sticking out from the head, lying on top
            a.blob(T(fx + sx*.36, fy - .02, H + .005) @ Rz(-sx*18) @ Ry(sx*10), (.12, .06, .035), "ear", "grey")
    gate_base = hexc("#FBF8F1"); gate_print_col = hexc("#6E6876"); gate_print_rim = hexc("#4A4550")
    def gate_pattern(s, X, Y, col, aa):
        rng = random.Random(9); q = np.full(X.shape, 9.0)
        for _ in range(60):
            cx, cy = rng.uniform(-.5, .5), rng.uniform(-.5, .5)
            q = np.minimum(q, np.hypot(X-cx, Y-cy) / rng.uniform(.09, .12))
        t = np.clip((q - .45)/.55, 0, 1)**1.6
        col[:] = col*(1-t[..., None]) + s.SHADE*t[..., None]
    def gate_print(s, lx, ly, sc): return cloven(lx, ly, sc*.85)

# -------------------------------------------------------------- horse
class Horse(Animal):
    name = "Horse"; type_id = "horse"; seed = 505
    body = Body(R=0.18, BULGE=0.028, BULGE_TOP=0.04)
    base = hexc("#6B3E26"); ear_base = hexc("#6B3E26"); ear_inner = hexc("#B98A66")
    swatches = {"body": hexc("#6B3E26"), "mane": hexc("#2B1A12")}
    MANE = hexc("#2B1A12"); MUZZLE = hexc("#EBC9A0"); HOOF = hexc("#D9B08A"); NOSE = hexc("#6A3E24")
    def pattern(s, P, col, ctx):
        fx, fy = ctx["face"]; S = s.body.S
        # mane along the left edge of the head's column, over the top and down the side
        edge = fx - S/2 + .19 + .03*np.sin(P[:, 1]*14)
        m = (np.abs(P[:, 0] - fx) < .5) & (P[:, 2] > s.body.HGT*.45)
        blend(col, np.where(m, P[:, 0] - edge, 1e9), s.MANE, ctx["aa"])
    def face(s, f):
        f.put(f.e(.5, .29, .28, .21), s.MUZZLE)
        for sx in (-1, 1): f.put(f.e(.5+sx*.095, .32, .035, .045), s.NOSE)
        f.smile(.5, .25, .075, .009, s.NOSE)
        for sx in (-1, 1):
            cu = .5 + sx*.18
            f.put(f.e(cu, .66, .095, .105), WHITE)
            f.put(f.e(cu + .02, .645, .052, .062), EYE_DARK)
            f.put(f.e(cu + .005, .67, .016, .016), WHITE)
        for du, dv in ((-.07, .93), (.0, .97), (.06, .92)):  # forelock
            f.put(f.e(.5+du, dv, .07, .13), s.MANE)
        f.blush(.45, .35, hexc("#E0907A"), .5)
    def tail(s, x, y, col, ctx, aa):
        tc = ctx["tail"]; d, p = tc["dir"], tc["perp"]; c = tc["c"] + d*.2
        for sx in (-1, 1):  # hooves at the far end
            h = c + p*sx*.13
            lx = (x-h[0])*p[0] + (y-h[1])*p[1]; ly = (x-h[0])*d[0] + (y-h[1])*d[1]
            u = np.abs(np.hypot(lx, ly*1.2) - .075) - .018      # U-shaped hoof, open towards the head
            blend(col, np.where(ly > -.025, u, 1e9), s.HOOF, aa)
    def accessories(s, a, ctx):
        fx, fy = ctx["face"]; H = s.body.HGT
        for sx in (-1, 1):
            a.cone(T(fx+sx*.27, fy+.3, H-.01) @ Ry(sx*20) @ Rx(-12), "body", segs=4, r1=.08, r2=.01, depth=.2, scale=(1, .55, 1))
    gate_base = hexc("#8A5A3E"); gate_print_col = hexc("#D8D8DC"); gate_print_rim = hexc("#9A9AA0")
    def gate_pattern(s, X, Y, col, aa):
        for k in range(-4, 5):  # wood-grain-ish lighter streaks
            blend(col, np.abs(Y - k*.11 - .02*np.sin(X*9 + k)) - .012, hexc("#A0704F"), aa, .7)
    def gate_print(s, lx, ly, sc):
        r, w = .07*sc, .024*sc
        ring_sd = np.abs(np.hypot(lx, ly) - r) - w/2
        return np.where(np.abs(np.arctan2(lx, ly)) < 2.3, ring_sd, 1e9)

# -------------------------------------------------------------- cat
class Cat(Animal):
    name = "Cat"; type_id = "cat"; seed = 606
    body = Body(R=0.18, BULGE=0.028, BULGE_TOP=0.04)
    base = hexc("#F5892E"); ear_base = hexc("#F5892E"); ear_inner = hexc("#F7B7B0")
    swatches = {"body": hexc("#F5892E")}
    STRIPE = hexc("#C9561A"); CREAM = hexc("#FCE3C2"); NOSE = hexc("#F08A9C"); DARK = hexc("#3A2418")
    def pattern(s, P, col, ctx):
        fx, fy = ctx["face"]
        t = (fy - P[:, 1]) + .04*np.sin(P[:, 0]*9)
        f = np.mod(t - .35, .3)
        sd = np.abs(f - .07) - .06
        far = np.hypot(P[:, 0] - fx, P[:, 1] - fy) > .42
        blend(col, np.where(far, sd, 1e9), s.STRIPE, ctx["aa"])
    def face(s, f):
        for du in (-.08, 0, .08): f.put(f.line([(.5+du, .97), (.5+du*.8, .86)], .022), s.STRIPE)
        f.put(f.e(.5, .36, .2, .15), s.CREAM)
        f.blush(.44, .34, hexc("#F07C7C"), .45)
        for sx in (-1, 1):
            cu = .5 + sx*.18
            f.put(f.e(cu, .62, .1, .11), WHITE)
            f.put(f.e(cu, .61, .066, .082), EYE_DARK)
            f.put(f.e(cu - .025, .645, .022, .022), WHITE)
            f.put(f.e(cu + .02, .585, .01, .01), WHITE)
        f.put(f.e(.5, .44, .045, .03), s.NOSE)
        for sx in (-1, 1):
            f.put(f.arc(.5+sx*.035, .405, .035, math.pi*1.05, math.pi*1.95, .008), s.DARK)
            for k, dv in enumerate((.06, 0, -.06)):  # whiskers
                f.put(f.line([(.5+sx*.2, .4 + dv*.3), (.5+sx*.38, .4 + dv)], .005), s.DARK)
    def tail(s, x, y, col, ctx, aa):
        tc = ctx["tail"]; d, p = tc["dir"], tc["perp"]; c = tc["c"]
        pts = [tuple(c + d*(.4 - .55*u) + p*(.14*math.sin(u*math.pi*1.3))) for u in np.linspace(0, 1, 24)]
        blend(col, poly_sd(x, y, pts, .06), s.STRIPE, aa)
        blend(col, poly_sd(x, y, pts, .045), hexc("#E07422"), aa)
        for u in (.2, .45, .7, .95):  # tail rings
            q = np.array(pts[int(u*23)])
            blend(col, np.hypot(x - q[0], y - q[1]) - .035, s.STRIPE, aa)
    def accessories(s, a, ctx):
        fx, fy = ctx["face"]; H = s.body.HGT
        for sx in (-1, 1):
            a.cone(T(fx+sx*.28, fy+.27, H-.01) @ Ry(sx*16) @ Rx(-10), "body", segs=4, r1=.11, r2=.01, depth=.2,
                   scale=(1, .5, 1))
    gate_base = hexc("#F7A050"); gate_print_col = hexc("#B9561A"); gate_print_rim = hexc("#B9561A")
    def gate_pattern(s, X, Y, col, aa):
        for k in range(-3, 4):
            sd = np.abs(Y - k*.15 - .03*np.sin(X*9)) - .025
            blend(col, np.where(np.abs(X) > .2, sd, 1e9), s.STRIPE, aa, .8)
    def gate_print(s, lx, ly, sc): return paw(lx, ly, sc)

# -------------------------------------------------------------- dog
class Dog(Animal):
    name = "Dog"; type_id = "dog"; seed = 707
    body = Body(R=0.18, BULGE=0.028, BULGE_TOP=0.04)
    base = hexc("#7590BC"); ear_base = hexc("#4C6290"); ear_inner = hexc("#6278A4")
    swatches = {"ear": hexc("#4C6290")}
    PATCH = hexc("#4C6290"); MUZZLE = hexc("#E4E9EF"); NOSE = hexc("#22252E"); TONGUE = hexc("#F0788A")
    COLLAR = hexc("#D8433A"); TAG = hexc("#F2C14E")
    def pattern(s, P, col, ctx):
        fx, fy = ctx["face"]; head = np.array([fx, fy, s.body.HGT])
        sph = blobs(ctx["tris"], ctx["rng"], .45, .15, .24, keep=lambda c, r: np.linalg.norm(c - head) > r + .5)
        blend(col, blob_sd(P, sph), s.PATCH, ctx["aa"])
        for nx, ny in ((0, -1), (1, 0), (-1, 0)):  # collar where the body continues behind / beside the head
            if (math.floor(fx + nx*.75 + .5), math.floor(fy + ny*.75 + .5)) not in ctx["cells"]: continue
            if nx == 0:
                bd = fy - .5; sd = np.abs(P[:, 1] - bd) - .045
                along = np.abs(P[:, 0] - fx) < .5
            else:
                bd = fx + nx*.5; sd = np.abs(P[:, 0] - bd) - .045
                along = np.abs(P[:, 1] - fy) < .5
            blend(col, np.where(along, sd, 1e9), s.COLLAR, ctx["aa"])
            top = np.array([fx + nx*.5 if nx else fx, fy - .5 if nx == 0 else fy, s.body.HGT + s.body.BULGE_TOP])
            blend(col, np.linalg.norm(P - top, axis=1) - .05, s.TAG, ctx["aa"])
            break
    def face(s, f):
        f.put(f.e(.5 - .19, .64, .14, .13), s.PATCH)   # eye patch
        f.put(f.e(.5, .37, .23, .17), s.MUZZLE)
        for sx in (-1, 1): f.shiny_eye(.5+sx*.19, .63, .068, .078)
        f.put(f.e(.5, .26, .055, .07), s.TONGUE)
        f.put(f.line([(.5, .29), (.5, .22)], .006), hexc("#C9506A"))
        for sx in (-1, 1): f.put(f.arc(.5+sx*.05, .335, .05, math.pi*1.05, math.pi*1.95, .008), s.NOSE)
        f.put(f.line([(.5, .42), (.5, .335)], .007), s.NOSE)
        f.put(f.e(.5, .44, .085, .06), s.NOSE)
        f.put(f.e(.48, .46, .025, .015), hexc("#5E6878"))
    def accessories(s, a, ctx):
        fx, fy = ctx["face"]; H = s.body.HGT
        for sx in (-1, 1):  # floppy ears hanging over the sides (or lying on top next to a neighbour cell)
            if side_free(ctx, sx, .1):
                a.blob(T(fx + sx*(s.body.S/2 + .07), fy + .12, H - .2) @ Ry(sx*72) @ Rx(10), (.22, .12, .04), "ear", "ear")
            else:
                a.blob(T(fx + sx*.4, fy + .12, H + s.body.BULGE_TOP + .01) @ Rz(sx*-25) @ Ry(sx*-15), (.15, .09, .035), "ear", "ear")
    gate_base = hexc("#7590BC"); gate_print_col = hexc("#34446A"); gate_print_rim = hexc("#34446A")
    def gate_pattern(s, X, Y, col, aa):
        for cx, cy, r in [(-.35, .3, .1), (.36, -.15, .12), (-.33, -.32, .08)]:
            blend(col, np.hypot(X-cx, Y-cy) - r, s.PATCH, aa)
    def gate_print(s, lx, ly, sc): return paw(lx, ly, sc)

# -------------------------------------------------------------- hay bale: the neutral blocker (no type, never leaves)
class HayBale(Animal):
    name = "HayBale"; type_id = ""; seed = 808; has_gate = False
    body = Body(R=0.11, BULGE=0.015, BULGE_TOP=0.02)
    base = hexc("#D9B867")
    def pattern(s, P, col, ctx):
        streak = np.sin(P[:, 0]*70 + np.sin(P[:, 1]*9)*3 + P[:, 2]*40) * np.sin(P[:, 1]*23 + P[:, 0]*5)
        col[:] = col * (1 + .08*streak[:, None])
        rng = ctx["rng"]
        for _ in range(int(len(ctx["cells"])*40)):  # loose straws
            c = np.array([rng.uniform(-1.5, 2.5), rng.uniform(-3.5, 1.5)]); a = rng.uniform(0, math.pi); L = rng.uniform(.04, .1)
            pts = [tuple(c - L*np.array([math.cos(a), math.sin(a)])), tuple(c + L*np.array([math.cos(a), math.sin(a)]))]
            blend(col, poly_sd(P[:, 0], P[:, 1], pts, .006), hexc("#B8964A"), ctx["aa"], .7)
        twine = np.abs(np.mod(P[:, 1], .5) - .25) - .022   # two twine bands across every cell
        blend(col, twine, hexc("#8C4A22"), ctx["aa"])

# -------------------------------------------------------------- footprints (local: ly points to the exit)
def cloven(lx, ly, s):
    best = np.full(lx.shape, 1e9)
    for side in (-1, 1):
        ex = (np.sqrt(((lx - side*.042*s)/(.04*s))**2 + (ly/(.075*s))**2) - 1) * .04*s
        best = np.minimum(best, np.maximum(ex, -(side*lx) + .008*s))
    return best

def paw(lx, ly, s):
    best = ell(lx, ly, 0, -.02*s, .055*s, .045*s)
    for tx, ty in ((-.06, .045), (-.022, .075), (.022, .075), (.06, .045)):
        best = np.minimum(best, ell(lx, ly, tx*s, ty*s, .022*s, .026*s))
    return best

ANIMALS = {c.name: c for c in (Cow(), Pig(), Chicken(), Sheep(), Horse(), Cat(), Dog(), HayBale())}

# ======================================================================= block build + bake
def material(name, img):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True; nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"); bsdf.inputs["Roughness"].default_value = 0.65
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = img; tex.location = (-400, 300); nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat

def new_image(name, arr, path):
    old = bpy.data.images.get(name)
    if old: bpy.data.images.remove(old)
    im = bpy.data.images.new(name, arr.shape[1], arr.shape[0], alpha=False)
    im.pixels.foreach_set(arr.ravel()); im.filepath_raw = path; im.file_format = 'PNG'; im.save(); im.pack()
    return im

def collection(name):
    col = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if col.name not in bpy.context.scene.collection.children: bpy.context.scene.collection.children.link(col)
    for o in list(col.objects): bpy.data.objects.remove(o)
    return col

def raster(tris):
    PIX = []; POS = []
    for uv, P in tris:
        mn = np.floor(uv.min(0)).astype(int); mx = np.ceil(uv.max(0)).astype(int)
        xs, ys = np.meshgrid(np.arange(mn[0], mx[0]+1), np.arange(mn[1], mx[1]+1))
        cx, cy = xs + .5, ys + .5
        (x1, y1), (x2, y2), (x3, y3) = uv
        den = (y2-y3)*(x1-x3) + (x3-x2)*(y1-y3)
        if abs(den) < 1e-9: continue
        l1 = ((y2-y3)*(cx-x3) + (x3-x2)*(cy-y3)) / den
        l2 = ((y3-y1)*(cx-x3) + (x1-x3)*(cy-y3)) / den
        l3 = 1 - l1 - l2
        m = (l1 >= -1e-4) & (l2 >= -1e-4) & (l3 >= -1e-4)
        if not m.any(): continue
        PIX.append(np.stack([ys[m], xs[m]], 1)); POS.append(np.stack([l1[m], l2[m], l3[m]], 1) @ P)
    return np.concatenate(PIX), np.concatenate(POS)

def tail_info(cells, fx, fy):
    far = max(cells, key=lambda c: ((c[0]-fx)**2 + (c[1]-fy)**2, -c[1]))
    d = np.array([far[0]-fx, far[1]-fy], float); n = np.linalg.norm(d)
    if n < .6: return None
    d /= n; return dict(c=np.array(far, float), dir=d, perp=np.array([-d[1], d[0]]))

def build_blocks(A, row_y):
    B = A.body
    G = {}
    for shape, (cells, face) in SHAPES.items():
        bc = [(x, -y) for x, y in cells]
        G[shape] = build_shape(bc, B); G[shape]["cells"] = set(bc); G[shape]["face"] = (face[0], -face[1])
    rects = []
    for shape, g in G.items():
        rects.append(((shape, "side"), int(math.ceil(g["ucol"][-1]*D_SIDE)), int(math.ceil(g["vrow"][-1]*D_SIDE))))
        ext = g["cap_max"] - g["cap_min"]
        rects.append(((shape, "cap"), int(math.ceil(ext[0]*D_CAP)), int(math.ceil(ext[1]*D_CAP))))
    rects.append(("ear", 256, 256))
    for k in A.swatches: rects.append((("sw", k), 32, 32))
    rects.sort(key=lambda r: -r[2])
    pos = {}; x = y = shelf = 0
    for key, w, h in rects:
        if x + w + 2*PAD > TEX: x = 0; y += shelf; shelf = 0
        pos[key] = (x + PAD, y + PAD, w, h); x += w + 2*PAD; shelf = max(shelf, h + 2*PAD)
    assert y + shelf <= TEX, ("atlas overflow", A.name, y + shelf)

    def uvpx(key, fu, fv): x0, y0, w, h = pos[key]; return (x0 + fu*w, y0 + fv*h)
    sw = lambda k: Vector(uvpx(("sw", k), .5, .5)) / TEX
    rect_uv = lambda key, fu, fv: Vector(uvpx(key, fu, fv)) / TEX

    mat = material(f"{A.name}_Mat", None)
    col = collection(f"{A.name}Blocks")
    tris_by = {}
    for i, (shape, g) in enumerate(G.items()):
        name = f"{A.name}_{shape}"
        bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
        rings = g["rings"]; N = len(rings[0])
        RV = [[bm.verts.new((p[0], p[1], B.PROFILE[k][1])) for p in rings[k]] for k in range(len(rings))]
        x0, y0, w, h = pos[(shape, "side")]
        for k in range(len(rings)-1):
            for j in range(N):
                j2 = (j+1) % N
                vs = [RV[k][j], RV[k][j2], RV[k+1][j2], RV[k+1][j]]
                uvs = [(g["ucol"][j], g["vrow"][k]), (g["ucol"][j+1], g["vrow"][k]),
                       (g["ucol"][j+1], g["vrow"][k+1]), (g["ucol"][j], g["vrow"][k+1])]
                f = bm.faces.new(vs)
                for l, (u, v) in zip(f.loops, uvs): l[uvl].uv = ((x0 + u*D_SIDE)/TEX, (y0 + v*D_SIDE)/TEX)
        CV = [bm.verts.new((p[0], p[1], z)) for p, z in zip(g["cap_v"], g["cap_z"])]
        x0, y0, w, h = pos[(shape, "cap")]
        for fi in g["cap_f"]:
            a, b, c = [g["cap_v"][q] for q in fi[:3]]
            if (b-a)[0]*(c-a)[1] - (b-a)[1]*(c-a)[0] < 0: fi = list(reversed(fi))
            f = bm.faces.new([CV[q] for q in fi])
            for l, q in zip(f.loops, fi):
                p = g["cap_v"][q]
                l[uvl].uv = ((x0 + (p[0]-g["cap_min"][0])*D_CAP)/TEX, (y0 + (p[1]-g["cap_min"][1])*D_CAP)/TEX)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges)
        tris_by[shape] = [(np.array([[l[uvl].uv.x*TEX, l[uvl].uv.y*TEX] for l in lt]), np.array([list(l.vert.co) for l in lt]))
                          for lt in bm.calc_loop_triangles()]
        ctx = dict(face=g["face"], cells=g["cells"])
        A.accessories(Acc(bm, uvl, sw, rect_uv), ctx)
        me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.materials.append(mat)
        ob = bpy.data.objects.new(name, me); col.objects.link(ob)
        xs_ = [c[0] for c in g["cells"]]; ys_ = [c[1] for c in g["cells"]]
        ob.location = ((i % 6)*5.0 - (min(xs_)+max(xs_))/2, row_y - (i // 6)*5.0 - (min(ys_)+max(ys_))/2, 0)
        print(name, "tris", sum(len(p.vertices)-2 for p in me.polygons))

    # bake
    img = np.empty((TEX, TEX, 4), np.float32); img[..., :3] = A.base; img[..., 3] = 1
    written = np.zeros((TEX, TEX), bool)
    for si, (shape, tris) in enumerate(tris_by.items()):
        g = G[shape]; fx, fy = g["face"]
        pix, P = raster(tris)
        colr = np.tile(A.base, (len(P), 1))
        ctx = dict(face=g["face"], cells=g["cells"], tris=tris, rng=random.Random(A.seed + si), aa=1.5/D_SIDE,
                   tail=tail_info(g["cells"], fx, fy))
        A.pattern(P, colr, ctx)
        top = P[:, 2] > B.HGT - B.R - .04
        if top.any():
            sub = colr[top]
            A.face(Face(sub, P[top, 0] - fx, B.S/2 + (P[top, 1] - fy), B.S, 1.5/D_CAP))
            if ctx["tail"] is not None: A.tail(P[top, 0], P[top, 1], sub, ctx, 1.5/D_CAP)
            colr[top] = sub
        img[pix[:, 0], pix[:, 1], :3] = colr; written[pix[:, 0], pix[:, 1]] = True
    for key, (x0, y0, w, h) in pos.items():
        sl = (slice(y0-PAD, y0+h+PAD), slice(x0-PAD, x0+w+PAD))
        if isinstance(key, tuple) and key[0] == "sw":
            img[sl][..., :3] = A.swatches[key[1]]; continue
        if key == "ear":
            ys, xs = np.mgrid[0:h+2*PAD, 0:w+2*PAD]; U = (xs - PAD + .5)/w; V = (ys - PAD + .5)/h
            reg = np.tile(A.ear_base if A.ear_base is not None else A.base, ((h+2*PAD)*(w+2*PAD), 1))
            blend(reg, ((np.sqrt(((U-.5)/.36)**2 + ((V-.5)/.33)**2) - 1)*.33).ravel(), A.ear_inner, 1.5/w)
            img[sl][..., :3] = reg.reshape(h+2*PAD, w+2*PAD, 3); continue
        reg = img[sl]; m = written[sl].copy()
        for _ in range(PAD + 2):
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                sm = np.roll(m, (dy, dx), (0, 1)); src = np.roll(reg, (dy, dx), (0, 1))
                fill = sm & ~m; reg[fill] = src[fill]; m |= fill
    out = os.path.join(GAME, "Blender", A.name, f"{A.name}_Albedo.png"); os.makedirs(os.path.dirname(out), exist_ok=True)
    im = new_image(f"{A.name}_Albedo", img, out); del img
    material(f"{A.name}_Mat", im)
    return col

# ======================================================================= gate
def build_gate(A, row_y):
    SW = {"rim": (820, 20), "wood": (820, 80), "bell": (820, 140), "strap": (820, 200), "dark": (820, 260)}
    SWC = {"rim": WOOD, "wood": WOOD, "bell": BELL, "strap": STRAP, "dark": EYE_DARK}
    img = np.empty((GATE_TEX, GATE_TEX, 4), np.float32); img[..., :3] = A.gate_base; img[..., 3] = 1
    for k, (x, y) in SW.items(): img[y-12:y+52, x-12:x+52, :3] = SWC[k]
    ys, xs = np.mgrid[0:TOP_PX, 0:TOP_PX]
    X = ((xs + .5)/TOP_PX - .5)*TILE; Y = ((ys + .5)/TOP_PX - .5)*TILE
    reg = np.tile(A.gate_base, (TOP_PX*TOP_PX, 1)); Xf, Yf = X.ravel(), Y.ravel()
    aa = 1.5/(TOP_PX/TILE)
    A.gate_pattern(Xf, Yf, reg, aa)
    for px, py, sc, r in [(-.09, -.28, 1.15, .12), (.09, -.02, 1.25, -.1), (-.08, .25, 1.35, .08)]:
        c, sn = math.cos(r), math.sin(r)
        lx = (Xf-px)*c + (Yf-py)*sn; ly = -(Xf-px)*sn + (Yf-py)*c
        sd = A.gate_print(lx, ly, sc)
        blend(reg, sd - .008, A.gate_print_rim, aa); blend(reg, sd, A.gate_print_col, aa)
    edge = TILE/2 - np.maximum(np.abs(Xf), np.abs(Yf))
    blend(reg, edge - .045, WOOD_D, aa*2); blend(reg, edge - .035, WOOD, aa)
    img[:TOP_PX, :TOP_PX, :3] = reg.reshape(TOP_PX, TOP_PX, 3)
    out = os.path.join(GAME, "Blender", A.name, f"{A.name}Gate_Albedo.png"); os.makedirs(os.path.dirname(out), exist_ok=True)
    im = new_image(f"{A.name}Gate_Albedo", img, out)
    mat = material(f"{A.name}Gate_Mat", im)
    col = collection(f"{A.name}Gate")

    def sw_uv(k): x, y = SW[k]; return Vector(((x + 20)/GATE_TEX, (y + 20)/GATE_TEX))
    def paint(bm, uvl, verts, key):
        for f in {f for v in verts for f in v.link_faces}:
            for l in f.loops: l[uvl].uv = sw_uv(key)
    def finish(name, bm, smooth=True):
        me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = smooth
        me.materials.append(mat); ob = bpy.data.objects.new(name, me); col.objects.link(ob); return ob

    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_cube(bm, size=1.0, matrix=T(0, 0, TILE_H/2) @ Matrix.Diagonal((TILE, TILE, TILE_H, 1)))
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=.025, segments=2, profile=.5, affect='EDGES')
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -.9], context='FACES_ONLY')
    for f in bm.faces:
        for l in f.loops:
            co = l.vert.co
            l[uvl].uv = (((co.x/TILE + .5)*TOP_PX/GATE_TEX, (co.y/TILE + .5)*TOP_PX/GATE_TEX) if f.normal.z > .3 else sw_uv("rim"))
    tile = finish(f"{A.name}Gate_Tile", bm)

    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
    PY = 0; POST_H = .86   # on the cell's centre line, where the fence rails of neighbouring walls arrive
    for sx in (-1, 1):
        r = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=.05, radius2=.045, depth=POST_H, matrix=T(sx*.46, PY, POST_H/2))
        paint(bm, uvl, r["verts"], "wood")
        r = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=.055, radius2=.02, depth=.07, matrix=T(sx*.46, PY, POST_H + .035))
        paint(bm, uvl, r["verts"], "wood")
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=T(0, PY, .78) @ Matrix.Diagonal((1.0, .06, .09, 1))); paint(bm, uvl, r["verts"], "wood")
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=T(0, PY, .705) @ Matrix.Diagonal((.035, .035, .07, 1))); paint(bm, uvl, r["verts"], "strap")
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=.085, radius2=.05, depth=.13, matrix=T(0, PY, .605)); paint(bm, uvl, r["verts"], "bell")
    r = bmesh.ops.create_uvsphere(bm, u_segments=6, v_segments=4, radius=.028, matrix=T(0, PY, .535)); paint(bm, uvl, r["verts"], "dark")
    arch = finish(f"{A.name}Gate_Arch", bm, smooth=False)
    for o in (tile, arch): o.location = (-4.0, row_y, 0)
    return col

# ======================================================================= fence (walls)
FENCE_TEX = 512
def build_fence():
    """Wall cell pieces: a post at the cell centre and rail arms from it to the cell edge (straight) or corner (diagonal).
    Unity enables the arms that lead to neighbouring walls and gates (WallCell)."""
    RAIL = hexc("#B07A45"); POST = hexc("#8A5A30"); GRASS = hexc("#6FA845"); GRASS_D = hexc("#4E8A35")
    img = np.empty((FENCE_TEX, FENCE_TEX, 4), np.float32); img[..., 3] = 1
    ys, xs = np.mgrid[0:FENCE_TEX, 0:FENCE_TEX]; U = (xs + .5)/FENCE_TEX; V = (ys + .5)/FENCE_TEX
    grain = 1 + .1*np.sin(V*260 + np.sin(U*14)*2.5) + .05*np.sin(V*90 + U*7)
    img[..., :3] = np.where((U < .5)[..., None], RAIL, POST) * grain[..., None]
    g = (U > .75) & (V > .75); img[g, :3] = GRASS
    g = (U > .75) & (V > .5) & (V < .75); img[g, :3] = GRASS_D
    out = os.path.join(GAME, "Blender", "Fence", "Fence_Albedo.png"); os.makedirs(os.path.dirname(out), exist_ok=True)
    mat = material("Fence_Mat", new_image("Fence_Albedo", img, out))
    col = collection("Fence")

    def box_uv(bm, uvl, verts, region):
        # wood: u along the piece (grain runs lengthwise), v across; rails in the left half, posts in the right
        u0 = 0 if region == "rail" else .5
        for f in {f for v in verts for f in v.link_faces}:
            n = f.normal
            for l in f.loops:
                co = l.vert.co
                if region == "rail": a, b = co.y, (co.x if abs(n.x) < .5 else co.z)
                else: a, b = co.z, (co.x if abs(n.x) < .5 else co.y)
                l[uvl].uv = (u0 + .05 + (b + .1)*.5, .05 + a*.55)
    def swatch(bm, uvl, verts, uv):
        for f in {f for v in verts for f in v.link_faces}:
            for l in f.loops: l[uvl].uv = uv
    def finish(name, bm, x):
        me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = False
        me.materials.append(mat); ob = bpy.data.objects.new(name, me); col.objects.link(ob); ob.location = (x, 6, 0); return ob

    # post with a pointed cap and a grass tuft at its foot
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
    r = bmesh.ops.create_cube(bm, size=1, matrix=T(0, 0, .34) @ Matrix.Diagonal((.16, .16, .68, 1))); box_uv(bm, uvl, r["verts"], "post")
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=4, radius1=.113, radius2=.0, depth=.12, matrix=T(0, 0, .74) @ Rz(45))
    box_uv(bm, uvl, r["verts"], "post")
    rng = random.Random(3)
    for k in range(7):
        a = k/7*2*math.pi + rng.uniform(-.3, .3); d = rng.uniform(.07, .11)
        r = bmesh.ops.create_cone(bm, cap_ends=False, segments=3, radius1=.035, radius2=0, depth=rng.uniform(.1, .17),
                                  matrix=T(d*math.cos(a), d*math.sin(a), .05) @ Ry(rng.uniform(-20, 20)) @ Rx(rng.uniform(-20, 20)))
        swatch(bm, uvl, r["verts"], ((.88 if k % 2 else .86), (.88 if k % 2 else .6)))
    finish("Fence_Post", bm, 0)

    # rails: two planks from the centre to the edge (+Y), straight and diagonal length
    for name, length, x in (("Fence_Rail", .5, 1.5), ("Fence_RailDiag", .5*math.sqrt(2), 3)):
        bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
        for z in (.3, .55):
            r = bmesh.ops.create_cube(bm, size=1, matrix=T(0, length/2, z) @ Matrix.Diagonal((.06, length, .11, 1)))
            box_uv(bm, uvl, r["verts"], "rail")
        finish(name, bm, x)
    return col

def export_fence():
    import shutil
    d = os.path.join(GAME, "Assets", "Prefabs", "Wall", "Fence"); os.makedirs(d, exist_ok=True)
    for ob in bpy.data.collections["Fence"].objects: export_fbx(ob, os.path.join(d, ob.name + ".fbx"))
    shutil.copy(os.path.join(GAME, "Blender", "Fence", "Fence_Albedo.png"), d)

def rebuild_gates(do_export=True):
    """Gate cells only (e.g. after changing the arch), for every animal with a gate."""
    for A in ANIMALS.values():
        if not A.has_gate: continue
        build_gate(A, ROWS[A.name] - 20)
        if not do_export: continue
        gdir = os.path.join(GAME, "Assets", "Prefabs", "Gate", A.name)
        for ob in bpy.data.collections[f"{A.name}Gate"].objects: export_fbx(ob, os.path.join(gdir, ob.name + ".fbx"))

# ======================================================================= export
def export_fbx(ob, path):
    # The FBX axis conversion maps Blender +Y to Unity -Z; turning the mesh 180° for the export keeps
    # the face's up-direction (+Y here) on Unity +Z, the far edge of the board, and footprints unrotated.
    turn = Matrix.Rotation(math.pi, 4, 'Z')
    ob.data.transform(turn)
    try:
        _export_fbx(ob, path)
    finally:
        ob.data.transform(turn.inverted())

def _export_fbx(ob, path):
    loc = ob.location.copy(); ob.location = (0, 0, 0)
    bpy.ops.object.select_all(action='DESELECT')
    ob.hide_set(False); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, global_scale=1.0,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_space_transform=True,
        bake_space_transform=True, axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE',
        use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
    ob.location = loc

def export(A):
    import shutil
    bdir = os.path.join(GAME, "Assets", "Prefabs", "Block", A.name); gdir = os.path.join(GAME, "Assets", "Prefabs", "Gate", A.name)
    os.makedirs(bdir, exist_ok=True)
    if A.has_gate: os.makedirs(gdir, exist_ok=True)
    for ob in bpy.data.collections[f"{A.name}Blocks"].objects: export_fbx(ob, os.path.join(bdir, ob.name + ".fbx"))
    src = os.path.join(GAME, "Blender", A.name)
    shutil.copy(os.path.join(src, f"{A.name}_Albedo.png"), bdir)
    if not A.has_gate: return
    for ob in bpy.data.collections[f"{A.name}Gate"].objects: export_fbx(ob, os.path.join(gdir, ob.name + ".fbx"))
    shutil.copy(os.path.join(src, f"{A.name}Gate_Albedo.png"), gdir)

ROWS = {"Cow": 0, "Pig": -14, "Chicken": -28, "Sheep": -42, "Horse": -56, "Cat": -70, "Dog": -84, "HayBale": -98}

def build(name, do_export=False):
    A = ANIMALS[name]
    build_blocks(A, ROWS[name] - 20)
    if A.has_gate: build_gate(A, ROWS[name] - 20)
    if do_export: export(A)
    print("built", name)

def build_all(names=("Pig", "Chicken", "Sheep", "Horse", "Cat", "Dog"), do_export=False):
    for n in names: build(n, do_export)
